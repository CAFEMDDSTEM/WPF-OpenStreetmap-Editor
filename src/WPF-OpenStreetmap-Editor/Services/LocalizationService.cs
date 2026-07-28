using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Markup;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed record AppLanguage(string Id, string DisplayName);

public sealed class LocalizationService : INotifyPropertyChanged {
    public const string SystemLanguageId = "system";

    private static readonly IReadOnlyDictionary<string, string> CultureResourceNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["en"] = "en",
            ["en-US"] = "en",
            ["en-GB"] = "en",
            ["zh-Hans"] = "zh-Hans",
            ["zh-CN"] = "zh-Hans",
            ["zh-SG"] = "zh-Hans",
            ["zh-Hant"] = "zh-Hant",
            ["zh-TW"] = "zh-Hant",
            ["zh-HK"] = "zh-Hant",
            ["zh-MO"] = "zh-Hant",
            ["ja"] = "ja",
            ["ja-JP"] = "ja",
            ["de"] = "de",
            ["de-DE"] = "de"
        };
    private static readonly IReadOnlyDictionary<string, string> HeadlessEnglishStrings =
        new Dictionary<string, string>(StringComparer.Ordinal) {
            ["Help.VersionFormat"] = "Version {0}",
            ["Help.Section.GetStarted"] = "Getting started",
            ["Help.GetStarted.Open"] = "Open or import map data from the File menu, then pan and zoom to inspect the area.",
            ["Help.GetStarted.Save"] = "Save the current map with File > Save as after editing.",
            ["Help.GetStarted.Layers"] = "Use the layer panel to choose imagery and data layers, then adjust visibility and opacity.",
            ["Help.Section.MapEditing"] = "Map editing",
            ["Help.MapEditing.Tools"] = "Use the toolbar to select, draw lines, and add nodes.",
            ["Help.MapEditing.Zoom"] = "Use the mouse wheel or zoom controls to change map scale.",
            ["Help.MapEditing.Selection"] = "Select features on the map or in the feature table before editing them.",
            ["Help.Section.SourcesThemes"] = "Sources and themes",
            ["Help.SourcesThemes.Settings"] = "Configure imagery sources and application theme from Settings.",
            ["Help.SourcesThemes.Imagery"] = "Use XYZ or TMS imagery sources for background layers.",
            ["Help.SourcesThemes.Attribution"] = "Keep imagery attribution accurate when adding or changing sources.",
            ["Help.Section.OsmPlugins"] = "OSM and plugins",
            ["Help.OsmPlugins.Plugins"] = "Manage plugins from the Plugins window.",
            ["Help.OsmPlugins.Accounts"] = "Add an OSM account before uploading changes.",
            ["Help.OsmPlugins.DownloadUpload"] = "Download OSM data for the selected area and review upload previews before sending changes.",
            ["Help.Section.Keyboard"] = "Keyboard shortcuts",
            ["Help.Keyboard.F1"] = "F1 opens Help and About.",
            ["Help.Keyboard.Save"] = "Ctrl+S saves the current map.",
            ["Help.Keyboard.Edit"] = "Ctrl+Z and Ctrl+Y undo and redo edits; Ctrl+Shift+V pastes copied tags onto the selection.",
            ["Help.Keyboard.Search"] = "Ctrl+F searches objects in the current data layer.",
            ["Help.Keyboard.Tags"] = "Ctrl+C copies selected features, Ctrl+Shift+V pastes only copied tags, and Shift+R copies tags from the previously selected feature.",
            ["Help.Keyboard.Modes"] = "S selects features, V box-selects, and A draws lines.",
            ["Help.Keyboard.JosmDrawing"] = "Shift+F enters fast line drawing; Ctrl+Shift+F toggles viewport following while drawing lines.",
            ["Help.Keyboard.Transform"] = "R rotates, M moves, X extrudes segments, and Q orthogonalizes selected features.",
            ["Help.Keyboard.ImageryOrder"] = "` rotates imagery layer order for quick comparison.",
            ["Help.Keyboard.TypedCommands"] = "Type supported edit commands when keyboard command input is active.",
            ["Help.Keyboard.Drag"] = "Drag selected features on the map when move mode is active.",
            ["Help.Keyboard.Nodes"] = "Insert adds a node at the map center.",
            ["Help.Info.Program"] = "Program",
            ["Help.Info.Version"] = "Version",
            ["Help.Info.License"] = "License",
            ["Help.Info.Runtime"] = "Runtime",
            ["Help.Info.Features"] = "Features",
            ["Help.Info.FeaturesValue"] = "WPF map editing, OSM download/upload, imagery layers, plugins, and theme support",
            ["Osm.Download.Error.FallbackFailed"] = "The OSM standard API and Overpass API could not process this area. Shrink the selected area and try again.",
            ["Osm.Download.Error.BadRequest"] = "The OSM server rejected this area. Shrink the selected area and try again.",
            ["Osm.Download.Error.TooManyRequests"] = "The OSM server received too many requests. Try again later.",
            ["Osm.Download.Error.HttpStatus"] = "The OSM server returned HTTP {0}. Try again later.",
            ["Osm.Download.Error.Timeout"] = "The connection to the OSM server timed out. Check the network and try again.",
            ["Osm.Download.Error.Connection"] = "Could not connect to the OSM server. Check the network and try again.",
            ["Osm.Download.Error.Generic"] = "OSM download failed. Try again later.",
            ["Osm.Download.SearchTooltip"] = "Search for a place or address to center the map",
            ["Osm.Download.SearchButton"] = "Search",
            ["Osm.Download.ClearSearch"] = "Clear search",
            ["Osm.Download.Searching"] = "Searching for {0}...",
            ["Osm.Download.SearchResult"] = "Showing {0}",
            ["Osm.Download.SearchNoResults"] = "No place was found for \"{0}\".",
            ["Osm.Download.SearchError"] = "Could not search for places. Check the network and try again.",
            ["Update.CurrentVersionInvalid"] = "The current version could not be recognized: {0}",
            ["Update.HttpFailed"] = "Could not check for updates: GitHub HTTP {0} {1}",
            ["Update.NoRelease"] = "No available release was found.",
            ["Update.Available"] = "New version {0} is available.",
            ["Update.UpToDate"] = "You are already on the latest version {0}.",
            ["Update.Timeout"] = "The update check timed out.",
            ["Update.JsonFailed"] = "The update response could not be parsed: {0}",
            ["Update.GenericFailed"] = "Could not check for updates: {0}",
            ["Welcome.Title"] = "Welcome to WPF OpenStreetmap Editor",
            ["Welcome.Warning"] = "This is a self-hosted OpenStreetMap editor. Check every edit carefully before uploading it.",
            ["Welcome.Description"] = "Use the tutorial to learn the interface, or jump straight into editing if you already know the workflow.",
            ["Welcome.PrivacyTitle"] = "Your privacy settings",
            ["Welcome.ShowThirdPartyIcons"] = "Show third-party icons",
            ["Welcome.StartTutorial"] = "Start tutorial",
            ["Welcome.EditNow"] = "Start editing",
            ["Osm.Accounts.BasicPassword"] = "User name and password",
            ["Osm.Accounts.Unknown"] = "Unknown",
            ["Osm.Auth.MissingPassword"] = "The OSM account is missing a password.",
            ["Osm.Auth.MissingToken"] = "The OSM account is missing an access token.",
            ["Osm.Auth.MissingUserName"] = "The OSM account is missing a user name.",
            ["Osm.Auth.UnsupportedMethod"] = "Unsupported OSM authentication method.",
            ["Common.Warning"] = "Warning",
            ["Settings.SourceSafetyGoogle"] = "Google Maps imagery is not OSM-compliant. Make sure you have permission and keep the attribution accurate.",
            ["Settings.SourceSafetyAmap"] = "Amap uses the GCJ-02 \"Mars\" coordinate system, which WOSM does not support.",
            ["Settings.SourceSafetyBaidu"] = "Baidu imagery uses the BD-09 coordinate system, which WOSM does not support.",
            ["Settings.SourceSafetyGcj02"] = "This imagery source commonly uses the GCJ-02 \"Mars\" coordinate system, which WOSM does not support.",
            ["Settings.SourceSafetyProprietary"] = "This is a commonly restricted proprietary map source. Do not use it for OSM editing unless you have explicit permission and keep the attribution accurate.",
            ["Settings.TileStoragePerformance"] = "Tile storage and performance",
            ["Settings.TilePerformanceMode"] = "Imagery experience",
            ["Settings.TilePerformanceResponsive"] = "More responsive",
            ["Settings.TilePerformanceMemorySaver"] = "Use less memory",
            ["Settings.TileCacheDays"] = "Cache days",
            ["Settings.TileStorageHelp"] = "Responsive mode keeps more decoded tiles in memory and preloads nearby non-volunteer sources. Memory saver uses a smaller memory cache and disables prefetch. Disk cache cleanup keeps tiles for the selected number of days.",
            ["Settings.TileCacheDaysRange"] = "Cache days must be an integer from {0} through {1}."
        };

    private readonly CultureInfo _startupUiCulture = CultureInfo.CurrentUICulture;
    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private string _languageId = SystemLanguageId;
    private string _resolvedLanguageId = "en";

    private LocalizationService() {
    }

    public static LocalizationService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<AppLanguage> Languages { get; } = [
        new(SystemLanguageId, "Use system setting"),
        new("en", "English"),
        new("zh-Hans", "简体中文"),
        new("zh-Hant", "繁體中文"),
        new("ja", "日本語"),
        new("de", "Deutsch")
    ];

    public string LanguageId => _languageId;
    public string ResolvedLanguageId => _resolvedLanguageId;

    public string this[string key] => GetString(key);

    public void Initialize(string? languageId) {
        ApplyLanguage(languageId);
    }

    public void ApplyLanguage(string? languageId) {
        var normalizedLanguageId = NormalizeLanguageId(languageId);
        var resolvedLanguageId = ResolveLanguageId(normalizedLanguageId);
        var culture = CultureInfo.GetCultureInfo(resolvedLanguageId);

        _languageId = normalizedLanguageId;
        _resolvedLanguageId = resolvedLanguageId;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        var dictionaries = LoadDictionaries(resolvedLanguageId);
        lock (_sync) {
            RefreshStrings(dictionaries);
        }
        ApplyApplicationResources(dictionaries);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LanguageId)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResolvedLanguageId)));
    }

    public string GetString(string key) {
        lock (_sync) {
            EnsureStringsLoaded();
            return _strings.TryGetValue(key, out var value) ? value : key;
        }
    }

    public string Format(string key, params object?[] args) {
        return string.Format(CultureInfo.CurrentCulture, GetString(key), args);
    }

    public static string NormalizeLanguageId(string? languageId) {
        if (string.IsNullOrWhiteSpace(languageId)) return SystemLanguageId;

        var trimmed = languageId.Trim();
        return trimmed.Equals(SystemLanguageId, StringComparison.OrdinalIgnoreCase)
            ? SystemLanguageId
            : CultureResourceNames.ContainsKey(trimmed)
                ? CultureResourceNames[trimmed]
                : SystemLanguageId;
    }

    internal string ResolveLanguageId(string languageId) {
        if (!languageId.Equals(SystemLanguageId, StringComparison.OrdinalIgnoreCase)) {
            return CultureResourceNames[languageId];
        }

        return ResolveSystemLanguageId(_startupUiCulture);
    }

    internal static string ResolveSystemLanguageId(CultureInfo culture) {
        var name = culture.Name;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) {
            return name.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("-TW", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("-HK", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("-MO", StringComparison.OrdinalIgnoreCase)
                    ? "zh-Hant"
                    : "zh-Hans";
        }

        if (name.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
        if (name.StartsWith("de", StringComparison.OrdinalIgnoreCase)) return "de";
        return "en";
    }

    private static IReadOnlyList<ResourceDictionary> LoadDictionaries(string languageId) {
        var dictionaries = new List<ResourceDictionary> { CreateDictionary("en") };
        if (languageId.Equals("en", StringComparison.OrdinalIgnoreCase)) return dictionaries;

        try {
            dictionaries.Add(CreateDictionary(languageId));
        } catch (Exception ex) when (ex is IOException or XamlParseException) {
        }

        return dictionaries;
    }

    private static ResourceDictionary CreateDictionary(string languageId) {
        return new ResourceDictionary {
            Source = new Uri($"/WPF-OpenStreetmap-Editor;component/Localization/Strings.{languageId}.xaml", UriKind.Relative)
        };
    }

    private void EnsureStringsLoaded() {
        if (_strings.Count > 0) return;

        try {
            RefreshStrings(LoadDictionaries(_resolvedLanguageId));
        } catch (Exception ex) when (ex is IOException or NotSupportedException or XamlParseException) {
            RefreshStrings(HeadlessEnglishStrings);
        }
    }

    private void RefreshStrings(IReadOnlyDictionary<string, string> strings) {
        _strings.Clear();
        foreach (var item in strings) {
            _strings[item.Key] = item.Value;
        }
    }

    private void RefreshStrings(IEnumerable<ResourceDictionary> dictionaries) {
        _strings.Clear();
        foreach (var dictionary in dictionaries) {
            foreach (var key in dictionary.Keys.OfType<string>()) {
                if (dictionary[key] is string value) {
                    _strings[key] = value;
                }
            }
        }
    }

    private static void ApplyApplicationResources(IEnumerable<ResourceDictionary> localizationDictionaries) {
        if (Application.Current is null) return;

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        for (var i = dictionaries.Count - 1; i >= 0; i--) {
            var source = dictionaries[i].Source?.OriginalString ?? "";
            if (source.Contains("/Localization/Strings.", StringComparison.OrdinalIgnoreCase) ||
                source.Contains("\\Localization\\Strings.", StringComparison.OrdinalIgnoreCase)) {
                dictionaries.RemoveAt(i);
            }
        }

        foreach (var dictionary in localizationDictionaries) {
            dictionaries.Add(dictionary);
        }
    }
}
