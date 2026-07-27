using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

internal static class ShapefileSpatialFormat {
    public static MapDocument Read(
        string path,
        SpatialImportOptions options,
        IProgress<SpatialImportProgress>? progress,
        CancellationToken ct) {
        var attributes = ReadDbf(FindCompanion(path, ".dbf"));
        var transform = CreateCoordinateTransform(FindCompanion(path, ".prj"));
        var document = new MapDocument();
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 100 || ReadInt32BigEndian(reader) != 9994) {
            throw new InvalidDataException("SHP 文件头无效。");
        }
        stream.Position = 100;

        var recordIndex = 0;
        var coordinateCount = 0;
        while (stream.Position + 8 <= stream.Length) {
            ct.ThrowIfCancellationRequested();
            var recordNumber = ReadInt32BigEndian(reader);
            var contentBytes = checked(ReadInt32BigEndian(reader) * 2);
            if (contentBytes < 4 || stream.Position + contentBytes > stream.Length) {
                throw new InvalidDataException($"SHP 记录 {recordNumber} 的长度无效。");
            }

            var content = reader.ReadBytes(contentBytes);
            using var recordStream = new MemoryStream(content, writable: false);
            using var recordReader = new BinaryReader(recordStream);
            var featureAttributes = recordIndex < attributes.Count
                ? attributes[recordIndex]
                : new Dictionary<string, string>(StringComparer.Ordinal);
            var feature = ReadShape(
                recordReader,
                recordNumber,
                featureAttributes,
                transform,
                ref coordinateCount,
                options);
            if (feature is not null) AddFeature(document, feature, options);
            recordIndex++;
            if (recordIndex % 1000 == 0) {
                progress?.Report(new SpatialImportProgress(document.Features.Count, "正在读取 Shapefile"));
            }
        }

