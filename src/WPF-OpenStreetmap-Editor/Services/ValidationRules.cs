using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

internal sealed class TagValidationRule : IValidationRule {
    public string Id => "tags";

    public IEnumerable<ValidationIssue> Validate(ValidationContext context) {
        foreach (var feature in context.Features.Where(feature => ValidationRuleSupport.ShouldValidateFeature(feature, context.OsmDataset))) {
            foreach (var issue in ValidateTags(feature.Attributes, ValidationRuleSupport.FeatureTarget(feature))) yield return issue;
        }

        if (context.OsmDataset is not { } dataset) yield break;
        foreach (var node in dataset.Nodes.Values) {
            foreach (var issue in ValidateTags(node.Tags, ValidationRuleSupport.NodeTarget(node.Id))) yield return issue;
        }
        foreach (var way in dataset.Ways.Values) {
            foreach (var issue in ValidateTags(way.Tags, ValidationRuleSupport.WayTarget(way.Id))) yield return issue;
        }
        foreach (var relation in dataset.Relations.Values) {
            foreach (var issue in ValidateTags(relation.Tags, ValidationRuleSupport.RelationTarget(relation.Id))) yield return issue;
        }
    }

    private static IEnumerable<ValidationIssue> ValidateTags(
        IReadOnlyDictionary<string, string> tags,
        ValidationObjectReference target) {
        foreach (var (key, value) in tags) {
            if (IsInvalidKey(key)) {
                yield return new ValidationIssue(
                    "tag.invalid-key",
                    ValidationSeverity.Error,
                    "Tag key is empty or contains a forbidden character.",
                    target,
                    new ValidationFixSuggestion(ValidationFixKind.RemoveTag, "Remove the invalid tag.", key));
            }
            if (string.IsNullOrWhiteSpace(value)) {
                yield return new ValidationIssue(
                    "tag.empty-value",
                    ValidationSeverity.Warning,
                    $"Tag '{key}' has an empty value.",
                    target,
                    new ValidationFixSuggestion(ValidationFixKind.RemoveTag, "Remove the empty tag.", key));
            }
        }
    }

    private static bool IsInvalidKey(string key) {
        return string.IsNullOrWhiteSpace(key) ||
            key.Contains('=') ||
            key.Any(char.IsControl);
    }
}

internal sealed class GeometryValidationRule : IValidationRule {
    public string Id => "geometry";

    public IEnumerable<ValidationIssue> Validate(ValidationContext context) {
        foreach (var feature in context.Features.Where(feature => ValidationRuleSupport.ShouldValidateFeature(feature, context.OsmDataset))) {
            foreach (var issue in ValidateFeature(feature)) yield return issue;
        }

        if (context.OsmDataset is not { } dataset) yield break;
        foreach (var way in dataset.Ways.Values) {
            foreach (var issue in ValidateWay(dataset, way)) yield return issue;
        }
    }

    private static IEnumerable<ValidationIssue> ValidateFeature(MapFeature feature) {
        var target = ValidationRuleSupport.FeatureTarget(feature);
        var minimum = feature.GeometryType switch {
            MapGeometryType.Point => 1,
            MapGeometryType.LineString => 2,
            MapGeometryType.Polygon => 4,
            _ => 1
        };

        if (feature.Parts.Count == 0 || feature.Parts.Any(part => part.Count < minimum)) {
            yield return new ValidationIssue(
                "geometry.insufficient-points",
                ValidationSeverity.Error,
                $"{feature.GeometryType} geometry does not contain enough points.",
                target);
        }

        foreach (var part in feature.Parts) {
            if (feature.GeometryType == MapGeometryType.Polygon && part.Count > 0 && part[0] != part[^1]) {
                yield return new ValidationIssue(
                    "geometry.unclosed-polygon",
                    ValidationSeverity.Error,
                    "Polygon ring is not closed.",
                    target,
                    new ValidationFixSuggestion(ValidationFixKind.ClosePolygon, "Append the first point to close the polygon."));
            }
            if (ValidationRuleSupport.HasConsecutiveDuplicates(part)) {
                yield return new ValidationIssue(
                    "geometry.consecutive-duplicate-points",
                    ValidationSeverity.Warning,
                    "Geometry contains consecutive duplicate points.",
                    target,
                    new ValidationFixSuggestion(
                        ValidationFixKind.RemoveConsecutiveDuplicatePoints,
                        "Remove consecutive duplicate points."));
            }
            if (ValidationRuleSupport.HasSelfIntersection(part, feature.GeometryType == MapGeometryType.Polygon)) {
                yield return new ValidationIssue(
                    "geometry.self-intersection",
                    ValidationSeverity.Error,
                    "Geometry intersects itself.",
                    target);
            }
        }
    }

