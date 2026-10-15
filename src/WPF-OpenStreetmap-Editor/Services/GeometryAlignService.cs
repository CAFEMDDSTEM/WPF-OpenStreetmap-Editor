using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public enum MirrorAxis {
    /// <summary>North-south axis: east and west swap.</summary>
    Vertical,
    /// <summary>East-west axis: north and south swap.</summary>
    Horizontal
}

public sealed record GeometryEditResult(List<List<GeoPoint>>? Parts, string? Error) {
    public bool IsSuccess => Parts is not null;
}

/// <summary>
/// JOSM-style vertex alignment tools. Each part is computed in a <see cref="LocalMetricFrame"/> centered on its
/// vertex centroid; a part with more than two points whose first and last point coincide is a closed ring whose
/// closing vertex is excluded from the computation and re-appended. Inputs are never mutated.
/// </summary>
public static class GeometryAlignService {
    // Angular gaps that are an exact multiple of the step size must not gain an extra subdivision through
    // floating-point noise in atan2.
    private const double AngleTolerance = 1e-9;
    private const double FullTurn = 2.0 * Math.PI;

    public static GeometryEditResult AlignInCircle(IEnumerable<IEnumerable<GeoPoint>> parts) {
        return TransformParts(parts, AlignPartInCircle);
    }

    public static GeometryEditResult AlignInLine(IEnumerable<IEnumerable<GeoPoint>> parts) {
        return TransformParts(parts, AlignPartInLine);
    }

    public static GeometryEditResult DistributeVertices(IEnumerable<IEnumerable<GeoPoint>> parts) {
        return TransformParts(parts, DistributePartVertices);
    }

    public static GeometryEditResult Mirror(IEnumerable<IEnumerable<GeoPoint>> parts, MirrorAxis axis) {
        ArgumentNullException.ThrowIfNull(parts);

        // Unlike the other tools, all parts share one axis so that holes keep their place inside the outer ring.
        var partList = parts.Select(static part => part.ToList()).ToList();
        var points = partList.SelectMany(static part => part).ToList();
        if (points.Count == 0) return Failure("The geometry has no vertices.");

        var frame = LocalMetricFrame.Create(GetCentroid(points));
        var flipEastWest = axis == MirrorAxis.Vertical;
        var coordinates = points.Select(frame.Project).Select(point => flipEastWest ? point.X : point.Y).ToList();
        var minimum = coordinates.Min();
        var maximum = coordinates.Max();
        if (maximum - minimum <= double.Epsilon) return new GeometryEditResult(partList, null);

        var reflectedSum = minimum + maximum;
        var mirrored = partList
            .Select(part => part
                .Select(point => {
                    var local = frame.Project(point);
                    return frame.Unproject(flipEastWest
                        ? local with { X = reflectedSum - local.X }
                        : local with { Y = reflectedSum - local.Y });
                })
                .ToList())
            .ToList();
        return new GeometryEditResult(mirrored, null);
    }

    public static GeometryEditResult CreateCircle(IEnumerable<IEnumerable<GeoPoint>> parts, int minimumVertexCount = 16) {
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumVertexCount, 3);

