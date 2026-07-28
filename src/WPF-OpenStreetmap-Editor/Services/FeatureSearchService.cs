using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public static class FeatureSearchService {
    public static IReadOnlyList<MapFeature> Filter(IEnumerable<MapFeature> features, string? query) {
        var terms = ParseTerms(query);
        if (terms.Count == 0) return features.ToList();

        return features
            .Where(feature => terms.All(term => MatchesTerm(feature, term)))
            .ToList();
    }

    public static bool Matches(MapFeature feature, string? query) {
        var terms = ParseTerms(query);
        return terms.Count == 0 || terms.All(term => MatchesTerm(feature, term));
    }

    private static IReadOnlyList<string> ParseTerms(string? query) {
        return string.IsNullOrWhiteSpace(query)
            ? []
            : query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool MatchesTerm(MapFeature feature, string term) {
        if (Contains(feature.Id, term)) return true;
        if (Contains(feature.GeometryType.ToString(), term)) return true;
        if (MatchesVisibility(feature, term)) return true;
        if (MatchesOsmMetadata(feature, term)) return true;

        var separatorIndex = term.IndexOf('=');
        if (separatorIndex >= 0) {
            var keyTerm = term[..separatorIndex];
            var valueTerm = term[(separatorIndex + 1)..];
            return feature.Attributes.Any(attribute =>
                Contains(attribute.Key, keyTerm) &&
                Contains(attribute.Value, valueTerm));
        }

        return feature.Attributes.Any(attribute =>
            Contains(attribute.Key, term) ||
            Contains(attribute.Value, term));
    }

    private static bool MatchesVisibility(MapFeature feature, string term) {
        return feature.IsHidden
            ? EqualsIgnoreCase(term, "hidden")
            : EqualsIgnoreCase(term, "visible");
    }

    private static bool MatchesOsmMetadata(MapFeature feature, string term) {
        if (feature.Osm is null) return false;

        return Contains(feature.Osm.PrimitiveType.ToString(), term) ||
            Contains(feature.Osm.Id.ToString(), term) ||
            Contains($"{feature.Osm.PrimitiveType.ToString().ToLowerInvariant()}/{feature.Osm.Id}", term) ||
            Contains($"v{feature.Osm.Version}", term);
    }

    private static bool Contains(string value, string term) {
        return term.Length == 0 || value.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private static bool EqualsIgnoreCase(string value, string term) {
        return string.Equals(value, term, StringComparison.OrdinalIgnoreCase);
    }
}
