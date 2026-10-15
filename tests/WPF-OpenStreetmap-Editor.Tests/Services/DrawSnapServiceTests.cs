using System.Windows;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class DrawSnapServiceTests {
    private static readonly GeoViewportProjection Projection = GeoViewportProjection.Create(0, 0, 16, new Size(800, 600));

    [Fact]
    public void FindSnapTarget_PrefersVertexOverCloserSegment() {
        var line = CreateLine(new GeoPoint(0, 0), new GeoPoint(0.002, 0));
        // 3px from the segment but about 6.7px from the vertex: the vertex must still win.
        var pointer = Projection.GeoToScreen(line.Parts[0][0]) + new Vector(6, 3);

        var target = DrawSnapService.FindSnapTarget([line], pointer, Projection);

        Assert.NotNull(target);
        Assert.Equal(SnapTargetKind.Vertex, target.Kind);
        Assert.Same(line, target.Feature);
        Assert.Equal(0, target.PartIndex);
        Assert.Equal(0, target.PointIndex);
        Assert.Equal(line.Parts[0][0], target.Point);
        Assert.Equal(Projection.GeoToScreen(line.Parts[0][0]), target.ScreenPoint);
    }

    [Fact]
    public void FindSnapTarget_ChoosesNearestVertex() {
        var line = CreateLine(new GeoPoint(0, 0), new GeoPoint(0.001, 0), new GeoPoint(0.002, 0));
        var pointer = Projection.GeoToScreen(line.Parts[0][1]) + new Vector(3, 0);

        var target = DrawSnapService.FindSnapTarget([line], pointer, Projection, vertexTolerance: 200);

        Assert.NotNull(target);
        Assert.Equal(SnapTargetKind.Vertex, target.Kind);
        Assert.Equal(1, target.PointIndex);
    }

    [Fact]
    public void FindSnapTarget_SnapsToPointFeatureAsVertex() {
        var point = CreatePoint(new GeoPoint(0.001, 0.001));
        var pointScreen = Projection.GeoToScreen(point.Parts[0][0]);

        var target = DrawSnapService.FindSnapTarget([point], pointScreen + new Vector(4, -4), Projection);

        Assert.NotNull(target);
        Assert.Equal(SnapTargetKind.Vertex, target.Kind);
        Assert.Same(point, target.Feature);
        Assert.Equal(0, target.PartIndex);
        Assert.Equal(0, target.PointIndex);
        Assert.Equal(point.Parts[0][0], target.Point);
        Assert.Equal(pointScreen, target.ScreenPoint);
    }

    [Fact]
    public void FindSnapTarget_SnapsToProjectedPointOnSegment() {
        var line = CreateLine(new GeoPoint(0, 0), new GeoPoint(0.002, 0));
        var start = Projection.GeoToScreen(line.Parts[0][0]);
        var end = Projection.GeoToScreen(line.Parts[0][1]);
        var pointer = new Point((start.X + end.X) / 2.0, start.Y + 5);

        var target = DrawSnapService.FindSnapTarget([line], pointer, Projection);

        Assert.NotNull(target);
        Assert.Equal(SnapTargetKind.Segment, target.Kind);
        Assert.Same(line, target.Feature);
        Assert.Equal(0, target.PartIndex);
        Assert.Equal(0, target.PointIndex);
        Assert.InRange((target.ScreenPoint - pointer).Length, 0, DrawSnapService.DefaultSegmentTolerance);
        Assert.Equal(start.Y, target.ScreenPoint.Y, 6);
        Assert.InRange(target.ScreenPoint.X, Math.Min(start.X, end.X), Math.Max(start.X, end.X));
        Assert.Equal(0, target.Point.Latitude, 9);
        Assert.InRange(target.Point.Longitude, 0, 0.002);
        var roundTrip = Projection.GeoToScreen(target.Point);
        Assert.Equal(target.ScreenPoint.X, roundTrip.X, 6);
        Assert.Equal(target.ScreenPoint.Y, roundTrip.Y, 6);
    }

    [Fact]
    public void FindSnapTarget_ChoosesNearestSegment() {
        var line = CreateLine(new GeoPoint(0, 0), new GeoPoint(0.002, 0), new GeoPoint(0.002, 0.002));
        var start = Projection.GeoToScreen(line.Parts[0][1]);
        var end = Projection.GeoToScreen(line.Parts[0][2]);
        var pointer = new Point(start.X + 4, (start.Y + end.Y) / 2.0);

        var target = DrawSnapService.FindSnapTarget([line], pointer, Projection);

        Assert.NotNull(target);
        Assert.Equal(SnapTargetKind.Segment, target.Kind);
        Assert.Equal(1, target.PointIndex);
    }

    [Fact]
    public void FindSnapTarget_NormalizesClosingVertexOfRingToIndexZero() {
        var polygon = CreatePolygon(
            new GeoPoint(0, 0),
            new GeoPoint(0.002, 0),
            new GeoPoint(0.002, 0.002),
            new GeoPoint(0, 0.002),
            new GeoPoint(0, 0));
        var pointer = Projection.GeoToScreen(polygon.Parts[0][^1]) + new Vector(2, 2);

        var target = DrawSnapService.FindSnapTarget([polygon], pointer, Projection);

        Assert.NotNull(target);
        Assert.Equal(SnapTargetKind.Vertex, target.Kind);
        Assert.Equal(0, target.PointIndex);
    }

    [Fact]
    public void FindSnapTarget_IgnoresHiddenFeatures() {
        var hidden = CreatePoint(new GeoPoint(0, 0));
        hidden.IsHidden = true;
        var pointer = Projection.GeoToScreen(hidden.Parts[0][0]);

        Assert.Null(DrawSnapService.FindSnapTarget([hidden], pointer, Projection));
    }

    [Fact]
    public void FindSnapTarget_IgnoresExcludedFeature() {
        var excluded = CreateLine(new GeoPoint(0, 0), new GeoPoint(0.002, 0));
        var pointer = Projection.GeoToScreen(excluded.Parts[0][0]);
        var other = CreatePoint(Projection.ScreenToGeo(pointer + new Vector(6, 0)));

        var target = DrawSnapService.FindSnapTarget([excluded, other], pointer, Projection, excludedFeature: excluded);

        Assert.NotNull(target);
        Assert.Same(other, target.Feature);
        Assert.Null(DrawSnapService.FindSnapTarget([excluded], pointer, Projection, excludedFeature: excluded));
    }

    [Fact]
    public void FindSnapTarget_ReturnsNullOutsideTolerance() {
        var line = CreateLine(new GeoPoint(0, 0), new GeoPoint(0.002, 0));
        var pointer = Projection.GeoToScreen(line.Parts[0][0]) + new Vector(0, 30);

        Assert.Null(DrawSnapService.FindSnapTarget([line], pointer, Projection));

        var widened = DrawSnapService.FindSnapTarget([line], pointer, Projection, segmentTolerance: 40);
        Assert.NotNull(widened);
        Assert.Equal(SnapTargetKind.Segment, widened.Kind);
    }

    private static MapFeature CreatePoint(GeoPoint point) {
        return new MapFeature {
            GeometryType = MapGeometryType.Point,
            Parts = [[point]]
        };
    }

    private static MapFeature CreateLine(params GeoPoint[] points) {
        return new MapFeature {
            GeometryType = MapGeometryType.LineString,
            Parts = [points.ToList()]
        };
    }

    private static MapFeature CreatePolygon(params GeoPoint[] ring) {
        return new MapFeature {
            GeometryType = MapGeometryType.Polygon,
            Parts = [ring.ToList()]
        };
    }
}
