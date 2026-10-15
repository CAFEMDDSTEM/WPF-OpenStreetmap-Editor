using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public readonly record struct VertexEditTarget(MapFeature Feature, int PartIndex, int PointIndex);

/// <summary>
/// Finds every vertex that represents the same OSM node as a given vertex so that edits
/// (drag, delete) keep junctions intact.
/// </summary>
public static class SharedVertexLocator {
    /// <summary>
    /// Returns the union of vertices sharing the hit vertex's OSM node id and vertices at the
    /// exact same coordinate. Both are needed: features drawn in this session carry no node ids
    /// yet and are joined to existing ways by coordinate until the next OSM synchronization,
    /// while tracked ways may reference one node from vertices that were moved independently.
    /// The hit vertex itself is always included. Returns an empty list for invalid indexes.
    /// </summary>
    public static IReadOnlyList<VertexEditTarget> FindTargets(IEnumerable<MapFeature> features, VertexHit hit) {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(hit);

        if (hit.PartIndex < 0 ||
            hit.PartIndex >= hit.Feature.Parts.Count ||
            hit.PointIndex < 0 ||
            hit.PointIndex >= hit.Feature.Parts[hit.PartIndex].Count) {
            return [];
        }

        var candidates = features as IReadOnlyList<MapFeature> ?? features.ToList();
        var seen = new HashSet<VertexEditTarget>();
        var targets = new List<VertexEditTarget>();
        if (TryGetOsmNodeId(hit.Feature, hit.PointIndex, out var nodeId)) {
            foreach (var target in FindByOsmNode(candidates, nodeId)) {
                if (seen.Add(target)) targets.Add(target);
            }
        }

        var point = hit.Feature.Parts[hit.PartIndex][hit.PointIndex];
        foreach (var target in FindByCoordinate(candidates, point)) {
            if (seen.Add(target)) targets.Add(target);
        }

        var self = new VertexEditTarget(hit.Feature, hit.PartIndex, NormalizeClosedRingPointIndex(hit.Feature.Parts[hit.PartIndex], hit.PointIndex));
        if (seen.Add(self)) targets.Add(self);
        return targets;
    }

    /// <summary>
    /// Returns the OSM node id behind a vertex when it can be trusted. Way features keep
    /// <see cref="OsmFeatureMetadata.NodeReferences"/> only as a cache that vertex insertion,
    /// removal and dragging do not maintain, so a reference is used solely when it is still
    /// index-aligned with the geometry and sits at the vertex's coordinate; otherwise callers
    /// fall back to coordinate matching, which is what the synchronizer does too.
    /// </summary>
    public static bool TryGetOsmNodeId(MapFeature feature, int pointIndex, out long nodeId) {
        nodeId = 0;
        if (feature.Osm?.PrimitiveType == OsmPrimitiveType.Node) {
            nodeId = feature.Osm.Id;
            return true;
        }

        if (feature.Osm?.PrimitiveType != OsmPrimitiveType.Way || feature.Parts.Count == 0) return false;

        var part = feature.Parts[0];
        var references = feature.Osm.NodeReferences;
        if (references.Count != part.Count ||
            pointIndex < 0 ||
            pointIndex >= part.Count ||
            references[pointIndex].Point != part[pointIndex]) {
            return false;
        }

        nodeId = references[pointIndex].Id;
        return true;
    }

    public static int NormalizeClosedRingPointIndex(IReadOnlyList<GeoPoint> part, int pointIndex) {
        return IsClosedRing(part) && pointIndex == part.Count - 1 ? 0 : pointIndex;
    }

    private static IEnumerable<VertexEditTarget> FindByOsmNode(IReadOnlyList<MapFeature> features, long nodeId) {
        foreach (var feature in features) {
            if (feature.GeometryType == MapGeometryType.Point &&
                feature.Osm?.PrimitiveType == OsmPrimitiveType.Node &&
                feature.Osm.Id == nodeId &&
                feature.Parts.Count > 0 &&
                feature.Parts[0].Count > 0) {
                yield return new VertexEditTarget(feature, 0, 0);
                continue;
            }

            if (feature.Osm?.PrimitiveType != OsmPrimitiveType.Way || feature.Parts.Count == 0) continue;

            var part = feature.Parts[0];
            var count = IsClosedRing(part) ? part.Count - 1 : part.Count;
            for (var i = 0; i < count; i++) {
                if (TryGetOsmNodeId(feature, i, out var candidateId) && candidateId == nodeId) {
                    yield return new VertexEditTarget(feature, 0, i);
                }
            }
        }
    }

    private static IEnumerable<VertexEditTarget> FindByCoordinate(IReadOnlyList<MapFeature> features, GeoPoint point) {
        foreach (var feature in features) {
            for (var partIndex = 0; partIndex < feature.Parts.Count; partIndex++) {
                var part = feature.Parts[partIndex];
                var count = IsClosedRing(part) ? part.Count - 1 : part.Count;
                for (var pointIndex = 0; pointIndex < count; pointIndex++) {
                    if (part[pointIndex] != point) continue;

                    yield return new VertexEditTarget(feature, partIndex, pointIndex);
                }
            }
        }
    }

    private static bool IsClosedRing(IReadOnlyList<GeoPoint> part) {
        return part.Count > 2 && part[0] == part[^1];
    }
}
