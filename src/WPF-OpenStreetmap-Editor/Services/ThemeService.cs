using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WPF_OpenStreetmap_Editor.Services;

public static class ThemeService {
    public const string SystemThemeId = "system";
    public const string LightThemeId = "light";
    public const string DarkThemeId = "dark";

    private const int DwmUseImmersiveDarkMode = 20;
    private static bool _isInitialized;
    private static string _activeThemeId = SystemThemeId;
    private static bool _usesDarkColors;

    public static string ActiveThemeId => _activeThemeId;

    public static ThemeCatalogResult GetAvailableThemes() {
        return ThemeCatalog.Load(AppPaths.ThemesDirectory);
    }

    public static void Initialize(string? themeId) {
        if (!_isInitialized) {
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
            _isInitialized = true;
        }

        ApplyTheme(themeId);
    }

    public static void Shutdown() {
        if (!_isInitialized) return;

        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
        _isInitialized = false;
    }

    public static ThemeDefinition ImportTheme(string sourcePath) {
        return ThemeCatalog.Install(sourcePath, AppPaths.ThemesDirectory);
    }

    public static void DeleteTheme(ThemeDefinition theme) {
        if (theme.IsBuiltIn || string.IsNullOrWhiteSpace(theme.SourcePath)) {
            throw new InvalidOperationException("内置主题不能删除。");
        }

        var themesDirectory = Path.GetFullPath(AppPaths.ThemesDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var themesRoot = themesDirectory + Path.DirectorySeparatorChar;
        var themePath = Path.GetFullPath(theme.SourcePath);
        if (!themePath.StartsWith(themesRoot, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("主题文件不在应用主题目录内。");
        }

        var sourceDirectory = Path.GetDirectoryName(themePath) ?? "";
        if (string.Equals(sourceDirectory, themesDirectory, StringComparison.OrdinalIgnoreCase)) {
            File.Delete(themePath);
            return;
        }

        var parentDirectory = Directory.GetParent(sourceDirectory)?.FullName;
        if (!Path.GetFileName(themePath).Equals("theme.json", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parentDirectory, themesDirectory, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("主题包目录结构无效。");
        }

        Directory.Delete(sourceDirectory, recursive: true);
    }

    public static void ApplyTheme(string? requestedThemeId) {
        if (Application.Current is null) return;

        var catalog = GetAvailableThemes();
        var selectedTheme = catalog.Themes.FirstOrDefault(theme =>
            string.Equals(theme.Id, requestedThemeId, StringComparison.OrdinalIgnoreCase));
        selectedTheme ??= catalog.Themes.First(theme => theme.Id == SystemThemeId);
        _activeThemeId = selectedTheme.Id;

        var systemMode = SystemThemeService.GetCurrentTheme();
        if (systemMode == SystemThemeMode.HighContrast) {
            ApplyHighContrast(Application.Current.Resources);
            _usesDarkColors = false;
        } else {
            var effectiveTheme = selectedTheme.Id == SystemThemeId
                ? catalog.Themes.First(theme => theme.Id == (systemMode == SystemThemeMode.Dark ? DarkThemeId : LightThemeId))
                : selectedTheme;
            ApplyColors(Application.Current.Resources, effectiveTheme.Colors);
            ApplyWindowBackground(Application.Current.Resources, effectiveTheme);
            _usesDarkColors = effectiveTheme.BaseTheme == "dark";
        }

        foreach (Window window in Application.Current.Windows) {
            ApplyWindowTheme(window);
        }
    }

    public static void ApplyWindowTheme(Window window) {
        window.SourceInitialized -= Window_SourceInitialized;
        window.SourceInitialized += Window_SourceInitialized;
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) {
            ApplyTitleBar(window);
        }
    }

    private static void ApplyColors(ResourceDictionary resources, ThemeColors colors) {
        SetBrush(resources, "Theme.WindowBrush", colors.Window);
        SetBrush(resources, "Theme.SurfaceBrush", colors.Surface);
        SetBrush(resources, "Theme.SurfaceAltBrush", colors.SurfaceAlt);
        SetBrush(resources, "Theme.TextBrush", colors.Text);
        SetBrush(resources, "Theme.MutedTextBrush", colors.MutedText);
        SetBrush(resources, "Theme.BorderBrush", colors.Border);
        SetBrush(resources, "Theme.AccentBrush", colors.Accent);
        SetBrush(resources, "Theme.AccentTextBrush", colors.AccentText);
        SetBrush(resources, "Theme.SelectionBrush", colors.Selection);
        SetBrush(resources, "Theme.SelectionTextBrush", colors.SelectionText);
        SetBrush(resources, "Theme.MapBackgroundBrush", colors.MapBackground);
        SetBrush(resources, "Theme.ErrorBrush", colors.Error);
        SetBrush(resources, "Theme.ImageOverlayBrush", "#A0000000");
        SetBrush(resources, "Theme.ImageOverlayTextBrush", "#FFFFFF");
    }

    private static void ApplyHighContrast(ResourceDictionary resources) {
        resources["Theme.WindowBrush"] = SystemColors.WindowBrush;
        resources["Theme.SurfaceBrush"] = SystemColors.WindowBrush;
        resources["Theme.SurfaceAltBrush"] = SystemColors.ControlBrush;
        resources["Theme.TextBrush"] = SystemColors.WindowTextBrush;
        resources["Theme.MutedTextBrush"] = SystemColors.GrayTextBrush;
        resources["Theme.BorderBrush"] = SystemColors.ActiveBorderBrush;
        resources["Theme.AccentBrush"] = SystemColors.HighlightBrush;
        resources["Theme.AccentTextBrush"] = SystemColors.HighlightTextBrush;
        resources["Theme.SelectionBrush"] = SystemColors.HighlightBrush;
        resources["Theme.SelectionTextBrush"] = SystemColors.HighlightTextBrush;
        resources["Theme.MapBackgroundBrush"] = SystemColors.ControlBrush;
        resources["Theme.ErrorBrush"] = SystemColors.WindowTextBrush;
        resources["Theme.ImageOverlayBrush"] = SystemColors.WindowBrush;
        resources["Theme.ImageOverlayTextBrush"] = SystemColors.WindowTextBrush;
        resources["Theme.WindowBackgroundBrush"] = SystemColors.WindowBrush;
    }

    private static void ApplyWindowBackground(ResourceDictionary resources, ThemeDefinition theme) {
        if (string.IsNullOrEmpty(theme.BackgroundImage) || string.IsNullOrEmpty(theme.SourcePath)) {
            resources["Theme.WindowBackgroundBrush"] = CreateBrush(theme.Colors.Window);
            return;
        }

        try {
            var imagePath = ThemeCatalog.ResolveAssetPath(theme.SourcePath, theme.BackgroundImage);
            var image = ThemePackageInstaller.LoadBackgroundImage(imagePath);
            var bounds = new Rect(0, 0, image.PixelWidth, image.PixelHeight);
            var baseDrawing = new GeometryDrawing(
                CreateBrush(theme.Colors.Window),
                null,
                new RectangleGeometry(bounds));
            var imageLayer = new DrawingGroup { Opacity = theme.BackgroundImageOpacity };
            imageLayer.Children.Add(new ImageDrawing(image, bounds));
            var drawing = new DrawingGroup();
            drawing.Children.Add(baseDrawing);
            drawing.Children.Add(imageLayer);
            var brush = new DrawingBrush(drawing) {
                Stretch = Stretch.UniformToFill,
                Viewbox = bounds,
                ViewboxUnits = BrushMappingMode.Absolute
            };
            brush.Freeze();
            resources["Theme.WindowBackgroundBrush"] = brush;
        } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) {
            Logger.Error($"Failed to load theme background '{theme.Id}'", ex);
            resources["Theme.WindowBackgroundBrush"] = CreateBrush(theme.Colors.Window);
        }
    }

    private static void SetBrush(ResourceDictionary resources, string key, string colorValue) {
        resources[key] = CreateBrush(colorValue);
    }

    private static SolidColorBrush CreateBrush(string colorValue) {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorValue));
        brush.Freeze();
        return brush;
    }

    private static void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) {
        ReapplySystemTheme();
    }

    private static void SystemParameters_StaticPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(SystemParameters.HighContrast)) {
            ReapplySystemTheme();
        }
    }

    private static void ReapplySystemTheme() {
        var application = Application.Current;
        if (application is null || application.Dispatcher.HasShutdownStarted) return;

        application.Dispatcher.BeginInvoke(() => ApplyTheme(_activeThemeId));
    }

    private static void Window_SourceInitialized(object? sender, EventArgs e) {
        if (sender is Window window) ApplyTitleBar(window);
    }

    private static void ApplyTitleBar(Window window) {
        if (!OperatingSystem.IsWindows() || SystemParameters.HighContrast) return;

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var enabled = _usesDarkColors ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int attributeValue, int attributeSize);
}
