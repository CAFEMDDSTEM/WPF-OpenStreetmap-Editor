using System.Globalization;
using System.IO;
using System.Xml.Linq;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed record OsmObjectReference(string Type, long OldId, MapFeature Feature);

public sealed record OsmWayNodePlan(MapFeature Feature, IReadOnlyList<long> NodeIds);

public sealed record OsmChangeBuildResult(
    string Xml,
    int CreateCount,
    int ModifyCount,
    int DeleteCount,
    IReadOnlyList<OsmObjectReference> References,
    IReadOnlyList<OsmWayNodePlan> WayNodePlans) {
    public int TotalCount => CreateCount + ModifyCount + DeleteCount;
}

public static class OsmChangeSerializer {
    public static OsmChangeBuildResult Build(MapDocument document, long changesetId) {
        ArgumentNullException.ThrowIfNull(document);
        var create = new XElement("create");
        var modify = new XElement("modify");
        var delete = new XElement("delete", new XAttribute("if-unused", "true"));
        var references = new List<OsmObjectReference>();
        var wayNodePlans = new List<OsmWayNodePlan>();
        var nextNodeId = -1L;
        var nextWayId = -1L;
        var createCount = 0;
        var modifyCount = 0;
        var deleteCount = 0;

        foreach (var feature in document.Features) {
            ValidateFeature(feature);
            if (feature.Osm is null) {
                WriteCreatedFeature(create, feature, changesetId, ref nextNodeId, ref nextWayId, references, wayNodePlans);
                createCount++;
                continue;
            }

            if (document.OriginalFeatures.TryGetValue(feature.Id, out var original) && FeaturesEqual(feature, original)) continue;
            WriteModifiedFeature(create, modify, feature, changesetId, ref nextNodeId, references, wayNodePlans);
            modifyCount++;
        }

        foreach (var feature in document.GetDeletedOriginalFeatures().Where(static item => item.Osm is not null)) {
            var metadata = feature.Osm!;
            delete.Add(new XElement(
                metadata.PrimitiveType == OsmPrimitiveType.Node ? "node" : "way",
                new XAttribute("id", metadata.Id),
                new XAttribute("version", metadata.Version),
                new XAttribute("changeset", changesetId)));
            deleteCount++;
        }

        var root = new XElement(
            "osmChange",
            new XAttribute("version", "0.6"),
            new XAttribute("generator", "WPF-OpenStreetmap-Editor"));
        if (create.HasElements) root.Add(create);
        if (modify.HasElements) root.Add(modify);
        if (delete.HasElements) root.Add(delete);
        return new OsmChangeBuildResult(
            new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString(),
            createCount,
            modifyCount,
            deleteCount,
            references,
            wayNodePlans);
    }

    public static void ApplyDiffResult(MapDocument document, OsmChangeBuildResult build, string responseXml) {
        var response = XDocument.Parse(responseXml, LoadOptions.None);
        var diffMappings = response.Root?.Elements()
            .Select(element => new {
                Type = element.Name.LocalName,
                OldId = ParseLongAttribute(element, "old_id"),
                NewId = ParseLongAttribute(element, "new_id"),
                NewVersion = ParseIntAttribute(element, "new_version")
            })
            .Where(static item => item.OldId.HasValue && item.NewId.HasValue && item.NewVersion.HasValue)
            .ToDictionary(
                item => (item.Type, item.OldId!.Value),
                item => (Id: item.NewId!.Value, Version: item.NewVersion!.Value)) ?? [];
        var references = build.References.ToDictionary(
            reference => (reference.Type, reference.OldId),
            reference => reference);
        foreach (var (key, mapping) in diffMappings) {
            if (!references.TryGetValue(key, out var reference)) continue;
            var existingNodeReferences = reference.Feature.Osm?.NodeReferences.ToList() ?? [];
            reference.Feature.Osm = new OsmFeatureMetadata {
                PrimitiveType = key.Type == "node" ? OsmPrimitiveType.Node : OsmPrimitiveType.Way,
                Id = mapping.Id,
                Version = mapping.Version,
                NodeReferences = existingNodeReferences
            };
        }
        foreach (var plan in build.WayNodePlans) {
            if (plan.Feature.Osm is not { PrimitiveType: OsmPrimitiveType.Way } metadata) continue;
            var oldReferences = metadata.NodeReferences
                .GroupBy(static reference => reference.Id)
                .ToDictionary(static group => group.Key, static group => group.First());
            var updatedReferences = new List<OsmNodeReference>(plan.NodeIds.Count);
            for (var i = 0; i < plan.NodeIds.Count; i++) {
                var oldId = plan.NodeIds[i];
                (long Id, int Version)? created = null;
                if (oldId < 0) {
                    if (!diffMappings.TryGetValue(("node", oldId), out var mapping)) {
                        throw new InvalidDataException($"OSM API 未返回新节点 {oldId} 的映射。");
                    }
                    created = mapping;
                }
                var id = created?.Id ?? oldId;
                var version = created?.Version ?? oldReferences.GetValueOrDefault(oldId)?.Version ?? 1;
                updatedReferences.Add(new OsmNodeReference(id, version, plan.Feature.Parts[0][i]));
            }
            metadata.NodeReferences = updatedReferences;
        }
        document.MarkClean();
    }

