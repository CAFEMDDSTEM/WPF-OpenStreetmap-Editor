using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

internal static class OsmDocumentSync {
    public static OsmDataset Synchronize(MapDocument document) {
        ArgumentNullException.ThrowIfNull(document);

        var dataset = document.Osm?.Clone() ?? new OsmDataset();
        dataset.NormalizeTemporaryIds();
        ApplyDeletedFeatures(document, dataset);
        foreach (var feature in document.Features) {
            ApplyFeature(document, dataset, feature);
        }
        PruneUnreferencedTemporaryObjects(document, dataset);
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

    private static void ApplyFeature(MapDocument document, OsmDataset dataset, MapFeature feature) {
        if (feature.Osm is null) {
            CreatePrimitiveForFeature(dataset, feature);
            return;
        }

        switch (feature.Osm.PrimitiveType) {
            case OsmPrimitiveType.Node:
                ApplyNodeFeature(dataset, feature);
                break;
            case OsmPrimitiveType.Way:
                ApplyWayFeature(document, dataset, feature);
                break;
            case OsmPrimitiveType.Relation:
                ApplyRelationFeature(dataset, feature);
                break;
        }
    }

    private static void CreatePrimitiveForFeature(OsmDataset dataset, MapFeature feature) {
        if (feature.Parts.Count != 1 || feature.Parts[0].Count == 0) return;

        if (feature.GeometryType == MapGeometryType.Point) {
            var nodeId = dataset.CreateNode(feature.Parts[0][0], feature.Attributes);
            feature.Osm = new OsmFeatureMetadata {
                PrimitiveType = OsmPrimitiveType.Node,
                Id = nodeId,
                Version = 1
            };
            return;
        }

        if (feature.Parts[0].Count < 2) return;

        var nodeIds = CreateNodesForPart(dataset, feature.Parts[0]);
        var wayId = dataset.CreateWay(nodeIds, feature.Attributes);
        feature.Osm = new OsmFeatureMetadata {
            PrimitiveType = OsmPrimitiveType.Way,
            Id = wayId,
            Version = 1,
            NodeReferences = CreateNodeReferences(dataset, nodeIds)
        };
    }

    private static void ApplyNodeFeature(OsmDataset dataset, MapFeature feature) {
        if (feature.Parts.Count != 1 || feature.Parts[0].Count == 0 || feature.Osm is null) return;

        if (!dataset.Nodes.TryGetValue(feature.Osm.Id, out var node)) {
            node = new OsmNode {
                Id = feature.Osm.Id,
                Version = feature.Osm.Version
            };
            dataset.Nodes[node.Id] = node;
        }

        node.Point = feature.Parts[0][0];
        node.Tags = OsmDataset.CopyTags(feature.Attributes);
    }

    private static void ApplyWayFeature(MapDocument document, OsmDataset dataset, MapFeature feature) {
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
        way.NodeIds = ReconcileWayNodes(dataset, feature.Parts[0], originalFeature, originalReferences);
        way.Tags = OsmDataset.CopyTags(feature.Attributes);
        feature.Osm.NodeReferences = CreateNodeReferences(dataset, way.NodeIds);
    }

    private static void ApplyRelationFeature(OsmDataset dataset, MapFeature feature) {
        if (feature.Osm is null) return;

        if (!dataset.Relations.TryGetValue(feature.Osm.Id, out var relation)) {
            relation = new OsmRelation {
                Id = feature.Osm.Id,
                Version = feature.Osm.Version
            };
            dataset.Relations[relation.Id] = relation;
        }

        relation.Tags = OsmDataset.CopyTags(feature.Attributes);
    }

    private static List<long> ReconcileWayNodes(
        OsmDataset dataset,
        IReadOnlyList<GeoPoint> points,
        MapFeature? originalFeature,
        IReadOnlyList<OsmNodeReference> originalReferences) {
        var matchedNodes = OsmNodeReferenceMatcher.MatchEditedWay(points, originalReferences);
        var nodeIds = new List<long>(points.Count);
        long? firstNodeId = null;
        for (var i = 0; i < points.Count; i++) {
            if (i == points.Count - 1 && points.Count > 2 && points[i] == points[0] && firstNodeId.HasValue) {
                nodeIds.Add(firstNodeId.Value);
                continue;
            }

            if (matchedNodes[i] is { } match) {
                var nodeId = match.Reference.Id;
                if (!dataset.Nodes.TryGetValue(nodeId, out var node)) {
                    node = new OsmNode {
                        Id = nodeId,
                        Version = match.Reference.Version,
                        Point = match.Reference.Point
                    };
                    dataset.Nodes[nodeId] = node;
                }

                if (WasMatchedPointEdited(originalFeature, match, points[i])) {
                    node.Point = points[i];
                }

                firstNodeId ??= nodeId;
                nodeIds.Add(nodeId);
                continue;
            }

            var createdNodeId = dataset.CreateNode(points[i]);
            firstNodeId ??= createdNodeId;
            nodeIds.Add(createdNodeId);
        }

        return nodeIds;
    }

    private static bool WasMatchedPointEdited(MapFeature? originalFeature, OsmNodeReferenceMatch match, GeoPoint currentPoint) {
        if (originalFeature?.Parts.Count == 1 &&
            match.OriginalIndex >= 0 &&
            match.OriginalIndex < originalFeature.Parts[0].Count) {
            return originalFeature.Parts[0][match.OriginalIndex] != currentPoint;
        }

        return match.Reference.Point != currentPoint;
    }

    private static List<long> CreateNodesForPart(OsmDataset dataset, IReadOnlyList<GeoPoint> points) {
        var nodeIds = new List<long>(points.Count);
        long? firstNodeId = null;
        for (var i = 0; i < points.Count; i++) {
            if (i == points.Count - 1 && points.Count > 2 && points[i] == points[0] && firstNodeId.HasValue) {
                nodeIds.Add(firstNodeId.Value);
                continue;
            }

            var nodeId = dataset.CreateNode(points[i]);
            firstNodeId ??= nodeId;
            nodeIds.Add(nodeId);
        }

        return nodeIds;
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
        var referencedNodes = dataset.Ways.Values
            .SelectMany(static way => way.NodeIds)
            .Concat(dataset.Relations.Values
                .SelectMany(static relation => relation.Members)
                .Where(static member => member.Type == OsmRelationMemberType.Node)
                .Select(static member => member.Id))
            .ToHashSet();
        foreach (var nodeId in dataset.Nodes.Keys
            .Where(id => id < 0 && !currentNodes.Contains(id) && !referencedNodes.Contains(id))
            .ToList()) {
            dataset.Nodes.Remove(nodeId);
        }
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
}
