using System.Windows;
using System.Windows.Controls;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class LayersWindow : Window {
    private static LocalizationService L => LocalizationService.Instance;

    public LayersWindow() {
        InitializeComponent();
        ThemeService.ApplyWindowTheme(this);
        Loaded += (_, _) => LoadLayersFromFile();
        Closed += (_, _) => SaveLayersToFile();
    }

    private void AddLayer_Click(object sender, RoutedEventArgs e) {
        var url = LayerUrlTextBox.Text?.Trim();
        var type = (LayerTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "XYZ";
        if (string.IsNullOrEmpty(url)) {
            MessageBox.Show(L.GetString("Layers.UrlRequired"), L.GetString("Common.Settings"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var item = $"[{type}] {url}";
        LayersListBox.Items.Add(item);
        LayerUrlTextBox.Clear();
        if (!SaveLayersToFile()) LayersListBox.Items.Remove(item);
    }

    private void RemoveLayer_Click(object sender, RoutedEventArgs e) {
        if (LayersListBox.SelectedItem is not null) {
            var selectedItem = LayersListBox.SelectedItem;
            var selectedIndex = LayersListBox.SelectedIndex;
            LayersListBox.Items.Remove(selectedItem);
            if (!SaveLayersToFile()) LayersListBox.Items.Insert(selectedIndex, selectedItem);
        }
    }

    private void LoadSelected_Click(object sender, RoutedEventArgs e) {
        if (LayersListBox.SelectedItem is null) {
            MessageBox.Show(L.GetString("Layers.SelectLayer"), L.GetString("Layers.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var item = LayersListBox.SelectedItem.ToString() ?? "";
        var type = "";
        var url = item;
        var idx = item.IndexOf(']');
        if (item.StartsWith("[") && idx > 1) {
            type = item[1..idx].Trim();
            url = idx + 1 < item.Length ? item[(idx + 1)..].Trim() : "";
        }

        if (string.Equals(type, "WMS", StringComparison.OrdinalIgnoreCase)) {
            MessageBox.Show(L.GetString("Layers.UnsupportedWms"), L.GetString("Layers.UnsupportedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Owner is MainWindow main) {
            if (!string.IsNullOrEmpty(type)) main.LoadLayer(type, url);
            else main.LoadMapFromUrl(url);
            Close();
        } else {
            MessageBox.Show(L.GetString("Layers.MainWindowMissing"), L.GetString("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadLayersFromFile() {
        var items = LayerService.LoadLayers();
        LayersListBox.Items.Clear();
        foreach (var it in items)
            LayersListBox.Items.Add(it);
    }

    private bool SaveLayersToFile() {
        var items = LayersListBox.Items.Cast<object>().Select(i => i.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s));
        if (LayerService.SaveLayers(items, out var error)) return true;

        PersistenceErrorPresenter.Show(this, error);
        return false;
    }
}
