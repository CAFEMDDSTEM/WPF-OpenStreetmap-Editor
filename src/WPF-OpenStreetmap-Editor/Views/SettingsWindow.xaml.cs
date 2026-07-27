using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public enum SettingsSection {
    Appearance,
    Sources
}

public partial class SettingsWindow : Window {
    private readonly AppSettings _workingSettings;
    private readonly ObservableCollection<TileSourcePreset> _sources;
    private readonly ObservableCollection<ThemeDefinition> _themes = [];
    private readonly string _originalThemeId;
    private TileSourcePreset? _selectedSource;
    private bool _loadingFields;
    private bool _loadingThemes;
    private bool _accepted;

    public AppSettings ResultSettings { get; private set; }

    public SettingsWindow(AppSettings settings, SettingsSection initialSection = SettingsSection.Appearance) {
        InitializeComponent();
        ThemeService.ApplyWindowTheme(this);
        SettingsTabControl.SelectedItem = initialSection == SettingsSection.Sources
            ? SourcesTabItem
            : AppearanceTabItem;

        _workingSettings = settings.Clone();
        _originalThemeId = _workingSettings.ThemeId;
        AppSettingsService.EnsureDefaults(_workingSettings);
        _sources = new ObservableCollection<TileSourcePreset>(_workingSettings.TileSources);
        ResultSettings = _workingSettings.Clone();

        SourcesListBox.ItemsSource = _sources;
        ThemeComboBox.ItemsSource = _themes;
        ExperimentalSmoothZoomCheckBox.IsChecked = _workingSettings.ExperimentalSmoothZoom;
        LoadThemes(_workingSettings.ThemeId);

        Loaded += (_, _) => {
            var selected = _sources.FirstOrDefault(source => source.Name == _workingSettings.ActiveSourceName) ??
                _sources.FirstOrDefault();
            SourcesListBox.SelectedItem = selected;
        };
        Closing += (_, _) => {
            if (!_accepted) ThemeService.ApplyTheme(_originalThemeId);
        };
    }

    private void ThemeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
        if (_loadingThemes || ThemeComboBox.SelectedItem is not ThemeDefinition theme) return;

        _workingSettings.ThemeId = theme.Id;
        ThemeService.ApplyTheme(theme.Id);
        UpdateThemeDetails(theme);
    }

    private void ImportTheme_Click(object sender, RoutedEventArgs e) {
        var dialog = new OpenFileDialog {
            Title = "导入 WOSM 主题",
            Filter = "WOSM 主题包 (*.wosm-theme;*.zip;*.7z)|*.wosm-theme;*.zip;*.7z|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        try {
            var installed = ThemeService.ImportTheme(dialog.FileName);
            LoadThemes(installed.Id);
            ThemeService.ApplyTheme(installed.Id);
            _workingSettings.ThemeId = installed.Id;
        } catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) {
            MessageBox.Show(ex.Message, "无法导入主题", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RemoveTheme_Click(object sender, RoutedEventArgs e) {
        if (ThemeComboBox.SelectedItem is not ThemeDefinition { IsBuiltIn: false } theme) return;

        var confirmation = MessageBox.Show(
            $"确定删除第三方主题“{theme.Name}”吗？主题目录及其中资源将从本机移除。",
            "删除主题",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes) return;

        try {
            ThemeService.DeleteTheme(theme);
            LoadThemes(ThemeService.SystemThemeId);
            ThemeService.ApplyTheme(ThemeService.SystemThemeId);
            _workingSettings.ThemeId = ThemeService.SystemThemeId;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) {
            MessageBox.Show(ex.Message, "无法删除主题", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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

        _workingSettings.ThemeId = (ThemeComboBox.SelectedItem as ThemeDefinition)?.Id ?? ThemeService.SystemThemeId;
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
        _accepted = true;
        DialogResult = true;
    }

    private void LoadThemes(string? selectedThemeId) {
        var catalog = ThemeService.GetAvailableThemes();
        _loadingThemes = true;
        try {
            _themes.Clear();
            foreach (var theme in catalog.Themes) {
                _themes.Add(theme);
            }

            var selected = _themes.FirstOrDefault(theme =>
                string.Equals(theme.Id, selectedThemeId, StringComparison.OrdinalIgnoreCase));
            selected ??= _themes.First(theme => theme.Id == ThemeService.SystemThemeId);
            ThemeComboBox.SelectedItem = selected;
            UpdateThemeDetails(selected);

            ThemeErrorsTextBlock.Text = catalog.Errors.Count == 0
                ? ""
                : $"发现 {catalog.Errors.Count} 个无效主题文件：{string.Join("；", catalog.Errors.Take(3))}";
            ThemeErrorsTextBlock.Visibility = catalog.Errors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        } finally {
            _loadingThemes = false;
        }
    }

    private void UpdateThemeDetails(ThemeDefinition theme) {
        ThemeMetadataTextBlock.Text = theme.IsBuiltIn
            ? $"WOSM 内置主题    版本：{theme.Version}"
            : $"作者：{theme.Author}    版本：{theme.Version}";
        ThemeDescriptionTextBlock.Text = theme.Description;
        RemoveThemeButton.IsEnabled = !theme.IsBuiltIn;
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
