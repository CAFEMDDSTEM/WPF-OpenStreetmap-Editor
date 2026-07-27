using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public enum SettingsSection {
    Appearance,
    Data,
    Sources
}

public partial class SettingsWindow : Window {
    private readonly AppSettings _workingSettings;
    private readonly ObservableCollection<TileSourcePreset> _sources;
    private readonly ObservableCollection<ProjectionDefinition> _projections;
    private readonly ObservableCollection<ThemeDefinition> _themes = [];
    private readonly string _originalThemeId;
    private readonly string _originalLanguageId;
    private TileSourcePreset? _selectedSource;
    private bool _loadingFields;
    private bool _loadingProjectionFields;
    private bool _loadingThemes;
    private bool _loadingLanguages;
    private bool _accepted;
    private static LocalizationService L => LocalizationService.Instance;

    public AppSettings ResultSettings { get; private set; }

    public SettingsWindow(AppSettings settings, SettingsSection initialSection = SettingsSection.Appearance) {
        InitializeComponent();
        ThemeService.ApplyWindowTheme(this);
        SettingsTabControl.SelectedItem = initialSection == SettingsSection.Sources
            ? SourcesTabItem
            : initialSection == SettingsSection.Data
                ? DataTabItem
                : AppearanceTabItem;

        _workingSettings = settings.Clone();
        AppSettingsService.EnsureDefaults(_workingSettings);
        _originalThemeId = _workingSettings.ThemeId;
        _originalLanguageId = _workingSettings.LanguageId;
        _sources = new ObservableCollection<TileSourcePreset>(_workingSettings.TileSources);
        _projections = new ObservableCollection<ProjectionDefinition>(ProjectionService.GetDefinitions());
        ResultSettings = _workingSettings.Clone();

        SourcesListBox.ItemsSource = _sources;
        ImportProjectionComboBox.ItemsSource = _projections;
        ThemeComboBox.ItemsSource = _themes;
        ExperimentalSmoothZoomCheckBox.IsChecked = _workingSettings.ExperimentalSmoothZoom;
        LoadProjectionFields();
        LoadThemes(_workingSettings.ThemeId);
        LoadLanguages(_workingSettings.LanguageId);

        Loaded += (_, _) => {
            var selected = _sources.FirstOrDefault(source => source.Name == _workingSettings.ActiveSourceName) ??
                _sources.FirstOrDefault();
            SourcesListBox.SelectedItem = selected;
        };
        Closing += (_, _) => {
            if (!_accepted) {
                ThemeService.ApplyTheme(_originalThemeId);
                L.ApplyLanguage(_originalLanguageId);
            }
        };
    }

    private void ThemeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
        if (_loadingThemes || ThemeComboBox.SelectedItem is not ThemeDefinition theme) return;

