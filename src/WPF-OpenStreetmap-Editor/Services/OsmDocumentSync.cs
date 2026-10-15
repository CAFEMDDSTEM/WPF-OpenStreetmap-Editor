using System.IO;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

internal static class OsmDocumentSync {
    public static OsmDataset Synchronize(MapDocument document) {
        ArgumentNullException.ThrowIfNull(document);

        var dataset = document.Osm?.Clone() ?? new OsmDataset();
        dataset.NormalizeTemporaryIds();
        var referencedBeforeEdits = GetReferencedNodeIdsBeforeEdits(document, dataset);
        ApplyDeletedFeatures(document, dataset);
        var nodeIndex = new NodeCoordinateIndex(dataset);
        foreach (var feature in OrderFeaturesForSync(document)) {
            ApplyFeature(document, dataset, nodeIndex, feature);
        }
        PruneUnreferencedTemporaryObjects(document, dataset);
        PruneNodesDetachedByEdits(document, dataset, referencedBeforeEdits);
        dataset.NormalizeTemporaryIds();
        RefreshFeatureGeometries(document, dataset);
        document.Osm = dataset;
        return dataset;
    }

    public static OsmDataset CreateDatasetFromFeatures(IEnumerable<MapFeature> features) {
        ArgumentNullException.ThrowIfNull(features);

        var dataset = new OsmDataset();
        foreach (var feature in features) {
            if (feature.Osm is null) continue;

            switch (feature.Osm.PrimitiveType) {
                case OsmPrimitiveType.Node:
                    if (feature.Parts.Count == 1 && feature.Parts[0].Count > 0) {
                        dataset.Nodes[feature.Osm.Id] = new OsmNode {
                            Id = feature.Osm.Id,
                            Version = feature.Osm.Version,
                            Point = feature.Parts[0][0],
                            Tags = OsmDataset.CopyTags(feature.Attributes)
                        };
                    }
                    break;
                case OsmPrimitiveType.Way:
                    foreach (var reference in feature.Osm.NodeReferences) {
                        dataset.Nodes.TryAdd(reference.Id, new OsmNode {
                            Id = reference.Id,
                            Version = reference.Version,
                            Point = reference.Point
                        });
                    }
                    dataset.Ways[feature.Osm.Id] = new OsmWay {
                        Id = feature.Osm.Id,
                        Version = feature.Osm.Version,
                        NodeIds = feature.Osm.NodeReferences.Select(static reference => reference.Id).ToList(),
                        Tags = OsmDataset.CopyTags(feature.Attributes)
                    };
                    break;
                case OsmPrimitiveType.Relation:
                    dataset.Relations[feature.Osm.Id] = new OsmRelation {
                        Id = feature.Osm.Id,
                        Version = feature.Osm.Version,
                        Tags = OsmDataset.CopyTags(feature.Attributes)
                    };
                    break;
            }
        }

        dataset.NormalizeTemporaryIds();
        return dataset;
    }

    public static MapFeature CreateNodeFeature(OsmNode node) {
        return new MapFeature {
            Id = $"osm-node-{node.Id}",
            GeometryType = MapGeometryType.Point,
            Parts = [[node.Point]],
            Attributes = OsmDataset.CopyTags(node.Tags),
            Osm = new OsmFeatureMetadata {
                PrimitiveType = OsmPrimitiveType.Node,
                Id = node.Id,
                Version = node.Version
            }
        };
    }

    public static MapFeature? CreateWayFeature(OsmDataset dataset, OsmWay way) {
        var points = ResolveWayPoints(dataset, way.NodeIds);
        if (points.Count < 2) return null;

        var isClosed = points.Count > 3 && points[0] == points[^1];
        return new MapFeature {
            Id = $"osm-way-{way.Id}",
            GeometryType = isClosed ? MapGeometryType.Polygon : MapGeometryType.LineString,
            Parts = [points],
            Attributes = OsmDataset.CopyTags(way.Tags),
            Osm = new OsmFeatureMetadata {
                PrimitiveType = OsmPrimitiveType.Way,
                Id = way.Id,
                Version = way.Version,
                NodeReferences = CreateNodeReferences(dataset, way.NodeIds)
            }
        };
    }

