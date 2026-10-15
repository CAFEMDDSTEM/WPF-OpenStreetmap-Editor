using System.Windows;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public enum SnapTargetKind {
    Vertex,
    Segment
}

/// <summary>
/// A geometry location near the pointer that drawing and vertex editing can snap to.
/// For <see cref="SnapTargetKind.Vertex"/>, <see cref="PointIndex"/> is the vertex index within the part; a closed
/// ring's duplicated closing vertex is reported as index 0.
/// For <see cref="SnapTargetKind.Segment"/>, <see cref="PointIndex"/> is the segment's start vertex (the segment runs
/// to <c>PointIndex + 1</c>) and <see cref="Point"/> is the nearest point on that segment in document coordinates.
/// <see cref="ScreenPoint"/> is the same location on screen.
/// </summary>
public sealed record SnapTarget(
    SnapTargetKind Kind,
    MapFeature Feature,
    int PartIndex,
    int PointIndex,
    GeoPoint Point,
    System.Windows.Point ScreenPoint);

public static class DrawSnapService {
    public const double DefaultVertexTolerance = 12;
    public const double DefaultSegmentTolerance = 8;

    /// <summary>
    /// Finds the snap target nearest to <paramref name="screenPoint"/>. Any vertex within
    /// <paramref name="vertexTolerance"/> wins over segments so existing nodes are reused; otherwise the nearest
    /// segment within <paramref name="segmentTolerance"/> is returned. Hidden features and
    /// <paramref name="excludedFeature"/> are skipped.
    /// </summary>
    public static SnapTarget? FindSnapTarget(
        IEnumerable<MapFeature> candidates,
        Point screenPoint,
        GeoViewportProjection projection,
        double vertexTolerance = DefaultVertexTolerance,
        double segmentTolerance = DefaultSegmentTolerance,
        MapFeature? excludedFeature = null) {
        ArgumentNullException.ThrowIfNull(candidates);

        SnapTarget? bestVertex = null;
        var bestVertexDistance = vertexTolerance;
        SnapTarget? bestSegment = null;
        var bestSegmentDistance = segmentTolerance;
        foreach (var feature in candidates) {
            if (feature.IsHidden || ReferenceEquals(feature, excludedFeature)) continue;

            var hasSegments = feature.GeometryType != MapGeometryType.Point;
            for (var partIndex = 0; partIndex < feature.Parts.Count; partIndex++) {
                var part = feature.Parts[partIndex];
                // The closing vertex of a ring duplicates index 0, so it only participates as a segment end.
                var vertexCount = VertexEditService.IsClosedRing(part) ? part.Count - 1 : part.Count;
                var previousScreen = default(Point);
                for (var pointIndex = 0; pointIndex < part.Count; pointIndex++) {
                    var vertexScreen = projection.GeoToScreen(part[pointIndex]);
                    if (pointIndex < vertexCount) {
                        var distance = (vertexScreen - screenPoint).Length;
                        if (distance <= bestVertexDistance) {
                            bestVertexDistance = distance;
                            bestVertex = new SnapTarget(
                                SnapTargetKind.Vertex,
                                feature,
                                partIndex,
                                pointIndex,
                                part[pointIndex],
                                vertexScreen);
                        }
                    }

                    if (hasSegments && pointIndex > 0) {
                        var snappedScreen = ProjectOntoSegment(screenPoint, previousScreen, vertexScreen);
                        var distance = (snappedScreen - screenPoint).Length;
                        if (distance <= bestSegmentDistance) {
                            bestSegmentDistance = distance;
                            bestSegment = new SnapTarget(
                                SnapTargetKind.Segment,
                                feature,
                                partIndex,
                                pointIndex - 1,
                                projection.ScreenToGeo(snappedScreen),
                                snappedScreen);
                        }
                    }

                    previousScreen = vertexScreen;
                }
            }
        }

        return bestVertex ?? bestSegment;
    }

    private static Point ProjectOntoSegment(Point point, Point start, Point end) {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared;
        if (lengthSquared == 0) return start;
        var t = Math.Clamp(Vector.Multiply(point - start, segment) / lengthSquared, 0, 1);
        return start + segment * t;
    }
}