        return TransformParts(parts, part => CreateCirclePart(part, minimumVertexCount));
    }

    private static GeometryEditResult TransformParts(
        IEnumerable<IEnumerable<GeoPoint>> parts,
        Func<PartContext, PartResult> transform) {
        ArgumentNullException.ThrowIfNull(parts);

        var result = new List<List<GeoPoint>>();
        foreach (var part in parts) {
            var points = part.ToList();
            if (points.Count == 0) return Failure("The geometry has no vertices.");

            var isClosed = points.Count > 2 && points[0] == points[^1];
            var vertices = isClosed ? points.GetRange(0, points.Count - 1) : points;
            var frame = LocalMetricFrame.Create(GetCentroid(vertices));
            var transformed = transform(new PartContext(vertices, vertices.Select(frame.Project).ToList(), isClosed, frame));
            if (transformed.Vertices is null) return Failure(transformed.Error!);

            if (transformed.IsClosed) transformed.Vertices.Add(transformed.Vertices[0]);
            result.Add(transformed.Vertices);
        }

        return result.Count == 0
            ? Failure("The geometry has no parts.")
            : new GeometryEditResult(result, null);
    }

    private static PartResult AlignPartInCircle(PartContext part) {
        if (part.Vertices.Distinct().Count() < 3) {
            return PartResult.Failure("Aligning in a circle needs at least three distinct vertices.");
        }

        var center = GetCentroid(part.Local);
        var radius = part.Local.Average(point => Distance(point, center));
        var aligned = new List<LocalPoint>(part.Local.Count);
        foreach (var point in part.Local) {
            var distance = Distance(point, center);
            if (distance <= double.Epsilon) {
                return PartResult.Failure("A vertex lies at the circle center and has no direction to move along.");
            }

            var scale = radius / distance;
            aligned.Add(new LocalPoint(
                center.X + (point.X - center.X) * scale,
                center.Y + (point.Y - center.Y) * scale));
        }

        return PartResult.Success(part.Unproject(aligned), part.IsClosed);
    }

    private static PartResult AlignPartInLine(PartContext part) {
        // Projecting a ring onto one axis collapses the area to nothing, which is never what a mapper wants.
        if (part.IsClosed) return PartResult.Failure("Aligning in a line needs an open line; orthogonalize areas instead.");
        if (part.Vertices.Count < 3) return PartResult.Failure("Aligning in a line needs at least three vertices.");

        // Endpoints are usually junction nodes, so they define the axis and are returned untouched.
        var start = part.Local[0];
        var direction = GetDirection(start, part.Local[^1]);
        if (direction is null) return PartResult.Failure("The first and last vertices coincide, so the line has no direction.");

        var aligned = new List<GeoPoint>(part.Vertices.Count) { part.Vertices[0] };
        for (var i = 1; i < part.Local.Count - 1; i++) {
            aligned.Add(part.Frame.Unproject(ProjectOntoLine(part.Local[i], start, direction.Value)));
        }
        aligned.Add(part.Vertices[^1]);
        return PartResult.Success(aligned, isClosed: false);
    }

    private static PartResult DistributePartVertices(PartContext part) {
        if (part.IsClosed) return PartResult.Failure("Vertices can only be distributed along an open line.");
        if (part.Vertices.Count < 3) return PartResult.Failure("Distributing vertices needs at least three vertices.");

        var start = part.Local[0];
        var end = part.Local[^1];
        var segmentCount = part.Local.Count - 1;
        var distributed = new List<GeoPoint>(part.Vertices.Count) { part.Vertices[0] };
        for (var i = 1; i < segmentCount; i++) {
            var fraction = (double)i / segmentCount;
            distributed.Add(part.Frame.Unproject(new LocalPoint(
                start.X + (end.X - start.X) * fraction,
                start.Y + (end.Y - start.Y) * fraction)));
        }
        distributed.Add(part.Vertices[^1]);
        return PartResult.Success(distributed, isClosed: false);
    }

    private static PartResult CreateCirclePart(PartContext part, int minimumVertexCount) {
        var distinct = part.Local.Distinct().ToList();
        if (distinct.Count < 2) return PartResult.Failure("Creating a circle needs at least two distinct vertices.");

        LocalPoint center;
        double radius;
        if (distinct.Count == 2) {
            // Two vertices span a diameter.
            center = new LocalPoint((distinct[0].X + distinct[1].X) / 2.0, (distinct[0].Y + distinct[1].Y) / 2.0);
            radius = Distance(distinct[0], distinct[1]) / 2.0;
        } else if (!TryFitCircle(distinct, out center, out radius)) {
            return PartResult.Failure("The vertices are collinear, so no circle passes through them.");
        }

        if (distinct.Any(point => Distance(point, center) <= double.Epsilon)) {
            return PartResult.Failure("A vertex lies at the circle center and has no direction to move along.");
        }

        // Keep the original winding and start at the original first vertex so vertex order stays recognizable.
        var angles = distinct.Select(point => Math.Atan2(point.Y - center.Y, point.X - center.X)).ToList();
        var clockwise = GetSignedArea(distinct) < 0;
        var order = Enumerable.Range(0, distinct.Count)
            .OrderBy(index => clockwise ? -angles[index] : angles[index])
            .ToList();
        var startPosition = order.IndexOf(0);
        order = order.Skip(startPosition).Concat(order.Take(startPosition)).ToList();

        var step = FullTurn / minimumVertexCount;
        var ring = new List<LocalPoint>();
        for (var i = 0; i < order.Count; i++) {
            var angle = angles[order[i]];
            var nextAngle = angles[order[(i + 1) % order.Count]];
            ring.Add(GetPointOnCircle(center, radius, angle));

            var gap = NormalizeAngle(clockwise ? angle - nextAngle : nextAngle - angle);
            // Two kept vertices at the same angle have no gap between them; FullTurn here is rounding noise.
            if (gap >= FullTurn - AngleTolerance) gap = 0.0;
            var subdivisions = Math.Max(1, (int)Math.Ceiling(gap / step - AngleTolerance));
            for (var k = 1; k < subdivisions; k++) {
                var offset = gap * k / subdivisions;
                ring.Add(GetPointOnCircle(center, radius, clockwise ? angle - offset : angle + offset));
            }
        }

        return PartResult.Success(part.Unproject(ring), isClosed: true);
    }

    private static GeoPoint GetCentroid(IReadOnlyList<GeoPoint> points) {
        return new GeoPoint(
            points.Average(static point => point.Longitude),
            points.Average(static point => point.Latitude));
    }

    private static LocalPoint GetCentroid(IReadOnlyList<LocalPoint> points) {
        return new LocalPoint(
            points.Average(static point => point.X),
            points.Average(static point => point.Y));
    }

    private static double Distance(LocalPoint first, LocalPoint second) {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static LocalPoint? GetDirection(LocalPoint start, LocalPoint end) {
        var length = Distance(start, end);
        if (length <= double.Epsilon) return null;

        return new LocalPoint((end.X - start.X) / length, (end.Y - start.Y) / length);
    }

    private static LocalPoint ProjectOntoLine(LocalPoint point, LocalPoint origin, LocalPoint unitDirection) {
        var along = (point.X - origin.X) * unitDirection.X + (point.Y - origin.Y) * unitDirection.Y;
        return new LocalPoint(origin.X + unitDirection.X * along, origin.Y + unitDirection.Y * along);
    }

    private static double GetSignedArea(IReadOnlyList<LocalPoint> points) {
        var sum = 0.0;
        for (var i = 0; i < points.Count; i++) {
            var current = points[i];
            var next = points[(i + 1) % points.Count];
            sum += current.X * next.Y - next.X * current.Y;
        }
        return sum / 2.0;
    }

    /// <summary>
    /// Algebraic (Kåsa) least-squares circle through the points: minimizes Σ(x² + y² + Dx + Ey + F)².
    /// Exact for three points, and unlike a centroid it recovers the circle an arc lies on, so a
    /// partial road curve or a few surveyed points can be completed into a full circle.
    /// Returns false for collinear input, where the normal equations are singular.
    /// </summary>
    private static bool TryFitCircle(IReadOnlyList<LocalPoint> points, out LocalPoint center, out double radius) {
        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0, sxz = 0, syz = 0, sz = 0;
        foreach (var point in points) {
            var z = point.X * point.X + point.Y * point.Y;
            sx += point.X;
            sy += point.Y;
            sxx += point.X * point.X;
            syy += point.Y * point.Y;
            sxy += point.X * point.Y;
            sxz += point.X * z;
            syz += point.Y * z;
            sz += z;
        }

        var n = points.Count;
        // Normal equations for (D, E, F) solved with Cramer's rule.
        var determinant = Determinant(sxx, sxy, sx, sxy, syy, sy, sx, sy, n);
        var scale = Math.Max(sxx + syy, double.Epsilon);
        center = default;
        radius = 0;
        if (Math.Abs(determinant) <= 1e-9 * scale * scale) return false;

        var d = Determinant(-sxz, sxy, sx, -syz, syy, sy, -sz, sy, n) / determinant;
        var e = Determinant(sxx, -sxz, sx, sxy, -syz, sy, sx, -sz, n) / determinant;
        var f = Determinant(sxx, sxy, -sxz, sxy, syy, -syz, sx, sy, -sz) / determinant;
        center = new LocalPoint(-d / 2.0, -e / 2.0);
        var radiusSquared = center.X * center.X + center.Y * center.Y - f;
        if (radiusSquared <= double.Epsilon) return false;

        radius = Math.Sqrt(radiusSquared);
        return true;
    }

    private static double Determinant(
        double a, double b, double c,
        double d, double e, double f,
        double g, double h, double i) {
        return a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
    }

    private static LocalPoint GetPointOnCircle(LocalPoint center, double radius, double angle) {
        return new LocalPoint(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
    }

    private static double NormalizeAngle(double radians) {
        var normalized = radians % FullTurn;
        return normalized < 0 ? normalized + FullTurn : normalized;
    }

    private static GeometryEditResult Failure(string error) => new(null, error);

    /// <summary>A part's vertices without the closing duplicate, projected into a frame centered on their centroid.</summary>
    private sealed record PartContext(
        IReadOnlyList<GeoPoint> Vertices,
        IReadOnlyList<LocalPoint> Local,
        bool IsClosed,
        LocalMetricFrame Frame) {
        public List<GeoPoint> Unproject(IEnumerable<LocalPoint> points) => points.Select(Frame.Unproject).ToList();
    }

    private sealed record PartResult(List<GeoPoint>? Vertices, bool IsClosed, string? Error) {
        public static PartResult Success(List<GeoPoint> vertices, bool isClosed) => new(vertices, isClosed, null);

        public static PartResult Failure(string error) => new(null, false, error);
    }
}
