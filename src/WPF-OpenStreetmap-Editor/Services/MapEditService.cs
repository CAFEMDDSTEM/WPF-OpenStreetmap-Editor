using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public static class MapEditService {
    public static IReadOnlyList<MapFeature> CopyFeatures(IEnumerable<MapFeature> sourceFeatures) {
        ArgumentNullException.ThrowIfNull(sourceFeatures);

        return sourceFeatures.Select(static feature => feature.Clone()).ToList();
    }

    public static IReadOnlyList<MapFeature> CreateNewCopies(
        IEnumerable<MapFeature> sourceFeatures,
        IEnumerable<string> reservedFeatureIds,
        double longitudeOffset,
        double latitudeOffset) {
        ArgumentNullException.ThrowIfNull(sourceFeatures);
        ArgumentNullException.ThrowIfNull(reservedFeatureIds);

        var reservedIds = reservedFeatureIds.ToHashSet(StringComparer.Ordinal);
        return sourceFeatures
            .Select(feature => CreateNewCopy(feature, reservedIds, longitudeOffset, latitudeOffset))
            .ToList();
    }

    public static GeoPoint GetGeometryCenter(IEnumerable<MapFeature> features) {
        ArgumentNullException.ThrowIfNull(features);

        var bounds = GeoBounds.FromPoints(features.SelectMany(static feature => feature.Points));
        return bounds.IsValid
            ? bounds.Center
            : new GeoPoint(double.NaN, double.NaN);
    }

    public static List<List<GeoPoint>> RotateParts(MapFeature feature, GeoPoint center, double angleDegrees) {
        ArgumentNullException.ThrowIfNull(feature);

        return RotateParts(feature.Parts, center, angleDegrees);
    }

    public static List<List<GeoPoint>> RotateParts(
        IEnumerable<IEnumerable<GeoPoint>> parts,
        GeoPoint center,
        double angleDegrees) {
        ArgumentNullException.ThrowIfNull(parts);

        var radians = angleDegrees * Math.PI / 180.0;
        var sin = Math.Sin(radians);
        var cos = Math.Cos(radians);
        return parts
            .Select(part => part
                .Select(point => RotatePoint(point, center, sin, cos))
                .ToList())
            .ToList();
    }

    public static List<List<GeoPoint>> MoveParts(
        IEnumerable<IEnumerable<GeoPoint>> parts,
        double longitudeOffset,
        double latitudeOffset) {
        ArgumentNullException.ThrowIfNull(parts);

        return parts
            .Select(part => part
                .Select(point => OffsetPoint(point, longitudeOffset, latitudeOffset))
                .ToList())
            .ToList();
    }

    public static List<List<GeoPoint>> OrthogonalizeParts(IEnumerable<IEnumerable<GeoPoint>> parts) {
        ArgumentNullException.ThrowIfNull(parts);

        var partList = parts.Select(static part => part.ToList()).ToList();
        if (partList.Count == 0) return [];

        var angle = GetDominantOrthogonalAngle(partList);
        return partList
            .Select(part => OrthogonalizePart(part, angle))
            .ToList();
    }

    private static MapFeature CreateNewCopy(
        MapFeature source,
        ISet<string> reservedIds,
        double longitudeOffset,
        double latitudeOffset) {
        return new MapFeature {
            Id = CreateUniqueFeatureId(reservedIds),
            GeometryType = source.GeometryType,
            Parts = source.Parts
                .Select(part => part
                    .Select(point => OffsetPoint(point, longitudeOffset, latitudeOffset))
                    .ToList())
                .ToList(),
            Attributes = new Dictionary<string, string>(source.Attributes, StringComparer.Ordinal),
            IsHidden = false,
            IsSelected = false
        };
    }

    private static string CreateUniqueFeatureId(ISet<string> reservedIds) {
        string id;
        do {
            id = Guid.NewGuid().ToString("N");
        } while (!reservedIds.Add(id));

        return id;
    }

    private static GeoPoint OffsetPoint(GeoPoint point, double longitudeOffset, double latitudeOffset) {
        return new GeoPoint(
            Math.Clamp(point.Longitude + longitudeOffset, -180.0, 180.0),
            GeoConverter.ClampLatitude(point.Latitude + latitudeOffset));
    }

    private static GeoPoint RotatePoint(GeoPoint point, GeoPoint center, double sin, double cos) {
        var x = point.Longitude - center.Longitude;
        var y = point.Latitude - center.Latitude;
        return new GeoPoint(
            Math.Clamp(center.Longitude + x * cos - y * sin, -180.0, 180.0),
            GeoConverter.ClampLatitude(center.Latitude + x * sin + y * cos));
    }

    private static double GetDominantOrthogonalAngle(IEnumerable<IReadOnlyList<GeoPoint>> parts) {
        var x = 0.0;
        var y = 0.0;
        foreach (var part in parts) {
            for (var i = 1; i < part.Count; i++) {
                var dx = part[i].Longitude - part[i - 1].Longitude;
                var dy = part[i].Latitude - part[i - 1].Latitude;
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length <= double.Epsilon) continue;

                var angle = Math.Atan2(dy, dx) * 4.0;
                x += Math.Cos(angle) * length;
                y += Math.Sin(angle) * length;
            }
        }

        if (Math.Abs(x) <= double.Epsilon && Math.Abs(y) <= double.Epsilon) return 0.0;
        return NormalizeOrthogonalAngle(Math.Atan2(y, x) / 4.0);
    }

    private static List<GeoPoint> OrthogonalizePart(IReadOnlyList<GeoPoint> part, double angleRadians) {
        if (part.Count < 2) return part.ToList();

        var isClosed = part.Count > 2 && part[0] == part[^1];
        var vertexCount = isClosed ? part.Count - 1 : part.Count;
        if (vertexCount < (isClosed ? 3 : 2)) return part.ToList();

        var origin = GetCenter(part.Take(vertexCount));
        var localPoints = part
            .Take(vertexCount)
            .Select(point => ToLocal(point, origin, angleRadians))
            .ToList();
        var segmentCount = isClosed ? vertexCount : vertexCount - 1;
        var segments = Enumerable
            .Range(0, segmentCount)
            .Select(index => CreateOrthogonalSegment(localPoints[index], localPoints[(index + 1) % vertexCount]))
            .ToList();

        var orthogonalized = new List<GeoPoint>(part.Count);
        for (var i = 0; i < vertexCount; i++) {
            var localPoint = isClosed
                ? GetOrthogonalizedClosedVertex(localPoints, segments, i)
                : GetOrthogonalizedOpenVertex(localPoints, segments, i);
            orthogonalized.Add(FromLocal(localPoint, origin, angleRadians));
        }

        if (isClosed) orthogonalized.Add(orthogonalized[0]);
        return orthogonalized;
    }

    private static GeoPoint GetCenter(IEnumerable<GeoPoint> points) {
        var longitude = 0.0;
        var latitude = 0.0;
        var count = 0;
        foreach (var point in points) {
            longitude += point.Longitude;
            latitude += point.Latitude;
            count++;
        }

        return count == 0
            ? new GeoPoint(0, 0)
            : new GeoPoint(longitude / count, latitude / count);
    }

    private static OrthogonalPoint GetOrthogonalizedClosedVertex(
        IReadOnlyList<OrthogonalPoint> points,
        IReadOnlyList<OrthogonalSegment> segments,
        int pointIndex) {
        return IntersectOrthogonalSegments(
            segments[(pointIndex + segments.Count - 1) % segments.Count],
            segments[pointIndex],
            points[pointIndex]);
    }

    private static OrthogonalPoint GetOrthogonalizedOpenVertex(
        IReadOnlyList<OrthogonalPoint> points,
        IReadOnlyList<OrthogonalSegment> segments,
        int pointIndex) {
        if (pointIndex == 0) return ProjectEndpoint(points[pointIndex], segments[0]);
        if (pointIndex == points.Count - 1) return ProjectEndpoint(points[pointIndex], segments[^1]);

        return IntersectOrthogonalSegments(segments[pointIndex - 1], segments[pointIndex], points[pointIndex]);
    }

    private static OrthogonalSegment CreateOrthogonalSegment(OrthogonalPoint start, OrthogonalPoint end) {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var isHorizontal = Math.Abs(dx) >= Math.Abs(dy);
        return isHorizontal
            ? new OrthogonalSegment(true, (start.Y + end.Y) / 2.0)
            : new OrthogonalSegment(false, (start.X + end.X) / 2.0);
    }

    private static OrthogonalPoint ProjectEndpoint(OrthogonalPoint point, OrthogonalSegment segment) {
        return segment.IsHorizontal
            ? point with { Y = segment.Value }
            : point with { X = segment.Value };
    }

    private static OrthogonalPoint IntersectOrthogonalSegments(
        OrthogonalSegment incoming,
        OrthogonalSegment outgoing,
        OrthogonalPoint fallback) {
        if (incoming.IsHorizontal && !outgoing.IsHorizontal) return new OrthogonalPoint(outgoing.Value, incoming.Value);
        if (!incoming.IsHorizontal && outgoing.IsHorizontal) return new OrthogonalPoint(incoming.Value, outgoing.Value);
        if (incoming.IsHorizontal) return new OrthogonalPoint(fallback.X, (incoming.Value + outgoing.Value) / 2.0);

        return new OrthogonalPoint((incoming.Value + outgoing.Value) / 2.0, fallback.Y);
    }

    private static OrthogonalPoint ToLocal(GeoPoint point, GeoPoint origin, double angleRadians) {
        var x = point.Longitude - origin.Longitude;
        var y = point.Latitude - origin.Latitude;
        var sin = Math.Sin(angleRadians);
        var cos = Math.Cos(angleRadians);
        return new OrthogonalPoint(x * cos + y * sin, -x * sin + y * cos);
    }

    private static GeoPoint FromLocal(OrthogonalPoint point, GeoPoint origin, double angleRadians) {
        var sin = Math.Sin(angleRadians);
        var cos = Math.Cos(angleRadians);
        return new GeoPoint(
            Math.Clamp(origin.Longitude + point.X * cos - point.Y * sin, -180.0, 180.0),
            GeoConverter.ClampLatitude(origin.Latitude + point.X * sin + point.Y * cos));
    }

    private static double NormalizeOrthogonalAngle(double radians) {
        while (radians > Math.PI / 4.0) radians -= Math.PI / 2.0;
        while (radians <= -Math.PI / 4.0) radians += Math.PI / 2.0;
        return radians;
    }

    private readonly record struct OrthogonalPoint(double X, double Y);

    private readonly record struct OrthogonalSegment(bool IsHorizontal, double Value);
}
