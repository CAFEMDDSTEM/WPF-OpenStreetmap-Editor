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
    private double _centerLon;
    private double _centerLat;
    private bool _isPanning;
    private Point _panStart;
    private double _startTranslateX;
    private double _startTranslateY;
    private TranslateTransform? _translate;
    private ScrollViewer? _mapScrollViewer;
    private CancellationTokenSource? _renderCts;
    private CancellationTokenSource? _zoomDebounceCts;
    private static readonly ConcurrentDictionary<string, BitmapSource?> TileCache = new();
    private const int MaxTileCache = 500;
    private static readonly SemaphoreSlim TileThrottle = new(6, 6);

    public MainWindow() {
        InitializeComponent();
        _translate = new();
        MapCanvas.RenderTransform = _translate;
        _mapScrollViewer = FindName("MapScrollViewer") as ScrollViewer;

        var defaultUrl = "https://services.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer/tile/{z}/{-y}/{x}";
        Loaded += (_, _) => {
            MapUrlTextBox.Text = defaultUrl;
            System.Diagnostics.Debug.WriteLine($"DEFAULT URL: {defaultUrl}");
            System.Diagnostics.Debug.WriteLine($"TILE TEMPLATE BEFORE: {_tileService.TileTemplate}");
            LoadMapFromUrl(defaultUrl);
            System.Diagnostics.Debug.WriteLine($"TILE TEMPLATE AFTER: {_tileService.TileTemplate}");
        };
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
        _renderCts?.Cancel();
        _renderCts = new();
        var ct = _renderCts.Token;

        if (_translate is { } translate) {
            translate.X = 0;
            translate.Y = 0;
        }

        const int tileSize = GeoConverter.TileSize;

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

        if (ct.IsCancellationRequested) return;

        var viewportW = _mapScrollViewer?.ViewportWidth ?? MapCanvas.ActualWidth;
        var viewportH = _mapScrollViewer?.ViewportHeight ?? MapCanvas.ActualHeight;

        if (viewportW <= 0) viewportW = 1024;
        if (viewportH <= 0) viewportH = 768;

        var tilesX = Math.Max(5, (int)Math.Ceiling(viewportW / tileSize) + 4);
        var tilesY = Math.Max(5, (int)Math.Ceiling(viewportH / tileSize) + 4);

        var n = GeoConverter.GetTileCount(z);
        var (centerTileX, centerTileY) = GeoConverter.LatLonToTileXY(_centerLat, _centerLon, z);

        var startX = centerTileX - tilesX / 2;
        var startY = centerTileY - tilesY / 2;

        var canvasW = tilesX * tileSize;
        var canvasH = tilesY * tileSize;

        await Dispatcher.InvokeAsync(() => {
            MapCanvas.Width = canvasW;
            MapCanvas.Height = canvasH;
            MapCanvas.Children.Clear();
        });

        if (ct.IsCancellationRequested) return;

        List<Task> tasks = [];
        var accessToken = AccessTokenTextBox.Text;

        for (int ty = 0; ty < tilesY; ty++) {
            for (int tx = 0; tx < tilesX; tx++) {
                var tileX = startX + tx;
                var tileY = startY + ty;
                if (tileX < 0 || tileX >= n || tileY < 0 || tileY >= n) continue;

                var capturedTx = tx;
                var capturedTy = ty;
                tasks.Add(Task.Run(async () => {
                    try {
                        var tileKey = $"{z}/{tileX}/{tileY}";
                        if (TileCache.TryGetValue(tileKey, out var cached) && cached is not null) {
                            if (ct.IsCancellationRequested) return;
                            var left = capturedTx * tileSize;
                            var top = capturedTy * tileSize;
                            await Dispatcher.InvokeAsync(() => {
                                if (ct.IsCancellationRequested) return;
                                var iv = new Image { Width = tileSize, Height = tileSize, Source = cached };
                                Canvas.SetLeft(iv, left);
                                Canvas.SetTop(iv, top);
                                MapCanvas.Children.Add(iv);
                            });
                            return;
                        }

                        await TileThrottle.WaitAsync(ct).ConfigureAwait(false);
                        try {
                            var bytes = await _tileService.GetTileBytesAsync(z, tileX, tileY, accessToken).ConfigureAwait(false);
                            if (bytes is null) return;
                            if (ct.IsCancellationRequested) return;

                            var source = LoadTileImage(bytes);
                            if (source is null) return;
                            if (TileCache.Count < MaxTileCache)
                                TileCache.TryAdd(tileKey, source);

                            var left = capturedTx * tileSize;
                            var top = capturedTy * tileSize;
                            await Dispatcher.InvokeAsync(() => {
                                if (ct.IsCancellationRequested) return;
                                var iv = new Image { Width = tileSize, Height = tileSize, Source = source };
                                Canvas.SetLeft(iv, left);
                                Canvas.SetTop(iv, top);
                                MapCanvas.Children.Add(iv);
                            });
                        } finally {
                            TileThrottle.Release();
                        }
                    } catch (OperationCanceledException) {
                    } catch (Exception ex) {
                        Logger.Error($"Tile task failed ({z},{tileX},{tileY})", ex);
                    }
                }));
            }
        }

        try { await Task.WhenAll(tasks); } catch (Exception ex) { Logger.Error("Tile rendering failed", ex); }

        if (ct.IsCancellationRequested) return;

        if (_mapScrollViewer is { } mapScrollViewer) {
            var vw = mapScrollViewer.ViewportWidth > 0
                ? mapScrollViewer.ViewportWidth
                : mapScrollViewer.ActualWidth;
            var vh = mapScrollViewer.ViewportHeight > 0
                ? mapScrollViewer.ViewportHeight
                : mapScrollViewer.ActualHeight;

            var (centerPx, centerPy) = GeoConverter.LatLonToPixelXY(_centerLat, _centerLon, z);
            var centerCanvasX = centerPx - startX * tileSize;
            var centerCanvasY = centerPy - startY * tileSize;
            var offsetX = Math.Max(0, centerCanvasX - vw / 2.0);
            var offsetY = Math.Max(0, centerCanvasY - vh / 2.0);

            await Dispatcher.InvokeAsync(() => {
                mapScrollViewer.ScrollToHorizontalOffset(offsetX);
                mapScrollViewer.ScrollToVerticalOffset(offsetY);
            });
        }
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

    private void SetZoomAndRender(int z) {
        ZoomTextBox.Text = z.ToString();
        if (!string.IsNullOrEmpty(_tileService.TileTemplate)) {
            _ = RenderTilesAsync(z);
        }
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
        _zoomDebounceCts?.Cancel();
        _zoomDebounceCts = new();
        var ct = _zoomDebounceCts.Token;
        var zoomIn = e.Delta > 0;
        e.Handled = true;
        _ = Task.Run(async () => {
            try {
                await Task.Delay(200, ct);
                await Dispatcher.InvokeAsync(() => {
                    if (zoomIn) ZoomIn_Click(this, new());
                    else ZoomOut_Click(this, new());
                });
            } catch (OperationCanceledException) {
            }
        });
    }

    private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        _isPanning = true;
        _panStart = e.GetPosition(this);
        _startTranslateX = _translate?.X ?? 0;
        _startTranslateY = _translate?.Y ?? 0;
        MapCanvas.CaptureMouse();
        Cursor = Cursors.Hand;
    }

    private void MapCanvas_MouseMove(object sender, MouseEventArgs e) {
        if (!_isPanning) return;
        var pos = e.GetPosition(this);
        if (_translate != null) {
            _translate.X = _startTranslateX + (pos.X - _panStart.X);
            _translate.Y = _startTranslateY + (pos.Y - _panStart.Y);
        }
    }

    private void MapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        if (!_isPanning) return;
        _isPanning = false;
        MapCanvas.ReleaseMouseCapture();
        Cursor = Cursors.Arrow;

        try {
            if (!int.TryParse(ZoomTextBox.Text, out var zoom)) return;

            var shiftX = _translate?.X ?? 0;
            var shiftY = _translate?.Y ?? 0;

            if (shiftX == 0 && shiftY == 0)
                return;

            var (oldPx, oldPy) = GeoConverter.LatLonToPixelXY(_centerLat, _centerLon, zoom);
            var newPx = oldPx - shiftX;
            var newPy = oldPy - shiftY;
            (_centerLat, _centerLon) = GeoConverter.PixelXYToLatLon(newPx, newPy, zoom);
        } catch (Exception ex) {
            Logger.Error("Pan update failed", ex);
        }
    }
}