    private static IEnumerable<ValidationIssue> ValidateWay(OsmDataset dataset, OsmWay way) {
        var target = ValidationRuleSupport.WayTarget(way.Id);
        var isArea = ValidationRuleSupport.IsArea(way.Tags);
        var minimum = isArea ? 4 : 2;
        if (way.NodeIds.Count < minimum) {
            yield return new ValidationIssue(
                "geometry.insufficient-points",
                ValidationSeverity.Error,
                $"Way does not contain the minimum {minimum} node references.",
                target);
        }

        if (isArea && way.NodeIds.Count > 0 && way.NodeIds[0] != way.NodeIds[^1]) {
            yield return new ValidationIssue(
                "geometry.unclosed-polygon",
                ValidationSeverity.Error,
                "Area way is not closed.",
                target,
                new ValidationFixSuggestion(ValidationFixKind.ClosePolygon, "Append the first node reference to close the area."));
        }

        if (ValidationRuleSupport.HasConsecutiveDuplicates(way.NodeIds)) {
            yield return new ValidationIssue(
                "geometry.consecutive-duplicate-points",
                ValidationSeverity.Warning,
                "Way contains consecutive duplicate node references.",
                target,
                new ValidationFixSuggestion(
                    ValidationFixKind.RemoveConsecutiveDuplicatePoints,
                    "Remove consecutive duplicate node references."));
        }

        var points = way.NodeIds
            .Where(dataset.Nodes.ContainsKey)
            .Select(id => dataset.Nodes[id].Point)
            .ToList();
        if (points.Count == way.NodeIds.Count && ValidationRuleSupport.HasSelfIntersection(points, isArea)) {
            yield return new ValidationIssue(
                "geometry.self-intersection",
                ValidationSeverity.Error,
                "Way intersects itself.",
                target);
        }
    }
}

internal sealed class RelationValidationRule : IValidationRule {
    public string Id => "relation";

    public IEnumerable<ValidationIssue> Validate(ValidationContext context) {
        if (context.OsmDataset is not { } dataset) yield break;

        foreach (var relation in dataset.Relations.Values) {
            var target = ValidationRuleSupport.RelationTarget(relation.Id);
            foreach (var member in relation.Members) {
                if (!MemberExists(dataset, member)) {
                    yield return new ValidationIssue(
                        "relation.missing-member",
                        ValidationSeverity.Error,
                        $"Relation member {member.Type.ToString().ToLowerInvariant()}/{member.Id} does not exist.",
                        target);
                }
            }

            foreach (var duplicate in relation.Members
                .GroupBy(static member => member)
                .Where(static group => group.Count() > 1)) {
                yield return new ValidationIssue(
                    "relation.duplicate-member",
                    ValidationSeverity.Warning,
                    $"Relation contains duplicate member {duplicate.Key.Type.ToString().ToLowerInvariant()}/{duplicate.Key.Id}.",
                    target);
            }

            if (!relation.Tags.TryGetValue("type", out var type) ||
                !string.Equals(type, "multipolygon", StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var member in relation.Members) {
                if (member.Type != OsmRelationMemberType.Way || !IsValidMultipolygonRole(member.Role)) {
                    yield return new ValidationIssue(
                        "relation.invalid-multipolygon-role",
                        ValidationSeverity.Error,
                        $"Multipolygon member {member.Type.ToString().ToLowerInvariant()}/{member.Id} has invalid role '{member.Role}'.",
                        target);
                }
            }
        }
    }

    private static bool MemberExists(OsmDataset dataset, OsmRelationMember member) {
        return member.Type switch {
            OsmRelationMemberType.Node => dataset.Nodes.ContainsKey(member.Id),
            OsmRelationMemberType.Way => dataset.Ways.ContainsKey(member.Id),
            OsmRelationMemberType.Relation => dataset.Relations.ContainsKey(member.Id),
            _ => false
        };
    }

    private static bool IsValidMultipolygonRole(string role) {
        return string.IsNullOrEmpty(role) || role is "outer" or "inner";
    }
}

internal static class ValidationRuleSupport {
    private const double Epsilon = 1e-12;
    private static readonly string[] AreaKeys = [
        "building", "landuse", "leisure", "amenity", "shop", "place", "boundary"
    ];

