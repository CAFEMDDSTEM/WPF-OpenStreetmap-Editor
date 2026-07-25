using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace WPF_OpenStreetmap_Editor.Views {
    /// <summary>
    /// Interaction logic for LayersWindow.xaml
    /// </summary>
    public partial class LayersWindow : Window {
        private readonly string _layersFile = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "layers.json");

        public LayersWindow() {
            InitializeComponent();
            this.Loaded += LayersWindow_Loaded;
            this.Closed += LayersWindow_Closed;
            // 确保 ComboBox 包含 WMTS 选项（有时 XAML 未更新）
            try {
                if (LayerTypeComboBox != null) {
                    bool hasWmts = LayerTypeComboBox.Items.Cast<object>().OfType<ComboBoxItem>().Any(ci => (ci.Content?.ToString() ?? "") == "WMTS");
                    if (!hasWmts) {
                        LayerTypeComboBox.Items.Add(new ComboBoxItem { Content = "WMTS" });
                    }
                }
            } catch { }
        }

        private void LayersWindow_Loaded(object? sender, RoutedEventArgs e) {
            LoadLayersFromFile();
        }

        private void LayersWindow_Closed(object? sender, EventArgs e) {
            SaveLayersToFile();
        }

        private void AddLayer_Click(object sender, RoutedEventArgs e) {
            var url = LayerUrlTextBox.Text?.Trim();
            var type = (LayerTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "WMS";
            if (string.IsNullOrEmpty(url)) {
                MessageBox.Show("请输入图层 URL", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var item = $"[{type}] {url}";
            LayersListBox.Items.Add(item);
            LayerUrlTextBox.Clear();
            // 保存图层到本地
            SaveLayersToFile();
        }

        private void LoadSelected_Click(object sender, RoutedEventArgs e) {
            if (LayersListBox.SelectedItem == null) {
                MessageBox.Show("请选择一个图层以加载", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var item = LayersListBox.SelectedItem.ToString();
            // 提取类型和 URL，格式为 [TYPE] URL
            var type = "";
            var url = item;
            var idx = item.IndexOf(']');
            if (item.StartsWith("[") && idx > 1) {
                type = item.Substring(1, idx - 1).Trim();
                url = idx + 1 < item.Length ? item.Substring(idx + 1).Trim() : "";
            }

            // 调用主窗口方法进行加载（如果 owner 是 MainWindow）
            if (this.Owner is WPF_OpenStreetmap_Editor.MainWindow main) {
                if (!string.IsNullOrEmpty(type)) main.LoadLayer(type, url); else main.LoadMapFromUrl(url);
                this.Close();
            } else {
                MessageBox.Show("未能找到主窗口来加载地图", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }

        }

        private void SaveLayersToFile() {
            try {
                var items = LayersListBox.Items.Cast<object>().Select(i => i.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_layersFile, json, Encoding.UTF8);
            } catch {
                // ignore
            }
        }

        private void LoadLayersFromFile() {
            try {
                if (!File.Exists(_layersFile)) return;
                var json = File.ReadAllText(_layersFile, Encoding.UTF8);
                var items = JsonSerializer.Deserialize<string[]>(json);
                if (items == null) return;
                LayersListBox.Items.Clear();
                foreach (var it in items) LayersListBox.Items.Add(it);
            } catch {
                // ignore
            }
        }

        private void RemoveSelected_Click(object sender, RoutedEventArgs e) {
            if (LayersListBox.SelectedItem != null) {
                LayersListBox.Items.Remove(LayersListBox.SelectedItem);
                SaveLayersToFile();
            }
        }

        private void SaveLayers_Click(object sender, RoutedEventArgs e) {
            SaveLayersToFile();
            MessageBox.Show("已保存图层", "保存", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
