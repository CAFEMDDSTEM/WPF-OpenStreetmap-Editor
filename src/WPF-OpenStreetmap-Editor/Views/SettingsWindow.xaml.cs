using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Views;

public enum SettingsSection {
    Appearance,
    Data,
    GpsPoints,
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
    private TileSourceSafetyWarningKind _loadedSourceSafetyWarningKind;
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
            : initialSection == SettingsSection.GpsPoints
                ? GpsTabItem
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
        DisplayAlignmentProjectionComboBox.ItemsSource = _projections;
        ThemeComboBox.ItemsSource = _themes;
        ExperimentalSmoothZoomCheckBox.IsChecked = _workingSettings.ExperimentalSmoothZoom;
        LoadTileCacheFields();
        LoadBackupFields();
        LoadProjectionFields();
        LoadGpsFields();
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

    private void DisplayAlignmentProjectionComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
        if (_loadingProjectionFields) return;

        CustomDisplayAlignmentProjectionWktTextBox.IsEnabled =
            (DisplayAlignmentProjectionComboBox.SelectedItem as ProjectionDefinition)?.Id == ProjectionService.CustomWktId;
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
            Name = TileSourceNameService.CreateUniqueName(_sources, L.GetString("Settings.NewSource")),
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
        if (!SaveTileCacheFields()) return;
        if (!SaveBackupFields()) return;
        if (!SaveGpsFields()) return;

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

            var displaySelectedId = ProjectionService.NormalizeProjectionId(_workingSettings.DisplayAlignmentProjectionId);
            DisplayAlignmentProjectionComboBox.SelectedItem = _projections.First(projection => projection.Id == displaySelectedId);
            CustomDisplayAlignmentProjectionWktTextBox.Text = _workingSettings.CustomDisplayAlignmentProjectionWkt;
            DisplayAlignmentOffsetXTextBox.Text = _workingSettings.DisplayAlignmentOffsetX.ToString("R", CultureInfo.InvariantCulture);
            DisplayAlignmentOffsetYTextBox.Text = _workingSettings.DisplayAlignmentOffsetY.ToString("R", CultureInfo.InvariantCulture);
            CustomDisplayAlignmentProjectionWktTextBox.IsEnabled = displaySelectedId == ProjectionService.CustomWktId;
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

