using System.Diagnostics;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class TopologyValidationRulesTests {
    private const double MetersPerDegree = 6_371_008.8 * Math.PI / 180.0;

    [Fact]
    public void DuplicateNodes_ReportsOneIssuePerGroupNamingEveryNode() {
        var dataset = new OsmDataset();
        dataset.Nodes[1] = Node(1, 0, 0);
        dataset.Nodes[2] = Node(2, 0, 0);
        dataset.Nodes[3] = Node(3, 0, 0);
        dataset.Nodes[4] = Node(4, 1, 1);

        var issue = Assert.Single(Validate(new DuplicateNodeValidationRule(), dataset));

        Assert.Equal("topology.duplicate-nodes", issue.RuleId);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.Equal(new ValidationObjectReference(ValidationObjectType.Node, "1"), issue.Target);
        Assert.Equal("3 nodes share the same position (1, 2, 3).", issue.Message);
    }

    [Fact]
    public void DuplicateNodes_IgnoresDistinctCoordinates() {
        var dataset = new OsmDataset();
        dataset.Nodes[1] = Node(1, 0, 0);
        dataset.Nodes[2] = Node(2, 0, 0.0000001);
        dataset.Nodes[3] = Node(3, 1, 1);

        Assert.Empty(Validate(new DuplicateNodeValidationRule(), dataset));
    }

    [Fact]
    public void DuplicateNodes_TruncatesLongIdLists() {
        var dataset = new OsmDataset();
        for (var id = 1; id <= 7; id++) dataset.Nodes[id] = Node(id, 0, 0);

        var issue = Assert.Single(Validate(new DuplicateNodeValidationRule(), dataset));

        Assert.Equal("7 nodes share the same position (1, 2, 3, 4, 5, …).", issue.Message);
    }

    [Fact]
    public void DuplicateNodes_ReportsCoincidingUntrackedPointFeatures() {
        var first = Feature(MapGeometryType.Point, [new GeoPoint(0, 0)]);
        var second = Feature(MapGeometryType.Point, [new GeoPoint(0, 0)]);
        var elsewhere = Feature(MapGeometryType.Point, [new GeoPoint(1, 1)]);

        var issue = Assert.Single(new DuplicateNodeValidationRule().Validate(new ValidationContext([first, second, elsewhere])));

        Assert.Equal("topology.duplicate-nodes", issue.RuleId);
        Assert.Equal(new ValidationObjectReference(ValidationObjectType.Feature, first.Id), issue.Target);
    }

    [Fact]
    public void DuplicateNodes_IgnoresFeatureCoincidingWithDatasetNode() {
        var dataset = new OsmDataset();
        dataset.Nodes[1] = Node(1, 0, 0);
        var feature = Feature(MapGeometryType.Point, [new GeoPoint(0, 0)]);

        Assert.Empty(new DuplicateNodeValidationRule().Validate(new ValidationContext([feature], dataset)));
    }

    [Fact]
    public void UntaggedObjects_ReportsUntaggedWaysAndNodesThatNothingReferences() {
        var dataset = new OsmDataset();
        for (var id = 1; id <= 5; id++) dataset.Nodes[id] = Node(id, id, 0);
        dataset.Ways[10] = new OsmWay { Id = 10, NodeIds = [1, 2] };
        dataset.Ways[11] = new OsmWay { Id = 11, NodeIds = [2, 3], Tags = { ["highway"] = "residential" } };
        dataset.Ways[12] = new OsmWay { Id = 12, NodeIds = [3, 1] };
        dataset.Relations[20] = new OsmRelation {
            Id = 20,
            Tags = { ["type"] = "site" },
            Members = [
                new OsmRelationMember(OsmRelationMemberType.Way, 12, "outline"),
                new OsmRelationMember(OsmRelationMemberType.Node, 5, "entrance")
            ]
        };

        var issues = Validate(new UntaggedObjectValidationRule(), dataset);

        var way = Assert.Single(issues, issue => issue.RuleId == "topology.untagged-way");
        Assert.Equal(new ValidationObjectReference(ValidationObjectType.Way, "10"), way.Target);
        var node = Assert.Single(issues, issue => issue.RuleId == "topology.untagged-node");
        Assert.Equal(new ValidationObjectReference(ValidationObjectType.Node, "4"), node.Target);
        Assert.All(issues, issue => Assert.Equal(ValidationSeverity.Warning, issue.Severity));
    }

    [Fact]
    public void UntaggedObjects_ReportsUntrackedLineFeaturesButSkipsUntrackedPoints() {
        var line = Feature(MapGeometryType.LineString, [new GeoPoint(0, 0), new GeoPoint(1, 1)]);
        var point = Feature(MapGeometryType.Point, [new GeoPoint(0, 0)]);
        var taggedLine = Feature(MapGeometryType.LineString, [new GeoPoint(0, 0), new GeoPoint(1, 1)]);
        taggedLine.Attributes["highway"] = "path";

        var issue = Assert.Single(new UntaggedObjectValidationRule().Validate(new ValidationContext([line, point, taggedLine])));

        Assert.Equal("topology.untagged-way", issue.RuleId);
        Assert.Equal(new ValidationObjectReference(ValidationObjectType.Feature, line.Id), issue.Target);
    }

    [Fact]
    public void HighwayConnectivity_ReportsEndNodeNearOtherHighway() {
        var dataset = new OsmDataset();
        AddHighway(dataset, 100, "residential", Meters(0, 0), Meters(100, 0));
        var spur = AddHighway(dataset, 101, "residential", Meters(50, -50), Meters(50, -2));

        var issue = Assert.Single(Validate(new HighwayConnectivityValidationRule(), dataset));

        Assert.Equal("topology.way-end-near-highway", issue.RuleId);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.Equal(new ValidationObjectReference(ValidationObjectType.Way, "101"), issue.Target);
        Assert.Equal(
            $"Highway end node {spur.NodeIds[^1]} is 2.0 m from highway way 100; possibly missing connection.",
            issue.Message);
    }

    [Fact]
    public void HighwayConnectivity_IgnoresMappedDeadEndsAndOtherLevels() {
        var dataset = new OsmDataset();
        AddHighway(dataset, 100, "residential", Meters(0, 0), Meters(100, 0));
        var deadEnd = AddHighway(dataset, 101, "residential", Meters(50, -50), Meters(50, -2));
        dataset.Nodes[deadEnd.NodeIds[^1]].Tags["noexit"] = "yes";
        AddWay(dataset, 102, [Meters(0, 20), Meters(100, 20)], ("highway", "residential"), ("bridge", "yes"), ("layer", "1"));
        AddHighway(dataset, 103, "residential", Meters(50, 60), Meters(50, 22));

        Assert.Empty(Validate(new HighwayConnectivityValidationRule(), dataset));
    }

    [Fact]
    public void HighwayConnectivity_IgnoresEndNodeSharedWithOtherWay() {
        var dataset = new OsmDataset();
        var road = AddHighway(dataset, 100, "residential", Meters(0, 0), Meters(50, 0), Meters(100, 0));
        var spur = AddHighway(dataset, 101, "residential", Meters(50, -50));
        spur.NodeIds.Add(road.NodeIds[1]);

        Assert.Empty(Validate(new HighwayConnectivityValidationRule(), dataset));
    }

    [Fact]
    public void HighwayConnectivity_IgnoresEndNodeFarFromOtherHighway() {
        var dataset = new OsmDataset();
        AddHighway(dataset, 100, "residential", Meters(0, 0), Meters(100, 0));
        AddHighway(dataset, 101, "residential", Meters(50, -70), Meters(50, -20));

        Assert.Empty(Validate(new HighwayConnectivityValidationRule(), dataset));
    }

    [Fact]
    public void HighwayConnectivity_IgnoresNonRoutableHighways() {
        var dataset = new OsmDataset();
        AddHighway(dataset, 100, "platform", Meters(0, 0), Meters(100, 0));
        AddHighway(dataset, 101, "residential", Meters(50, -50), Meters(50, -2));

        Assert.Empty(Validate(new HighwayConnectivityValidationRule(), dataset));
    }

    [Fact]
    public void HighwayConnectivity_ReportsTouchingEndNodeAsMissingConnectionNotCrossing() {
        var dataset = new OsmDataset();
        AddHighway(dataset, 100, "residential", Meters(0, 0), Meters(100, 0));
        var spur = AddHighway(dataset, 101, "residential", Meters(50, -50), Meters(50, 0));

        var issue = Assert.Single(Validate(new HighwayConnectivityValidationRule(), dataset));

        Assert.Equal("topology.way-end-near-highway", issue.RuleId);
        Assert.Equal(
            $"Highway end node {spur.NodeIds[^1]} is 0.0 m from highway way 100; possibly missing connection.",
            issue.Message);
    }

    [Fact]
    public void HighwayConnectivity_ReportsCrossingHighwaysOncePerWayPair() {
        var dataset = new OsmDataset();
        AddHighway(dataset, 101, "residential", Meters(0, 0), Meters(100, 0));
        AddHighway(dataset, 100, "residential", Meters(30, -50), Meters(50, 50), Meters(70, -50));

        var issue = Assert.Single(Validate(new HighwayConnectivityValidationRule(), dataset));

        Assert.Equal("topology.crossing-highways", issue.RuleId);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.Equal(new ValidationObjectReference(ValidationObjectType.Way, "100"), issue.Target);
        Assert.Equal("Highway way 100 crosses highway way 101 without a shared node.", issue.Message);
    }

    [Fact]
    public void HighwayConnectivity_IgnoresCrossingAtSharedNode() {
        var dataset = new OsmDataset();
        var road = AddHighway(dataset, 100, "residential", Meters(0, 0), Meters(50, 0), Meters(100, 0));
        var crossing = AddHighway(dataset, 101, "residential", Meters(50, -50), Meters(50, 50));
        crossing.NodeIds.Insert(1, road.NodeIds[1]);

        Assert.Empty(Validate(new HighwayConnectivityValidationRule(), dataset));
    }

    [Theory]
    [InlineData("bridge", "yes", 0)]
    [InlineData("tunnel", "culvert", 0)]
    [InlineData("layer", "-1", 0)]
    [InlineData("bridge", "no", 1)]
    public void HighwayConnectivity_IgnoresCrossingsOnDifferentLevels(string key, string value, int expectedIssues) {
        var dataset = new OsmDataset();
        AddWay(dataset, 100, [Meters(0, 0), Meters(100, 0)], ("highway", "residential"), (key, value));
        AddHighway(dataset, 101, "residential", Meters(50, -50), Meters(50, 50));

        var issues = Validate(new HighwayConnectivityValidationRule(), dataset);

        Assert.Equal(expectedIssues, issues.Count(issue => issue.RuleId == "topology.crossing-highways"));
    }

    [Fact]
    public void HighwayConnectivity_SkipsFeaturesWithoutDataset() {
        var line = Feature(MapGeometryType.LineString, [new GeoPoint(0, 0), new GeoPoint(1, 1)]);
        line.Attributes["highway"] = "residential";

        Assert.Empty(new HighwayConnectivityValidationRule().Validate(new ValidationContext([line])));
    }

    [Fact]
    public void HighwayConnectivity_HandlesThousandsOfSegmentsQuickly() {
        var random = new Random(42);
        var dataset = new OsmDataset();
        for (var index = 0; index < 2000; index++) {
            var start = new GeoPoint(random.NextDouble() * 0.05, random.NextDouble() * 0.05);
            var end = new GeoPoint(
                start.Longitude + (random.NextDouble() - 0.5) * 0.001,
                start.Latitude + (random.NextDouble() - 0.5) * 0.001);
            AddHighway(dataset, 1000 + index, "residential", start, end);
        }

        var stopwatch = Stopwatch.StartNew();
        Validate(new HighwayConnectivityValidationRule(), dataset);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"Validation took {stopwatch.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public void ValidationService_RegistersTopologyRules() {
        var rules = new ValidationService().Rules;

        Assert.Contains(rules, static rule => rule is DuplicateNodeValidationRule);
        Assert.Contains(rules, static rule => rule is UntaggedObjectValidationRule);
        Assert.Contains(rules, static rule => rule is HighwayConnectivityValidationRule);
    }

    private static List<ValidationIssue> Validate(IValidationRule rule, OsmDataset dataset) {
        return rule.Validate(new ValidationContext([], dataset)).ToList();
    }

    // Near the equator one metre is the same angular size along both axes.
    private static GeoPoint Meters(double east, double north) => new(east / MetersPerDegree, north / MetersPerDegree);

    private static MapFeature Feature(MapGeometryType type, List<GeoPoint> points) {
        return new MapFeature { GeometryType = type, Parts = [points] };
    }

    private static OsmNode Node(long id, double longitude, double latitude) {
        return new OsmNode { Id = id, Point = new GeoPoint(longitude, latitude) };
    }

    private static OsmWay AddHighway(OsmDataset dataset, long wayId, string highway, params GeoPoint[] points) {
        return AddWay(dataset, wayId, points, ("highway", highway));
    }

    private static OsmWay AddWay(OsmDataset dataset, long wayId, IReadOnlyList<GeoPoint> points, params (string Key, string Value)[] tags) {
        var nodeIds = new List<long>();
        foreach (var point in points) {
            var nodeId = dataset.Nodes.Count + 1;
            dataset.Nodes[nodeId] = new OsmNode { Id = nodeId, Point = point };
            nodeIds.Add(nodeId);
        }
        var way = new OsmWay {
            Id = wayId,
            NodeIds = nodeIds,
            Tags = tags.ToDictionary(static tag => tag.Key, static tag => tag.Value, StringComparer.Ordinal)
        };
        dataset.Ways[wayId] = way;
        return way;
    }
}