    public static MapFeature? CreateRelationFeature(OsmDataset dataset, OsmRelation relation) {
        if (!IsRenderableAreaRelation(relation)) return null;

        var parts = new List<List<GeoPoint>>();
        foreach (var member in relation.Members) {
            if (member.Type != OsmRelationMemberType.Way ||
                string.Equals(member.Role, "inner", StringComparison.OrdinalIgnoreCase) ||
                !dataset.Ways.TryGetValue(member.Id, out var way)) {
                continue;
            }

            var points = ResolveWayPoints(dataset, way.NodeIds);
            if (points.Count > 3 && points[0] == points[^1]) parts.Add(points);
        }

        if (parts.Count == 0) return null;
        return new MapFeature {
            Id = $"osm-relation-{relation.Id}",
            GeometryType = MapGeometryType.Polygon,
            Parts = parts,
            Attributes = OsmDataset.CopyTags(relation.Tags),
            Osm = new OsmFeatureMetadata {
                PrimitiveType = OsmPrimitiveType.Relation,
                Id = relation.Id,
                Version = relation.Version
            }
        };
    }

    public static List<OsmNodeReference> CreateNodeReferences(OsmDataset dataset, IReadOnlyList<long> nodeIds) {
        var references = new List<OsmNodeReference>(nodeIds.Count);
        foreach (var nodeId in nodeIds) {
            if (dataset.Nodes.TryGetValue(nodeId, out var node)) {
                references.Add(new OsmNodeReference(node.Id, node.Version, node.Point));
            }
        }

        return references;
    }

    private static void ApplyDeletedFeatures(MapDocument document, OsmDataset dataset) {
        var currentFeatureIds = document.Features.Select(static feature => feature.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var original in document.OriginalFeatures.Values) {
            if (currentFeatureIds.Contains(original.Id) || original.Osm is null) continue;

            DeletePrimitiveForFeature(dataset, original.Osm);
        }
    }

    private static void DeletePrimitiveForFeature(OsmDataset dataset, OsmFeatureMetadata metadata) {
        switch (metadata.PrimitiveType) {
            case OsmPrimitiveType.Node:
                if (IsNodeReferenced(dataset, metadata.Id)) {
                    if (dataset.Nodes.TryGetValue(metadata.Id, out var node)) node.Tags.Clear();
                } else {
                    dataset.Nodes.Remove(metadata.Id);
                }
                RemoveRelationMembers(dataset, OsmRelationMemberType.Node, metadata.Id);
                break;
            case OsmPrimitiveType.Way:
                dataset.Ways.Remove(metadata.Id);
                RemoveRelationMembers(dataset, OsmRelationMemberType.Way, metadata.Id);
                break;
            case OsmPrimitiveType.Relation:
                dataset.Relations.Remove(metadata.Id);
                RemoveRelationMembers(dataset, OsmRelationMemberType.Relation, metadata.Id);
                break;
        }
    }

    // Nodes are positioned (and indexed) before any way can snap to them: existing node features first,
    // then new POIs, so a vertex inserted into an existing way at a fresh POI joins the POI's node.
    // Among existing ways, those whose vertex count is unchanged go first because their moved vertices
    // are still matched by reference and re-index their nodes, which ways with inserted or removed
    // vertices (matched by coordinate only) then find at the new location. New lines come last.
    private static List<MapFeature> OrderFeaturesForSync(MapDocument document) {
        return document.Features
            .OrderBy(feature => (feature.Osm?.PrimitiveType, feature.GeometryType) switch {
                (OsmPrimitiveType.Node, _) => 0,
                (null, MapGeometryType.Point) => 1,
                (OsmPrimitiveType.Way, _) => HasOriginalVertexCount(document, feature) ? 2 : 3,
                (OsmPrimitiveType.Relation, _) => 4,
                _ => 5
            })
            .ToList();
    }

    private static bool HasOriginalVertexCount(MapDocument document, MapFeature feature) {
        if (!document.OriginalFeatures.TryGetValue(feature.Id, out var original) ||
            original.Parts.Count != 1 ||
            feature.Parts.Count != 1) {
            return true;
        }

        return original.Parts[0].Count == feature.Parts[0].Count;
    }

    private static void ApplyFeature(
        MapDocument document,
        OsmDataset dataset,
        NodeCoordinateIndex nodeIndex,
        MapFeature feature) {
        if (feature.Osm is null) {
            CreatePrimitiveForFeature(dataset, nodeIndex, feature);
            return;
        }

        switch (feature.Osm.PrimitiveType) {
            case OsmPrimitiveType.Node:
                ApplyNodeFeature(dataset, nodeIndex, feature);
                break;
            case OsmPrimitiveType.Way:
                ApplyWayFeature(document, dataset, nodeIndex, feature);
                break;
            case OsmPrimitiveType.Relation:
                ApplyRelationFeature(document, dataset, nodeIndex, feature);
                break;
        }
    }

