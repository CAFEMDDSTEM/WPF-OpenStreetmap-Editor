using System.Windows;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class SharedVertexLocatorTests {
    [Fact]
    public void FindTargets_UnionsNodeIdAndCoordinateMatches() {
        var shared = new GeoPoint(10, 20);
        var trackedWay = CreateWay([new GeoPoint(9, 20), shared], [1, 2]);
        var trackedOtherNode = CreateWay([shared, new GeoPoint(11, 20)], [2, 3]);
        var drawnLine = CreateLine([shared, new GeoPoint(10, 21)]);
        var unrelated = CreateLine([new GeoPoint(0, 0), new GeoPoint(1, 1)]);

        var targets = SharedVertexLocator.FindTargets(
            [trackedWay, trackedOtherNode, drawnLine, unrelated],
            new VertexHit(trackedWay, 0, 1, new Point()));

        Assert.Equal(3, targets.Count);
        Assert.Contains(new VertexEditTarget(trackedWay, 0, 1), targets);
        Assert.Contains(new VertexEditTarget(trackedOtherNode, 0, 0), targets);
        Assert.Contains(new VertexEditTarget(drawnLine, 0, 0), targets);
    }

    [Fact]
    public void FindTargets_IgnoresStaleNodeReferencesAfterVertexRemoval() {
        // Way A lost its second vertex but still carries four node references; the junction with B
        // must be found by coordinate at A's new index 1, never at the stale reference index 2.
        var p0 = new GeoPoint(0, 0);
        var p1 = new GeoPoint(1, 0);
        var p2 = new GeoPoint(2, 0);
        var p3 = new GeoPoint(3, 0);
        var wayA = CreateWay([p0, p1, p2, p3], [10, 11, 12, 13]);
        wayA.Parts[0].RemoveAt(1);
        var wayB = CreateWay([p2, new GeoPoint(2, 1)], [12, 20]);

        var targets = SharedVertexLocator.FindTargets([wayA, wayB], new VertexHit(wayB, 0, 0, new Point()));

        Assert.Equal(2, targets.Count);
        Assert.Contains(new VertexEditTarget(wayA, 0, 1), targets);
        Assert.DoesNotContain(targets, target => ReferenceEquals(target.Feature, wayA) && target.PointIndex == 2);
    }

    [Fact]
    public void TryGetOsmNodeId_RequiresAlignedReferenceAtVertexCoordinate() {
        var way = CreateWay([new GeoPoint(0, 0), new GeoPoint(1, 0)], [1, 2]);

        Assert.True(SharedVertexLocator.TryGetOsmNodeId(way, 1, out var nodeId));
        Assert.Equal(2, nodeId);

        way.Parts[0][1] = new GeoPoint(1.5, 0);
        Assert.False(SharedVertexLocator.TryGetOsmNodeId(way, 1, out _));

        way.Parts[0].Insert(1, new GeoPoint(0.5, 0));
        Assert.False(SharedVertexLocator.TryGetOsmNodeId(way, 0, out _));
    }

    [Fact]
    public void FindTargets_FollowsNodeIdWhenCoordinatesDrifted() {
        var moved = new GeoPoint(10.5, 20);
        var way = CreateWay([new GeoPoint(9, 20), moved], [1, 2]);
        var pointFeature = new MapFeature {
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(10, 20)]],
            Osm = new OsmFeatureMetadata { PrimitiveType = OsmPrimitiveType.Node, Id = 2, Version = 1 }
        };

        var targets = SharedVertexLocator.FindTargets([way, pointFeature], new VertexHit(way, 0, 1, new Point()));

        Assert.Contains(new VertexEditTarget(pointFeature, 0, 0), targets);
    }

    [Fact]
    public void FindTargets_NormalizesClosingVertexOfRing() {
        var start = new GeoPoint(0, 0);
        var ring = CreateLine([start, new GeoPoint(1, 0), new GeoPoint(1, 1), start]);
        ring.GeometryType = MapGeometryType.Polygon;

        var targets = SharedVertexLocator.FindTargets([ring], new VertexHit(ring, 0, 3, new Point()));

        Assert.Equal([new VertexEditTarget(ring, 0, 0)], targets);
    }

    [Fact]
    public void FindTargets_AlwaysIncludesHitVertex() {
        var line = CreateLine([new GeoPoint(0, 0), new GeoPoint(1, 1)]);

        var targets = SharedVertexLocator.FindTargets([line], new VertexHit(line, 0, 1, new Point()));

        Assert.Equal([new VertexEditTarget(line, 0, 1)], targets);
    }

    [Fact]
    public void FindTargets_ReturnsEmptyForInvalidIndex() {
        var line = CreateLine([new GeoPoint(0, 0), new GeoPoint(1, 1)]);

        Assert.Empty(SharedVertexLocator.FindTargets([line], new VertexHit(line, 0, 5, new Point())));
        Assert.Empty(SharedVertexLocator.FindTargets([line], new VertexHit(line, 1, 0, new Point())));
    }

    private static MapFeature CreateLine(List<GeoPoint> points) {
        return new MapFeature {
            GeometryType = MapGeometryType.LineString,
            Parts = [points]
        };
    }

    private static MapFeature CreateWay(List<GeoPoint> points, long[] nodeIds) {
        var feature = CreateLine(points);
        feature.Osm = new OsmFeatureMetadata {
            PrimitiveType = OsmPrimitiveType.Way,
            Id = 100,
            Version = 1,
            NodeReferences = points.Select((point, index) => new OsmNodeReference(nodeIds[index], 1, point)).ToList()
        };
        return feature;
    }
}