        var displaySelectedId = (DisplayAlignmentProjectionComboBox.SelectedItem as ProjectionDefinition)?.Id ?? ProjectionService.Wgs84Id;
        var displayCustomWkt = CustomDisplayAlignmentProjectionWktTextBox.Text.Trim();
        if (!SettingsFieldParser.TryParseDouble(DisplayAlignmentOffsetXTextBox.Text, out var displayOffsetX) ||
            !SettingsFieldParser.TryParseDouble(DisplayAlignmentOffsetYTextBox.Text, out var displayOffsetY)) {
            MessageBox.Show(L.GetString("Settings.DisplayAlignmentOffsetRequired"), L.GetString("Settings.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        try {
            _ = MapDisplayTransform.Create(new MapDisplayAlignmentOptions {
                ProjectionId = displaySelectedId,
                CustomProjectionWkt = displayCustomWkt,
                OffsetX = displayOffsetX,
                OffsetY = displayOffsetY
            });
        } catch (InvalidDataException ex) {
            MessageBox.Show(ex.Message, L.GetString("Settings.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _workingSettings.DefaultImportProjectionId = selectedId;
        _workingSettings.CustomImportProjectionWkt = customWkt;
        _workingSettings.DisplayAlignmentProjectionId = displaySelectedId;
        _workingSettings.CustomDisplayAlignmentProjectionWkt = displayCustomWkt;
        _workingSettings.DisplayAlignmentOffsetX = displayOffsetX;
        _workingSettings.DisplayAlignmentOffsetY = displayOffsetY;
        return true;
    }

    private void LoadTileCacheFields() {
        foreach (System.Windows.Controls.ComboBoxItem item in TilePerformanceModeComboBox.Items) {
            if (Enum.TryParse<TilePerformanceMode>(item.Tag?.ToString(), out var mode) &&
                mode == _workingSettings.TilePerformanceMode) {
                TilePerformanceModeComboBox.SelectedItem = item;
                break;
            }
        }

        TilePerformanceModeComboBox.SelectedItem ??= TilePerformanceModeComboBox.Items[0];
        TileCacheDaysTextBox.Text = _workingSettings.TileCacheMaxAgeDays.ToString(CultureInfo.InvariantCulture);
    }

    private bool SaveTileCacheFields() {
        if (TilePerformanceModeComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            Enum.TryParse<TilePerformanceMode>(item.Tag?.ToString(), out var mode)) {
            _workingSettings.TilePerformanceMode = mode;
        } else {
            _workingSettings.TilePerformanceMode = TilePerformanceMode.Responsive;
        }

        if (!SettingsFieldParser.TryParseIntegerInRange(
                TileCacheDaysTextBox.Text,
                AppSettings.MinTileCacheMaxAgeDays,
                AppSettings.MaxTileCacheMaxAgeDays,
                out var days)) {
            MessageBox.Show(
                L.Format(
                    "Settings.TileCacheDaysRange",
                    AppSettings.MinTileCacheMaxAgeDays,
                    AppSettings.MaxTileCacheMaxAgeDays),
                L.GetString("Settings.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        _workingSettings.TileCacheMaxAgeDays = days;
        return true;
    }

    private void LoadBackupFields() {
        AutosaveEnabledCheckBox.IsChecked = _workingSettings.AutosaveEnabled;
        AutosaveIntervalSecondsTextBox.Text = _workingSettings.AutosaveIntervalSeconds.ToString(CultureInfo.InvariantCulture);
        AutosaveFilesPerLayerTextBox.Text = _workingSettings.AutosaveFilesPerLayer.ToString(CultureInfo.InvariantCulture);
        KeepBackupFileOnSaveCheckBox.IsChecked = _workingSettings.KeepBackupFileOnSave;
        NotifyOnEverySaveCheckBox.IsChecked = _workingSettings.NotifyOnEverySave;
    }

    private bool SaveBackupFields() {
        if (!SettingsFieldParser.TryParseIntegerInRange(
                AutosaveIntervalSecondsTextBox.Text,
                AppSettings.MinAutosaveIntervalSeconds,
                AppSettings.MaxAutosaveIntervalSeconds,
                out var intervalSeconds)) {
            MessageBox.Show(
                L.Format(
                    "Settings.AutosaveIntervalRange",
                    AppSettings.MinAutosaveIntervalSeconds,
                    AppSettings.MaxAutosaveIntervalSeconds),
                L.GetString("Settings.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (!SettingsFieldParser.TryParseIntegerInRange(
                AutosaveFilesPerLayerTextBox.Text,
                AppSettings.MinAutosaveFilesPerLayer,
                AppSettings.MaxAutosaveFilesPerLayer,
                out var filesPerLayer)) {
            MessageBox.Show(
                L.Format(
                    "Settings.AutosaveFilesPerLayerRange",
                    AppSettings.MinAutosaveFilesPerLayer,
                    AppSettings.MaxAutosaveFilesPerLayer),
                L.GetString("Settings.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        _workingSettings.AutosaveEnabled = AutosaveEnabledCheckBox.IsChecked == true;
        _workingSettings.AutosaveIntervalSeconds = intervalSeconds;
        _workingSettings.AutosaveFilesPerLayer = filesPerLayer;
        _workingSettings.KeepBackupFileOnSave = KeepBackupFileOnSaveCheckBox.IsChecked == true;
        _workingSettings.NotifyOnEverySave = NotifyOnEverySaveCheckBox.IsChecked == true;
        return true;
    }

    private void LoadGpsFields() {
        GpsDrawLinesBetweenPointsCheckBox.IsChecked = _workingSettings.GpsDrawLinesBetweenPoints;
        GpsLineMinimumDistanceTextBox.Text = _workingSettings.GpsLineMinimumDistancePixels.ToString(CultureInfo.InvariantCulture);
        GpsDrawLargePointsCheckBox.IsChecked = _workingSettings.GpsDrawLargeGpsPoints;
        GpsTrackDrawingWidthTextBox.Text = _workingSettings.GpsTrackDrawingWidth.ToString("R", CultureInfo.InvariantCulture);

        GpsSingleColorRadioButton.IsChecked = _workingSettings.GpsTrackColorMode == GpsTrackColorMode.SingleColor;
        GpsSpeedColorRadioButton.IsChecked = _workingSettings.GpsTrackColorMode == GpsTrackColorMode.Speed;
        GpsFixedValueColorRadioButton.IsChecked = _workingSettings.GpsTrackColorMode == GpsTrackColorMode.FixedValue;
        GpsReferenceIdColorRadioButton.IsChecked = _workingSettings.GpsTrackColorMode == GpsTrackColorMode.ReferenceId;
        GpsTimestampColorRadioButton.IsChecked = _workingSettings.GpsTrackColorMode == GpsTrackColorMode.Timestamp;
        GpsHeatmapColorRadioButton.IsChecked = _workingSettings.GpsTrackColorMode == GpsTrackColorMode.Heatmap;

        SelectComboBoxItemByTag(GpsSpeedProfileComboBox, _workingSettings.GpsSpeedProfile);
        SelectComboBoxItemByTag(GpsHeatmapBlendModeComboBox, _workingSettings.GpsHeatmapBlendMode.ToString());
        GpsHeatmapOverlayAdjustmentSlider.Value = _workingSettings.GpsHeatmapOverlayAdjustment;
        GpsHeatmapUseTargetColorCheckBox.IsChecked = _workingSettings.GpsHeatmapUseTargetColor;
        UpdateGpsOptionAvailability();
    }

    private bool SaveGpsFields() {
        if (!SettingsFieldParser.TryParseInteger(GpsLineMinimumDistanceTextBox.Text, out var minimumDistance)) {
            MessageBox.Show(L.GetString("Settings.GpsLineMinimumDistanceRequired"), L.GetString("Settings.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!SettingsFieldParser.TryParseDouble(GpsTrackDrawingWidthTextBox.Text, out var drawingWidth)) {
            MessageBox.Show(L.GetString("Settings.GpsTrackDrawingWidthRequired"), L.GetString("Settings.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (minimumDistance < 0 || drawingWidth < 0) {
            MessageBox.Show(L.GetString("Settings.GpsNonNegativeRequired"), L.GetString("Settings.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _workingSettings.GpsDrawLinesBetweenPoints = GpsDrawLinesBetweenPointsCheckBox.IsChecked == true;
        _workingSettings.GpsLineMinimumDistancePixels = minimumDistance;
        _workingSettings.GpsDrawLargeGpsPoints = GpsDrawLargePointsCheckBox.IsChecked == true;
        _workingSettings.GpsTrackDrawingWidth = drawingWidth;
        _workingSettings.GpsTrackColorMode = GetSelectedGpsTrackColorMode();
        _workingSettings.GpsSpeedProfile = GetSelectedComboBoxTag(GpsSpeedProfileComboBox) ?? "Car";
        _workingSettings.GpsHeatmapBlendMode = Enum.TryParse<GpsHeatmapBlendMode>(GetSelectedComboBoxTag(GpsHeatmapBlendModeComboBox), out var blendMode)
            ? blendMode
            : GpsHeatmapBlendMode.Normal;
        _workingSettings.GpsHeatmapOverlayAdjustment = GpsHeatmapOverlayAdjustmentSlider.Value;
        _workingSettings.GpsHeatmapUseTargetColor = GpsHeatmapUseTargetColorCheckBox.IsChecked == true;
        return true;
    }

    private void GpsTrackColorModeRadioButton_Checked(object sender, RoutedEventArgs e) {
        UpdateGpsOptionAvailability();
    }

    private void UpdateGpsOptionAvailability() {
        var isSpeed = GpsSpeedColorRadioButton.IsChecked == true;
        var isHeatmap = GpsHeatmapColorRadioButton.IsChecked == true;
        GpsSpeedProfileComboBox.IsEnabled = isSpeed;
        GpsHeatmapBlendModeComboBox.IsEnabled = isHeatmap;
        GpsHeatmapOverlayAdjustmentSlider.IsEnabled = isHeatmap;
        GpsHeatmapUseTargetColorCheckBox.IsEnabled = isHeatmap;
    }

    private GpsTrackColorMode GetSelectedGpsTrackColorMode() {
        if (GpsSpeedColorRadioButton.IsChecked == true) return GpsTrackColorMode.Speed;
        if (GpsFixedValueColorRadioButton.IsChecked == true) return GpsTrackColorMode.FixedValue;
        if (GpsReferenceIdColorRadioButton.IsChecked == true) return GpsTrackColorMode.ReferenceId;
        if (GpsTimestampColorRadioButton.IsChecked == true) return GpsTrackColorMode.Timestamp;
        if (GpsHeatmapColorRadioButton.IsChecked == true) return GpsTrackColorMode.Heatmap;
        return GpsTrackColorMode.SingleColor;
    }

    private static void SelectComboBoxItemByTag(System.Windows.Controls.ComboBox comboBox, string tag) {
        foreach (System.Windows.Controls.ComboBoxItem item in comboBox.Items) {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase)) {
                comboBox.SelectedItem = item;
                return;
            }
        }

        if (comboBox.Items.Count > 0) {
            comboBox.SelectedItem = comboBox.Items[0];
        }
    }

    private static string? GetSelectedComboBoxTag(System.Windows.Controls.ComboBox comboBox) {
        return (comboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString();
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
            _loadedSourceSafetyWarningKind = TileSourceSafetyService.GetWarningKind(source?.Name, source?.Source);
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

        if (!SettingsFieldParser.TryParseZoom(MapMaxZoomTextBox.Text, out var mapMaxZoom) ||
            !SettingsFieldParser.TryParseZoom(ImageMaxZoomTextBox.Text, out var imageMaxZoom)) {
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
        _selectedSource.NoTileEtags = SettingsFieldParser.ParseSignatures(NoTileEtagsTextBox.Text);
        _selectedSource.NoTileMd5s = SettingsFieldParser.ParseSignatures(NoTileMd5sTextBox.Text);
        _selectedSource.IsVisible = VisibleCheckBox.IsChecked == true;
        _selectedSource.IsKnownSource = KnownSourceCheckBox.IsChecked == true;
        SourcesListBox.Items.Refresh();
        ShowSourceSafetyWarning(name, url);
        _loadedSourceSafetyWarningKind = TileSourceSafetyService.GetWarningKind(name, url);
        return true;
    }

    private void ShowSourceSafetyWarning(string name, string sourceUrl) {
        var warningKind = TileSourceSafetyService.GetWarningKind(name, sourceUrl);
        if (warningKind == TileSourceSafetyWarningKind.None ||
            warningKind == _loadedSourceSafetyWarningKind) {
            return;
        }

        MessageBox.Show(
            TileSourceSafetyService.GetWarningMessage(warningKind),
            L.GetString("Common.Warning"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
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
                new LanguageOption("de", L.GetString("Language.German")),
                new LanguageOption("ru", L.GetString("Language.Russian"))
            };
            LanguageComboBox.ItemsSource = languages;
            LanguageComboBox.SelectedItem = languages.First(language => language.Id == selectedId);
        } finally {
            _loadingLanguages = false;
        }
    }

    private sealed record LanguageOption(string Id, string DisplayName);
}
