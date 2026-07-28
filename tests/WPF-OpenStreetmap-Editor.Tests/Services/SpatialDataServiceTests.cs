using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using OsmSharp;
using OsmSharp.Streams;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class SpatialDataServiceTests {
    [Fact]
    public async Task ImportAsync_GeoJsonReadsSupportedGeometryAndProperties() {
        var root = CreateTestDirectory();
        var path = Path.Combine(root, "map.geojson");
        try {
            File.WriteAllText(path, """
                {
                  "type": "FeatureCollection",
                  "features": [
                    {"type":"Feature","id":"p","properties":{"name":"Point"},"geometry":{"type":"Point","coordinates":[103.8,1.3]}},
                    {"type":"Feature","id":"l","properties":{},"geometry":{"type":"LineString","coordinates":[[103.8,1.3],[103.9,1.4]]}},
                    {"type":"Feature","id":"a","properties":{},"geometry":{"type":"Polygon","coordinates":[[[103.8,1.3],[103.9,1.3],[103.9,1.4],[103.8,1.3]]]}}
                  ]
                }
                """);

            var document = await SpatialDataService.ImportAsync(path);

            Assert.Equal(3, document.Features.Count);
            Assert.Equal("Point", document.Features[0].Attributes["name"]);
            Assert.Equal(MapGeometryType.Point, document.Features[0].GeometryType);
            Assert.Equal(MapGeometryType.LineString, document.Features[1].GeometryType);
            Assert.Equal(MapGeometryType.Polygon, document.Features[2].GeometryType);
            Assert.False(document.IsDirty);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public async Task ImportAsync_GeoJsonTransformsConfiguredProjectedCoordinates() {
        var root = CreateTestDirectory();
        var path = Path.Combine(root, "projected.geojson");
        try {
            var (x, y) = ToWebMercatorMeters(10, 20);
            File.WriteAllText(path, $$"""
                {
                  "type": "Feature",
                  "id": "projected-point",
                  "properties": {},
                  "geometry": { "type": "Point", "coordinates": [{{x.ToString("R", CultureInfo.InvariantCulture)}}, {{y.ToString("R", CultureInfo.InvariantCulture)}}] }
                }
                """);

            var document = await SpatialDataService.ImportAsync(
                path,
                new SpatialImportOptions { SourceProjectionId = ProjectionService.WebMercatorId });

            var point = Assert.Single(document.Features).Parts[0][0];
            Assert.Equal(10, point.Longitude, precision: 6);
            Assert.Equal(20, point.Latitude, precision: 6);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void ProjectionService_Cgcs2000MercatorPresetCanTransformCoordinates() {
        var transform = ProjectionService.CreateImportTransform(ProjectionService.Cgcs2000MercatorId);

        var point = transform(0, 0);

        Assert.Equal(0, point.Longitude, precision: 10);
        Assert.Equal(0, point.Latitude, precision: 10);
    }

    [Fact]
    public void ProjectionService_Etrs89Utm33NTransformsBerlinCoordinate() {
        var transform = ProjectionService.CreateImportTransform(ProjectionService.Etrs89Utm33NId);

        var point = transform(391779.26, 5820072.16);

        Assert.Equal(13.405, point.Longitude, precision: 3);
        Assert.Equal(52.52, point.Latitude, precision: 2);
    }

    [Fact]
    public void MapDisplayTransform_AppliesMetricOffsetReversibly() {
        var transform = MapDisplayTransform.Create(new MapDisplayAlignmentOptions {
            ProjectionId = ProjectionService.Etrs89Utm33NId,
            OffsetX = 25,
            OffsetY = -15
        });
        var documentPoint = new GeoPoint(13.405, 52.52);

        var displayPoint = transform.DocumentToDisplay(documentPoint);
        var restoredPoint = transform.DisplayToDocument(displayPoint);

        Assert.NotEqual(documentPoint.Longitude, displayPoint.Longitude, precision: 8);
        Assert.Equal(documentPoint.Longitude, restoredPoint.Longitude, precision: 6);
        Assert.Equal(documentPoint.Latitude, restoredPoint.Latitude, precision: 6);
    }

    [Fact]
    public async Task SaveAsync_GeoJsonRoundTripsDocument() {
        var root = CreateTestDirectory();
        var path = Path.Combine(root, "saved.geojson");
        try {
            var document = new MapDocument();
            document.Features.Add(new MapFeature {
                Id = "line-1",
                GeometryType = MapGeometryType.LineString,
                Parts = [[new GeoPoint(10, 20), new GeoPoint(11, 21)]],
                Attributes = new Dictionary<string, string> { ["highway"] = "service" }
            });

            await SpatialDataService.SaveAsync(document, path);
            var loaded = await SpatialDataService.ImportAsync(path);

            var feature = Assert.Single(loaded.Features);
            Assert.Equal("service", feature.Attributes["highway"]);
            Assert.Equal(new GeoPoint(11, 21), feature.Parts[0][1]);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public async Task SaveAsync_OsmXmlReusesOriginalWayNodesAfterInsertedPoint() {
        var root = CreateTestDirectory();
        var path = Path.Combine(root, "saved.osm");
        try {
            var first = new GeoPoint(1, 1);
            var inserted = new GeoPoint(1.5, 1.5);
            var second = new GeoPoint(2, 2);
            var third = new GeoPoint(3, 3);
            var document = new MapDocument();
            document.Features.Add(new MapFeature {
                Id = "osm-way-10",
                GeometryType = MapGeometryType.LineString,
                Parts = [[first, inserted, second, third]],
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
            });

            await SpatialDataService.SaveAsync(document, path);
            var xml = XDocument.Load(path);

            var nodeIds = xml.Descendants("node")
                .Select(element => element.Attribute("id")!.Value);
            var createdNodeId = Assert.Single(nodeIds, static id => id.StartsWith("-"));
            var wayNodeIds = xml.Descendants("way").Single().Elements("nd")
                .Select(element => element.Attribute("ref")!.Value)
                .ToArray();
            Assert.Equal(["1", createdNodeId, "2", "3"], wayNodeIds);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public async Task ImportAsync_OsmXmlResolvesWaysAndMetadata() {
        var root = CreateTestDirectory();
        var path = Path.Combine(root, "map.osm");
        try {
            File.WriteAllText(path, """
                <osm version="0.6">
                  <node id="1" lat="1.30" lon="103.80" version="2"><tag k="amenity" v="bench" /></node>
                  <node id="2" lat="1.31" lon="103.81" version="1" />
                  <node id="3" lat="1.32" lon="103.82" version="1" />
                  <way id="10" version="4"><nd ref="1"/><nd ref="2"/><nd ref="3"/><tag k="highway" v="service"/></way>
                </osm>
                """);

            var document = await SpatialDataService.ImportAsync(path);

            Assert.Equal(2, document.Features.Count);
            var node = Assert.Single(document.Features, feature => feature.GeometryType == MapGeometryType.Point);
            var way = Assert.Single(document.Features, feature => feature.GeometryType == MapGeometryType.LineString);
            Assert.Equal(1, node.Osm!.Id);
            Assert.Equal(4, way.Osm!.Version);
            Assert.Equal(3, way.Parts[0].Count);
            Assert.Same(document.Osm, document.OriginalOsm);
            Assert.All(document.OriginalFeatures.Values, feature => Assert.Empty(feature.Parts));
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public async Task ImportAsync_OsmXmlResolvesWayGeometryWhenNodesAppearAfterWay() {
        var root = CreateTestDirectory();
        var path = Path.Combine(root, "map.osm");
        try {
            File.WriteAllText(path, """
                <osm version="0.6">
                  <way id="10" version="4"><nd ref="1"/><nd ref="2"/><nd ref="3"/><tag k="highway" v="service"/></way>
                  <node id="1" lat="1.30" lon="103.80" version="2" />
                  <node id="2" lat="1.31" lon="103.81" version="1" />
                  <node id="3" lat="1.32" lon="103.82" version="1" />
                </osm>
                """);

            var document = await SpatialDataService.ImportAsync(path);

            var way = Assert.Single(document.Features);
            Assert.Equal(MapGeometryType.LineString, way.GeometryType);
            Assert.Equal(3, way.Parts[0].Count);
            Assert.Equal(103.82, way.Parts[0][2].Longitude, precision: 6);
            Assert.Equal(1.32, way.Parts[0][2].Latitude, precision: 6);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public async Task ImportAsync_OsmXmlPreservesRelationsInDataset() {
        var root = CreateTestDirectory();
        var path = Path.Combine(root, "relations.osm");
        try {
            File.WriteAllText(path, """
                <osm version="0.6">
                  <node id="1" lat="1.30" lon="103.80" version="1" />
                  <node id="2" lat="1.31" lon="103.81" version="1" />
                  <node id="3" lat="1.32" lon="103.82" version="1" />
                  <way id="10" version="2"><nd ref="1"/><nd ref="2"/><nd ref="3"/><nd ref="1"/></way>
                  <relation id="20" version="3">
                    <member type="way" ref="10" role="outer" />
                    <tag k="type" v="multipolygon" />
                    <tag k="name" v="Test area" />
                  </relation>
                </osm>
                """);

            var document = await SpatialDataService.ImportAsync(path);

            Assert.NotNull(document.Osm);
            Assert.Equal(3, document.Osm!.Nodes.Count);
            Assert.Single(document.Osm.Ways);
            var relation = Assert.Single(document.Osm.Relations.Values);
            var member = Assert.Single(relation.Members);
            Assert.Equal(OsmRelationMemberType.Way, member.Type);
            Assert.Equal(10, member.Id);
            Assert.Equal("outer", member.Role);
            Assert.Equal(0, document.SkippedFeatureCount);
            Assert.Contains(document.Features, feature => feature.Osm?.PrimitiveType == OsmPrimitiveType.Relation);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public async Task SaveAsync_OsmXmlWritesDatasetRelations() {
        var root = CreateTestDirectory();
        var path = Path.Combine(root, "relations.osm");
        try {
            var document = new MapDocument {
                Osm = new OsmDataset()
            };
            document.Osm.Nodes[1] = new OsmNode { Id = 1, Version = 1, Point = new GeoPoint(103.8, 1.3) };
            document.Osm.Nodes[2] = new OsmNode { Id = 2, Version = 1, Point = new GeoPoint(103.9, 1.4) };
            document.Osm.Ways[10] = new OsmWay { Id = 10, Version = 2, NodeIds = [1, 2] };
            document.Osm.Relations[20] = new OsmRelation {
                Id = 20,
                Version = 3,
                Members = [new OsmRelationMember(OsmRelationMemberType.Way, 10, "route")],
                Tags = new Dictionary<string, string> { ["type"] = "route" }
            };

            await SpatialDataService.SaveAsync(document, path);
            var xml = XDocument.Load(path);

            var relation = Assert.Single(xml.Descendants("relation"));
            Assert.Equal("20", relation.Attribute("id")!.Value);
            var member = Assert.Single(relation.Elements("member"));
            Assert.Equal("way", member.Attribute("type")!.Value);
            Assert.Equal("10", member.Attribute("ref")!.Value);
            Assert.Equal("route", member.Attribute("role")!.Value);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public async Task SaveAsync_OsmXmlDoesNotResetUploadBaseline() {
        var root = CreateTestDirectory();
        var path = Path.Combine(root, "saved.osm");
        try {
            var original = new GeoPoint(1, 1);
            var moved = new GeoPoint(1.2, 1.2);
            var document = new MapDocument {
                Osm = new OsmDataset()
            };
            document.Osm.Nodes[1] = new OsmNode { Id = 1, Version = 2, Point = original };
            var feature = OsmDocumentSync.CreateNodeFeature(document.Osm.Nodes[1]);
            document.Features.Add(feature);
            document.MarkClean();
            feature.Parts[0][0] = moved;
            document.IsDirty = true;

            await SpatialDataService.SaveAsync(document, path);
            var changes = OsmChangeSerializer.Build(document, 99);
            var xml = XDocument.Parse(changes.Xml);

            Assert.False(document.IsDirty);
            Assert.Equal(1, changes.ModifyCount);
            var node = Assert.Single(xml.Root!.Element("modify")!.Elements("node"));
            Assert.Equal("1", node.Attribute("id")!.Value);
            Assert.Equal(moved.Latitude.ToString("R", CultureInfo.InvariantCulture), node.Attribute("lat")!.Value);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public async Task ImportAsync_OsmPbfResolvesWayGeometry() {
        var root = CreateTestDirectory();
        var path = Path.Combine(root, "map.pbf");
        try {
            using (var stream = File.Create(path)) {
                var target = new PBFOsmStreamTarget(stream);
                target.RegisterSource(new OsmGeo[] {
                    new Node { Id = 1, Latitude = 1.3, Longitude = 103.8, Version = 1 },
                    new Node { Id = 2, Latitude = 1.4, Longitude = 103.9, Version = 1 },
                    new Way { Id = 10, Nodes = [1, 2], Version = 2 }
                });
                target.Pull();
                target.Flush();
            }

            var document = await SpatialDataService.ImportAsync(path);

            var way = Assert.Single(document.Features);
            Assert.Equal(MapGeometryType.LineString, way.GeometryType);
            Assert.Equal(10, way.Osm!.Id);
            Assert.Equal(103.9, way.Parts[0][1].Longitude, precision: 6);
            Assert.Equal(1.4, way.Parts[0][1].Latitude, precision: 6);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Theory]
    [InlineData("map.kml", "<kml xmlns='http://www.opengis.net/kml/2.2'><Placemark><name>A</name><Point><coordinates>103.8,1.3</coordinates></Point></Placemark></kml>")]
    [InlineData("map.gpx", "<gpx version='1.1' xmlns='http://www.topografix.com/GPX/1/1'><wpt lat='1.3' lon='103.8'><name>A</name></wpt></gpx>")]
    [InlineData("map.gml", "<FeatureCollection xmlns='http://www.opengis.net/gml/3.2'><featureMember><feature xmlns='urn:test'><geometry><Point xmlns='http://www.opengis.net/gml/3.2'><pos>103.8 1.3</pos></Point></geometry></feature></featureMember></FeatureCollection>")]
    public async Task ImportAsync_XmlPointFormatsReadPoint(string fileName, string content) {
        var root = CreateTestDirectory();
        var path = Path.Combine(root, fileName);
        try {
            File.WriteAllText(path, content);

            var document = await SpatialDataService.ImportAsync(path);

            var feature = Assert.Single(document.Features);
            Assert.Equal(MapGeometryType.Point, feature.GeometryType);
            Assert.Equal(new GeoPoint(103.8, 1.3), feature.Parts[0][0]);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public async Task ImportAsync_KmzReadsContainedKml() {
        var root = CreateTestDirectory();
        var path = Path.Combine(root, "map.kmz");
        try {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create)) {
                var entry = archive.CreateEntry("doc.kml");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("<kml xmlns='http://www.opengis.net/kml/2.2'><Placemark><Point><coordinates>103.8,1.3</coordinates></Point></Placemark></kml>");
            }

            var document = await SpatialDataService.ImportAsync(path);

            Assert.Single(document.Features);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Theory]
    [InlineData("map.dbf")]
    [InlineData("map.shx")]
    public async Task ImportAsync_ShapefileCompanionSelectionLoadsShapeAndDbf(string selectedFile) {
        var root = CreateTestDirectory();
        try {
            WritePointShapefile(Path.Combine(root, "map.shp"), 103.8, 1.3);
            WriteDbf(Path.Combine(root, "map.dbf"), "Central");
            File.WriteAllBytes(Path.Combine(root, "map.shx"), [0]);

            var document = await SpatialDataService.ImportAsync(Path.Combine(root, selectedFile));

            var feature = Assert.Single(document.Features);
            Assert.Equal(new GeoPoint(103.8, 1.3), feature.Parts[0][0]);
            Assert.Equal("Central", feature.Attributes["NAME"]);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public async Task ImportAsync_ShapefileWithoutPrjUsesConfiguredProjection() {
        var root = CreateTestDirectory();
        try {
            var (x, y) = ToWebMercatorMeters(10, 20);
            WritePointShapefile(Path.Combine(root, "map.shp"), x, y);

            var document = await SpatialDataService.ImportAsync(
                Path.Combine(root, "map.shp"),
                new SpatialImportOptions { SourceProjectionId = ProjectionService.WebMercatorId });

            var point = Assert.Single(document.Features).Parts[0][0];
            Assert.Equal(10, point.Longitude, precision: 6);
            Assert.Equal(20, point.Latitude, precision: 6);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public async Task ImportAsync_StopsAtConfiguredCoordinateLimit() {
        var root = CreateTestDirectory();
        var path = Path.Combine(root, "map.geojson");
        try {
            File.WriteAllText(path, """
                {"type":"LineString","coordinates":[[0,0],[1,1],[2,2]]}
                """);

            await Assert.ThrowsAsync<SpatialDataLimitException>(() => SpatialDataService.ImportAsync(
                path,
                new SpatialImportOptions { MaxCoordinates = 2 }));
        } finally {
            DeleteTestDirectory(root);
        }
    }

    private static void WritePointShapefile(string path, double longitude, double latitude) {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        WriteBigEndian(writer, 9994);
        for (var i = 0; i < 5; i++) WriteBigEndian(writer, 0);
        WriteBigEndian(writer, 64);
        writer.Write(1000);
        writer.Write(1);
        writer.Write(longitude);
        writer.Write(latitude);
        writer.Write(longitude);
        writer.Write(latitude);
        writer.Write(0d);
        writer.Write(0d);
        writer.Write(0d);
        writer.Write(0d);
        WriteBigEndian(writer, 1);
        WriteBigEndian(writer, 10);
        writer.Write(1);
        writer.Write(longitude);
        writer.Write(latitude);
    }

    private static (double X, double Y) ToWebMercatorMeters(double longitude, double latitude) {
        const double radius = 6378137.0;
        var x = longitude * Math.PI / 180.0 * radius;
        var latRad = latitude * Math.PI / 180.0;
        var y = Math.Log(Math.Tan(Math.PI / 4.0 + latRad / 2.0)) * radius;
        return (x, y);
    }

    private static void WriteDbf(string path, string value) {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)3);
        writer.Write(new byte[3]);
        writer.Write(1);
        writer.Write((ushort)65);
        writer.Write((ushort)11);
        writer.Write(new byte[20]);
        var fieldName = new byte[11];
        Encoding.ASCII.GetBytes("NAME").CopyTo(fieldName, 0);
        writer.Write(fieldName);
        writer.Write((byte)'C');
        writer.Write(new byte[4]);
        writer.Write((byte)10);
        writer.Write((byte)0);
        writer.Write(new byte[14]);
        writer.Write((byte)0x0D);
        writer.Write((byte)' ');
        writer.Write(Encoding.ASCII.GetBytes(value.PadRight(10)));
        writer.Write((byte)0x1A);
    }

    private static void WriteBigEndian(BinaryWriter writer, int value) {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        writer.Write(bytes);
    }

    private static string CreateTestDirectory() {
        var path = Path.Combine(Path.GetTempPath(), "wpf-osm-editor-spatial-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTestDirectory(string path) {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
