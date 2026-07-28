using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed record TopologyEditCommandResult(IEditCommand? Command, string? Error) {
    public bool IsSuccess => Command is not null;
}

public static class TopologyEditService {
    private const double EarthRadiusMeters = 6_371_008.8;

    public static TopologyEditCommandResult CreateReverseLineCommand(
        MapEditDataset dataset,
        MapFeature feature) {
        if (!TryGetSinglePart(dataset, feature, MapGeometryType.LineString, out var layer, out var points, out var error)) {
            return Failure(error);
        }
        if (points.Count < 2) return Failure("A line must contain at least two vertices.");

        var afterOsm = layer.Osm?.Clone();
        if (!TryGetWay(feature, layer.Osm, points, requireDataset: false, out var references, out var way, out error)) {
            return Failure(error);
        }

        var reversed = points.Reverse().ToList();
        var metadata = feature.Osm?.Clone();
        if (metadata is not null) metadata.NodeReferences = references!.Reverse().ToList();
        if (way is not null) afterOsm!.Ways[way.Id].NodeIds.Reverse();

        return Success("Reverse line", layer, layer.Features, layer.Features, layer.Osm, afterOsm,
            [Capture(feature)],
            [Capture(feature, [reversed], metadata)]);
    }

    /// <summary>
    /// Creates a simplification command. The tolerance is the maximum perpendicular deviation in meters,
    /// measured in a local equirectangular projection.
    /// </summary>
    public static TopologyEditCommandResult CreateSimplifyCommand(
        MapEditDataset dataset,
        MapFeature feature,
        double toleranceMeters) {
        if (!double.IsFinite(toleranceMeters) || toleranceMeters <= 0) {
            return Failure("Tolerance must be a finite value greater than zero meters.");
        }
        if (feature.GeometryType is not (MapGeometryType.LineString or MapGeometryType.Polygon)) {
            return Failure("Only lines and polygons can be simplified.");
        }
        if (!TryGetSinglePart(dataset, feature, feature.GeometryType, out var layer, out var points, out var error)) {
            return Failure(error);
        }

        var isPolygon = feature.GeometryType == MapGeometryType.Polygon;
        if (isPolygon && (points.Count < 4 || points[0] != points[^1])) {
            return Failure("A polygon must be a closed ring with at least three distinct vertices.");
        }
        if (!isPolygon && points.Count < 3) return Failure("A line needs an interior vertex to simplify.");

        var keptIndexes = isPolygon
            ? SimplifyClosedRing(points, toleranceMeters)
            : SimplifyOpenLine(points, toleranceMeters);
        if (keptIndexes.Count == points.Count) return Failure("No vertices are within the requested tolerance.");
        if (isPolygon && keptIndexes.Count < 4) return Failure("Simplification would collapse the polygon.");

        var afterOsm = layer.Osm?.Clone();
        if (!TryGetWay(feature, layer.Osm, points, requireDataset: feature.Osm is not null,
                out var references, out var way, out error)) {
            return Failure(error);
        }

        var metadata = feature.Osm?.Clone();
        if (metadata is not null) {
            var alignedReferences = references!;
            var kept = keptIndexes.ToHashSet();
            var removedNodeIds = alignedReferences
                .Where((_, index) => !kept.Contains(index))
                .Select(static reference => reference.Id)
                .ToHashSet();
            if (HasProtectedNodes(layer.Osm!, removedNodeIds)) {
                return Failure("Simplification would remove a tagged node or a node used by a relation.");
            }

            metadata.NodeReferences = keptIndexes.Select(index => alignedReferences[index]).ToList();
            afterOsm!.Ways[way!.Id].NodeIds = metadata.NodeReferences.Select(static reference => reference.Id).ToList();
        }

        var simplified = keptIndexes.Select(index => points[index]).ToList();
        return Success("Simplify geometry", layer, layer.Features, layer.Features, layer.Osm, afterOsm,
            [Capture(feature)],
            [Capture(feature, [simplified], metadata)]);
    }

