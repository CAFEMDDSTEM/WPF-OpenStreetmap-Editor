using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

/// <summary>
/// Magic-wand selection: finds the features that are the same kind of thing as a seed feature,
/// optionally limited to those connected to the seed through shared vertices.
/// </summary>
public static class SimilarFeatureSelector {
    /// <summary>Keys that define what a feature is, most specific first; the first key present wins.</summary>
    private static readonly string[] PrimaryKeys = [
        "highway", "railway", "waterway", "aeroway", "aerialway", "building", "landuse", "natural", "leisure",
        "amenity", "shop", "tourism", "man_made", "power", "barrier", "boundary", "place", "public_transport",
        "route", "historic", "office", "craft", "emergency", "military"
    ];

    /// <summary>The tag that defines what a feature is, or null when it carries no primary key.</summary>
    public static KeyValuePair<string, string>? GetPrimaryTag(IReadOnlyDictionary<string, string> tags) {
        ArgumentNullException.ThrowIfNull(tags);

        foreach (var key in PrimaryKeys) {
            if (tags.TryGetValue(key, out var value)) return new KeyValuePair<string, string>(key, value);
        }

        return null;
    }

    /// <summary>
    /// Two features are similar when they have the same geometry type and either both carry the same
    /// primary tag, or neither carries one and their tag sets are equal (so two untagged lines are
    /// similar while an untagged line and a named one are not). Comparison is ordinal.
    /// </summary>
    public static bool AreSimilar(MapFeature first, MapFeature second) {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (first.GeometryType != second.GeometryType) return false;

        var firstPrimary = GetPrimaryTag(first.Attributes);
        var secondPrimary = GetPrimaryTag(second.Attributes);
        if (firstPrimary is null && secondPrimary is null) return HaveEqualTags(first.Attributes, second.Attributes);

        return firstPrimary is { } a &&
            secondPrimary is { } b &&
            string.Equals(a.Key, b.Key, StringComparison.Ordinal) &&
            string.Equals(a.Value, b.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns the seed followed by every visible candidate similar to it, in candidate order. With
    /// <paramref name="contiguous"/> only features reachable from the seed through shared vertices are
    /// returned, breadth-first in discovery order; non-similar or hidden features never act as a bridge,
    /// and point features are never connected to anything.
    /// </summary>
    public static IReadOnlyList<MapFeature> SelectSimilar(MapFeature seed, IEnumerable<MapFeature> candidates, bool contiguous) {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(candidates);

        var similar = new List<MapFeature> { seed };
        var seen = new HashSet<MapFeature> { seed };
        foreach (var candidate in candidates) {
            if (candidate.IsHidden || !AreSimilar(seed, candidate) || !seen.Add(candidate)) continue;

            similar.Add(candidate);
        }

        if (!contiguous) return similar;
        if (seed.GeometryType == MapGeometryType.Point) return [seed];

        return CollectConnected(seed, similar);
    }

    private static List<MapFeature> CollectConnected(MapFeature seed, IReadOnlyList<MapFeature> similar) {
        var index = VertexShareIndex.Build(similar);
        var result = new List<MapFeature> { seed };
        var visited = new HashSet<MapFeature> { seed };
        var queue = new Queue<MapFeature>();
        queue.Enqueue(seed);
        while (queue.TryDequeue(out var feature)) {
            for (var partIndex = 0; partIndex < feature.Parts.Count; partIndex++) {
                var part = feature.Parts[partIndex];
                for (var pointIndex = 0; pointIndex < part.Count; pointIndex++) {
                    long? nodeId = partIndex == 0 && SharedVertexLocator.TryGetOsmNodeId(feature, pointIndex, out var id) ? id : null;
                    foreach (var target in index.FindTargets(part[pointIndex], nodeId)) {
                        if (!visited.Add(target.Feature)) continue;

                        result.Add(target.Feature);
                        queue.Enqueue(target.Feature);
                    }
                }
            }
        }

        return result;
    }

    private static bool HaveEqualTags(IReadOnlyDictionary<string, string> first, IReadOnlyDictionary<string, string> second) {
        if (first.Count != second.Count) return false;

        foreach (var (key, value) in first) {
            if (!second.TryGetValue(key, out var other) || !string.Equals(value, other, StringComparison.Ordinal)) return false;
        }

        return true;
    }
}
