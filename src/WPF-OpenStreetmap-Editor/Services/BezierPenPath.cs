using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

/// <summary>
/// Mutable pen-tool path: a sequence of anchors that flattens to a polyline in document coordinates.
/// Flattening never mutates the anchors or the points passed in.
/// </summary>
public sealed class BezierPenPath {
    // Twelve halvings cut a segment 4096-fold; splitting further only chases rounding noise in the metric frame.
    private const int MaxSubdivisionDepth = 12;

    private readonly List<BezierAnchor> _anchors = [];

    public BezierPenPath() {
        Anchors = _anchors.AsReadOnly();
    }

    /// <summary>Live read-only view of the anchors; it reflects later edits to the path.</summary>
    public IReadOnlyList<BezierAnchor> Anchors { get; }

    public int Count => _anchors.Count;

    public bool IsEmpty => _anchors.Count == 0;

    public void AddCorner(GeoPoint point) {
        _anchors.Add(BezierAnchor.Corner(point));
    }

    public void AddSmooth(GeoPoint point, GeoPoint handleOut) {
        _anchors.Add(BezierAnchor.Symmetric(point, handleOut));
    }

    /// <summary>
    /// Turns the last anchor into a symmetric smooth anchor with the given outgoing handle, as a press-and-drag after
    /// a click does. A handle equal to the anchor point makes it a corner again.
    /// </summary>
    /// <exception cref="InvalidOperationException">The path has no anchor.</exception>
    public void SetLastHandle(GeoPoint handleOut) {
        if (_anchors.Count == 0) throw new InvalidOperationException("The path has no anchor to set a handle on.");

        var point = _anchors[^1].Point;
        _anchors[^1] = handleOut == point
            ? BezierAnchor.Corner(point)
            : BezierAnchor.Symmetric(point, handleOut);
    }

    public bool RemoveLast() {
        if (_anchors.Count == 0) return false;

        _anchors.RemoveAt(_anchors.Count - 1);
        return true;
    }

    public void Clear() {
        _anchors.Clear();
    }

    /// <summary>
    /// Flattens the committed anchors. Each anchor pair forms a cubic whose missing handles default to the anchor
    /// points, so two corners produce a straight segment. With <paramref name="closed"/> the segment back to the
    /// first anchor is appended and the ring ends with a copy of its first point. Consecutive duplicate points are
    /// removed, so a path whose anchors all coincide collapses to a single point. Fewer than two anchors return the
    /// anchor points as-is.
    /// </summary>
    public IReadOnlyList<GeoPoint> Flatten(double toleranceMeters, bool closed = false) {
        ValidateTolerance(toleranceMeters);
        if (_anchors.Count < 2) return _anchors.Select(static anchor => anchor.Point).ToList();

        var points = new List<GeoPoint> { _anchors[0].Point };
        for (var i = 1; i < _anchors.Count; i++) {
            AppendSegment(points, _anchors[i - 1], _anchors[i], toleranceMeters);
        }
        if (closed) AppendSegment(points, _anchors[^1], _anchors[0], toleranceMeters);
        return points;
    }

    /// <summary>
    /// Preview of the segment from the last anchor to <paramref name="pointer"/>: it leaves along the last anchor's
    /// outgoing handle and arrives straight at the pointer. Empty when the path has no anchor.
    /// </summary>
    public IReadOnlyList<GeoPoint> FlattenPending(GeoPoint pointer, double toleranceMeters) {
        ValidateTolerance(toleranceMeters);
        if (_anchors.Count == 0) return [];

        var last = _anchors[^1];
        return FlattenCubic(last.Point, last.HandleOut ?? last.Point, pointer, pointer, toleranceMeters);
    }

    /// <summary>
    /// Adaptively flattens one cubic in a metric frame centered on <paramref name="p0"/>: the curve is split with de
    /// Casteljau until both control points lie within <paramref name="toleranceMeters"/> of the chord, which by the
    /// convex hull property bounds how far the curve strays from that chord. Returns points from
    /// <paramref name="p0"/> to <paramref name="p3"/> inclusive; a straight cubic returns just the two endpoints.
    /// </summary>
    public static IReadOnlyList<GeoPoint> FlattenCubic(GeoPoint p0, GeoPoint c1, GeoPoint c2, GeoPoint p3, double toleranceMeters) {
        ValidateTolerance(toleranceMeters);

        var frame = LocalMetricFrame.Create(p0);
        var pieceEnds = new List<LocalPoint>();
        Subdivide(frame.Project(p0), frame.Project(c1), frame.Project(c2), frame.Project(p3), toleranceMeters, 0, pieceEnds);

        // The endpoints are returned verbatim so that a round trip through the frame cannot shift them.
        var points = new List<GeoPoint>(pieceEnds.Count + 1) { p0 };
        for (var i = 0; i < pieceEnds.Count - 1; i++) {
            points.Add(frame.Unproject(pieceEnds[i]));
        }
        points.Add(p3);
        return points;
    }

    private static void AppendSegment(List<GeoPoint> points, BezierAnchor start, BezierAnchor end, double toleranceMeters) {
        var segment = FlattenCubic(start.Point, start.HandleOut ?? start.Point, end.HandleIn ?? end.Point, end.Point, toleranceMeters);
        foreach (var point in segment) {
            if (point != points[^1]) points.Add(point);
        }
    }

    /// <summary>Appends the end point of every flat piece of the cubic in curve order; p0 itself is not appended.</summary>
    private static void Subdivide(
        LocalPoint p0,
        LocalPoint c1,
        LocalPoint c2,
        LocalPoint p3,
        double toleranceMeters,
        int depth,
        List<LocalPoint> pieceEnds) {
        if (depth >= MaxSubdivisionDepth || IsFlat(p0, c1, c2, p3, toleranceMeters)) {
            pieceEnds.Add(p3);
            return;
        }

        var p01 = Midpoint(p0, c1);
        var p12 = Midpoint(c1, c2);
        var p23 = Midpoint(c2, p3);
        var p012 = Midpoint(p01, p12);
        var p123 = Midpoint(p12, p23);
        var middle = Midpoint(p012, p123);
        Subdivide(p0, p01, p012, middle, toleranceMeters, depth + 1, pieceEnds);
        Subdivide(middle, p123, p23, p3, toleranceMeters, depth + 1, pieceEnds);
    }

    // Measured against the chord segment rather than its infinite line so that a control point overshooting an
    // endpoint along the chord still forces a split instead of being drawn as a straight line.
    private static bool IsFlat(LocalPoint p0, LocalPoint c1, LocalPoint c2, LocalPoint p3, double toleranceMeters) {
        return DistanceToSegment(c1, p0, p3) <= toleranceMeters && DistanceToSegment(c2, p0, p3) <= toleranceMeters;
    }

    private static double DistanceToSegment(LocalPoint point, LocalPoint start, LocalPoint end) {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        var along = lengthSquared <= double.Epsilon
            ? 0.0
            : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0.0, 1.0);
        var offsetX = point.X - (start.X + dx * along);
        var offsetY = point.Y - (start.Y + dy * along);
        return Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
    }

    private static LocalPoint Midpoint(LocalPoint first, LocalPoint second) {
        return new LocalPoint((first.X + second.X) / 2.0, (first.Y + second.Y) / 2.0);
    }

    private static void ValidateTolerance(double toleranceMeters) {
        if (!double.IsFinite(toleranceMeters) || toleranceMeters <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(toleranceMeters),
                toleranceMeters,
                "The flattening tolerance must be a positive, finite number of meters.");
        }
    }
}
