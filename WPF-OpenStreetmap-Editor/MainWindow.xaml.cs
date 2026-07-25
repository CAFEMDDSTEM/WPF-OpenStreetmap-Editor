using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Net.Http;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace WPF_OpenStreetmap_Editor {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {
        private readonly HttpClient _http = new HttpClient();
        private string? _tileTemplate;
        private bool _isTms;
        private string _cacheRoot = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Cache", "tiles");
        // 当前视图中心（经纬度），默认世界中心
        private double _centerLon = 0.0;
        private double _centerLat = 0.0;
        // panning state
        private bool _isPanning = false;
        private System.Windows.Point _panStart;
        private double _startTranslateX = 0;
        private double _startTranslateY = 0;
        private TranslateTransform? _translate;
        private ScrollViewer? _mapScrollViewer;

        public MainWindow() {
            InitializeComponent();
            _translate = new TranslateTransform();
            MapCanvas.RenderTransform = _translate;
            _mapScrollViewer = this.FindName("MapScrollViewer") as ScrollViewer;
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {

        }

        // 打开按钮关联的 ContextMenu
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
            // 打开图层管理窗口
            var win = new Views.LayersWindow {
                Owner = this
            };
            win.ShowDialog();
        }

        private void Show_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem mi) {
                MessageBox.Show($"{mi.Header} 被点击", "菜单", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // 公共方法，供 LayersWindow 调用以加载地图 URL
        public void LoadMapFromUrl(string url) {
            if (string.IsNullOrEmpty(url))
                return;
            MapUrlTextBox.Text = url;
            // 处理占位符
            _isTms = false;
            _tileTemplate = url;
            if (_tileTemplate.Contains("{-y}")) {
                _isTms = true;
                _tileTemplate = _tileTemplate.Replace("{-y}", "{y}");
            }
            _tileTemplate = _tileTemplate.Replace("{zoom}", "{z}");

            // 启动渲染，读取缩放控件
            int zoom = 2;
            if (int.TryParse(ZoomTextBox.Text, out var z)) zoom = z;
            _ = RenderTilesAsync(zoom);
        }

        private void LoadMapButton_Click(object sender, RoutedEventArgs e) {
            var url = MapUrlTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(url)) {
                MessageBox.Show("请输入地图 URL", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            LoadMapFromUrl(url);
        }

        private string GetCachePath(int z, int x, int y) {
            var dir = Path.Combine(_cacheRoot, z.ToString(), x.ToString());
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, y + ".png");
        }

        private async Task<BitmapImage?> GetTileAsync(int z, int x, int y, CancellationToken ct = default) {
            try {
                if (string.IsNullOrEmpty(_tileTemplate)) return null;
                int n = 1 << z;
                // wrap x horizontally
                int xWrapped = ((x % n) + n) % n;
                int yForUrl = y;
                if (_isTms) {
                    yForUrl = (n - 1) - y;
                }
                if (yForUrl < 0 || yForUrl >= n) return null;
                var url = _tileTemplate.Replace("{z}", z.ToString()).Replace("{x}", xWrapped.ToString()).Replace("{y}", yForUrl.ToString());

                var cache = GetCachePath(z, xWrapped, yForUrl);
                if (File.Exists(cache)) {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    using (var fs = File.OpenRead(cache)) {
                        bi.StreamSource = new MemoryStream();
                        fs.CopyTo(bi.StreamSource);
                        bi.StreamSource.Position = 0;
                    }
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }

                // 下载
                using (var resp = await _http.GetAsync(url, ct).ConfigureAwait(false)) {
                    if (!resp.IsSuccessStatusCode) return null;
                    var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                    // 保存到缓存
                    try {
                        File.WriteAllBytes(cache, bytes);
                    } catch { }
                    // 从字节创建 BitmapImage
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = new MemoryStream(bytes);
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
            } catch {
                return null;
            }
        }

        private async Task RenderTilesAsync(int z) {
            // 使用固定瓦片大小 256
            const int tileSize = 256;

            // 计算需要渲染的瓦片行列数，使地图在 Canvas 中居中显示
            double width = MapCanvas.ActualWidth; if (width <= 0) width = MapCanvas.Width;
            double height = MapCanvas.ActualHeight; if (height <= 0) height = MapCanvas.Height;

            int tilesX = Math.Max(3, (int)Math.Ceiling(width / tileSize));
            int tilesY = Math.Max(3, (int)Math.Ceiling(height / tileSize));

            // 计算中心瓦片索引（XYZ）
            int n = 1 << z;
            double lon = _centerLon;
            double lat = Math.Max(Math.Min(_centerLat, 85.05112878), -85.05112878);
            double latRad = lat * Math.PI / 180.0;
            int centerX = (int)Math.Floor((lon + 180.0) / 360.0 * n);
            int centerY = (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);

            int startX = centerX - tilesX / 2;
            int startY = centerY - tilesY / 2;

            // 设置 Canvas 大小为渲染瓦片的像素大小
            double canvasW = tilesX * tileSize;
            double canvasH = tilesY * tileSize;
            await Dispatcher.InvokeAsync(() => {
                MapCanvas.Width = canvasW;
                MapCanvas.Height = canvasH;
                MapCanvas.Children.Clear();
            });

            var cts = new CancellationTokenSource();
            var tasks = new List<Task>();

            for (int ty = 0; ty < tilesY; ty++) {
                for (int tx = 0; tx < tilesX; tx++) {
                    int tileX = startX + tx;
                    int tileY = startY + ty;
                    // skip out-of-range Y (no wrap vertically)
                    if (tileY < 0 || tileY >= n) continue;
                    tasks.Add(Task.Run(async () => {
                        var img = await GetTileAsync(z, tileX, tileY, cts.Token).ConfigureAwait(false);
                        if (img != null) {
                            double left = tx * tileSize;
                            double top = ty * tileSize;
                            await Dispatcher.InvokeAsync(() => {
                                var iv = new System.Windows.Controls.Image {
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

            try {
                await Task.WhenAll(tasks);
            } catch { }

            // 渲染完成后把视图滚动到居中位置
            await Dispatcher.InvokeAsync(() => {
                try {
                    if (_mapScrollViewer != null) {
                        double viewportW = _mapScrollViewer.ViewportWidth;
                        double viewportH = _mapScrollViewer.ViewportHeight;
                        if (viewportW <= 0) viewportW = _mapScrollViewer.ActualWidth;
                        if (viewportH <= 0) viewportH = _mapScrollViewer.ActualHeight;
                        double offsetX = Math.Max(0, (MapCanvas.Width - viewportW) / 2.0);
                        double offsetY = Math.Max(0, (MapCanvas.Height - viewportH) / 2.0);
                        _mapScrollViewer.ScrollToHorizontalOffset(offsetX);
                        _mapScrollViewer.ScrollToVerticalOffset(offsetY);
                    }
                } catch { }
            });
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e) {
            if (int.TryParse(ZoomTextBox.Text, out var z)) {
                SetZoomAndRender(Math.Min(22, z + 1));
            }
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e) {
            if (int.TryParse(ZoomTextBox.Text, out var z)) {
                SetZoomAndRender(Math.Max(0, z - 1));
            }
        }

        private void SetZoomAndRender(int z) {
            ZoomTextBox.Text = z.ToString();
            if (!string.IsNullOrEmpty(_tileTemplate)) {
                _ = RenderTilesAsync(z);
            }
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
            // 支持 +/- 和 数字键盘 Add/Subtract 以及 PageUp/PageDown
            if (e.Key == System.Windows.Input.Key.Add || e.Key == System.Windows.Input.Key.OemPlus) {
                ZoomIn_Click(this, new RoutedEventArgs());
                e.Handled = true;
            } else if (e.Key == System.Windows.Input.Key.Subtract || e.Key == System.Windows.Input.Key.OemMinus) {
                ZoomOut_Click(this, new RoutedEventArgs());
                e.Handled = true;
            } else if (e.Key == System.Windows.Input.Key.PageUp) {
                ZoomIn_Click(this, new RoutedEventArgs());
                e.Handled = true;
            } else if (e.Key == System.Windows.Input.Key.PageDown) {
                ZoomOut_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void MapCanvas_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) {
            if (e.Delta > 0) ZoomIn_Click(this, new RoutedEventArgs()); else ZoomOut_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }

        private void MapCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            _isPanning = true;
            _panStart = e.GetPosition(this);
            _startTranslateX = _translate?.X ?? 0;
            _startTranslateY = _translate?.Y ?? 0;
            MapCanvas.CaptureMouse();
            this.Cursor = System.Windows.Input.Cursors.Hand;
        }

        private void MapCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) {
            if (!_isPanning) return;
            var pos = e.GetPosition(this);
            var dx = pos.X - _panStart.X;
            var dy = pos.Y - _panStart.Y;
            if (_translate != null) {
                _translate.X = _startTranslateX + dx;
                _translate.Y = _startTranslateY + dy;
            }
        }

        private void MapCanvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            if (!_isPanning) return;
            _isPanning = false;
            MapCanvas.ReleaseMouseCapture();
            this.Cursor = System.Windows.Input.Cursors.Arrow;
            // After panning, update center lon/lat based on translate and re-render tiles
            try {
                if (int.TryParse(ZoomTextBox.Text, out var z)) {
                    int zoom = z;
                    double world = 256.0 * (1 << zoom);
                    // old center pixel
                    double lon = _centerLon;
                    double lat = Math.Max(Math.Min(_centerLat, 85.05112878), -85.05112878);
                    double latRad = lat * Math.PI / 180.0;
                    double oldCenterPixelX = (lon + 180.0) / 360.0 * world;
                    double oldCenterPixelY = (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * world;

                    // translate is the visual shift of the canvas; map pixels shift opposite direction
                    double newCenterPixelX = oldCenterPixelX - (_translate?.X ?? 0);
                    double newCenterPixelY = oldCenterPixelY - (_translate?.Y ?? 0);

                    // compute new lon/lat from pixel
                    double newLon = newCenterPixelX / world * 360.0 - 180.0;
                    double n = Math.PI - 2.0 * Math.PI * newCenterPixelY / world;
                    double newLat = (180.0 / Math.PI) * Math.Atan(Math.Sinh(n));

                    _centerLon = newLon;
                    _centerLat = newLat;

                    // reset visual translate
                    if (_translate != null) {
                        _translate.X = 0;
                        _translate.Y = 0;
                    }

                    // re-render at same zoom with new center
                    _ = RenderTilesAsync(zoom);
                }
            } catch { }
        }
    }
}