using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

internal static class TagPresetApplicationService {
    public static IReadOnlyDictionary<string, string?> BuildTagPatch(
        TagPreset preset,
        IReadOnlyDictionary<string, string?> fieldValues) {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(fieldValues);

        var patch = preset.Tags.ToDictionary(
            static tag => tag.Key,
            static tag => (string?)tag.Value,
            StringComparer.Ordinal);
        foreach (var field in preset.Fields) {
            if (!fieldValues.TryGetValue(field.Key, out var value) || value is null) continue;
            var normalized = value.Trim();
            if (normalized.Length > 0) patch[field.Key] = normalized;
        }

        return patch;
    }
}
