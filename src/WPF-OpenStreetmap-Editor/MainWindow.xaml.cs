using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WPF_OpenStreetmap_Editor.Services;


namespace WPF_OpenStreetmap_Editor;

public partial class MainWindow : Window {
    private readonly TileService _tileService = new();
    private WindowState _lastNonMinimizedWindowState = WindowState.Normal;
    private double _centerLon;
    private double _centerLat;
    private bool _isPanning;
    private Point _panStart;
    private double _panOffsetX;
    private double _panOffsetY;
    private CancellationTokenSource? _renderCts;
    private CancellationTokenSource? _renderDebounceCts;
    private MapLayer? _activeLayer;
    private MapLayer? _fallbackLayer;
    private MapLayer? _stagingLayer;
    private static readonly ConcurrentDictionary<string, BitmapSource?> TileCache = new();
    private const int MaxTileCache = 500;
    private static readonly SemaphoreSlim TileThrottle = new(6, 6);

    public MainWindow() {
        InitializeComponent();
        WindowStartupService.ApplyStartupState(this);
        _lastNonMinimizedWindowState = WindowState;
        StateChanged += Window_StateChanged;
        Closing += (_, _) => WindowStartupService.Save(_lastNonMinimizedWindowState);
        Closed += (_, _) => {
            _renderDebounceCts?.Cancel();
            _renderCts?.Cancel();
            _tileService.Dispose();
        };

        var defaultUrl = "https://services.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer/tile/{z}/{-y}/{x}";
        Loaded += (_, _) => {
            MapUrlTextBox.Text = defaultUrl;
            System.Diagnostics.Debug.WriteLine($"DEFAULT URL: {defaultUrl}");
            System.Diagnostics.Debug.WriteLine($"TILE TEMPLATE BEFORE: {_tileService.TileTemplate}");
            LoadMapFromUrl(defaultUrl);
            System.Diagnostics.Debug.WriteLine($"TILE TEMPLATE AFTER: {_tileService.TileTemplate}");
        };
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
        if (string.IsNullOrEmpty(url))
            return;

        MapUrlTextBox.Text = url;
        _tileService.ParseUrlTemplate(url, AccessTokenTextBox.Text);

        var zoom = int.TryParse(ZoomTextBox.Text, out var z) ? z : 2;
        Dispatcher.InvokeAsync(async () => {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await RenderTilesAsync(zoom);
        }, DispatcherPriority.Background);
    }

    public void LoadLayer(string type, string url) {
        if (string.IsNullOrEmpty(url)) return;
        MapUrlTextBox.Text = url;
        _tileService.ParseUrlTemplate(url, AccessTokenTextBox.Text);
        var t = type.Trim().ToUpperInvariant();
        if (t == "TMS") _tileService.IsTms = true;
        var zoom = int.TryParse(ZoomTextBox.Text, out var z) ? z : 2;
        _ = RenderTilesAsync(zoom);
    }

