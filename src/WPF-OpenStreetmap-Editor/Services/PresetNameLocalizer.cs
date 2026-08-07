using System.IO;
using System.Xml;
using System.Xml.Linq;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

/// <summary>
/// Resolves localized display names for tagging presets and preset groups.
/// Translations are sourced from the JOSM translation dataset, bundled as
/// per-language <c>PresetNames.{lang}.xml</c> embedded resources. The lookup
/// mirrors JOSM: an entry with a <c>name_context</c> is resolved via the
/// <c>(name, context)</c> pair; otherwise the plain name is used. Missing
/// translations fall back to the English name.
/// </summary>
public static class PresetNameLocalizer {
    private const string ResourcePrefix = "WPF_OpenStreetmap_Editor.Assets.PresetNames.";
    private const char KeySeparator = '\u001F';

    private static readonly object Sync = new();
    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.Ordinal);

    public static string GetName(string? name, string? context = null) {
        if (string.IsNullOrWhiteSpace(name)) return name ?? "";
        var languageId = LocalizationService.Instance.ResolvedLanguageId;
        return GetNameForLanguage(languageId, name, context);
    }

    /// <summary>Resolves the display name for an explicit language id without touching the current UI language.</summary>
    internal static string GetNameForLanguage(string languageId, string? name, string? context = null) {
        if (string.IsNullOrWhiteSpace(name)) return name ?? "";
        if (string.IsNullOrEmpty(languageId) || languageId.Equals("en", StringComparison.OrdinalIgnoreCase)) {
            return name;
        }

        var entries = Load(languageId);
        if (entries.Count == 0) return name;

        var key = name + KeySeparator + (context ?? "");
        return entries.TryGetValue(key, out var value) ? value : name;
    }

    public static string GetName(TagPreset? preset) {
        return preset is null ? "" : GetName(preset.Name, preset.NameContext);
    }

    public static string GetName(TagPresetGroup? group) {
        return group is null ? "" : GetName(group.Name, group.NameContext);
    }

    private static Dictionary<string, string> Load(string languageId) {
        lock (Sync) {
            if (Cache.TryGetValue(languageId, out var cached)) return cached;

            Dictionary<string, string>? entries = null;
            try {
                var resourceName = ResourcePrefix + languageId + ".xml";
                var assembly = typeof(PresetNameLocalizer).Assembly;
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is not null) {
                    entries = Parse(XDocument.Load(stream));
                }
            } catch (Exception ex) when (ex is IOException or XmlException) {
                entries = null;
            }

            Cache[languageId] = entries ?? [];
            return Cache[languageId];
        }
    }

    private static Dictionary<string, string> Parse(XDocument document) {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        if (document.Root is null) return entries;

        foreach (var element in document.Root.Elements("Entry")) {
            var name = (string?)element.Attribute("Name");
            var context = (string?)element.Attribute("Context") ?? "";
            var value = (string?)element.Attribute("Value");
            if (name is null || value is null) continue;
            entries.TryAdd(name + KeySeparator + context, value);
        }
        return entries;
    }

    /// <summary>Clears the cached translation tables. Used by tests.</summary>
    internal static void ClearCache() {
        lock (Sync) Cache.Clear();
    }
}