    public static TopologyEditCommandResult CreateSplitLineCommand(
        MapEditDataset dataset,
        MapFeature feature,
        int vertexIndex) {
        if (!TryGetSinglePart(dataset, feature, MapGeometryType.LineString, out var layer, out var points, out var error)) {
            return Failure(error);
        }
        if (vertexIndex <= 0 || vertexIndex >= points.Count - 1) {
            return Failure("The split vertex must be an interior vertex.");
        }

        var firstPoints = points.Take(vertexIndex + 1).ToList();
        var secondPoints = points.Skip(vertexIndex).ToList();
        if (firstPoints.Distinct().Count() < 2 || secondPoints.Distinct().Count() < 2) {
            return Failure("Splitting at this vertex would create a collapsed line.");
        }

        var afterOsm = layer.Osm?.Clone();
        if (!TryGetWay(feature, layer.Osm, points, requireDataset: feature.Osm is not null,
                out var references, out var way, out error)) {
            return Failure(error);
        }

        var firstMetadata = feature.Osm?.Clone();
        OsmFeatureMetadata? secondMetadata = null;
        if (firstMetadata is not null) {
            var alignedReferences = references!;
            var firstReferences = alignedReferences.Take(vertexIndex + 1).ToList();
            var secondReferences = alignedReferences.Skip(vertexIndex).ToList();
            firstMetadata.NodeReferences = firstReferences;
            afterOsm!.Ways[way!.Id].NodeIds = firstReferences.Select(static reference => reference.Id).ToList();
            var secondWayId = CreateWayWithAvailableId(
                afterOsm,
                secondReferences.Select(static reference => reference.Id).ToList(),
                way.Tags);
            secondMetadata = new OsmFeatureMetadata {
                PrimitiveType = OsmPrimitiveType.Way,
                Id = secondWayId,
                Version = 1,
                NodeReferences = secondReferences
            };
            AddSplitWayToRelations(afterOsm, way.Id, secondWayId);
        }

        var secondFeature = new MapFeature {
            GeometryType = MapGeometryType.LineString,
            Parts = [secondPoints],
            Attributes = new Dictionary<string, string>(feature.Attributes, StringComparer.Ordinal),
            Osm = secondMetadata
        };
        var featureIndex = layer.Features.IndexOf(feature);
        var afterFeatures = layer.Features.ToList();
        afterFeatures.Insert(featureIndex + 1, secondFeature);

        return Success("Split line", layer, layer.Features, afterFeatures, layer.Osm, afterOsm,
            [Capture(feature)],
            [Capture(feature, [firstPoints], firstMetadata), Capture(secondFeature)]);
    }

