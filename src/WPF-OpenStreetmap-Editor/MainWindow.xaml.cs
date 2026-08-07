using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    private readonly WorkbenchViewModel _viewModel = new();
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
            if ((_featureRotation is not null || _featureMove is not null || _vertexMove is not null || _segmentExtrude is not null) &&
                MapViewport.IsMouseCaptured) {
                MapViewport.ReleaseMouseCapture();
            }
            _featureRotation = null;
            _featureMove = null;
            _vertexMove = null;
            _segmentExtrude = null;
            _keyboardEditCommand = "";
            Editor.ReplaceDocument(value);
            SynchronizeActiveDataLayer();
            _featureClipboard = [];
            _previousSingleSelectedFeature = null;
            _clipboardPasteCount = 0;
        }
    }

    private IReadOnlyList<MapFeature> _featureClipboard = [];
    private int _clipboardPasteCount;
    private FeatureRotation? _featureRotation;
    private FeatureMove? _featureMove;
    private VertexMove? _vertexMove;
    private SegmentExtrude? _segmentExtrude;
    private SegmentExtrudeConstraint _pendingSegmentExtrudeConstraint = SegmentExtrudeConstraint.Free;
    private MapFeature? _hoveredFeature;
    private VertexHit? _hoveredVertex;
    private long _selectionRevision;
    private string _keyboardEditCommand = "";
    private MapFeature? _previousSingleSelectedFeature;
    private bool _followDrawingViewport;
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
    private CancellationTokenSource? _autosaveCts;
    private readonly DispatcherTimer _autosaveTimer = new();
    private MapLayer? _activeLayer;
    private MapLayer? _fallbackLayer;
    private MapLayer? _stagingLayer;
    private string? _aiTagFeatureId;
    private CancellationTokenSource? _aiTagCts;
    private IReadOnlyList<AiTagSuggestionItem> _aiTagSuggestions = [];
    private AppSettings _settings = AppSettingsService.Load();
    private readonly AppSettingsSaveController _settingsWriter = new();
    private readonly PresetService _presets = PresetService.Instance;
    private bool _isAutosaving;
    private MapDisplayTransform _displayTransform = MapDisplayTransform.Identity;
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
    private const double MinimumPromotableTileCoverage = 0.98;
    private const double FeaturePasteOffsetPixels = 24.0;
    private const double MinimumCommittedRotationDegrees = 0.01;
    private const double MinimumCommittedMovePixels = 0.5;
    private const double DefaultKeyboardExtrudeDistanceDecimeters = 10.0;
    private const string OsmTransferPluginId = "org.openstreetmap.transfer";
    private const string OsmTransferPluginName = "OpenStreetMap transfer";
    private const double PanPrefetchDistance = GeoConverter.TileSize * 0.75;
    private static readonly SemaphoreSlim TileThrottle = new(MaxConcurrentTileLoads, MaxConcurrentTileLoads);
    private static readonly HttpClient OsmHttpClient = new() { Timeout = TimeSpan.FromMinutes(3) };
    private static LocalizationService L => LocalizationService.Instance;
    private static readonly BetterIdAiClient BetterIdAi = new(OsmHttpClient);

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
        DataContext = _viewModel;
        ThemeService.ApplyWindowTheme(this);
        AppSettingsService.EnsureDefaults(_settings);
        _viewModel.RightPanelWidth = _settings.WorkbenchLayout.RightPanelWidth;
        _viewModel.IsRightPanelVisible = _settings.WorkbenchLayout.IsRightPanelVisible;
        RightPanelColumn.Width = _settings.WorkbenchLayout.IsRightPanelVisible
            ? new GridLength(_viewModel.RightPanelWidth)
            : new GridLength(0);
        RightPanel.Visibility = _settings.WorkbenchLayout.IsRightPanelVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        RightPanelSplitter.Visibility = RightPanel.Visibility;
        ApplyTileCacheSettings(forceDiskMaintenance: false);
        _displayTransform = CreateDisplayTransform(_settings);
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
            _autosaveTimer.Stop();
            _autosaveCts?.Cancel();
            _tileSourceCts?.Cancel();
            _aiTagCts?.Cancel();
            _layerStackRefreshCts?.Cancel();
            _renderDebounceCts?.Cancel();
            _renderCts?.Cancel();
            TileImageLoader.Shared.Clear();
            DisposeTileLayers(_tileLayers);
            _tileLayers = [];
        };

        Loaded += MainWindow_Loaded;
        _autosaveTimer.Tick += AutosaveTimer_Tick;
        ConfigureAutosaveTimer();
        SetEditorMode(EditorMode.Select);
        UpdateDocumentUi();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e) {
        RefreshRenderedLayerFromStack(_settings.GetActiveLayer());
        UpdateVectorLayer();
        RefreshPluginMenus();
        RefreshPluginToolbar();
        RebuildPresetToolbar();
        try {
            _nonTextInputImeGuard ??= NonTextInputImeGuard.Attach(this);
        } catch (Exception ex) {
            Logger.Error("Failed to enable non-text input IME guard", ex);
        }
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
            L.Format("Main.UpdateAvailableMessage", latest.Version, _startupUpdateCheck.CurrentVersion),
            L.GetString("Main.UpdateAvailableTitle"),
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
        if (!ConfirmDiscardChanges(L.GetString("Main.Discard.Exit"))) {
            e.Cancel = true;
            return;
        }

        if (RightPanelColumn.ActualWidth > 0) {
            _settings.WorkbenchLayout.RightPanelWidth = RightPanelColumn.ActualWidth;
        }
        _settings.WorkbenchLayout.IsRightPanelVisible = RightPanel.Visibility == Visibility.Visible;
        AppSettingsService.Save(_settings);
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

        var source = GetOrCreateCustomSource(L.GetString("Main.CustomSource"), url.Trim());
        AddImageLayer(source);
    }

    public void LoadLayer(string type, string url) {
        if (string.IsNullOrWhiteSpace(url)) return;

        var normalizedSource = NormalizeLayerSource(type, url);
        var source = GetOrCreateCustomSource(L.GetString("Main.CustomLayer"), normalizedSource);
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
        _settingsWriter.Save(_settings, this);
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
        _settingsWriter.Save(_settings, this);
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
                var tileService = new TileService(cacheMaxAge: GetTileCacheMaxAge());
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
            _settingsWriter.Save(_settings, this);

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
        if (!HasUsableTileCoverage(loadedLayers, viewportW, viewportH, z)) {
            return;
        }

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
            return new TileLayerLoadResult(new TileRenderGroup([], context.Opacity), 0, 0);
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
            TileRenderLayout.EnumerateRequestsByDistance(tileRange, sourceCenterPixelX, sourceCenterPixelY)
                .Select(static request => (request.X, request.Y)));
        var requestedTileCount = pendingRequests.Count;
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
                loadedTileCount,
                requestedTileCount);
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
        var allowLowerZoomFallback = context.Source.NoTileEtags.Count > 0 || context.Source.NoTileMd5s.Count > 0;
        var minZoom = allowLowerZoomFallback ? context.Service.ImageMinZoom : requestedZoom;
        for (var zoom = requestedZoom; zoom >= minZoom; zoom--) {
            var shift = requestedZoom - zoom;
            var candidateX = shift == 0 ? tileX : tileX >> shift;
            var candidateY = shift == 0 ? tileY : tileY >> shift;
            await TileThrottle.WaitAsync(ct).ConfigureAwait(false);
            BitmapSource? source;
            try {
                source = await TileImageLoader.Shared
                    .LoadAsync(context.Service, zoom, candidateX, candidateY, context.Source.AccessToken, ct)
                    .ConfigureAwait(false);
            } finally {
                TileThrottle.Release();
            }

            if (source is not null) {
                return new LoadedTile(source, zoom, candidateX, candidateY, zoom < requestedZoom);
            }

            if (zoom == requestedZoom && !allowLowerZoomFallback) {
                return null;
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

    private int GetTileWorkerCount(TileSourcePreset source) {
        if (IsOpenStreetMapVolunteerSource(source)) return 2;

        return _settings.TilePerformanceMode == TilePerformanceMode.MemorySaver
            ? Math.Min(4, MaxConcurrentTileLoads)
            : MaxConcurrentTileLoads;
    }

    private bool ShouldPrefetch(TileSourcePreset source) {
        return _settings.TilePerformanceMode == TilePerformanceMode.Responsive &&
            !IsOpenStreetMapVolunteerSource(source);
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
            TileRenderLayout.EnumerateRequestsByDistance(prefetchRange, centerPixelX, centerPixelY)
                .Where(tile => !TileRenderLayout.Contains(renderRange, tile.X, tile.Y))
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

    private static bool HasUsableTileCoverage(
        IReadOnlyList<TileLayerLoadResult> loadedLayers,
        double viewportWidth,
        double viewportHeight,
        int renderZoom) {
        var visibleLayers = loadedLayers
            .Where(static result => result.Group.Opacity > 0)
            .ToList();
        var loadedTileCount = visibleLayers.Sum(static result => result.TileCount);
        var requestedTileCount = visibleLayers.Sum(static result => result.RequestedTileCount);
        if (loadedTileCount == 0 || requestedTileCount == 0) return false;

        var coverage = TileRenderLayout.GetViewportCoverage(
            visibleLayers.SelectMany(static result => result.Group.Tiles.Select(static tile => tile.Placement)),
            viewportWidth,
            viewportHeight);
        if (coverage >= MinimumPromotableTileCoverage) return true;

        var worldSize = GeoConverter.TileSize * (double)GeoConverter.GetTileCount(renderZoom);
        return loadedTileCount >= requestedTileCount &&
            (worldSize <= viewportWidth || worldSize <= viewportHeight);
    }

    private void New_Click(object sender, RoutedEventArgs e) {
        if (!ConfirmDiscardChanges(L.GetString("Main.Discard.New"))) return;

        _document = new MapDocument();
        _document.MarkClean();
        _selectionBounds = null;
        SetSelectedFeatures([]);
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private async void Open_Click(object sender, RoutedEventArgs e) {
        if (!ConfirmDiscardChanges(L.GetString("Main.Discard.Open"))) return;

        var dialog = new OpenFileDialog {
            Title = L.GetString("Main.ImportDialogTitle"),
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

    private async Task ExportGpxAsync() {
        if (!PrepareDocumentForWrite()) return;
        if (_document is null) return;

        var path = DocumentExportPathService.CreateSiblingPath(_document, ".gpx") ??
            ChooseExportPath(_document, "GPX|*.gpx", ".gpx");
        if (string.IsNullOrWhiteSpace(path) || !ConfirmOverwriteExport(path)) return;

        await WriteDocumentSnapshotAsync(path);
    }

    private async Task ExportToParentPathAsync() {
        if (!PrepareDocumentForWrite()) return;
        if (_document is null) return;

        var path = DocumentExportPathService.CreateParentPath(_document);
        if (string.IsNullOrWhiteSpace(path)) {
            path = ChooseSavePath(_document);
        }
        if (string.IsNullOrWhiteSpace(path) || !ConfirmOverwriteExport(path)) return;

        await WriteDocumentSnapshotAsync(path);
    }

    private async Task ImportDocumentAsync(string path) {
        try {
            IsEnabled = false;
            _viewModel.DocumentStatus = L.Format("Main.Status.Importing", Path.GetFileName(path));
            var progress = new Progress<SpatialImportProgress>(update =>
                _viewModel.DocumentStatus = L.Format("Main.Status.ImportProgress", update.Stage, update.FeaturesRead));
            var document = await SpatialDataService.ImportAsync(
                path,
                new SpatialImportOptions {
                    SourceProjectionId = _settings.DefaultImportProjectionId,
                    CustomProjectionWkt = _settings.CustomImportProjectionWkt
                },
                progress);
            _document = document;
            _selectionBounds = null;
            SetSelectedFeatures([]);
            FitDocumentToViewport();
            RefreshFeatureList();
            UpdateVectorLayer();
            UpdateDocumentUi();
        } catch (Exception ex) {
            Logger.Error($"Failed to import spatial data '{path}'", ex);
            MessageBox.Show(ex.Message, L.GetString("Main.ImportErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        } finally {
            IsEnabled = true;
        }
    }

    private async Task SaveDocumentAsync(bool forceSaveAs) {
        if (!PrepareDocumentForWrite()) return;
        if (_document is null) return;

        var path = forceSaveAs || string.IsNullOrWhiteSpace(_document.SourcePath) ||
            _document.SourceFormat is SpatialFileFormat.OsmPbf or SpatialFileFormat.Shapefile or SpatialFileFormat.Kmz
                ? ChooseSavePath(_document)
                : _document.SourcePath;
        if (string.IsNullOrWhiteSpace(path)) return;

        try {
            IsEnabled = false;
            _viewModel.DocumentStatus = L.Format("Main.Status.Saving", Path.GetFileName(path));
            if (_settings.KeepBackupFileOnSave) {
                await DocumentBackupService.SaveKeepBackupAsync(path);
            }
            await SpatialDataService.SaveAsync(_document, path);
            Editor.CommandStack.Clear();
            RefreshFeatureList();
            UpdateDocumentUi();
        } catch (Exception ex) {
            Logger.Error($"Failed to save spatial data '{path}'", ex);
            MessageBox.Show(ex.Message, L.GetString("Main.SaveErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        } finally {
            IsEnabled = true;
        }
    }

    private string? ChooseSavePath(MapDocument document) {
        var dialog = new SaveFileDialog {
            Title = L.GetString("Main.SaveDialogTitle"),
            Filter = SpatialDataService.SaveFileFilter,
            FileName = DocumentExportPathService.CreateDefaultFileName(document, ".geojson"),
            AddExtension = true,
            DefaultExt = ".geojson",
            OverwritePrompt = true
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? ChooseExportPath(MapDocument document, string filter, string defaultExtension) {
        var dialog = new SaveFileDialog {
            Title = L.GetString("Main.SaveDialogTitle"),
            Filter = filter,
            FileName = DocumentExportPathService.CreateDefaultFileName(document, defaultExtension),
            AddExtension = true,
            DefaultExt = defaultExtension,
            OverwritePrompt = true
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private bool PrepareDocumentForWrite() {
        if (_featureRotation is not null) CommitFeatureRotation();
        if (_featureMove is not null) CommitFeatureMove();
        if (_vertexMove is not null) CommitVertexMove();
        if (_segmentExtrude is not null) CommitSegmentExtrude();
        if (Editor.HasDraftLine) FinishDraftLine();

        if (_document is not null) return true;

        MessageBox.Show(L.GetString("Main.NoMapToSave"), L.GetString("Main.SaveDialogTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private bool ConfirmOverwriteExport(string path) {
        if (!File.Exists(path)) return true;

        return MessageBox.Show(
            $"Overwrite {Path.GetFileName(path)}?",
            L.GetString("Main.SaveDialogTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private async Task WriteDocumentSnapshotAsync(string path) {
        if (_document is null) return;

        try {
            IsEnabled = false;
            _viewModel.DocumentStatus = L.Format("Main.Status.Saving", Path.GetFileName(path));
            await SpatialDataService.WriteSnapshotAsync(_document, path);
            RefreshFeatureList();
            UpdateDocumentUi();
        } catch (Exception ex) {
            Logger.Error($"Failed to export spatial data '{path}'", ex);
            MessageBox.Show(ex.Message, L.GetString("Main.SaveErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        } finally {
            IsEnabled = true;
        }
    }

    private void ConfigureAutosaveTimer() {
        _autosaveTimer.Stop();
        var intervalSeconds = Math.Clamp(
            _settings.AutosaveIntervalSeconds,
            AppSettings.MinAutosaveIntervalSeconds,
            AppSettings.MaxAutosaveIntervalSeconds);
        _autosaveTimer.Interval = TimeSpan.FromSeconds(intervalSeconds);
        if (_settings.AutosaveEnabled) {
            _autosaveTimer.Start();
        }
    }

    private async void AutosaveTimer_Tick(object? sender, EventArgs e) {
        if (_isAutosaving || _document?.IsDirty != true || Editor.HasDraftLine) return;

        _isAutosaving = true;
        _autosaveCts?.Cancel();
        _autosaveCts = new CancellationTokenSource();
        try {
            var paths = await DocumentBackupService.SaveDirtyAutosavesAsync(
                _document,
                _settings.AutosaveFilesPerLayer,
                _autosaveCts.Token);
            if (_settings.NotifyOnEverySave && paths.Count > 0) {
                _viewModel.DocumentStatus = L.Format("Main.Status.Autosaved", Path.GetFileName(paths[^1]));
            }
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            Logger.Error("Failed to autosave map data", ex);
            _viewModel.DocumentStatus = L.Format("Main.Status.AutosaveFailed", ex.Message);
        } finally {
            _isAutosaving = false;
        }
    }

    private void Layer_Click(object sender, RoutedEventArgs e) {
        var win = new Views.LayersWindow { Owner = this };
        win.ShowDialog();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) {
        ShowSettings(Views.SettingsSection.Appearance);
    }

    private void DataValidator_Click(object sender, RoutedEventArgs e) {
        var win = new Views.DataValidatorWindow(
            Editor,
            Selection.Features,
            feature => {
                SetSelectedFeatures([feature]);
                CenterViewportOnDocumentPoint(feature.Bounds.Center);
            }) { Owner = this };
        win.ShowDialog();
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void ImagerySettings_Click(object sender, RoutedEventArgs e) {
        ShowSettings(Views.SettingsSection.Sources);
    }

    private void ShowSettings(Views.SettingsSection initialSection) {
        var win = new Views.SettingsWindow(_settings, initialSection) { Owner = this };
        if (win.ShowDialog() != true) return;

        var settings = win.ResultSettings;
        AppSettingsService.EnsureDefaults(settings);
        if (!_settingsWriter.Save(settings, this)) {
            ThemeService.ApplyTheme(_settings.ThemeId);
            LocalizationService.Instance.ApplyLanguage(_settings.LanguageId);
            return;
        }

        _settings = settings;
        _displayTransform = CreateDisplayTransform(_settings);
        ThemeService.ApplyTheme(_settings.ThemeId);
        LocalizationService.Instance.ApplyLanguage(_settings.LanguageId);
        ApplyTileCacheSettings(forceDiskMaintenance: true);
        ConfigureAutosaveTimer();
        RefreshImageryMenu();
        var activeLayer = _settings.GetActiveLayer();
        RefreshLocalizedText();
        RefreshRenderedLayerFromStack(activeLayer);
        UpdateVectorLayer();
    }

    private static MapDisplayTransform CreateDisplayTransform(AppSettings settings) {
        try {
            return MapDisplayTransform.Create(new MapDisplayAlignmentOptions {
                ProjectionId = settings.DisplayAlignmentProjectionId,
                CustomProjectionWkt = settings.CustomDisplayAlignmentProjectionWkt,
                OffsetX = settings.DisplayAlignmentOffsetX,
                OffsetY = settings.DisplayAlignmentOffsetY
            });
        } catch (InvalidDataException ex) {
            Logger.Error("Failed to create map display alignment", ex);
            return MapDisplayTransform.Identity;
        }
    }

    private void ApplyTileCacheSettings(bool forceDiskMaintenance) {
        TileImageLoader.ConfigureShared(_settings.TilePerformanceMode);
        TileDiskCache.ScheduleMaintenance(
            AppPaths.TileCacheDirectory,
            TileDiskCache.DefaultMaxBytes,
            GetTileCacheMaxAge(),
            forceDiskMaintenance);
    }

    private TimeSpan GetTileCacheMaxAge() {
        return TimeSpan.FromDays(Math.Clamp(
            _settings.TileCacheMaxAgeDays,
            AppSettings.MinTileCacheMaxAgeDays,
            AppSettings.MaxTileCacheMaxAgeDays));
    }

    private void Plugins_Click(object sender, RoutedEventArgs e) {
        if (_pluginHost is null) {
            MessageBox.Show(L.GetString("Main.PluginHostNotReady"), L.GetString("Plugins.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
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

    internal void ShowHelpWindow() {
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
                MinWidth = string.IsNullOrWhiteSpace(contribution.Toolbar.Label) ? 34 : 96,
                Height = 34,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                ToolTip = toolTip,
                Tag = contribution,
                Content = CreatePluginToolbarContent(contribution.Toolbar)
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

    private static object CreatePluginToolbarContent(PluginToolbarManifest toolbarItem) {
        var icon = CreatePluginToolbarIcon(toolbarItem.Icon);
        if (string.IsNullOrWhiteSpace(toolbarItem.Label)) {
            return icon;
        }

        return new StackPanel {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 0, 8, 0),
            Children = {
                icon,
                new TextBlock {
                    Text = toolbarItem.Label,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
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
                await ApplyPluginActionAsync(pluginId, pluginName, action);
            }
        } catch (Exception ex) {
            Logger.Error($"Plugin command '{commandId}' failed", ex);
            MessageBox.Show(ex.Message, pluginName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<bool> TryExecuteOptionalPluginCommandAsync(
        string pluginId,
        string pluginName,
        string commandId,
        object? payload = null) {
        if (_pluginHost?.Plugins.Any(plugin =>
                plugin.Id == pluginId &&
                plugin.Status == PluginLoadStatus.Loaded) != true) {
            return false;
        }

        try {
            var result = await _pluginHost.ExecuteCommandAsync(
                pluginId,
                commandId,
                payload);
            foreach (var action in result.Actions) {
                await ApplyPluginActionAsync(pluginId, pluginName, action);
            }
            return true;
        } catch (Exception ex) {
            Logger.Error($"Optional plugin '{pluginId}' command '{commandId}' failed", ex);
            return false;
        }
    }

    private async Task ApplyPluginActionsAsync(IEnumerable<PluginActionRequest> requests) {
        foreach (var request in requests) {
            await ApplyPluginActionAsync(request.PluginId, request.PluginName, request.Action);
        }
    }

    private async Task ApplyPluginActionAsync(string pluginId, string pluginName, PluginActionManifest action) {
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
                    throw new InvalidOperationException(L.GetString("Main.PluginHttpOnly"));
                }
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                break;
            case PluginActionTypes.AddImagery:
                var sourceUrl = GetPluginArgument(action, "url");
                if (string.IsNullOrWhiteSpace(sourceUrl)) {
                    throw new InvalidOperationException(L.GetString("Main.PluginAddImageryMissingUrl"));
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
            case PluginActionTypes.OpenPythonTerminal:
                if (_pluginHost is null) return;

                var command = GetPluginArgument(action, "command");
                if (string.IsNullOrWhiteSpace(command)) {
                    throw new InvalidOperationException("openPythonTerminal requires a command.");
                }
                var terminal = new Views.PythonTerminalWindow(
                    _pluginHost,
                    pluginId,
                    pluginName,
                    command,
                    GetPluginArgument(action, "title") ?? pluginName,
                    GetPluginArgument(action, "intro") ?? "") {
                    Owner = this
                };
                terminal.Show();
                break;
            default:
                throw new InvalidOperationException(L.Format("Main.PluginUnsupportedAction", action.Type));
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
            MessageBox.Show(L.Format("Main.MenuClicked", mi.Header), L.GetString("Main.MenuTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
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
                ToolTip = isSupported ? null : L.GetString("Main.UnsupportedLegacySource")
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
        _settingsWriter.Save(_settings, this);
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
        _settingsWriter.Save(_settings, this);
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
        _settingsWriter.Save(_settings, this);
        RefreshRenderedLayerFromStack(layer);
        e.Handled = true;
    }

    private void PrimaryLayer_Click(object sender, RoutedEventArgs e) {
        if (sender is not RadioButton { DataContext: MapImageLayer layer }) return;

        foreach (var item in _settings.ImageLayers) {
            item.IsPrimary = item.Id == layer.Id;
        }

        _settings.ActiveLayerId = layer.Id;
        SynchronizeActiveDataLayer();
        _settingsWriter.Save(_settings, this);
        RefreshLayerList(layer);
    }

    private void LayerVisibilityButton_Click(object sender, RoutedEventArgs e) {
        if (sender is not Button { DataContext: MapImageLayer layer }) return;

        layer.IsVisible = !layer.IsVisible;
        _settingsWriter.Save(_settings, this);
        RefreshRenderedLayerFromStack(layer);
    }

    private void RemoveImageLayer_Click(object sender, RoutedEventArgs e) {
        if (LayerListBox.SelectedItem is not MapImageLayer layer) return;

        _settings.ImageLayers.Remove(layer);
        _settings.ActiveLayerId = _settings.ImageLayers.LastOrDefault(candidate => candidate.IsVisible)?.Id ??
            _settings.ImageLayers.LastOrDefault()?.Id ??
            "";
        AppSettingsService.EnsureSinglePrimaryLayer(_settings);
        _settingsWriter.Save(_settings, this);
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
        if (modifiers == ModifierKeys.Control && e.Key == Key.O) {
            Open_Click(this, new());
            e.Handled = true;
            return;
        }
        if (modifiers == ModifierKeys.Control && e.Key == Key.N) {
            Layer_Click(this, new());
            e.Handled = true;
            return;
        }
        if (modifiers == ModifierKeys.Control && e.Key == Key.S) {
            await SaveDocumentAsync(forceSaveAs: false);
            e.Handled = true;
            return;
        }
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S) {
            await SaveDocumentAsync(forceSaveAs: true);
            e.Handled = true;
            return;
        }
        if (modifiers == ModifierKeys.Control && e.Key == Key.E) {
            await ExportGpxAsync();
            e.Handled = true;
            return;
        }
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && e.Key == Key.D) {
            await ExportToParentPathAsync();
            e.Handled = true;
            return;
        }
        if (modifiers == ModifierKeys.Control && e.Key == Key.F) {
            FocusFeatureSearch();
            e.Handled = true;
            return;
        }
        if (NonTextInputImeGuard.IsEditableTextInput(e.OriginalSource as DependencyObject)) {
            return;
        }

        if (modifiers == ModifierKeys.Control && e.Key == Key.C) {
            CopySelectedFeatures();
            e.Handled = true;
        } else if (modifiers == ModifierKeys.Control && e.Key == Key.V) {
            PasteFeatures();
            e.Handled = true;
        } else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.V) {
            PasteTagsSelectedFeatures();
            e.Handled = true;
        } else if (modifiers == ModifierKeys.Shift && e.Key == Key.R) {
            PasteTagsFromPreviousSingleSelection();
            e.Handled = true;
        } else if (modifiers == ModifierKeys.Control && e.Key == Key.T) {
            EditTagsSelectedFeatures();
            e.Handled = true;
        } else if (modifiers == ModifierKeys.Control && e.Key == Key.Z) {
            UndoLastEdit();
            e.Handled = true;
        } else if ((modifiers == ModifierKeys.Control ||
                modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) &&
            e.Key == Key.D) {
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
        } else if (modifiers == ModifierKeys.Shift && e.Key == Key.F) {
            ClearKeyboardEditCommand(updateUi: false);
            SetEditorMode(EditorMode.DrawLine);
            e.Handled = true;
        } else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.F) {
            _followDrawingViewport = !_followDrawingViewport;
            UpdateDocumentUi();
            e.Handled = true;
        } else if (modifiers == ModifierKeys.None && e.Key == Key.F1) {
            ShowHelp();
            e.Handled = true;
        } else if (modifiers == ModifierKeys.None && e.Key == Key.Oem3) {
            ReorderImageryLayers();
            e.Handled = true;
        } else if (modifiers == ModifierKeys.None && HandleKeyboardEditCommandKey(e)) {
            e.Handled = true;
        } else if (modifiers == ModifierKeys.None && e.Key == Key.S) {
            ClearKeyboardEditCommand(updateUi: false);
            SetEditorMode(EditorMode.Select);
            e.Handled = true;
        } else if (modifiers == ModifierKeys.None && e.Key == Key.V) {
            ClearKeyboardEditCommand(updateUi: false);
            SetEditorMode(EditorMode.BoxSelect);
            e.Handled = true;
        } else if (modifiers == ModifierKeys.None && e.Key == Key.Insert) {
            ClearKeyboardEditCommand(updateUi: false);
            AddNodeAtCenter();
            e.Handled = true;
        } else if (modifiers == ModifierKeys.None && e.Key == Key.Delete) {
            ClearKeyboardEditCommand(updateUi: false);
            DeleteSelectedFeatures();
            e.Handled = true;
        } else if (modifiers == ModifierKeys.None && e.Key == Key.H) {
            ClearKeyboardEditCommand(updateUi: false);
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

    private bool HandleKeyboardEditCommandKey(KeyEventArgs e) {
        if (e.Key == Key.Return) return ApplyKeyboardEditCommandOrActiveInteraction();
        if (e.Key == Key.Back && _keyboardEditCommand.Length > 0) {
            _keyboardEditCommand = _keyboardEditCommand[..^1];
            UpdateDocumentUi();
            return true;
        }

        if (_keyboardEditCommand.Length > 0 && TryGetKeyboardCommandText(e.Key, out var text)) {
            if ((_keyboardEditCommand == "r" && text == "r") ||
                (_keyboardEditCommand == "m" && text == "m") ||
                (_keyboardEditCommand == "e" && text == "e")) {
                return ApplyKeyboardEditCommandOrActiveInteraction();
            }

            _keyboardEditCommand += text;
            UpdateDocumentUi();
            return true;
        }

        if (e.IsRepeat) return false;

        if (e.Key == Key.A) {
            SetKeyboardEditCommand("a");
            SetEditorMode(EditorMode.DrawLine);
            return true;
        }
        if (e.Key == Key.R) {
            if (_featureRotation is not null) return ApplyKeyboardEditCommandOrActiveInteraction();

            BeginFeatureRotation();
            if (_featureRotation is not null) SetKeyboardEditCommand("r");
            return true;
        }
        if (e.Key == Key.M) {
            if (_featureMove is not null) return ApplyKeyboardEditCommandOrActiveInteraction();

            BeginFeatureMove();
            if (_featureMove is not null) SetKeyboardEditCommand("m");
            return true;
        }
        if (e.Key == Key.E) {
            if (_segmentExtrude is not null) return ApplyKeyboardEditCommandOrActiveInteraction();

            SetKeyboardEditCommand("e");
            SetEditorMode(EditorMode.Extrude);
            return true;
        }
        if (e.Key == Key.Q) {
            ClearKeyboardEditCommand(updateUi: false);
            OrthogonalizeSelectedFeatures();
            return true;
        }

        return false;
    }

    private bool ApplyKeyboardEditCommandOrActiveInteraction() {
        if (_keyboardEditCommand.Length > 0) {
            var commandText = _keyboardEditCommand;
            ClearKeyboardEditCommand(updateUi: false);
            if (!EditKeyboardCommandParser.TryParse(commandText, out var command)) {
                UpdateDocumentUi();
                return true;
            }

            ApplyKeyboardEditCommand(command);
            return true;
        }

        if (_featureRotation is not null) {
            CommitFeatureRotation();
            return true;
        }
        if (_featureMove is not null) {
            CommitFeatureMove();
            return true;
        }
        if (_vertexMove is not null) {
            CommitVertexMove();
            return true;
        }
        if (_segmentExtrude is not null) {
            CommitSegmentExtrude();
            return true;
        }
        if (_editorMode == EditorMode.DrawLine) {
            FinishDraftLine();
            return true;
        }

        return false;
    }

    private void ApplyKeyboardEditCommand(EditKeyboardCommand command) {
        switch (command.Kind) {
            case EditKeyboardCommandKind.DrawLine:
                FinishDraftLine();
                break;
            case EditKeyboardCommandKind.Rotate when command.RotationDegrees.HasValue:
                RotateSelectedFeaturesByDegrees(command.RotationDegrees.Value);
                break;
            case EditKeyboardCommandKind.Rotate:
                CommitFeatureRotation();
                break;
            case EditKeyboardCommandKind.Move when command.HasMoveDistance:
                MoveSelectedFeaturesByDecimeters(command.MoveEastDecimeters, command.MoveNorthDecimeters);
                break;
            case EditKeyboardCommandKind.Move:
                CommitFeatureMove();
                break;
            case EditKeyboardCommandKind.Extrude:
                ApplyKeyboardExtrudeCommand(command);
                break;
        }
    }

    private void ApplyKeyboardExtrudeCommand(EditKeyboardCommand command) {
        if (command.ExtrudeMode == EditKeyboardExtrudeMode.Free) {
            SetEditorMode(EditorMode.Extrude);
            return;
        }

        if (command.ExtrudeMode == EditKeyboardExtrudeMode.InnerSquare) {
            var sideDecimeters = command.HasExtrudeDistance
                ? Math.Abs(command.ExtrudeDistanceDecimeters)
                : DefaultKeyboardExtrudeDistanceDecimeters;
            AddInnerSquareToSelectedPolygons(sideDecimeters);
            return;
        }

        if (command.HasExtrudeDistance) {
            ExtrudeKeyboardSegmentByDecimeters(command.ExtrudeMode, command.ExtrudeDistanceDecimeters);
            return;
        }

        _pendingSegmentExtrudeConstraint = command.ExtrudeMode switch {
            EditKeyboardExtrudeMode.X => SegmentExtrudeConstraint.Horizontal,
            EditKeyboardExtrudeMode.Y => SegmentExtrudeConstraint.Vertical,
            EditKeyboardExtrudeMode.Segment => SegmentExtrudeConstraint.SegmentNormal,
            _ => SegmentExtrudeConstraint.Free
        };
        SetEditorMode(EditorMode.Extrude);
    }

    private static bool TryGetKeyboardCommandText(Key key, out string text) {
        text = key switch {
            Key.E => "e",
            Key.X => "x",
            Key.Y => "y",
            Key.S => "s",
            Key.D => "d",
            Key.M => "m",
            >= Key.D0 and <= Key.D9 => ((char)('0' + (int)key - (int)Key.D0)).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (int)key - (int)Key.NumPad0)).ToString(),
            Key.Space => " ",
            Key.OemPeriod or Key.Decimal => ".",
            Key.OemMinus or Key.Subtract => "-",
            Key.OemPlus or Key.Add => "+",
            _ => ""
        };
        return text.Length > 0;
    }

    private void SetKeyboardEditCommand(string commandText) {
        _keyboardEditCommand = commandText;
        UpdateDocumentUi();
    }

    private void ClearKeyboardEditCommand(bool updateUi = true) {
        if (_keyboardEditCommand.Length == 0) return;

        _keyboardEditCommand = "";
        if (updateUi) UpdateDocumentUi();
    }

    private void MapCanvas_MouseWheel(object sender, MouseWheelEventArgs e) {
        e.Handled = true;
        if (_featureRotation is not null || _featureMove is not null || _vertexMove is not null || _segmentExtrude is not null) return;
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
        if (_vertexMove is not null) {
            CommitVertexMove();
            e.Handled = true;
            return;
        }
        if (_segmentExtrude is not null) {
            CommitSegmentExtrude();
            e.Handled = true;
            return;
        }
        if (_isPanning) {
            e.Handled = true;
            return;
        }

        var position = e.GetPosition(MapViewport);
        if (_editorMode == EditorMode.Select) {
            if (TryBeginVertexMove(position)) {
                e.Handled = true;
                return;
            }

            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && TryBeginSelectedFeatureDrag(position)) {
                e.Handled = true;
                return;
            }

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
        if (_editorMode == EditorMode.Extrude) {
            if (TryBeginSegmentExtrude(position)) {
                e.Handled = true;
                return;
            }

            SelectFeatureAt(position, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
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
        if (_vertexMove is not null) {
            CancelVertexMove();
            e.Handled = true;
            return;
        }
        if (_segmentExtrude is not null) {
            CancelSegmentExtrude();
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
        var pointerPosition = e.GetPosition(MapViewport);
        var pointerGeo = GetPointerGeo(pointerPosition);
        _viewModel.PointerStatus = L.Format("Main.Status.Coordinates", pointerGeo.Latitude, pointerGeo.Longitude);
        if (_featureRotation is not null) {
            ClearMapHover();
            UpdateFeatureRotation(e.GetPosition(MapViewport));
            e.Handled = true;
            return;
        }
        if (_featureMove is not null) {
            ClearMapHover();
            UpdateFeatureMove(e.GetPosition(MapViewport));
            e.Handled = true;
            return;
        }
        if (_vertexMove is not null) {
            ClearMapHover();
            UpdateVertexMove(e.GetPosition(MapViewport));
            e.Handled = true;
            return;
        }
        if (_segmentExtrude is not null) {
            ClearMapHover();
            UpdateSegmentExtrude(e.GetPosition(MapViewport));
            e.Handled = true;
            return;
        }
        if (_boxSelectionStart is { } boxStart) {
            ClearMapHover();
            ShowSelectionRectangle(new Rect(boxStart, e.GetPosition(MapViewport)));
            return;
        }
        if (!_isPanning) {
            UpdateMapHover(e.GetPosition(MapViewport));
            return;
        }
        ClearMapHover();
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
        if (_featureMove is not null) {
            CommitFeatureMove();
            e.Handled = true;
            return;
        }
        if (_vertexMove is not null) {
            CommitVertexMove();
            e.Handled = true;
            return;
        }
        if (_segmentExtrude is not null) {
            CommitSegmentExtrude();
            e.Handled = true;
            return;
        }

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

    private void ExtrudeTool_Click(object sender, RoutedEventArgs e) => SetEditorMode(EditorMode.Extrude);

    private void FitDocument_Click(object sender, RoutedEventArgs e) => FitDocumentToViewport();

    private async void OsmDownload_Click(object sender, RoutedEventArgs e) => await DownloadOsmSelectionAsync();

    private async void OsmUpload_Click(object sender, RoutedEventArgs e) => await UploadOsmChangesAsync();

    private void OsmAccounts_Click(object sender, RoutedEventArgs e) {
        new Views.OsmAccountsWindow(_osmAccountStore) { Owner = this }.ShowDialog();
    }

    private void ToggleRightPanel_Click(object sender, RoutedEventArgs e) {
        var show = RightPanel.Visibility != Visibility.Visible;
        if (!show && RightPanelColumn.ActualWidth > 0) {
            _viewModel.RightPanelWidth = RightPanelColumn.ActualWidth;
        }

        RightPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        RightPanelSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        RightPanelColumn.Width = show ? new GridLength(_viewModel.RightPanelWidth) : new GridLength(0);
        _viewModel.IsRightPanelVisible = show;
        _settings.WorkbenchLayout.IsRightPanelVisible = show;
        _settings.WorkbenchLayout.RightPanelWidth = _viewModel.RightPanelWidth;
        _settingsWriter.Save(_settings, this);
    }

    private void AddNode_Click(object sender, RoutedEventArgs e) => AddNodeAtCenter();

    private void DeleteSelected_Click(object sender, RoutedEventArgs e) => DeleteSelectedFeatures();

    private void HideSelected_Click(object sender, RoutedEventArgs e) => HideSelectedFeatures();

    private void Undo_Click(object sender, RoutedEventArgs e) => UndoLastEdit();

    private void Redo_Click(object sender, RoutedEventArgs e) => RedoLastEdit();

    private void CopySelected_Click(object sender, RoutedEventArgs e) => CopySelectedFeatures();

    private void PasteFeatures_Click(object sender, RoutedEventArgs e) => PasteFeatures();

    private void PasteTags_Click(object sender, RoutedEventArgs e) => PasteTagsSelectedFeatures();

    private void DuplicateSelected_Click(object sender, RoutedEventArgs e) => DuplicateSelectedFeatures();

    private void EditTagsSelected_Click(object sender, RoutedEventArgs e) => EditTagsSelectedFeatures();

    private void FindFeatures_Click(object sender, RoutedEventArgs e) => FocusFeatureSearch();

    private void ShowPresets_Click(object sender, RoutedEventArgs e) {
        if (RightPanel.Visibility != Visibility.Visible) ToggleRightPanel_Click(sender, e);
        PresetExpander.IsExpanded = true;
        PresetSearchTextBox.Focus();
        PresetSearchTextBox.SelectAll();
    }

    private void PresetSearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshPresetList();

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (PresetComboBox.SelectedItem is not TagPreset preset) {
            PresetSummaryTextBlock.Text = "";
            ApplyPresetButton.IsEnabled = false;
            PinPresetButton.IsEnabled = false;
            return;
        }

        PresetSummaryTextBlock.Text = preset.Tags.Count == 0
            ? L.GetString("Main.Presets.NoFixedTags")
            : string.Join("  ", preset.Tags.Select(static tag => $"{tag.Key}={tag.Value}"));
        ApplyPresetButton.IsEnabled = true;
        PinPresetButton.IsEnabled = true;
    }

    private void ApplyPreset_Click(object sender, RoutedEventArgs e) {
        if (PresetComboBox.SelectedItem is not TagPreset preset) return;
        ApplyPresetToSelection(preset);
    }

    private void ApplyPresetToSelection(TagPreset preset) {
        if (preset.Tags.Count == 0) {
            MessageBox.Show(
                L.GetString("Main.Presets.NoFixedTags"),
                L.GetString("Main.Presets.MismatchTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        if (Selection.Count == 0) {
            MessageBox.Show(
                L.GetString("Main.Presets.SelectFeatureFirst"),
                L.GetString("Main.Presets.MismatchTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var targets = Selection.Features
            .Where(feature => TagPresetCatalog.SupportsGeometry(preset, feature.GeometryType))
            .ToList();
        if (targets.Count == 0) {
            ShowPresetGeometryMismatch(preset);
            return;
        }
        if (!Editor.Execute(new SetFeaturesAttributesCommand(targets, preset.Tags))) return;

        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void ShowPresetGeometryMismatch(TagPreset preset) {
        MessageBox.Show(
            L.Format(
                "Main.Presets.MismatchMessage",
                preset.DisplayName,
                DescribeGeometries(preset.Geometries),
                DescribeSelectionGeometries()),
            L.GetString("Main.Presets.MismatchTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static string DescribeGeometries(TagPresetGeometry geometries) {
        var labels = new List<string>();
        if ((geometries & TagPresetGeometry.Point) != 0) {
            labels.Add(L.GetString("Main.Presets.GeometryPoint"));
        }
        if ((geometries & TagPresetGeometry.Line) != 0) {
            labels.Add(L.GetString("Main.Presets.GeometryLine"));
        }
        if ((geometries & TagPresetGeometry.Area) != 0) {
            labels.Add(L.GetString("Main.Presets.GeometryArea"));
        }
        return labels.Count == 0
            ? L.GetString("Main.Presets.GeometryNone")
            : string.Join(", ", labels);
    }

    private string DescribeSelectionGeometries() {
        var groups = Selection.Features
            .GroupBy(static feature => feature.GeometryType)
            .OrderBy(static group => group.Key)
            .Select(group => $"{GeometryTypeLabel(group.Key)} ({group.Count()})");
        return string.Join(", ", groups);
    }

    private static string GeometryTypeLabel(MapGeometryType type) {
        return type switch {
            MapGeometryType.Point => L.GetString("Main.Presets.GeometryPoint"),
            MapGeometryType.LineString => L.GetString("Main.Presets.GeometryLine"),
            _ => L.GetString("Main.Presets.GeometryArea")
        };
    }

    private void PinPresetToToolbar_Click(object sender, RoutedEventArgs e) {
        if (PresetComboBox.SelectedItem is not TagPreset preset) return;
        PinPreset(preset);
    }

    private void PinPreset(TagPreset preset) {
        if (_settings.PresetToolbarButtons.Any(button => button.PresetId == preset.Id)) {
            RebuildPresetToolbar();
            return;
        }

        _settings.PresetToolbarButtons.Add(new PresetToolbarButton {
            PresetId = preset.Id,
            Label = preset.Name,
            Icon = preset.Icon
        });
        _settingsWriter.Save(_settings, this);
        RebuildPresetToolbar();
    }

    private void PresetToolbarSetup_Click(object sender, RoutedEventArgs e) {
        var window = new Views.PresetToolbarSetupWindow(_settings.PresetToolbarButtons, _presets) { Owner = this };
        if (window.ShowDialog() != true || !window.Changed) return;

        _settings.PresetToolbarButtons = window.Result;
        _settingsWriter.Save(_settings, this);
        RebuildPresetToolbar();
    }

    private void RebuildPresetToolbar() {
        if (PresetToolbarPanel is null || PresetToolbarSeparator is null) return;
        PresetToolbarPanel.Children.Clear();

        var actions = PresetToolbarService.Resolve(_settings.PresetToolbarButtons, _presets);
        var hasActions = actions.Count > 0;
        PresetToolbarPanel.Visibility = hasActions ? Visibility.Visible : Visibility.Collapsed;
        PresetToolbarSeparator.Visibility = hasActions ? Visibility.Visible : Visibility.Collapsed;
        if (!hasActions) return;

        foreach (var action in actions) {
            PresetToolbarPanel.Children.Add(CreatePresetToolbarButton(action));
        }
    }

    private Button CreatePresetToolbarButton(PresetToolbarAction action) {
        var displayLabel = PresetDisplayLabel(action);
        var button = new Button {
            Width = 34,
            Height = 34,
            Margin = new Thickness(2),
            Padding = new Thickness(0),
            ToolTip = action.Kind == PresetToolbarActionKind.Group
                ? L.Format("PresetToolbar.GroupTooltip", displayLabel, action.GroupPresets.Count)
                : L.Format("PresetToolbar.ItemTooltip", displayLabel),
            Tag = action
        };
        button.SetValue(AutomationProperties.NameProperty, displayLabel);
        var content = new StackPanel {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center
        };
        var iconKind = PresetIconCatalog.Resolve(action.Icon, action.Label);
        if (iconKind is not null && Enum.TryParse<PackIconLucideKind>(iconKind, out var kind)) {
            content.Children.Add(new PackIconLucide {
                Kind = kind,
                Width = 18,
                Height = 18,
                HorizontalAlignment = HorizontalAlignment.Center
            });
        } else {
            content.Children.Add(new TextBlock {
                Text = GetShortLabel(displayLabel),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }
        button.Content = content;
        button.Click += PresetToolbarButton_Click;
        button.ContextMenu = CreatePresetToolbarContextMenu(action);
        return button;
    }

    private ContextMenu CreatePresetToolbarContextMenu(PresetToolbarAction action) {
        var menu = new ContextMenu();
        if (action.Kind == PresetToolbarActionKind.Group) {
            var header = new MenuItem {
                Header = PresetDisplayLabel(action),
                IsEnabled = false
            };
            menu.Items.Add(header);
            menu.Items.Add(new Separator());
            foreach (var preset in action.GroupPresets) {
                var item = new MenuItem { Header = preset.DisplayName, Tag = preset };
                item.Click += (_, _) => ApplyPresetToSelection(preset);
                menu.Items.Add(item);
            }
        } else {
            var remove = new MenuItem { Header = L.GetString("PresetToolbar.RemoveFromToolbar") };
            remove.Click += (_, _) => RemoveToolbarButton(action.ButtonId);
            menu.Items.Add(remove);
        }
        return menu;
    }

    private void PresetToolbarButton_Click(object sender, RoutedEventArgs e) {
        if (sender is not Button { Tag: PresetToolbarAction action }) return;
        if (action.Kind == PresetToolbarActionKind.Single) {
            if (action.Preset is not null) ApplyPresetToSelection(action.Preset);
            return;
        }

        var button = (Button)sender;
        var menu = CreatePresetToolbarContextMenu(action);
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void RemoveToolbarButton(string buttonId) {
        var removed = _settings.PresetToolbarButtons.RemoveAll(button => button.Id == buttonId);
        if (removed == 0) return;
        _settingsWriter.Save(_settings, this);
        RebuildPresetToolbar();
    }

    private static string PresetDisplayLabel(PresetToolbarAction action) {
        return action.Preset?.DisplayName ?? action.Group?.DisplayName ?? action.Label;
    }

    private static string GetShortLabel(string label) {
        if (label.Length <= 3) return label;
        return label[..3];
    }

    private void FeatureSearchTextBox_TextChanged(object sender, TextChangedEventArgs e) {
        RefreshFeatureList();
        UpdateFeatureSearchUi();
        UpdateDocumentUi();
    }

    private void FeatureSearchTextBox_KeyDown(object sender, KeyEventArgs e) {
        if (e.Key != Key.Escape) return;

        ClearFeatureSearch();
        e.Handled = true;
    }

    private void ClearFeatureSearch_Click(object sender, RoutedEventArgs e) => ClearFeatureSearch();

    private void RotateSelected_Click(object sender, RoutedEventArgs e) {
        if (_featureRotation is null) BeginFeatureRotation();
        else CommitFeatureRotation();
    }

    private void MoveSelected_Click(object sender, RoutedEventArgs e) {
        if (_featureMove is null) BeginFeatureMove();
        else CommitFeatureMove();
    }

    private void OrthogonalizeSelected_Click(object sender, RoutedEventArgs e) => OrthogonalizeSelectedFeatures();

    private void ReverseSelectedLine_Click(object sender, RoutedEventArgs e) {
        var feature = GetSelectedFeaturesInDocumentOrder().SingleOrDefault();
        ApplyTopologyResult(feature is null
            ? new TopologyEditCommandResult(null, L.GetString("Main.Topology.SelectOne"))
            : TopologyEditService.CreateReverseLineCommand(Editor.Dataset, feature));
    }

    private void SplitSelectedLine_Click(object sender, RoutedEventArgs e) {
        var feature = GetSelectedFeaturesInDocumentOrder().SingleOrDefault();
        ApplyTopologyResult(feature is null || _hoveredVertex is not { } vertex || !ReferenceEquals(feature, vertex.Feature)
            ? new TopologyEditCommandResult(null, L.GetString("Main.Topology.HoverVertex"))
            : TopologyEditService.CreateSplitLineCommand(Editor.Dataset, feature, vertex.PointIndex));
    }

    private void CombineSelectedLines_Click(object sender, RoutedEventArgs e) {
        var features = GetSelectedFeaturesInDocumentOrder();
        ApplyTopologyResult(features.Count != 2
            ? new TopologyEditCommandResult(null, L.GetString("Main.Topology.SelectTwo"))
            : TopologyEditService.CreateCombineLinesCommand(Editor.Dataset, features[0], features[1]));
    }

    private void SimplifySelectedGeometry_Click(object sender, RoutedEventArgs e) {
        var feature = GetSelectedFeaturesInDocumentOrder().SingleOrDefault();
        ApplyTopologyResult(feature is null
            ? new TopologyEditCommandResult(null, L.GetString("Main.Topology.SelectOne"))
            : TopologyEditService.CreateSimplifyCommand(Editor.Dataset, feature, 1.0));
    }

    private void ApplyTopologyResult(TopologyEditCommandResult result) {
        if (result.Command is null) {
            MessageBox.Show(
                this,
                result.Error ?? L.GetString("Common.UnknownError"),
                L.GetString("Main.Menu.Topology"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        if (!Editor.Execute(result.Command)) return;

        PruneSelectionToDocument();
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void RestoreHistory_Click(object sender, RoutedEventArgs e) {
        if (HistoryListBox.SelectedItem is not EditHistoryEntry entry) return;
        if (!Editor.CommandStack.MoveToHistoryPosition(entry.Position)) return;

        PruneSelectionToDocument();
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

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
        if (_editorMode == EditorMode.VertexMove && mode != EditorMode.VertexMove && _vertexMove is not null) {
            CommitVertexMove();
        }
        if (_editorMode == EditorMode.Extrude && mode != EditorMode.Extrude && _segmentExtrude is not null) {
            CommitSegmentExtrude();
        }
        if (_editorMode == EditorMode.DrawLine && mode != EditorMode.DrawLine) FinishDraftLine();
        _editorMode = mode;
        PanToolButton.ClearValue(Control.BackgroundProperty);
        SelectToolButton.ClearValue(Control.BackgroundProperty);
        BoxSelectToolButton.ClearValue(Control.BackgroundProperty);
        DrawLineToolButton.ClearValue(Control.BackgroundProperty);
        ExtrudeToolButton.ClearValue(Control.BackgroundProperty);
        var activeButton = mode switch {
            EditorMode.Pan => PanToolButton,
            EditorMode.Select => SelectToolButton,
            EditorMode.BoxSelect => BoxSelectToolButton,
            EditorMode.DrawLine => DrawLineToolButton,
            EditorMode.Rotate => SelectToolButton,
            EditorMode.Move => SelectToolButton,
            EditorMode.VertexMove => SelectToolButton,
            EditorMode.Extrude => ExtrudeToolButton,
            _ => SelectToolButton
        };
        activeButton.SetResourceReference(Control.BackgroundProperty, "Theme.SelectionBrush");
        _viewModel.EditorModeStatus = mode switch {
            EditorMode.Pan => L.GetString("Main.Mode.Pan"),
            EditorMode.Select => L.GetString("Main.Mode.Select"),
            EditorMode.BoxSelect => L.GetString("Main.Mode.BoxSelect"),
            EditorMode.DrawLine => L.GetString("Main.Mode.DrawLine"),
            EditorMode.Rotate => L.GetString("Main.Mode.Rotate"),
            EditorMode.Move => L.GetString("Main.Mode.Move"),
            EditorMode.VertexMove => L.GetString("Main.Mode.Move"),
            EditorMode.Extrude => L.GetString("Main.Mode.Extrude"),
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
            EditorMode.VertexMove => Cursors.SizeAll,
            EditorMode.Extrude => Cursors.SizeAll,
            _ => Cursors.Arrow
        };
    }

    private bool TryBeginSelectedFeatureDrag(Point position) {
        if (_document is null || Selection.Count == 0 || !int.TryParse(ZoomTextBox.Text, out var zoom)) return false;

        var feature = VectorMapInteraction.HitTest(
            VectorLayer.LastPlan?.Features ?? [],
            position,
            _centerLat,
            _centerLon,
            zoom,
            new Size(MapViewport.ActualWidth, MapViewport.ActualHeight),
            displayTransform: _displayTransform,
            panOffsetX: _panOffsetX,
            panOffsetY: _panOffsetY);
        if (feature is null || !Selection.Features.Contains(feature)) return false;

        return TryBeginFeatureMove(position);
    }

    private void UpdateMapHover(Point position) {
        if (_document is null || !int.TryParse(ZoomTextBox.Text, out var zoom)) {
            ClearMapHover();
            return;
        }

        var viewport = GetMapViewportSize();
        var candidates = GetInteractionCandidates();
        var vertex = VectorMapInteraction.HitTestVertex(
            GetVertexInteractionCandidates(),
            position,
            _centerLat,
            _centerLon,
            zoom,
            viewport,
            tolerance: 12,
            displayTransform: _displayTransform,
            panOffsetX: _panOffsetX,
            panOffsetY: _panOffsetY);
        var feature = vertex?.Feature ?? VectorMapInteraction.HitTest(
            candidates,
            position,
            _centerLat,
            _centerLon,
            zoom,
            viewport,
            tolerance: 10,
            displayTransform: _displayTransform,
            panOffsetX: _panOffsetX,
            panOffsetY: _panOffsetY);

        var changed = !SameVertex(_hoveredVertex, vertex) ||
            !ReferenceEquals(_hoveredFeature, feature);
        _hoveredVertex = vertex;
        _hoveredFeature = feature;
        Cursor = vertex is not null
            ? Cursors.SizeAll
            : feature is not null
                ? Cursors.Hand
                : GetEditorModeCursor();
        if (changed) UpdateVectorLayer();
    }

    private void ClearMapHover() {
        if (_hoveredFeature is null && _hoveredVertex is null) return;

        _hoveredFeature = null;
        _hoveredVertex = null;
        Cursor = GetEditorModeCursor();
        UpdateVectorLayer();
    }

    private IReadOnlyList<MapFeature> GetInteractionCandidates() {
        return VectorLayer.LastPlan?.Features ?? [];
    }

    private IReadOnlyList<MapFeature> GetVertexInteractionCandidates() {
        var visible = GetInteractionCandidates();
        if (Selection.Count == 0) return visible;

        var result = Selection.Features
            .Concat(visible)
            .Distinct()
            .ToList();
        return result;
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
            new Size(MapViewport.ActualWidth, MapViewport.ActualHeight),
            displayTransform: _displayTransform,
            panOffsetX: _panOffsetX,
            panOffsetY: _panOffsetY);
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
            viewport,
            _displayTransform,
            panOffsetX: _panOffsetX,
            panOffsetY: _panOffsetY).ToList();
        if (extendSelection) selected.AddRange(Selection.Features.Where(feature => !selected.Contains(feature)));
        SetSelectedFeatures(selected);

        var first = VectorMapInteraction.ScreenToGeo(rect.TopLeft, _centerLat, _centerLon, zoom, viewport, displayTransform: _displayTransform);
        var second = VectorMapInteraction.ScreenToGeo(rect.BottomRight, _centerLat, _centerLon, zoom, viewport, displayTransform: _displayTransform);
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
            new Size(MapViewport.ActualWidth, MapViewport.ActualHeight),
            displayTransform: _displayTransform);
        if (!point.IsValid) return;

        if (!Editor.AddDraftLinePoint(point)) return;

        if (Editor.DraftLine is not null) SetSelectedFeatures([Editor.DraftLine]);
        if (_followDrawingViewport) CenterViewportOnDocumentPoint(point);
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
        var point = _displayTransform.DisplayToDocument(new GeoPoint(_centerLon, _centerLat));
        if (!point.IsValid) return;

        var feature = new MapFeature {
            GeometryType = MapGeometryType.Point,
            Parts = [[point]]
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
            L.Format("Main.DeleteSelectedConfirm", selectedFeatures.Count),
            L.GetString("Main.DeleteSelectedTitle"),
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

    private void PasteTagsSelectedFeatures() {
        PasteTagsSelectedFeatures(_featureClipboard);
    }

    private void PasteTagsFromPreviousSingleSelection() {
        if (_previousSingleSelectedFeature is null ||
            _document?.Features.Contains(_previousSingleSelectedFeature) != true) {
            return;
        }

        PasteTagsSelectedFeatures([_previousSingleSelectedFeature]);
    }

    private void PasteTagsSelectedFeatures(IReadOnlyList<MapFeature> sourceFeatures) {
        if (sourceFeatures.Count == 0) return;

        var selectedFeatures = GetSelectedFeaturesInDocumentOrder();
        if (selectedFeatures.Count == 0) return;

        var tags = MapEditService.CreatePasteTags(sourceFeatures);
        if (tags.Count == 0) return;
        if (!Editor.Execute(new SetFeaturesAttributesCommand(selectedFeatures, tags))) return;

        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void DuplicateSelectedFeatures() {
        var selectedFeatures = GetSelectedFeaturesInDocumentOrder();
        if (selectedFeatures.Count == 0) return;

        InsertFeatureCopies(selectedFeatures, offsetMultiplier: 1);
    }

    private void EditTagsSelectedFeatures() {
        if (_featureRotation is not null) CommitFeatureRotation();
        if (_featureMove is not null) CommitFeatureMove();
        if (_vertexMove is not null) CommitVertexMove();
        if (_segmentExtrude is not null) CommitSegmentExtrude();
        if (Editor.HasDraftLine) FinishDraftLine();

        var selectedFeatures = GetSelectedFeaturesInDocumentOrder();
        if (selectedFeatures.Count == 0) return;
        if (selectedFeatures.Count > 1) {
            MessageBox.Show(L.GetString("Main.EditTagsSelectOne"), L.GetString("FeatureTags.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var feature = selectedFeatures[0];
        var window = new Views.FeatureTagsWindow(feature) { Owner = this };
        if (window.ShowDialog() != true) return;

        if (!Editor.Execute(new SetFeatureAttributesCommand(feature, window.Tags))) return;

        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void EditRelationSelected_Click(object sender, RoutedEventArgs e) => EditRelationSelectedFeatures();

    private void CreateRelationFromSelection_Click(object sender, RoutedEventArgs e) => CreateRelationFromSelection();

    private void EditRelationSelectedFeatures() {
        CommitPendingModes();
        if (_document is null) return;

        var selectedFeatures = GetSelectedFeaturesInDocumentOrder();
        if (selectedFeatures.Count == 0) return;
        if (selectedFeatures.Count > 1) {
            MessageBox.Show(
                L.GetString("Main.EditRelationSelectOne"),
                L.GetString("Osm.Relation.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var feature = selectedFeatures[0];
        if (feature.Osm?.PrimitiveType != OsmPrimitiveType.Relation) {
            MessageBox.Show(
                L.GetString("Main.EditRelationNotRelation"),
                L.GetString("Osm.Relation.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!EnsureSyncedDocument()) return;
        if (!_document!.Osm!.Relations.TryGetValue(feature.Osm.Id, out var relation)) return;

        var window = new Views.RelationEditorWindow(_document, relation, relation.Members) { Owner = this };
        if (window.ShowDialog() != true) return;
        if (!Editor.Execute(new SetRelationMembersCommand(feature, window.Members))) return;

        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void CreateRelationFromSelection() {
        CommitPendingModes();
        if (_document is null) return;

        var selectedFeatures = GetSelectedFeaturesInDocumentOrder();
        if (selectedFeatures.Count == 0) {
            MessageBox.Show(
                L.GetString("Main.CreateRelationSelectFeatures"),
                L.GetString("Osm.Relation.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!EnsureSyncedDocument()) return;

        var initialMembers = selectedFeatures
            .Where(feature => feature.Osm is not null)
            .Select(feature => new OsmRelationMember(
                RelationEditService.ToMemberType(feature.Osm!.PrimitiveType) ?? OsmRelationMemberType.Way,
                feature.Osm.Id,
                ""))
            .ToList();
        if (initialMembers.Count == 0) {
            MessageBox.Show(
                L.GetString("Main.CreateRelationNoOsm"),
                L.GetString("Osm.Relation.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var window = new Views.RelationEditorWindow(
            _document,
            relation: null,
            initialMembers,
            selectedFeatures) { Owner = this };
        if (window.ShowDialog() != true) return;

        var tags = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["type"] = "multipolygon"
        };
        var command = new CreateRelationCommand(window.Members, tags);
        if (!Editor.Execute(command)) return;

        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
        if (command.CreatedFeature is not null) {
            SetSelectedFeatures([command.CreatedFeature]);
        }
    }

    private void CommitPendingModes() {
        if (_featureRotation is not null) CommitFeatureRotation();
        if (_featureMove is not null) CommitFeatureMove();
        if (_vertexMove is not null) CommitVertexMove();
        if (_segmentExtrude is not null) CommitSegmentExtrude();
        if (Editor.HasDraftLine) FinishDraftLine();
    }

    private bool EnsureSyncedDocument() {
        try {
            OsmDocumentSync.Synchronize(_document!);
            return true;
        } catch (Exception ex) {
            MessageBox.Show(
                ex.Message,
                L.GetString("Osm.Relation.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
    }

    private void OrthogonalizeSelectedFeatures() {
        if (_featureRotation is not null || _featureMove is not null || _vertexMove is not null || _segmentExtrude is not null) return;
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

    private void CenterViewportOnDocumentPoint(GeoPoint documentPoint) {
        var displayPoint = _displayTransform.DocumentToDisplay(documentPoint);
        if (!displayPoint.IsValid) return;

        _centerLon = displayPoint.Longitude;
        _centerLat = GeoConverter.ClampLatitude(displayPoint.Latitude);
        ApplyLayerTransforms();
        if (int.TryParse(ZoomTextBox.Text, out var zoom) && _tileLayers.Count > 0) {
            ScheduleRender(zoom, 0);
        }
    }

    private (double Longitude, double Latitude) GetFeaturePasteOffset() {
        var zoom = int.TryParse(ZoomTextBox.Text, out var parsedZoom)
            ? ClampZoom(parsedZoom)
            : GeoConverter.MinZoom;
        var viewport = new Size(
            MapViewport.ActualWidth > 0 ? MapViewport.ActualWidth : 1024,
            MapViewport.ActualHeight > 0 ? MapViewport.ActualHeight : 768);
        var center = new Point(viewport.Width / 2.0, viewport.Height / 2.0);
        var origin = VectorMapInteraction.ScreenToGeo(center, _centerLat, _centerLon, zoom, viewport, displayTransform: _displayTransform);
        var shifted = VectorMapInteraction.ScreenToGeo(
            new Point(center.X + FeaturePasteOffsetPixels, center.Y + FeaturePasteOffsetPixels),
            _centerLat,
            _centerLon,
            zoom,
            viewport,
            displayTransform: _displayTransform);

        return (shifted.Longitude - origin.Longitude, shifted.Latitude - origin.Latitude);
    }

    private void BeginFeatureRotation() {
        if (_featureMove is not null || _vertexMove is not null || _segmentExtrude is not null) return;
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

        ApplyFeatureRotation(rotation, angleDegrees);
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void ApplyFeatureRotation(FeatureRotation rotation, double angleDegrees) {
        foreach (var state in rotation.OriginalStates) {
            Editor.Dataset.ReplaceParts(
                state.Feature,
                MapEditService.RotateParts(state.Parts, rotation.Center, angleDegrees),
                markDirty: false);
        }
    }

    private void CommitFeatureRotation(bool forceCommit = false) {
        if (_featureRotation is not { } rotation) return;

        _featureRotation = null;
        ClearKeyboardEditCommand(updateUi: false);
        if (MapViewport.IsMouseCaptured) MapViewport.ReleaseMouseCapture();
        var afterStates = rotation.OriginalStates.Select(state => CaptureFeatureParts(state.Feature)).ToList();
        var angleDegrees = Math.Abs(rotation.ScreenAngleRadians * 180.0 / Math.PI);
        var committed = (forceCommit || angleDegrees >= MinimumCommittedRotationDegrees) &&
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
        ClearKeyboardEditCommand(updateUi: false);
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
            viewport,
            displayTransform: _displayTransform);
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

    private static bool SameVertex(VertexHit? left, VertexHit? right) {
        if (left is null || right is null) return left is null && right is null;

        return ReferenceEquals(left.Feature, right.Feature) &&
            left.PartIndex == right.PartIndex &&
            left.PointIndex == right.PointIndex;
    }

    private bool RotateSelectedFeaturesByDegrees(double angleDegrees) {
        if (_featureRotation is null) BeginFeatureRotation();
        if (_featureRotation is not { } rotation) return false;

        rotation.ScreenAngleRadians = -angleDegrees * Math.PI / 180.0;
        ApplyFeatureRotation(rotation, angleDegrees);
        CommitFeatureRotation(forceCommit: true);
        return true;
    }

    private void BeginFeatureMove() {
        _ = TryBeginFeatureMove(Mouse.GetPosition(MapViewport));
    }

    private bool TryBeginFeatureMove(Point pointer) {
        if (_featureRotation is not null || _vertexMove is not null || _segmentExtrude is not null) return false;
        if (Editor.HasDraftLine) FinishDraftLine();

        var selectedFeatures = GetSelectedFeaturesInDocumentOrder();
        if (_featureMove is not null || selectedFeatures.Count == 0) return false;

        MapViewport.Focus();
        _featureMove = new FeatureMove(
            _editorMode,
            selectedFeatures.Select(CaptureFeatureParts).ToList(),
            pointer,
            GetPointerGeo(pointer));
        SetEditorMode(EditorMode.Move);
        MapViewport.CaptureMouse();
        UpdateDocumentUi();
        return true;
    }

    private bool TryBeginVertexMove(Point pointer) {
        if (_featureRotation is not null || _featureMove is not null || _segmentExtrude is not null) return false;
        if (Editor.HasDraftLine) FinishDraftLine();
        if (_document is null || !int.TryParse(ZoomTextBox.Text, out var zoom)) return false;

        var hit = _hoveredVertex ?? VectorMapInteraction.HitTestVertex(
            GetVertexInteractionCandidates(),
            pointer,
            _centerLat,
            _centerLon,
            zoom,
            GetMapViewportSize(),
            tolerance: 16,
            displayTransform: _displayTransform,
            panOffsetX: _panOffsetX,
            panOffsetY: _panOffsetY);
        if (hit is null) return false;

        if (!Selection.Features.Contains(hit.Feature)) {
            SetSelectedFeatures([hit.Feature]);
        }

        var targets = GetSharedVertexTargets(hit);
        var originalStates = targets
            .Select(static target => target.Feature)
            .Distinct()
            .Select(CaptureFeatureParts)
            .ToList();
        if (targets.Count == 0 || originalStates.Count == 0) return false;

        MapViewport.Focus();
        _vertexMove = new VertexMove(
            _editorMode,
            originalStates,
            targets,
            pointer);
        SetEditorMode(EditorMode.VertexMove);
        MapViewport.CaptureMouse();
        UpdateDocumentUi();
        return true;
    }

    private bool TryBeginSegmentExtrude(Point pointer) {
        if (_featureRotation is not null || _featureMove is not null || _vertexMove is not null) return false;
        if (Editor.HasDraftLine) FinishDraftLine();
        if (_document is null || !int.TryParse(ZoomTextBox.Text, out var zoom)) return false;

        var hit = VectorMapInteraction.HitTestSegment(
            GetInteractionCandidates(),
            pointer,
            _centerLat,
            _centerLon,
            zoom,
            GetMapViewportSize(),
            tolerance: 14,
            displayTransform: _displayTransform,
            panOffsetX: _panOffsetX,
            panOffsetY: _panOffsetY);
        if (hit is null) return false;

        if (!Selection.Features.Contains(hit.Feature)) {
            SetSelectedFeatures([hit.Feature]);
        }

        MapViewport.Focus();
        var constraint = _pendingSegmentExtrudeConstraint;
        _pendingSegmentExtrudeConstraint = SegmentExtrudeConstraint.Free;
        _segmentExtrude = new SegmentExtrude(
            _editorMode,
            CaptureFeatureParts(hit.Feature),
            hit,
            pointer,
            GetPointerGeo(pointer),
            constraint);
        SetEditorMode(EditorMode.Extrude);
        MapViewport.CaptureMouse();
        UpdateDocumentUi();
        return true;
    }

    private void UpdateSegmentExtrude(Point pointerPosition) {
        if (_segmentExtrude is not { } extrude) return;

        extrude.CurrentPointer = pointerPosition;
        var pointerGeo = GetPointerGeo(pointerPosition);
        var longitudeOffset = pointerGeo.Longitude - extrude.StartPointerGeo.Longitude;
        var latitudeOffset = pointerGeo.Latitude - extrude.StartPointerGeo.Latitude;
        ApplySegmentExtrudeConstraint(extrude, pointerPosition, ref longitudeOffset, ref latitudeOffset);
        Editor.Dataset.ReplaceParts(
            extrude.OriginalState.Feature,
            MapEditService.ExtrudeSegment(
                extrude.OriginalState.Parts,
                extrude.Hit.PartIndex,
                extrude.Hit.StartPointIndex,
                extrude.Hit.EndPointIndex,
                longitudeOffset,
                latitudeOffset),
            markDirty: false);
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void ApplySegmentExtrudeConstraint(
        SegmentExtrude extrude,
        Point pointerPosition,
        ref double longitudeOffset,
        ref double latitudeOffset) {
        switch (extrude.Constraint) {
            case SegmentExtrudeConstraint.Horizontal:
                latitudeOffset = 0;
                break;
            case SegmentExtrudeConstraint.Vertical:
                longitudeOffset = 0;
                break;
            case SegmentExtrudeConstraint.SegmentNormal:
                var segment = extrude.Hit.EndScreenPoint - extrude.Hit.StartScreenPoint;
                if (segment.Length <= double.Epsilon) return;

                var normal = new Vector(-segment.Y / segment.Length, segment.X / segment.Length);
                var pointerDelta = pointerPosition - extrude.StartPointer;
                var constrainedPointer = extrude.StartPointer + normal * (pointerDelta * normal);
                var constrainedGeo = GetPointerGeo(constrainedPointer);
                longitudeOffset = constrainedGeo.Longitude - extrude.StartPointerGeo.Longitude;
                latitudeOffset = constrainedGeo.Latitude - extrude.StartPointerGeo.Latitude;
                break;
        }
    }

    private bool ExtrudeKeyboardSegmentByDecimeters(EditKeyboardExtrudeMode mode, double distanceDecimeters) {
        if (!TryGetKeyboardExtrudeSegment(out var hit)) return false;

        if (!Selection.Features.Contains(hit.Feature)) {
            SetSelectedFeatures([hit.Feature]);
        }

        var beforeState = CaptureFeatureParts(hit.Feature);
        var bounds = GeoBounds.FromPoints(beforeState.Parts.SelectMany(static part => part));
        if (!bounds.IsValid) return false;

        var distanceMeters = distanceDecimeters / 10.0;
        var parts = mode switch {
            EditKeyboardExtrudeMode.X => MapEditService.ExtrudeSegmentByMeters(
                beforeState.Parts,
                hit.PartIndex,
                hit.StartPointIndex,
                hit.EndPointIndex,
                distanceMeters,
                0,
                bounds.Center.Latitude),
            EditKeyboardExtrudeMode.Y => MapEditService.ExtrudeSegmentByMeters(
                beforeState.Parts,
                hit.PartIndex,
                hit.StartPointIndex,
                hit.EndPointIndex,
                0,
                distanceMeters,
                bounds.Center.Latitude),
            EditKeyboardExtrudeMode.Segment => MapEditService.ExtrudeSegmentNormalByMeters(
                beforeState.Parts,
                hit.PartIndex,
                hit.StartPointIndex,
                hit.EndPointIndex,
                distanceMeters),
            _ => beforeState.Parts.Select(static part => part.ToList()).ToList()
        };

        var changed = Editor.Execute(new SetFeaturePartsCommand([beforeState], [new FeaturePartsSnapshot(hit.Feature, parts)]));
        if (!changed) return false;

        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
        return true;
    }

    private bool AddInnerSquareToSelectedPolygons(double sideDecimeters) {
        var selectedPolygons = GetSelectedFeaturesInDocumentOrder()
            .Where(static feature => feature.GeometryType == MapGeometryType.Polygon)
            .ToList();
        if (selectedPolygons.Count == 0) return false;

        var sideMeters = sideDecimeters / 10.0;
        var beforeStates = selectedPolygons.Select(CaptureFeatureParts).ToList();
        var afterStates = beforeStates
            .Select(state => new FeaturePartsSnapshot(
                state.Feature,
                MapEditService.AddInnerSquareRingByMeters(state.Parts, sideMeters)))
            .ToList();
        var changed = Editor.Execute(new SetFeaturePartsCommand(beforeStates, afterStates));
        if (!changed) return false;

        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
        return true;
    }

    private bool TryGetKeyboardExtrudeSegment(out SegmentHit hit) {
        hit = default!;
        if (_document is null || !int.TryParse(ZoomTextBox.Text, out var zoom)) return false;

        var pointer = Mouse.GetPosition(MapViewport);
        var hovered = VectorMapInteraction.HitTestSegment(
            GetInteractionCandidates(),
            pointer,
            _centerLat,
            _centerLon,
            zoom,
            GetMapViewportSize(),
            tolerance: 14,
            displayTransform: _displayTransform,
            panOffsetX: _panOffsetX,
            panOffsetY: _panOffsetY);
        if (hovered is not null) {
            hit = hovered;
            return true;
        }

        foreach (var feature in GetSelectedFeaturesInDocumentOrder()) {
            if (TryCreateFirstSegmentHit(feature, zoom, out hit)) return true;
        }

        return false;
    }

    private bool TryCreateFirstSegmentHit(MapFeature feature, int zoom, out SegmentHit hit) {
        hit = default!;
        for (var partIndex = 0; partIndex < feature.Parts.Count; partIndex++) {
            var part = feature.Parts[partIndex];
            if (part.Count < 2) continue;

            var viewport = GetMapViewportSize();
            var start = VectorMapInteraction.GeoToScreen(
                part[0],
                _centerLat,
                _centerLon,
                zoom,
                viewport,
                displayTransform: _displayTransform);
            var end = VectorMapInteraction.GeoToScreen(
                part[1],
                _centerLat,
                _centerLon,
                zoom,
                viewport,
                displayTransform: _displayTransform);
            hit = new SegmentHit(feature, partIndex, 0, 1, start, end);
            return true;
        }

        return false;
    }

    private void CommitSegmentExtrude(bool forceCommit = false) {
        if (_segmentExtrude is not { } extrude) return;

        _segmentExtrude = null;
        ClearKeyboardEditCommand(updateUi: false);
        if (MapViewport.IsMouseCaptured) MapViewport.ReleaseMouseCapture();
        var afterState = CaptureFeatureParts(extrude.OriginalState.Feature);
        var pointerDelta = extrude.CurrentPointer - extrude.StartPointer;
        var committed = (forceCommit || pointerDelta.Length >= MinimumCommittedMovePixels) &&
            Editor.Execute(new SetFeaturePartsCommand([extrude.OriginalState], [afterState]));
        if (!committed) RestoreSegmentExtrude(extrude);

        SetEditorMode(extrude.PreviousMode == EditorMode.Extrude ? EditorMode.Select : extrude.PreviousMode);
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void CancelSegmentExtrude() {
        if (_segmentExtrude is not { } extrude) return;

        _segmentExtrude = null;
        ClearKeyboardEditCommand(updateUi: false);
        RestoreSegmentExtrude(extrude);
        if (MapViewport.IsMouseCaptured) MapViewport.ReleaseMouseCapture();
        SetEditorMode(extrude.PreviousMode == EditorMode.Extrude ? EditorMode.Select : extrude.PreviousMode);
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void RestoreSegmentExtrude(SegmentExtrude extrude) {
        Editor.Dataset.ReplaceParts(
            extrude.OriginalState.Feature,
            extrude.OriginalState.Parts.Select(static part => part.ToList()),
            markDirty: false);
    }

    private void UpdateVertexMove(Point pointerPosition) {
        if (_vertexMove is not { } move) return;

        move.CurrentPointer = pointerPosition;
        var point = GetPointerGeo(pointerPosition);
        foreach (var state in move.OriginalStates) {
            var parts = state.Parts.Select(static part => part.ToList()).ToList();
            foreach (var target in move.Targets.Where(target => ReferenceEquals(target.Feature, state.Feature))) {
                parts = MapEditService.MoveVertex(parts, target.PartIndex, target.PointIndex, point);
            }
            Editor.Dataset.ReplaceParts(state.Feature, parts, markDirty: false);
        }
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void CommitVertexMove(bool forceCommit = false) {
        if (_vertexMove is not { } move) return;

        _vertexMove = null;
        ClearKeyboardEditCommand(updateUi: false);
        if (MapViewport.IsMouseCaptured) MapViewport.ReleaseMouseCapture();
        var afterStates = move.OriginalStates.Select(state => CaptureFeatureParts(state.Feature)).ToList();
        var pointerDelta = move.CurrentPointer - move.StartPointer;
        var committed = (forceCommit || pointerDelta.Length >= MinimumCommittedMovePixels) &&
            Editor.Execute(new SetFeaturePartsCommand(move.OriginalStates, afterStates));
        if (!committed) RestoreVertexMove(move);

        SetEditorMode(move.PreviousMode == EditorMode.VertexMove ? EditorMode.Select : move.PreviousMode);
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void CancelVertexMove() {
        if (_vertexMove is not { } move) return;

        _vertexMove = null;
        ClearKeyboardEditCommand(updateUi: false);
        RestoreVertexMove(move);
        if (MapViewport.IsMouseCaptured) MapViewport.ReleaseMouseCapture();
        SetEditorMode(move.PreviousMode == EditorMode.VertexMove ? EditorMode.Select : move.PreviousMode);
        RefreshFeatureList();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void RestoreVertexMove(VertexMove move) {
        foreach (var state in move.OriginalStates) {
            Editor.Dataset.ReplaceParts(
                state.Feature,
                state.Parts.Select(static part => part.ToList()),
                markDirty: false);
        }
    }

    private IReadOnlyList<VertexEditTarget> GetSharedVertexTargets(VertexHit hit) {
        if (_document is null ||
            hit.PartIndex < 0 ||
            hit.PartIndex >= hit.Feature.Parts.Count ||
            hit.PointIndex < 0 ||
            hit.PointIndex >= hit.Feature.Parts[hit.PartIndex].Count) {
            return [];
        }

        if (TryGetOsmNodeId(hit.Feature, hit.PointIndex, out var nodeId)) {
            var byNode = GetVertexTargetsForOsmNode(nodeId).ToList();
            if (byNode.Count > 0) return byNode;
        }

        return GetVertexTargetsForPoint(hit.Feature.Parts[hit.PartIndex][hit.PointIndex]).ToList();
    }

    private IEnumerable<VertexEditTarget> GetVertexTargetsForOsmNode(long nodeId) {
        if (_document is null) yield break;

        var seen = new HashSet<(MapFeature Feature, int PartIndex, int PointIndex)>();
        foreach (var feature in _document.Features) {
            if (feature.GeometryType == MapGeometryType.Point &&
                feature.Osm?.PrimitiveType == OsmPrimitiveType.Node &&
                feature.Osm.Id == nodeId &&
                AddVertexTarget(seen, feature, 0, 0)) {
                yield return new VertexEditTarget(feature, 0, 0);
                continue;
            }

            if (feature.Osm?.PrimitiveType != OsmPrimitiveType.Way ||
                feature.Osm.NodeReferences.Count == 0) {
                continue;
            }

            var count = Math.Min(feature.Osm.NodeReferences.Count, feature.Parts.FirstOrDefault()?.Count ?? 0);
            for (var i = 0; i < count; i++) {
                if (feature.Osm.NodeReferences[i].Id != nodeId ||
                    !AddVertexTarget(seen, feature, 0, NormalizeClosedRingPointIndex(feature.Parts[0], i))) {
                    continue;
                }

                yield return new VertexEditTarget(feature, 0, NormalizeClosedRingPointIndex(feature.Parts[0], i));
            }
        }
    }

    private IEnumerable<VertexEditTarget> GetVertexTargetsForPoint(GeoPoint point) {
        if (_document is null) yield break;

        var seen = new HashSet<(MapFeature Feature, int PartIndex, int PointIndex)>();
        foreach (var feature in _document.Features) {
            for (var partIndex = 0; partIndex < feature.Parts.Count; partIndex++) {
                var part = feature.Parts[partIndex];
                var count = IsClosedRing(part) ? part.Count - 1 : part.Count;
                for (var pointIndex = 0; pointIndex < count; pointIndex++) {
                    if (part[pointIndex] != point || !AddVertexTarget(seen, feature, partIndex, pointIndex)) continue;

                    yield return new VertexEditTarget(feature, partIndex, pointIndex);
                }
            }
        }
    }

    private static bool TryGetOsmNodeId(MapFeature feature, int pointIndex, out long nodeId) {
        nodeId = 0;
        if (feature.Osm?.PrimitiveType == OsmPrimitiveType.Node) {
            nodeId = feature.Osm.Id;
            return true;
        }

        if (feature.Osm?.PrimitiveType == OsmPrimitiveType.Way &&
            pointIndex >= 0 &&
            pointIndex < feature.Osm.NodeReferences.Count) {
            nodeId = feature.Osm.NodeReferences[pointIndex].Id;
            return true;
        }

        return false;
    }

    private static int NormalizeClosedRingPointIndex(IReadOnlyList<GeoPoint> part, int pointIndex) {
        return IsClosedRing(part) && pointIndex == part.Count - 1 ? 0 : pointIndex;
    }

    private static bool IsClosedRing(IReadOnlyList<GeoPoint> part) {
        return part.Count > 2 && part[0] == part[^1];
    }

    private static bool AddVertexTarget(
        HashSet<(MapFeature Feature, int PartIndex, int PointIndex)> seen,
        MapFeature feature,
        int partIndex,
        int pointIndex) {
        return seen.Add((feature, partIndex, pointIndex));
    }

    private void UpdateFeatureMove(Point pointerPosition) {
        if (_featureMove is not { } move) return;

        move.CurrentPointer = pointerPosition;
        var pointerGeo = GetPointerGeo(pointerPosition);
        var longitudeOffset = pointerGeo.Longitude - move.StartPointerGeo.Longitude;
        var latitudeOffset = pointerGeo.Latitude - move.StartPointerGeo.Latitude;

        ApplyFeatureMove(move, longitudeOffset, latitudeOffset);
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void ApplyFeatureMove(FeatureMove move, double longitudeOffset, double latitudeOffset) {
        foreach (var state in move.OriginalStates) {
            Editor.Dataset.ReplaceParts(
                state.Feature,
                MapEditService.MoveParts(state.Parts, longitudeOffset, latitudeOffset),
                markDirty: false);
        }
    }

    private void CommitFeatureMove(bool forceCommit = false) {
        if (_featureMove is not { } move) return;

        _featureMove = null;
        ClearKeyboardEditCommand(updateUi: false);
        if (MapViewport.IsMouseCaptured) MapViewport.ReleaseMouseCapture();
        var afterStates = move.OriginalStates.Select(state => CaptureFeatureParts(state.Feature)).ToList();
        var pointerDelta = move.CurrentPointer - move.StartPointer;
        var committed = (forceCommit || pointerDelta.Length >= MinimumCommittedMovePixels) &&
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
        ClearKeyboardEditCommand(updateUi: false);
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

    private bool MoveSelectedFeaturesByDecimeters(double eastDecimeters, double northDecimeters) {
        if (_featureMove is null) BeginFeatureMove();
        if (_featureMove is not { } move) return false;

        var bounds = GeoBounds.FromPoints(move.OriginalStates
            .SelectMany(static state => state.Parts)
            .SelectMany(static part => part));
        if (!bounds.IsValid) {
            CancelFeatureMove();
            return false;
        }

        var eastMeters = eastDecimeters / 10.0;
        var northMeters = northDecimeters / 10.0;
        foreach (var state in move.OriginalStates) {
            Editor.Dataset.ReplaceParts(
                state.Feature,
                MapEditService.MovePartsByMeters(state.Parts, eastMeters, northMeters, bounds.Center.Latitude),
                markDirty: false);
        }

        CommitFeatureMove(forceCommit: true);
        return true;
    }

    private GeoPoint GetPointerGeo(Point pointerPosition) {
        return VectorMapInteraction.ScreenToGeo(
            pointerPosition,
            _centerLat,
            _centerLon,
            int.TryParse(ZoomTextBox.Text, out var zoom) ? ClampZoom(zoom) : GeoConverter.MinZoom,
            GetMapViewportSize(),
            displayTransform: _displayTransform);
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

    private void RefreshAiTagAssistant() {
        var feature = GetSingleSelectedFeature();
        PresetExpander.Visibility = Selection.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RefreshPresetList();
        AiTagAssistantExpander.Visibility = feature is null ? Visibility.Collapsed : Visibility.Visible;
        AiTagSuggestButton.IsEnabled = feature is not null;

        if (feature is null) {
            _aiTagFeatureId = null;
            _aiTagCts?.Cancel();
            ClearAiTagResults("");
            return;
        }

        if (_aiTagFeatureId == feature.Id) {
            UpdateAiTagApplyButton();
            return;
        }

        _aiTagFeatureId = feature.Id;
        _aiTagCts?.Cancel();
        AiTagDescriptionTextBox.Text = "";
        ClearAiTagResults(L.GetString("Main.AiTags.Ready"));
    }

    private MapFeature? GetSingleSelectedFeature() {
        return Selection.Count == 1 ? Selection.Features.FirstOrDefault() : null;
    }

    private async void AiTagSuggest_Click(object sender, RoutedEventArgs e) {
        var feature = GetSingleSelectedFeature();
        if (feature is null) {
            AiTagStatusTextBlock.Text = L.GetString("Main.AiTags.SelectFeature");
            return;
        }

        var description = AiTagDescriptionTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(description)) {
            AiTagStatusTextBlock.Text = L.GetString("Main.AiTags.EnterDescription");
            return;
        }

        _aiTagCts?.Cancel();
        _aiTagCts = new CancellationTokenSource();
        var requestFeatureId = feature.Id;
        var ct = _aiTagCts.Token;

        try {
            AiTagSuggestButton.IsEnabled = false;
            AiTagApplyButton.IsEnabled = false;
            AiTagStatusTextBlock.Text = L.GetString("Main.AiTags.Requesting");
            AiTagSuggestionsItemsControl.ItemsSource = null;

            var request = BetterIdAiClient.CreateTagSuggestionRequest(
                description,
                feature.Attributes,
                GetAiGeometry(feature),
                GetAiLocation(feature));
            var response = await BetterIdAi.GetTagSuggestionsAsync(request, ct);
            if (ct.IsCancellationRequested || GetSingleSelectedFeature()?.Id != requestFeatureId) return;

            var result = BetterIdAiTagSuggestionNormalizer.Normalize(response, feature.Attributes);
            _aiTagSuggestions = result.Suggestions.Select(static suggestion => new AiTagSuggestionItem(suggestion)).ToList();
            AiTagSuggestionsItemsControl.ItemsSource = _aiTagSuggestions;
            AiTagStatusTextBlock.Text = FormatAiTagStatus(result);
            UpdateAiTagApplyButton();
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            Logger.Error("Failed to request BetterID AI tag suggestions", ex);
            ClearAiTagResults(L.Format("Main.AiTags.Failed", ex.Message));
        } finally {
            if (!ct.IsCancellationRequested) {
                AiTagSuggestButton.IsEnabled = GetSingleSelectedFeature() is not null;
            }
        }
    }

    private void AiTagSuggestionCheckBox_Click(object sender, RoutedEventArgs e) {
        UpdateAiTagApplyButton();
    }

    private void AiTagApply_Click(object sender, RoutedEventArgs e) {
        var feature = GetSingleSelectedFeature();
        if (feature is null) {
            AiTagStatusTextBlock.Text = L.GetString("Main.AiTags.SelectFeature");
            return;
        }

        var changes = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var suggestion in _aiTagSuggestions.Where(static suggestion => suggestion.Selected)) {
            changes[suggestion.Key] = suggestion.Action == "remove" ? null : suggestion.Value;
        }
        if (changes.Count == 0) {
            AiTagStatusTextBlock.Text = L.GetString("Main.AiTags.SelectSuggestion");
            return;
        }

        if (!Editor.Execute(SetFeatureAttributesCommand.CreatePatch(feature, changes))) {
            AiTagStatusTextBlock.Text = L.GetString("Main.AiTags.NoChanges");
            return;
        }

        FeatureDataGrid.Items.Refresh();
        UpdateVectorLayer();
        UpdateDocumentUi();
        AiTagStatusTextBlock.Text = L.Format("Main.AiTags.Applied", changes.Count);
        ClearAiTagSuggestionsOnly();
    }

    private void ClearAiTagResults(string status) {
        _aiTagSuggestions = [];
        AiTagSuggestionsItemsControl.ItemsSource = null;
        AiTagStatusTextBlock.Text = status;
        AiTagApplyButton.IsEnabled = false;
    }

    private void ClearAiTagSuggestionsOnly() {
        _aiTagSuggestions = [];
        AiTagSuggestionsItemsControl.ItemsSource = null;
        AiTagApplyButton.IsEnabled = false;
    }

    private void UpdateAiTagApplyButton() {
        AiTagApplyButton.IsEnabled = _aiTagSuggestions.Any(static suggestion => suggestion.Selected);
    }

    private static string FormatAiTagStatus(BetterIdAiNormalizedTagSuggestionResult result) {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.Summary)) lines.Add(result.Summary);
        lines.Add(result.Suggestions.Count == 0
            ? L.GetString("Main.AiTags.NoSuggestions")
            : L.Format("Main.AiTags.SuggestionCount", result.Suggestions.Count));
        lines.AddRange(result.Warnings);
        if (result.Sources.Count > 0) {
            lines.Add(L.Format(
                "Main.AiTags.Sources",
                string.Join("; ", result.Sources.Select(static source => string.IsNullOrWhiteSpace(source.Title) ? source.Url : source.Title))));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string GetAiGeometry(MapFeature feature) {
        if (feature.Osm?.PrimitiveType == OsmPrimitiveType.Relation) return "relation";
        return feature.GeometryType switch {
            MapGeometryType.Point => "point",
            MapGeometryType.LineString => "line",
            MapGeometryType.Polygon => "area",
            _ => "feature"
        };
    }

    private static BetterIdAiLocation? GetAiLocation(MapFeature feature) {
        if (feature.Bounds.IsValid) {
            var center = feature.Bounds.Center;
            return new BetterIdAiLocation(center.Latitude, center.Longitude);
        }

        foreach (var point in feature.Points) {
            if (point.IsValid) return new BetterIdAiLocation(point.Latitude, point.Longitude);
        }

        return null;
    }

    private void SetSelectedFeatures(IEnumerable<MapFeature> features, bool updateGrid = true) {
        var nextFeatures = features.ToList();
        var currentSingleSelection = Selection.Count == 1 ? Selection.Features.FirstOrDefault() : null;
        var nextSingleSelection = nextFeatures.Count == 1 ? nextFeatures[0] : null;
        if (currentSingleSelection is not null && !ReferenceEquals(currentSingleSelection, nextSingleSelection)) {
            _previousSingleSelectedFeature = currentSingleSelection;
        }

        if (Selection.Set(nextFeatures)) _selectionRevision++;

        if (updateGrid) {
            _isUpdatingFeatureSelection = true;
            try {
                var displayedFeatures = GetDisplayedFeatures().ToHashSet();
                FeatureDataGrid.SelectedItems.Clear();
                foreach (var feature in Selection.Features.Take(100).Where(displayedFeatures.Contains)) {
                    FeatureDataGrid.SelectedItems.Add(feature);
                }
                var firstDisplayedSelection = Selection.Features.FirstOrDefault(displayedFeatures.Contains);
                if (firstDisplayedSelection is not null) FeatureDataGrid.ScrollIntoView(firstDisplayedSelection);
            } finally {
                _isUpdatingFeatureSelection = false;
            }
        }
        RefreshAiTagAssistant();
        UpdateVectorLayer();
        UpdateDocumentUi();
    }

    private void EnsureDocument() {
        Editor.EnsureDocument();
    }

    private void FitDocumentToViewport() {
        if (_document is null || !_document.Bounds.IsValid) return;
        var bounds = _document.Bounds;
        var displayBounds = VectorMapInteraction.ToDisplayBounds(bounds, _displayTransform);
        var center = displayBounds.IsValid ? displayBounds.Center : bounds.Center;
        _centerLon = center.Longitude;
        _centerLat = GeoConverter.ClampLatitude(center.Latitude);
        var viewport = new Size(
            Math.Max(320, MapViewport.ActualWidth),
            Math.Max(240, MapViewport.ActualHeight));
        var zoom = VectorMapInteraction.GetFitZoom(bounds, viewport, Math.Min(GeoConverter.MaxZoom, 20), _displayTransform);
        ZoomTextBox.Text = ClampZoom(zoom).ToString();
        ApplyLayerTransforms();
        if (_tileLayers.Count > 0) ScheduleRender(zoom, 0);
    }

    private void RefreshFeatureList() {
        _isUpdatingFeatureSelection = true;
        try {
            var displayedFeatures = GetDisplayedFeatures();
            FeatureDataGrid.ItemsSource = null;
            FeatureDataGrid.ItemsSource = displayedFeatures;
            foreach (var feature in Selection.Features.Take(100).Where(displayedFeatures.Contains)) {
                FeatureDataGrid.SelectedItems.Add(feature);
            }
        } finally {
            _isUpdatingFeatureSelection = false;
        }
    }

    private IReadOnlyList<MapFeature> GetDisplayedFeatures() {
        if (_document is null) return [];

        var query = FeatureSearchTextBox?.Text;
        return FeatureSearchService.Filter(_document.Features, query);
    }

    private bool HasActiveFeatureSearch() {
        return !string.IsNullOrWhiteSpace(FeatureSearchTextBox?.Text);
    }

    private void FocusFeatureSearch() {
        ClearKeyboardEditCommand(updateUi: false);
        FeatureSearchPanel.Visibility = Visibility.Visible;
        FeatureSearchTextBox.Focus();
        FeatureSearchTextBox.SelectAll();
    }

    private void ClearFeatureSearch() {
        if (FeatureSearchTextBox is null) return;

        FeatureSearchTextBox.Clear();
        FeatureSearchPanel.Visibility = Visibility.Collapsed;
        MapViewport.Focus();
    }

    private void UpdateFeatureSearchUi() {
        if (ClearFeatureSearchButton is null) return;

        ClearFeatureSearchButton.IsEnabled = HasActiveFeatureSearch();
    }

    private void UpdateVectorLayer() {
        if (VectorLayer is null || !int.TryParse(ZoomTextBox.Text, out var zoom)) return;

        var dataLayer = GetVectorDataLayer();
        var opacity = dataLayer?.IsVisible == false
            ? 0.0
            : Math.Clamp(dataLayer?.Opacity ?? 1.0, 0.0, 1.0);
        VectorLayer.Opacity = opacity;
        VectorLayer.Visibility = opacity <= 0 ? Visibility.Hidden : Visibility.Visible;
        VectorLayer.UpdateView(
            _document,
            _centerLat,
            _centerLon,
            zoom,
            _panOffsetX,
            _panOffsetY,
            _document?.Revision ?? 0,
            _selectionRevision,
            _displayTransform,
            _hoveredFeature,
            _hoveredVertex);
    }

    private MapImageLayer? GetVectorDataLayer() {
        return _document is null
            ? _settings.ImageLayers.FirstOrDefault(static layer => layer.Kind == MapLayerKind.Data)
            : MapDataLayerSelectionService.FindMapLayer(_document.ActiveDataLayer, _settings.ImageLayers) ??
            _settings.ImageLayers.FirstOrDefault(static layer => layer.Kind == MapLayerKind.Data);
    }

    private void RefreshPresetList() {
        if (PresetComboBox is null || PresetSearchTextBox is null) return;
        var geometry = Selection.Features.Select(static feature => feature.GeometryType).Distinct().ToList();
        var geometryFilter = geometry.Count == 1 ? geometry[0] : (MapGeometryType?)null;
        var selectedId = (PresetComboBox.SelectedItem as TagPreset)?.Id;
        var presets = TagPresetCatalog.Search(PresetSearchTextBox.Text, geometryFilter);
        PresetComboBox.ItemsSource = presets;
        PresetComboBox.SelectedItem = presets.FirstOrDefault(preset => preset.Id == selectedId) ?? presets.FirstOrDefault();
    }

    private void SynchronizeActiveDataLayer() {
        if (_document is null) return;

        MapDataLayerSelectionService.SelectPrimaryDataLayer(_document, _settings.ImageLayers);
    }

    private void UpdateDocumentUi() {
        var total = _document?.Features.Count ?? 0;
        var hidden = _document?.Features.Count(static feature => feature.IsHidden) ?? 0;
        var displayed = HasActiveFeatureSearch() ? GetDisplayedFeatures().Count : total;
        var command = _keyboardEditCommand.Length > 0 ? L.Format("Main.Status.Command", _keyboardEditCommand) : "";
        if (HasActiveFeatureSearch()) {
            _viewModel.FeatureCount = hidden > 0
                ? L.Format("Main.FeatureSearchCountWithHidden", displayed, total, hidden)
                : L.Format("Main.FeatureSearchCount", displayed, total);
        } else {
            _viewModel.FeatureCount = hidden > 0
                ? L.Format("Main.FeatureCountWithHidden", total, hidden)
                : total.ToString("N0", CultureInfo.CurrentCulture);
        }
        if (_document is null) {
            _viewModel.DocumentStatus = L.Format("Main.Status.NoMapOpen", command);
            return;
        }

        var dirty = _document.IsDirty ? " *" : "";
        var selection = Selection.Count > 0 ? L.Format("Main.Status.Selection", Selection.Count) : "";
        var area = _selectionBounds.HasValue ? L.GetString("Main.Status.DownloadAreaSelected") : "";
        var skipped = _document.SkippedFeatureCount > 0 ? L.Format("Main.Status.Skipped", _document.SkippedFeatureCount) : "";
        _viewModel.DocumentStatus = L.Format(
            "Main.Status.Document",
            _document.Name,
            dirty,
            total,
            selection,
            area,
            skipped,
            command);
    }

    private void RefreshLocalizedText() {
        SetEditorMode(_editorMode);
        UpdateDocumentUi();
        UpdateSourceSummary();
        RefreshLayerList(LayerListBox.SelectedItem as MapImageLayer ?? _settings.GetActiveLayer());
        RefreshPresetList();
        RebuildPresetToolbar();
    }

    private bool ConfirmDiscardChanges(string message) {
        if (_document?.IsDirty != true &&
            !Editor.HasDraftLine &&
            _featureRotation is null &&
            _featureMove is null &&
            _vertexMove is null &&
            _segmentExtrude is null) {
            return true;
        }
        return MessageBox.Show(
            message,
            L.GetString("Main.UnsavedChangesTitle"),
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
        if (_vertexMove is not null) {
            CancelVertexMove();
            return;
        }
        if (_segmentExtrude is not null) {
            CancelSegmentExtrude();
            return;
        }

        ClearKeyboardEditCommand(updateUi: false);
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
        var payload = new {
            bounds = _selectionBounds,
            documentName = _document?.Name,
            sourcePath = _document?.SourcePath
        };
        if (await TryExecuteOptionalPluginCommandAsync(
                OsmTransferPluginId,
                OsmTransferPluginName,
                commandId,
                payload)) {
            return;
        }

        switch (commandId) {
            case "download":
                await DownloadOsmSelectionAsync();
                break;
            case "upload":
                await UploadOsmChangesAsync();
                break;
            case "accounts":
                new Views.OsmAccountsWindow(_osmAccountStore) { Owner = this }.ShowDialog();
                break;
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
        if (!ConfirmDiscardChanges(L.GetString("Main.Discard.OsmDownload"))) return false;

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
            document.Name = L.Format("Main.OsmDownloadDocumentName", DateTime.Now);
            document.SourcePath = null;
            document.SourceFormat = SpatialFileFormat.OsmXml;
            document.MarkClean(compactOsmHistory: true);
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
        if (_vertexMove is not null) CommitVertexMove();
        if (_segmentExtrude is not null) CommitSegmentExtrude();
        if (Editor.HasDraftLine) FinishDraftLine();

        if (_document is null) {
            MessageBox.Show(L.GetString("Main.OsmUpload.NoMap"), L.GetString("Main.OsmUpload.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show(L.GetString("Main.OsmUpload.NoCredential"), L.GetString("Main.OsmUpload.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var preview = OsmChangeSerializer.Build(_document, 0);
        if (preview.TotalCount == 0) {
            MessageBox.Show(L.GetString("Main.OsmUpload.NoChanges"), L.GetString("Main.OsmUpload.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var uploadWindow = new Views.OsmUploadWindow(
            account,
            preview,
            () => OsmChangeSerializer.Build(_document, 0),
            (feature, metadata) => Editor.Execute(new SetFeatureOsmMetadataCommand(feature, metadata)),
            _document,
            BetterIdAi) { Owner = this };
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
            _viewModel.DocumentStatus = L.GetString("Main.OsmUpload.CreatingChangeset");
            changesetId = await api.CreateChangesetAsync(
                account.ApiBaseUrl,
                credential,
                uploadWindow.Comment,
                uploadWindow.Source,
                uploadWindow.ReviewRequested,
                CancellationToken.None);
            var changes = OsmChangeSerializer.Build(_document, changesetId.Value);
            _viewModel.DocumentStatus = L.Format("Main.OsmUpload.UploadingChanges", changes.TotalCount);
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
            MessageBox.Show(L.Format("Main.OsmUpload.Completed", changesetId.Value), L.GetString("Main.OsmUpload.CompletedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
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
        SynchronizeActiveDataLayer();
        var rasterLayers = LayerRenderPlanner.GetLayersToRender(_settings.ImageLayers);
        RefreshLayerList(selectedLayer ?? _settings.GetActiveLayer());
        LoadImageLayers(rasterLayers);
    }

    private void ReorderImageryLayers() {
        if (!AppSettingsService.RotateRasterLayerOrder(_settings)) return;

        _settingsWriter.Save(_settings, this);
        RefreshRenderedLayerFromStack(_settings.GetActiveLayer());
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
            _viewModel.ActiveLayerStatus = layer?.Name ?? "";
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
            Name = TileSourceNameService.CreateUniqueName(_settings.TileSources, baseName),
            Source = sourceUrl,
            MapMaxZoom = GeoConverter.MaxZoom,
            ImageMaxZoom = GeoConverter.MaxZoom,
            IsVisible = true,
            IsKnownSource = false
        };
        ShowSourceSafetyWarning(source);
        _settings.TileSources.Add(source);
        _settingsWriter.Save(_settings, this);
        RefreshImageryMenu();
        return source;
    }

    private void ShowSourceSafetyWarning(TileSourcePreset source) {
        var warningKind = TileSourceSafetyService.GetWarningKind(source.Name, source.Source);
        if (warningKind == TileSourceSafetyWarningKind.None) return;

        MessageBox.Show(
            TileSourceSafetyService.GetWarningMessage(warningKind),
            L.GetString("Common.Warning"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
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

        ZoomLimitTextBlock.Text = L.Format(
            "Main.ZoomLimit",
            mapMaxZoom ?? _activeSource.MapMaxZoom,
            imageMaxZoom ?? _activeSource.ImageMaxZoom);
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

    private sealed record TileLayerLoadResult(TileRenderGroup Group, int TileCount, int RequestedTileCount);

    private sealed record TileLayerContext(
        string LayerId,
        TileService Service,
        TileSourcePreset Source,
        double Opacity);

    private sealed class AiTagSuggestionItem {
        public AiTagSuggestionItem(BetterIdAiNormalizedTagSuggestion suggestion) {
            Key = suggestion.Key;
            Value = suggestion.Value;
            Action = suggestion.Action;
            ProposedText = suggestion.ProposedText;
            DetailText = FormatDetailText(suggestion);
            Reason = string.IsNullOrWhiteSpace(suggestion.Reason)
                ? L.GetString("Main.AiTags.NoReason")
                : suggestion.Reason;
            Selected = suggestion.Selected;
        }

        public string Key { get; }

        public string Value { get; }

        public string Action { get; }

        public string ProposedText { get; }

        public string DetailText { get; }

        public string Reason { get; }

        public bool Selected { get; set; }

        private static string FormatDetailText(BetterIdAiNormalizedTagSuggestion suggestion) {
            var confidence = suggestion.ConfidenceLabel switch {
                "high" => L.GetString("Main.AiTags.Confidence.High"),
                "medium" => L.GetString("Main.AiTags.Confidence.Medium"),
                _ => L.GetString("Main.AiTags.Confidence.Low")
            };
            if (suggestion.ConfidenceScore.HasValue) {
                confidence = $"{confidence} {suggestion.ConfidenceScore.Value:P0}";
            }

            return suggestion.CurrentValue is null
                ? confidence
                : L.Format("Main.AiTags.CurrentValue", confidence, suggestion.CurrentValue);
        }
    }

    private enum EditorMode {
        Pan,
        Select,
        BoxSelect,
        DrawLine,
        Rotate,
        Move,
        VertexMove,
        Extrude
    }

    private enum SegmentExtrudeConstraint {
        Free,
        Horizontal,
        Vertical,
        SegmentNormal
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

    private sealed class VertexMove {
        public VertexMove(
            EditorMode previousMode,
            IReadOnlyList<FeaturePartsSnapshot> originalStates,
            IReadOnlyList<VertexEditTarget> targets,
            Point startPointer) {
            PreviousMode = previousMode;
            OriginalStates = originalStates;
            Targets = targets;
            StartPointer = startPointer;
            CurrentPointer = startPointer;
        }

        public EditorMode PreviousMode { get; }

        public IReadOnlyList<FeaturePartsSnapshot> OriginalStates { get; }

        public IReadOnlyList<VertexEditTarget> Targets { get; }

        public Point StartPointer { get; }

        public Point CurrentPointer { get; set; }
    }

    private sealed class SegmentExtrude {
        public SegmentExtrude(
            EditorMode previousMode,
            FeaturePartsSnapshot originalState,
            SegmentHit hit,
            Point startPointer,
            GeoPoint startPointerGeo,
            SegmentExtrudeConstraint constraint) {
            PreviousMode = previousMode;
            OriginalState = originalState;
            Hit = hit;
            StartPointer = startPointer;
            CurrentPointer = startPointer;
            StartPointerGeo = startPointerGeo;
            Constraint = constraint;
        }

        public EditorMode PreviousMode { get; }

        public FeaturePartsSnapshot OriginalState { get; }

        public SegmentHit Hit { get; }

        public Point StartPointer { get; }

        public Point CurrentPointer { get; set; }

        public GeoPoint StartPointerGeo { get; }

        public SegmentExtrudeConstraint Constraint { get; }
    }

    private readonly record struct VertexEditTarget(MapFeature Feature, int PartIndex, int PointIndex);

    private static double ClampWorldPixel(double value, double worldSize) {
        return Math.Clamp(value, 0, worldSize);
    }
}
