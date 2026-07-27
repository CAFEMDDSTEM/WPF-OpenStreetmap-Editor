using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
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