    public static TopologyEditCommandResult CreateCombineLinesCommand(
        MapEditDataset dataset,
        MapFeature first,
        MapFeature second) {
        if (ReferenceEquals(first, second)) return Failure("Select two different lines.");
        if (!TryGetSinglePart(dataset, first, MapGeometryType.LineString, out var layer, out var firstPoints, out var error) ||
            !TryGetSinglePart(dataset, second, MapGeometryType.LineString, out var secondLayer, out var secondPoints, out error)) {
            return Failure(error);
        }
        if (!ReferenceEquals(layer, secondLayer)) return Failure("Both lines must belong to the same data layer.");
        if (firstPoints.Count < 2 || secondPoints.Count < 2 || firstPoints[0] == firstPoints[^1] || secondPoints[0] == secondPoints[^1]) {
            return Failure("Only open lines with at least two vertices can be combined.");
        }
        if (!TagsEqual(first.Attributes, second.Attributes)) return Failure("The lines must have equal tags.");

        var sharedEndpoints = new[] { firstPoints[0], firstPoints[^1] }
            .Intersect([secondPoints[0], secondPoints[^1]])
            .Distinct()
            .ToList();
        if (sharedEndpoints.Count != 1) return Failure("The lines must share exactly one endpoint.");
        var sharedPoint = sharedEndpoints[0];
        var orientedFirst = firstPoints[^1] == sharedPoint ? firstPoints.ToList() : firstPoints.Reverse().ToList();
        var orientedSecond = secondPoints[0] == sharedPoint ? secondPoints.ToList() : secondPoints.Reverse().ToList();

        var afterOsm = layer.Osm?.Clone();
        List<OsmNodeReference>? combinedReferences = null;
        if ((first.Osm is null) != (second.Osm is null)) {
            return Failure("An OSM way cannot be combined with an untracked line.");
        }
        if (first.Osm is not null) {
            if (!TryGetWay(first, layer.Osm, firstPoints, requireDataset: true,
                    out var firstReferences, out var firstWay, out error) ||
                !TryGetWay(second, layer.Osm, secondPoints, requireDataset: true,
                    out var secondReferences, out var secondWay, out error)) {
                return Failure(error);
            }
            if (!TagsEqual(firstWay!.Tags, secondWay!.Tags)) return Failure("The underlying OSM ways have different tags.");
            if (IsWayInRelation(layer.Osm!, firstWay.Id) || IsWayInRelation(layer.Osm!, secondWay.Id)) {
                return Failure("Ways used by relations cannot be combined safely.");
            }

            var orientedFirstReferences = firstPoints[^1] == sharedPoint
                ? firstReferences!.ToList()
                : firstReferences!.Reverse().ToList();
            var orientedSecondReferences = secondPoints[0] == sharedPoint
                ? secondReferences!.ToList()
                : secondReferences!.Reverse().ToList();
            if (orientedFirstReferences[^1].Id != orientedSecondReferences[0].Id) {
                return Failure("The shared coordinate refers to different OSM nodes.");
            }
            combinedReferences = orientedFirstReferences.Concat(orientedSecondReferences.Skip(1)).ToList();
            afterOsm!.Ways[firstWay.Id].NodeIds = combinedReferences.Select(static reference => reference.Id).ToList();
            afterOsm.Ways.Remove(secondWay.Id);
        }

        var combinedPoints = orientedFirst.Concat(orientedSecond.Skip(1)).ToList();
        var metadata = first.Osm?.Clone();
        if (metadata is not null) metadata.NodeReferences = combinedReferences!;
        var afterFeatures = layer.Features.Where(feature => !ReferenceEquals(feature, second)).ToList();

        return Success("Combine lines", layer, layer.Features, afterFeatures, layer.Osm, afterOsm,
            [Capture(first), Capture(second)],
            [Capture(first, [combinedPoints], metadata)]);
    }

    private static bool TryGetSinglePart(
        MapEditDataset dataset,
        MapFeature feature,
        MapGeometryType geometryType,
        out MapDataLayer layer,
        out IReadOnlyList<GeoPoint> points,
        out string error) {
        layer = null!;
        points = [];
        error = string.Empty;
        if (feature.GeometryType != geometryType) {
            error = geometryType == MapGeometryType.LineString ? "The feature must be a line." : "Unexpected geometry type.";
            return false;
        }

        layer = dataset.GetLayer(feature)!;
        if (layer is null) {
            error = "The feature does not belong to the edit dataset.";
            return false;
        }
        if (feature.Parts.Count != 1) {
            error = "Multipart features are not supported by this topology operation.";
            return false;
        }

        points = feature.Parts[0];
        return true;
    }

    private static bool TryGetWay(
        MapFeature feature,
        OsmDataset? osm,
        IReadOnlyList<GeoPoint> points,
        bool requireDataset,
        out IReadOnlyList<OsmNodeReference>? references,
        out OsmWay? way,
        out string error) {
        references = null;
        way = null;
        error = string.Empty;
        if (feature.Osm is null) return true;
        if (feature.Osm.PrimitiveType != OsmPrimitiveType.Way ||
            feature.Osm.NodeReferences.Count != points.Count ||
            !feature.Osm.NodeReferences.Select(static reference => reference.Point).SequenceEqual(points)) {
            error = "The feature does not have an exact OSM way-to-vertex mapping.";
            return false;
        }

        references = feature.Osm.NodeReferences;
        if (osm is null) {
            if (requireDataset) {
                error = "The OSM dataset is required to preserve topology for this operation.";
                return false;
            }
            return true;
        }
        if (!osm.Ways.TryGetValue(feature.Osm.Id, out way) ||
            !way.NodeIds.SequenceEqual(references.Select(static reference => reference.Id))) {
            error = "The feature and underlying OSM way are out of sync.";
            return false;
        }
        if (references.Any(reference =>
                !osm.Nodes.TryGetValue(reference.Id, out var node) || node.Point != reference.Point)) {
            error = "The OSM node coordinates are missing or out of sync with the feature.";
            return false;
        }
        return true;
    }

