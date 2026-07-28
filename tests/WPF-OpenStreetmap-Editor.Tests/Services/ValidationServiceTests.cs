using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class ValidationServiceTests {
    private readonly ValidationService _service = new();

    [Fact]
    public void ValidateFeatures_ReportsInvalidAndEmptyTagsWithNonMutatingFixSuggestions() {
        var feature = Feature(MapGeometryType.Point, [new GeoPoint(0, 0)]);
        feature.Attributes["bad=key"] = "value";
        feature.Attributes["name"] = " ";

        var issues = _service.ValidateFeatures([feature]);

        Assert.Contains(issues, issue =>
            issue.RuleId == "tag.invalid-key" &&
            issue.Severity == ValidationSeverity.Error &&
            issue.SuggestedFix == new ValidationFixSuggestion(ValidationFixKind.RemoveTag, "Remove the invalid tag.", "bad=key"));
        Assert.Contains(issues, issue =>
            issue.RuleId == "tag.empty-value" &&
            issue.SuggestedFix?.TagKey == "name");
        Assert.Equal("value", feature.Attributes["bad=key"]);
        Assert.Equal(" ", feature.Attributes["name"]);
    }

    [Fact]
    public void ValidateFeatures_ReportsInsufficientDuplicateUnclosedAndSelfIntersectingGeometry() {
        var shortLine = Feature(MapGeometryType.LineString, [new GeoPoint(0, 0)]);
        var duplicateLine = Feature(MapGeometryType.LineString, [
            new GeoPoint(0, 0), new GeoPoint(0, 0), new GeoPoint(1, 1)
        ]);
        var bowTie = Feature(MapGeometryType.Polygon, [
            new GeoPoint(0, 0), new GeoPoint(2, 2), new GeoPoint(0, 2), new GeoPoint(2, 0)
        ]);

        var issues = _service.ValidateFeatures([shortLine, duplicateLine, bowTie]);

        Assert.Contains(issues, issue => issue.RuleId == "geometry.insufficient-points" && issue.Target.Id == shortLine.Id);
        Assert.Contains(issues, issue =>
            issue.RuleId == "geometry.consecutive-duplicate-points" &&
            issue.Target.Id == duplicateLine.Id &&
            issue.SuggestedFix?.Kind == ValidationFixKind.RemoveConsecutiveDuplicatePoints);
        Assert.Contains(issues, issue =>
            issue.RuleId == "geometry.unclosed-polygon" &&
            issue.Target.Id == bowTie.Id &&
            issue.SuggestedFix?.Kind == ValidationFixKind.ClosePolygon);
        Assert.Contains(issues, issue => issue.RuleId == "geometry.self-intersection" && issue.Target.Id == bowTie.Id);
    }

    [Fact]
    public void ValidateDataset_ReportsAreaWayProblems() {
        var dataset = new OsmDataset();
        dataset.Nodes[1] = Node(1, 0, 0);
        dataset.Nodes[2] = Node(2, 2, 2);
        dataset.Nodes[3] = Node(3, 0, 2);
        dataset.Nodes[4] = Node(4, 2, 0);
        dataset.Ways[10] = new OsmWay {
            Id = 10,
            NodeIds = [1, 2, 3, 4],
            Tags = new Dictionary<string, string> { ["building"] = "yes" }
        };

        var issues = _service.ValidateDataset(dataset);

        Assert.Contains(issues, issue => issue.RuleId == "geometry.unclosed-polygon" && issue.Target.Id == "10");
        Assert.Contains(issues, issue => issue.RuleId == "geometry.self-intersection" && issue.Target.Id == "10");
    }

    [Fact]
    public void ValidateDataset_ReportsMissingInvalidAndDuplicateRelationMembers() {
        var dataset = new OsmDataset();
        dataset.Nodes[1] = Node(1, 0, 0);
        dataset.Relations[7] = new OsmRelation {
            Id = 7,
            Tags = new Dictionary<string, string> { ["type"] = "multipolygon" },
            Members = [
                new OsmRelationMember(OsmRelationMemberType.Node, 1, "outer"),
                new OsmRelationMember(OsmRelationMemberType.Way, 99, "outline"),
                new OsmRelationMember(OsmRelationMemberType.Way, 99, "outline")
            ]
        };

        var issues = _service.ValidateDataset(dataset);

        Assert.Contains(issues, issue => issue.RuleId == "relation.missing-member");
        Assert.Equal(3, issues.Count(issue => issue.RuleId == "relation.invalid-multipolygon-role"));
        Assert.Single(issues, issue => issue.RuleId == "relation.duplicate-member");
    }

    [Fact]
    public void Validate_OsmDatasetSuppressesDuplicateFeatureMirrorIssues() {
        var dataset = new OsmDataset();
        dataset.Nodes[42] = Node(42, 0, 0);
        dataset.Nodes[42].Tags["name"] = "";
        var feature = Feature(MapGeometryType.Point, [new GeoPoint(0, 0)]);
        feature.Osm = new OsmFeatureMetadata { PrimitiveType = OsmPrimitiveType.Node, Id = 42 };
        feature.Attributes["name"] = "";

        var issues = _service.Validate(new ValidationContext([feature], dataset));

        var issue = Assert.Single(issues, issue => issue.RuleId == "tag.empty-value");
        Assert.Equal(ValidationObjectType.Node, issue.Target.Type);
    }

    [Fact]
    public void Validate_UsesInjectedRules() {
        var service = new ValidationService([new StubRule()]);

        var issue = Assert.Single(service.ValidateFeatures([]));

        Assert.Equal("custom", issue.RuleId);
        Assert.Single(service.Rules);
    }

    private static MapFeature Feature(MapGeometryType type, List<GeoPoint> points) {
        return new MapFeature { GeometryType = type, Parts = [points] };
    }

    private static OsmNode Node(long id, double longitude, double latitude) {
        return new OsmNode { Id = id, Point = new GeoPoint(longitude, latitude) };
    }

    private sealed class StubRule : IValidationRule {
        public string Id => "custom";

        public IEnumerable<ValidationIssue> Validate(ValidationContext context) {
            yield return new ValidationIssue(
                Id,
                ValidationSeverity.Information,
                "Custom result.",
                new ValidationObjectReference(ValidationObjectType.Feature, "test"));
        }
    }
}
