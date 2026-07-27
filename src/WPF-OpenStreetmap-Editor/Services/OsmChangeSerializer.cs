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
    IReadOnlyList<OsmWayNodePlan> WayNodePlans,
    OsmDataset Dataset) {
    public int TotalCount => CreateCount + ModifyCount + DeleteCount;
}

public static class OsmChangeSerializer {
    public static OsmChangeBuildResult Build(MapDocument document, long changesetId) {
        ArgumentNullException.ThrowIfNull(document);
        foreach (var feature in document.Features) ValidateFeature(feature);

        var current = OsmDocumentSync.Synchronize(document);
        var original = GetOriginalDataset(document);
        var create = new XElement("create");
        var modify = new XElement("modify");
        var delete = new XElement("delete", new XAttribute("if-unused", "true"));

        var createCount = WriteCreates(create, current, original, changesetId);
        var modifyCount = WriteModifies(modify, current, original, changesetId);
        var deleteCount = WriteDeletes(delete, current, original, changesetId);
        var root = new XElement(
            "osmChange",
            new XAttribute("version", "0.6"),
            new XAttribute("generator", "WPF-OpenStreetmap-Editor"));
        if (create.HasElements) root.Add(create);
        if (modify.HasElements) root.Add(modify);
        if (delete.HasElements) root.Add(delete);

        var references = CreateFeatureReferences(document);
        var wayNodePlans = CreateWayNodePlans(document, current);
        return new OsmChangeBuildResult(
            new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString(),
            createCount,
            modifyCount,
            deleteCount,
            references,
            wayNodePlans,
            current.Clone());
    }

    public static void ApplyDiffResult(MapDocument document, OsmChangeBuildResult build, string responseXml) {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(build);

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

        var dataset = build.Dataset.Clone();
        ApplyMappingsToDataset(dataset, diffMappings);
        EnsureNoUnmappedTemporaryIds(dataset);
        document.Osm = dataset;
        UpdateFeatureMetadata(document, dataset, diffMappings);
        document.MarkClean();
    }

    private static OsmDataset GetOriginalDataset(MapDocument document) {
        return document.OriginalOsm?.Clone() ??
            OsmDocumentSync.CreateDatasetFromFeatures(document.OriginalFeatures.Values);
    }

    private static int WriteCreates(XElement create, OsmDataset current, OsmDataset original, long changesetId) {
        var count = 0;
        foreach (var node in current.Nodes.Values
            .Where(static node => node.Id < 0)
            .OrderBy(static node => node.Id)) {
            create.Add(CreateNode(node.Id, node.Version, changesetId, node.Point, node.Tags));
            count++;
        }

        foreach (var way in current.Ways.Values
            .Where(static way => way.Id < 0)
            .OrderBy(static way => way.Id)) {
            create.Add(CreateWay(way.Id, way.Version, changesetId, way.NodeIds, way.Tags));
            count++;
        }

        foreach (var relation in current.Relations.Values
            .Where(static relation => relation.Id < 0)
            .OrderBy(static relation => relation.Id)) {
            create.Add(CreateRelation(relation.Id, relation.Version, changesetId, relation.Members, relation.Tags));
            count++;
        }

        return count;
    }

    private static int WriteModifies(XElement modify, OsmDataset current, OsmDataset original, long changesetId) {
        var count = 0;
        foreach (var node in current.Nodes.Values
            .Where(node => node.Id >= 0 &&
                (!original.Nodes.TryGetValue(node.Id, out var originalNode) ||
                    !NodesEqual(node, originalNode)))
            .OrderBy(static node => node.Id)) {
            modify.Add(CreateNode(node.Id, node.Version, changesetId, node.Point, node.Tags));
            count++;
        }

        foreach (var way in current.Ways.Values
            .Where(way => way.Id >= 0 &&
                (!original.Ways.TryGetValue(way.Id, out var originalWay) ||
                    !WaysEqual(way, originalWay)))
            .OrderBy(static way => way.Id)) {
            modify.Add(CreateWay(way.Id, way.Version, changesetId, way.NodeIds, way.Tags));
            count++;
        }

        foreach (var relation in current.Relations.Values
            .Where(relation => relation.Id >= 0 &&
                (!original.Relations.TryGetValue(relation.Id, out var originalRelation) ||
                    !RelationsEqual(relation, originalRelation)))
            .OrderBy(static relation => relation.Id)) {
            modify.Add(CreateRelation(relation.Id, relation.Version, changesetId, relation.Members, relation.Tags));
            count++;
        }

        return count;
    }