        return document;
    }

    private static MapFeature? ReadShape(
        BinaryReader reader,
        int recordNumber,
        Dictionary<string, string> attributes,
        Func<double, double, GeoPoint> transform,
        ref int coordinateCount,
        SpatialImportOptions options) {
        var shapeType = reader.ReadInt32();
        if (shapeType == 0) return null;

        if (shapeType is 1 or 11 or 21) {
            return new MapFeature {
                Id = $"shape-{recordNumber}",
                GeometryType = MapGeometryType.Point,
                Parts = [[ReadPoint(reader, transform, ref coordinateCount, options)]],
                Attributes = attributes
            };
        }

        if (shapeType is 8 or 18 or 28) {
            SkipBounds(reader);
            var pointCount = ReadNonNegativeCount(reader, "点");
            var pointParts = new List<List<GeoPoint>>(pointCount);
            for (var i = 0; i < pointCount; i++) {
                pointParts.Add([ReadPoint(reader, transform, ref coordinateCount, options)]);
            }
            return new MapFeature {
                Id = $"shape-{recordNumber}",
                GeometryType = MapGeometryType.Point,
                Parts = pointParts,
                Attributes = attributes
            };
        }

        if (shapeType is not (3 or 5 or 13 or 15 or 23 or 25)) {
            throw new NotSupportedException($"暂不支持 Shapefile 几何类型 {shapeType}。");
        }

        SkipBounds(reader);
        var partCount = ReadNonNegativeCount(reader, "分段");
        var pointTotal = ReadNonNegativeCount(reader, "点");
        if (partCount == 0 || pointTotal == 0) return null;
        var partStarts = new int[partCount];
        for (var i = 0; i < partCount; i++) {
            partStarts[i] = reader.ReadInt32();
            if (partStarts[i] < 0 || partStarts[i] >= pointTotal ||
                (i > 0 && partStarts[i] <= partStarts[i - 1])) {
                throw new InvalidDataException("SHP 分段索引无效。");
            }
        }

        var points = new List<GeoPoint>(pointTotal);
        for (var i = 0; i < pointTotal; i++) {
            points.Add(ReadPoint(reader, transform, ref coordinateCount, options));
        }
        var parts = new List<List<GeoPoint>>(partCount);
        for (var i = 0; i < partCount; i++) {
            var end = i + 1 < partCount ? partStarts[i + 1] : pointTotal;
            parts.Add(points.GetRange(partStarts[i], end - partStarts[i]));
        }

        return new MapFeature {
            Id = $"shape-{recordNumber}",
            GeometryType = shapeType is 5 or 15 or 25 ? MapGeometryType.Polygon : MapGeometryType.LineString,
            Parts = parts,
            Attributes = attributes
        };
    }

    private static List<Dictionary<string, string>> ReadDbf(string? path) {
        if (path is null) return [];
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 32) throw new InvalidDataException("DBF 文件头无效。");

        reader.ReadByte();
        reader.ReadBytes(3);
        var recordCount = reader.ReadInt32();
        var headerLength = reader.ReadUInt16();
        var recordLength = reader.ReadUInt16();
        if (recordCount < 0 || headerLength < 33 || recordLength < 1 || headerLength > stream.Length) {
            throw new InvalidDataException("DBF 文件头包含无效长度。");
        }
        stream.Position = 32;

        var fields = new List<DbfField>();
        while (stream.Position < headerLength) {
            var first = reader.ReadByte();
            if (first == 0x0D) break;
            var nameBytes = new byte[11];
            nameBytes[0] = first;
            reader.Read(nameBytes, 1, 10);
            var name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0', ' ');
            var type = (char)reader.ReadByte();
            reader.ReadBytes(4);
            var length = reader.ReadByte();
            var decimalCount = reader.ReadByte();
            reader.ReadBytes(14);
            fields.Add(new DbfField(name, type, length, decimalCount));
        }

        var encoding = ResolveDbfEncoding(path);
        stream.Position = headerLength;
        var records = new List<Dictionary<string, string>>(recordCount);
        for (var recordIndex = 0; recordIndex < recordCount && stream.Position + recordLength <= stream.Length; recordIndex++) {
            var deleted = reader.ReadByte() == (byte)'*';
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in fields) {
                var value = encoding.GetString(reader.ReadBytes(field.Length)).Trim('\0', ' ');
                if (!deleted && value.Length > 0) values[field.Name] = value;
            }
            var consumed = 1 + fields.Sum(static field => field.Length);
            if (consumed < recordLength) reader.ReadBytes(recordLength - consumed);
            records.Add(values);
        }
        return records;
    }

    private static Encoding ResolveDbfEncoding(string dbfPath) {
        var cpgPath = FindCompanion(dbfPath, ".cpg");
        if (cpgPath is not null) {
            var name = File.ReadAllText(cpgPath).Trim();
            try {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(name);
            } catch (ArgumentException) {
                throw new InvalidDataException($"DBF 的 CPG 文件指定了不支持的编码“{name}”。");
            }
        }
        return Encoding.Latin1;
    }

    private static Func<double, double, GeoPoint> CreateCoordinateTransform(string? prjPath) {
        if (prjPath is null) return static (x, y) => ValidatePoint(x, y);
        var wkt = File.ReadAllText(prjPath);
        try {
            var coordinateSystemFactory = new CoordinateSystemFactory();
            var source = coordinateSystemFactory.CreateFromWkt(wkt);
            var transform = new CoordinateTransformationFactory()
                .CreateFromCoordinateSystems(source, GeographicCoordinateSystem.WGS84)
                .MathTransform;
            return (x, y) => {
                var result = transform.Transform([x, y]);
                return ValidatePoint(result[0], result[1]);
            };
        } catch (Exception ex) when (ex is not InvalidDataException) {
            throw new InvalidDataException("无法解析 Shapefile 的 PRJ 坐标系。", ex);
        }
    }

    private static GeoPoint ReadPoint(
        BinaryReader reader,
        Func<double, double, GeoPoint> transform,
        ref int coordinateCount,
        SpatialImportOptions options) {
        if (++coordinateCount > options.MaxCoordinates) {
            throw new SpatialDataLimitException($"Shapefile 超过安全导入上限 {options.MaxCoordinates:N0} 个坐标。");
        }
        return transform(reader.ReadDouble(), reader.ReadDouble());
    }

    private static GeoPoint ValidatePoint(double longitude, double latitude) {
        var point = new GeoPoint(longitude, latitude);
        if (!point.IsValid) {
            throw new InvalidDataException("Shapefile 坐标不在有效经纬度范围内，并且没有可用的 PRJ 转换。");
        }
        return point;
    }

    private static void AddFeature(MapDocument document, MapFeature feature, SpatialImportOptions options) {
        if (document.Features.Count >= options.MaxFeatures) {
            throw new SpatialDataLimitException($"Shapefile 超过安全导入上限 {options.MaxFeatures:N0} 个要素。");
        }
        document.Features.Add(feature);
    }

    private static int ReadInt32BigEndian(BinaryReader reader) {
        Span<byte> bytes = stackalloc byte[4];
        if (reader.Read(bytes) != bytes.Length) throw new EndOfStreamException();
        return BinaryPrimitives.ReadInt32BigEndian(bytes);
    }

    private static int ReadNonNegativeCount(BinaryReader reader, string label) {
        var value = reader.ReadInt32();
        if (value < 0 || value > 10_000_000) throw new InvalidDataException($"SHP {label}数量无效。");
        return value;
    }

    private static void SkipBounds(BinaryReader reader) {
        reader.ReadBytes(32);
    }

    private static string? FindCompanion(string path, string extension) {
        var expected = Path.ChangeExtension(path, extension);
        if (File.Exists(expected)) return expected;
        var directory = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        return Directory.EnumerateFiles(directory)
            .FirstOrDefault(candidate =>
                string.Equals(Path.GetFileNameWithoutExtension(candidate), stem, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetExtension(candidate), extension, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record DbfField(string Name, char Type, int Length, int DecimalCount);
}