    private static void CreatePrimitiveForFeature(OsmDataset dataset, NodeCoordinateIndex nodeIndex, MapFeature feature) {
        if (feature.Parts.Count != 1 || feature.Parts[0].Count == 0) return;

        if (feature.GeometryType == MapGeometryType.Point) {
            // A POI owns its node even when another node already sits at the same coordinate.
            var nodeId = dataset.CreateNode(feature.Parts[0][0], feature.Attributes);
            nodeIndex.Add(dataset.Nodes[nodeId]);
            feature.Osm = new OsmFeatureMetadata {
                PrimitiveType = OsmPrimitiveType.Node,
                Id = nodeId,
                Version = 1
            };
            return;
        }

        if (feature.Parts[0].Count < 2) return;

        var nodeIds = CreateNodesForPart(dataset, nodeIndex, feature.Parts[0]);
        var wayId = dataset.CreateWay(nodeIds, feature.Attributes);
        feature.Osm = new OsmFeatureMetadata {
            PrimitiveType = OsmPrimitiveType.Way,
            Id = wayId,
            Version = 1,
            NodeReferences = CreateNodeReferences(dataset, nodeIds)
        };
    }

    private static void ApplyNodeFeature(OsmDataset dataset, NodeCoordinateIndex nodeIndex, MapFeature feature) {
        if (feature.Parts.Count != 1 || feature.Parts[0].Count == 0 || feature.Osm is null) return;

        var point = feature.Parts[0][0];
        if (dataset.Nodes.TryGetValue(feature.Osm.Id, out var node)) {
            nodeIndex.Move(node, point);
        } else {
            node = new OsmNode {
                Id = feature.Osm.Id,
                Version = feature.Osm.Version,
                Point = point
            };
            dataset.Nodes[node.Id] = node;
            nodeIndex.Add(node);
        }

        node.Tags = OsmDataset.CopyTags(feature.Attributes);
    }

    private static void ApplyWayFeature(
        MapDocument document,
        OsmDataset dataset,
        NodeCoordinateIndex nodeIndex,
        MapFeature feature) {
        if (feature.Parts.Count != 1 || feature.Parts[0].Count < 2 || feature.Osm is null) return;

        if (!dataset.Ways.TryGetValue(feature.Osm.Id, out var way)) {
            way = new OsmWay {
                Id = feature.Osm.Id,
                Version = feature.Osm.Version
            };
            dataset.Ways[way.Id] = way;
        }

        document.OriginalFeatures.TryGetValue(feature.Id, out var originalFeature);
        var originalReferences = originalFeature?.Osm?.NodeReferences.Count > 0 == true
            ? originalFeature.Osm.NodeReferences
            : feature.Osm.NodeReferences.Count > 0
                ? feature.Osm.NodeReferences
                : CreateNodeReferences(dataset, way.NodeIds);
        var originalPart = originalFeature?.Parts.Count == 1 ? originalFeature.Parts[0] : null;
        way.NodeIds = ReconcileWayNodes(dataset, nodeIndex, feature.Parts[0], originalPart, originalReferences);
        way.Tags = OsmDataset.CopyTags(feature.Attributes);
        feature.Osm.NodeReferences = CreateNodeReferences(dataset, way.NodeIds);
    }

    private static void ApplyRelationFeature(
        MapDocument document,
        OsmDataset dataset,
        NodeCoordinateIndex nodeIndex,
        MapFeature feature) {
        if (feature.Osm is null) return;

        if (!dataset.Relations.TryGetValue(feature.Osm.Id, out var relation)) {
            relation = new OsmRelation {
                Id = feature.Osm.Id,
                Version = feature.Osm.Version
            };
            dataset.Relations[relation.Id] = relation;
        }

        relation.Tags = OsmDataset.CopyTags(feature.Attributes);
        if (!HasRelationGeometryChange(document, dataset, feature, relation)) return;

        var outerWays = GetRenderableOuterWays(dataset, relation).ToList();
        if (outerWays.Count != feature.Parts.Count) {
            throw new InvalidDataException(
                $"Relation {relation.Id} geometry has {feature.Parts.Count:N0} editable parts but {outerWays.Count:N0} outer way members.");
        }

        document.OriginalFeatures.TryGetValue(feature.Id, out var originalFeature);
        for (var i = 0; i < outerWays.Count; i++) {
            var part = feature.Parts[i];
            if (part.Count < 4 || part[0] != part[^1]) {
                throw new InvalidDataException($"Relation {relation.Id} part {i + 1:N0} must be a closed ring.");
            }

            var way = outerWays[i];
            var originalPart = originalFeature is not null && i < originalFeature.Parts.Count
                ? originalFeature.Parts[i]
                : null;
            var originalReferences = CreateNodeReferences(dataset, way.NodeIds);
            way.NodeIds = ReconcileWayNodes(dataset, nodeIndex, part, originalPart, originalReferences);
        }
    }

