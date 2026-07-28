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
        var dataset = OsmDocumentSync.Synchronize(document);
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

        foreach (var node in dataset.Nodes.Values.OrderBy(static node => node.Id)) {
            ct.ThrowIfCancellationRequested();
            WriteNode(writer, node.Id, node.Version, node.Point, node.Tags);
        }

        foreach (var way in dataset.Ways.Values.OrderBy(static way => way.Id)) {
            ct.ThrowIfCancellationRequested();
            WriteWay(writer, way);
        }

        foreach (var relation in dataset.Relations.Values.OrderBy(static relation => relation.Id)) {
            ct.ThrowIfCancellationRequested();
            WriteRelation(writer, relation);
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
        var dataset = new OsmDataset();
        document.Osm = dataset;
        var count = 0;

        foreach (var item in source) {
            ct.ThrowIfCancellationRequested();
            count++;
            switch (item) {
                case Node node when node.Id.HasValue && node.Longitude.HasValue && node.Latitude.HasValue:
                    if (dataset.Nodes.Count >= options.MaxCoordinates) {
                        throw new SpatialDataLimitException($"OSM 文件超过安全导入上限 {options.MaxCoordinates:N0} 个节点。请下载或裁剪更小的区域。");
                    }
                    var point = new GeoPoint(node.Longitude.Value, node.Latitude.Value);
                    if (!point.IsValid) break;
                    var osmNode = new OsmNode {
                        Id = node.Id.Value,
                        Version = node.Version ?? 1,
                        Point = point,
                        Tags = ReadTags(node.Tags)
                    };
                    dataset.Nodes[osmNode.Id] = osmNode;
                    if (osmNode.Tags.Count > 0) AddFeature(document, options, OsmDocumentSync.CreateNodeFeature(osmNode));
                    break;
                case Way way when way.Id.HasValue && way.Nodes is { Length: > 1 }:
                    var osmWay = new OsmWay {
                        Id = way.Id.Value,
                        Version = way.Version ?? 1,
                        NodeIds = way.Nodes.ToList(),
                        Tags = ReadTags(way.Tags)
                    };
                    dataset.Ways[osmWay.Id] = osmWay;
                    break;
                case Relation relation when relation.Id.HasValue:
                    dataset.Relations[relation.Id.Value] = new OsmRelation {
                        Id = relation.Id.Value,
                        Version = relation.Version ?? 1,
                        Members = ReadMembers(relation.Members),
                        Tags = ReadTags(relation.Tags)
                    };
                    break;
            }

            if (count % 10_000 == 0) {
                progress?.Report(new SpatialImportProgress(document.Features.Count, stage));
            }
        }

        foreach (var way in dataset.Ways.Values.OrderBy(static way => way.Id)) {
            var feature = OsmDocumentSync.CreateWayFeature(dataset, way);
            if (feature is null) {
                document.SkippedFeatureCount++;
                continue;
            }
            AddFeature(document, options, feature);
        }

        foreach (var relation in dataset.Relations.Values.OrderBy(static relation => relation.Id)) {
            var feature = OsmDocumentSync.CreateRelationFeature(dataset, relation);
            if (feature is not null) AddFeature(document, options, feature);
        }
        dataset.NormalizeTemporaryIds();
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

    private static void WriteWay(XmlWriter writer, OsmWay way) {
        writer.WriteStartElement("way");
        writer.WriteAttributeString("id", way.Id.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("version", way.Version.ToString(CultureInfo.InvariantCulture));
        foreach (var nodeId in way.NodeIds) {
            writer.WriteStartElement("nd");
            writer.WriteAttributeString("ref", nodeId.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }
        WriteTags(writer, way.Tags);
        writer.WriteEndElement();
    }

    private static void WriteRelation(XmlWriter writer, OsmRelation relation) {
        writer.WriteStartElement("relation");
        writer.WriteAttributeString("id", relation.Id.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("version", relation.Version.ToString(CultureInfo.InvariantCulture));
        foreach (var member in relation.Members) {
            writer.WriteStartElement("member");
            writer.WriteAttributeString("type", FormatMemberType(member.Type));
            writer.WriteAttributeString("ref", member.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("role", member.Role);
            writer.WriteEndElement();
        }
        WriteTags(writer, relation.Tags);
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

    private static List<OsmRelationMember> ReadMembers(RelationMember[]? members) {
        if (members is null || members.Length == 0) return [];

        var result = new List<OsmRelationMember>(members.Length);
        foreach (var member in members) {
            if (TryConvertMemberType(member.Type, out var type)) {
                result.Add(new OsmRelationMember(type, member.Id, member.Role ?? ""));
            }
        }

        return result;
    }

    private static bool TryConvertMemberType(OsmGeoType type, out OsmRelationMemberType memberType) {
        switch (type) {
            case OsmGeoType.Node:
                memberType = OsmRelationMemberType.Node;
                return true;
            case OsmGeoType.Way:
                memberType = OsmRelationMemberType.Way;
                return true;
            case OsmGeoType.Relation:
                memberType = OsmRelationMemberType.Relation;
                return true;
            default:
                memberType = default;
                return false;
        }
    }

    private static string FormatMemberType(OsmRelationMemberType type) {
        return type switch {
            OsmRelationMemberType.Node => "node",
            OsmRelationMemberType.Way => "way",
            OsmRelationMemberType.Relation => "relation",
            _ => throw new InvalidDataException($"Unsupported OSM relation member type: {type}.")
        };
    }
}
