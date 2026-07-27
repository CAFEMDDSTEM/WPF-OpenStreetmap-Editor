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

        Assert.Equal(1, result.CreateCount);
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
