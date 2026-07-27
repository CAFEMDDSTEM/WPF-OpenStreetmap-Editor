using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using MahApps.Metro.IconPacks;
using Microsoft.Win32;
using WPF_OpenStreetmap_Editor.Controls;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Plugins;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor;

public partial class MainWindow : Window {
    private readonly MainWindowViewModel _viewModel = new();
    private readonly PluginHost? _pluginHost;
    private readonly IReadOnlyList<PluginActionRequest> _startupPluginActions;
    private readonly AppUpdateCheckResult? _startupUpdateCheck;
    private readonly List<MenuItem> _pluginMenuItems = [];
    private NonTextInputImeGuard? _nonTextInputImeGuard;
    private readonly OsmAccountStore _osmAccountStore = new();
    private WindowState _lastNonMinimizedWindowState = WindowState.Normal;
    private double _centerLon;
    private double _centerLat;
    private bool _isPanning;
    private bool _isUpdatingFeatureSelection;
    private bool _isUpdatingLayerList;
    private bool _isUpdatingLayerDetails;
    private Point _layerDragStart;
    private MapImageLayer? _draggedLayer;
    private EditorMode _editorMode = EditorMode.Select;
    private EditorSession Editor => _viewModel.EditorSession;
    private SelectionService Selection => _viewModel.Selection;
    private MapDocument? _document {
        get => Editor.Document;
        set {
            if ((_featureRotation is not null || _featureMove is not null) && MapViewport.IsMouseCaptured) {
                MapViewport.ReleaseMouseCapture();
            }
            _featureRotation = null;
            _featureMove = null;
            Editor.ReplaceDocument(value);
            _featureClipboard = [];
            _clipboardPasteCount = 0;
        }
    }

    private IReadOnlyList<MapFeature> _featureClipboard = [];
    private int _clipboardPasteCount;
    private FeatureRotation? _featureRotation;
    private FeatureMove? _featureMove;
    private Point? _boxSelectionStart;
    private GeoBounds? _selectionBounds;
    private MouseButton? _panButton;
    private Point _panStart;
    private double _panOffsetX;
    private double _panOffsetY;
    private CancellationTokenSource? _renderCts;
    private CancellationTokenSource? _renderDebounceCts;
    private CancellationTokenSource? _layerStackRefreshCts;
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
    private const int RenderTileBuffer = 0;
    private const int PrefetchTileBuffer = 1;
    private const int MaxPrefetchTiles = 24;
    private const int MaxPrefetchWorkers = 2;
    private const int MaxConcurrentTileLoads = 8;
    private const int WheelRenderDelayMilliseconds = 180;
    private const int ResizeRenderDelayMilliseconds = 120;
    private const int LayerStackRefreshDelayMilliseconds = 160;
    private const int PanPrefetchIntervalMilliseconds = 140;
    private const double FeaturePasteOffsetPixels = 24.0;
    private const double MinimumCommittedRotationDegrees = 0.01;
    private const double MinimumCommittedMovePixels = 0.5;
    private const double PanPrefetchDistance = GeoConverter.TileSize * 0.75;
    private static readonly SemaphoreSlim TileThrottle = new(MaxConcurrentTileLoads, MaxConcurrentTileLoads);
    private static readonly HttpClient OsmHttpClient = new() { Timeout = TimeSpan.FromMinutes(3) };

    public MainWindow() : this(null, null, null) {
    }

    public MainWindow(
        PluginHost? pluginHost,
        IReadOnlyList<PluginActionRequest>? startupPluginActions = null,
        AppUpdateCheckResult? startupUpdateCheck = null) {
        _pluginHost = pluginHost;
        _startupPluginActions = startupPluginActions ?? [];
        _startupUpdateCheck = startupUpdateCheck;
        InitializeComponent();
        ThemeService.ApplyWindowTheme(this);
        AppSettingsService.EnsureDefaults(_settings);
        RefreshImageryMenu();
        RefreshLayerList(_settings.GetActiveLayer());

        WindowStartupService.ApplyStartupState(this, App.StartupArguments);
        _lastNonMinimizedWindowState = WindowState;
        StateChanged += Window_StateChanged;
        Closing += MainWindow_Closing;
        Closing += (_, _) => WindowStartupService.Save(
            WindowStartupService.GetStateToSave(WindowState, _lastNonMinimizedWindowState));
        Closed += (_, _) => {
            _nonTextInputImeGuard?.Dispose();
            _nonTextInputImeGuard = null;
            _tileSourceCts?.Cancel();
            _layerStackRefreshCts?.Cancel();
            _renderDebounceCts?.Cancel();
            _renderCts?.Cancel();
            TileImageLoader.Shared.Clear();
            DisposeTileLayers(_tileLayers);
            _tileLayers = [];
        };

        Loaded += MainWindow_Loaded;
        SetEditorMode(EditorMode.Select);
        UpdateDocumentUi();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e) {
        RefreshRenderedLayerFromStack(_settings.GetActiveLayer());
        UpdateVectorLayer();
        RefreshPluginMenus();
        RefreshPluginToolbar();
        await ExecuteDefaultPluginCommandAsync(
            BuiltInPluginCatalog.BetterImePluginId,
            "Better IME For WOSM",
            BuiltInPluginCatalog.BetterImeEnableCommandId);
        try {
            await ApplyPluginActionsAsync(_startupPluginActions);
            if (_pluginHost is not null) {
                var actions = await _pluginHost.PublishAsync(PluginHooks.MainWindowLoaded, new {
                    title = Title
                });
                await ApplyPluginActionsAsync(actions);
            }
        } catch (Exception ex) {
            Logger.Error("Failed to publish the main-window plugin hook", ex);
        }

        _ = Dispatcher.InvokeAsync(ShowStartupUpdateNotification, DispatcherPriority.ApplicationIdle);
    }

    private void ShowStartupUpdateNotification() {
        if (_startupUpdateCheck?.IsUpdateAvailable != true ||
            _startupUpdateCheck.LatestRelease is not { } latest) {
            return;
        }

        var response = MessageBox.Show(
            $"发现新版本 {latest.Version}。\n当前版本：{_startupUpdateCheck.CurrentVersion}\n\n是否打开下载页面？",
            "发现更新",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.Yes);
        if (response != MessageBoxResult.Yes) return;

        var uri = TryCreateWebUri(latest.ReleaseUrl);
        if (uri is null) return;

        try {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        } catch (Exception ex) {
            Logger.Error("Failed to open update release page", ex);
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e) {
        if (!ConfirmDiscardChanges("退出前尚有未保存的地图修改。是否放弃这些修改？")) {
            e.Cancel = true;
        }
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
            await TileThrottle.WaitAsync(ct).ConfigureAwait(false);
            try {
                var source = await TileImageLoader.Shared
                    .LoadAsync(context.Service, zoom, candidateX, candidateY, context.Source.AccessToken, ct)
                    .ConfigureAwait(false);
                if (source is null) continue;

                return new LoadedTile(source, zoom, candidateX, candidateY, zoom < requestedZoom);
            } finally {
                TileThrottle.Release();
            }
        }

        return null;
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
        UpdateVectorLayer();
    }

    private IEnumerable<MapLayer> GetVisibleLayers() {
        if (_fallbackLayer is not null) yield return _fallbackLayer;
        if (_activeLayer is not null && !ReferenceEquals(_activeLayer, _fallbackLayer)) yield return _activeLayer;
        if (_stagingLayer is not null && !ReferenceEquals(_stagingLayer, _activeLayer)) yield return _stagingLayer;
    }

