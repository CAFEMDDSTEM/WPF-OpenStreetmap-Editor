using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        _translate = new TranslateTransform();
        MapCanvas.RenderTransform = _translate;
        _mapScrollViewer = this.FindName("MapScrollViewer") as ScrollViewer;

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

        int zoom = int.TryParse(ZoomTextBox.Text, out var z) ? z : 2;
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
        int zoom = int.TryParse(ZoomTextBox.Text, out var z) ? z : 2;
        _ = RenderTilesAsync(zoom);
    }

    private async Task RenderTilesAsync(int z) {
        _renderCts?.Cancel();
        _renderCts = new CancellationTokenSource();
        var ct = _renderCts.Token;

        if (_translate != null) {
            _translate.X = 0;
            _translate.Y = 0;
        }

        const int tileSize = GeoConverter.TileSize;

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

        if (ct.IsCancellationRequested) return;

        double viewportW = _mapScrollViewer?.ViewportWidth ?? MapCanvas.ActualWidth;
        double viewportH = _mapScrollViewer?.ViewportHeight ?? MapCanvas.ActualHeight;

        if (viewportW <= 0) viewportW = 1024;
        if (viewportH <= 0) viewportH = 768;

        int tilesX = Math.Max(5, (int)Math.Ceiling(viewportW / tileSize) + 4);
        int tilesY = Math.Max(5, (int)Math.Ceiling(viewportH / tileSize) + 4);

        int n = GeoConverter.GetTileCount(z);
        var (centerTileX, centerTileY) = GeoConverter.LatLonToTileXY(_centerLat, _centerLon, z);

        int startX = centerTileX - tilesX / 2;
        int startY = centerTileY - tilesY / 2;

        double canvasW = tilesX * tileSize;
        double canvasH = tilesY * tileSize;

        await Dispatcher.InvokeAsync(() => {
            MapCanvas.Width = canvasW;
            MapCanvas.Height = canvasH;
            MapCanvas.Children.Clear();
        });

        if (ct.IsCancellationRequested) return;

        var tasks = new List<Task>();
        string accessToken = AccessTokenTextBox.Text;

        for (int ty = 0; ty < tilesY; ty++) {
            for (int tx = 0; tx < tilesX; tx++) {
                int tileX = startX + tx;
                int tileY = startY + ty;
                if (tileX < 0 || tileX >= n || tileY < 0 || tileY >= n) continue;

                int capturedTx = tx;
                int capturedTy = ty;
                tasks.Add(Task.Run(async () => {
                    try {
                        string tileKey = $"{z}/{tileX}/{tileY}";
                        if (TileCache.TryGetValue(tileKey, out var cached) && cached != null) {
                            if (ct.IsCancellationRequested) return;
                            double left = capturedTx * tileSize;
                            double top = capturedTy * tileSize;
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
                            if (bytes == null) return;
                            if (ct.IsCancellationRequested) return;

                            var source = LoadTileImage(bytes);
                            if (source == null) return;
                            if (TileCache.Count < MaxTileCache)
                                TileCache.TryAdd(tileKey, source);

                            double left = capturedTx * tileSize;
                            double top = capturedTy * tileSize;
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

        try { await Task.WhenAll(tasks); }
        catch (Exception ex) { Logger.Error("Tile rendering failed", ex); }

        if (ct.IsCancellationRequested) return;

        if (_mapScrollViewer != null) {
            double vw = _mapScrollViewer.ViewportWidth > 0
                ? _mapScrollViewer.ViewportWidth
                : _mapScrollViewer.ActualWidth;
            double vh = _mapScrollViewer.ViewportHeight > 0
                ? _mapScrollViewer.ViewportHeight
                : _mapScrollViewer.ActualHeight;

            var (centerPx, centerPy) = GeoConverter.LatLonToPixelXY(_centerLat, _centerLon, z);
            double centerCanvasX = centerPx - startX * tileSize;
            double centerCanvasY = centerPy - startY * tileSize;
            double offsetX = Math.Max(0, centerCanvasX - vw / 2.0);
            double offsetY = Math.Max(0, centerCanvasY - vh / 2.0);

            await Dispatcher.InvokeAsync(() => {
                _mapScrollViewer.ScrollToHorizontalOffset(offsetX);
                _mapScrollViewer.ScrollToVerticalOffset(offsetY);
            });
        }
    }

    private static BitmapSource? LoadTileImage(byte[] data) {
        try {
            using var ms = new MemoryStream(data);
            using var img = System.Drawing.Image.FromStream(ms);
            using var bitmap = new System.Drawing.Bitmap(img);
            var hBitmap = bitmap.GetHbitmap();
            try {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            } finally {
                NativeMethods.DeleteObject(hBitmap);
            }
        } catch (Exception ex) {
            Logger.Error("Failed to decode image bytes", ex);
            return null;
        }
    }

    private void TopMenu_Click(object sender, RoutedEventArgs e) {
        if (sender is Button btn && btn.ContextMenu != null) {
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
            ZoomIn_Click(this, new RoutedEventArgs());
            e.Handled = true;
        } else if (e.Key == Key.Subtract || e.Key == Key.OemMinus) {
            ZoomOut_Click(this, new RoutedEventArgs());
            e.Handled = true;
        } else if (e.Key == Key.PageUp) {
            ZoomIn_Click(this, new RoutedEventArgs());
            e.Handled = true;
        } else if (e.Key == Key.PageDown) {
            ZoomOut_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void MapCanvas_MouseWheel(object sender, MouseWheelEventArgs e) {
        _zoomDebounceCts?.Cancel();
        _zoomDebounceCts = new CancellationTokenSource();
        var ct = _zoomDebounceCts.Token;
        bool zoomIn = e.Delta > 0;
        e.Handled = true;
        _ = Task.Run(async () => {
            try {
                await Task.Delay(200, ct);
                await Dispatcher.InvokeAsync(() => {
                    if (zoomIn) ZoomIn_Click(this, new RoutedEventArgs());
                    else ZoomOut_Click(this, new RoutedEventArgs());
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

            double shiftX = _translate?.X ?? 0;
            double shiftY = _translate?.Y ?? 0;

            if (shiftX == 0 && shiftY == 0)
                return;

            var (oldPx, oldPy) = GeoConverter.LatLonToPixelXY(_centerLat, _centerLon, zoom);
            double newPx = oldPx - shiftX;
            double newPy = oldPy - shiftY;
            (_centerLat, _centerLon) = GeoConverter.PixelXYToLatLon(newPx, newPy, zoom);
        } catch (Exception ex) {
            Logger.Error("Pan update failed", ex);
        }
    }
}

internal static class NativeMethods {
    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);
}