    private static int WriteDeletes(XElement delete, OsmDataset current, OsmDataset original, long changesetId) {
        var count = 0;
        foreach (var relation in original.Relations.Values
            .Where(relation => !current.Relations.ContainsKey(relation.Id))
            .OrderByDescending(static relation => relation.Id)) {
            delete.Add(CreateDeletedPrimitive("relation", relation.Id, relation.Version, changesetId));
            count++;
        }

        foreach (var way in original.Ways.Values
            .Where(way => !current.Ways.ContainsKey(way.Id))
            .OrderByDescending(static way => way.Id)) {
            delete.Add(CreateDeletedPrimitive("way", way.Id, way.Version, changesetId));
            count++;
        }

        foreach (var node in original.Nodes.Values
            .Where(node => !current.Nodes.ContainsKey(node.Id))
            .OrderByDescending(static node => node.Id)) {
            delete.Add(CreateDeletedPrimitive("node", node.Id, node.Version, changesetId));
            count++;
        }

        return count;
    }

    private static IReadOnlyList<OsmObjectReference> CreateFeatureReferences(MapDocument document) {
        return document.Features
            .Where(static feature => feature.Osm is not null)
            .Select(static feature => new OsmObjectReference(
                FormatPrimitiveType(feature.Osm!.PrimitiveType),
                feature.Osm.Id,
                feature))
            .ToList();
    }

    private static IReadOnlyList<OsmWayNodePlan> CreateWayNodePlans(MapDocument document, OsmDataset dataset) {
        return document.Features
            .Where(static feature => feature.Osm?.PrimitiveType == OsmPrimitiveType.Way)
            .Select(feature => dataset.Ways.TryGetValue(feature.Osm!.Id, out var way)
                ? new OsmWayNodePlan(feature, way.NodeIds.ToList())
                : null)
            .OfType<OsmWayNodePlan>()
            .ToList();
    }

    private static void ApplyMappingsToDataset(
        OsmDataset dataset,
        IReadOnlyDictionary<(string Type, long OldId), (long Id, int Version)> mappings) {
        var nodeIdMap = ApplyNodeMappings(dataset, mappings);
        var wayIdMap = ApplyWayMappings(dataset, mappings);
        var relationIdMap = ApplyRelationMappings(dataset, mappings);

        foreach (var way in dataset.Ways.Values) {
            way.NodeIds = way.NodeIds
                .Select(nodeId => nodeIdMap.GetValueOrDefault(nodeId, nodeId))
                .ToList();
        }

        foreach (var relation in dataset.Relations.Values) {
            relation.Members = relation.Members
                .Select(member => member.Type switch {
                    OsmRelationMemberType.Node => member with { Id = nodeIdMap.GetValueOrDefault(member.Id, member.Id) },
                    OsmRelationMemberType.Way => member with { Id = wayIdMap.GetValueOrDefault(member.Id, member.Id) },
                    OsmRelationMemberType.Relation => member with { Id = relationIdMap.GetValueOrDefault(member.Id, member.Id) },
                    _ => member
                })
                .ToList();
        }

        dataset.NormalizeTemporaryIds();
    }

    private static Dictionary<long, long> ApplyNodeMappings(
        OsmDataset dataset,
        IReadOnlyDictionary<(string Type, long OldId), (long Id, int Version)> mappings) {
        var idMap = new Dictionary<long, long>();
        foreach (var oldId in dataset.Nodes.Keys.ToList()) {
            if (!mappings.TryGetValue(("node", oldId), out var mapping)) continue;

            var node = dataset.Nodes[oldId];
            dataset.Nodes.Remove(oldId);
            node.Id = mapping.Id;
            node.Version = mapping.Version;
            dataset.Nodes[node.Id] = node;
            idMap[oldId] = node.Id;
        }

        return idMap;
    }

