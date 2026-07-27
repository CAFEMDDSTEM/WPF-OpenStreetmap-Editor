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
            ["zh-Hans"] = "zh-Hans",
            ["zh-Hant"] = "zh-Hant",
            ["ja"] = "ja",
            ["de"] = "de"
        };

    private readonly CultureInfo _startupUiCulture = CultureInfo.CurrentUICulture;
    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);
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

        var dictionary = LoadDictionary(resolvedLanguageId);
        RefreshStrings(dictionary);
        ApplyApplicationResources(dictionary);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LanguageId)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResolvedLanguageId)));
    }

    public string GetString(string key) {
        return _strings.TryGetValue(key, out var value) ? value : key;
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

    private static ResourceDictionary LoadDictionary(string languageId) {
        try {
            return CreateDictionary(languageId);
        } catch (Exception ex) when (
            !languageId.Equals("en", StringComparison.OrdinalIgnoreCase) &&
            ex is IOException or XamlParseException) {
            return CreateDictionary("en");
        }
    }

    private static ResourceDictionary CreateDictionary(string languageId) {
        return new ResourceDictionary {
            Source = new Uri($"/WPF-OpenStreetmap-Editor;component/Localization/Strings.{languageId}.xaml", UriKind.Relative)
        };
    }

    private void RefreshStrings(ResourceDictionary dictionary) {
        _strings.Clear();
        foreach (var key in dictionary.Keys.OfType<string>()) {
            if (dictionary[key] is string value) {
                _strings[key] = value;
            }
        }
    }

    private static void ApplyApplicationResources(ResourceDictionary dictionary) {
        if (Application.Current is null) return;

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        for (var i = dictionaries.Count - 1; i >= 0; i--) {
            var source = dictionaries[i].Source?.OriginalString ?? "";
            if (source.Contains("/Localization/Strings.", StringComparison.OrdinalIgnoreCase) ||
                source.Contains("\\Localization\\Strings.", StringComparison.OrdinalIgnoreCase)) {
                dictionaries.RemoveAt(i);
            }
        }

        dictionaries.Add(dictionary);
    }
}
