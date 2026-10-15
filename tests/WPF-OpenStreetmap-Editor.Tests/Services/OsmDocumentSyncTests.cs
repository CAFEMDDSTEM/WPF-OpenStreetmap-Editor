using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class OsmDocumentSyncTests {
    private static readonly GeoPoint A = new(103.80, 1.30);
    private static readonly GeoPoint B = new(103.81, 1.31);
    private static readonly GeoPoint C = new(103.82, 1.32);
    private static readonly GeoPoint D = new(103.83, 1.33);
    private static readonly GeoPoint E = new(103.84, 1.34);

    [Fact]
    public void Synchronize_NewLineStartingOnExistingNodeReusesIt() {
        var document = CreateDocument(dataset => {
            dataset.Nodes[1] = Node(1, A);
            dataset.Nodes[2] = Node(2, B);
            dataset.Ways[10] = Way(10, 1, 2);
        });
        var line = NewLine(A, C);
        document.Features.Add(line);

        var synced = OsmDocumentSync.Synchronize(document);

        Assert.Equal(1, synced.Ways[line.Osm!.Id].NodeIds[0]);
        Assert.Equal(3, synced.Nodes.Count);
        Assert.Single(synced.Nodes.Values, node => node.Point == A);
    }

    [Fact]
    public void Synchronize_TwoNewLinesSharingAnEndpointShareOneNode() {
        var document = CreateDocument();
        var first = NewLine(A, B);
        var second = NewLine(B, C);
        document.Features.Add(first);
        document.Features.Add(second);

        var synced = OsmDocumentSync.Synchronize(document);

        Assert.Equal(synced.Ways[first.Osm!.Id].NodeIds[1], synced.Ways[second.Osm!.Id].NodeIds[0]);
        Assert.Equal(3, synced.Nodes.Count);
    }

    [Fact]
    public void Synchronize_InsertedVertexAndNewLineAtSamePointShareOneNode() {
        var document = CreateDocument(dataset => {
            dataset.Nodes[1] = Node(1, A);
            dataset.Nodes[3] = Node(3, C);
            dataset.Ways[10] = Way(10, 1, 3);
        });
        WayFeature(document, 10).Parts[0].Insert(1, B);
        var line = NewLine(B, D);
        document.Features.Insert(0, line);

        var synced = OsmDocumentSync.Synchronize(document);

        var insertedNodeId = synced.Ways[10].NodeIds[1];
        Assert.True(insertedNodeId < 0);
        Assert.Equal([1, insertedNodeId, 3], synced.Ways[10].NodeIds);
        Assert.Equal(insertedNodeId, synced.Ways[line.Osm!.Id].NodeIds[0]);
        Assert.Single(synced.Nodes.Values, node => node.Point == B);
    }

    [Fact]
    public void Synchronize_NewWayVertexOnTaggedPointFeatureUsesThePointNode() {
        var document = CreateDocument(dataset => dataset.Nodes[2] = TaggedNode(2, B, "amenity", "cafe"));
        document.Features.Add(OsmDocumentSync.CreateNodeFeature(document.Osm!.Nodes[2]));
        document.MarkClean();
        var line = NewLine(B, C);
        document.Features.Add(line);

        var synced = OsmDocumentSync.Synchronize(document);

        Assert.Equal(2, synced.Ways[line.Osm!.Id].NodeIds[0]);
        Assert.Equal("cafe", synced.Nodes[2].Tags["amenity"]);
        Assert.Equal(2, synced.Nodes.Count);
    }

    [Fact]
    public void Synchronize_TwoPointFeaturesAtSameCoordinateStayDistinctNodes() {
        var document = CreateDocument();
        var first = NewPoint(A, "amenity", "bench");
        var second = NewPoint(A, "amenity", "waste_basket");
        document.Features.Add(first);
        document.Features.Add(second);

        var synced = OsmDocumentSync.Synchronize(document);

        Assert.NotEqual(first.Osm!.Id, second.Osm!.Id);
        Assert.Equal(2, synced.Nodes.Count);
    }

    [Fact]
    public void Synchronize_NewWayThroughNewPointUsesThePointNodeRegardlessOfFeatureOrder() {
        var document = CreateDocument();
        var line = NewLine(A, B);
        var point = NewPoint(B, "barrier", "gate");
        document.Features.Add(line);
        document.Features.Add(point);

        var synced = OsmDocumentSync.Synchronize(document);

        Assert.Equal(point.Osm!.Id, synced.Ways[line.Osm!.Id].NodeIds[1]);
        Assert.Equal(2, synced.Nodes.Count);
    }

    [Fact]
    public void Synchronize_MovingSharedNodeFromBothWaysKeepsOneNodeAtNewLocation() {
        var document = CreateDocument(dataset => {
            dataset.Nodes[1] = Node(1, A);
            dataset.Nodes[2] = Node(2, B);
            dataset.Nodes[3] = Node(3, C);
            dataset.Ways[10] = Way(10, 1, 2);
            dataset.Ways[11] = Way(11, 2, 3);
        });
        var moved = new GeoPoint(B.Longitude + 0.001, B.Latitude);
        foreach (var feature in document.Features) {
            feature.Parts[0][feature.Parts[0].IndexOf(B)] = moved;
        }
        var line = NewLine(moved, D);
        document.Features.Add(line);

        var synced = OsmDocumentSync.Synchronize(document);

        Assert.Equal(moved, synced.Nodes[2].Point);
        Assert.Equal([1, 2], synced.Ways[10].NodeIds);
        Assert.Equal([2, 3], synced.Ways[11].NodeIds);
        Assert.Equal(2, synced.Ways[line.Osm!.Id].NodeIds[0]);
        Assert.Equal(4, synced.Nodes.Count);
    }

    [Fact]
    public void Synchronize_MovedNodeDoesNotMergeWithNodeAlreadyAtTarget() {
        var document = CreateDocument(dataset => {
            dataset.Nodes[1] = Node(1, A);
            dataset.Nodes[2] = Node(2, B);
            dataset.Nodes[3] = Node(3, C);
            dataset.Nodes[4] = Node(4, D);
            dataset.Ways[10] = Way(10, 1, 2);
            dataset.Ways[11] = Way(11, 3, 4);
        });
        WayFeature(document, 10).Parts[0][1] = C;

        var synced = OsmDocumentSync.Synchronize(document);

        Assert.Equal(C, synced.Nodes[2].Point);
        Assert.Equal(C, synced.Nodes[3].Point);
        Assert.Equal([1, 2], synced.Ways[10].NodeIds);
        Assert.Equal([3, 4], synced.Ways[11].NodeIds);
    }

    [Fact]
    public void Synchronize_LeavesUntouchedImportedDataUnchanged() {
        var document = CreateDocument(dataset => {
            dataset.Nodes[1] = Node(1, A, version: 2);
            dataset.Nodes[2] = Node(2, B, version: 5);
            dataset.Nodes[3] = Node(3, C);
            dataset.Nodes[4] = TaggedNode(4, D, "highway", "crossing");
            dataset.Ways[10] = new OsmWay { Id = 10, Version = 7, NodeIds = [1, 2, 4] };
            dataset.Ways[11] = Way(11, 2, 3);
        });

        var synced = OsmDocumentSync.Synchronize(document);

        Assert.Equal([1L, 2L, 3L, 4L], synced.Nodes.Keys.Order());
        Assert.Equal(2, synced.Nodes[1].Version);
        Assert.Equal(5, synced.Nodes[2].Version);
        Assert.Equal("crossing", synced.Nodes[4].Tags["highway"]);
        Assert.Equal(7, synced.Ways[10].Version);
        Assert.Equal([1, 2, 4], synced.Ways[10].NodeIds);
        Assert.Equal([2, 3], synced.Ways[11].NodeIds);
        Assert.Equal(-1, synced.NextNodeId);
        Assert.Equal(-1, synced.NextWayId);
    }

    [Fact]
    public void Synchronize_DeletingWayRemovesOnlyItsExclusiveUntaggedNodes() {
        var document = CreateDocument(dataset => {
            dataset.Nodes[1] = Node(1, A);
            dataset.Nodes[2] = TaggedNode(2, B, "highway", "crossing");
            dataset.Nodes[3] = Node(3, C);
            dataset.Nodes[4] = Node(4, D);
            dataset.Nodes[5] = Node(5, E);
            dataset.Ways[10] = Way(10, 1, 2, 3, 4);
            dataset.Ways[11] = Way(11, 4, 5);
        });
        document.Features.Add(OsmDocumentSync.CreateNodeFeature(document.Osm!.Nodes[3]));
        document.MarkClean();
        document.Features.Remove(WayFeature(document, 10));

        var synced = OsmDocumentSync.Synchronize(document);
        var build = OsmChangeSerializer.Build(document, 1);

        Assert.False(synced.Ways.ContainsKey(10));
        Assert.Equal([2L, 3L, 4L, 5L], synced.Nodes.Keys.Order());
        Assert.Equal(2, build.DeleteCount);
        Assert.Equal(0, build.CreateCount + build.ModifyCount);
    }

    [Fact]
    public void Synchronize_KeepsNodesThatWereAlreadyUnreferenced() {
        var document = CreateDocument(dataset => {
            dataset.Nodes[1] = Node(1, A);
            dataset.Nodes[2] = Node(2, B);
            dataset.Nodes[99] = Node(99, E);
            dataset.Ways[10] = Way(10, 1, 2);
        });
        document.Features.Remove(WayFeature(document, 10));

        var synced = OsmDocumentSync.Synchronize(document);

        Assert.Equal([99L], synced.Nodes.Keys);
    }

    [Fact]
    public void Synchronize_DeletingRelationKeepsItsUntaggedMemberNodes() {
        // Like JOSM, deleting a relation never deletes member nodes, even untagged node-only members.
        var document = CreateDocument(dataset => {
            dataset.Nodes[1] = Node(1, A);
            dataset.Nodes[2] = Node(2, B);
            dataset.Nodes[7] = Node(7, E);
            dataset.Ways[10] = Way(10, 1, 2);
            dataset.Relations[20] = new OsmRelation {
                Id = 20,
                Members = [new OsmRelationMember(OsmRelationMemberType.Way, 10, "outer"), new OsmRelationMember(OsmRelationMemberType.Node, 7, "label")],
                Tags = new Dictionary<string, string> { ["type"] = "multipolygon" }
            };
        });
        var relationFeature = new MapFeature {
            GeometryType = MapGeometryType.Polygon,
            Osm = new OsmFeatureMetadata { PrimitiveType = OsmPrimitiveType.Relation, Id = 20, Version = 1 }
        };
        document.Features.Add(relationFeature);
        document.MarkClean();
        document.Features.Remove(relationFeature);

        var synced = OsmDocumentSync.Synchronize(document);

        Assert.False(synced.Relations.ContainsKey(20));
        Assert.Equal([1L, 2L, 7L], synced.Nodes.Keys.Order());
    }

    [Fact]
    public void Synchronize_RemovingMiddleVertexDeletesItsUntaggedNode() {
        var document = CreateDocument(dataset => {
            dataset.Nodes[1] = Node(1, A);
            dataset.Nodes[2] = Node(2, B);
            dataset.Nodes[3] = Node(3, C);
            dataset.Ways[10] = Way(10, 1, 2, 3);
        });
        WayFeature(document, 10).Parts[0].RemoveAt(1);

        var synced = OsmDocumentSync.Synchronize(document);

        Assert.Equal([1, 3], synced.Ways[10].NodeIds);
        Assert.Equal([1L, 3L], synced.Nodes.Keys.Order());
    }

    [Fact]
    public void Synchronize_RemovesNodeDetachedInDatasetSinceBaseline() {
        // Topology commands shrink way.NodeIds in the dataset before the sync runs, so the node is
        // already unreferenced when Synchronize starts and only the baseline shows it was in use.
        var document = CreateDocument(dataset => {
            dataset.Nodes[1] = Node(1, A);
            dataset.Nodes[2] = Node(2, B);
            dataset.Nodes[3] = Node(3, C);
            dataset.Ways[10] = Way(10, 1, 2, 3);
        });
        var feature = WayFeature(document, 10);
        var edited = document.Osm!.Clone();
        edited.Ways[10].NodeIds = [1, 3];
        document.Osm = edited;
        feature.Parts[0].RemoveAt(1);
        feature.Osm!.NodeReferences.RemoveAt(1);

        var synced = OsmDocumentSync.Synchronize(document);

        Assert.Equal([1, 3], synced.Ways[10].NodeIds);
        Assert.Equal([1L, 3L], synced.Nodes.Keys.Order());
    }

    [Fact]
    public void Synchronize_NewPointOnVertexInsertedIntoExistingWaySharesOneNode() {
        // "Join node to way": the POI is a new feature while the way already exists in the dataset.
        var joinPoint = new GeoPoint(103.805, 1.305);
        var document = CreateDocument(dataset => {
            dataset.Nodes[1] = Node(1, A);
            dataset.Nodes[2] = Node(2, B);
            dataset.Ways[10] = Way(10, 1, 2);
        });
        WayFeature(document, 10).Parts[0].Insert(1, joinPoint);
        var poi = NewPoint(joinPoint, "amenity", "bench");
        document.Features.Add(poi);

        var synced = OsmDocumentSync.Synchronize(document);

        Assert.Equal(3, synced.Nodes.Count);
        Assert.Equal(poi.Osm!.Id, synced.Ways[10].NodeIds[1]);
        Assert.Equal("bench", synced.Nodes[poi.Osm.Id].Tags["amenity"]);
    }

    [Fact]
    public void Synchronize_MovedVertexAfterInsertKeepsItsNode() {
        var document = CreateDocument(dataset => {
            dataset.Nodes[1] = Node(1, A);
            dataset.Nodes[2] = Node(2, B);
            dataset.Nodes[3] = Node(3, C);
            dataset.Nodes[4] = Node(4, D);
            dataset.Ways[10] = Way(10, 1, 2, 3, 4);
        });
        var way = WayFeature(document, 10);
        way.Parts[0].Insert(1, new GeoPoint(103.805, 1.305));
        var movedC = new GeoPoint(103.8205, 1.3205);
        way.Parts[0][3] = movedC;

        var synced = OsmDocumentSync.Synchronize(document);
        var build = OsmChangeSerializer.Build(document, 1);

        Assert.Equal(5, synced.Ways[10].NodeIds.Count);
        Assert.Equal(3, synced.Ways[10].NodeIds[3]);
        Assert.Equal(movedC, synced.Nodes[3].Point);
        Assert.Equal(1, build.CreateCount);
        Assert.Equal(0, build.DeleteCount);
    }

    [Fact]
    public void Synchronize_MoveNextToInsertCreatesNewNodeButKeepsGeometry() {
        // Insert and move in the same gap are ambiguous, so the matcher deliberately does not pair
        // them; the result is a fresh node (and the old one pruned) rather than a guessed identity.
        var document = CreateDocument(dataset => {
            dataset.Nodes[1] = Node(1, A);
            dataset.Nodes[2] = Node(2, B);
            dataset.Nodes[3] = Node(3, C);
            dataset.Ways[10] = Way(10, 1, 2, 3);
        });
        var way = WayFeature(document, 10);
        var inserted = new GeoPoint(103.805, 1.305);
        var movedB = new GeoPoint(103.8105, 1.3105);
        way.Parts[0].Insert(1, inserted);
        way.Parts[0][2] = movedB;

        var synced = OsmDocumentSync.Synchronize(document);

        Assert.Equal([A, inserted, movedB, C], synced.Ways[10].NodeIds.Select(id => synced.Nodes[id].Point));
        Assert.False(synced.Nodes.ContainsKey(2));
    }

    [Fact]
    public void Synchronize_DraggedJunctionAfterInsertStaysSharedRegardlessOfOrder() {
        // A gained a vertex (coordinate-only matching) and the junction it shares with B was dragged.
        var movedJunction = new GeoPoint(103.8205, 1.3205);
        var document = CreateDocument(dataset => {
            dataset.Nodes[1] = Node(1, A);
            dataset.Nodes[2] = Node(2, B);
            dataset.Nodes[3] = Node(3, C);
            dataset.Nodes[4] = Node(4, D);
            dataset.Ways[10] = Way(10, 1, 2, 3);
            dataset.Ways[11] = Way(11, 3, 4);
        });
        var wayA = WayFeature(document, 10);
        wayA.Parts[0].Insert(1, new GeoPoint(103.805, 1.305));
        wayA.Parts[0][3] = movedJunction;
        WayFeature(document, 11).Parts[0][0] = movedJunction;

        var synced = OsmDocumentSync.Synchronize(document);

        Assert.Equal(5, synced.Nodes.Count);
        Assert.Equal(synced.Ways[11].NodeIds[0], synced.Ways[10].NodeIds[3]);
        Assert.Equal(3, synced.Ways[11].NodeIds[0]);
        Assert.Equal(movedJunction, synced.Nodes[3].Point);
    }

    [Fact]
    public void Synchronize_RestoringDeletedWayRecreatesItsNodesWithoutChanges() {
        var document = CreateDocument(dataset => {
            dataset.Nodes[1] = Node(1, A, version: 2);
            dataset.Nodes[2] = Node(2, B, version: 3);
            dataset.Ways[10] = new OsmWay {
                Id = 10,
                Version = 4,
                NodeIds = [1, 2],
                Tags = new Dictionary<string, string> { ["highway"] = "service" }
            };
        });
        var feature = WayFeature(document, 10);
        document.Features.Remove(feature);
        var afterDelete = OsmDocumentSync.Synchronize(document);
        Assert.Empty(afterDelete.Nodes);
        Assert.Empty(afterDelete.Ways);

        document.Features.Add(feature);
        var restored = OsmDocumentSync.Synchronize(document);

        Assert.Equal([1, 2], restored.Ways[10].NodeIds);
        Assert.Equal(4, restored.Ways[10].Version);
        Assert.Equal(2, restored.Nodes[1].Version);
        Assert.Equal(A, restored.Nodes[1].Point);
        Assert.Equal(3, restored.Nodes[2].Version);
        Assert.Equal(B, restored.Nodes[2].Point);
        Assert.Equal(0, OsmChangeSerializer.Build(document, 1).TotalCount);
    }

    private static MapDocument CreateDocument(Action<OsmDataset>? configure = null) {
        var dataset = new OsmDataset();
        configure?.Invoke(dataset);
        var document = new MapDocument { Osm = dataset };
        foreach (var way in dataset.Ways.Values) {
            document.Features.Add(OsmDocumentSync.CreateWayFeature(dataset, way)!);
        }
        document.MarkClean();
        return document;
    }

    private static MapFeature WayFeature(MapDocument document, long wayId) {
        return document.Features.Single(feature =>
            feature.Osm?.PrimitiveType == OsmPrimitiveType.Way && feature.Osm.Id == wayId);
    }

    private static MapFeature NewLine(params GeoPoint[] points) {
        return new MapFeature { GeometryType = MapGeometryType.LineString, Parts = [points.ToList()] };
    }

    private static MapFeature NewPoint(GeoPoint point, string key, string value) {
        return new MapFeature {
            GeometryType = MapGeometryType.Point,
            Parts = [[point]],
            Attributes = new Dictionary<string, string> { [key] = value }
        };
    }

    private static OsmNode Node(long id, GeoPoint point, int version = 1) {
        return new OsmNode { Id = id, Version = version, Point = point };
    }

    private static OsmNode TaggedNode(long id, GeoPoint point, string key, string value) {
        var node = Node(id, point);
        node.Tags[key] = value;
        return node;
    }

    private static OsmWay Way(long id, params long[] nodeIds) {
        return new OsmWay { Id = id, NodeIds = nodeIds.ToList() };
    }
}