    private static Dictionary<long, long> ApplyWayMappings(
        OsmDataset dataset,
        IReadOnlyDictionary<(string Type, long OldId), (long Id, int Version)> mappings) {
        var idMap = new Dictionary<long, long>();
        foreach (var oldId in dataset.Ways.Keys.ToList()) {
            if (!mappings.TryGetValue(("way", oldId), out var mapping)) continue;

            var way = dataset.Ways[oldId];
            dataset.Ways.Remove(oldId);
            way.Id = mapping.Id;
            way.Version = mapping.Version;
            dataset.Ways[way.Id] = way;
            idMap[oldId] = way.Id;
        }

        return idMap;
    }

    private static Dictionary<long, long> ApplyRelationMappings(
        OsmDataset dataset,
        IReadOnlyDictionary<(string Type, long OldId), (long Id, int Version)> mappings) {
        var idMap = new Dictionary<long, long>();
        foreach (var oldId in dataset.Relations.Keys.ToList()) {
            if (!mappings.TryGetValue(("relation", oldId), out var mapping)) continue;

            var relation = dataset.Relations[oldId];
            dataset.Relations.Remove(oldId);
            relation.Id = mapping.Id;
            relation.Version = mapping.Version;
            dataset.Relations[relation.Id] = relation;
            idMap[oldId] = relation.Id;
        }

        return idMap;
    }

    private static void EnsureNoUnmappedTemporaryIds(OsmDataset dataset) {
        var temporaryNodeId = dataset.Nodes.Keys.FirstOrDefault(static id => id < 0);
        if (temporaryNodeId < 0) throw new InvalidDataException($"OSM API did not return a mapping for new node {temporaryNodeId}.");

        var temporaryWayId = dataset.Ways.Keys.FirstOrDefault(static id => id < 0);
        if (temporaryWayId < 0) throw new InvalidDataException($"OSM API did not return a mapping for new way {temporaryWayId}.");

        var temporaryRelationId = dataset.Relations.Keys.FirstOrDefault(static id => id < 0);
        if (temporaryRelationId < 0) throw new InvalidDataException($"OSM API did not return a mapping for new relation {temporaryRelationId}.");
    }

