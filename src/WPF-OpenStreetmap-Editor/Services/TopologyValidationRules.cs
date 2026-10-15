using System.Globalization;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

internal sealed class DuplicateNodeValidationRule : IValidationRule {
    private const int MaximumListedIds = 5;

    public string Id => "topology";

    public IEnumerable<ValidationIssue> Validate(ValidationContext context) {
        // Features and dataset nodes are only compared among themselves: a tracked feature is validated
        // through its dataset copy, and a fresh point drawn on an existing node is merged into that node
        // by the sync layer rather than being a duplicate.
        var untrackedPoints = context.Features.Where(feature =>
            feature.GeometryType == MapGeometryType.Point &&
            feature.Parts.Count > 0 && feature.Parts[0].Count > 0 &&
            ValidationRuleSupport.ShouldValidateFeature(feature, context.OsmDataset));
        foreach (var group in untrackedPoints.GroupBy(static feature => feature.Parts[0][0])) {
            var features = group.ToList();
            if (features.Count < 2) continue;
            yield return new ValidationIssue(
                "topology.duplicate-nodes",
                ValidationSeverity.Warning,
                $"{features.Count} point features share the same position.",
                ValidationRuleSupport.FeatureTarget(features[0]));
        }

        if (context.OsmDataset is not { } dataset) yield break;
        foreach (var group in dataset.Nodes.Values.Where(static node => node.Point.IsValid).GroupBy(static node => node.Point)) {
            var ids = group.Select(static node => node.Id).Order().ToList();
            if (ids.Count < 2) continue;
            yield return new ValidationIssue(
                "topology.duplicate-nodes",
                ValidationSeverity.Warning,
                $"{ids.Count} nodes share the same position ({TopologyValidationSupport.FormatIdList(ids, MaximumListedIds)}).",
                ValidationRuleSupport.NodeTarget(ids[0]));
        }
    }
}

internal sealed class UntaggedObjectValidationRule : IValidationRule {
    public string Id => "topology";

    public IEnumerable<ValidationIssue> Validate(ValidationContext context) {
        var dataset = context.OsmDataset;
        var relationWayIds = MemberIds(dataset, OsmRelationMemberType.Way);

        // Untracked point features are skipped: a freshly placed point is tagged as the next step.
        foreach (var feature in context.Features) {
            if (feature.GeometryType == MapGeometryType.Point ||
                feature.Attributes.Count > 0 ||
                feature.Osm is { PrimitiveType: not OsmPrimitiveType.Way } ||
                !ValidationRuleSupport.ShouldValidateFeature(feature, dataset)) {
                continue;
            }
            if (feature.Osm is { } osm && relationWayIds.Contains(osm.Id)) continue;
            yield return UntaggedWay(ValidationRuleSupport.FeatureTarget(feature));
        }

        if (dataset is null) yield break;
        foreach (var way in dataset.Ways.Values) {
            if (way.Tags.Count > 0 || relationWayIds.Contains(way.Id)) continue;
            yield return UntaggedWay(ValidationRuleSupport.WayTarget(way.Id));
        }

        var referencedNodeIds = dataset.Ways.Values.SelectMany(static way => way.NodeIds).ToHashSet();
        referencedNodeIds.UnionWith(MemberIds(dataset, OsmRelationMemberType.Node));
        foreach (var node in dataset.Nodes.Values) {
            if (node.Tags.Count > 0 || referencedNodeIds.Contains(node.Id)) continue;
            yield return new ValidationIssue(
                "topology.untagged-node",
                ValidationSeverity.Warning,
                "Node has no tags and is not referenced by any way or relation.",
                ValidationRuleSupport.NodeTarget(node.Id));
        }
    }

    private static ValidationIssue UntaggedWay(ValidationObjectReference target) {
        return new ValidationIssue(
            "topology.untagged-way",
            ValidationSeverity.Warning,
            "Way has no tags and is not a member of any relation.",
            target);
    }