    private static IReadOnlyList<int> SimplifyOpenLine(IReadOnlyList<GeoPoint> points, double toleranceMeters) {
        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;
        SimplifySection(points, 0, points.Count - 1, toleranceMeters, keep);
        return Enumerable.Range(0, points.Count).Where(index => keep[index]).ToList();
    }

    private static IReadOnlyList<int> SimplifyClosedRing(IReadOnlyList<GeoPoint> points, double toleranceMeters) {
        var uniqueCount = points.Count - 1;
        var firstAnchor = 0;
        var secondAnchor = Enumerable.Range(1, uniqueCount - 1)
            .MaxBy(index => SquaredProjectedDistance(points[firstAnchor], points[index], points[firstAnchor]));
        var keep = new bool[points.Count];
        keep[firstAnchor] = true;
        keep[secondAnchor] = true;
        keep[^1] = true;
        SimplifySection(points, firstAnchor, secondAnchor, toleranceMeters, keep);

        var wrapped = points.Skip(secondAnchor).Take(uniqueCount - secondAnchor).Append(points[0]).ToList();
        var wrappedKeep = new bool[wrapped.Count];
        wrappedKeep[0] = true;
        wrappedKeep[^1] = true;
        SimplifySection(wrapped, 0, wrapped.Count - 1, toleranceMeters, wrappedKeep);
        for (var i = 1; i < wrapped.Count - 1; i++) {
            if (wrappedKeep[i]) keep[secondAnchor + i] = true;
        }
        return Enumerable.Range(0, points.Count).Where(index => keep[index]).ToList();
    }

    private static void SimplifySection(
        IReadOnlyList<GeoPoint> points,
        int start,
        int end,
        double toleranceMeters,
        bool[] keep) {
        if (end <= start + 1) return;

        var greatestDistance = -1.0;
        var greatestIndex = -1;
        for (var i = start + 1; i < end; i++) {
            var distance = DistanceToSegmentMeters(points[i], points[start], points[end]);
            if (distance <= greatestDistance) continue;
            greatestDistance = distance;
            greatestIndex = i;
        }
        if (greatestDistance <= toleranceMeters) return;

        keep[greatestIndex] = true;
        SimplifySection(points, start, greatestIndex, toleranceMeters, keep);
        SimplifySection(points, greatestIndex, end, toleranceMeters, keep);
    }