    private static void UpdateFeatureMetadata(
        MapDocument document,
        OsmDataset dataset,
        IReadOnlyDictionary<(string Type, long OldId), (long Id, int Version)> mappings) {
        foreach (var feature in document.Features) {
            if (feature.Osm is null) continue;

            var typeName = FormatPrimitiveType(feature.Osm.PrimitiveType);
            if (mappings.TryGetValue((typeName, feature.Osm.Id), out var mapping)) {
                feature.Osm.Id = mapping.Id;
                feature.Osm.Version = mapping.Version;
            }

            switch (feature.Osm.PrimitiveType) {
                case OsmPrimitiveType.Node when dataset.Nodes.TryGetValue(feature.Osm.Id, out var node):
                    feature.Osm.Version = node.Version;
                    if (feature.Parts.Count == 1 && feature.Parts[0].Count > 0) feature.Parts[0][0] = node.Point;
                    feature.Attributes.Clear();
                    foreach (var tag in node.Tags) feature.Attributes[tag.Key] = tag.Value;
                    feature.InvalidateGeometry();
                    break;
                case OsmPrimitiveType.Way when dataset.Ways.TryGetValue(feature.Osm.Id, out var way):
                    feature.Osm.Version = way.Version;
                    feature.Osm.NodeReferences = OsmDocumentSync.CreateNodeReferences(dataset, way.NodeIds);
                    if (feature.Osm.NodeReferences.Count >= 2) {
                        feature.Parts.Clear();
                        feature.Parts.Add(feature.Osm.NodeReferences.Select(static reference => reference.Point).ToList());
                        feature.GeometryType = feature.Osm.NodeReferences.Count > 3 &&
                            feature.Osm.NodeReferences[0].Point == feature.Osm.NodeReferences[^1].Point
                                ? MapGeometryType.Polygon
                                : MapGeometryType.LineString;
                        feature.InvalidateGeometry();
                    }
                    feature.Attributes.Clear();
                    foreach (var tag in way.Tags) feature.Attributes[tag.Key] = tag.Value;
                    break;
                case OsmPrimitiveType.Relation when dataset.Relations.TryGetValue(feature.Osm.Id, out var relation):
                    feature.Osm.Version = relation.Version;
                    feature.Attributes.Clear();
                    foreach (var tag in relation.Tags) feature.Attributes[tag.Key] = tag.Value;
                    break;
            }
        }

        document.InvalidateSpatialIndex();
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

    private static XElement CreateRelation(
        long id,
        int version,
        long changesetId,
        IEnumerable<OsmRelationMember> members,
        IReadOnlyDictionary<string, string> attributes) {
        var relation = new XElement(
            "relation",
            new XAttribute("id", id),
            new XAttribute("version", version),
            new XAttribute("changeset", changesetId));
        relation.Add(members.Select(member => new XElement(
            "member",
            new XAttribute("type", FormatMemberType(member.Type)),
            new XAttribute("ref", member.Id),
            new XAttribute("role", member.Role))));
        AddTags(relation, attributes);
        return relation;
    }

    private static XElement CreateDeletedPrimitive(string type, long id, int version, long changesetId) {
        return new XElement(
            type,
            new XAttribute("id", id),
            new XAttribute("version", version),
            new XAttribute("changeset", changesetId));
    }

    private static void AddTags(XElement element, IReadOnlyDictionary<string, string>? attributes) {
        if (attributes is null) return;
        element.Add(attributes
            .Where(static attribute => !attribute.Key.StartsWith('_') && !string.IsNullOrEmpty(attribute.Key))
            .OrderBy(static attribute => attribute.Key, StringComparer.Ordinal)
            .Select(attribute => new XElement("tag", new XAttribute("k", attribute.Key), new XAttribute("v", attribute.Value))));
    }

    private static bool NodesEqual(OsmNode left, OsmNode right) {
        return left.Version == right.Version &&
            left.Point == right.Point &&
            TagsEqual(left.Tags, right.Tags);
    }

    private static bool WaysEqual(OsmWay left, OsmWay right) {
        return left.Version == right.Version &&
            left.NodeIds.SequenceEqual(right.NodeIds) &&
            TagsEqual(left.Tags, right.Tags);
    }

    private static bool RelationsEqual(OsmRelation left, OsmRelation right) {
        return left.Version == right.Version &&
            left.Members.SequenceEqual(right.Members) &&
            TagsEqual(left.Tags, right.Tags);
    }

    private static bool TagsEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) {
        return left.Count == right.Count &&
            left.OrderBy(static item => item.Key, StringComparer.Ordinal)
                .SequenceEqual(right.OrderBy(static item => item.Key, StringComparer.Ordinal));
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
        if (feature.Osm?.PrimitiveType == OsmPrimitiveType.Relation) return;

        if (feature.Parts.Count != 1) {
            throw new InvalidDataException($"Feature {feature.Id} has multipart geometry. Split it before uploading to OSM.");
        }

        var minimumPoints = feature.GeometryType == MapGeometryType.Point ? 1 : 2;
        if (feature.Parts[0].Count < minimumPoints || feature.Parts[0].Any(static point => !point.IsValid)) {
            throw new InvalidDataException($"Feature {feature.Id} has invalid geometry.");
        }
    }

    private static string FormatPrimitiveType(OsmPrimitiveType type) {
        return type switch {
            OsmPrimitiveType.Node => "node",
            OsmPrimitiveType.Way => "way",
            OsmPrimitiveType.Relation => "relation",
            _ => throw new InvalidDataException($"Unsupported OSM primitive type: {type}.")
        };
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
