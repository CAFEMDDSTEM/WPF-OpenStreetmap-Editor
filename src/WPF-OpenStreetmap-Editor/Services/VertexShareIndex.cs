using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

/// <summary>
/// Snapshot index of every vertex in a feature set, keyed by coordinate and by OSM node id, so
/// whole-feature transforms can find the junction vertices other features must follow.
/// Build once per document revision; the index is not updated by later edits.
/// </summary>
public sealed class VertexShareIndex {
    private readonly Dictionary<GeoPoint, List<VertexEditTarget>> _byCoordinate = [];
    private readonly Dictionary<long, List<VertexEditTarget>> _byNodeId = [];

    private VertexShareIndex() {
    }

    public static VertexShareIndex Build(IEnumerable<MapFeature> features) {
        ArgumentNullException.ThrowIfNull(features);

        var index = new VertexShareIndex();
        foreach (var feature in features) {
            for (var partIndex = 0; partIndex < feature.Parts.Count; partIndex++) {
                var part = feature.Parts[partIndex];
                var count = part.Count > 2 && part[0] == part[^1] ? part.Count - 1 : part.Count;
                for (var pointIndex = 0; pointIndex < count; pointIndex++) {
                    var target = new VertexEditTarget(feature, partIndex, pointIndex);
                    Add(index._byCoordinate, part[pointIndex], target);
                    if (partIndex == 0 && SharedVertexLocator.TryGetOsmNodeId(feature, pointIndex, out var nodeId)) {
                        Add(index._byNodeId, nodeId, target);
                    }
                }
            }
        }

        return index;
    }

    /// <summary>Vertices sharing the given original coordinate or node id, in index order, without duplicates.</summary>
    public IReadOnlyList<VertexEditTarget> FindTargets(GeoPoint originalPoint, long? nodeId) {
        var result = new List<VertexEditTarget>();
        var seen = new HashSet<VertexEditTarget>();
        if (nodeId.HasValue && _byNodeId.TryGetValue(nodeId.Value, out var byNode)) {
            foreach (var target in byNode) {
                if (seen.Add(target)) result.Add(target);
            }
        }
        if (_byCoordinate.TryGetValue(originalPoint, out var byCoordinate)) {
            foreach (var target in byCoordinate) {
                if (seen.Add(target)) result.Add(target);
            }
        }

        return result;
    }

    private static void Add<TKey>(Dictionary<TKey, List<VertexEditTarget>> map, TKey key, VertexEditTarget target)
        where TKey : notnull {
        if (!map.TryGetValue(key, out var list)) {
            list = [];
            map[key] = list;
        }

        list.Add(target);
    }
}

/// <summary>
/// Computes how features outside an edit must change so that vertices they share with the
/// edited features (junction nodes) move along, mirroring JOSM where moving a way moves its nodes.
/// </summary>
public static class SharedVertexFollowers {
    /// <summary>
    /// Returns the features that share at least one vertex with the given states, excluding the
    /// states' own features. Use at the start of an interactive edit to capture follower originals.
    /// </summary>
    public static IReadOnlyList<MapFeature> FindFollowerFeatures(
        VertexShareIndex index,
        IReadOnlyList<FeaturePartsSnapshot> editedStates) {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(editedStates);

        var edited = editedStates.Select(static state => state.Feature).ToHashSet();
        var followers = new List<MapFeature>();
        var seen = new HashSet<MapFeature>();
        foreach (var state in editedStates) {
            for (var partIndex = 0; partIndex < state.Parts.Count; partIndex++) {
                var part = state.Parts[partIndex];
                for (var pointIndex = 0; pointIndex < part.Count; pointIndex++) {
                    long? nodeId = partIndex == 0 && SharedVertexLocator.TryGetOsmNodeId(state.Feature, pointIndex, out var id) ? id : null;
                    foreach (var target in index.FindTargets(part[pointIndex], nodeId)) {
                        if (!edited.Contains(target.Feature) && seen.Add(target.Feature)) followers.Add(target.Feature);
                    }
                }
            }
        }

        return followers;
    }

    /// <summary>
    /// Applies every vertex displacement between <paramref name="before"/> and <paramref name="after"/>
    /// to the matching shared vertices of <paramref name="followerOriginals"/> and returns the resulting
    /// follower geometry (same order as the originals). Parts whose vertex count changed are skipped
    /// because old and new vertices can no longer be paired by index.
    /// </summary>
    public static IReadOnlyList<FeaturePartsSnapshot> CreateFollowerStates(
        VertexShareIndex index,
        IReadOnlyList<FeaturePartsSnapshot> before,
        IReadOnlyList<FeaturePartsSnapshot> after,
        IReadOnlyList<FeaturePartsSnapshot> followerOriginals) {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(followerOriginals);

        var edited = before.Select(static state => state.Feature).ToHashSet();
        var followerParts = followerOriginals.ToDictionary(
            static state => state.Feature,
            static state => state.Parts.Select(static part => part.ToList()).ToList());
        var afterByFeature = after.ToDictionary(static state => state.Feature);
        foreach (var beforeState in before) {
            if (!afterByFeature.TryGetValue(beforeState.Feature, out var afterState) ||
                afterState.Parts.Count != beforeState.Parts.Count) {
                continue;
            }

            for (var partIndex = 0; partIndex < beforeState.Parts.Count; partIndex++) {
                var oldPart = beforeState.Parts[partIndex];
                var newPart = afterState.Parts[partIndex];
                if (oldPart.Count != newPart.Count) continue;

                for (var pointIndex = 0; pointIndex < oldPart.Count; pointIndex++) {
                    if (oldPart[pointIndex] == newPart[pointIndex]) continue;

                    long? nodeId = partIndex == 0 && SharedVertexLocator.TryGetOsmNodeId(beforeState.Feature, pointIndex, out var id) ? id : null;
                    foreach (var target in index.FindTargets(oldPart[pointIndex], nodeId)) {
                        if (edited.Contains(target.Feature) || !followerParts.TryGetValue(target.Feature, out var parts)) continue;

                        followerParts[target.Feature] = MapEditService.MoveVertex(parts, target.PartIndex, target.PointIndex, newPart[pointIndex]);
                    }
                }
            }
        }

        return followerOriginals
            .Select(state => new FeaturePartsSnapshot(
                state.Feature,
                followerParts[state.Feature].Select(static part => (IReadOnlyList<GeoPoint>)part).ToList()))
            .ToList();
    }
}
