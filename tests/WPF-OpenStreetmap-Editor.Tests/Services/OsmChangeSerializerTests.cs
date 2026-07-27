using System.Xml.Linq;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class OsmChangeSerializerTests {
    [Fact]
    public void Build_SeparatesCreatedModifiedAndDeletedFeatures() {
        var document = new MapDocument();
        var modified = new MapFeature {
            Id = "osm-node-1",
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(1, 1)]],
            Osm = new OsmFeatureMetadata { PrimitiveType = OsmPrimitiveType.Node, Id = 1, Version = 3 }
        };
        var deleted = new MapFeature {
            Id = "osm-way-2",
            GeometryType = MapGeometryType.LineString,
            Parts = [[new GeoPoint(1, 1), new GeoPoint(2, 2)]],
            Osm = new OsmFeatureMetadata { PrimitiveType = OsmPrimitiveType.Way, Id = 2, Version = 4 }
        };
        document.Features.Add(modified);
        document.Features.Add(deleted);
        document.MarkClean();
        modified.Parts[0][0] = new GeoPoint(1.1, 1.1);
        document.Features.Remove(deleted);
        document.Features.Add(new MapFeature {
            Id = "new-way",
            GeometryType = MapGeometryType.LineString,
            Parts = [[new GeoPoint(3, 3), new GeoPoint(4, 4)]],
            Attributes = new Dictionary<string, string> { ["highway"] = "service" }
        });

        var result = OsmChangeSerializer.Build(document, 99);
        var xml = XDocument.Parse(result.Xml);

        Assert.Equal(3, result.CreateCount);
        Assert.Equal(1, result.ModifyCount);
        Assert.Equal(1, result.DeleteCount);
        Assert.Contains(xml.Descendants("node"), element => element.Attribute("id")?.Value == "1");
        Assert.Contains(xml.Descendants("way"), element => element.Attribute("id")?.Value == "2");
        Assert.All(xml.Descendants().Where(element => element.Name.LocalName is "node" or "way"),
            element => Assert.Equal("99", element.Attribute("changeset")?.Value));
    }

    [Fact]
    public void ApplyDiffResult_AssignsCreatedOsmIdentityAndMarksClean() {
        var document = new MapDocument();
        var feature = new MapFeature {
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(1, 1)]]
        };
        document.Features.Add(feature);
        var build = OsmChangeSerializer.Build(document, 10);
        var reference = Assert.Single(build.References);

        OsmChangeSerializer.ApplyDiffResult(
            document,
            build,
            $"<diffResult><node old_id='{reference.OldId}' new_id='123' new_version='1'/></diffResult>");

        Assert.Equal(123, feature.Osm!.Id);
        Assert.Equal(1, feature.Osm.Version);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void Build_DoesNotMutateDocumentWhenPreviewCreatesOsmIdentity() {
        var document = new MapDocument();
        var feature = new MapFeature {
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(1, 1)]]
        };
        document.Features.Add(feature);

        var result = OsmChangeSerializer.Build(document, 10);

        Assert.Equal(1, result.CreateCount);
        Assert.Null(feature.Osm);
        Assert.Null(document.Osm);
    }

    [Fact]
    public void Build_PositiveOsmIdWithoutOriginalDatasetWritesModify() {
        var document = new MapDocument();
        document.Features.Add(new MapFeature {
            Id = "manual-osm-node",
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(1, 1)]],
            Osm = new OsmFeatureMetadata {
                PrimitiveType = OsmPrimitiveType.Node,
                Id = 123,
                Version = 7
            }
        });

        var result = OsmChangeSerializer.Build(document, 99);
        var xml = XDocument.Parse(result.Xml);

        Assert.Equal(0, result.CreateCount);
        Assert.Equal(1, result.ModifyCount);
        var node = Assert.Single(xml.Root!.Element("modify")!.Elements("node"));
        Assert.Equal("123", node.Attribute("id")!.Value);
        Assert.Equal("7", node.Attribute("version")!.Value);
    }

    [Fact]
    public void Build_ModifiedWayWithInsertedPointReusesUnchangedOriginalNodes() {
        var first = new GeoPoint(1, 1);
        var inserted = new GeoPoint(1.5, 1.5);
        var second = new GeoPoint(2, 2);
        var third = new GeoPoint(3, 3);
        var document = new MapDocument();
        var way = new MapFeature {
            Id = "osm-way-10",
            GeometryType = MapGeometryType.LineString,
            Parts = [[first, second, third]],
            Osm = new OsmFeatureMetadata {
                PrimitiveType = OsmPrimitiveType.Way,
                Id = 10,
                Version = 4,
                NodeReferences = [
                    new OsmNodeReference(1, 1, first),
                    new OsmNodeReference(2, 1, second),
                    new OsmNodeReference(3, 1, third)
                ]
            }
        };
        document.Features.Add(way);
        document.MarkClean();
        way.Parts[0].Insert(1, inserted);

        var result = OsmChangeSerializer.Build(document, 99);
        var xml = XDocument.Parse(result.Xml);

        var createdNode = Assert.Single(xml.Root!.Element("create")!.Elements("node"));
        var createdNodeId = createdNode.Attribute("id")!.Value;
        var wayNodeIds = xml.Root.Element("modify")!.Element("way")!.Elements("nd")
            .Select(element => element.Attribute("ref")!.Value)
            .ToArray();
        Assert.StartsWith("-", createdNodeId);
        Assert.Equal(["1", createdNodeId, "2", "3"], wayNodeIds);
    }

    [Fact]
    public void Build_ModifiedWayWithDeletedPointReusesRemainingOriginalNodes() {
        var first = new GeoPoint(1, 1);
        var second = new GeoPoint(2, 2);
        var third = new GeoPoint(3, 3);
        var document = new MapDocument();
        var way = new MapFeature {
            Id = "osm-way-10",
            GeometryType = MapGeometryType.LineString,
            Parts = [[first, second, third]],
            Osm = new OsmFeatureMetadata {
                PrimitiveType = OsmPrimitiveType.Way,
                Id = 10,
                Version = 4,
                NodeReferences = [
                    new OsmNodeReference(1, 1, first),
                    new OsmNodeReference(2, 1, second),
                    new OsmNodeReference(3, 1, third)
                ]
            }
        };
        document.Features.Add(way);
        document.MarkClean();
        way.Parts[0].RemoveAt(1);

        var result = OsmChangeSerializer.Build(document, 99);
        var xml = XDocument.Parse(result.Xml);

        Assert.Null(xml.Root!.Element("create"));
        var wayNodeIds = xml.Root.Element("modify")!.Element("way")!.Elements("nd")
            .Select(element => element.Attribute("ref")!.Value)
            .ToArray();
        Assert.Equal(["1", "3"], wayNodeIds);
    }

    [Fact]
    public void Build_ReversedWayReordersNodeReferencesWithoutMovingNodes() {
        var first = new GeoPoint(1, 1);
        var second = new GeoPoint(2, 2);
        var third = new GeoPoint(3, 3);
        var document = new MapDocument {
            Osm = new OsmDataset()
        };
        document.Osm.Nodes[1] = new OsmNode { Id = 1, Version = 1, Point = first };
        document.Osm.Nodes[2] = new OsmNode { Id = 2, Version = 1, Point = second };
        document.Osm.Nodes[3] = new OsmNode { Id = 3, Version = 1, Point = third };
        document.Osm.Ways[10] = new OsmWay { Id = 10, Version = 4, NodeIds = [1, 2, 3] };
        var way = OsmDocumentSync.CreateWayFeature(document.Osm, document.Osm.Ways[10])!;
        document.Features.Add(way);
        document.MarkClean();
        way.Parts[0].Reverse();

        var result = OsmChangeSerializer.Build(document, 99);
        var xml = XDocument.Parse(result.Xml);

        Assert.Equal([3, 2, 1], result.Dataset.Ways[10].NodeIds);
        Assert.Equal(first, result.Dataset.Nodes[1].Point);
        Assert.Equal(second, result.Dataset.Nodes[2].Point);
        Assert.Equal(third, result.Dataset.Nodes[3].Point);
        Assert.Equal(1, result.ModifyCount);
        Assert.Empty(xml.Root!.Element("modify")!.Elements("node"));
        var wayNodeIds = xml.Root.Element("modify")!.Element("way")!.Elements("nd")
            .Select(element => element.Attribute("ref")!.Value)
            .ToArray();
        Assert.Equal(["3", "2", "1"], wayNodeIds);
    }

    [Fact]
    public void Build_CompactOsmBaselineStillDetectsDeletedWay() {
        var document = new MapDocument {
            Osm = new OsmDataset()
        };
        document.Osm.Nodes[1] = new OsmNode { Id = 1, Version = 1, Point = new GeoPoint(1, 1) };
        document.Osm.Nodes[2] = new OsmNode { Id = 2, Version = 1, Point = new GeoPoint(2, 2) };
        document.Osm.Ways[10] = new OsmWay { Id = 10, Version = 4, NodeIds = [1, 2] };
        var way = OsmDocumentSync.CreateWayFeature(document.Osm, document.Osm.Ways[10])!;
        document.Features.Add(way);
        document.MarkClean(compactOsmHistory: true);
        document.Features.Clear();

        var result = OsmChangeSerializer.Build(document, 99);
        var xml = XDocument.Parse(result.Xml);

        Assert.Equal(1, result.DeleteCount);
        var deletedWay = Assert.Single(xml.Root!.Element("delete")!.Elements("way"));
        Assert.Equal("10", deletedWay.Attribute("id")!.Value);
        Assert.False(result.Dataset.Ways.ContainsKey(10));
    }

    [Fact]
    public void Build_MovingSharedWayNodeModifiesOneDatasetNode() {
        var first = new GeoPoint(1, 1);
        var shared = new GeoPoint(2, 2);
        var moved = new GeoPoint(2.1, 2.1);
        var third = new GeoPoint(3, 3);
        var document = new MapDocument {
            Osm = new OsmDataset()
        };
        document.Osm.Nodes[1] = new OsmNode { Id = 1, Version = 1, Point = first };
        document.Osm.Nodes[2] = new OsmNode { Id = 2, Version = 1, Point = shared };
        document.Osm.Nodes[3] = new OsmNode { Id = 3, Version = 1, Point = third };
        document.Osm.Ways[10] = new OsmWay { Id = 10, Version = 1, NodeIds = [1, 2] };
        document.Osm.Ways[11] = new OsmWay { Id = 11, Version = 1, NodeIds = [2, 3] };
        var firstWay = OsmDocumentSync.CreateWayFeature(document.Osm, document.Osm.Ways[10])!;
        var secondWay = OsmDocumentSync.CreateWayFeature(document.Osm, document.Osm.Ways[11])!;
        document.Features.Add(firstWay);
        document.Features.Add(secondWay);
        document.MarkClean();
        firstWay.Parts[0][1] = moved;

        var result = OsmChangeSerializer.Build(document, 99);
        var xml = XDocument.Parse(result.Xml);

        Assert.Equal(0, result.CreateCount);
        Assert.Equal(1, result.ModifyCount);
        var modifiedNode = Assert.Single(xml.Root!.Element("modify")!.Elements("node"));
        Assert.Equal("2", modifiedNode.Attribute("id")!.Value);
        Assert.Null(xml.Root.Element("modify")!.Element("way"));
        Assert.Equal(moved, result.Dataset.Nodes[2].Point);
        Assert.Equal(shared, secondWay.Parts[0][0]);
    }

    [Fact]
    public void Build_ModifiedRelationWritesRelationMembers() {
        var document = new MapDocument {
            Osm = new OsmDataset()
        };
        document.Osm.Nodes[1] = new OsmNode { Id = 1, Version = 1, Point = new GeoPoint(1, 1) };
        document.Osm.Nodes[2] = new OsmNode { Id = 2, Version = 1, Point = new GeoPoint(2, 2) };
        document.Osm.Ways[10] = new OsmWay { Id = 10, Version = 1, NodeIds = [1, 2] };
        document.Osm.Relations[20] = new OsmRelation {
            Id = 20,
            Version = 3,
            Members = [new OsmRelationMember(OsmRelationMemberType.Way, 10, "outer")],
            Tags = new Dictionary<string, string> {
                ["type"] = "multipolygon",
                ["name"] = "Old"
            }
        };
        var relationFeature = new MapFeature {
            Id = "osm-relation-20",
            GeometryType = MapGeometryType.Polygon,
            Parts = [[new GeoPoint(1, 1), new GeoPoint(2, 2), new GeoPoint(1, 1)]],
            Attributes = new Dictionary<string, string> {
                ["type"] = "multipolygon",
                ["name"] = "Old"
            },
            Osm = new OsmFeatureMetadata {
                PrimitiveType = OsmPrimitiveType.Relation,
                Id = 20,
                Version = 3
            }
        };
        document.Features.Add(relationFeature);
        document.MarkClean();
        relationFeature.Attributes["name"] = "New";

        var result = OsmChangeSerializer.Build(document, 99);
        var xml = XDocument.Parse(result.Xml);

        var relation = Assert.Single(xml.Root!.Element("modify")!.Elements("relation"));
        Assert.Equal("20", relation.Attribute("id")!.Value);
        var member = Assert.Single(relation.Elements("member"));
        Assert.Equal("way", member.Attribute("type")!.Value);
        Assert.Equal("10", member.Attribute("ref")!.Value);
        Assert.Contains(relation.Elements("tag"), tag =>
            tag.Attribute("k")?.Value == "name" && tag.Attribute("v")?.Value == "New");
    }

    [Fact]
    public void Build_ModifiedRelationGeometryUpdatesOuterWayNodes() {
        var first = new GeoPoint(1, 1);
        var second = new GeoPoint(2, 2);
        var moved = new GeoPoint(2.2, 2.2);
        var third = new GeoPoint(3, 3);
        var document = new MapDocument {
            Osm = new OsmDataset()
        };
        document.Osm.Nodes[1] = new OsmNode { Id = 1, Version = 1, Point = first };
        document.Osm.Nodes[2] = new OsmNode { Id = 2, Version = 1, Point = second };
        document.Osm.Nodes[3] = new OsmNode { Id = 3, Version = 1, Point = third };
        document.Osm.Ways[10] = new OsmWay { Id = 10, Version = 1, NodeIds = [1, 2, 3, 1] };
        document.Osm.Relations[20] = new OsmRelation {
            Id = 20,
            Version = 3,
            Members = [new OsmRelationMember(OsmRelationMemberType.Way, 10, "outer")],
            Tags = new Dictionary<string, string> {
                ["type"] = "multipolygon",
                ["name"] = "Area"
            }
        };
        var relationFeature = OsmDocumentSync.CreateRelationFeature(document.Osm, document.Osm.Relations[20])!;
        document.Features.Add(relationFeature);
        document.MarkClean();
        relationFeature.Parts[0][1] = moved;

        var result = OsmChangeSerializer.Build(document, 99);
        var xml = XDocument.Parse(result.Xml);

        Assert.Equal(moved, result.Dataset.Nodes[2].Point);
        Assert.Contains(xml.Root!.Element("modify")!.Elements("node"), node => node.Attribute("id")!.Value == "2");
        Assert.DoesNotContain(xml.Root.Element("modify")!.Elements("relation"), relation => relation.Attribute("id")!.Value == "20");
    }

    [Fact]
    public void Build_RejectsMultipartFeatures() {
        var document = new MapDocument();
        document.Features.Add(new MapFeature {
            GeometryType = MapGeometryType.LineString,
            Parts = [
                [new GeoPoint(1, 1), new GeoPoint(2, 2)],
                [new GeoPoint(3, 3), new GeoPoint(4, 4)]
            ]
        });

        Assert.Throws<InvalidDataException>(() => OsmChangeSerializer.Build(document, 1));
    }
}
