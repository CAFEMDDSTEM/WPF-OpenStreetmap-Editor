using System.Collections.ObjectModel;
using System.Windows;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public partial class SettingsWindow : Window {
    private readonly AppSettings _workingSettings;
    private readonly ObservableCollection<TileSourcePreset> _sources;
    private TileSourcePreset? _selectedSource;
    private bool _loadingFields;

    public AppSettings ResultSettings { get; private set; }

    public SettingsWindow(AppSettings settings) {
        InitializeComponent();

        _workingSettings = settings.Clone();
        AppSettingsService.EnsureDefaults(_workingSettings);
        _sources = new ObservableCollection<TileSourcePreset>(_workingSettings.TileSources);
        ResultSettings = _workingSettings.Clone();

        SourcesListBox.ItemsSource = _sources;
        ExperimentalSmoothZoomCheckBox.IsChecked = _workingSettings.ExperimentalSmoothZoom;

        Loaded += (_, _) => {
            var selected = _sources.FirstOrDefault(source => source.Name == _workingSettings.ActiveSourceName) ??
                _sources.FirstOrDefault();
            SourcesListBox.SelectedItem = selected;
        };
    }

    private void SourcesListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
        if (_loadingFields) return;
        if (_selectedSource is not null && !SaveCurrentSourceFields()) {
            _loadingFields = true;
            SourcesListBox.SelectedItem = _selectedSource;
            _loadingFields = false;
            return;
        }

        _selectedSource = SourcesListBox.SelectedItem as TileSourcePreset;
        LoadSourceFields(_selectedSource);
    }

    private void AddSource_Click(object sender, RoutedEventArgs e) {
        if (_selectedSource is not null && !SaveCurrentSourceFields()) return;

        var source = new TileSourcePreset {
            Name = CreateUniqueSourceName("新图源"),
            Source = "xyz:https://tile.openstreetmap.org/{z}/{x}/{y}.png",
            MapMaxZoom = GeoConverter.MaxZoom,
            ImageMaxZoom = GeoConverter.MaxZoom,
            AttributionText = "© OpenStreetMap contributors",
            AttributionUrl = "https://www.openstreetmap.org/copyright",
            IsVisible = true,
            IsKnownSource = false
        };
        _sources.Add(source);
        SourcesListBox.SelectedItem = source;
    }

    private void RemoveSource_Click(object sender, RoutedEventArgs e) {
        if (SourcesListBox.SelectedItem is not TileSourcePreset source) return;
        if (_sources.Count <= 1) {
            MessageBox.Show("至少保留一个图源。", "设置", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var index = SourcesListBox.SelectedIndex;
        _sources.Remove(source);
        _selectedSource = null;
        SourcesListBox.SelectedIndex = Math.Clamp(index, 0, _sources.Count - 1);
    }

    private void Save_Click(object sender, RoutedEventArgs e) {
        if (_selectedSource is not null && !SaveCurrentSourceFields()) return;

        _workingSettings.TileSources = [.. _sources];
        _workingSettings.ExperimentalSmoothZoom = ExperimentalSmoothZoomCheckBox.IsChecked == true;
        _workingSettings.ActiveSourceName =
            (SourcesListBox.SelectedItem as TileSourcePreset)?.Name ??
            _sources.FirstOrDefault()?.Name ??
            "";
        _workingSettings.MapMaxZoom =
            (SourcesListBox.SelectedItem as TileSourcePreset)?.MapMaxZoom ??
            GeoConverter.MaxZoom;
        AppSettingsService.EnsureDefaults(_workingSettings);

        ResultSettings = _workingSettings.Clone();
        DialogResult = true;
    }

    private void LoadSourceFields(TileSourcePreset? source) {
        _loadingFields = true;
        try {
            SourceNameTextBox.Text = source?.Name ?? "";
            SourceUrlTextBox.Text = source?.Source ?? "";
            MapMaxZoomTextBox.Text = (source?.MapMaxZoom ?? GeoConverter.MaxZoom).ToString();
            ImageMaxZoomTextBox.Text = (source?.ImageMaxZoom ?? GeoConverter.MaxZoom).ToString();
            AccessTokenPasswordBox.Password = source?.AccessToken ?? "";
            AttributionTextBox.Text = source?.AttributionText ?? "";
            AttributionUrlTextBox.Text = source?.AttributionUrl ?? "";
            NoTileEtagsTextBox.Text = string.Join(Environment.NewLine, source?.NoTileEtags ?? []);
            NoTileMd5sTextBox.Text = string.Join(Environment.NewLine, source?.NoTileMd5s ?? []);
            VisibleCheckBox.IsChecked = source?.IsVisible ?? true;
            KnownSourceCheckBox.IsChecked = source?.IsKnownSource ?? false;
        } finally {
            _loadingFields = false;
        }
    }

    private bool SaveCurrentSourceFields() {
        if (_selectedSource is null) return true;

        var name = SourceNameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) {
            MessageBox.Show("请输入图源名称。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (_sources.Any(source => !ReferenceEquals(source, _selectedSource) && source.Name == name)) {
            MessageBox.Show("图源名称不能重复。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var url = SourceUrlTextBox.Text.Trim();
        if (string.IsNullOrEmpty(url)) {
            MessageBox.Show("请输入图源 URL。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!TryReadZoom(MapMaxZoomTextBox.Text, out var mapMaxZoom) ||
            !TryReadZoom(ImageMaxZoomTextBox.Text, out var imageMaxZoom)) {
            MessageBox.Show($"层级必须是 {GeoConverter.MinZoom} 到 {GeoConverter.MaxZoom} 之间的整数。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _selectedSource.Name = name;
        _selectedSource.Source = url;
        _selectedSource.MapMaxZoom = mapMaxZoom;
        _selectedSource.ImageMaxZoom = imageMaxZoom;
        _selectedSource.AccessToken = AccessTokenPasswordBox.Password.Trim();
        _selectedSource.AttributionText = AttributionTextBox.Text.Trim();
        _selectedSource.AttributionUrl = AttributionUrlTextBox.Text.Trim();
        _selectedSource.NoTileEtags = SplitSignatures(NoTileEtagsTextBox.Text);
        _selectedSource.NoTileMd5s = SplitSignatures(NoTileMd5sTextBox.Text);
        _selectedSource.IsVisible = VisibleCheckBox.IsChecked == true;
        _selectedSource.IsKnownSource = KnownSourceCheckBox.IsChecked == true;
        SourcesListBox.Items.Refresh();
        return true;
    }

    private string CreateUniqueSourceName(string baseName) {
        if (_sources.All(source => source.Name != baseName)) return baseName;

        for (var i = 2; i < 1000; i++) {
            var candidate = $"{baseName} {i}";
            if (_sources.All(source => source.Name != candidate)) return candidate;
        }

        return $"{baseName} {DateTime.Now:HHmmss}";
    }

    private static bool TryReadZoom(string text, out int zoom) {
        if (int.TryParse(text.Trim(), out zoom)) {
            zoom = Math.Clamp(zoom, GeoConverter.MinZoom, GeoConverter.MaxZoom);
            return true;
        }

        zoom = GeoConverter.MaxZoom;
        return false;
    }

    private static List<string> SplitSignatures(string text) {
        return text
            .Split(["\r\n", "\n", "\r", ",", ";"], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }
}
