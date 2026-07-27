using System.IO;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed record SpatialImportProgress(int FeaturesRead, string Stage);

public sealed class SpatialImportOptions {
    public int MaxFeatures { get; init; } = 1_000_000;
    public int MaxCoordinates { get; init; } = 8_000_000;
    public string SourceProjectionId { get; init; } = ProjectionService.Wgs84Id;
    public string CustomProjectionWkt { get; init; } = "";
}

public sealed class SpatialDataLimitException(string message) : IOException(message);

public static class SpatialDataService {
    public static readonly string OpenFileFilter =
        "支持的地图数据|*.osm;*.pbf;*.shp;*.dbf;*.shx;*.geojson;*.json;*.gml;*.kml;*.kmz;*.gpx|" +
        "OpenStreetMap|*.osm;*.pbf|Shapefile|*.shp;*.dbf;*.shx|GeoJSON|*.geojson;*.json|" +
        "GML|*.gml|KML/KMZ|*.kml;*.kmz|GPX|*.gpx|所有文件|*.*";

    public static readonly string SaveFileFilter =
        "GeoJSON|*.geojson|OpenStreetMap XML|*.osm|GPX|*.gpx|KML|*.kml|GML|*.gml";

    public static Task<MapDocument> ImportAsync(
        string path,
        SpatialImportOptions? options = null,
        IProgress<SpatialImportProgress>? progress = null,
        CancellationToken ct = default) {
        var fullPath = ResolveInputPath(path);
        var format = DetectFormat(fullPath);
        options ??= new SpatialImportOptions();

        return Task.Run(() => {
            ct.ThrowIfCancellationRequested();
            var document = format switch {
                SpatialFileFormat.GeoJson => GeoJsonSpatialFormat.Read(fullPath, options, progress, ct),
                SpatialFileFormat.OsmXml => OsmSpatialFormat.ReadXml(fullPath, options, progress, ct),
                SpatialFileFormat.OsmPbf => OsmSpatialFormat.ReadPbf(fullPath, options, progress, ct),
                SpatialFileFormat.Shapefile => ShapefileSpatialFormat.Read(fullPath, options, progress, ct),
                SpatialFileFormat.Gml => XmlSpatialFormats.ReadGml(fullPath, options, progress, ct),
                SpatialFileFormat.Kml => XmlSpatialFormats.ReadKml(fullPath, options, progress, ct),
                SpatialFileFormat.Kmz => XmlSpatialFormats.ReadKmz(fullPath, options, progress, ct),
                SpatialFileFormat.Gpx => XmlSpatialFormats.ReadGpx(fullPath, options, progress, ct),
                _ => throw new NotSupportedException($"不支持的数据格式：{Path.GetExtension(fullPath)}")
            };
            document.SourcePath = fullPath;
            document.SourceFormat = format;
            document.Name = Path.GetFileName(fullPath);
            document.MarkClean(compactOsmHistory: IsOsmFormat(format));
            return document;
        }, ct);
    }

    public static Task SaveAsync(MapDocument document, string path, CancellationToken ct = default) {
        var fullPath = Path.GetFullPath(path);
        var format = DetectFormat(fullPath);
        return Task.Run(() => {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            switch (format) {
                case SpatialFileFormat.GeoJson:
                    GeoJsonSpatialFormat.Write(document, fullPath, ct);
                    break;
                case SpatialFileFormat.OsmXml:
                    OsmSpatialFormat.WriteXml(document, fullPath, ct);
                    break;
                case SpatialFileFormat.Gpx:
                    XmlSpatialFormats.WriteGpx(document, fullPath, ct);
                    break;
                case SpatialFileFormat.Kml:
                    XmlSpatialFormats.WriteKml(document, fullPath, ct);
                    break;
                case SpatialFileFormat.Gml:
                    XmlSpatialFormats.WriteGml(document, fullPath, ct);
                    break;
                default:
                    throw new NotSupportedException("PBF、Shapefile 和 KMZ 当前只支持导入；请另存为 GeoJSON、OSM、GPX、KML 或 GML。");
            }

            document.SourcePath = fullPath;
            document.SourceFormat = format;
            document.Name = Path.GetFileName(fullPath);
            document.MarkSaved();
        }, ct);
    }

    public static SpatialFileFormat DetectFormat(string path) {
        return Path.GetExtension(path).ToLowerInvariant() switch {
            ".osm" => SpatialFileFormat.OsmXml,
            ".pbf" => SpatialFileFormat.OsmPbf,
            ".shp" or ".dbf" or ".shx" => SpatialFileFormat.Shapefile,
            ".geojson" or ".json" => SpatialFileFormat.GeoJson,
            ".gml" => SpatialFileFormat.Gml,
            ".kml" => SpatialFileFormat.Kml,
            ".kmz" => SpatialFileFormat.Kmz,
            ".gpx" => SpatialFileFormat.Gpx,
            var extension => throw new NotSupportedException($"不支持的数据格式：{extension}")
        };
    }

    private static bool IsOsmFormat(SpatialFileFormat format) {
        return format is SpatialFileFormat.OsmXml or SpatialFileFormat.OsmPbf;
    }

    private static string ResolveInputPath(string path) {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) {
            throw new FileNotFoundException("地图数据文件不存在。", fullPath);
        }

        var extension = Path.GetExtension(fullPath);
        if (extension.Equals(".dbf", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".shx", StringComparison.OrdinalIgnoreCase)) {
            var shapePath = Path.ChangeExtension(fullPath, ".shp");
            if (!File.Exists(shapePath)) {
                throw new FileNotFoundException("导入 DBF 或 SHX 时需要同名的 SHP 文件。", shapePath);
            }
            return shapePath;
        }

        return fullPath;
    }
}