    private static HashSet<long> MemberIds(OsmDataset? dataset, OsmRelationMemberType type) {
        if (dataset is null) return [];
        return dataset.Relations.Values
            .SelectMany(static relation => relation.Members)
            .Where(member => member.Type == type)
            .Select(static member => member.Id)
            .ToHashSet();
    }
}

internal sealed class HighwayConnectivityValidationRule : IValidationRule {
    private const double ConnectionToleranceMeters = 5.0;
    private static readonly HashSet<string> RoutableHighwayValues = new(StringComparer.Ordinal) {
        "motorway", "motorway_link", "trunk", "trunk_link", "primary", "primary_link",
        "secondary", "secondary_link", "tertiary", "tertiary_link", "unclassified", "residential",
        "service", "living_street", "track", "path", "footway", "cycleway", "bridleway",
        "pedestrian", "steps", "road"
    };

    public string Id => "topology";

    public IEnumerable<ValidationIssue> Validate(ValidationContext context) {
        if (context.OsmDataset is not { } dataset) return [];

        var highways = CollectRoutableHighways(dataset);
        var grid = new SegmentGrid();
        foreach (var highway in highways.Values) {
            for (var index = 1; index < highway.Points.Count; index++) {
                var start = highway.Points[index - 1];
                var end = highway.Points[index];
                // Zero-length segments are the geometry rule's consecutive-duplicate finding.
                if (start != end) grid.Add(new WaySegment(highway.Way.Id, start, end));
            }
        }

        return FindDanglingEnds(dataset, highways, grid).Concat(FindCrossings(highways, grid));
    }

    private static Dictionary<long, RoutableHighway> CollectRoutableHighways(OsmDataset dataset) {
        var highways = new Dictionary<long, RoutableHighway>();
        foreach (var way in dataset.Ways.Values) {
            if (!way.Tags.TryGetValue("highway", out var highway) || !RoutableHighwayValues.Contains(highway)) continue;

            var points = new List<GeoPoint>(way.NodeIds.Count);
            foreach (var nodeId in way.NodeIds) {
                if (!dataset.Nodes.TryGetValue(nodeId, out var node) || !node.Point.IsValid) break;
                points.Add(node.Point);
            }
            // A way with unresolved or invalid nodes has no geometry worth checking.
            if (points.Count != way.NodeIds.Count || points.Count < 2) continue;

            highways[way.Id] = new RoutableHighway(way, points, ParseLayer(way.Tags), IsBridgeOrTunnel(way.Tags));
        }
        return highways;
    }

    private static IEnumerable<ValidationIssue> FindDanglingEnds(
        OsmDataset dataset,
        IReadOnlyDictionary<long, RoutableHighway> highways,
        SegmentGrid grid) {
        // Counting every reference rather than distinct ways also covers closed ways and ways looping
        // back onto their own end node: both reference that node twice, so it never counts as dangling.
        var referenceCounts = new Dictionary<long, int>();
        foreach (var nodeId in dataset.Ways.Values.SelectMany(static way => way.NodeIds)) {
            referenceCounts[nodeId] = referenceCounts.GetValueOrDefault(nodeId) + 1;
        }

        foreach (var highway in highways.Values) {
            var nodeIds = highway.Way.NodeIds;
            foreach (var (nodeId, point) in new[] { (nodeIds[0], highway.Points[0]), (nodeIds[^1], highway.Points[^1]) }) {
                if (referenceCounts[nodeId] != 1 || IsMappedDeadEnd(dataset, nodeId)) continue;
                if (FindNearestForeignSegment(grid, highways, highway, point) is not { } nearest) continue;
                yield return new ValidationIssue(
                    "topology.way-end-near-highway",
                    ValidationSeverity.Warning,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Highway end node {nodeId} is {nearest.Distance:0.0} m from highway way {nearest.Segment.WayId}; possibly missing connection."),
                    ValidationRuleSupport.WayTarget(highway.Way.Id));
            }
        }
    }