    private static double DistanceToSegmentMeters(GeoPoint point, GeoPoint start, GeoPoint end) {
        var latitude = (point.Latitude + start.Latitude + end.Latitude) / 3.0;
        var cosine = Math.Cos(latitude * Math.PI / 180.0);
        var px = (point.Longitude - start.Longitude) * Math.PI / 180.0 * EarthRadiusMeters * cosine;
        var py = (point.Latitude - start.Latitude) * Math.PI / 180.0 * EarthRadiusMeters;
        var ex = (end.Longitude - start.Longitude) * Math.PI / 180.0 * EarthRadiusMeters * cosine;
        var ey = (end.Latitude - start.Latitude) * Math.PI / 180.0 * EarthRadiusMeters;
        var lengthSquared = ex * ex + ey * ey;
        if (lengthSquared == 0) return Math.Sqrt(px * px + py * py);

        var scale = Math.Clamp((px * ex + py * ey) / lengthSquared, 0.0, 1.0);
        var dx = px - scale * ex;
        var dy = py - scale * ey;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double SquaredProjectedDistance(GeoPoint first, GeoPoint second, GeoPoint origin) {
        var cosine = Math.Cos(origin.Latitude * Math.PI / 180.0);
        var dx = (second.Longitude - first.Longitude) * cosine;
        var dy = second.Latitude - first.Latitude;
        return dx * dx + dy * dy;
    }

    private static bool HasProtectedNodes(OsmDataset osm, IReadOnlySet<long> nodeIds) {
        if (nodeIds.Any(id => osm.Nodes.TryGetValue(id, out var node) && node.Tags.Count > 0)) return true;
        return osm.Relations.Values.Any(relation => relation.Members.Any(member =>
            member.Type == OsmRelationMemberType.Node && nodeIds.Contains(member.Id)));
    }

    private static void AddSplitWayToRelations(OsmDataset osm, long originalWayId, long splitWayId) {
        foreach (var relation in osm.Relations.Values) {
            for (var index = relation.Members.Count - 1; index >= 0; index--) {
                var member = relation.Members[index];
                if (member.Type != OsmRelationMemberType.Way || member.Id != originalWayId) continue;
                relation.Members.Insert(index + 1, member with { Id = splitWayId });
            }
        }
    }

    private static bool IsWayInRelation(OsmDataset osm, long wayId) {
        return osm.Relations.Values.Any(relation => relation.Members.Any(member =>
            member.Type == OsmRelationMemberType.Way && member.Id == wayId));
    }

    private static long CreateWayWithAvailableId(
        OsmDataset osm,
        IReadOnlyList<long> nodeIds,
        IReadOnlyDictionary<string, string> tags) {
        var id = Math.Min(-1, osm.NextWayId);
        while (osm.Ways.ContainsKey(id)) id--;

        osm.NextWayId = id - 1;
        osm.Ways[id] = new OsmWay {
            Id = id,
            Version = 1,
            NodeIds = nodeIds.ToList(),
            Tags = OsmDataset.CopyTags(tags)
        };
        return id;
    }

    private static bool TagsEqual(
        IReadOnlyDictionary<string, string> first,
        IReadOnlyDictionary<string, string> second) {
        return first.Count == second.Count && first.All(item =>
            second.TryGetValue(item.Key, out var value) && value == item.Value);
    }

    private static TopologyFeatureState Capture(
        MapFeature feature,
        IReadOnlyList<IReadOnlyList<GeoPoint>>? parts = null,
        OsmFeatureMetadata? metadata = null) {
        return new TopologyFeatureState(
            feature,
            (parts ?? feature.Parts).Select(static part => (IReadOnlyList<GeoPoint>)part.ToList()).ToList(),
            metadata?.Clone() ?? (parts is null ? feature.Osm?.Clone() : null));
    }

    private static TopologyEditCommandResult Success(
        string description,
        MapDataLayer layer,
        IEnumerable<MapFeature> beforeFeatures,
        IEnumerable<MapFeature> afterFeatures,
        OsmDataset? beforeOsm,
        OsmDataset? afterOsm,
        IEnumerable<TopologyFeatureState> beforeStates,
        IEnumerable<TopologyFeatureState> afterStates) {
        return new TopologyEditCommandResult(
            new TopologySnapshotCommand(
                description,
                layer,
                beforeFeatures.ToList(),
                afterFeatures.ToList(),
                beforeOsm?.Clone(),
                afterOsm?.Clone(),
                beforeStates.ToList(),
                afterStates.ToList()),
            null);
    }

    private static TopologyEditCommandResult Failure(string error) => new(null, error);

    private sealed record TopologyFeatureState(
        MapFeature Feature,
        IReadOnlyList<IReadOnlyList<GeoPoint>> Parts,
        OsmFeatureMetadata? Metadata);

    private sealed class TopologySnapshotCommand : IEditCommand {
        private readonly MapDataLayer _layer;
        private readonly IReadOnlyList<MapFeature> _beforeFeatures;
        private readonly IReadOnlyList<MapFeature> _afterFeatures;
        private readonly OsmDataset? _beforeOsm;
        private readonly OsmDataset? _afterOsm;
        private readonly IReadOnlyList<TopologyFeatureState> _beforeStates;
        private readonly IReadOnlyList<TopologyFeatureState> _afterStates;
        private MapDirtyState? _dirtyState;

        public TopologySnapshotCommand(
            string description,
            MapDataLayer layer,
            IReadOnlyList<MapFeature> beforeFeatures,
            IReadOnlyList<MapFeature> afterFeatures,
            OsmDataset? beforeOsm,
            OsmDataset? afterOsm,
            IReadOnlyList<TopologyFeatureState> beforeStates,
            IReadOnlyList<TopologyFeatureState> afterStates) {
            Description = description;
            _layer = layer;
            _beforeFeatures = beforeFeatures;
            _afterFeatures = afterFeatures;
            _beforeOsm = beforeOsm;
            _afterOsm = afterOsm;
            _beforeStates = beforeStates;
            _afterStates = afterStates;
        }

        public string Description { get; }

        public bool Execute(MapEditDataset dataset) {
            if (dataset.Document is null || !dataset.Document.DataLayers.Contains(_layer) ||
                !Matches(_layer.Features, _beforeFeatures, _beforeStates) ||
                !OsmEqual(_layer.Osm, _beforeOsm)) {
                return false;
            }

            _dirtyState = dataset.CaptureDirtyState();
            Apply(dataset, _afterFeatures, _afterStates, _afterOsm);
            return true;
        }

        public void Undo(MapEditDataset dataset) {
            Apply(dataset, _beforeFeatures, _beforeStates, _beforeOsm);
            dataset.RestoreDirty(_dirtyState);
        }

        private void Apply(
            MapEditDataset dataset,
            IReadOnlyList<MapFeature> features,
            IReadOnlyList<TopologyFeatureState> states,
            OsmDataset? osm) {
            _layer.Features.Clear();
            _layer.Features.AddRange(features);
            foreach (var state in states) {
                state.Feature.Parts.Clear();
                state.Feature.Parts.AddRange(state.Parts.Select(static part => part.ToList()));
                state.Feature.Osm = state.Metadata?.Clone();
                state.Feature.InvalidateGeometry();
            }
            _layer.Osm = osm?.Clone();
            _layer.InvalidateSpatialIndex();
            dataset.Document!.InvalidateSpatialIndex();
            dataset.Document.MarkContentChanged();
            _layer.IsDirty = true;
        }

        private static bool Matches(
            IReadOnlyList<MapFeature> actualFeatures,
            IReadOnlyList<MapFeature> expectedFeatures,
            IReadOnlyList<TopologyFeatureState> expectedStates) {
            if (!actualFeatures.SequenceEqual(expectedFeatures)) return false;
            return expectedStates.All(state =>
                state.Feature.Parts.Count == state.Parts.Count &&
                state.Feature.Parts.Zip(state.Parts).All(pair => pair.First.SequenceEqual(pair.Second)) &&
                MetadataEqual(state.Feature.Osm, state.Metadata));
        }

        private static bool MetadataEqual(OsmFeatureMetadata? first, OsmFeatureMetadata? second) {
            if (first is null || second is null) return first is null && second is null;
            return first.PrimitiveType == second.PrimitiveType &&
                first.Id == second.Id &&
                first.Version == second.Version &&
                first.NodeReferences.SequenceEqual(second.NodeReferences);
        }

        private static bool OsmEqual(OsmDataset? first, OsmDataset? second) {
            if (first is null || second is null) return first is null && second is null;
            if (first.NextNodeId != second.NextNodeId ||
                first.NextWayId != second.NextWayId ||
                first.NextRelationId != second.NextRelationId ||
                first.Nodes.Count != second.Nodes.Count ||
                first.Ways.Count != second.Ways.Count ||
                first.Relations.Count != second.Relations.Count) {
                return false;
            }

            foreach (var (id, node) in first.Nodes) {
                if (!second.Nodes.TryGetValue(id, out var other) ||
                    node.Version != other.Version ||
                    node.Point != other.Point ||
                    !TagsEqual(node.Tags, other.Tags)) {
                    return false;
                }
            }
            foreach (var (id, way) in first.Ways) {
                if (!second.Ways.TryGetValue(id, out var other) ||
                    way.Version != other.Version ||
                    !way.NodeIds.SequenceEqual(other.NodeIds) ||
                    !TagsEqual(way.Tags, other.Tags)) {
                    return false;
                }
            }
            foreach (var (id, relation) in first.Relations) {
                if (!second.Relations.TryGetValue(id, out var other) ||
                    relation.Version != other.Version ||
                    !relation.Members.SequenceEqual(other.Members) ||
                    !TagsEqual(relation.Tags, other.Tags)) {
                    return false;
                }
            }
            return true;
        }
    }
}