    private static void WriteCreatedFeature(
        XElement create,
        MapFeature feature,
        long changesetId,
        ref long nextNodeId,
        ref long nextWayId,
        List<OsmObjectReference> references,
        List<OsmWayNodePlan> wayNodePlans) {
        if (feature.GeometryType == MapGeometryType.Point) {
            var id = nextNodeId--;
            create.Add(CreateNode(id, 1, changesetId, feature.Parts[0][0], feature.Attributes));
            references.Add(new OsmObjectReference("node", id, feature));
            return;
        }

        var nodeIds = WriteWayNodes(create, feature.Parts[0], null, changesetId, ref nextNodeId);
        var wayId = nextWayId--;
        create.Add(CreateWay(wayId, 1, changesetId, nodeIds, feature.Attributes));
        references.Add(new OsmObjectReference("way", wayId, feature));
        wayNodePlans.Add(new OsmWayNodePlan(feature, nodeIds));
    }

    private static void WriteModifiedFeature(
        XElement create,
        XElement modify,
        MapFeature feature,
        long changesetId,
        ref long nextNodeId,
        List<OsmObjectReference> references,
        List<OsmWayNodePlan> wayNodePlans) {
        var metadata = feature.Osm!;
        if (metadata.PrimitiveType == OsmPrimitiveType.Node && feature.GeometryType == MapGeometryType.Point) {
            modify.Add(CreateNode(metadata.Id, metadata.Version, changesetId, feature.Parts[0][0], feature.Attributes));
            references.Add(new OsmObjectReference("node", metadata.Id, feature));
            return;
        }
        if (metadata.PrimitiveType != OsmPrimitiveType.Way || feature.GeometryType == MapGeometryType.Point) {
            throw new InvalidDataException($"要素 {feature.Id} 的 OSM 类型与当前几何不匹配。");
        }

        var nodeIds = WriteWayNodes(
            create,
            feature.Parts[0],
            metadata.NodeReferences,
            changesetId,
            ref nextNodeId);
        modify.Add(CreateWay(metadata.Id, metadata.Version, changesetId, nodeIds, feature.Attributes));
        references.Add(new OsmObjectReference("way", metadata.Id, feature));
        wayNodePlans.Add(new OsmWayNodePlan(feature, nodeIds));
    }

    private static List<long> WriteWayNodes(
        XElement create,
        IReadOnlyList<GeoPoint> points,
        IReadOnlyList<OsmNodeReference>? originalNodes,
        long changesetId,
        ref long nextNodeId) {
        var nodeIds = new List<long>(points.Count);
        long? firstNodeId = null;
        for (var i = 0; i < points.Count; i++) {
            if (i == points.Count - 1 && points.Count > 2 && points[i] == points[0] && firstNodeId.HasValue) {
                nodeIds.Add(firstNodeId.Value);
                continue;
            }
            if (originalNodes is not null && i < originalNodes.Count && points[i] == originalNodes[i].Point) {
                var existingNodeId = originalNodes[i].Id;
                firstNodeId ??= existingNodeId;
                nodeIds.Add(existingNodeId);
                continue;
            }
            var nodeId = nextNodeId--;
            firstNodeId ??= nodeId;
            nodeIds.Add(nodeId);
            create.Add(CreateNode(nodeId, 1, changesetId, points[i], null));
        }
        return nodeIds;
    }

    private static XElement CreateNode(
        long id,
        int version,
        long changesetId,
        GeoPoint point,
        IReadOnlyDictionary<string, string>? attributes) {
        var node = new XElement(
            "node",
            new XAttribute("id", id),
            new XAttribute("version", version),
            new XAttribute("changeset", changesetId),
            new XAttribute("lat", point.Latitude.ToString("R", CultureInfo.InvariantCulture)),
            new XAttribute("lon", point.Longitude.ToString("R", CultureInfo.InvariantCulture)));
        AddTags(node, attributes);
        return node;
    }

    private static XElement CreateWay(
        long id,
        int version,
        long changesetId,
        IEnumerable<long> nodeIds,
        IReadOnlyDictionary<string, string> attributes) {
        var way = new XElement(
            "way",
            new XAttribute("id", id),
            new XAttribute("version", version),
            new XAttribute("changeset", changesetId));
        way.Add(nodeIds.Select(nodeId => new XElement("nd", new XAttribute("ref", nodeId))));
        AddTags(way, attributes);
        return way;
    }

    private static void AddTags(XElement element, IReadOnlyDictionary<string, string>? attributes) {
        if (attributes is null) return;
        element.Add(attributes
            .Where(static attribute => !attribute.Key.StartsWith('_') && !string.IsNullOrEmpty(attribute.Key))
            .OrderBy(static attribute => attribute.Key, StringComparer.Ordinal)
            .Select(attribute => new XElement("tag", new XAttribute("k", attribute.Key), new XAttribute("v", attribute.Value))));
    }

    private static bool FeaturesEqual(MapFeature left, MapFeature right) {
        if (left.GeometryType != right.GeometryType || left.Parts.Count != right.Parts.Count ||
            left.Attributes.Count != right.Attributes.Count) return false;
        for (var i = 0; i < left.Parts.Count; i++) {
            if (!left.Parts[i].SequenceEqual(right.Parts[i])) return false;
        }
        return left.Attributes.OrderBy(static item => item.Key, StringComparer.Ordinal)
            .SequenceEqual(right.Attributes.OrderBy(static item => item.Key, StringComparer.Ordinal));
    }

    private static long? ParseLongAttribute(XElement element, string name) {
        return long.TryParse(element.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static int? ParseIntAttribute(XElement element, string name) {
        return int.TryParse(element.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static void ValidateFeature(MapFeature feature) {
        if (feature.Parts.Count != 1) {
            throw new InvalidDataException($"要素 {feature.Id} 是多部件几何，请拆分后再上传 OSM。");
        }
        var minimumPoints = feature.GeometryType == MapGeometryType.Point ? 1 : 2;
        if (feature.Parts[0].Count < minimumPoints || feature.Parts[0].Any(static point => !point.IsValid)) {
            throw new InvalidDataException($"要素 {feature.Id} 的几何无效。");
        }
    }
}