    private void New_Click(object sender, RoutedEventArgs e) {
        if (!ConfirmDiscardChanges("当前地图尚未保存。是否放弃修改并新建地图？")) return;

        _document = new MapDocument();
        _document.MarkClean();
        _selectionBounds = null;
        SetSelectedFeatures([]);
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private async void Open_Click(object sender, RoutedEventArgs e) {
        if (!ConfirmDiscardChanges("当前地图尚未保存。是否放弃修改并打开其他文件？")) return;

        var dialog = new OpenFileDialog {
            Title = "导入地图数据",
            Filter = SpatialDataService.OpenFileFilter,
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        await ImportDocumentAsync(dialog.FileName);
    }

    private async void Save_Click(object sender, RoutedEventArgs e) {
        await SaveDocumentAsync(forceSaveAs: false);
    }

    private async void SaveAs_Click(object sender, RoutedEventArgs e) {
        await SaveDocumentAsync(forceSaveAs: true);
    }

    private async Task ImportDocumentAsync(string path) {
        try {
            IsEnabled = false;
            DocumentStatusTextBlock.Text = $"正在导入 {Path.GetFileName(path)}...";
            var progress = new Progress<SpatialImportProgress>(update =>
                DocumentStatusTextBlock.Text = $"{update.Stage}：{update.FeaturesRead:N0} 个要素");
            var document = await SpatialDataService.ImportAsync(path, progress: progress);
            _document = document;
            _selectionBounds = null;
            SetSelectedFeatures([]);
            FitDocumentToViewport();
            RefreshFeatureList();
            UpdateVectorLayer();
            UpdateDocumentUi();
        } catch (Exception ex) {
            Logger.Error($"Failed to import spatial data '{path}'", ex);
            MessageBox.Show(ex.Message, "无法导入地图数据", MessageBoxButton.OK, MessageBoxImage.Error);
        } finally {
            IsEnabled = true;
        }
    }

    private async Task SaveDocumentAsync(bool forceSaveAs) {
        if (_featureRotation is not null) CommitFeatureRotation();
        if (_featureMove is not null) CommitFeatureMove();
        if (Editor.HasDraftLine) FinishDraftLine();

        if (_document is null) {
            MessageBox.Show("当前没有可保存的地图数据。", "保存地图", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var path = forceSaveAs || string.IsNullOrWhiteSpace(_document.SourcePath) ||
            _document.SourceFormat is SpatialFileFormat.OsmPbf or SpatialFileFormat.Shapefile or SpatialFileFormat.Kmz
                ? ChooseSavePath(_document)
                : _document.SourcePath;
        if (string.IsNullOrWhiteSpace(path)) return;

        try {
            IsEnabled = false;
            DocumentStatusTextBlock.Text = $"正在保存 {Path.GetFileName(path)}...";
            await SpatialDataService.SaveAsync(_document, path);
            Editor.CommandStack.Clear();
            RefreshFeatureList();
            UpdateDocumentUi();
        } catch (Exception ex) {
            Logger.Error($"Failed to save spatial data '{path}'", ex);
            MessageBox.Show(ex.Message, "无法保存地图", MessageBoxButton.OK, MessageBoxImage.Error);
        } finally {
            IsEnabled = true;
        }
    }

    private string? ChooseSavePath(MapDocument document) {
        var baseName = Path.GetFileNameWithoutExtension(document.SourcePath ?? document.Name);
        var dialog = new SaveFileDialog {
            Title = "保存地图",
            Filter = SpatialDataService.SaveFileFilter,
            FileName = string.IsNullOrWhiteSpace(baseName) ? "map.geojson" : $"{baseName}.geojson",
            AddExtension = true,
            DefaultExt = ".geojson",
            OverwritePrompt = true
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void Layer_Click(object sender, RoutedEventArgs e) {
        var win = new Views.LayersWindow { Owner = this };
        win.ShowDialog();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) {
        ShowSettings(Views.SettingsSection.Appearance);
    }

    private void ImagerySettings_Click(object sender, RoutedEventArgs e) {
        ShowSettings(Views.SettingsSection.Sources);
    }

    private void ShowSettings(Views.SettingsSection initialSection) {
        var win = new Views.SettingsWindow(_settings, initialSection) { Owner = this };
        if (win.ShowDialog() != true) return;

        _settings = win.ResultSettings;
        AppSettingsService.EnsureDefaults(_settings);
        AppSettingsService.Save(_settings);
        ThemeService.ApplyTheme(_settings.ThemeId);
        TileImageLoader.Shared.Clear();
        RefreshImageryMenu();
        var activeLayer = _settings.GetActiveLayer();
        RefreshRenderedLayerFromStack(activeLayer);
    }

    private void Plugins_Click(object sender, RoutedEventArgs e) {
        if (_pluginHost is null) {
            MessageBox.Show("插件宿主尚未初始化。", "插件", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new Views.PluginsWindow(_pluginHost) { Owner = this };
        window.ShowDialog();
        RefreshPluginMenus();
        RefreshPluginToolbar();
    }

    private void Help_Click(object sender, RoutedEventArgs e) {
        ShowHelp();
    }

    private void ShowHelp() {
        var window = new Views.HelpWindow { Owner = this };
        window.ShowDialog();
    }

    private void RefreshPluginMenus() {
        foreach (var item in _pluginMenuItems) {
            ToolsMenuItem.Items.Remove(item);
        }
        _pluginMenuItems.Clear();

        if (_pluginHost is null) {
            PluginContributionsSeparator.Visibility = Visibility.Collapsed;
            return;
        }

        var insertionIndex = ToolsMenuItem.Items.IndexOf(PluginContributionsSeparator);
        foreach (var contribution in _pluginHost.MenuContributions) {
            var item = new MenuItem {
                Header = contribution.Menu.Label,
                Tag = contribution,
                ToolTip = contribution.PluginName
            };
            item.Click += PluginCommand_Click;
            ToolsMenuItem.Items.Insert(insertionIndex++, item);
            _pluginMenuItems.Add(item);
        }

        PluginContributionsSeparator.Visibility = _pluginMenuItems.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void PluginCommand_Click(object sender, RoutedEventArgs e) {
        if (_pluginHost is null || sender is not MenuItem { Tag: PluginMenuContribution contribution }) return;

        await ExecutePluginCommandAsync(
            contribution.PluginId,
            contribution.PluginName,
            contribution.Menu.Command);
    }

    private void RefreshPluginToolbar() {
        PluginToolbarPanel.Children.Clear();
        if (_pluginHost is null) return;

        foreach (var contribution in _pluginHost.ToolbarContributions
                     .Where(contribution => string.Equals(
                         contribution.Toolbar.Location,
                         "main",
                         StringComparison.OrdinalIgnoreCase))
                     .OrderBy(contribution => contribution.Toolbar.Order)
                     .ThenBy(contribution => contribution.PluginName, StringComparer.CurrentCulture)
                     .ThenBy(contribution => contribution.Toolbar.Command, StringComparer.Ordinal)) {
            var toolTip = string.IsNullOrWhiteSpace(contribution.Toolbar.ToolTip)
                ? contribution.PluginName
                : contribution.Toolbar.ToolTip;
            var button = new Button {
                Width = 34,
                Height = 34,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                ToolTip = toolTip,
                Tag = contribution,
                Content = CreatePluginToolbarIcon(contribution.Toolbar.Icon)
            };
            AutomationProperties.SetName(button, toolTip);
            button.Click += PluginToolbarCommand_Click;
            PluginToolbarPanel.Children.Add(button);
        }
    }

    private static PackIconLucide CreatePluginToolbarIcon(string icon) {
        var kind = Enum.TryParse(icon, true, out PackIconLucideKind parsedKind)
            ? parsedKind
            : PackIconLucideKind.Puzzle;
        return new PackIconLucide {
            Kind = kind,
            Width = 18,
            Height = 18
        };
    }

    private async void PluginToolbarCommand_Click(object sender, RoutedEventArgs e) {
        if (sender is not Button { Tag: PluginToolbarContribution contribution }) return;

        await ExecutePluginCommandAsync(
            contribution.PluginId,
            contribution.PluginName,
            contribution.Toolbar.Command);
    }

    private async Task ExecutePluginCommandAsync(string pluginId, string pluginName, string commandId) {
        if (_pluginHost is null) return;

        try {
            var result = await _pluginHost.ExecuteCommandAsync(
                pluginId,
                commandId);
            foreach (var action in result.Actions) {
                await ApplyPluginActionAsync(pluginName, action);
            }
        } catch (Exception ex) {
            Logger.Error($"Plugin command '{commandId}' failed", ex);
            MessageBox.Show(ex.Message, pluginName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ExecuteDefaultPluginCommandAsync(string pluginId, string pluginName, string commandId) {
        if (_pluginHost is null) return;

        try {
            var result = await _pluginHost.ExecuteCommandAsync(
                pluginId,
                commandId);
            foreach (var action in result.Actions) {
                await ApplyPluginActionAsync(pluginName, action);
            }
        } catch (Exception ex) {
            Logger.Error($"Default plugin '{pluginId}' command '{commandId}' failed", ex);
        }
    }

    private async Task ApplyPluginActionsAsync(IEnumerable<PluginActionRequest> requests) {
        foreach (var request in requests) {
            await ApplyPluginActionAsync(request.PluginName, request.Action);
        }
    }

    private async Task ApplyPluginActionAsync(string pluginName, PluginActionManifest action) {
        switch (action.Type) {
            case PluginActionTypes.ShowMessage:
                var message = GetPluginArgument(action, "message");
                var title = GetPluginArgument(action, "title");
                MessageBox.Show(
                    message ?? "",
                    string.IsNullOrWhiteSpace(title) ? pluginName : title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                break;
            case PluginActionTypes.OpenUrl:
                var url = GetPluginArgument(action, "url");
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("http" or "https")) {
                    throw new InvalidOperationException("插件只能打开 HTTP 或 HTTPS 链接。");
                }
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                break;
            case PluginActionTypes.AddImagery:
                var sourceUrl = GetPluginArgument(action, "url");
                if (string.IsNullOrWhiteSpace(sourceUrl)) {
                    throw new InvalidOperationException("addImagery 动作缺少 url。");
                }
                LoadLayer(GetPluginArgument(action, "type") ?? "xyz", sourceUrl);
                break;
            case PluginActionTypes.EnableNonTextInputImeGuard:
                _nonTextInputImeGuard ??= NonTextInputImeGuard.Attach(this);
                break;
            case PluginActionTypes.ManageOsmAccounts:
                new Views.OsmAccountsWindow(_osmAccountStore) { Owner = this }.ShowDialog();
                break;
            case PluginActionTypes.DownloadOsm:
                await DownloadOsmSelectionAsync();
                break;
            case PluginActionTypes.UploadOsm:
                await UploadOsmChangesAsync();
                break;
            default:
                throw new InvalidOperationException($"不支持插件动作“{action.Type}”。");
        }
    }

    private static string? GetPluginArgument(PluginActionManifest action, string name) {
        if (action.Arguments.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !action.Arguments.TryGetProperty(name, out var value) ||
            value.ValueKind != System.Text.Json.JsonValueKind.String) {
            return null;
        }
        return value.GetString();
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

        UpdateSelectedLayerDetails();
    }

    private void LayerListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        _layerDragStart = e.GetPosition(LayerListBox);
        _draggedLayer = null;

        var source = e.OriginalSource as DependencyObject;
        if (FindVisualAncestor<ButtonBase>(source) is not null) return;
        if (FindVisualAncestor<Slider>(source) is not null) return;

        _draggedLayer = FindVisualAncestor<ListBoxItem>(source)?.DataContext as MapImageLayer;
    }

    private void SelectedLayerVisibilityCheckBox_Click(object sender, RoutedEventArgs e) {
        if (_isUpdatingLayerDetails || LayerListBox.SelectedItem is not MapImageLayer layer) return;

        layer.IsVisible = SelectedLayerVisibilityCheckBox.IsChecked == true;
        AppSettingsService.Save(_settings);
        if (layer.Kind == MapLayerKind.Data) {
            UpdateVectorLayer();
            RefreshLayerList(layer);
            return;
        }

        RefreshRenderedLayerFromStack(layer);
    }

    private void LayerOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
        if (_isUpdatingLayerDetails) return;

        var opacity = Math.Clamp(LayerOpacitySlider.Value / 100.0, 0.0, 1.0);
        LayerOpacityValueTextBlock.Text = FormatLayerOpacityPercent(opacity);
        if (LayerListBox.SelectedItem is not MapImageLayer layer) return;
        if (Math.Abs(layer.Opacity - opacity) < 0.0001) return;

        layer.Opacity = opacity;
        AppSettingsService.Save(_settings);
        if (layer.Kind == MapLayerKind.Data) {
            UpdateVectorLayer();
            return;
        }

        ScheduleLayerStackRefresh(layer);
    }

    private void LayerListBox_PreviewMouseMove(object sender, MouseEventArgs e) {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedLayer is null) return;

        var position = e.GetPosition(LayerListBox);
        if (Math.Abs(position.X - _layerDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _layerDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) {
            return;
        }

        var layer = _draggedLayer;
        _draggedLayer = null;
        DragDrop.DoDragDrop(LayerListBox, new DataObject(typeof(MapImageLayer), layer), DragDropEffects.Move);
    }

    private void LayerListBox_DragOver(object sender, DragEventArgs e) {
        e.Effects = e.Data.GetDataPresent(typeof(MapImageLayer))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void LayerListBox_Drop(object sender, DragEventArgs e) {
        if (!e.Data.GetDataPresent(typeof(MapImageLayer))) return;
        if (e.Data.GetData(typeof(MapImageLayer)) is not MapImageLayer layer) return;

        var insertIndex = _settings.ImageLayers.Count;
        var targetItem = FindVisualAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (targetItem?.DataContext is MapImageLayer targetLayer) {
            var targetIndex = _settings.ImageLayers.FindIndex(candidate =>
                ReferenceEquals(candidate, targetLayer) || candidate.Id == targetLayer.Id);
            if (targetIndex < 0) return;

            insertIndex = targetIndex;
            if (e.GetPosition(targetItem).Y > targetItem.ActualHeight / 2.0) {
                insertIndex++;
            }
        }

        if (!AppSettingsService.MoveImageLayer(_settings, layer, insertIndex)) return;

        AppSettingsService.EnsureSinglePrimaryLayer(_settings);
        AppSettingsService.Save(_settings);
        RefreshRenderedLayerFromStack(layer);
        e.Handled = true;
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

    private async void Window_KeyDown(object sender, KeyEventArgs e) {
        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.Control && e.Key == Key.S) {
            await SaveDocumentAsync(forceSaveAs: false);
            e.Handled = true;
        } else if (modifiers == ModifierKeys.Control && e.Key == Key.C) {
            CopySelectedFeatures();
            e.Handled = true;
        } else if (modifiers == ModifierKeys.Control && e.Key == Key.V) {
            PasteFeatures();
            e.Handled = true;
        } else if (modifiers == ModifierKeys.Control && e.Key == Key.Z) {
            UndoLastEdit();
            e.Handled = true;
        } else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.D) {
            DuplicateSelectedFeatures();
            e.Handled = true;
        } else if ((modifiers == ModifierKeys.Control && e.Key == Key.Y) ||
            (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z)) {
            RedoLastEdit();
            e.Handled = true;
        } else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Down) {
            await ExecuteOsmPluginCommandAsync("download");
            e.Handled = true;
        } else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Up) {
            await ExecuteOsmPluginCommandAsync("upload");
            e.Handled = true;
        } else if (modifiers == ModifierKeys.None && e.Key == Key.F1) {
            ShowHelp();
            e.Handled = true;
        } else if (modifiers == ModifierKeys.None && e.Key == Key.A) {
            SetEditorMode(EditorMode.DrawLine);
            e.Handled = true;
        } else if (modifiers == ModifierKeys.None && e.Key == Key.S) {
            SetEditorMode(EditorMode.Select);
            e.Handled = true;
        } else if (modifiers == ModifierKeys.None && e.Key == Key.V) {
            SetEditorMode(EditorMode.BoxSelect);
            e.Handled = true;
        } else if (modifiers == ModifierKeys.None && e.Key == Key.R) {
            if (!e.IsRepeat) {
                if (_featureRotation is null) BeginFeatureRotation();
                else CommitFeatureRotation();
            }
            e.Handled = true;
        } else if (modifiers == ModifierKeys.None && e.Key == Key.M) {
            if (!e.IsRepeat) {
                if (_featureMove is null) BeginFeatureMove();
                else CommitFeatureMove();
            }
            e.Handled = true;
        } else if (modifiers == ModifierKeys.None && e.Key == Key.Q) {
            if (!e.IsRepeat) OrthogonalizeSelectedFeatures();
            e.Handled = true;
        } else if (modifiers == ModifierKeys.None && e.Key == Key.Insert) {
            AddNodeAtCenter();
            e.Handled = true;
        } else if (modifiers == ModifierKeys.None && e.Key == Key.Delete) {
            DeleteSelectedFeatures();
            e.Handled = true;
        } else if (modifiers == ModifierKeys.None && e.Key == Key.H) {
            HideSelectedFeatures();
            e.Handled = true;
        } else if (modifiers == ModifierKeys.None && e.Key == Key.Escape) {
            CancelActiveInteraction();
            e.Handled = true;
        } else if (e.Key == Key.Add || e.Key == Key.OemPlus) {
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
        if (_featureRotation is not null || _featureMove is not null) return;
        if (!int.TryParse(ZoomTextBox.Text, out var zoom)) return;

        var nextZoom = e.Delta > 0
            ? ClampZoom(zoom + 1)
            : Math.Max(GeoConverter.MinZoom, zoom - 1);
        SetZoomAndRender(nextZoom, e.GetPosition(MapViewport), debounceRender: true);
    }

    private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        MapViewport.Focus();
        if (_featureRotation is not null) {
            CommitFeatureRotation();
            e.Handled = true;
            return;
        }
        if (_featureMove is not null) {
            CommitFeatureMove();
            e.Handled = true;
            return;
        }
        if (_isPanning) {
            e.Handled = true;
            return;
        }

        var position = e.GetPosition(MapViewport);
        if (_editorMode == EditorMode.Select) {
            SelectFeatureAt(position, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
            return;
        }
        if (_editorMode == EditorMode.BoxSelect) {
            _boxSelectionStart = position;
            ShowSelectionRectangle(new Rect(position, position));
            MapViewport.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (_editorMode == EditorMode.DrawLine) {
            if (e.ClickCount >= 2) FinishDraftLine();
            else AddDraftLinePoint(position);
            e.Handled = true;
            return;
        }

        BeginPan(position, MouseButton.Left);
        e.Handled = true;
    }

    private void MapCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e) {
        MapViewport.Focus();
        if (_featureRotation is not null) {
            CancelFeatureRotation();
            e.Handled = true;
            return;
        }
        if (_featureMove is not null) {
            CancelFeatureMove();
            e.Handled = true;
            return;
        }
        if (_boxSelectionStart is not null || _isPanning) {
            e.Handled = true;
            return;
        }

        BeginPan(e.GetPosition(MapViewport), MouseButton.Right);
        e.Handled = true;
    }

    private void BeginPan(Point position, MouseButton button) {
        _isPanning = true;
        _panButton = button;
        _panStart = position;
        _panOffsetX = 0;
        _panOffsetY = 0;
        _lastPanPrefetchOffsetX = 0;
        _lastPanPrefetchOffsetY = 0;
        _lastPanPrefetchAt = DateTime.MinValue;
        MapViewport.CaptureMouse();
        Cursor = Cursors.Hand;
    }

    private void MapCanvas_MouseMove(object sender, MouseEventArgs e) {
        if (_featureRotation is not null) {
            UpdateFeatureRotation(e.GetPosition(MapViewport));
            e.Handled = true;
            return;
        }
        if (_featureMove is not null) {
            UpdateFeatureMove(e.GetPosition(MapViewport));
            e.Handled = true;
            return;
        }
        if (_boxSelectionStart is { } boxStart) {
            ShowSelectionRectangle(new Rect(boxStart, e.GetPosition(MapViewport)));
            return;
        }
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
        if (_boxSelectionStart is { } boxStart) {
            var rect = new Rect(boxStart, e.GetPosition(MapViewport));
            _boxSelectionStart = null;
            SelectionRectangle.Visibility = Visibility.Collapsed;
            MapViewport.ReleaseMouseCapture();
            ApplyBoxSelection(rect, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
            return;
        }
        if (!EndPan(MouseButton.Left)) return;
        e.Handled = true;
    }

    private void MapCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e) {
        if (!EndPan(MouseButton.Right)) return;
        e.Handled = true;
    }

    private bool EndPan(MouseButton button) {
        if (!_isPanning || _panButton != button) return false;

        _isPanning = false;
        _panButton = null;
        if (MapViewport.IsMouseCaptured) MapViewport.ReleaseMouseCapture();
        Cursor = GetEditorModeCursor();

        try {
            if (!int.TryParse(ZoomTextBox.Text, out var zoom)) return true;

            var shiftX = _panOffsetX;
            var shiftY = _panOffsetY;

            if (shiftX == 0 && shiftY == 0) {
                return true;
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

        return true;
    }

    private void MapViewport_SizeChanged(object sender, SizeChangedEventArgs e) {
        ApplyLayerTransforms();
        if (!IsLoaded || _tileLayers.Count == 0 || string.IsNullOrEmpty(_tileService.TileTemplate) || _activeImageLayer?.IsVisible != true) return;
        if (!int.TryParse(ZoomTextBox.Text, out var zoom)) return;

        var isExpanding = e.NewSize.Width > e.PreviousSize.Width || e.NewSize.Height > e.PreviousSize.Height;
        ScheduleRender(zoom, isExpanding ? 0 : ResizeRenderDelayMilliseconds);
    }

    private void PanTool_Click(object sender, RoutedEventArgs e) => SetEditorMode(EditorMode.Pan);

    private void SelectTool_Click(object sender, RoutedEventArgs e) => SetEditorMode(EditorMode.Select);

    private void BoxSelectTool_Click(object sender, RoutedEventArgs e) => SetEditorMode(EditorMode.BoxSelect);

    private void DrawLineTool_Click(object sender, RoutedEventArgs e) => SetEditorMode(EditorMode.DrawLine);

    private void AddNode_Click(object sender, RoutedEventArgs e) => AddNodeAtCenter();

    private void DeleteSelected_Click(object sender, RoutedEventArgs e) => DeleteSelectedFeatures();

    private void HideSelected_Click(object sender, RoutedEventArgs e) => HideSelectedFeatures();

    private void Undo_Click(object sender, RoutedEventArgs e) => UndoLastEdit();

    private void Redo_Click(object sender, RoutedEventArgs e) => RedoLastEdit();

    private void CopySelected_Click(object sender, RoutedEventArgs e) => CopySelectedFeatures();

    private void PasteFeatures_Click(object sender, RoutedEventArgs e) => PasteFeatures();

    private void DuplicateSelected_Click(object sender, RoutedEventArgs e) => DuplicateSelectedFeatures();

    private void RotateSelected_Click(object sender, RoutedEventArgs e) {
        if (_featureRotation is null) BeginFeatureRotation();
        else CommitFeatureRotation();
    }

    private void MoveSelected_Click(object sender, RoutedEventArgs e) {
        if (_featureMove is null) BeginFeatureMove();
        else CommitFeatureMove();
    }

    private void OrthogonalizeSelected_Click(object sender, RoutedEventArgs e) => OrthogonalizeSelectedFeatures();

    private void ShowAllFeatures_Click(object sender, RoutedEventArgs e) {
        if (_document is null) return;
        Editor.Execute(new SetFeatureHiddenCommand(_document.Features, isHidden: false));
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void FeatureDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (_isUpdatingFeatureSelection) return;
        SetSelectedFeatures(FeatureDataGrid.SelectedItems.Cast<MapFeature>(), updateGrid: false);
    }

    private void SetEditorMode(EditorMode mode) {
        if (_editorMode == EditorMode.Rotate && mode != EditorMode.Rotate && _featureRotation is not null) {
            CommitFeatureRotation();
        }
        if (_editorMode == EditorMode.Move && mode != EditorMode.Move && _featureMove is not null) {
            CommitFeatureMove();
        }
        if (_editorMode == EditorMode.DrawLine && mode != EditorMode.DrawLine) FinishDraftLine();
        _editorMode = mode;
        PanToolButton.ClearValue(Control.BackgroundProperty);
        SelectToolButton.ClearValue(Control.BackgroundProperty);
        BoxSelectToolButton.ClearValue(Control.BackgroundProperty);
        DrawLineToolButton.ClearValue(Control.BackgroundProperty);
        var activeButton = mode switch {
            EditorMode.Pan => PanToolButton,
            EditorMode.Select => SelectToolButton,
            EditorMode.BoxSelect => BoxSelectToolButton,
            EditorMode.DrawLine => DrawLineToolButton,
            EditorMode.Rotate => SelectToolButton,
            EditorMode.Move => SelectToolButton,
            _ => SelectToolButton
        };
        activeButton.SetResourceReference(Control.BackgroundProperty, "Theme.SelectionBrush");
        EditorModeTextBlock.Text = mode switch {
            EditorMode.Pan => "拖动模式",
            EditorMode.Select => "选择模式 (S)",
            EditorMode.BoxSelect => "框选工具 (V)",
            EditorMode.DrawLine => "画线模式 (A)",
            EditorMode.Rotate => "旋转模式 (R)",
            EditorMode.Move => "移动模式 (M)",
            _ => ""
        };
        Cursor = GetEditorModeCursor();
    }

    private Cursor GetEditorModeCursor() {
        return _editorMode switch {
            EditorMode.Pan => Cursors.Hand,
            EditorMode.DrawLine => Cursors.Cross,
            EditorMode.BoxSelect => Cursors.Cross,
            EditorMode.Rotate => Cursors.SizeAll,
            EditorMode.Move => Cursors.SizeAll,
            _ => Cursors.Arrow
        };
    }

    private void SelectFeatureAt(Point point, bool extendSelection) {
        if (_document is null || !int.TryParse(ZoomTextBox.Text, out var zoom)) return;
        var candidates = VectorLayer.LastPlan?.Features ?? [];
        var feature = VectorMapInteraction.HitTest(
            candidates,
            point,
            _centerLat,
            _centerLon,
            zoom,
            new Size(MapViewport.ActualWidth, MapViewport.ActualHeight));
        if (feature is null) {
            if (!extendSelection) SetSelectedFeatures([]);
            return;
        }

        var selected = extendSelection ? Selection.Features.ToList() : [];
        if (extendSelection && selected.Remove(feature)) SetSelectedFeatures(selected);
        else {
            selected.Add(feature);
            SetSelectedFeatures(selected);
        }
    }

    private void ApplyBoxSelection(Rect rect, bool extendSelection) {
        if (_document is null || !int.TryParse(ZoomTextBox.Text, out var zoom) || rect.Width < 2 || rect.Height < 2) return;
        var viewport = new Size(MapViewport.ActualWidth, MapViewport.ActualHeight);
        var candidates = VectorLayer.LastPlan?.Features ?? [];
        var selected = VectorMapInteraction.FindWithin(
            candidates,
            rect,
            _centerLat,
            _centerLon,
            zoom,
            viewport).ToList();
        if (extendSelection) selected.AddRange(Selection.Features.Where(feature => !selected.Contains(feature)));
        SetSelectedFeatures(selected);

        var first = VectorMapInteraction.ScreenToGeo(rect.TopLeft, _centerLat, _centerLon, zoom, viewport);
        var second = VectorMapInteraction.ScreenToGeo(rect.BottomRight, _centerLat, _centerLon, zoom, viewport);
        _selectionBounds = new GeoBounds(
            Math.Min(first.Longitude, second.Longitude),
            Math.Min(first.Latitude, second.Latitude),
            Math.Max(first.Longitude, second.Longitude),
            Math.Max(first.Latitude, second.Latitude));
        UpdateDocumentUi();
    }

    private void ShowSelectionRectangle(Rect rect) {
        Canvas.SetLeft(SelectionRectangle, rect.Left);
        Canvas.SetTop(SelectionRectangle, rect.Top);
        SelectionRectangle.Width = rect.Width;
        SelectionRectangle.Height = rect.Height;
        SelectionRectangle.Visibility = Visibility.Visible;
    }

    private void AddDraftLinePoint(Point screenPoint) {
        EnsureDocument();
        if (_document is null || !int.TryParse(ZoomTextBox.Text, out var zoom)) return;
        var point = VectorMapInteraction.ScreenToGeo(
            screenPoint,
            _centerLat,
            _centerLon,
            zoom,
            new Size(MapViewport.ActualWidth, MapViewport.ActualHeight));
        if (!point.IsValid) return;

        if (!Editor.AddDraftLinePoint(point)) return;

        if (Editor.DraftLine is not null) SetSelectedFeatures([Editor.DraftLine]);
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void FinishDraftLine() {
        if (!Editor.HasDraftLine) return;

        var completedLine = Editor.FinishDraftLine();
        SetSelectedFeatures(completedLine is null ? [] : [completedLine]);
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void AddNodeAtCenter() {
        var feature = new MapFeature {
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(_centerLon, _centerLat)]]
        };
        if (!Editor.Execute(new AddFeatureCommand(feature))) return;

        SetSelectedFeatures([feature]);
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void DeleteSelectedFeatures() {
        var selectedFeatures = Selection.Features.ToList();
        if (_document is null || selectedFeatures.Count == 0) return;
        var answer = MessageBox.Show(
            $"确定删除选定的 {selectedFeatures.Count:N0} 个对象吗？",
            "删除对象",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        Editor.CancelDraftLine();
        if (!Editor.Execute(new RemoveFeaturesCommand(selectedFeatures))) return;

        SetSelectedFeatures([]);
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void HideSelectedFeatures() {
        var selectedFeatures = Selection.Features.ToList();
        if (_document is null || selectedFeatures.Count == 0) return;

        if (!Editor.Execute(new SetFeatureHiddenCommand(selectedFeatures, isHidden: true))) return;
        SetSelectedFeatures([]);
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void CopySelectedFeatures() {
        var selectedFeatures = GetSelectedFeaturesInDocumentOrder();
        if (selectedFeatures.Count == 0) return;

        _featureClipboard = MapEditService.CopyFeatures(selectedFeatures);
        _clipboardPasteCount = 0;
    }

    private void PasteFeatures() {
        if (_featureClipboard.Count == 0) return;

        var pasteCount = _clipboardPasteCount + 1;
        if (InsertFeatureCopies(_featureClipboard, pasteCount)) {
            _clipboardPasteCount = pasteCount;
        }
    }

    private void DuplicateSelectedFeatures() {
        var selectedFeatures = GetSelectedFeaturesInDocumentOrder();
        if (selectedFeatures.Count == 0) return;

        InsertFeatureCopies(selectedFeatures, offsetMultiplier: 1);
    }

    private void OrthogonalizeSelectedFeatures() {
        if (_featureRotation is not null || _featureMove is not null) return;
        if (Editor.HasDraftLine) FinishDraftLine();

        var selectedFeatures = GetSelectedFeaturesInDocumentOrder()
            .Where(static feature => feature.GeometryType != MapGeometryType.Point)
            .ToList();
        if (selectedFeatures.Count == 0) return;

        var beforeStates = selectedFeatures.Select(CaptureFeatureParts).ToList();
        var afterStates = beforeStates
            .Select(static state => new FeaturePartsSnapshot(
                state.Feature,
                MapEditService.OrthogonalizeParts(state.Parts)))
            .ToList();
        if (!Editor.Execute(new SetFeaturePartsCommand(beforeStates, afterStates))) return;

        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private bool InsertFeatureCopies(IEnumerable<MapFeature> sourceFeatures, int offsetMultiplier) {
        var document = Editor.EnsureDocument();
        var source = sourceFeatures.ToList();
        if (source.Count == 0) return false;

        var (longitudeOffset, latitudeOffset) = GetFeaturePasteOffset();
        var copies = MapEditService.CreateNewCopies(
            source,
            document.Features.Select(static feature => feature.Id),
            longitudeOffset * offsetMultiplier,
            latitudeOffset * offsetMultiplier);
        if (copies.Count == 0 || !Editor.Execute(new AddFeaturesCommand(copies))) return false;

        RefreshFeatureList();
        SetSelectedFeatures(copies);
        return true;
    }

    private IReadOnlyList<MapFeature> GetSelectedFeaturesInDocumentOrder() {
        if (_document is null || Selection.Count == 0) return [];

        var selectedFeatures = Selection.Features.ToHashSet();
        return _document.Features.Where(selectedFeatures.Contains).ToList();
    }

    private (double Longitude, double Latitude) GetFeaturePasteOffset() {
        var zoom = int.TryParse(ZoomTextBox.Text, out var parsedZoom)
            ? ClampZoom(parsedZoom)
            : GeoConverter.MinZoom;
        var viewport = new Size(
            MapViewport.ActualWidth > 0 ? MapViewport.ActualWidth : 1024,
            MapViewport.ActualHeight > 0 ? MapViewport.ActualHeight : 768);
        var center = new Point(viewport.Width / 2.0, viewport.Height / 2.0);
        var origin = VectorMapInteraction.ScreenToGeo(center, _centerLat, _centerLon, zoom, viewport);
        var shifted = VectorMapInteraction.ScreenToGeo(
            new Point(center.X + FeaturePasteOffsetPixels, center.Y + FeaturePasteOffsetPixels),
            _centerLat,
            _centerLon,
            zoom,
            viewport);

        return (shifted.Longitude - origin.Longitude, shifted.Latitude - origin.Latitude);
    }

    private void BeginFeatureRotation() {
        if (_featureMove is not null) return;
        if (Editor.HasDraftLine) FinishDraftLine();

        var selectedFeatures = GetSelectedFeaturesInDocumentOrder();
        if (_featureRotation is not null || selectedFeatures.Count == 0) return;

        var center = MapEditService.GetGeometryCenter(selectedFeatures);
        if (!center.IsValid) return;

        MapViewport.Focus();
        var pointerAngle = GetRotationPointerAngle(Mouse.GetPosition(MapViewport), center);
        _featureRotation = new FeatureRotation(
            _editorMode,
            center,
            selectedFeatures.Select(CaptureFeatureParts).ToList(),
            pointerAngle);
        SetEditorMode(EditorMode.Rotate);
        MapViewport.CaptureMouse();
        UpdateDocumentUi();
    }

    private void UpdateFeatureRotation(Point pointerPosition) {
        if (_featureRotation is not { } rotation) return;

        var pointerAngle = GetRotationPointerAngle(pointerPosition, rotation.Center);
        rotation.ScreenAngleRadians += NormalizeAngleRadians(pointerAngle - rotation.LastPointerAngleRadians);
        rotation.LastPointerAngleRadians = pointerAngle;
        var angleDegrees = -rotation.ScreenAngleRadians * 180.0 / Math.PI;

        foreach (var state in rotation.OriginalStates) {
            Editor.Dataset.ReplaceParts(
                state.Feature,
                MapEditService.RotateParts(state.Parts, rotation.Center, angleDegrees),
                markDirty: false);
        }
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void CommitFeatureRotation() {
        if (_featureRotation is not { } rotation) return;

        _featureRotation = null;
        if (MapViewport.IsMouseCaptured) MapViewport.ReleaseMouseCapture();
        var afterStates = rotation.OriginalStates.Select(state => CaptureFeatureParts(state.Feature)).ToList();
        var angleDegrees = Math.Abs(rotation.ScreenAngleRadians * 180.0 / Math.PI);
        var committed = angleDegrees >= MinimumCommittedRotationDegrees &&
            Editor.Execute(new SetFeaturePartsCommand(rotation.OriginalStates, afterStates));
        if (!committed) RestoreFeatureRotation(rotation);

        SetEditorMode(rotation.PreviousMode == EditorMode.Rotate ? EditorMode.Select : rotation.PreviousMode);
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void CancelFeatureRotation() {
        if (_featureRotation is not { } rotation) return;

        _featureRotation = null;
        RestoreFeatureRotation(rotation);
        if (MapViewport.IsMouseCaptured) MapViewport.ReleaseMouseCapture();
        SetEditorMode(rotation.PreviousMode == EditorMode.Rotate ? EditorMode.Select : rotation.PreviousMode);
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void RestoreFeatureRotation(FeatureRotation rotation) {
        foreach (var state in rotation.OriginalStates) {
            Editor.Dataset.ReplaceParts(
                state.Feature,
                state.Parts.Select(static part => part.ToList()),
                markDirty: false);
        }
    }

    private FeaturePartsSnapshot CaptureFeatureParts(MapFeature feature) {
        return new FeaturePartsSnapshot(
            feature,
            feature.Parts.Select(static part => (IReadOnlyList<GeoPoint>)part.ToList()).ToList());
    }

    private double GetRotationPointerAngle(Point pointerPosition, GeoPoint center) {
        var viewport = GetMapViewportSize();
        var centerPoint = VectorMapInteraction.GeoToScreen(
            center,
            _centerLat,
            _centerLon,
            int.TryParse(ZoomTextBox.Text, out var zoom) ? ClampZoom(zoom) : GeoConverter.MinZoom,
            viewport);
        return Math.Atan2(pointerPosition.Y - centerPoint.Y, pointerPosition.X - centerPoint.X);
    }

    private Size GetMapViewportSize() {
        return new Size(
            MapViewport.ActualWidth > 0 ? MapViewport.ActualWidth : 1024,
            MapViewport.ActualHeight > 0 ? MapViewport.ActualHeight : 768);
    }

    private static double NormalizeAngleRadians(double radians) {
        while (radians > Math.PI) radians -= Math.PI * 2.0;
        while (radians < -Math.PI) radians += Math.PI * 2.0;
        return radians;
    }

    private void BeginFeatureMove() {
        if (_featureRotation is not null) return;
        if (Editor.HasDraftLine) FinishDraftLine();

        var selectedFeatures = GetSelectedFeaturesInDocumentOrder();
        if (_featureMove is not null || selectedFeatures.Count == 0) return;

        var pointer = Mouse.GetPosition(MapViewport);
        MapViewport.Focus();
        _featureMove = new FeatureMove(
            _editorMode,
            selectedFeatures.Select(CaptureFeatureParts).ToList(),
            pointer,
            GetPointerGeo(pointer));
        SetEditorMode(EditorMode.Move);
        MapViewport.CaptureMouse();
        UpdateDocumentUi();
    }

    private void UpdateFeatureMove(Point pointerPosition) {
        if (_featureMove is not { } move) return;

        move.CurrentPointer = pointerPosition;
        var pointerGeo = GetPointerGeo(pointerPosition);
        var longitudeOffset = pointerGeo.Longitude - move.StartPointerGeo.Longitude;
        var latitudeOffset = pointerGeo.Latitude - move.StartPointerGeo.Latitude;

        foreach (var state in move.OriginalStates) {
            Editor.Dataset.ReplaceParts(
                state.Feature,
                MapEditService.MoveParts(state.Parts, longitudeOffset, latitudeOffset),
                markDirty: false);
        }
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void CommitFeatureMove() {
        if (_featureMove is not { } move) return;

        _featureMove = null;
        if (MapViewport.IsMouseCaptured) MapViewport.ReleaseMouseCapture();
        var afterStates = move.OriginalStates.Select(state => CaptureFeatureParts(state.Feature)).ToList();
        var pointerDelta = move.CurrentPointer - move.StartPointer;
        var committed = pointerDelta.Length >= MinimumCommittedMovePixels &&
            Editor.Execute(new SetFeaturePartsCommand(move.OriginalStates, afterStates));
        if (!committed) RestoreFeatureMove(move);

        SetEditorMode(move.PreviousMode == EditorMode.Move ? EditorMode.Select : move.PreviousMode);
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void CancelFeatureMove() {
        if (_featureMove is not { } move) return;

        _featureMove = null;
        RestoreFeatureMove(move);
        if (MapViewport.IsMouseCaptured) MapViewport.ReleaseMouseCapture();
        SetEditorMode(move.PreviousMode == EditorMode.Move ? EditorMode.Select : move.PreviousMode);
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void RestoreFeatureMove(FeatureMove move) {
        foreach (var state in move.OriginalStates) {
            Editor.Dataset.ReplaceParts(
                state.Feature,
                state.Parts.Select(static part => part.ToList()),
                markDirty: false);
        }
    }

    private GeoPoint GetPointerGeo(Point pointerPosition) {
        return VectorMapInteraction.ScreenToGeo(
            pointerPosition,
            _centerLat,
            _centerLon,
            int.TryParse(ZoomTextBox.Text, out var zoom) ? ClampZoom(zoom) : GeoConverter.MinZoom,
            GetMapViewportSize());
    }

    private void UndoLastEdit() {
        if (Editor.HasDraftLine) {
            Editor.CancelDraftLine();
            SetSelectedFeatures([]);
            RefreshFeatureList();
            UpdateVectorLayer();
            UpdateDocumentUi();
            return;
        }

        if (!Editor.Undo()) return;
        PruneSelectionToDocument();
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void RedoLastEdit() {
        if (Editor.HasDraftLine || !Editor.Redo()) return;

        PruneSelectionToDocument();
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void PruneSelectionToDocument() {
        if (_document is null) {
            SetSelectedFeatures([]);
            return;
        }

        SetSelectedFeatures(Selection.Features.Where(_document.Features.Contains).ToList());
    }

    private void SetSelectedFeatures(IEnumerable<MapFeature> features, bool updateGrid = true) {
        Selection.Set(features);

        if (updateGrid) {
            _isUpdatingFeatureSelection = true;
            try {
                FeatureDataGrid.SelectedItems.Clear();
                foreach (var feature in Selection.Features.Take(100)) FeatureDataGrid.SelectedItems.Add(feature);
                if (Selection.Count > 0) FeatureDataGrid.ScrollIntoView(Selection.Features.First());
            } finally {
                _isUpdatingFeatureSelection = false;
            }
        }
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void EnsureDocument() {
        Editor.EnsureDocument();
    }

    private void FitDocumentToViewport() {
        if (_document is null || !_document.Bounds.IsValid) return;
        var bounds = _document.Bounds;
        var center = bounds.Center;
        _centerLon = center.Longitude;
        _centerLat = GeoConverter.ClampLatitude(center.Latitude);
        var viewport = new Size(
            Math.Max(320, MapViewport.ActualWidth),
            Math.Max(240, MapViewport.ActualHeight));
        var zoom = VectorMapInteraction.GetFitZoom(bounds, viewport, Math.Min(GeoConverter.MaxZoom, 20));
        ZoomTextBox.Text = ClampZoom(zoom).ToString();
        ApplyLayerTransforms();
        if (_tileLayers.Count > 0) ScheduleRender(zoom, 0);
    }

    private void RefreshFeatureList() {
        _isUpdatingFeatureSelection = true;
        try {
            FeatureDataGrid.ItemsSource = null;
            FeatureDataGrid.ItemsSource = _document?.Features;
        } finally {
            _isUpdatingFeatureSelection = false;
        }
    }

    private void UpdateVectorLayer() {
        if (VectorLayer is null || !int.TryParse(ZoomTextBox.Text, out var zoom)) return;

        var dataLayer = GetVectorDataLayer();
        var opacity = dataLayer?.IsVisible == false
            ? 0.0
            : Math.Clamp(dataLayer?.Opacity ?? 1.0, 0.0, 1.0);
        VectorLayer.Opacity = opacity;
        VectorLayer.Visibility = opacity <= 0 ? Visibility.Hidden : Visibility.Visible;
        VectorLayer.UpdateView(_document, _centerLat, _centerLon, zoom, _panOffsetX, _panOffsetY);
    }

    private MapImageLayer? GetVectorDataLayer() {
        return _settings.ImageLayers.FirstOrDefault(static layer => layer.Kind == MapLayerKind.Data && layer.IsPrimary) ??
            _settings.ImageLayers.FirstOrDefault(static layer => layer.Kind == MapLayerKind.Data);
    }

    private void UpdateDocumentUi() {
        var total = _document?.Features.Count ?? 0;
        var hidden = _document?.Features.Count(static feature => feature.IsHidden) ?? 0;
        FeatureCountTextBlock.Text = hidden > 0 ? $"{total:N0} / 隐藏 {hidden:N0}" : $"{total:N0}";
        if (_document is null) {
            DocumentStatusTextBlock.Text = "未打开地图数据";
            return;
        }

        var dirty = _document.IsDirty ? " *" : "";
        var selection = Selection.Count > 0 ? $"，选中 {Selection.Count:N0}" : "";
        var area = _selectionBounds.HasValue ? "，已框选下载区域" : "";
        var skipped = _document.SkippedFeatureCount > 0 ? $"，跳过 {_document.SkippedFeatureCount:N0}" : "";
        DocumentStatusTextBlock.Text = $"{_document.Name}{dirty}：{total:N0} 个要素{selection}{area}{skipped}";
    }

    private bool ConfirmDiscardChanges(string message) {
        if (_document?.IsDirty != true &&
            !Editor.HasDraftLine &&
            _featureRotation is null &&
            _featureMove is null) {
            return true;
        }
        return MessageBox.Show(
            message,
            "未保存的修改",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private void CancelActiveInteraction() {
        if (_featureRotation is not null) {
            CancelFeatureRotation();
            return;
        }
        if (_featureMove is not null) {
            CancelFeatureMove();
            return;
        }

        _boxSelectionStart = null;
        _isPanning = false;
        _panButton = null;
        _panOffsetX = 0;
        _panOffsetY = 0;
        _lastPanPrefetchOffsetX = 0;
        _lastPanPrefetchOffsetY = 0;
        SelectionRectangle.Visibility = Visibility.Collapsed;
        if (MapViewport.IsMouseCaptured) MapViewport.ReleaseMouseCapture();
        ApplyLayerTransforms();
        if (Editor.CancelDraftLine()) {
            SetSelectedFeatures([]);
            RefreshFeatureList();
            UpdateVectorLayer();
        }
        SetEditorMode(EditorMode.Select);
    }

    private async Task ExecuteOsmPluginCommandAsync(string commandId) {
        if (_pluginHost is null) return;

        try {
            var result = await _pluginHost.ExecuteCommandAsync(
                BuiltInPluginCatalog.OsmTransferPluginId,
                commandId,
                new {
                    bounds = _selectionBounds,
                    documentName = _document?.Name,
                    sourcePath = _document?.SourcePath
                });
            foreach (var action in result.Actions) await ApplyPluginActionAsync("OpenStreetMap 传输", action);
        } catch (Exception ex) {
            Logger.Error($"OSM plugin command '{commandId}' failed", ex);
            MessageBox.Show(ex.Message, "OpenStreetMap 传输", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private Task DownloadOsmSelectionAsync() {
        var downloadWindow = new Views.OsmDownloadWindow(DownloadOsmBoundsAsync) { Owner = this };
        downloadWindow.ShowDialog();
        return Task.CompletedTask;
    }

    private async Task<bool> DownloadOsmBoundsAsync(
        GeoBounds bounds,
        IProgress<OsmDownloadStage> progress,
        CancellationToken ct) {
        if (!ConfirmDiscardChanges("下载的数据将替换当前未保存的地图。是否放弃当前修改？")) return false;

        string? temporaryPath = null;
        try {
            var apiBaseUrl = _osmAccountStore.GetActive()?.ApiBaseUrl ?? OsmApiClient.DefaultApiBaseUrl;
            var bytes = await new OsmApiClient(OsmHttpClient).DownloadMapAsync(apiBaseUrl, bounds, progress, ct);
            progress.Report(OsmDownloadStage.Importing);
            var temporaryDirectory = Path.Combine(Path.GetTempPath(), "WPF-OpenStreetmap-Editor");
            Directory.CreateDirectory(temporaryDirectory);
            temporaryPath = Path.Combine(temporaryDirectory, $"osm-download-{Guid.NewGuid():N}.osm");
            await File.WriteAllBytesAsync(temporaryPath, bytes, ct);
            var document = await SpatialDataService.ImportAsync(temporaryPath);
            document.Name = $"OSM 下载 {DateTime.Now:yyyy-MM-dd HHmm}";
            document.SourcePath = null;
            document.SourceFormat = SpatialFileFormat.OsmXml;
            document.MarkClean();
            _document = document;
            _selectionBounds = null;
            SetSelectedFeatures([]);
            FitDocumentToViewport();
            RefreshFeatureList();
            UpdateVectorLayer();
            UpdateDocumentUi();
            return true;
        } finally {
            if (temporaryPath is not null && File.Exists(temporaryPath)) {
                try {
                    File.Delete(temporaryPath);
                } catch (IOException ex) {
                    Logger.Error("Failed to remove temporary OSM download", ex);
                }
            }
        }
    }

    private async Task UploadOsmChangesAsync() {
        if (_featureRotation is not null) CommitFeatureRotation();
        if (_featureMove is not null) CommitFeatureMove();
        if (Editor.HasDraftLine) FinishDraftLine();

        if (_document is null) {
            MessageBox.Show("当前没有可上传的地图数据。", "上传到 OSM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var account = _osmAccountStore.GetActive();
        if (account is null) {
            new Views.OsmAccountsWindow(_osmAccountStore) { Owner = this }.ShowDialog();
            account = _osmAccountStore.GetActive();
            if (account is null) return;
        }
        var credential = _osmAccountStore.GetCredential(account);
        if (credential is null) {
            MessageBox.Show("当前 OSM 账号没有保存凭据。", "上传到 OSM", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var preview = OsmChangeSerializer.Build(_document, 0);
        if (preview.TotalCount == 0) {
            MessageBox.Show("当前地图没有可上传的变更。", "上传到 OSM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var uploadWindow = new Views.OsmUploadWindow(account, preview, () => OsmChangeSerializer.Build(_document, 0)) { Owner = this };
        var uploadConfirmed = uploadWindow.ShowDialog() == true;
        if (uploadWindow.MetadataChanged) {
            _document.IsDirty = true;
            RefreshFeatureList();
            UpdateVectorLayer();
            UpdateDocumentUi();
        }
        if (!uploadConfirmed) return;

        var api = new OsmApiClient(OsmHttpClient);
        long? changesetId = null;
        try {
            IsEnabled = false;
            DocumentStatusTextBlock.Text = "正在创建 OSM 变更集...";
            changesetId = await api.CreateChangesetAsync(
                account.ApiBaseUrl,
                credential,
                uploadWindow.Comment,
                uploadWindow.Source,
                uploadWindow.ReviewRequested,
                CancellationToken.None);
            var changes = OsmChangeSerializer.Build(_document, changesetId.Value);
            DocumentStatusTextBlock.Text = $"正在上传 {changes.TotalCount:N0} 个变更...";
            var response = await api.UploadChangesAsync(
                account.ApiBaseUrl,
                credential,
                changesetId.Value,
                changes.Xml);
            OsmChangeSerializer.ApplyDiffResult(_document, changes, response);
            Editor.CommandStack.Clear();
            RefreshFeatureList();
            UpdateVectorLayer();
            UpdateDocumentUi();
            MessageBox.Show($"已上传到 OSM 变更集 {changesetId.Value}。", "上传完成", MessageBoxButton.OK, MessageBoxImage.Information);
        } finally {
            if (changesetId.HasValue) {
                try {
                    await api.CloseChangesetAsync(account.ApiBaseUrl, credential, changesetId.Value, CancellationToken.None);
                } catch (Exception ex) {
                    Logger.Error($"Failed to close OSM changeset {changesetId.Value}", ex);
                }
            }
            IsEnabled = true;
        }
    }

    private void RefreshRenderedLayerFromStack(MapImageLayer? selectedLayer = null) {
        AppSettingsService.EnsureSinglePrimaryLayer(_settings);
        var rasterLayers = LayerRenderPlanner.GetLayersToRender(_settings.ImageLayers);
        RefreshLayerList(selectedLayer ?? _settings.GetActiveLayer());
        LoadImageLayers(rasterLayers);
    }

    private void CancelPendingMapWork() {
        _tileSourceCts?.Cancel();
        _layerStackRefreshCts?.Cancel();
        _layerStackRefreshCts = null;
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

        UpdateSelectedLayerDetails();
    }

    private void UpdateSelectedLayerDetails() {
        _isUpdatingLayerDetails = true;
        try {
            var layer = LayerListBox.SelectedItem as MapImageLayer;
            SelectedLayerOptionsPanel.IsEnabled = layer is not null;
            SelectedLayerVisibilityCheckBox.IsChecked = layer?.IsVisible == true;
            var opacity = Math.Clamp(layer?.Opacity ?? 1.0, 0.0, 1.0);
            LayerOpacitySlider.Value = opacity * 100.0;
            LayerOpacityValueTextBlock.Text = FormatLayerOpacityPercent(opacity);
        } finally {
            _isUpdatingLayerDetails = false;
        }
    }

    private void ScheduleLayerStackRefresh(MapImageLayer selectedLayer) {
        _layerStackRefreshCts?.Cancel();
        var refreshCts = new CancellationTokenSource();
        _layerStackRefreshCts = refreshCts;

        _ = Task.Run(async () => {
            try {
                await Task.Delay(LayerStackRefreshDelayMilliseconds, refreshCts.Token).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() => {
                    if (refreshCts.IsCancellationRequested || !ReferenceEquals(_layerStackRefreshCts, refreshCts)) return;

                    RefreshRenderedLayerFromStack(selectedLayer);
                });
            } catch (OperationCanceledException) {
            }
        });
    }

    private static string FormatLayerOpacityPercent(double opacity) {
        return $"{Math.Clamp(opacity, 0.0, 1.0) * 100.0:0}%";
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

    private static T? FindVisualAncestor<T>(DependencyObject? element) where T : DependencyObject {
        while (element is not null) {
            if (element is T match) return match;
            element = VisualTreeHelper.GetParent(element);
        }

        return null;
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
            ZoomLimitTextBlock.Text = "";
            UpdateAttribution();
            return;
        }

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

    private enum EditorMode {
        Pan,
        Select,
        BoxSelect,
        DrawLine,
        Rotate,
        Move
    }

    private sealed class FeatureRotation {
        public FeatureRotation(
            EditorMode previousMode,
            GeoPoint center,
            IReadOnlyList<FeaturePartsSnapshot> originalStates,
            double lastPointerAngleRadians) {
            PreviousMode = previousMode;
            Center = center;
            OriginalStates = originalStates;
            LastPointerAngleRadians = lastPointerAngleRadians;
        }

        public EditorMode PreviousMode { get; }

        public GeoPoint Center { get; }

        public IReadOnlyList<FeaturePartsSnapshot> OriginalStates { get; }

        public double LastPointerAngleRadians { get; set; }

        public double ScreenAngleRadians { get; set; }
    }

    private sealed class FeatureMove {
        public FeatureMove(
            EditorMode previousMode,
            IReadOnlyList<FeaturePartsSnapshot> originalStates,
            Point startPointer,
            GeoPoint startPointerGeo) {
            PreviousMode = previousMode;
            OriginalStates = originalStates;
            StartPointer = startPointer;
            CurrentPointer = startPointer;
            StartPointerGeo = startPointerGeo;
        }

        public EditorMode PreviousMode { get; }

        public IReadOnlyList<FeaturePartsSnapshot> OriginalStates { get; }

        public Point StartPointer { get; }

        public Point CurrentPointer { get; set; }

        public GeoPoint StartPointerGeo { get; }
    }

    private static double ClampWorldPixel(double value, double worldSize) {
        return Math.Clamp(value, 0, worldSize);
    }
}
