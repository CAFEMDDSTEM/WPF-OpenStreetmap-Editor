using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using WPF_OpenStreetmap_Editor.Controls;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor;

public partial class MainWindow : Window {
    private WindowState _lastNonMinimizedWindowState = WindowState.Normal;
    private double _centerLon;
    private double _centerLat;
    private bool _isPanning;
    private bool _isUpdatingLayerList;
    private Point _panStart;
    private double _panOffsetX;
    private double _panOffsetY;
    private CancellationTokenSource? _renderCts;
    private CancellationTokenSource? _renderDebounceCts;
    private CancellationTokenSource? _tileSourceCts;
    private MapLayer? _activeLayer;
    private MapLayer? _fallbackLayer;
    private MapLayer? _stagingLayer;
    private AppSettings _settings = AppSettingsService.Load();
    private MapImageLayer? _activeImageLayer;
    private TileSourcePreset _activeSource = TileSourcePreset.CreateDefaults()[0];
    private IReadOnlyList<TileLayerContext> _tileLayers = [];
    private TileService _tileService => _tileLayers[0].Service;
    private double _lastPanPrefetchOffsetX;
    private double _lastPanPrefetchOffsetY;
    private DateTime _lastPanPrefetchAt = DateTime.MinValue;
    private static readonly TileMemoryCache TileCache = new(MaxTileCache, MaxTileCacheBytes);
    private const int MaxTileCache = 768;
    private const long MaxTileCacheBytes = 192L * 1024 * 1024;
    private const int RenderTileBuffer = 0;
    private const int PrefetchTileBuffer = 1;
    private const int MaxPrefetchTiles = 24;
    private const int MaxPrefetchWorkers = 2;
    private const int MaxConcurrentTileLoads = 8;
    private const int WheelRenderDelayMilliseconds = 180;
    private const int ResizeRenderDelayMilliseconds = 120;
    private const int PanPrefetchIntervalMilliseconds = 140;
    private const double PanPrefetchDistance = GeoConverter.TileSize * 0.75;
    private static readonly SemaphoreSlim TileThrottle = new(MaxConcurrentTileLoads, MaxConcurrentTileLoads);

    public MainWindow() {
        InitializeComponent();
        AppSettingsService.EnsureDefaults(_settings);
        RefreshImageryMenu();
        RefreshLayerList(_settings.GetActiveLayer());

        WindowStartupService.ApplyStartupState(this);
        _lastNonMinimizedWindowState = WindowState;
        StateChanged += Window_StateChanged;
        Closing += (_, _) => WindowStartupService.Save(
            WindowStartupService.GetStateToSave(WindowState, _lastNonMinimizedWindowState));
        Closed += (_, _) => {
            _tileSourceCts?.Cancel();
            _renderDebounceCts?.Cancel();
            _renderCts?.Cancel();
            TileCache.Clear();
            DisposeTileLayers(_tileLayers);
            _tileLayers = [];
        };

        Loaded += (_, _) => RefreshRenderedLayerFromStack(_settings.GetActiveLayer());
    }

    private void Window_StateChanged(object? sender, EventArgs e) {
        if (WindowState == WindowState.Minimized) {
            return;
        }

        _lastNonMinimizedWindowState = WindowState;

        if (WindowState == WindowState.Maximized) {
            WindowStartupService.ClearNormalWindowLimits(this);
            return;
        }

        WindowStartupService.ApplyNormalWindowLimits(this);
    }

    public void LoadMapFromUrl(string url) {
        if (string.IsNullOrWhiteSpace(url)) return;

        var source = GetOrCreateCustomSource("自定义图源", url.Trim());
        AddImageLayer(source);
    }

    public void LoadLayer(string type, string url) {
        if (string.IsNullOrWhiteSpace(url)) return;

        var normalizedSource = NormalizeLayerSource(type, url);
        var source = GetOrCreateCustomSource("自定义图层", normalizedSource);
        AddImageLayer(source);
    }

    public void LoadSource(TileSourcePreset source) {
        AddImageLayer(source);
    }

    private void AddImageLayer(TileSourcePreset source) {
        var resolvedSource = ResolveSettingsSource(source);
        var layer = _settings.ImageLayers.FirstOrDefault(existing =>
            existing.SourceName == resolvedSource.Name ||
            string.Equals(existing.Source, resolvedSource.Source, StringComparison.Ordinal));
        if (layer is null) {
            layer = MapImageLayer.FromSource(resolvedSource);
            layer.IsVisible = resolvedSource.IsVisible;
            _settings.ImageLayers.Insert(0, layer);
        }

        AppSettingsService.EnsureSinglePrimaryLayer(_settings);
        AppSettingsService.Save(_settings);
        RefreshRenderedLayerFromStack(layer);
    }

    private void LoadImageLayers(IReadOnlyList<MapImageLayer> layers) {
        CancelPendingMapWork();
        if (layers.Count == 0) {
            _activeImageLayer = null;
            DisposeTileLayers(_tileLayers);
            _tileLayers = [];
            ClearMapLayers();
            UpdateSourceSummary();
            UpdateAttribution();
            return;
        }

        _activeImageLayer = ResolveSettingsLayer(layers[0]);
        var source = _settings.GetSourceForLayer(_activeImageLayer);
        if (source is null) {
            _activeImageLayer = null;
            DisposeTileLayers(_tileLayers);
            _tileLayers = [];
            ClearMapLayers();
            UpdateSourceSummary();
            UpdateAttribution();
            return;
        }

        _activeSource = ResolveSettingsSource(source);
        _activeImageLayer.SourceName = _activeSource.Name;
        _activeImageLayer.Source = _activeSource.Source;
        _settings.ActiveSourceName = _activeSource.Name;
        _settings.MapMaxZoom = _activeSource.MapMaxZoom;
        AppSettingsService.Save(_settings);
        UpdateSourceSummary(_activeSource.MapMaxZoom, _activeSource.ImageMaxZoom);

        _ = LoadMapSourcesAsync(layers);
    }