    // noexit=yes is the mapper's explicit statement that the road really ends here.
    private static bool IsMappedDeadEnd(OsmDataset dataset, long nodeId) {
        return dataset.Nodes.TryGetValue(nodeId, out var node) && HasAffirmativeValue(node.Tags, "noexit");
    }

    private static (WaySegment Segment, double Distance)? FindNearestForeignSegment(
        SegmentGrid grid,
        IReadOnlyDictionary<long, RoutableHighway> highways,
        RoutableHighway highway,
        GeoPoint point) {
        (WaySegment Segment, double Distance)? nearest = null;
        foreach (var index in grid.Query(TopologyValidationSupport.BoundsAround(point, ConnectionToleranceMeters))) {
            var segment = grid.Segments[index];
            // An end node sitting exactly on one of the other way's nodes is the duplicate-node finding, and a
            // road ending under a bridge or above a tunnel is not a missing connection.
            if (segment.WayId == highway.Way.Id || segment.Start == point || segment.End == point ||
                !SharesLevel(highway, highways[segment.WayId])) {
                continue;
            }
            var distance = TopologyValidationSupport.DistanceToSegmentMeters(point, segment.Start, segment.End);
            if (distance > ConnectionToleranceMeters) continue;
            if (nearest is null || distance < nearest.Value.Distance) nearest = (segment, distance);
        }
        return nearest;
    }

    private static IEnumerable<ValidationIssue> FindCrossings(IReadOnlyDictionary<long, RoutableHighway> highways, SegmentGrid grid) {
        var reported = new HashSet<(long Lower, long Upper)>();
        for (var first = 0; first < grid.Segments.Count; first++) {
            var firstSegment = grid.Segments[first];
            var firstHighway = highways[firstSegment.WayId];
            foreach (var second in grid.Query(firstSegment.Bounds)) {
                // Only look forward so every segment pair is examined once; a way crossing itself is the
                // geometry rule's self-intersection finding.
                if (second <= first) continue;
                var secondSegment = grid.Segments[second];
                if (secondSegment.WayId == firstSegment.WayId) continue;

                var pair = (
                    Lower: Math.Min(firstSegment.WayId, secondSegment.WayId),
                    Upper: Math.Max(firstSegment.WayId, secondSegment.WayId));
                if (reported.Contains(pair) || !SharesLevel(firstHighway, highways[secondSegment.WayId])) continue;
                if (!TopologyValidationSupport.SegmentsCross(firstSegment.Start, firstSegment.End, secondSegment.Start, secondSegment.End)) continue;

                reported.Add(pair);
                yield return new ValidationIssue(
                    "topology.crossing-highways",
                    ValidationSeverity.Warning,
                    $"Highway way {pair.Lower} crosses highway way {pair.Upper} without a shared node.",
                    ValidationRuleSupport.WayTarget(pair.Lower));
            }
        }
    }

    // Ways on different layers, or carried by a bridge or tunnel, legitimately pass over or under each other.
    private static bool SharesLevel(RoutableHighway first, RoutableHighway second) {
        return first.Layer == second.Layer && !first.IsBridgeOrTunnel && !second.IsBridgeOrTunnel;
    }

    private static int ParseLayer(IReadOnlyDictionary<string, string> tags) {
        return tags.TryGetValue("layer", out var layer) &&
            int.TryParse(layer, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static bool IsBridgeOrTunnel(IReadOnlyDictionary<string, string> tags) {
        return HasAffirmativeValue(tags, "bridge") || HasAffirmativeValue(tags, "tunnel");
    }

    private static bool HasAffirmativeValue(IReadOnlyDictionary<string, string> tags, string key) {
        return tags.TryGetValue(key, out var value) && !string.Equals(value, "no", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record RoutableHighway(OsmWay Way, IReadOnlyList<GeoPoint> Points, int Layer, bool IsBridgeOrTunnel);
}
