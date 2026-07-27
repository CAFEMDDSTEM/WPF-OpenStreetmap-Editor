using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using OsmSharp;
using OsmSharp.Streams;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

internal static class OsmSpatialFormat {
    public static MapDocument ReadXml(
        string path,
        SpatialImportOptions options,
        IProgress<SpatialImportProgress>? progress,
        CancellationToken ct) {
        using var stream = File.OpenRead(path);
        using var source = new XmlOsmStreamSource(stream);
        return Read(source, options, progress, ct, "正在读取 OSM XML");
    }

    public static MapDocument ReadPbf(
        string path,
        SpatialImportOptions options,
        IProgress<SpatialImportProgress>? progress,
        CancellationToken ct) {
        using var stream = File.OpenRead(path);
        using var source = new PBFOsmStreamSource(stream);
        return Read(source, options, progress, ct, "正在读取 OSM PBF");
    }

    public static void WriteXml(MapDocument document, string path, CancellationToken ct) {
        var settings = new XmlWriterSettings {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            Async = false
        };
        using var writer = XmlWriter.Create(path, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("osm");
        writer.WriteAttributeString("version", "0.6");
        writer.WriteAttributeString("generator", "WPF-OpenStreetmap-Editor");

        var nextNodeId = -1L;
        var nextWayId = -1L;
        var writtenNodeIds = new HashSet<long>();
        foreach (var feature in document.Features.Where(static item => item.GeometryType == MapGeometryType.Point)) {
            ct.ThrowIfCancellationRequested();
            foreach (var part in feature.Parts.Where(static part => part.Count > 0)) {
                var id = feature.Osm is { PrimitiveType: OsmPrimitiveType.Node } metadata
                    ? metadata.Id
                    : nextNodeId--;
                if (writtenNodeIds.Add(id)) {
                    WriteNode(writer, id, feature.Osm?.Version ?? 1, part[0], feature.Attributes);
                }
            }
        }

        foreach (var feature in document.Features.Where(static item => item.GeometryType != MapGeometryType.Point)) {
            ct.ThrowIfCancellationRequested();
            foreach (var part in feature.Parts.Where(static part => part.Count > 1)) {
                var nodeIds = new List<long>(part.Count);
                var originalNodes = feature.Osm?.NodeReferences;
                for (var i = 0; i < part.Count; i++) {
                    var point = part[i];
                    if (i == part.Count - 1 && point == part[0] && nodeIds.Count > 0) {
                        nodeIds.Add(nodeIds[0]);
                        continue;
                    }
                    var originalNode = originalNodes is not null && i < originalNodes.Count &&
                        originalNodes[i].Point == point
                            ? originalNodes[i]
                            : null;
                    var nodeId = originalNode?.Id ?? nextNodeId--;
                    nodeIds.Add(nodeId);
                    if (writtenNodeIds.Add(nodeId)) {
                        WriteNode(writer, nodeId, originalNode?.Version ?? 1, point, null);
                    }
                }

                writer.WriteStartElement("way");
                var wayId = feature.Osm is { PrimitiveType: OsmPrimitiveType.Way } metadata
                    ? metadata.Id
                    : nextWayId--;
                writer.WriteAttributeString("id", wayId.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("version", (feature.Osm?.Version ?? 1).ToString(CultureInfo.InvariantCulture));
                foreach (var nodeId in nodeIds) {
                    writer.WriteStartElement("nd");
                    writer.WriteAttributeString("ref", nodeId.ToString(CultureInfo.InvariantCulture));
                    writer.WriteEndElement();
                }
                WriteTags(writer, feature.Attributes);
                writer.WriteEndElement();
            }
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static MapDocument Read(
        OsmStreamSource source,
        SpatialImportOptions options,
        IProgress<SpatialImportProgress>? progress,
        CancellationToken ct,
        string stage) {
        var document = new MapDocument();
        var nodes = new Dictionary<long, OsmNodeReference>();
        var count = 0;

        foreach (var item in source) {
            ct.ThrowIfCancellationRequested();
            count++;
            switch (item) {
                case Node node when node.Id.HasValue && node.Longitude.HasValue && node.Latitude.HasValue:
                    if (nodes.Count >= options.MaxCoordinates) {
                        throw new SpatialDataLimitException($"OSM 文件超过安全导入上限 {options.MaxCoordinates:N0} 个节点。请下载或裁剪更小的区域。");
                    }
                    var point = new GeoPoint(node.Longitude.Value, node.Latitude.Value);
                    if (!point.IsValid) break;
                    nodes[node.Id.Value] = new OsmNodeReference(node.Id.Value, node.Version ?? 1, point);
                    if (node.Tags?.Count > 0) {
                        AddFeature(document, options, new MapFeature {
                            Id = $"osm-node-{node.Id.Value}",
                            GeometryType = MapGeometryType.Point,
                            Parts = [[point]],
                            Attributes = ReadTags(node.Tags),
                            Osm = new OsmFeatureMetadata {
                                PrimitiveType = OsmPrimitiveType.Node,
                                Id = node.Id.Value,
                                Version = node.Version ?? 1
                            }
                        });
                    }
                    break;
                case Way way when way.Id.HasValue && way.Nodes is { Length: > 1 }:
                    var nodeReferences = new List<OsmNodeReference>(way.Nodes.Length);
                    var points = new List<GeoPoint>(way.Nodes.Length);
                    foreach (var nodeId in way.Nodes) {
                        if (!nodes.TryGetValue(nodeId, out var nodeReference)) continue;

                        nodeReferences.Add(nodeReference);
                        points.Add(nodeReference.Point);
                    }
                    if (points.Count < 2) {
                        document.SkippedFeatureCount++;
                        break;
                    }
                    var attributes = ReadTags(way.Tags);
                    var isClosed = points.Count > 3 && points[0] == points[^1];
                    AddFeature(document, options, new MapFeature {
                        Id = $"osm-way-{way.Id.Value}",
                        GeometryType = isClosed ? MapGeometryType.Polygon : MapGeometryType.LineString,
                        Parts = [points],
                        Attributes = attributes,
                        Osm = new OsmFeatureMetadata {
                            PrimitiveType = OsmPrimitiveType.Way,
                            Id = way.Id.Value,
                            Version = way.Version ?? 1,
                            NodeReferences = nodeReferences
                        }
                    });
                    break;
                case Relation:
                    document.SkippedFeatureCount++;
                    break;
            }

            if (count % 10_000 == 0) {
                progress?.Report(new SpatialImportProgress(document.Features.Count, stage));
            }
        }

        return document;
    }

    private static Dictionary<string, string> ReadTags(OsmSharp.Tags.TagsCollectionBase? tags) {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (tags is null) return attributes;
        foreach (var tag in tags) attributes[tag.Key] = tag.Value;
        return attributes;
    }

    private static void AddFeature(MapDocument document, SpatialImportOptions options, MapFeature feature) {
        if (document.Features.Count >= options.MaxFeatures) {
            throw new SpatialDataLimitException($"OSM 文件超过安全导入上限 {options.MaxFeatures:N0} 个要素。请下载或裁剪更小的区域。");
        }
        document.Features.Add(feature);
    }

    private static void WriteNode(
        XmlWriter writer,
        long id,
        int version,
        GeoPoint point,
        IReadOnlyDictionary<string, string>? attributes) {
        writer.WriteStartElement("node");
        writer.WriteAttributeString("id", id.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("version", version.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("lat", point.Latitude.ToString("R", CultureInfo.InvariantCulture));
        writer.WriteAttributeString("lon", point.Longitude.ToString("R", CultureInfo.InvariantCulture));
        WriteTags(writer, attributes);
        writer.WriteEndElement();
    }

    private static void WriteTags(XmlWriter writer, IReadOnlyDictionary<string, string>? attributes) {
        if (attributes is null) return;
        foreach (var attribute in attributes.OrderBy(static item => item.Key, StringComparer.Ordinal)) {
            writer.WriteStartElement("tag");
            writer.WriteAttributeString("k", attribute.Key);
            writer.WriteAttributeString("v", attribute.Value);
            writer.WriteEndElement();
        }
    }
}
