using System.Globalization;
using System.IO;
using System.Text.Json;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

internal static class GeoJsonSpatialFormat {
    public static MapDocument Read(
        string path,
        SpatialImportOptions options,
        IProgress<SpatialImportProgress>? progress,
        CancellationToken ct) {
        using var stream = File.OpenRead(path);
        using var json = JsonDocument.Parse(stream, new JsonDocumentOptions {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        var document = new MapDocument();
        var coordinateCount = 0;
        var root = json.RootElement;

        if (IsType(root, "FeatureCollection") && root.TryGetProperty("features", out var features)) {
            var index = 0;
            foreach (var feature in features.EnumerateArray()) {
                ct.ThrowIfCancellationRequested();
                ReadFeature(feature, document, ref coordinateCount, options, index++);
                Report(progress, document.Features.Count, "正在读取 GeoJSON");
            }
        } else if (IsType(root, "Feature")) {
            ReadFeature(root, document, ref coordinateCount, options, 0);
        } else {
            ReadGeometry(root, new Dictionary<string, string>(), document, ref coordinateCount, options, "geometry-0");
        }

        return document;
    }

    public static void Write(MapDocument document, string path, CancellationToken ct) {
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("type", "FeatureCollection");
        writer.WriteStartArray("features");
        foreach (var feature in document.Features) {
            ct.ThrowIfCancellationRequested();
            writer.WriteStartObject();
            writer.WriteString("type", "Feature");
            writer.WriteString("id", feature.Id);
            writer.WriteStartObject("properties");
            foreach (var attribute in feature.Attributes.OrderBy(static item => item.Key, StringComparer.Ordinal)) {
                writer.WriteString(attribute.Key, attribute.Value);
            }
            writer.WriteEndObject();
            writer.WritePropertyName("geometry");
            WriteGeometry(writer, feature);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void ReadFeature(
        JsonElement source,
        MapDocument document,
        ref int coordinateCount,
        SpatialImportOptions options,
        int index) {
        if (!source.TryGetProperty("geometry", out var geometry) || geometry.ValueKind == JsonValueKind.Null) return;

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (source.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object) {
            foreach (var property in properties.EnumerateObject()) {
                attributes[property.Name] = JsonValueToString(property.Value);
            }
        }

        var id = source.TryGetProperty("id", out var idValue)
            ? JsonValueToString(idValue)
            : $"feature-{index.ToString(CultureInfo.InvariantCulture)}";
        ReadGeometry(geometry, attributes, document, ref coordinateCount, options, id);
    }

    private static void ReadGeometry(
        JsonElement geometry,
        Dictionary<string, string> attributes,
        MapDocument document,
        ref int coordinateCount,
        SpatialImportOptions options,
        string id) {
        if (!geometry.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String) return;
        var type = typeElement.GetString();

        if (type == "GeometryCollection" && geometry.TryGetProperty("geometries", out var geometries)) {
            var childIndex = 0;
            foreach (var child in geometries.EnumerateArray()) {
                ReadGeometry(child, new Dictionary<string, string>(attributes), document, ref coordinateCount, options, $"{id}-{childIndex++}");
            }
            return;
        }
        if (!geometry.TryGetProperty("coordinates", out var coordinates)) return;

        switch (type) {
            case "Point":
                AddFeature(document, options, new MapFeature {
                    Id = id,
                    GeometryType = MapGeometryType.Point,
                    Parts = [[ReadPoint(coordinates, ref coordinateCount, options)]],
                    Attributes = attributes
                });
                break;
            case "MultiPoint":
                var pointParts = new List<List<GeoPoint>>();
                foreach (var point in coordinates.EnumerateArray()) {
                    pointParts.Add([ReadPoint(point, ref coordinateCount, options)]);
                }
                AddFeature(document, options, new MapFeature {
                    Id = id,
                    GeometryType = MapGeometryType.Point,
                    Parts = pointParts,
                    Attributes = attributes
                });
                break;
            case "LineString":
                AddFeature(document, options, new MapFeature {
                    Id = id,
                    GeometryType = MapGeometryType.LineString,
                    Parts = [ReadLine(coordinates, ref coordinateCount, options)],
                    Attributes = attributes
                });
                break;
            case "MultiLineString":
                var lineParts = new List<List<GeoPoint>>();
                foreach (var line in coordinates.EnumerateArray()) {
                    lineParts.Add(ReadLine(line, ref coordinateCount, options));
                }
                AddFeature(document, options, new MapFeature {
                    Id = id,
                    GeometryType = MapGeometryType.LineString,
                    Parts = lineParts,
                    Attributes = attributes
                });
                break;
            case "Polygon":
                var polygonParts = new List<List<GeoPoint>>();
                foreach (var ring in coordinates.EnumerateArray()) {
                    polygonParts.Add(ReadLine(ring, ref coordinateCount, options));
                }
                AddFeature(document, options, new MapFeature {
                    Id = id,
                    GeometryType = MapGeometryType.Polygon,
                    Parts = polygonParts,
                    Attributes = attributes
                });
                break;
            case "MultiPolygon":
                var polygonIndex = 0;
                foreach (var polygon in coordinates.EnumerateArray()) {
                    var rings = new List<List<GeoPoint>>();
                    foreach (var ring in polygon.EnumerateArray()) {
                        rings.Add(ReadLine(ring, ref coordinateCount, options));
                    }
                    AddFeature(document, options, new MapFeature {
                        Id = $"{id}-{polygonIndex++}",
                        GeometryType = MapGeometryType.Polygon,
                        Parts = rings,
                        Attributes = new Dictionary<string, string>(attributes, StringComparer.Ordinal)
                    });
                }
                break;
        }
    }

    private static GeoPoint ReadPoint(JsonElement coordinates, ref int count, SpatialImportOptions options) {
        if (coordinates.ValueKind != JsonValueKind.Array || coordinates.GetArrayLength() < 2) {
            throw new InvalidDataException("GeoJSON 坐标必须至少包含经度和纬度。");
        }
        CheckCoordinateLimit(++count, options);
        var point = new GeoPoint(coordinates[0].GetDouble(), coordinates[1].GetDouble());
        if (!point.IsValid) throw new InvalidDataException("GeoJSON 包含无效的经纬度坐标。");
        return point;
    }

    private static List<GeoPoint> ReadLine(JsonElement coordinates, ref int count, SpatialImportOptions options) {
        var points = new List<GeoPoint>();
        foreach (var point in coordinates.EnumerateArray()) {
            points.Add(ReadPoint(point, ref count, options));
        }
        return points;
    }

    private static void AddFeature(MapDocument document, SpatialImportOptions options, MapFeature feature) {
        if (feature.Parts.Count == 0 || feature.Points.Any(static point => !point.IsValid)) return;
        if (document.Features.Count >= options.MaxFeatures) {
            throw new SpatialDataLimitException($"文件超过安全导入上限 {options.MaxFeatures:N0} 个要素。请先裁剪数据范围后重试。");
        }
        document.Features.Add(feature);
    }

    private static void CheckCoordinateLimit(int count, SpatialImportOptions options) {
        if (count > options.MaxCoordinates) {
            throw new SpatialDataLimitException($"文件超过安全导入上限 {options.MaxCoordinates:N0} 个坐标。请先裁剪数据范围后重试。");
        }
    }

    private static void WriteGeometry(Utf8JsonWriter writer, MapFeature feature) {
        writer.WriteStartObject();
        switch (feature.GeometryType) {
            case MapGeometryType.Point:
                writer.WriteString("type", feature.Parts.Count == 1 ? "Point" : "MultiPoint");
                writer.WritePropertyName("coordinates");
                if (feature.Parts.Count == 1) WritePoint(writer, feature.Parts[0][0]);
                else WriteParts(writer, feature.Parts, WritePoint);
                break;
            case MapGeometryType.LineString:
                writer.WriteString("type", feature.Parts.Count == 1 ? "LineString" : "MultiLineString");
                writer.WritePropertyName("coordinates");
                if (feature.Parts.Count == 1) WriteLine(writer, feature.Parts[0]);
                else WriteParts(writer, feature.Parts, WriteLine);
                break;
            case MapGeometryType.Polygon:
                writer.WriteString("type", "Polygon");
                writer.WritePropertyName("coordinates");
                WriteParts(writer, feature.Parts, WriteLine);
                break;
        }
        writer.WriteEndObject();
    }

    private static void WriteParts(Utf8JsonWriter writer, IEnumerable<List<GeoPoint>> parts, Action<Utf8JsonWriter, IEnumerable<GeoPoint>> writePart) {
        writer.WriteStartArray();
        foreach (var part in parts) writePart(writer, part);
        writer.WriteEndArray();
    }

    private static void WritePoint(Utf8JsonWriter writer, IEnumerable<GeoPoint> points) {
        WritePoint(writer, points.First());
    }

    private static void WritePoint(Utf8JsonWriter writer, GeoPoint point) {
        writer.WriteStartArray();
        writer.WriteNumberValue(point.Longitude);
        writer.WriteNumberValue(point.Latitude);
        writer.WriteEndArray();
    }

    private static void WriteLine(Utf8JsonWriter writer, IEnumerable<GeoPoint> points) {
        writer.WriteStartArray();
        foreach (var point in points) WritePoint(writer, point);
        writer.WriteEndArray();
    }

    private static bool IsType(JsonElement element, string expected) {
        return element.TryGetProperty("type", out var type) &&
            string.Equals(type.GetString(), expected, StringComparison.Ordinal);
    }

    private static string JsonValueToString(JsonElement value) {
        return value.ValueKind switch {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Null => "",
            _ => value.GetRawText()
        };
    }

    private static void Report(IProgress<SpatialImportProgress>? progress, int count, string stage) {
        if (count % 1000 == 0) progress?.Report(new SpatialImportProgress(count, stage));
    }
}
