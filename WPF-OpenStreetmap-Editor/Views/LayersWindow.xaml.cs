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

namespace WPF_OpenStreetmap_Editor.Views {
    /// <summary>
    /// Interaction logic for LayersWindow.xaml
    /// </summary>
    public partial class LayersWindow : Window {
        public LayersWindow() {
            InitializeComponent();
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
        }

        private void LoadSelected_Click(object sender, RoutedEventArgs e) {
            if (LayersListBox.SelectedItem == null) {
                MessageBox.Show("请选择一个图层以加载", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var item = LayersListBox.SelectedItem.ToString();
            // 提取 URL
            var idx = item.IndexOf(']');
            string url = idx >= 0 && idx + 1 < item.Length ? item.Substring(idx + 1).Trim() : item;

            // 调用主窗口方法进行加载（如果 owner 是 MainWindow）
            if (this.Owner is WPF_OpenStreetmap_Editor.MainWindow main) {
                main.LoadMapFromUrl(url);
                this.Close();
            } else {
                MessageBox.Show("未能找到主窗口来加载地图", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
