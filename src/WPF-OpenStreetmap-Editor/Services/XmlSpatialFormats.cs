using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

internal static class XmlSpatialFormats {
    private const long MaxCompressedKmlBytes = 64L * 1024 * 1024;

    public static MapDocument ReadKml(
        string path,
        SpatialImportOptions options,
        IProgress<SpatialImportProgress>? progress,
        CancellationToken ct) {
        using var stream = File.OpenRead(path);
        return ReadKmlStream(stream, options, progress, ct);
    }

    public static MapDocument ReadKmz(
        string path,
        SpatialImportOptions options,
        IProgress<SpatialImportProgress>? progress,
        CancellationToken ct) {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.Entries.FirstOrDefault(candidate =>
            candidate.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidDataException("KMZ 包中没有 KML 文件。");
        if (entry.Length > MaxCompressedKmlBytes) {
            throw new SpatialDataLimitException("KMZ 中的 KML 文件超过 64 MB 安全上限。");
        }
        using var stream = entry.Open();
        return ReadKmlStream(stream, options, progress, ct);
    }

    public static MapDocument ReadGpx(
        string path,
        SpatialImportOptions options,
        IProgress<SpatialImportProgress>? progress,
        CancellationToken ct) {
        using var stream = File.OpenRead(path);
        var xml = LoadXml(stream);
        var document = new MapDocument();
        var coordinateCount = 0;
        var index = 0;

        foreach (var waypoint in xml.Descendants().Where(static element => element.Name.LocalName == "wpt")) {
            ct.ThrowIfCancellationRequested();
            var point = ReadLatLonAttributes(waypoint, ref coordinateCount, options);
            AddFeature(document, options, new MapFeature {
                Id = $"gpx-waypoint-{index++}",
                GeometryType = MapGeometryType.Point,
                Parts = [[point]],
                Attributes = ReadCommonXmlAttributes(waypoint)
            });
        }

        foreach (var route in xml.Descendants().Where(static element => element.Name.LocalName == "rte")) {
            ct.ThrowIfCancellationRequested();
            var points = route.Elements().Where(static element => element.Name.LocalName == "rtept")
                .Select(element => ReadLatLonAttributes(element, ref coordinateCount, options)).ToList();
            if (points.Count < 2) continue;
            AddFeature(document, options, new MapFeature {
                Id = $"gpx-route-{index++}",
                GeometryType = MapGeometryType.LineString,
                Parts = [points],
                Attributes = ReadCommonXmlAttributes(route)
            });
        }

        foreach (var track in xml.Descendants().Where(static element => element.Name.LocalName == "trk")) {
            ct.ThrowIfCancellationRequested();
            var parts = track.Elements().Where(static element => element.Name.LocalName == "trkseg")
                .Select(segment => segment.Elements().Where(static element => element.Name.LocalName == "trkpt")
                    .Select(element => ReadLatLonAttributes(element, ref coordinateCount, options)).ToList())
                .Where(static part => part.Count > 1)
                .ToList();
            if (parts.Count == 0) continue;
            AddFeature(document, options, new MapFeature {
                Id = $"gpx-track-{index++}",
                GeometryType = MapGeometryType.LineString,
                Parts = parts,
                Attributes = ReadCommonXmlAttributes(track)
            });
            progress?.Report(new SpatialImportProgress(document.Features.Count, "正在读取 GPX"));
        }
        return document;
    }

    public static MapDocument ReadGml(
        string path,
        SpatialImportOptions options,
        IProgress<SpatialImportProgress>? progress,
        CancellationToken ct) {
        using var stream = File.OpenRead(path);
        var xml = LoadXml(stream);
        var document = new MapDocument();
        var coordinateCount = 0;
        var geometryIndex = 0;
        var geometryNames = new HashSet<string>(StringComparer.Ordinal) { "Point", "LineString", "Curve", "Polygon" };
        var geometries = xml.Descendants().Where(element =>
            geometryNames.Contains(element.Name.LocalName) &&
            !element.Ancestors().Any(ancestor => geometryNames.Contains(ancestor.Name.LocalName)));

        foreach (var geometry in geometries) {
            ct.ThrowIfCancellationRequested();
            var attributes = ReadGmlProperties(geometry);
            switch (geometry.Name.LocalName) {
                case "Point":
                    var pointCoordinates = ReadGmlCoordinates(geometry, ref coordinateCount, options);
                    if (pointCoordinates.Count > 0) {
                        AddFeature(document, options, new MapFeature {
                            Id = $"gml-{geometryIndex++}",
                            GeometryType = MapGeometryType.Point,
                            Parts = [[pointCoordinates[0]]],
                            Attributes = attributes
                        });
                    }
                    break;
                case "LineString":
                case "Curve":
                    var line = ReadGmlCoordinates(geometry, ref coordinateCount, options);
                    if (line.Count > 1) {
                        AddFeature(document, options, new MapFeature {
                            Id = $"gml-{geometryIndex++}",
                            GeometryType = MapGeometryType.LineString,
                            Parts = [line],
                            Attributes = attributes
                        });
                    }
                    break;
                case "Polygon":
                    var rings = geometry.Descendants()
                        .Where(static element => element.Name.LocalName is "LinearRing" or "Ring")
                        .Select(element => ReadGmlCoordinates(element, ref coordinateCount, options))
                        .Where(static ring => ring.Count > 2)
                        .ToList();
                    if (rings.Count > 0) {
                        AddFeature(document, options, new MapFeature {
                            Id = $"gml-{geometryIndex++}",
                            GeometryType = MapGeometryType.Polygon,
                            Parts = rings,
                            Attributes = attributes
                        });
                    }
                    break;
            }
            if (document.Features.Count % 1000 == 0) {
                progress?.Report(new SpatialImportProgress(document.Features.Count, "正在读取 GML"));
            }
        }
        return document;
    }

    public static void WriteKml(MapDocument document, string path, CancellationToken ct) {
        var settings = CreateXmlWriterSettings();
        using var writer = XmlWriter.Create(path, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("kml", "http://www.opengis.net/kml/2.2");
        writer.WriteStartElement("Document");
        foreach (var feature in document.Features) {
            ct.ThrowIfCancellationRequested();
            writer.WriteStartElement("Placemark");
            if (feature.Attributes.TryGetValue("name", out var name)) writer.WriteElementString("name", name);
            WriteKmlGeometry(writer, feature);
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    public static void WriteGpx(MapDocument document, string path, CancellationToken ct) {
        var settings = CreateXmlWriterSettings();
        using var writer = XmlWriter.Create(path, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("gpx", "http://www.topografix.com/GPX/1/1");
        writer.WriteAttributeString("version", "1.1");
        writer.WriteAttributeString("creator", "WPF-OpenStreetmap-Editor");
        foreach (var feature in document.Features) {
            ct.ThrowIfCancellationRequested();
            if (feature.GeometryType == MapGeometryType.Point) {
                foreach (var part in feature.Parts.Where(static item => item.Count > 0)) {
                    WriteGpxPoint(writer, "wpt", part[0], feature.Attributes);
                }
                continue;
            }

            writer.WriteStartElement("trk");
            if (feature.Attributes.TryGetValue("name", out var name)) writer.WriteElementString("name", name);
            foreach (var part in feature.Parts) {
                writer.WriteStartElement("trkseg");
                foreach (var point in part) WriteGpxPoint(writer, "trkpt", point, null);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    public static void WriteGml(MapDocument document, string path, CancellationToken ct) {
        var settings = CreateXmlWriterSettings();
        using var writer = XmlWriter.Create(path, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("FeatureCollection", "http://www.opengis.net/gml/3.2");
        foreach (var feature in document.Features) {
            ct.ThrowIfCancellationRequested();
            writer.WriteStartElement("featureMember");
            writer.WriteStartElement("feature", "urn:wpf-openstreetmap-editor");
            writer.WriteAttributeString("id", feature.Id);
            WriteGmlGeometry(writer, feature);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static MapDocument ReadKmlStream(
        Stream stream,
        SpatialImportOptions options,
        IProgress<SpatialImportProgress>? progress,
        CancellationToken ct) {
        var xml = LoadXml(stream);
        var document = new MapDocument();
        var coordinateCount = 0;
        var index = 0;
        foreach (var placemark in xml.Descendants().Where(static element => element.Name.LocalName == "Placemark")) {
            ct.ThrowIfCancellationRequested();
            var attributes = ReadCommonXmlAttributes(placemark);
            foreach (var geometry in placemark.Descendants().Where(element =>
                element.Name.LocalName is "Point" or "LineString" or "Polygon" &&
                !element.Ancestors().Any(ancestor => ancestor != placemark && ancestor.Name.LocalName is "Point" or "LineString" or "Polygon"))) {
                switch (geometry.Name.LocalName) {
                    case "Point":
                        var points = ReadKmlCoordinateElement(geometry, ref coordinateCount, options);
                        if (points.Count > 0) AddFeature(document, options, new MapFeature {
                            Id = $"kml-{index++}",
                            GeometryType = MapGeometryType.Point,
                            Parts = [[points[0]]],
                            Attributes = new Dictionary<string, string>(attributes, StringComparer.Ordinal)
                        });
                        break;
                    case "LineString":
                        var line = ReadKmlCoordinateElement(geometry, ref coordinateCount, options);
                        if (line.Count > 1) AddFeature(document, options, new MapFeature {
                            Id = $"kml-{index++}",
                            GeometryType = MapGeometryType.LineString,
                            Parts = [line],
                            Attributes = new Dictionary<string, string>(attributes, StringComparer.Ordinal)
                        });
                        break;
                    case "Polygon":
                        var rings = geometry.Descendants().Where(static element => element.Name.LocalName == "LinearRing")
                            .Select(element => ReadKmlCoordinateElement(element, ref coordinateCount, options))
                            .Where(static ring => ring.Count > 2).ToList();
                        if (rings.Count > 0) AddFeature(document, options, new MapFeature {
                            Id = $"kml-{index++}",
                            GeometryType = MapGeometryType.Polygon,
                            Parts = rings,
                            Attributes = new Dictionary<string, string>(attributes, StringComparer.Ordinal)
                        });
                        break;
                }
            }
            progress?.Report(new SpatialImportProgress(document.Features.Count, "正在读取 KML"));
        }
        return document;
    }

    private static List<GeoPoint> ReadKmlCoordinateElement(XElement geometry, ref int count, SpatialImportOptions options) {
        var text = geometry.DescendantsAndSelf().FirstOrDefault(static element => element.Name.LocalName == "coordinates")?.Value;
        if (string.IsNullOrWhiteSpace(text)) return [];
        var result = new List<GeoPoint>();
        foreach (var tuple in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) {
            var values = tuple.Split(',');
            if (values.Length < 2 ||
                !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) ||
                !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)) {
                throw new InvalidDataException("KML 包含无效坐标。");
            }
            result.Add(CheckPoint(new GeoPoint(longitude, latitude), ref count, options));
        }
        return result;
    }

    private static List<GeoPoint> ReadGmlCoordinates(XElement geometry, ref int count, SpatialImportOptions options) {
        var coordinateElement = geometry.DescendantsAndSelf().FirstOrDefault(element =>
            element.Name.LocalName is "pos" or "posList" or "coordinates");
        if (coordinateElement is null) return [];
        if (coordinateElement.Name.LocalName == "coordinates") {
            var legacyCoordinates = new List<GeoPoint>();
            var tuples = coordinateElement.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(static tuple => tuple.Split(';', StringSplitOptions.RemoveEmptyEntries));
            foreach (var tuple in tuples) {
                var values = tuple.Split(',');
                if (values.Length < 2) continue;
                legacyCoordinates.Add(CheckPoint(new GeoPoint(
                    double.Parse(values[0], CultureInfo.InvariantCulture),
                    double.Parse(values[1], CultureInfo.InvariantCulture)), ref count, options));
            }
            return legacyCoordinates;
        }

        var numbers = coordinateElement.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        var dimensionText = coordinateElement.Attribute("srsDimension")?.Value ??
            geometry.AncestorsAndSelf().Select(element => element.Attribute("srsDimension")?.Value).FirstOrDefault(static value => value is not null);
        var dimension = int.TryParse(dimensionText, out var parsedDimension) && parsedDimension >= 2 ? parsedDimension : 2;
        var latitudeFirst = geometry.AncestorsAndSelf().Attributes("srsName")
            .Any(attribute => attribute.Value.Contains("::4326", StringComparison.OrdinalIgnoreCase));
        var result = new List<GeoPoint>();
        for (var i = 0; i + 1 < numbers.Length; i += dimension) {
            var point = latitudeFirst
                ? new GeoPoint(numbers[i + 1], numbers[i])
                : new GeoPoint(numbers[i], numbers[i + 1]);
            result.Add(CheckPoint(point, ref count, options));
        }
        return result;
    }

    private static Dictionary<string, string> ReadGmlProperties(XElement geometry) {
        var container = geometry.Parent?.Parent;
        if (container is null) return new Dictionary<string, string>(StringComparer.Ordinal);
        return container.Elements()
            .Where(element => !element.DescendantsAndSelf().Contains(geometry) && !element.HasElements)
            .ToDictionary(element => element.Name.LocalName, element => element.Value, StringComparer.Ordinal);
    }

    private static Dictionary<string, string> ReadCommonXmlAttributes(XElement element) {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "name", "desc", "description", "type" }) {
            var value = element.Elements().FirstOrDefault(candidate => candidate.Name.LocalName == name)?.Value;
            if (!string.IsNullOrWhiteSpace(value)) result[name] = value;
        }
        return result;
    }

    private static GeoPoint ReadLatLonAttributes(XElement element, ref int count, SpatialImportOptions options) {
        if (!double.TryParse(element.Attribute("lon")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) ||
            !double.TryParse(element.Attribute("lat")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)) {
            throw new InvalidDataException("GPX 包含无效经纬度。");
        }
        return CheckPoint(new GeoPoint(longitude, latitude), ref count, options);
    }

    private static GeoPoint CheckPoint(GeoPoint point, ref int count, SpatialImportOptions options) {
        if (!point.IsValid) throw new InvalidDataException("空间数据包含超出经纬度范围的坐标。");
        if (++count > options.MaxCoordinates) {
            throw new SpatialDataLimitException($"文件超过安全导入上限 {options.MaxCoordinates:N0} 个坐标。");
        }
        return point;
    }

    private static void AddFeature(MapDocument document, SpatialImportOptions options, MapFeature feature) {
        if (document.Features.Count >= options.MaxFeatures) {
            throw new SpatialDataLimitException($"文件超过安全导入上限 {options.MaxFeatures:N0} 个要素。");
        }
        document.Features.Add(feature);
    }

    private static XDocument LoadXml(Stream stream) {
        var settings = new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = 256L * 1024 * 1024
        };
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static XmlWriterSettings CreateXmlWriterSettings() {
        return new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true };
    }

    private static void WriteKmlGeometry(XmlWriter writer, MapFeature feature) {
        if (feature.Parts.Count > 1 && feature.GeometryType != MapGeometryType.Polygon) writer.WriteStartElement("MultiGeometry");
        switch (feature.GeometryType) {
            case MapGeometryType.Point:
                foreach (var part in feature.Parts.Where(static item => item.Count > 0)) {
                    writer.WriteStartElement("Point");
                    writer.WriteElementString("coordinates", FormatCoordinate(part[0]));
                    writer.WriteEndElement();
                }
                break;
            case MapGeometryType.LineString:
                foreach (var part in feature.Parts) {
                    writer.WriteStartElement("LineString");
                    writer.WriteElementString("coordinates", string.Join(' ', part.Select(FormatCoordinate)));
                    writer.WriteEndElement();
                }
                break;
            case MapGeometryType.Polygon:
                writer.WriteStartElement("Polygon");
                for (var i = 0; i < feature.Parts.Count; i++) {
                    writer.WriteStartElement(i == 0 ? "outerBoundaryIs" : "innerBoundaryIs");
                    writer.WriteStartElement("LinearRing");
                    writer.WriteElementString("coordinates", string.Join(' ', feature.Parts[i].Select(FormatCoordinate)));
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
                break;
        }
        if (feature.Parts.Count > 1 && feature.GeometryType != MapGeometryType.Polygon) writer.WriteEndElement();
    }

    private static void WriteGmlGeometry(XmlWriter writer, MapFeature feature) {
        const string gml = "http://www.opengis.net/gml/3.2";
        var points = feature.Parts.SelectMany(static part => part).ToList();
        switch (feature.GeometryType) {
            case MapGeometryType.Point:
                writer.WriteStartElement("gml", "Point", gml);
                writer.WriteElementString("gml", "pos", gml, FormatPosition(points[0]));
                writer.WriteEndElement();
                break;
            case MapGeometryType.LineString:
                writer.WriteStartElement("gml", "LineString", gml);
                writer.WriteElementString("gml", "posList", gml, string.Join(' ', points.Select(FormatPosition)));
                writer.WriteEndElement();
                break;
            case MapGeometryType.Polygon:
                writer.WriteStartElement("gml", "Polygon", gml);
                for (var i = 0; i < feature.Parts.Count; i++) {
                    writer.WriteStartElement("gml", i == 0 ? "exterior" : "interior", gml);
                    writer.WriteStartElement("gml", "LinearRing", gml);
                    writer.WriteElementString("gml", "posList", gml, string.Join(' ', feature.Parts[i].Select(FormatPosition)));
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
                break;
        }
    }

    private static void WriteGpxPoint(
        XmlWriter writer,
        string elementName,
        GeoPoint point,
        IReadOnlyDictionary<string, string>? attributes) {
        writer.WriteStartElement(elementName);
        writer.WriteAttributeString("lat", point.Latitude.ToString("R", CultureInfo.InvariantCulture));
        writer.WriteAttributeString("lon", point.Longitude.ToString("R", CultureInfo.InvariantCulture));
        if (attributes?.TryGetValue("name", out var name) == true) writer.WriteElementString("name", name);
        writer.WriteEndElement();
    }

    private static string FormatCoordinate(GeoPoint point) {
        return $"{point.Longitude.ToString("R", CultureInfo.InvariantCulture)},{point.Latitude.ToString("R", CultureInfo.InvariantCulture)}";
    }

    private static string FormatPosition(GeoPoint point) {
        return $"{point.Longitude.ToString("R", CultureInfo.InvariantCulture)} {point.Latitude.ToString("R", CultureInfo.InvariantCulture)}";
    }
}
