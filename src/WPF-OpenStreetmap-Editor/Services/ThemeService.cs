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
            ApplyMapStyle(Application.Current.Resources, effectiveTheme.MapStyle, effectiveTheme.BaseTheme);
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
        ApplyHighContrastMapStyle(resources);
    }

    private static void ApplyMapStyle(ResourceDictionary resources, ThemeMapStyle? mapStyle, string baseTheme) {
        var style = ThemeMapStyle.Complete(mapStyle, baseTheme);

        SetAreaStyle(resources, "GenericArea", style.GenericArea!);
        SetAreaStyle(resources, "Water", style.Water!);
        SetAreaStyle(resources, "Farmland", style.Farmland!);
        SetAreaStyle(resources, "Forest", style.Forest!);
        SetAreaStyle(resources, "Park", style.Park!);
        SetAreaStyle(resources, "BuiltArea", style.BuiltArea!);
        SetAreaStyle(resources, "Building", style.Building!);

        SetLineStyle(resources, "GenericLine", style.GenericLine!);
        SetLineStyle(resources, "Boundary", style.Boundary!);
        SetLineStyle(resources, "Waterway", style.Waterway!);
        SetLineStyle(resources, "Rail", style.Rail!);
        SetLineStyle(resources, "Path", style.Path!);
        SetLineStyle(resources, "TrackRoad", style.TrackRoad!);
        SetLineStyle(resources, "ServiceRoad", style.ServiceRoad!);
        SetLineStyle(resources, "ResidentialRoad", style.ResidentialRoad!);
        SetLineStyle(resources, "LivingStreetRoad", style.LivingStreetRoad!);
        SetLineStyle(resources, "UnclassifiedRoad", style.UnclassifiedRoad!);
        SetLineStyle(resources, "LocalRoad", style.LocalRoad!);
        SetLineStyle(resources, "TertiaryRoad", style.TertiaryRoad!);
        SetLineStyle(resources, "SecondaryRoad", style.SecondaryRoad!);
        SetLineStyle(resources, "PrimaryRoad", style.PrimaryRoad!);
        SetLineStyle(resources, "TrunkRoad", style.TrunkRoad!);
        SetLineStyle(resources, "Motorway", style.Motorway!);

        SetPointStyle(resources, "GenericPoint", style.GenericPoint!);
        SetPointStyle(resources, "Poi", style.Poi!);
        SetPointStyle(resources, "FoodPoint", style.FoodPoint!);
        SetPointStyle(resources, "ParkingPoint", style.ParkingPoint!);
        SetPointStyle(resources, "MedicalPoint", style.MedicalPoint!);
        SetPointStyle(resources, "EducationPoint", style.EducationPoint!);
        SetPointStyle(resources, "TransitPoint", style.TransitPoint!);
        SetPointStyle(resources, "FuelPoint", style.FuelPoint!);
        SetPointStyle(resources, "BankPoint", style.BankPoint!);
        SetPointStyle(resources, "ToiletPoint", style.ToiletPoint!);
        SetPointStyle(resources, "SafetyPoint", style.SafetyPoint!);
        SetPointStyle(resources, "PostPoint", style.PostPoint!);
        SetPointStyle(resources, "HotelPoint", style.HotelPoint!);
        SetPointStyle(resources, "ShopPoint", style.ShopPoint!);
        SetPointStyle(resources, "TourismPoint", style.TourismPoint!);
        SetPointStyle(resources, "Place", style.Place!);
    }

    private static void ApplyHighContrastMapStyle(ResourceDictionary resources) {
        SetAreaStyle(resources, "GenericArea", SystemColors.ControlBrush, SystemColors.WindowTextBrush, 1.0);
        SetAreaStyle(resources, "Water", SystemColors.ControlBrush, SystemColors.HighlightBrush, 1.0);
        SetAreaStyle(resources, "Farmland", SystemColors.ControlBrush, SystemColors.WindowTextBrush, 1.0);
        SetAreaStyle(resources, "Forest", SystemColors.ControlBrush, SystemColors.WindowTextBrush, 1.0);
        SetAreaStyle(resources, "Park", SystemColors.ControlBrush, SystemColors.WindowTextBrush, 1.0);
        SetAreaStyle(resources, "BuiltArea", SystemColors.ControlBrush, SystemColors.WindowTextBrush, 1.0);
        SetAreaStyle(resources, "Building", SystemColors.WindowBrush, SystemColors.WindowTextBrush, 1.2);

        SetLineStyle(resources, "GenericLine", SystemColors.WindowTextBrush, SystemColors.WindowTextBrush, 1.6, 1.6);
        SetLineStyle(resources, "Boundary", SystemColors.WindowTextBrush, SystemColors.WindowTextBrush, 1.4, 1.4, [5, 3]);
        SetLineStyle(resources, "Waterway", SystemColors.HighlightBrush, SystemColors.HighlightBrush, 1.8, 1.8);
        SetLineStyle(resources, "Rail", SystemColors.WindowTextBrush, SystemColors.WindowBrush, 1.6, 3.4, [8, 3]);
        SetLineStyle(resources, "Path", SystemColors.WindowTextBrush, SystemColors.WindowTextBrush, 1.4, 1.4, [4, 3]);
        SetLineStyle(resources, "TrackRoad", SystemColors.WindowTextBrush, SystemColors.WindowTextBrush, 1.2, 1.2, [6, 4]);
        SetLineStyle(resources, "ServiceRoad", SystemColors.WindowBrush, SystemColors.WindowTextBrush, 1.6, 3.0);
        SetLineStyle(resources, "ResidentialRoad", SystemColors.WindowBrush, SystemColors.WindowTextBrush, 2.2, 3.9);
        SetLineStyle(resources, "LivingStreetRoad", SystemColors.WindowBrush, SystemColors.WindowTextBrush, 2.2, 3.8);
        SetLineStyle(resources, "UnclassifiedRoad", SystemColors.WindowBrush, SystemColors.WindowTextBrush, 2.2, 4.0);
        SetLineStyle(resources, "LocalRoad", SystemColors.WindowBrush, SystemColors.WindowTextBrush, 2.4, 4.4);
        SetLineStyle(resources, "TertiaryRoad", SystemColors.WindowBrush, SystemColors.WindowTextBrush, 2.8, 4.8);
        SetLineStyle(resources, "SecondaryRoad", SystemColors.WindowBrush, SystemColors.WindowTextBrush, 3.2, 5.3);
        SetLineStyle(resources, "PrimaryRoad", SystemColors.WindowBrush, SystemColors.WindowTextBrush, 3.7, 5.8);
        SetLineStyle(resources, "TrunkRoad", SystemColors.WindowBrush, SystemColors.WindowTextBrush, 4.0, 6.2);
        SetLineStyle(resources, "Motorway", SystemColors.WindowBrush, SystemColors.WindowTextBrush, 4.5, 6.8);

        SetPointStyle(resources, "GenericPoint", SystemColors.WindowBrush, SystemColors.WindowTextBrush, 3.5, 1.2);
        SetPointStyle(resources, "Poi", SystemColors.HighlightBrush, SystemColors.HighlightTextBrush, 4.0, 1.2);
        SetPointStyle(resources, "FoodPoint", SystemColors.HighlightBrush, SystemColors.HighlightTextBrush, 5.0, 1.2);
        SetPointStyle(resources, "ParkingPoint", SystemColors.HighlightBrush, SystemColors.HighlightTextBrush, 5.0, 1.2);
        SetPointStyle(resources, "MedicalPoint", SystemColors.HighlightBrush, SystemColors.HighlightTextBrush, 5.0, 1.2);
        SetPointStyle(resources, "EducationPoint", SystemColors.HighlightBrush, SystemColors.HighlightTextBrush, 5.0, 1.2);
        SetPointStyle(resources, "TransitPoint", SystemColors.HighlightBrush, SystemColors.HighlightTextBrush, 5.0, 1.2);
        SetPointStyle(resources, "FuelPoint", SystemColors.HighlightBrush, SystemColors.HighlightTextBrush, 5.0, 1.2);
        SetPointStyle(resources, "BankPoint", SystemColors.HighlightBrush, SystemColors.HighlightTextBrush, 5.0, 1.2);
        SetPointStyle(resources, "ToiletPoint", SystemColors.HighlightBrush, SystemColors.HighlightTextBrush, 5.0, 1.2);
        SetPointStyle(resources, "SafetyPoint", SystemColors.HighlightBrush, SystemColors.HighlightTextBrush, 5.0, 1.2);
        SetPointStyle(resources, "PostPoint", SystemColors.HighlightBrush, SystemColors.HighlightTextBrush, 5.0, 1.2);
        SetPointStyle(resources, "HotelPoint", SystemColors.HighlightBrush, SystemColors.HighlightTextBrush, 5.0, 1.2);
        SetPointStyle(resources, "ShopPoint", SystemColors.HighlightBrush, SystemColors.HighlightTextBrush, 5.0, 1.2);
        SetPointStyle(resources, "TourismPoint", SystemColors.HighlightBrush, SystemColors.HighlightTextBrush, 5.0, 1.2);
        SetPointStyle(resources, "Place", SystemColors.HighlightBrush, SystemColors.HighlightTextBrush, 4.5, 1.4);
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

    private static void SetAreaStyle(ResourceDictionary resources, string name, ThemeAreaStyle style) {
        SetAreaStyle(
            resources,
            name,
            CreateBrush(style.Fill!),
            CreateBrush(style.Stroke!),
            style.StrokeWidth ?? 0.0);
    }

    private static void SetAreaStyle(
        ResourceDictionary resources,
        string name,
        Brush fill,
        Brush stroke,
        double strokeWidth) {
        resources[$"Theme.Map.{name}FillBrush"] = fill;
        resources[$"Theme.Map.{name}StrokeBrush"] = stroke;
        resources[$"Theme.Map.{name}StrokeThickness"] = strokeWidth;
    }

    private static void SetLineStyle(ResourceDictionary resources, string name, ThemeLineStyle style) {
        SetLineStyle(
            resources,
            name,
            CreateBrush(style.Stroke!),
            CreateBrush(style.Casing!),
            style.StrokeWidth ?? 1.0,
            style.CasingWidth ?? style.StrokeWidth ?? 1.0,
            style.DashArray ?? Array.Empty<double>());
    }

    private static void SetLineStyle(
        ResourceDictionary resources,
        string name,
        Brush stroke,
        Brush casing,
        double strokeWidth,
        double casingWidth,
        double[]? dashArray = null) {
        resources[$"Theme.Map.{name}StrokeBrush"] = stroke;
        resources[$"Theme.Map.{name}CasingBrush"] = casing;
        resources[$"Theme.Map.{name}StrokeThickness"] = strokeWidth;
        resources[$"Theme.Map.{name}CasingThickness"] = casingWidth;
        resources[$"Theme.Map.{name}DashArray"] = dashArray ?? Array.Empty<double>();
    }

    private static void SetPointStyle(ResourceDictionary resources, string name, ThemePointStyle style) {
        SetPointStyle(
            resources,
            name,
            CreateBrush(style.Fill!),
            CreateBrush(style.Stroke!),
            style.Radius ?? 3.5,
            style.StrokeWidth ?? 1.0);
    }

    private static void SetPointStyle(
        ResourceDictionary resources,
        string name,
        Brush fill,
        Brush stroke,
        double radius,
        double strokeWidth) {
        resources[$"Theme.Map.{name}FillBrush"] = fill;
        resources[$"Theme.Map.{name}StrokeBrush"] = stroke;
        resources[$"Theme.Map.{name}Radius"] = radius;
        resources[$"Theme.Map.{name}StrokeThickness"] = strokeWidth;
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