    private async Task LoadMapSourcesAsync(IReadOnlyList<MapImageLayer> layers) {
        List<TileLayerContext> newTileLayers = [];
        try {
            _tileSourceCts?.Cancel();
            var tileSourceCts = new CancellationTokenSource();
            _tileSourceCts = tileSourceCts;
            var ct = tileSourceCts.Token;

            foreach (var layer in layers) {
                var settingsLayer = ResolveSettingsLayer(layer);
                var source = _settings.GetSourceForLayer(settingsLayer);
                if (source is null) continue;

                var sourceSnapshot = source.Clone();
                var tileService = new TileService();
                try {
                    await ApplyTileSourceAsync(tileService, sourceSnapshot, ct);
                } catch (OperationCanceledException) {
                    tileService.Dispose();
                    throw;
                } catch (Exception ex) {
                    tileService.Dispose();
                    Logger.Error($"Skipped unavailable map source '{sourceSnapshot.Name}'", ex);
                    continue;
                }

                newTileLayers.Add(new TileLayerContext(
                    settingsLayer.Id,
                    tileService,
                    sourceSnapshot,
                    Math.Clamp(settingsLayer.Opacity, 0.0, 1.0)));
                if (!source.IsKnownSource) {
                    source.ImageMaxZoom = tileService.ImageMaxZoom;
                }
                if (!LayerRenderPlanner.AllowsLowerLayers(settingsLayer)) {
                    break;
                }
            }

            if (ct.IsCancellationRequested || !ReferenceEquals(_tileSourceCts, tileSourceCts)) return;

            var oldTileLayers = _tileLayers;
            _tileLayers = newTileLayers;
            newTileLayers = [];
            DisposeTileLayers(oldTileLayers);
            UpdateAttribution();
            AppSettingsService.Save(_settings);

            var activeContext = _tileLayers.FirstOrDefault();
            if (activeContext is not null) {
                var loadedLayer = _settings.ImageLayers.FirstOrDefault(layer => layer.Id == activeContext.LayerId);
                var loadedSource = _settings.GetSourceForLayer(loadedLayer);
                if (loadedLayer is not null && loadedSource is not null) {
                    _activeImageLayer = loadedLayer;
                    _activeSource = loadedSource;
                    _settings.ActiveSourceName = loadedSource.Name;
                }
                UpdateSourceSummary(activeContext.Service.MapMaxZoom, activeContext.Service.ImageMaxZoom);
            } else if (_activeImageLayer is not null) {
                ActiveSourceTextBlock.Text = $"{_activeImageLayer.Name}（图源不可用）";
                ZoomLimitTextBlock.Text = "";
            }
            RefreshLayerList(_activeImageLayer);

            var zoom = ClampZoom(int.TryParse(ZoomTextBox.Text, out var z) ? z : 2);
            ZoomTextBox.Text = zoom.ToString();
            await Dispatcher.InvokeAsync(async () => {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                await RenderTilesAsync(zoom);
            }, DispatcherPriority.Background);
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            Logger.Error("Failed to load map source", ex);
        } finally {
            DisposeTileLayers(newTileLayers);
        }
    }