    private static List<long> ReconcileWayNodes(
        OsmDataset dataset,
        NodeCoordinateIndex nodeIndex,
        IReadOnlyList<GeoPoint> points,
        IReadOnlyList<GeoPoint>? originalPoints,
        IReadOnlyList<OsmNodeReference> originalReferences) {
        var matchedNodes = OsmNodeReferenceMatcher.MatchEditedWay(points, originalReferences);
        var nodeIds = new List<long>(points.Count);
        long? firstNodeId = null;
        for (var i = 0; i < points.Count; i++) {
            if (i == points.Count - 1 && points.Count > 2 && points[i] == points[0] && firstNodeId.HasValue) {
                nodeIds.Add(firstNodeId.Value);
                continue;
            }

            long nodeId;
            if (matchedNodes[i] is { } match) {
                nodeId = match.Reference.Id;
                if (!dataset.Nodes.TryGetValue(nodeId, out var node)) {
                    node = new OsmNode {
                        Id = nodeId,
                        Version = match.Reference.Version,
                        Point = match.Reference.Point
                    };
                    dataset.Nodes[nodeId] = node;
                    nodeIndex.Add(node);
                }

                if (WasMatchedPointEdited(originalPoints, match, points[i])) {
                    nodeIndex.Move(node, points[i]);
                }
            } else {
                nodeId = GetOrCreateNode(dataset, nodeIndex, points[i]);
            }

            firstNodeId ??= nodeId;
            nodeIds.Add(nodeId);
        }

        return nodeIds;
    }

    private static bool WasMatchedPointEdited(
        IReadOnlyList<GeoPoint>? originalPoints,
        OsmNodeReferenceMatch match,
        GeoPoint currentPoint) {
        if (originalPoints is not null &&
            match.OriginalIndex >= 0 &&
            match.OriginalIndex < originalPoints.Count) {
            return originalPoints[match.OriginalIndex] != currentPoint;
        }

        return match.Reference.Point != currentPoint;
    }

    private static List<long> CreateNodesForPart(
        OsmDataset dataset,
        NodeCoordinateIndex nodeIndex,
        IReadOnlyList<GeoPoint> points) {
        var nodeIds = new List<long>(points.Count);
        long? firstNodeId = null;
        for (var i = 0; i < points.Count; i++) {
            if (i == points.Count - 1 && points.Count > 2 && points[i] == points[0] && firstNodeId.HasValue) {
                nodeIds.Add(firstNodeId.Value);
                continue;
            }

            var nodeId = GetOrCreateNode(dataset, nodeIndex, points[i]);
            firstNodeId ??= nodeId;
            nodeIds.Add(nodeId);
        }

        return nodeIds;
    }

    private static long GetOrCreateNode(OsmDataset dataset, NodeCoordinateIndex nodeIndex, GeoPoint point) {
        if (nodeIndex.TryGetNodeId(point, out var nodeId)) return nodeId;

        nodeId = dataset.CreateNode(point);
        nodeIndex.Add(dataset.Nodes[nodeId]);
        return nodeId;
    }

