using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed record NodeJoinResult(
    IReadOnlyList<FeaturePartsSnapshot> Before,
    IReadOnlyList<FeaturePartsSnapshot> After,
    int JoinedCount);

/// <summary>JOSM "Join node to way": moves point features onto the nearest line segment and inserts them as vertices there.</summary>
public static class NodeJoinService {
    public static NodeJoinResult JoinPointsToWays(
        IReadOnlyList<MapFeature> points,
        IReadOnlyList<MapFeature> candidates,
        GeoViewportProjection projection,
        double segmentTolerance) {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(candidates);

        // Snapping runs against working copies that already contain earlier insertions so segment
        // indexes stay valid; the map resolves each copy back to the real feature.
        var working = candidates.Where(static feature => feature.GeometryType != MapGeometryType.Point).ToList();
        var copyToOriginal = new Dictionary<MapFeature, MapFeature>();
        var wayBefore = new Dictionary<MapFeature, FeaturePartsSnapshot>();
        var wayAfter = new Dictionary<MapFeature, FeaturePartsSnapshot>();
        var pointBefore = new List<FeaturePartsSnapshot>();
        var pointAfter = new List<FeaturePartsSnapshot>();
        foreach (var point in points) {
            if (point.GeometryType != MapGeometryType.Point || point.Parts.Count != 1 || point.Parts[0].Count != 1) continue;

            var location = point.Parts[0][0];
            var snap = DrawSnapService.FindSnapTarget(
                working,
                projection.GeoToScreen(location),
                projection,
                vertexTolerance: 0,
                segmentTolerance: segmentTolerance);
            if (snap is null || snap.Kind != SnapTargetKind.Segment) continue;

            var way = copyToOriginal.GetValueOrDefault(snap.Feature, snap.Feature);
            if (!wayBefore.ContainsKey(way)) wayBefore[way] = Capture(way);
            var currentParts = wayAfter.TryGetValue(way, out var pending) ? pending.Parts : wayBefore[way].Parts;
            var afterParts = VertexEditService.InsertVertex(currentParts, snap.PartIndex, snap.PointIndex, snap.Point);
            wayAfter[way] = new FeaturePartsSnapshot(way, afterParts);
            pointBefore.Add(Capture(point));
            pointAfter.Add(new FeaturePartsSnapshot(point, [[snap.Point]]));

            var workingCopy = new MapFeature {
                Id = way.Id,
                GeometryType = way.GeometryType,
                Parts = afterParts,
                IsHidden = way.IsHidden
            };
            copyToOriginal[workingCopy] = way;
            working = working.Select(feature => ReferenceEquals(feature, snap.Feature) ? workingCopy : feature).ToList();
        }

        return new NodeJoinResult(
            wayBefore.Values.Concat(pointBefore).ToList(),
            wayAfter.Values.Concat(pointAfter).ToList(),
            pointAfter.Count);
    }

    private static FeaturePartsSnapshot Capture(MapFeature feature) {
        return new FeaturePartsSnapshot(
            feature,
            feature.Parts.Select(static part => (IReadOnlyList<GeoPoint>)part.ToList()).ToList());
    }
}
