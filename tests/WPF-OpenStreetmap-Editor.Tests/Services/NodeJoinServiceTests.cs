using System.Windows;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class NodeJoinServiceTests {
    private static readonly GeoViewportProjection Projection =
        GeoViewportProjection.Create(0, 0, 18, new Size(1000, 1000));

    [Fact]
    public void JoinPointsToWays_InsertsEachPointIntoTheSegmentItSnapsTo() {
        var way = Line(new GeoPoint(0, 0), new GeoPoint(0.002, 0));
        var first = Point(new GeoPoint(0.0005, 0.000002));
        var second = Point(new GeoPoint(0.0015, -0.000002));

        var result = NodeJoinService.JoinPointsToWays([first, second], [way], Projection, segmentTolerance: 14);

        Assert.Equal(2, result.JoinedCount);
        var wayAfter = result.After.Single(state => ReferenceEquals(state.Feature, way)).Parts[0];
        Assert.Equal(4, wayAfter.Count);
        Assert.True(wayAfter[1].Longitude < wayAfter[2].Longitude);
        Assert.Equal(wayAfter[1], result.After.Single(state => ReferenceEquals(state.Feature, first)).Parts[0][0]);
        Assert.Equal(wayAfter[2], result.After.Single(state => ReferenceEquals(state.Feature, second)).Parts[0][0]);
        Assert.Equal(2, way.Parts[0].Count);
        Assert.Equal(new GeoPoint(0.0005, 0.000002), first.Parts[0][0]);
    }

    [Fact]
    public void JoinPointsToWays_SkipsPointsWithoutNearbyWayAndNonPointFeatures() {
        var way = Line(new GeoPoint(0, 0), new GeoPoint(0.002, 0));
        var farPoint = Point(new GeoPoint(0.001, 0.01));
        var otherLine = Line(new GeoPoint(0.001, 0), new GeoPoint(0.001, 0.001));

        var result = NodeJoinService.JoinPointsToWays([farPoint, otherLine], [way, otherLine], Projection, segmentTolerance: 14);

        Assert.Equal(0, result.JoinedCount);
        Assert.Empty(result.Before);
        Assert.Empty(result.After);
    }

    private static MapFeature Line(params GeoPoint[] points) {
        return new MapFeature { GeometryType = MapGeometryType.LineString, Parts = [points.ToList()] };
    }

    private static MapFeature Point(GeoPoint point) {
        return new MapFeature { GeometryType = MapGeometryType.Point, Parts = [[point]] };
    }
}