    private static void PruneUnreferencedTemporaryObjects(MapDocument document, OsmDataset dataset) {
        var currentNodes = GetCurrentFeatureIds(document, OsmPrimitiveType.Node);
        var currentWays = GetCurrentFeatureIds(document, OsmPrimitiveType.Way);
        var currentRelations = GetCurrentFeatureIds(document, OsmPrimitiveType.Relation);

        foreach (var relationId in dataset.Relations.Keys.Where(id => id < 0 && !currentRelations.Contains(id)).ToList()) {
            dataset.Relations.Remove(relationId);
        }

        foreach (var wayId in dataset.Ways.Keys.Where(id => id < 0 && !currentWays.Contains(id)).ToList()) {
            dataset.Ways.Remove(wayId);
        }

        PruneMissingRelationMembers(dataset);
        var referencedNodes = GetReferencedNodeIds(dataset);
        foreach (var nodeId in dataset.Nodes.Keys
            .Where(id => id < 0 && !currentNodes.Contains(id) && !referencedNodes.Contains(id))
            .ToList()) {
            dataset.Nodes.Remove(nodeId);
        }
    }

    // Nodes that a way used at the clean baseline or when this sync began are the user's to clean up:
    // once nothing references them they are removed like JOSM does when deleting a way. Relation-only
    // members are not candidates because deleting a relation must never delete its member nodes.
    // The baseline is included because topology commands shrink way.NodeIds in the dataset ahead of
    // the sync, so by now those nodes already look unreferenced. Nodes that were unreferenced in both
    // states are pre-existing orphans in the source data and are left alone.
    private static HashSet<long> GetReferencedNodeIdsBeforeEdits(MapDocument document, OsmDataset dataset) {
        var referenced = GetWayNodeIds(dataset);
        if (document.OriginalOsm is { } baseline) referenced.UnionWith(GetWayNodeIds(baseline));
        return referenced;
    }

    private static void PruneNodesDetachedByEdits(
        MapDocument document,
        OsmDataset dataset,
        IReadOnlySet<long> referencedBeforeEdits) {
        var currentNodes = GetCurrentFeatureIds(document, OsmPrimitiveType.Node);
        var referencedNodes = GetReferencedNodeIds(dataset);
        foreach (var nodeId in referencedBeforeEdits) {
            if (referencedNodes.Contains(nodeId) ||
                currentNodes.Contains(nodeId) ||
                !dataset.Nodes.TryGetValue(nodeId, out var node) ||
                node.Tags.Count > 0) {
                continue;
            }

            dataset.Nodes.Remove(nodeId);
        }
    }

    private static HashSet<long> GetWayNodeIds(OsmDataset dataset) {
        return dataset.Ways.Values.SelectMany(static way => way.NodeIds).ToHashSet();
    }

    private static HashSet<long> GetReferencedNodeIds(OsmDataset dataset) {
        var referenced = GetWayNodeIds(dataset);
        referenced.UnionWith(dataset.Relations.Values
            .SelectMany(static relation => relation.Members)
            .Where(static member => member.Type == OsmRelationMemberType.Node)
            .Select(static member => member.Id));
        return referenced;
    }

    private static HashSet<long> GetCurrentFeatureIds(MapDocument document, OsmPrimitiveType type) {
        return document.Features
            .Where(feature => feature.Osm?.PrimitiveType == type)
            .Select(feature => feature.Osm!.Id)
            .ToHashSet();
    }

    private static void PruneMissingRelationMembers(OsmDataset dataset) {
        foreach (var relation in dataset.Relations.Values) {
            relation.Members.RemoveAll(member => member.Type switch {
                OsmRelationMemberType.Node => !dataset.Nodes.ContainsKey(member.Id),
                OsmRelationMemberType.Way => !dataset.Ways.ContainsKey(member.Id),
                OsmRelationMemberType.Relation => !dataset.Relations.ContainsKey(member.Id),
                _ => false
            });
        }
    }

    private static bool IsNodeReferenced(OsmDataset dataset, long nodeId) {
        return dataset.Ways.Values.Any(way => way.NodeIds.Contains(nodeId)) ||
            dataset.Relations.Values.Any(relation => relation.Members.Any(member =>
                member.Type == OsmRelationMemberType.Node && member.Id == nodeId));
    }

    private static void RemoveRelationMembers(OsmDataset dataset, OsmRelationMemberType type, long id) {
        foreach (var relation in dataset.Relations.Values) {
            relation.Members.RemoveAll(member => member.Type == type && member.Id == id);
        }
    }

    private static List<GeoPoint> ResolveWayPoints(OsmDataset dataset, IReadOnlyList<long> nodeIds) {
        var points = new List<GeoPoint>(nodeIds.Count);
        foreach (var nodeId in nodeIds) {
            if (dataset.Nodes.TryGetValue(nodeId, out var node)) points.Add(node.Point);
        }

        return points;
    }