        _workingSettings.ThemeId = theme.Id;
        ThemeService.ApplyTheme(theme.Id);
        UpdateThemeDetails(theme);
    }

    private void LanguageComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
        if (_loadingLanguages || LanguageComboBox.SelectedItem is not LanguageOption language) return;

        _workingSettings.LanguageId = language.Id;
        L.ApplyLanguage(language.Id);
        LoadLanguages(language.Id);
        if (ThemeComboBox.SelectedItem is ThemeDefinition theme) {
            UpdateThemeDetails(theme);
        }
    }

    private void ImportProjectionComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
        if (_loadingProjectionFields) return;

        CustomProjectionWktTextBox.IsEnabled =
            (ImportProjectionComboBox.SelectedItem as ProjectionDefinition)?.Id == ProjectionService.CustomWktId;
    }

    private void ImportTheme_Click(object sender, RoutedEventArgs e) {
        var dialog = new OpenFileDialog {
            Title = L.GetString("Settings.ImportThemeDialogTitle"),
            Filter = L.GetString("Settings.ImportThemeFilter"),
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
            MessageBox.Show(ex.Message, L.GetString("Settings.ImportThemeErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RemoveTheme_Click(object sender, RoutedEventArgs e) {
        if (ThemeComboBox.SelectedItem is not ThemeDefinition { IsBuiltIn: false } theme) return;

        var confirmation = MessageBox.Show(
            L.Format("Settings.DeleteThemeConfirm", theme.Name),
            L.GetString("Settings.DeleteThemeTitle"),
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
            MessageBox.Show(ex.Message, L.GetString("Settings.DeleteThemeErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            Name = CreateUniqueSourceName(L.GetString("Settings.NewSource")),
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
            MessageBox.Show(L.GetString("Settings.KeepOneSource"), L.GetString("Settings.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var index = SourcesListBox.SelectedIndex;
        _sources.Remove(source);
        _selectedSource = null;
        SourcesListBox.SelectedIndex = Math.Clamp(index, 0, _sources.Count - 1);
    }

    private void Save_Click(object sender, RoutedEventArgs e) {
        if (_selectedSource is not null && !SaveCurrentSourceFields()) return;
        if (!SaveProjectionFields()) return;

        _workingSettings.ThemeId = (ThemeComboBox.SelectedItem as ThemeDefinition)?.Id ?? ThemeService.SystemThemeId;
        _workingSettings.LanguageId = (LanguageComboBox.SelectedItem as LanguageOption)?.Id ?? LocalizationService.SystemLanguageId;
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
                : L.Format("Settings.InvalidThemeFiles", catalog.Errors.Count, string.Join("; ", catalog.Errors.Take(3)));
            ThemeErrorsTextBlock.Visibility = catalog.Errors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        } finally {
            _loadingThemes = false;
        }
    }

    private void UpdateThemeDetails(ThemeDefinition theme) {
        ThemeMetadataTextBlock.Text = theme.IsBuiltIn
            ? L.Format("Settings.BuiltInThemeMetadata", theme.Version)
            : L.Format("Settings.ThemeMetadata", theme.Author, theme.Version);
        ThemeDescriptionTextBlock.Text = theme.Description;
        RemoveThemeButton.IsEnabled = !theme.IsBuiltIn;
    }

    private void LoadProjectionFields() {
        _loadingProjectionFields = true;
        try {
            var selectedId = ProjectionService.NormalizeProjectionId(_workingSettings.DefaultImportProjectionId);
            ImportProjectionComboBox.SelectedItem = _projections.First(projection => projection.Id == selectedId);
            CustomProjectionWktTextBox.Text = _workingSettings.CustomImportProjectionWkt;
            CustomProjectionWktTextBox.IsEnabled = selectedId == ProjectionService.CustomWktId;
        } finally {
            _loadingProjectionFields = false;
        }
    }

    private bool SaveProjectionFields() {
        var selectedId = (ImportProjectionComboBox.SelectedItem as ProjectionDefinition)?.Id ?? ProjectionService.Wgs84Id;
        var customWkt = CustomProjectionWktTextBox.Text.Trim();
        if (selectedId == ProjectionService.CustomWktId) {
            try {
                _ = ProjectionService.CreateImportTransform(selectedId, customWkt);
            } catch (InvalidDataException ex) {
                MessageBox.Show(ex.Message, L.GetString("Settings.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        _workingSettings.DefaultImportProjectionId = selectedId;
        _workingSettings.CustomImportProjectionWkt = customWkt;
        return true;
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
            MessageBox.Show(L.GetString("Settings.SourceNameRequired"), L.GetString("Settings.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (_sources.Any(source => !ReferenceEquals(source, _selectedSource) && source.Name == name)) {
            MessageBox.Show(L.GetString("Settings.SourceNameDuplicate"), L.GetString("Settings.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var url = SourceUrlTextBox.Text.Trim();
        if (string.IsNullOrEmpty(url)) {
            MessageBox.Show(L.GetString("Settings.SourceUrlRequired"), L.GetString("Settings.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!TryReadZoom(MapMaxZoomTextBox.Text, out var mapMaxZoom) ||
            !TryReadZoom(ImageMaxZoomTextBox.Text, out var imageMaxZoom)) {
            MessageBox.Show(L.Format("Settings.ZoomRangeRequired", GeoConverter.MinZoom, GeoConverter.MaxZoom), L.GetString("Settings.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
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

    private void LoadLanguages(string selectedLanguageId) {
        _loadingLanguages = true;
        try {
            var selectedId = LocalizationService.NormalizeLanguageId(selectedLanguageId);
            var languages = new[] {
                new LanguageOption(LocalizationService.SystemLanguageId, L.GetString("Language.System")),
                new LanguageOption("en", L.GetString("Language.English")),
                new LanguageOption("zh-Hans", L.GetString("Language.SimplifiedChinese")),
                new LanguageOption("zh-Hant", L.GetString("Language.TraditionalChinese")),
                new LanguageOption("ja", L.GetString("Language.Japanese")),
                new LanguageOption("de", L.GetString("Language.German"))
            };
            LanguageComboBox.ItemsSource = languages;
            LanguageComboBox.SelectedItem = languages.First(language => language.Id == selectedId);
        } finally {
            _loadingLanguages = false;
        }
    }

    private sealed record LanguageOption(string Id, string DisplayName);
}