    private async Task RenderTilesAsync(int z) {
        _renderDebounceCts?.Cancel();
        _renderDebounceCts = null;
        _renderCts?.Cancel();
        var renderCts = new CancellationTokenSource();
        _renderCts = renderCts;
        var ct = renderCts.Token;

        const int tileSize = GeoConverter.TileSize;

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

        if (ct.IsCancellationRequested) return;

        var viewportW = MapViewport.ActualWidth;
        var viewportH = MapViewport.ActualHeight;

        if (viewportW <= 0) viewportW = 1024;
        if (viewportH <= 0) viewportH = 768;

        var n = GeoConverter.GetTileCount(z);
        var (centerPixelX, centerPixelY) = GeoConverter.LatLonToPixelXY(_centerLat, _centerLon, z);
        var startX = (int)Math.Floor((centerPixelX - viewportW / 2.0 - tileSize) / tileSize);
        var endX = (int)Math.Floor((centerPixelX + viewportW / 2.0 + tileSize) / tileSize);
        var startY = (int)Math.Floor((centerPixelY - viewportH / 2.0 - tileSize) / tileSize);
        var endY = (int)Math.Floor((centerPixelY + viewportH / 2.0 + tileSize) / tileSize);

        var canvas = new Canvas {
            Width = viewportW,
            Height = viewportH,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(canvas, BitmapScalingMode.LowQuality);
        var layer = new MapLayer(canvas, z, centerPixelX, centerPixelY, viewportW, viewportH);

        BeginStagingLayer(layer);

        if (ct.IsCancellationRequested) return;

        List<Task> tasks = [];
        var accessToken = AccessTokenTextBox.Text;
        var sourceKey = $"{_tileService.TileTemplate}|{_tileService.IsTms}";
        var loadedTileCount = 0;

        var tileRequests = new List<(int X, int Y, double Distance)>();
        for (var tileY = startY; tileY <= endY; tileY++) {
            if (tileY < 0 || tileY >= n) continue;

            for (var tileX = startX; tileX <= endX; tileX++) {
                var tileCenterX = (tileX + 0.5) * tileSize;
                var tileCenterY = (tileY + 0.5) * tileSize;
                var distanceX = tileCenterX - centerPixelX;
                var distanceY = tileCenterY - centerPixelY;
                tileRequests.Add((tileX, tileY, distanceX * distanceX + distanceY * distanceY));
            }
        }

        foreach (var request in tileRequests.OrderBy(static request => request.Distance)) {
            tasks.Add(LoadAndAddTileAsync(request.X, request.Y));
        }

        try {
            await Task.WhenAll(tasks);
        } catch (OperationCanceledException) {
            return;
        } catch (Exception ex) {
            Logger.Error("Tile rendering failed", ex);
        }

        if (ct.IsCancellationRequested || !ReferenceEquals(_renderCts, renderCts)) return;

        if (loadedTileCount == 0 && _activeLayer is not null) {
            MapLayerHost.Children.Remove(layer.Canvas);
            if (ReferenceEquals(_stagingLayer, layer)) _stagingLayer = null;
            return;
        }

        PromoteStagingLayer(layer);

        async Task LoadAndAddTileAsync(int tileX, int tileY) {
            try {
                var wrappedX = ((tileX % n) + n) % n;
                var tileKey = $"{sourceKey}|{z}/{wrappedX}/{tileY}";
                if (TileCache.TryGetValue(tileKey, out var cached) && cached is not null) {
                    AddTile(cached, tileX, tileY);
                    return;
                }

                await TileThrottle.WaitAsync(ct).ConfigureAwait(false);
                try {
                    var bytes = await _tileService
                        .GetTileBytesAsync(z, tileX, tileY, accessToken, ct)
                        .ConfigureAwait(false);
                    if (bytes is null || ct.IsCancellationRequested) return;

                    var source = LoadTileImage(bytes);
                    if (source is null) return;
                    if (TileCache.Count < MaxTileCache) TileCache.TryAdd(tileKey, source);

                    await Dispatcher.InvokeAsync(() => AddTile(source, tileX, tileY));
                } finally {
                    TileThrottle.Release();
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error($"Tile task failed ({z},{tileX},{tileY})", ex);
            }
        }

        void AddTile(BitmapSource source, int tileX, int tileY) {
            if (ct.IsCancellationRequested || !MapLayerHost.Children.Contains(layer.Canvas)) return;

            var image = new Image {
                Width = tileSize,
                Height = tileSize,
                Source = source
            };
            Canvas.SetLeft(image, tileX * tileSize - centerPixelX + viewportW / 2.0);
            Canvas.SetTop(image, tileY * tileSize - centerPixelY + viewportH / 2.0);
            layer.Canvas.Children.Add(image);
            Interlocked.Increment(ref loadedTileCount);
        }
    }

    private void BeginStagingLayer(MapLayer layer) {
        if (_stagingLayer is not null && !ReferenceEquals(_stagingLayer, _activeLayer)) {
            if (_activeLayer is null) {
                _activeLayer = _stagingLayer;
            } else {
                MapLayerHost.Children.Remove(_stagingLayer.Canvas);
            }
        }

        if (_fallbackLayer is not null && !ReferenceEquals(_fallbackLayer, _activeLayer)) {
            MapLayerHost.Children.Remove(_fallbackLayer.Canvas);
            _fallbackLayer = null;
        }

        _stagingLayer = layer;
        MapLayerHost.Children.Add(layer.Canvas);
        ApplyLayerTransforms();
    }

    private void PromoteStagingLayer(MapLayer layer) {
        if (!ReferenceEquals(_stagingLayer, layer)) return;

        if (_fallbackLayer is not null && !ReferenceEquals(_fallbackLayer, _activeLayer)) {
            MapLayerHost.Children.Remove(_fallbackLayer.Canvas);
        }

        _fallbackLayer = ReferenceEquals(_activeLayer, layer) ? null : _activeLayer;
        _activeLayer = layer;
        _stagingLayer = null;
        ApplyLayerTransforms();
    }

    private void ApplyLayerTransforms() {
        if (!int.TryParse(ZoomTextBox.Text, out var zoom)) return;

        var viewportWidth = MapViewport.ActualWidth;
        var viewportHeight = MapViewport.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        foreach (var layer in GetVisibleLayers()) {
            var scale = Math.Pow(2, zoom - layer.Zoom);
            var (targetCenterX, targetCenterY) = GeoConverter.LatLonToPixelXY(_centerLat, _centerLon, layer.Zoom);
            var offsetX = viewportWidth / 2.0 - scale * layer.ViewportWidth / 2.0
                + scale * (layer.CenterPixelX - targetCenterX) + _panOffsetX;
            var offsetY = viewportHeight / 2.0 - scale * layer.ViewportHeight / 2.0
                + scale * (layer.CenterPixelY - targetCenterY) + _panOffsetY;

            layer.Canvas.RenderTransform = new MatrixTransform(scale, 0, 0, scale, offsetX, offsetY);
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

    private void TopMenu_Click(object sender, RoutedEventArgs e) {
        if (sender is Button { ContextMenu: not null } btn) {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
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

    private void Show_Click(object sender, RoutedEventArgs e) {
        if (sender is MenuItem mi) {
            MessageBox.Show($"{mi.Header} 被点击", "菜单", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void LoadMapButton_Click(object sender, RoutedEventArgs e) {
        var url = MapUrlTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(url)) {
            MessageBox.Show("请输入地图 URL", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        LoadMapFromUrl(url);
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) {
        if (int.TryParse(ZoomTextBox.Text, out var z)) {
            SetZoomAndRender(Math.Min(GeoConverter.MaxZoom, z + 1));
        }
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e) {
        if (int.TryParse(ZoomTextBox.Text, out var z)) {
            SetZoomAndRender(Math.Max(GeoConverter.MinZoom, z - 1));
        }
    }

    private void SetZoomAndRender(int z, Point? anchor = null, bool debounceRender = false) {
        if (!int.TryParse(ZoomTextBox.Text, out var oldZoom)) oldZoom = z;
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
            newCenterY = Math.Clamp(newCenterY, 0, worldSize);
            (_centerLat, _centerLon) = GeoConverter.PixelXYToLatLon(newCenterX, newCenterY, z);
        }

        ZoomTextBox.Text = z.ToString();
        ApplyLayerTransforms();

        if (!string.IsNullOrEmpty(_tileService.TileTemplate)) {
            ScheduleRender(z, debounceRender ? 120 : 0);
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
            ? Math.Min(GeoConverter.MaxZoom, zoom + 1)
            : Math.Max(GeoConverter.MinZoom, zoom - 1);
        SetZoomAndRender(nextZoom, e.GetPosition(MapViewport), debounceRender: true);
    }

    private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        _isPanning = true;
        _panStart = e.GetPosition(MapViewport);
        _panOffsetX = 0;
        _panOffsetY = 0;
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

            if (shiftX == 0 && shiftY == 0)
                return;

            var (oldPx, oldPy) = GeoConverter.LatLonToPixelXY(_centerLat, _centerLon, zoom);
            var newPx = oldPx - shiftX;
            var worldSize = GeoConverter.TileSize * (double)GeoConverter.GetTileCount(zoom);
            var newPy = Math.Clamp(oldPy - shiftY, 0, worldSize);
            (_centerLat, _centerLon) = GeoConverter.PixelXYToLatLon(newPx, newPy, zoom);

            _panOffsetX = 0;
            _panOffsetY = 0;
            ApplyLayerTransforms();
            ScheduleRender(zoom, 0);
        } catch (Exception ex) {
            Logger.Error("Pan update failed", ex);
        } finally {
            _panOffsetX = 0;
            _panOffsetY = 0;
        }
    }

    private void MapViewport_SizeChanged(object sender, SizeChangedEventArgs e) {
        ApplyLayerTransforms();
        if (!IsLoaded || string.IsNullOrEmpty(_tileService.TileTemplate)) return;
        if (!int.TryParse(ZoomTextBox.Text, out var zoom)) return;

        ScheduleRender(zoom, 120);
    }

    private sealed record MapLayer(
        Canvas Canvas,
        int Zoom,
        double CenterPixelX,
        double CenterPixelY,
        double ViewportWidth,
        double ViewportHeight);
}