    public static ValidationObjectReference FeatureTarget(MapFeature feature) =>
        new(ValidationObjectType.Feature, feature.Id);

    public static ValidationObjectReference NodeTarget(long id) =>
        new(ValidationObjectType.Node, id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static ValidationObjectReference WayTarget(long id) =>
        new(ValidationObjectType.Way, id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static ValidationObjectReference RelationTarget(long id) =>
        new(ValidationObjectType.Relation, id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static bool ShouldValidateFeature(MapFeature feature, OsmDataset? dataset) {
        if (feature.Osm is not { } osm || dataset is null) return true;
        return osm.PrimitiveType switch {
            OsmPrimitiveType.Node => !dataset.Nodes.ContainsKey(osm.Id),
            OsmPrimitiveType.Way => !dataset.Ways.ContainsKey(osm.Id),
            OsmPrimitiveType.Relation => !dataset.Relations.ContainsKey(osm.Id),
            _ => true
        };
    }

    public static bool IsArea(IReadOnlyDictionary<string, string> tags) {
        if (tags.TryGetValue("area", out var area)) {
            return string.Equals(area, "yes", StringComparison.OrdinalIgnoreCase);
        }
        return AreaKeys.Any(key => tags.TryGetValue(key, out var value) &&
            !string.Equals(value, "no", StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasConsecutiveDuplicates<T>(IReadOnlyList<T> values) {
        var comparer = EqualityComparer<T>.Default;
        for (var index = 1; index < values.Count; index++) {
            if (comparer.Equals(values[index - 1], values[index])) return true;
        }
        return false;
    }

    public static bool HasSelfIntersection(IReadOnlyList<GeoPoint> points, bool closed) {
        if (points.Count < 2) return false;

        IReadOnlyList<GeoPoint> path = points;
        if (closed && points[0] != points[^1]) {
            path = [.. points, points[0]];
        }

        var segmentCount = path.Count - 1;
        if (segmentCount < 2) return false;

        for (var first = 0; first < segmentCount; first++) {
            for (var second = first + 1; second < segmentCount; second++) {
                if (second == first + 1) continue;
                if (closed && first == 0 && second == segmentCount - 1) continue;
                if (SegmentsIntersect(path[first], path[first + 1], path[second], path[second + 1])) return true;
            }
        }
        return false;
    }

    private static bool SegmentsIntersect(GeoPoint a, GeoPoint b, GeoPoint c, GeoPoint d) {
        var abC = Cross(a, b, c);
        var abD = Cross(a, b, d);
        var cdA = Cross(c, d, a);
        var cdB = Cross(c, d, b);

        if (OppositeSigns(abC, abD) && OppositeSigns(cdA, cdB)) return true;
        return IsZero(abC) && IsOnSegment(a, b, c) ||
            IsZero(abD) && IsOnSegment(a, b, d) ||
            IsZero(cdA) && IsOnSegment(c, d, a) ||
            IsZero(cdB) && IsOnSegment(c, d, b);
    }

    private static double Cross(GeoPoint a, GeoPoint b, GeoPoint c) {
        return (b.Longitude - a.Longitude) * (c.Latitude - a.Latitude) -
            (b.Latitude - a.Latitude) * (c.Longitude - a.Longitude);
    }

    private static bool OppositeSigns(double left, double right) =>
        left > Epsilon && right < -Epsilon || left < -Epsilon && right > Epsilon;

    private static bool IsZero(double value) => Math.Abs(value) <= Epsilon;

    private static bool IsOnSegment(GeoPoint a, GeoPoint b, GeoPoint point) {
        return point.Longitude >= Math.Min(a.Longitude, b.Longitude) - Epsilon &&
            point.Longitude <= Math.Max(a.Longitude, b.Longitude) + Epsilon &&
            point.Latitude >= Math.Min(a.Latitude, b.Latitude) - Epsilon &&
            point.Latitude <= Math.Max(a.Latitude, b.Latitude) + Epsilon;
    }
}
