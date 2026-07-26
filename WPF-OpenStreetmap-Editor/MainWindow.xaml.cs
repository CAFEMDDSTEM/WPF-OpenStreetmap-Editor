using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    public MainWindow() {
        InitializeComponent();
        _translate = new TranslateTransform();
        MapCanvas.RenderTransform = _translate;
        _mapScrollViewer = this.FindName("MapScrollViewer") as ScrollViewer;

        var defaultUrl = "https://services.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer/tile/{z}/{-y}/{x}";
        Loaded += (_, _) => {
            MapUrlTextBox.Text = defaultUrl;
            LoadMapFromUrl(defaultUrl);
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
        _tileService.IsTms = false;
        _tileService.TileTemplate = url;

        var t = type.Trim().ToUpperInvariant();
        if (t == "TMS") _tileService.IsTms = true;

        var template = url;
        if (template.Contains("{-y}")) {
            _tileService.IsTms = true;
            template = template.Replace("{-y}", "{y}");
        }
        template = template.Replace("{zoom}", "{z}");

        if (!string.IsNullOrEmpty(template) &&
            (template.IndexOf("mapbox.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
             template.IndexOf("api.mapbox", StringComparison.OrdinalIgnoreCase) >= 0)) {
            template = template.Replace("{access_token}", AccessTokenTextBox.Text);
        }

        _tileService.TileTemplate = template;
        int zoom = int.TryParse(ZoomTextBox.Text, out var z) ? z : 2;
        _ = RenderTilesAsync(zoom);
    }

    private async Task RenderTilesAsync(int z) {
        const int tileSize = GeoConverter.TileSize;

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

        double viewportW = _mapScrollViewer?.ViewportWidth ?? MapCanvas.ActualWidth;
        double viewportH = _mapScrollViewer?.ViewportHeight ?? MapCanvas.ActualHeight;

        if (viewportW <= 0) viewportW = 1024;
        if (viewportH <= 0) viewportH = 768;

        int tilesX = Math.Max(3, (int)Math.Ceiling(viewportW / tileSize) + 2);
        int tilesY = Math.Max(3, (int)Math.Ceiling(viewportH / tileSize) + 2);

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

        var tasks = new List<Task>();
        string accessToken = AccessTokenTextBox.Text;

        for (int ty = 0; ty < tilesY; ty++) {
            for (int tx = 0; tx < tilesX; tx++) {
                int tileX = startX + tx;
                int tileY = startY + ty;
                if (tileY < 0 || tileY >= n) continue;

                tasks.Add(Task.Run(async () => {
                    var img = await _tileService.GetTileAsync(z, tileX, tileY, accessToken).ConfigureAwait(false);
                    if (img != null) {
                        double left = tx * tileSize;
                        double top = ty * tileSize;
                        await Dispatcher.InvokeAsync(() => {
                            var iv = new Image {
                                Width = tileSize,
                                Height = tileSize,
                                Source = img
                            };
                            Canvas.SetLeft(iv, left);
                            Canvas.SetTop(iv, top);
                            MapCanvas.Children.Add(iv);
                        });
                    }
                }));
            }
        }

        try { await Task.WhenAll(tasks); }
        catch (Exception ex) { Logger.Error("Tile rendering failed", ex); }

        if (_mapScrollViewer != null) {
            double vw = _mapScrollViewer.ViewportWidth > 0
                ? _mapScrollViewer.ViewportWidth
                : _mapScrollViewer.ActualWidth;
            double vh = _mapScrollViewer.ViewportHeight > 0
                ? _mapScrollViewer.ViewportHeight
                : _mapScrollViewer.ActualHeight;
            double offsetX = Math.Max(0, (MapCanvas.Width - vw) / 2.0);
            double offsetY = Math.Max(0, (MapCanvas.Height - vh) / 2.0);
            await Dispatcher.InvokeAsync(() => {
                _mapScrollViewer.ScrollToHorizontalOffset(offsetX);
                _mapScrollViewer.ScrollToVerticalOffset(offsetY);
            });
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
        if (e.Delta > 0) ZoomIn_Click(this, new RoutedEventArgs());
        else ZoomOut_Click(this, new RoutedEventArgs());
        e.Handled = true;
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

            var (oldPx, oldPy) = GeoConverter.LatLonToPixelXY(_centerLat, _centerLon, zoom);

            double shiftX = _translate?.X ?? 0;
            double shiftY = _translate?.Y ?? 0;

            double newPx = oldPx - shiftX;
            double newPy = oldPy - shiftY;

            (_centerLat, _centerLon) = GeoConverter.PixelXYToLatLon(newPx, newPy, zoom);

            if (_translate != null) {
                _translate.X = 0;
                _translate.Y = 0;
            }

            _ = RenderTilesAsync(zoom);
        } catch (Exception ex) {
            Logger.Error("Pan update failed", ex);
        }
    }
}
