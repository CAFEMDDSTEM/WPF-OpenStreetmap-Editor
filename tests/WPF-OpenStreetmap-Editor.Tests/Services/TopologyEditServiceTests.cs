using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class TopologyEditServiceTests {
    [Fact]
    public void ReverseLine_UpdatesFeatureAndOsmWayAndIsUndoable() {
        var context = CreateOsmLine([Point(0), Point(1), Point(2)], [10, 11, 12]);
        var stack = new EditCommandStack(context.Dataset);

        var result = TopologyEditService.CreateReverseLineCommand(context.Dataset, context.Feature);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(stack.Execute(result.Command!));
        Assert.Equal([Point(2), Point(1), Point(0)], context.Feature.Parts[0]);
        Assert.Equal([12L, 11L, 10L], context.Feature.Osm!.NodeReferences.Select(static node => node.Id));
        Assert.Equal([12L, 11L, 10L], context.Layer.Osm!.Ways[100].NodeIds);

        Assert.True(stack.Undo());
        Assert.Equal([Point(0), Point(1), Point(2)], context.Feature.Parts[0]);
        Assert.Equal([10L, 11L, 12L], context.Layer.Osm!.Ways[100].NodeIds);
    }

    [Fact]
    public void SimplifyLine_UsesMeterToleranceAndRestoresGeometryOnUndo() {
        var feature = CreateFeature(MapGeometryType.LineString,
            [new GeoPoint(0, 0), new GeoPoint(0.001, 0.000001), new GeoPoint(0.002, 0)]);
        var dataset = CreateDataset(feature);
        var stack = new EditCommandStack(dataset);

        var result = TopologyEditService.CreateSimplifyCommand(dataset, feature, toleranceMeters: 1);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(stack.Execute(result.Command!));
        Assert.Equal([new GeoPoint(0, 0), new GeoPoint(0.002, 0)], feature.Parts[0]);
        Assert.True(stack.Undo());
        Assert.Equal(3, feature.Parts[0].Count);
    }

    [Fact]
    public void SimplifyPolygon_PreservesClosureAndMinimumRing() {
        var feature = CreateFeature(MapGeometryType.Polygon, [
            new GeoPoint(0, 0),
            new GeoPoint(0.001, 0),
            new GeoPoint(0.002, 0),
            new GeoPoint(0.002, 0.002),
            new GeoPoint(0, 0.002),
            new GeoPoint(0, 0)
        ]);
        var dataset = CreateDataset(feature);

        var result = TopologyEditService.CreateSimplifyCommand(dataset, feature, toleranceMeters: 1);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(new EditCommandStack(dataset).Execute(result.Command!));
        Assert.Equal(feature.Parts[0][0], feature.Parts[0][^1]);
        Assert.True(feature.Parts[0].Count >= 4);
        Assert.Equal(5, feature.Parts[0].Count);
    }

    [Fact]
    public void SimplifyOsmWay_RejectsRemovingTaggedNode() {
        var context = CreateOsmLine(
            [new GeoPoint(0, 0), new GeoPoint(0.001, 0.000001), new GeoPoint(0.002, 0)],
            [10, 11, 12]);
        context.Layer.Osm!.Nodes[11].Tags["barrier"] = "gate";

        var result = TopologyEditService.CreateSimplifyCommand(context.Dataset, context.Feature, toleranceMeters: 1);

        Assert.False(result.IsSuccess);
        Assert.Contains("tagged node", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void SplitLine_UpdatesOsmWayAndRelationAndIsUndoable() {
        var context = CreateOsmLine([Point(0), Point(1), Point(2)], [10, 11, 12]);
        context.Layer.Osm!.Ways[-1] = new OsmWay { Id = -1, NodeIds = [10, 12] };
        context.Layer.Osm!.Relations[200] = new OsmRelation {
            Id = 200,
            Members = [new OsmRelationMember(OsmRelationMemberType.Way, 100, "forward")]
        };
        var stack = new EditCommandStack(context.Dataset);

        var result = TopologyEditService.CreateSplitLineCommand(context.Dataset, context.Feature, 1);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(stack.Execute(result.Command!));
        Assert.Equal(2, context.Layer.Features.Count);
        var second = context.Layer.Features[1];
        Assert.Equal([Point(0), Point(1)], context.Feature.Parts[0]);
        Assert.Equal([Point(1), Point(2)], second.Parts[0]);
        Assert.Equal([10L, 11L], context.Layer.Osm.Ways[100].NodeIds);
        Assert.Equal(2, context.Layer.Osm.Relations[200].Members.Count);
        Assert.Equal(second.Osm!.Id, context.Layer.Osm.Relations[200].Members[1].Id);
        Assert.Equal(-2, second.Osm.Id);
        Assert.True(context.Layer.Osm.Ways.ContainsKey(-1));

        Assert.True(stack.Undo());
        Assert.Single(context.Layer.Features);
        Assert.Equal([10L, 11L, 12L], context.Layer.Osm!.Ways[100].NodeIds);
        Assert.Single(context.Layer.Osm.Relations[200].Members);
    }

    [Fact]
    public void CombineLines_RemovesSecondOsmWayAndUndoRestoresIt() {
        var firstContext = CreateOsmLine([Point(0), Point(1)], [10, 11]);
        var second = CreateFeature(MapGeometryType.LineString, [Point(1), Point(2)]);
        second.Attributes["highway"] = "service";
        second.Osm = new OsmFeatureMetadata {
            PrimitiveType = OsmPrimitiveType.Way,
            Id = 101,
            Version = 3,
            NodeReferences = [new OsmNodeReference(11, 1, Point(1)), new OsmNodeReference(12, 1, Point(2))]
        };
        firstContext.Feature.Attributes["highway"] = "service";
        firstContext.Layer.Features.Add(second);
        firstContext.Layer.Osm!.Nodes[12] = new OsmNode { Id = 12, Point = Point(2) };
        firstContext.Layer.Osm.Ways[100].Tags["highway"] = "service";
        firstContext.Layer.Osm.Ways[101] = new OsmWay {
            Id = 101,
            Version = 3,
            NodeIds = [11, 12],
            Tags = new Dictionary<string, string> { ["highway"] = "service" }
        };
        var stack = new EditCommandStack(firstContext.Dataset);

        var result = TopologyEditService.CreateCombineLinesCommand(firstContext.Dataset, firstContext.Feature, second);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(stack.Execute(result.Command!));
        Assert.Single(firstContext.Layer.Features);
        Assert.Equal([Point(0), Point(1), Point(2)], firstContext.Feature.Parts[0]);
        Assert.Equal([10L, 11L, 12L], firstContext.Layer.Osm.Ways[100].NodeIds);
        Assert.False(firstContext.Layer.Osm.Ways.ContainsKey(101));

        Assert.True(stack.Undo());
        Assert.Equal(2, firstContext.Layer.Features.Count);
        Assert.True(firstContext.Layer.Osm!.Ways.ContainsKey(101));
    }

    [Fact]
    public void CombineLines_RejectsWaysUsedByRelations() {
        var context = CreateOsmLine([Point(0), Point(1)], [10, 11]);
        var second = CreateFeature(MapGeometryType.LineString, [Point(1), Point(2)]);
        second.Osm = new OsmFeatureMetadata {
            PrimitiveType = OsmPrimitiveType.Way,
            Id = 101,
            NodeReferences = [new OsmNodeReference(11, 1, Point(1)), new OsmNodeReference(12, 1, Point(2))]
        };
        context.Layer.Features.Add(second);
        context.Layer.Osm!.Nodes[12] = new OsmNode { Id = 12, Point = Point(2) };
        context.Layer.Osm.Ways[101] = new OsmWay { Id = 101, NodeIds = [11, 12] };
        context.Layer.Osm.Relations[200] = new OsmRelation {
            Id = 200,
            Members = [new OsmRelationMember(OsmRelationMemberType.Way, 100, string.Empty)]
        };

        var result = TopologyEditService.CreateCombineLinesCommand(context.Dataset, context.Feature, second);

        Assert.False(result.IsSuccess);
        Assert.Contains("relations", result.Error, StringComparison.Ordinal);
    }

    private static OsmLineContext CreateOsmLine(IReadOnlyList<GeoPoint> points, IReadOnlyList<long> nodeIds) {
        var feature = CreateFeature(MapGeometryType.LineString, points);
        feature.Osm = new OsmFeatureMetadata {
            PrimitiveType = OsmPrimitiveType.Way,
            Id = 100,
            Version = 3,
            NodeReferences = nodeIds.Zip(points, static (id, point) => new OsmNodeReference(id, 1, point)).ToList()
        };
        var dataset = CreateDataset(feature);
        var layer = dataset.Document!.ActiveDataLayer;
        layer.Osm = new OsmDataset();
        foreach (var (nodeId, point) in nodeIds.Zip(points)) {
            layer.Osm.Nodes[nodeId] = new OsmNode { Id = nodeId, Point = point };
        }
        layer.Osm.Ways[100] = new OsmWay { Id = 100, Version = 3, NodeIds = nodeIds.ToList() };
        return new OsmLineContext(dataset, layer, feature);
    }

    private static MapEditDataset CreateDataset(MapFeature feature) {
        var document = new MapDocument();
        document.Features.Add(feature);
        return new MapEditDataset(document);
    }

    private static MapFeature CreateFeature(MapGeometryType geometryType, IReadOnlyList<GeoPoint> points) {
        return new MapFeature { GeometryType = geometryType, Parts = [points.ToList()] };
    }

    private static GeoPoint Point(double longitude) => new(longitude, 0);

    private sealed record OsmLineContext(MapEditDataset Dataset, MapDataLayer Layer, MapFeature Feature);
}
