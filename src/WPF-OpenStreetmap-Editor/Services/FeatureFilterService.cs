using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public static class FeatureFilterService {
    public static FeatureFilterResult Evaluate(
        MapFeature feature,
        IEnumerable<FeatureFilterDefinition> filters) {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(filters);

        var hide = false;
        var dim = false;
        var matches = new List<string>();

        foreach (var filter in filters) {
            if (!filter.IsEnabled) continue;

            var isMatch = FeatureSearchService.Matches(feature, filter.Query);
            if (filter.IsInverse) isMatch = !isMatch;
            if (!isMatch) continue;

            matches.Add(filter.Id);
            if (filter.Effect == FeatureFilterEffect.Hide) hide = true;
            if (filter.Effect == FeatureFilterEffect.Dim) dim = true;
        }

        return matches.Count == 0
            ? FeatureFilterResult.Visible
            : new FeatureFilterResult(hide, dim, matches.AsReadOnly());
    }

    public static IReadOnlyDictionary<string, FeatureFilterResult> EvaluateAll(
        IEnumerable<MapFeature> features,
        IEnumerable<FeatureFilterDefinition> filters) {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(filters);

        var filterSnapshot = filters.ToList();
        return features.ToDictionary(
            static feature => feature.Id,
            feature => Evaluate(feature, filterSnapshot),
            StringComparer.Ordinal);
    }
}
