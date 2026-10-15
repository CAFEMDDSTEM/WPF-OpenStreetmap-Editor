using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class VertexShareIndexTests {
    [Fact]
    public void FindTargets_CombinesNodeIdAndCoordinateMatches() {
        var junction = new GeoPoint(10, 20);
        var tracked = CreateWay([new GeoPoint(9, 20), junction], [1, 2]);
        var drifted = CreateWay([new GeoPoint(10.1, 20), new GeoPoint(11, 20)], [2, 3]);
        var drawn = CreateLine([junction, new GeoPoint(10, 21)]);

        var index = VertexShareIndex.Build([tracked, drifted, drawn]);
        var targets = index.FindTargets(junction, nodeId: 2);

        Assert.Equal(3, targets.Count);
        Assert.Contains(new VertexEditTarget(tracked, 0, 1), targets);
        Assert.Contains(new VertexEditTarget(drifted, 0, 0), targets);
        Assert.Contains(new VertexEditTarget(drawn, 0, 0), targets);
    }

    [Fact]
    public void FindFollowerFeatures_ExcludesEditedFeatures() {
        var junction = new GeoPoint(0, 0);
        var moved = CreateLine([junction, new GeoPoint(1, 0)]);
        var alsoMoved = CreateLine([junction, new GeoPoint(0, 1)]);
        var follower = CreateLine([junction, new GeoPoint(-1, 0)]);
        var unrelated = CreateLine([new GeoPoint(5, 5), new GeoPoint(6, 6)]);
        var index = VertexShareIndex.Build([moved, alsoMoved, follower, unrelated]);

        var followers = SharedVertexFollowers.FindFollowerFeatures(index, [Capture(moved), Capture(alsoMoved)]);

        Assert.Equal([follower], followers);
    }

    [Fact]
    public void CreateFollowerStates_MovesSharedVertexAndKeepsRingClosed() {
        var junction = new GeoPoint(0, 0);
        var road = CreateLine([junction, new GeoPoint(1, 0)]);
        var building = CreateLine([junction, new GeoPoint(0, -1), new GeoPoint(-1, -1), junction]);
        building.GeometryType = MapGeometryType.Polygon;
        var index = VertexShareIndex.Build([road, building]);
        var before = Capture(road);
        var after = new FeaturePartsSnapshot(road, [[new GeoPoint(0.5, 0.5), new GeoPoint(1.5, 0.5)]]);

        var followers = SharedVertexFollowers.CreateFollowerStates(index, [before], [after], [Capture(building)]);

        var ring = Assert.Single(followers).Parts[0];
        Assert.Equal(new GeoPoint(0.5, 0.5), ring[0]);
        Assert.Equal(new GeoPoint(0.5, 0.5), ring[^1]);
        Assert.Equal(new GeoPoint(0, -1), ring[1]);
        Assert.Equal(junction, building.Parts[0][0]);
    }

    [Fact]
    public void CreateFollowerStates_SkipsPartsWhoseVertexCountChanged() {
        var junction = new GeoPoint(0, 0);
        var road = CreateLine([junction, new GeoPoint(1, 0)]);
        var other = CreateLine([junction, new GeoPoint(0, 1)]);
        var index = VertexShareIndex.Build([road, other]);
        var after = new FeaturePartsSnapshot(road, [[new GeoPoint(2, 2), new GeoPoint(3, 3), new GeoPoint(4, 4)]]);

        var followers = SharedVertexFollowers.CreateFollowerStates(index, [Capture(road)], [after], [Capture(other)]);

        Assert.Equal(junction, Assert.Single(followers).Parts[0][0]);
    }

    [Fact]
    public void CreateFollowerStates_DoesNotTrustStaleReferencesAfterVertexRemoval() {
        // Audit scenario: A lost vertex p1 (references stale), B shares A's node at p2. Moving B must
        // drag A's vertex at p2 (now index 1) and leave A's p3 alone.
        var p0 = new GeoPoint(0, 0);
        var p2 = new GeoPoint(2, 0);
        var p3 = new GeoPoint(3, 0);
        var wayA = CreateWay([p0, new GeoPoint(1, 0), p2, p3], [10, 11, 12, 13]);
        wayA.Parts[0].RemoveAt(1);
        var wayB = CreateWay([p2, new GeoPoint(2, 1)], [12, 20]);
        var index = VertexShareIndex.Build([wayA, wayB]);
        var moved = new GeoPoint(2.5, 0.5);
        var after = new FeaturePartsSnapshot(wayB, [[moved, new GeoPoint(2.5, 1.5)]]);

        var followers = SharedVertexFollowers.CreateFollowerStates(index, [Capture(wayB)], [after], [Capture(wayA)]);

        Assert.Equal([p0, moved, p3], Assert.Single(followers).Parts[0]);
    }

    private static FeaturePartsSnapshot Capture(MapFeature feature) {
        return new FeaturePartsSnapshot(
            feature,
            feature.Parts.Select(static part => (IReadOnlyList<GeoPoint>)part.ToList()).ToList());
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
