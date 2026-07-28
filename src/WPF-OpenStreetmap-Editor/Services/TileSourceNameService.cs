namespace WPF_OpenStreetmap_Editor.Services;

internal static class TileSourceNameService {
    public static string CreateUniqueName(
        IEnumerable<TileSourcePreset> sources,
        string baseName,
        DateTime? now = null) {
        ArgumentNullException.ThrowIfNull(sources);

        var existingNames = sources
            .Select(static source => source.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (!existingNames.Contains(baseName)) return baseName;

        for (var i = 2; i < 1000; i++) {
            var candidate = $"{baseName} {i}";
            if (!existingNames.Contains(candidate)) return candidate;
        }

        return $"{baseName} {(now ?? DateTime.Now):HHmmss}";
    }
}
