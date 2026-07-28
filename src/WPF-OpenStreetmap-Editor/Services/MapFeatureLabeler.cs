using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

internal enum MapFeatureLabelKind {
    Name,
    Ref
}

internal readonly record struct MapFeatureLabel(string Text, MapFeatureLabelKind Kind);

internal static class MapFeatureLabeler {
    private static readonly string[] NameKeys = [
        "name",
        "name:zh",
        "name:zh-Hans",
        "name:en",
        "official_name",
        "short_name",
        "brand",
        "operator",
        "alt_name",
        "loc_name"
    ];
    private static readonly string[] RoadRefKeys = [
        "ref",
        "int_ref",
        "nat_ref",
        "national_ref",
        "reg_ref",
        "regional_ref",
        "prov_ref",
        "province_ref",
        "county_ref",
        "local_ref",
        "route_ref",
        "unsigned_ref",
        "destination:ref",
        "ref:CN:national",
        "ref:CN:provincial",
        "ref:CN:county",
        "ref:cn:national",
        "ref:cn:provincial",
        "ref:cn:county"
    ];

    public static IReadOnlyList<MapFeatureLabel> GetLabels(MapFeature feature) {
        ArgumentNullException.ThrowIfNull(feature);

        var labels = new List<MapFeatureLabel>(2);
        var name = GetFirstValue(feature, NameKeys);
        var roadRef = IsRoad(feature) ? GetFirstValue(feature, RoadRefKeys) : null;

        if (!string.IsNullOrWhiteSpace(name)) {
            labels.Add(new MapFeatureLabel(name!, MapFeatureLabelKind.Name));
        }

        if (!string.IsNullOrWhiteSpace(roadRef) &&
            !string.Equals(name, roadRef, StringComparison.OrdinalIgnoreCase)) {
            labels.Add(new MapFeatureLabel(roadRef!, MapFeatureLabelKind.Ref));
        }

        return labels;
    }

    private static bool IsRoad(MapFeature feature) {
        return feature.Attributes.Keys.Any(key => string.Equals(key, "highway", StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetFirstValue(MapFeature feature, IEnumerable<string> keys) {
        foreach (var key in keys) {
            if (TryGetAttribute(feature, key, out var value) && !string.IsNullOrWhiteSpace(value)) {
                return value.Trim();
            }
        }

        foreach (var attribute in feature.Attributes) {
            if (IsLocalizedNameKey(attribute.Key) && !string.IsNullOrWhiteSpace(attribute.Value)) {
                return attribute.Value.Trim();
            }
        }

        return null;
    }

    private static bool IsLocalizedNameKey(string key) {
        if (!key.StartsWith("name:", StringComparison.OrdinalIgnoreCase)) return false;

        var suffix = key[5..];
        return suffix.Length is >= 2 and <= 12 &&
            suffix.All(static character =>
                char.IsAsciiLetter(character) ||
                char.IsAsciiDigit(character) ||
                character == '-' ||
                character == '_');
    }

    private static bool TryGetAttribute(MapFeature feature, string key, out string value) {
        if (feature.Attributes.TryGetValue(key, out value!)) return true;

        foreach (var attribute in feature.Attributes) {
            if (string.Equals(attribute.Key, key, StringComparison.OrdinalIgnoreCase)) {
                value = attribute.Value;
                return true;
            }
        }

        value = "";
        return false;
    }
}
