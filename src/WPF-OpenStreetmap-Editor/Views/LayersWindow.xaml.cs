using System.Windows;
using System.Windows.Controls;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class LayersWindow : Window {
    public LayersWindow() {
        InitializeComponent();
        Loaded += (_, _) => LoadLayersFromFile();
        Closed += (_, _) => SaveLayersToFile();
    }

    private void AddLayer_Click(object sender, RoutedEventArgs e) {
        var url = LayerUrlTextBox.Text?.Trim();
        var type = (LayerTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "WMS";
        if (string.IsNullOrEmpty(url)) {
            MessageBox.Show("请输入图层 URL", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LayersListBox.Items.Add($"[{type}] {url}");
        LayerUrlTextBox.Clear();
        SaveLayersToFile();
    }

    private void RemoveLayer_Click(object sender, RoutedEventArgs e) {
        if (LayersListBox.SelectedItem != null) {
            LayersListBox.Items.Remove(LayersListBox.SelectedItem);
            SaveLayersToFile();
        }
    }

    private void LoadSelected_Click(object sender, RoutedEventArgs e) {
        if (LayersListBox.SelectedItem == null) {
            MessageBox.Show("请选择一个图层以加载", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var item = LayersListBox.SelectedItem.ToString() ?? "";
        var type = "";
        var url = item;
        var idx = item.IndexOf(']');
        if (item.StartsWith("[") && idx > 1) {
            type = item.Substring(1, idx - 1).Trim();
            url = idx + 1 < item.Length ? item.Substring(idx + 1).Trim() : "";
        }

        if (this.Owner is MainWindow main) {
            if (!string.IsNullOrEmpty(type)) main.LoadLayer(type, url);
            else main.LoadMapFromUrl(url);
            this.Close();
        } else {
            MessageBox.Show("未能找到主窗口来加载地图", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadLayersFromFile() {
        var items = LayerService.LoadLayers();
        LayersListBox.Items.Clear();
        foreach (var it in items)
            LayersListBox.Items.Add(it);
    }

    private void SaveLayersToFile() {
        var items = LayersListBox.Items.Cast<object>().Select(i => i.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s));
        LayerService.SaveLayers(items);
    }
}
