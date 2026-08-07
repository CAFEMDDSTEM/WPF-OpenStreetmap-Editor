using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class RelationEditCommandsTests {
    [Fact]
    public void SetRelationMembers_UpdatesMembersAndGeometryAndIsUndoable() {
        var document = CreateMultipolygonDocument();
        var feature = document.Features.Single(static feature => feature.Osm?.PrimitiveType == OsmPrimitiveType.Relation);
        document.Osm!.Ways[12] = new OsmWay { Id = 12, NodeIds = [5, 6, 7, 8, 5] };
        var stack = new EditCommandStack(new MapEditDataset(document));

        var newMembers = new List<OsmRelationMember> {
            new(OsmRelationMemberType.Way, 10, "outer"),
            new(OsmRelationMemberType.Way, 12, "outer")
        };

        Assert.True(stack.Execute(new SetRelationMembersCommand(feature, newMembers)));
        Assert.Equal(2, document.Osm!.Relations[200].Members.Count);
        Assert.Equal([OsmRelationMemberType.Way, OsmRelationMemberType.Way],
            document.Osm!.Relations[200].Members.Select(static member => member.Type));
        Assert.Equal(2, feature.Parts.Count);

        Assert.True(stack.Undo());
        Assert.Equal(2, document.Osm!.Relations[200].Members.Count);
        Assert.Equal(OsmRelationMemberType.Way, document.Osm!.Relations[200].Members[1].Type);
        Assert.Equal(11, document.Osm!.Relations[200].Members[1].Id);
        Assert.Equal(1, feature.Parts.Count);
    }

    [Fact]
    public void SetRelationMembers_NoopWhenMembersUnchanged() {
        var document = CreateMultipolygonDocument();
        var feature = document.Features.Single(static feature => feature.Osm?.PrimitiveType == OsmPrimitiveType.Relation);
        var members = document.Osm!.Relations[200].Members;
        var stack = new EditCommandStack(new MapEditDataset(document));

        Assert.False(stack.Execute(new SetRelationMembersCommand(feature, members)));
    }

    [Fact]
    public void CreateRelation_CreatesDatasetRelationAndRenderableFeature() {
        var document = CreateDocumentWithWays();
        var stack = new EditCommandStack(new MapEditDataset(document));

        var members = new List<OsmRelationMember> {
            new(OsmRelationMemberType.Way, 10, "outer"),
            new(OsmRelationMemberType.Way, 12, "outer")
        };
        var command = new CreateRelationCommand(
            members,
            new Dictionary<string, string> { ["type"] = "multipolygon" });

        Assert.True(stack.Execute(command));
        var relation = Assert.Single(document.Osm!.Relations.Values);
        Assert.Equal(2, relation.Members.Count);
        Assert.NotNull(command.CreatedFeature);
        Assert.Contains(command.CreatedFeature, document.Features);
        Assert.Equal(2, command.CreatedFeature!.Parts.Count);

        Assert.True(stack.Undo());
        Assert.Empty(document.Osm!.Relations);
        Assert.DoesNotContain(command.CreatedFeature, document.Features);
    }

    [Fact]
    public void CreateRelation_NonRenderableRelationHasNoFeature() {
        var document = CreateDocumentWithWays();
        var stack = new EditCommandStack(new MapEditDataset(document));

        var command = new CreateRelationCommand(
            [new OsmRelationMember(OsmRelationMemberType.Node, 1, string.Empty)],
            new Dictionary<string, string> { ["type"] = "route" });

        Assert.True(stack.Execute(command));
        Assert.NotNull(Assert.Single(document.Osm!.Relations.Values));
        Assert.Null(command.CreatedFeature);
    }

    [Fact]
    public void CreateRelation_FailsWithoutDataset() {
        var document = new MapDocument();
        var stack = new EditCommandStack(new MapEditDataset(document));

        Assert.False(stack.Execute(new CreateRelationCommand(
            [new OsmRelationMember(OsmRelationMemberType.Way, 10, "outer")],
            new Dictionary<string, string>())));
    }

    private static MapDocument CreateMultipolygonDocument() {
        var document = CreateDocumentWithWays();
        document.Osm!.Ways[11] = new OsmWay { Id = 11, NodeIds = [5, 6, 7, 8, 5] };
        document.Osm.Relations[200] = new OsmRelation {
            Id = 200,
            Version = 1,
            Members = [
                new OsmRelationMember(OsmRelationMemberType.Way, 10, "outer"),
                new OsmRelationMember(OsmRelationMemberType.Way, 11, "inner")
            ],
            Tags = new Dictionary<string, string> { ["type"] = "multipolygon" }
        };
        var relationFeature = OsmDocumentSync.CreateRelationFeature(document.Osm, document.Osm.Relations[200])!;
        document.Features.Add(relationFeature);
        return document;
    }

    private static MapDocument CreateDocumentWithWays() {
        var document = new MapDocument();
        var dataset = new OsmDataset();
        document.Osm = dataset;

        dataset.Nodes[1] = new OsmNode { Id = 1, Point = new GeoPoint(0, 0) };
        dataset.Nodes[2] = new OsmNode { Id = 2, Point = new GeoPoint(1, 0) };
        dataset.Nodes[3] = new OsmNode { Id = 3, Point = new GeoPoint(1, 1) };
        dataset.Nodes[4] = new OsmNode { Id = 4, Point = new GeoPoint(0, 1) };
        dataset.Ways[10] = new OsmWay { Id = 10, NodeIds = [1, 2, 3, 4, 1] };

        dataset.Nodes[5] = new OsmNode { Id = 5, Point = new GeoPoint(0.2, 0.2) };
        dataset.Nodes[6] = new OsmNode { Id = 6, Point = new GeoPoint(0.4, 0.2) };
        dataset.Nodes[7] = new OsmNode { Id = 7, Point = new GeoPoint(0.4, 0.4) };
        dataset.Nodes[8] = new OsmNode { Id = 8, Point = new GeoPoint(0.2, 0.4) };
        dataset.Ways[12] = new OsmWay { Id = 12, NodeIds = [5, 6, 7, 8, 5] };

        foreach (var way in dataset.Ways.Values) {
            var feature = OsmDocumentSync.CreateWayFeature(dataset, way)!;
            document.Features.Add(feature);
        }

        return document;
    }
}