    private async Task RenderTilesAsync(int z) {
        z = ClampZoom(z);
        UpdateAttribution(z);
        _renderDebounceCts?.Cancel();
        _renderDebounceCts = null;
        _renderCts?.Cancel();
        var renderCts = new CancellationTokenSource();
        _renderCts = renderCts;
        var ct = renderCts.Token;

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

        var renderContexts = _tileLayers;
        if (ct.IsCancellationRequested) return;
        if (renderContexts.Count == 0) {
            await Dispatcher.InvokeAsync(ClearMapLayers);
            return;
        }

        var viewportW = MapViewport.ActualWidth;
        var viewportH = MapViewport.ActualHeight;
        if (viewportW <= 0) viewportW = 1024;
        if (viewportH <= 0) viewportH = 768;

        var (centerPixelX, centerPixelY) = GetRenderCenterPixel(z);
        var tileLayer = new TileLayerElement {
            Width = viewportW,
            Height = viewportH,
            IsHitTestVisible = false,
            ClipToBounds = false,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            CacheMode = new BitmapCache()
        };
        RenderOptions.SetBitmapScalingMode(tileLayer, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(tileLayer, EdgeMode.Aliased);
        var layer = new MapLayer(tileLayer, z, centerPixelX, centerPixelY, viewportW, viewportH);

        TileLayerLoadResult[] loadedLayers;
        try {
            loadedLayers = await Task.WhenAll(renderContexts
                .Reverse()
                .Select(context => LoadTileRenderGroupAsync(
                    context,
                    z,
                    centerPixelX,
                    centerPixelY,
                    viewportW,
                    viewportH,
                    ct)));
        } catch (OperationCanceledException) {
            return;
        } catch (Exception ex) {
            Logger.Error("Tile rendering failed", ex);
            return;
        }

        if (ct.IsCancellationRequested || !ReferenceEquals(_renderCts, renderCts)) return;
        if (loadedLayers.Sum(static result => result.TileCount) == 0 && _activeLayer is not null) return;

        await Dispatcher.InvokeAsync(() => {
            if (ct.IsCancellationRequested || !ReferenceEquals(_renderCts, renderCts)) return;

            layer.Element.SetLayers(loadedLayers.Select(static result => result.Group));
            BeginStagingLayer(layer);
            PromoteStagingLayer(layer);
        });

        foreach (var context in renderContexts) {
            if (!ShouldPrefetch(context.Source)) continue;

            var imageZoom = Math.Min(z, context.Service.ImageMaxZoom);
            var scale = Math.Pow(2, z - imageZoom);
            StartPredictivePrefetch(
                context,
                imageZoom,
                centerPixelX / scale,
                centerPixelY / scale,
                viewportW / scale,
                viewportH / scale,
                ct);
        }
    }

    private async Task<TileLayerLoadResult> LoadTileRenderGroupAsync(
        TileLayerContext context,
        int renderZoom,
        double renderCenterPixelX,
        double renderCenterPixelY,
        double viewportWidth,
        double viewportHeight,
        CancellationToken ct) {
        var imageZoom = Math.Min(renderZoom, context.Service.ImageMaxZoom);
        if (imageZoom < context.Service.ImageMinZoom) {
            return new TileLayerLoadResult(new TileRenderGroup([], context.Opacity), 0);
        }

        var scale = Math.Pow(2, renderZoom - imageZoom);
        var sourceCenterPixelX = renderCenterPixelX / scale;
        var sourceCenterPixelY = renderCenterPixelY / scale;
        var tileRange = TileRenderLayout.GetVisibleTileRange(
            sourceCenterPixelX,
            sourceCenterPixelY,
            viewportWidth / scale,
            viewportHeight / scale,
            imageZoom,
            RenderTileBuffer);
        var pendingRequests = new Queue<(int X, int Y)>(
            EnumerateTileRequests(tileRange, sourceCenterPixelX, sourceCenterPixelY)
                .Select(static request => (request.X, request.Y)));
        var tileItems = new List<TileRenderItem>();
        var addedTileKeys = new HashSet<string>();
        var tileItemsLock = new object();
        var loadedTileCount = 0;

        List<Task> workers = [];
        var workerCount = Math.Min(GetTileWorkerCount(context.Source), pendingRequests.Count);
        for (var i = 0; i < workerCount; i++) {
            workers.Add(LoadPendingTilesAsync());
        }
        await Task.WhenAll(workers).ConfigureAwait(false);

        lock (tileItemsLock) {
            return new TileLayerLoadResult(
                new TileRenderGroup([.. tileItems], context.Opacity),
                loadedTileCount);
        }

        async Task LoadPendingTilesAsync() {
            while (!ct.IsCancellationRequested) {
                (int X, int Y) request;
                lock (pendingRequests) {
                    if (pendingRequests.Count == 0) return;
                    request = pendingRequests.Dequeue();
                }

                try {
                    var tile = await GetOrLoadTileSourceAsync(
                        context,
                        imageZoom,
                        request.X,
                        request.Y,
                        ct).ConfigureAwait(false);
                    if (tile is not null) AddTile(tile);
                } catch (OperationCanceledException) {
                } catch (Exception ex) {
                    Logger.Error($"Tile task failed ({imageZoom},{request.X},{request.Y})", ex);
                }
            }
        }

        void AddTile(LoadedTile tile) {
            if (ct.IsCancellationRequested) return;

            var placement = TileRenderLayout.GetTilePlacement(
                tile.X,
                tile.Y,
                tile.Zoom,
                renderZoom,
                renderCenterPixelX,
                renderCenterPixelY,
                viewportWidth,
                viewportHeight);
            lock (tileItemsLock) {
                if (!addedTileKeys.Add($"{tile.Zoom}/{tile.X}/{tile.Y}")) return;

                tileItems.Add(new TileRenderItem(tile.Source, placement, tile.IsFallback));
            }
            Interlocked.Increment(ref loadedTileCount);
        }
    }

    private (double PixelX, double PixelY) GetRenderCenterPixel(int zoom) {
        var (centerPixelX, centerPixelY) = GeoConverter.LatLonToPixelXY(_centerLat, _centerLon, zoom);
        if (_panOffsetX == 0 && _panOffsetY == 0) {
            return (centerPixelX, centerPixelY);
        }

        var worldSize = GeoConverter.TileSize * (double)GeoConverter.GetTileCount(zoom);
        return (
            ClampWorldPixel(centerPixelX - _panOffsetX, worldSize),
            ClampWorldPixel(centerPixelY - _panOffsetY, worldSize));
    }

    private async Task<LoadedTile?> GetOrLoadTileSourceAsync(
        TileLayerContext context,
        int requestedZoom,
        int tileX,
        int tileY,
        CancellationToken ct) {
        var allowNoTileFallback = context.Source.NoTileEtags.Count > 0 || context.Source.NoTileMd5s.Count > 0;
        var minZoom = allowNoTileFallback ? context.Service.ImageMinZoom : requestedZoom;
        for (var zoom = requestedZoom; zoom >= minZoom; zoom--) {
            var shift = requestedZoom - zoom;
            var candidateX = shift == 0 ? tileX : tileX >> shift;
            var candidateY = shift == 0 ? tileY : tileY >> shift;
            var tileKey = GetTileCacheKey(context.Service.CacheIdentity, zoom, candidateX, candidateY);
            if (TileCache.TryGetValue(tileKey, out var cached)) {
                return new LoadedTile(cached, zoom, candidateX, candidateY, zoom < requestedZoom);
            }

            await TileThrottle.WaitAsync(ct).ConfigureAwait(false);
            try {
                if (TileCache.TryGetValue(tileKey, out cached)) {
                    return new LoadedTile(cached, zoom, candidateX, candidateY, zoom < requestedZoom);
                }

                var bytes = await context.Service
                    .GetTileBytesAsync(zoom, candidateX, candidateY, context.Source.AccessToken, ct)
                    .ConfigureAwait(false);
                if (bytes is null || ct.IsCancellationRequested) continue;

                var source = LoadTileImage(bytes);
                if (source is null) continue;

                TileCache.Add(tileKey, source);
                return new LoadedTile(source, zoom, candidateX, candidateY, zoom < requestedZoom);
            } finally {
                TileThrottle.Release();
            }
        }

        return null;
    }

    private static string GetTileCacheKey(string sourceKey, int z, int tileX, int tileY) {
        return $"{sourceKey}|{z}/{tileX}/{tileY}";
    }

    private async Task ApplyTileSourceAsync(
        TileService tileService,
        TileSourcePreset source,
        CancellationToken cancellationToken) {
        var accessToken = source.AccessToken ?? string.Empty;
        tileService.ParseUrlTemplate(source.Source, accessToken);
        tileService.ApplySourceOptions(
            source.MapMaxZoom,
            source.ImageMaxZoom,
            source.NoTileEtags,
            source.NoTileMd5s);
        await tileService.InitializeSourceAsync(accessToken, cancellationToken);

        if (!source.IsKnownSource && !tileService.IsBing) {
            await tileService.ResolveAutoMaxZoomAsync(_centerLat, _centerLon, accessToken, cancellationToken);
            source.ImageMaxZoom = tileService.ImageMaxZoom;
        }
    }

    private int ClampZoom(int zoom) {
        var mapMaxZoom = _tileLayers.FirstOrDefault()?.Service.MapMaxZoom ?? _activeSource.MapMaxZoom;
        return Math.Clamp(zoom, GeoConverter.MinZoom, mapMaxZoom);
    }

    private static int GetTileWorkerCount(TileSourcePreset source) {
        return IsOpenStreetMapVolunteerSource(source) ? 2 : MaxConcurrentTileLoads;
    }

    private static bool ShouldPrefetch(TileSourcePreset source) {
        return !IsOpenStreetMapVolunteerSource(source);
    }

    private static bool IsOpenStreetMapVolunteerSource(TileSourcePreset source) {
        return source.Source.Contains("tile.openstreetmap.org", StringComparison.OrdinalIgnoreCase);
    }

    private void StartPredictivePrefetch(
        TileLayerContext context,
        int z,
        double centerPixelX,
        double centerPixelY,
        double viewportWidth,
        double viewportHeight,
        CancellationToken ct) {
        _ = Task.Run(async () => {
            try {
                await PrefetchTilesAsync(
                    context,
                    z,
                    centerPixelX,
                    centerPixelY,
                    viewportWidth,
                    viewportHeight,
                    ct).ConfigureAwait(false);
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error("Tile prefetch failed", ex);
            }
        }, ct);
    }

    private async Task PrefetchTilesAsync(
        TileLayerContext context,
        int z,
        double centerPixelX,
        double centerPixelY,
        double viewportWidth,
        double viewportHeight,
        CancellationToken ct) {
        var renderRange = TileRenderLayout.GetVisibleTileRange(
            centerPixelX,
            centerPixelY,
            viewportWidth,
            viewportHeight,
            z,
            RenderTileBuffer);
        var prefetchRange = TileRenderLayout.GetVisibleTileRange(
            centerPixelX,
            centerPixelY,
            viewportWidth,
            viewportHeight,
            z,
            PrefetchTileBuffer);

        var requests = new Queue<(int X, int Y)>(
            EnumerateTileRequests(prefetchRange, centerPixelX, centerPixelY)
                .Where(tile => !ContainsTile(renderRange, tile.X, tile.Y))
                .Take(MaxPrefetchTiles)
                .Select(static tile => (tile.X, tile.Y)));
        if (requests.Count == 0) return;

        var workerCount = Math.Min(MaxPrefetchWorkers, requests.Count);
        List<Task> workers = [];
        for (var i = 0; i < workerCount; i++) {
            workers.Add(Task.Run(PrefetchWorkerAsync, ct));
        }

        await Task.WhenAll(workers).ConfigureAwait(false);

        async Task PrefetchWorkerAsync() {
            while (!ct.IsCancellationRequested) {
                (int X, int Y) request;
                lock (requests) {
                    if (requests.Count == 0) return;
                    request = requests.Dequeue();
                }

                await GetOrLoadTileSourceAsync(context, z, request.X, request.Y, ct)
                    .ConfigureAwait(false);
            }
        }
    }

    private static IEnumerable<(int X, int Y, double Distance)> EnumerateTileRequests(
        TileRange range,
        double centerPixelX,
        double centerPixelY) {
        List<(int X, int Y, double Distance)> requests = [];
        for (var tileY = range.StartY; tileY <= range.EndY; tileY++) {
            for (var tileX = range.StartX; tileX <= range.EndX; tileX++) {
                var tileCenterX = (tileX + 0.5) * GeoConverter.TileSize;
                var tileCenterY = (tileY + 0.5) * GeoConverter.TileSize;
                var distanceX = tileCenterX - centerPixelX;
                var distanceY = tileCenterY - centerPixelY;
                requests.Add((tileX, tileY, distanceX * distanceX + distanceY * distanceY));
            }
        }

        return requests.OrderBy(static request => request.Distance);
    }

    private static bool ContainsTile(TileRange range, int tileX, int tileY) {
        return tileX >= range.StartX &&
            tileX <= range.EndX &&
            tileY >= range.StartY &&
            tileY <= range.EndY;
    }

    private void BeginStagingLayer(MapLayer layer) {
        if (_stagingLayer is not null && !ReferenceEquals(_stagingLayer, _activeLayer)) {
            if (_activeLayer is null) {
                _activeLayer = _stagingLayer;
            } else {
                MapLayerHost.Children.Remove(_stagingLayer.Element);
            }
        }

        if (_fallbackLayer is not null && !ReferenceEquals(_fallbackLayer, _activeLayer)) {
            MapLayerHost.Children.Remove(_fallbackLayer.Element);
            _fallbackLayer = null;
        }

        _stagingLayer = layer;
        layer.Element.Visibility = _activeLayer is null ? Visibility.Visible : Visibility.Hidden;
        MapLayerHost.Children.Add(layer.Element);
        ApplyLayerTransforms();
    }

    private void PromoteStagingLayer(MapLayer layer) {
        if (!ReferenceEquals(_stagingLayer, layer)) return;

        if (_fallbackLayer is not null && !ReferenceEquals(_fallbackLayer, _activeLayer)) {
            MapLayerHost.Children.Remove(_fallbackLayer.Element);
        }

        _fallbackLayer = ReferenceEquals(_activeLayer, layer) ? null : _activeLayer;
        _activeLayer = layer;
        _stagingLayer = null;
        layer.Element.Visibility = Visibility.Visible;
        ApplyLayerTransforms();
    }

    private void ApplyLayerTransforms() {
        if (!int.TryParse(ZoomTextBox.Text, out var zoom)) return;

        var viewportWidth = MapViewport.ActualWidth;
        var viewportHeight = MapViewport.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        var dpi = VisualTreeHelper.GetDpi(MapViewport);
        foreach (var layer in GetVisibleLayers()) {
            var scale = Math.Pow(2, zoom - layer.Zoom);
            var (targetCenterX, targetCenterY) = GeoConverter.LatLonToPixelXY(_centerLat, _centerLon, layer.Zoom);
            var offsetX = viewportWidth / 2.0 - scale * layer.ViewportWidth / 2.0
                + scale * (layer.CenterPixelX - targetCenterX) + _panOffsetX;
            var offsetY = viewportHeight / 2.0 - scale * layer.ViewportHeight / 2.0
                + scale * (layer.CenterPixelY - targetCenterY) + _panOffsetY;

            offsetX = TileRenderLayout.SnapToDevicePixel(offsetX, dpi.DpiScaleX);
            offsetY = TileRenderLayout.SnapToDevicePixel(offsetY, dpi.DpiScaleY);
            layer.Element.RenderTransform = new MatrixTransform(scale, 0, 0, scale, offsetX, offsetY);
        }
    }

    private IEnumerable<MapLayer> GetVisibleLayers() {
        if (_fallbackLayer is not null) yield return _fallbackLayer;
        if (_activeLayer is not null && !ReferenceEquals(_activeLayer, _fallbackLayer)) yield return _activeLayer;
        if (_stagingLayer is not null && !ReferenceEquals(_stagingLayer, _activeLayer)) yield return _stagingLayer;
    }

    private static BitmapSource? LoadTileImage(byte[] data) {
        try {
            using var ms = new MemoryStream(data);
            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.StreamSource = ms;
            source.EndInit();
            source.Freeze();
            return source;
        } catch (Exception ex) {
            Logger.Error("Failed to decode image bytes", ex);
            return null;
        }
    }

    private void New_Click(object sender, RoutedEventArgs e) {
        MessageBox.Show("新建 被点击", "文件", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Open_Click(object sender, RoutedEventArgs e) {
        MessageBox.Show("打开 被点击", "文件", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Layer_Click(object sender, RoutedEventArgs e) {
        var win = new Views.LayersWindow { Owner = this };
        win.ShowDialog();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) {
        var win = new Views.SettingsWindow(_settings) { Owner = this };
        if (win.ShowDialog() != true) return;

        _settings = win.ResultSettings;
        AppSettingsService.EnsureDefaults(_settings);
        AppSettingsService.Save(_settings);
        TileCache.Clear();
        RefreshImageryMenu();
        var activeLayer = _settings.GetActiveLayer();
        RefreshRenderedLayerFromStack(activeLayer);
    }

    private void Show_Click(object sender, RoutedEventArgs e) {
        if (sender is MenuItem mi) {
            MessageBox.Show($"{mi.Header} 被点击", "菜单", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void RefreshImageryMenu() {
        while (ImageryMenuItem.Items.Count > 2) {
            ImageryMenuItem.Items.RemoveAt(2);
        }

        foreach (var source in _settings.TileSources) {
            var isSupported = TileSourceDefinition.IsSupported(source.Source);
            var item = new MenuItem {
                Header = source.Name,
                Tag = source,
                IsEnabled = isSupported,
                ToolTip = isSupported ? null : "此旧图源不再受支持，请在影像选项中替换或删除。"
            };
            item.Click += AddImageSourceMenuItem_Click;
            ImageryMenuItem.Items.Add(item);
        }
    }

    private void AddImageSourceMenuItem_Click(object sender, RoutedEventArgs e) {
        if (sender is MenuItem { Tag: TileSourcePreset source }) {
            AddImageLayer(source);
        }
    }

    private void LayerListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (_isUpdatingLayerList) return;
    }

    private void PrimaryLayer_Click(object sender, RoutedEventArgs e) {
        if (sender is not RadioButton { DataContext: MapImageLayer layer }) return;

        foreach (var item in _settings.ImageLayers) {
            item.IsPrimary = item.Id == layer.Id;
        }

        _settings.ActiveLayerId = layer.Id;
        AppSettingsService.Save(_settings);
        RefreshLayerList(layer);
    }

    private void LayerVisibilityButton_Click(object sender, RoutedEventArgs e) {
        if (sender is not Button { DataContext: MapImageLayer layer }) return;

        layer.IsVisible = !layer.IsVisible;
        AppSettingsService.Save(_settings);
        RefreshRenderedLayerFromStack(layer);
    }

    private void RemoveImageLayer_Click(object sender, RoutedEventArgs e) {
        if (LayerListBox.SelectedItem is not MapImageLayer layer) return;

        _settings.ImageLayers.Remove(layer);
        _settings.ActiveLayerId = _settings.ImageLayers.LastOrDefault(candidate => candidate.IsVisible)?.Id ??
            _settings.ImageLayers.LastOrDefault()?.Id ??
            "";
        AppSettingsService.EnsureSinglePrimaryLayer(_settings);
        AppSettingsService.Save(_settings);
        RefreshRenderedLayerFromStack(_settings.GetActiveLayer());
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) {
        if (int.TryParse(ZoomTextBox.Text, out var z)) {
            SetZoomAndRender(ClampZoom(z + 1));
        }
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e) {
        if (int.TryParse(ZoomTextBox.Text, out var z)) {
            SetZoomAndRender(Math.Max(GeoConverter.MinZoom, z - 1));
        }
    }

    private void SetZoomAndRender(int z, Point? anchor = null, bool debounceRender = false) {
        if (!int.TryParse(ZoomTextBox.Text, out var oldZoom)) oldZoom = z;
        z = ClampZoom(z);
        if (z == oldZoom) return;

        if (anchor is { } anchorPoint) {
            var viewportCenter = new Point(MapViewport.ActualWidth / 2.0, MapViewport.ActualHeight / 2.0);
            var offsetX = anchorPoint.X - viewportCenter.X;
            var offsetY = anchorPoint.Y - viewportCenter.Y;
            var (oldCenterX, oldCenterY) = GeoConverter.LatLonToPixelXY(_centerLat, _centerLon, oldZoom);
            var scale = Math.Pow(2, z - oldZoom);
            var newCenterX = (oldCenterX + offsetX) * scale - offsetX;
            var newCenterY = (oldCenterY + offsetY) * scale - offsetY;
            var worldSize = GeoConverter.TileSize * (double)GeoConverter.GetTileCount(z);
            newCenterX = ClampWorldPixel(newCenterX, worldSize);
            newCenterY = ClampWorldPixel(newCenterY, worldSize);
            (_centerLat, _centerLon) = GeoConverter.PixelXYToLatLon(newCenterX, newCenterY, z);
        }

        ZoomTextBox.Text = z.ToString();
        ApplyLayerTransforms();

        if (_tileLayers.Count > 0 && !string.IsNullOrEmpty(_tileService.TileTemplate) && _activeImageLayer?.IsVisible == true) {
            ScheduleRender(z, debounceRender ? WheelRenderDelayMilliseconds : 0);
        }
    }

    private void ScheduleRender(int zoom, int delayMilliseconds) {
        _renderDebounceCts?.Cancel();
        var debounceCts = new CancellationTokenSource();
        _renderDebounceCts = debounceCts;

        if (delayMilliseconds <= 0) {
            _ = RenderTilesAsync(zoom);
            return;
        }

        _ = Task.Run(async () => {
            try {
                await Task.Delay(delayMilliseconds, debounceCts.Token).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() => _ = RenderTilesAsync(zoom));
            } catch (OperationCanceledException) {
            }
        });
    }

    private void Window_KeyDown(object sender, KeyEventArgs e) {
        if (e.Key == Key.Add || e.Key == Key.OemPlus) {
            ZoomIn_Click(this, new());
            e.Handled = true;
        } else if (e.Key == Key.Subtract || e.Key == Key.OemMinus) {
            ZoomOut_Click(this, new());
            e.Handled = true;
        } else if (e.Key == Key.PageUp) {
            ZoomIn_Click(this, new());
            e.Handled = true;
        } else if (e.Key == Key.PageDown) {
            ZoomOut_Click(this, new());
            e.Handled = true;
        }
    }

    private void MapCanvas_MouseWheel(object sender, MouseWheelEventArgs e) {
        e.Handled = true;
        if (!int.TryParse(ZoomTextBox.Text, out var zoom)) return;

        var nextZoom = e.Delta > 0
            ? ClampZoom(zoom + 1)
            : Math.Max(GeoConverter.MinZoom, zoom - 1);
        SetZoomAndRender(nextZoom, e.GetPosition(MapViewport), debounceRender: true);
    }

    private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        _isPanning = true;
        _panStart = e.GetPosition(MapViewport);
        _panOffsetX = 0;
        _panOffsetY = 0;
        _lastPanPrefetchOffsetX = 0;
        _lastPanPrefetchOffsetY = 0;
        _lastPanPrefetchAt = DateTime.MinValue;
        MapViewport.CaptureMouse();
        Cursor = Cursors.Hand;
        e.Handled = true;
    }

    private void MapCanvas_MouseMove(object sender, MouseEventArgs e) {
        if (!_isPanning) return;
        var pos = e.GetPosition(MapViewport);
        _panOffsetX = pos.X - _panStart.X;
        _panOffsetY = pos.Y - _panStart.Y;
        ApplyLayerTransforms();
        SchedulePanPrefetch();
    }

    private void SchedulePanPrefetch() {
        if (_tileLayers.Count == 0 || string.IsNullOrEmpty(_tileService.TileTemplate) || _activeImageLayer?.IsVisible != true) return;
        if (!int.TryParse(ZoomTextBox.Text, out var zoom)) return;

        var movedX = _panOffsetX - _lastPanPrefetchOffsetX;
        var movedY = _panOffsetY - _lastPanPrefetchOffsetY;
        if (movedX * movedX + movedY * movedY < PanPrefetchDistance * PanPrefetchDistance) return;

        var now = DateTime.UtcNow;
        if ((now - _lastPanPrefetchAt).TotalMilliseconds < PanPrefetchIntervalMilliseconds) return;

        _lastPanPrefetchOffsetX = _panOffsetX;
        _lastPanPrefetchOffsetY = _panOffsetY;
        _lastPanPrefetchAt = now;
        ScheduleRender(zoom, 0);
    }

    private void MapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        if (!_isPanning) return;
        _isPanning = false;
        MapViewport.ReleaseMouseCapture();
        Cursor = Cursors.Arrow;

        try {
            if (!int.TryParse(ZoomTextBox.Text, out var zoom)) return;

            var shiftX = _panOffsetX;
            var shiftY = _panOffsetY;

            if (shiftX == 0 && shiftY == 0) {
                return;
            }

            var (oldPx, oldPy) = GeoConverter.LatLonToPixelXY(_centerLat, _centerLon, zoom);
            var worldSize = GeoConverter.TileSize * (double)GeoConverter.GetTileCount(zoom);
            var newPx = ClampWorldPixel(oldPx - shiftX, worldSize);
            var newPy = ClampWorldPixel(oldPy - shiftY, worldSize);
            (_centerLat, _centerLon) = GeoConverter.PixelXYToLatLon(newPx, newPy, zoom);

            _panOffsetX = 0;
            _panOffsetY = 0;
            _lastPanPrefetchOffsetX = 0;
            _lastPanPrefetchOffsetY = 0;
            ApplyLayerTransforms();
            ScheduleRender(zoom, 0);
        } catch (Exception ex) {
            Logger.Error("Pan update failed", ex);
        } finally {
            _panOffsetX = 0;
            _panOffsetY = 0;
            _lastPanPrefetchOffsetX = 0;
            _lastPanPrefetchOffsetY = 0;
        }
    }

    private void MapViewport_SizeChanged(object sender, SizeChangedEventArgs e) {
        ApplyLayerTransforms();
        if (!IsLoaded || _tileLayers.Count == 0 || string.IsNullOrEmpty(_tileService.TileTemplate) || _activeImageLayer?.IsVisible != true) return;
        if (!int.TryParse(ZoomTextBox.Text, out var zoom)) return;

        var isExpanding = e.NewSize.Width > e.PreviousSize.Width || e.NewSize.Height > e.PreviousSize.Height;
        ScheduleRender(zoom, isExpanding ? 0 : ResizeRenderDelayMilliseconds);
    }

    private void RefreshRenderedLayerFromStack(MapImageLayer? selectedLayer = null) {
        AppSettingsService.EnsureSinglePrimaryLayer(_settings);
        var rasterLayers = LayerRenderPlanner.GetRasterCandidates(_settings.ImageLayers);
        RefreshLayerList(selectedLayer ?? _settings.GetActiveLayer());
        LoadImageLayers(rasterLayers);
    }

    private void CancelPendingMapWork() {
        _tileSourceCts?.Cancel();
        _renderDebounceCts?.Cancel();
        _renderCts?.Cancel();
    }

    private static void DisposeTileLayers(IEnumerable<TileLayerContext> layers) {
        foreach (var layer in layers) {
            layer.Service.Dispose();
        }
    }

    private void RefreshLayerList(MapImageLayer? selectedLayer = null) {
        _isUpdatingLayerList = true;
        try {
            var selected = selectedLayer is null
                ? _settings.GetActiveLayer()
                : ResolveSettingsLayer(selectedLayer);
            LayerListBox.ItemsSource = null;
            LayerListBox.ItemsSource = _settings.ImageLayers;
            LayerListBox.SelectedItem = selected;
        } finally {
            _isUpdatingLayerList = false;
        }
    }

    private void SelectLayer(MapImageLayer layer) {
        _isUpdatingLayerList = true;
        try {
            LayerListBox.SelectedItem = ResolveSettingsLayer(layer);
        } finally {
            _isUpdatingLayerList = false;
        }
    }

    private MapImageLayer ResolveSettingsLayer(MapImageLayer layer) {
        return _settings.ImageLayers.FirstOrDefault(candidate => ReferenceEquals(candidate, layer)) ??
            _settings.ImageLayers.FirstOrDefault(candidate => candidate.Id == layer.Id) ??
            layer;
    }

    private TileSourcePreset ResolveSettingsSource(TileSourcePreset source) {
        return _settings.TileSources.FirstOrDefault(candidate => ReferenceEquals(candidate, source)) ??
            _settings.TileSources.FirstOrDefault(candidate => candidate.Name == source.Name) ??
            source;
    }

    private TileSourcePreset GetOrCreateCustomSource(string baseName, string sourceUrl) {
        var existing = _settings.TileSources.FirstOrDefault(source =>
            string.Equals(source.Source, sourceUrl, StringComparison.Ordinal));
        if (existing is not null) return existing;

        var source = new TileSourcePreset {
            Name = CreateUniqueSourceName(baseName),
            Source = sourceUrl,
            MapMaxZoom = GeoConverter.MaxZoom,
            ImageMaxZoom = GeoConverter.MaxZoom,
            IsVisible = true,
            IsKnownSource = false
        };
        _settings.TileSources.Add(source);
        AppSettingsService.Save(_settings);
        RefreshImageryMenu();
        return source;
    }

    private string CreateUniqueSourceName(string baseName) {
        if (_settings.TileSources.All(source => source.Name != baseName)) return baseName;

        for (var i = 2; i < 1000; i++) {
            var candidate = $"{baseName} {i}";
            if (_settings.TileSources.All(source => source.Name != candidate)) return candidate;
        }

        return $"{baseName} {DateTime.Now:HHmmss}";
    }

    private static string NormalizeLayerSource(string type, string url) {
        var trimmedUrl = url.Trim();
        if (!trimmedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
            return trimmedUrl;
        }

        var prefix = string.IsNullOrWhiteSpace(type) ? "xyz" : type.Trim().ToLowerInvariant();
        return $"{prefix}:{trimmedUrl}";
    }

    private void UpdateSourceSummary(int? mapMaxZoom = null, int? imageMaxZoom = null) {
        if (_activeImageLayer is null) {
            ActiveSourceTextBlock.Text = "未添加图层";
            ZoomLimitTextBlock.Text = "";
            UpdateAttribution();
            return;
        }

        ActiveSourceTextBlock.Text = _activeImageLayer.Name;
        ZoomLimitTextBlock.Text =
            $"地图 0-{mapMaxZoom ?? _activeSource.MapMaxZoom} / 影像 0-{imageMaxZoom ?? _activeSource.ImageMaxZoom}";
        UpdateAttribution();
    }

    private void UpdateAttribution() => UpdateAttribution(null);

    private void UpdateAttribution(int? zoomOverride) {
        List<TileAttribution> attributions = [];
        if (_tileLayers.Count > 0) {
            var zoom = zoomOverride ?? (int.TryParse(ZoomTextBox.Text, out var parsedZoom) ? parsedZoom : GeoConverter.MinZoom);
            var bounds = GetViewportBounds(zoom);
            foreach (var context in _tileLayers) {
                if (context.Service.IsBing) {
                    var imageryZoom = Math.Min(zoom, context.Service.ImageMaxZoom);
                    attributions.AddRange(context.Service.GetAttributions(
                        imageryZoom,
                        bounds.South,
                        bounds.West,
                        bounds.North,
                        bounds.East));
                    continue;
                }

                var attribution = GetAttribution(context.Source);
                if (!string.IsNullOrWhiteSpace(attribution.Text)) {
                    attributions.Add(new TileAttribution(attribution.Text, attribution.Url));
                }
            }
        } else if (_activeImageLayer?.IsVisible == true) {
            var attribution = GetAttribution(_activeSource);
            if (!string.IsNullOrWhiteSpace(attribution.Text)) {
                attributions.Add(new TileAttribution(attribution.Text, attribution.Url));
            }
        }

        attributions = attributions.Distinct().ToList();

        AttributionTextBlock.Inlines.Clear();
        for (var i = 0; i < attributions.Count; i++) {
            if (i > 0) AttributionTextBlock.Inlines.Add(new Run(" | "));

            var attribution = attributions[i];
            var uri = TryCreateWebUri(attribution.Url);
            if (uri is null) {
                AttributionTextBlock.Inlines.Add(new Run(attribution.Text));
                continue;
            }

            var link = new Hyperlink(new Run(attribution.Text)) { NavigateUri = uri };
            link.RequestNavigate += AttributionHyperlink_RequestNavigate;
            AttributionTextBlock.Inlines.Add(link);
        }

        AttributionBorder.Visibility = attributions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private (double South, double West, double North, double East) GetViewportBounds(int zoom) {
        var viewportWidth = MapViewport.ActualWidth > 0 ? MapViewport.ActualWidth : 1024;
        var viewportHeight = MapViewport.ActualHeight > 0 ? MapViewport.ActualHeight : 768;
        var (centerX, centerY) = GetRenderCenterPixel(zoom);
        var worldSize = GeoConverter.TileSize * (double)GeoConverter.GetTileCount(zoom);
        var topY = Math.Clamp(centerY - viewportHeight / 2.0, 0, worldSize);
        var bottomY = Math.Clamp(centerY + viewportHeight / 2.0, 0, worldSize);
        var topLeft = GeoConverter.PixelXYToLatLon(centerX - viewportWidth / 2.0, topY, zoom);
        var bottomRight = GeoConverter.PixelXYToLatLon(centerX + viewportWidth / 2.0, bottomY, zoom);
        return (bottomRight.Lat, topLeft.Lon, topLeft.Lat, bottomRight.Lon);
    }

    private static (string Text, string Url) GetAttribution(TileSourcePreset source) {
        if (!string.IsNullOrWhiteSpace(source.AttributionText)) {
            return (source.AttributionText, source.AttributionUrl);
        }

        return IsOpenStreetMapVolunteerSource(source)
            ? ("© OpenStreetMap contributors", "https://www.openstreetmap.org/copyright")
            : ("", "");
    }

    private void AttributionHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) {
        if (e.Uri.Scheme is not ("http" or "https")) return;

        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private static Uri? TryCreateWebUri(string value) {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;

        return uri.Scheme is "http" or "https" ? uri : null;
    }

    private void ClearMapLayers() {
        _renderCts?.Cancel();
        _renderDebounceCts?.Cancel();
        _renderDebounceCts = null;
        MapLayerHost.Children.Clear();
        _activeLayer = null;
        _fallbackLayer = null;
        _stagingLayer = null;
    }

    private sealed record MapLayer(
        TileLayerElement Element,
        int Zoom,
        double CenterPixelX,
        double CenterPixelY,
        double ViewportWidth,
        double ViewportHeight);

    private sealed record LoadedTile(BitmapSource Source, int Zoom, int X, int Y, bool IsFallback);

    private sealed record TileLayerLoadResult(TileRenderGroup Group, int TileCount);

    private sealed record TileLayerContext(
        string LayerId,
        TileService Service,
        TileSourcePreset Source,
        double Opacity);

    private static double ClampWorldPixel(double value, double worldSize) {
        return Math.Clamp(value, 0, worldSize);
    }
}