    private static bool IsRenderableAreaRelation(OsmRelation relation) {
        return relation.Tags.TryGetValue("type", out var type) &&
            (string.Equals(type, "multipolygon", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "boundary", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasRelationGeometryChange(
        MapDocument document,
        OsmDataset dataset,
        MapFeature feature,
        OsmRelation relation) {
        if (document.OriginalFeatures.TryGetValue(feature.Id, out var originalFeature) &&
            originalFeature.Parts.Count > 0) {
            return !PartsEqual(feature.Parts, originalFeature.Parts);
        }

        var relationFeature = CreateRelationFeature(dataset, relation);
        return relationFeature is not null && !PartsEqual(feature.Parts, relationFeature.Parts);
    }

    private static IEnumerable<OsmWay> GetRenderableOuterWays(OsmDataset dataset, OsmRelation relation) {
        foreach (var member in relation.Members) {
            if (member.Type != OsmRelationMemberType.Way ||
                string.Equals(member.Role, "inner", StringComparison.OrdinalIgnoreCase) ||
                !dataset.Ways.TryGetValue(member.Id, out var way)) {
                continue;
            }

            var points = ResolveWayPoints(dataset, way.NodeIds);
            if (points.Count > 3 && points[0] == points[^1]) yield return way;
        }
    }

    private static bool PartsEqual(IReadOnlyList<List<GeoPoint>> left, IReadOnlyList<List<GeoPoint>> right) {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++) {
            if (!left[i].SequenceEqual(right[i])) return false;
        }

        return true;
    }

    private static void RefreshFeatureGeometries(MapDocument document, OsmDataset dataset) {
        foreach (var feature in document.Features) {
            switch (feature.Osm?.PrimitiveType) {
                case OsmPrimitiveType.Node when dataset.Nodes.TryGetValue(feature.Osm.Id, out var node):
                    if (feature.Parts.Count == 1 && feature.Parts[0].Count > 0) {
                        feature.Parts[0][0] = node.Point;
                        feature.InvalidateGeometry();
                    }
                    break;
                case OsmPrimitiveType.Way when dataset.Ways.TryGetValue(feature.Osm.Id, out var way):
                    var wayPoints = ResolveWayPoints(dataset, way.NodeIds);
                    if (wayPoints.Count >= 2) {
                        feature.Parts.Clear();
                        feature.Parts.Add(wayPoints);
                        feature.GeometryType = wayPoints.Count > 3 && wayPoints[0] == wayPoints[^1]
                            ? MapGeometryType.Polygon
                            : MapGeometryType.LineString;
                        feature.Osm.NodeReferences = CreateNodeReferences(dataset, way.NodeIds);
                        feature.InvalidateGeometry();
                    }
                    break;
                case OsmPrimitiveType.Relation when dataset.Relations.TryGetValue(feature.Osm.Id, out var relation):
                    var relationFeature = CreateRelationFeature(dataset, relation);
                    if (relationFeature is not null) {
                        feature.Parts.Clear();
                        feature.Parts.AddRange(relationFeature.Parts);
                        feature.InvalidateGeometry();
                    }
                    break;
            }
        }

        document.InvalidateSpatialIndex();
    }

    /// <summary>
    /// Coordinate lookup that lets newly drawn or inserted way vertices join the node already sitting
    /// at the exact same point. Points are compared bit-for-bit, so only snapped vertices connect.
    /// </summary>
    private sealed class NodeCoordinateIndex {
        private readonly Dictionary<GeoPoint, long> _nodeIdsByPoint = [];

        public NodeCoordinateIndex(OsmDataset dataset) {
            // When the dataset already holds coincident nodes the first enumerated one keeps the coordinate.
            foreach (var node in dataset.Nodes.Values) Add(node);
        }

        public bool TryGetNodeId(GeoPoint point, out long nodeId) {
            return _nodeIdsByPoint.TryGetValue(point, out nodeId);
        }

        public void Add(OsmNode node) {
            _nodeIdsByPoint.TryAdd(node.Point, node.Id);
        }

        /// <summary>Moves the node and keeps the lookup pointing at it, without displacing another node already at the target.</summary>
        public void Move(OsmNode node, GeoPoint point) {
            if (_nodeIdsByPoint.TryGetValue(node.Point, out var indexedNodeId) && indexedNodeId == node.Id) {
                _nodeIdsByPoint.Remove(node.Point);
            }

            node.Point = point;
            Add(node);
        }
    }
}
