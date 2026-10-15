using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class BezierPenPathTests {
    private const double Tolerance = 0.5;

    // A quarter-circle-like cubic of roughly 110 m radius next to the equator, where degrees map to meters evenly.
    private static readonly GeoPoint ArcStart = new(0, 0);
    private static readonly GeoPoint ArcControl1 = new(0, 0.0005);
    private static readonly GeoPoint ArcControl2 = new(0.0005, 0.001);
    private static readonly GeoPoint ArcEnd = new(0.001, 0.001);

    private static readonly GeoPoint A = new(10.0000, 50.0000);
    private static readonly GeoPoint B = new(10.0010, 50.0005);
    private static readonly GeoPoint C = new(10.0020, 50.0000);
    private static readonly GeoPoint D = new(10.0030, 50.0010);
    private static readonly GeoPoint Handle = new(10.0014, 50.0012);

    [Fact]
    public void FlattenCubic_StraightCubicReturnsOnlyTheEndpoints() {
        var points = BezierPenPath.FlattenCubic(A, Interpolate(A, C, 0.25), Interpolate(A, C, 0.75), C, Tolerance);

        Assert.Equal(new[] { A, C }, points);
    }

    [Fact]
    public void FlattenCubic_SplitsWhenAControlPointOvershootsAnEndpointAlongTheChord() {
        var overshoot = Interpolate(A, C, 1.5);

        var points = BezierPenPath.FlattenCubic(A, A, overshoot, C, Tolerance);

        Assert.True(points.Count > 2);
    }

    [Theory]
    [InlineData(5.0)]
    [InlineData(0.5)]
    public void FlattenCubic_SamplesLieOnTheCurveAndThePolylineStaysWithinTolerance(double toleranceMeters) {
        var points = BezierPenPath.FlattenCubic(ArcStart, ArcControl1, ArcControl2, ArcEnd, toleranceMeters);

        Assert.Equal(ArcStart, points[0]);
        Assert.Equal(ArcEnd, points[^1]);
        var frame = LocalMetricFrame.Create(ArcStart);
        var polyline = points.Select(frame.Project).ToList();
        var curve = Enumerable.Range(0, 4001)
            .Select(i => frame.Project(Evaluate(ArcStart, ArcControl1, ArcControl2, ArcEnd, i / 4000.0)))
            .ToList();
        foreach (var sample in polyline) {
            Assert.True(DistanceToPolyline(sample, curve) <= 1e-3, "A flattened point left the curve.");
        }
        foreach (var point in curve) {
            Assert.True(DistanceToPolyline(point, polyline) <= toleranceMeters + 1e-6, "The curve strayed from the polyline.");
        }
    }

    [Fact]
    public void FlattenCubic_UsesMorePointsForATighterTolerance() {
        var coarse = BezierPenPath.FlattenCubic(ArcStart, ArcControl1, ArcControl2, ArcEnd, 5.0);
        var fine = BezierPenPath.FlattenCubic(ArcStart, ArcControl1, ArcControl2, ArcEnd, 0.5);

        Assert.True(coarse.Count > 2);
        Assert.True(fine.Count > coarse.Count);
    }

    [Fact]
    public void FlattenCubic_CapsSubdivisionAtDepthTwelve() {
        var points = BezierPenPath.FlattenCubic(ArcStart, ArcControl1, ArcControl2, ArcEnd, 1e-9);

        Assert.Equal(4097, points.Count);
    }

    [Fact]
    public void Flatten_TwoCornersFormAStraightSegment() {
        var path = new BezierPenPath();
        path.AddCorner(A);
        path.AddCorner(B);

        Assert.Equal(new[] { A, B }, path.Flatten(Tolerance));
    }

    [Fact]
    public void Flatten_InsertsInteriorPointsOnlyInCurvedSpans() {
        var path = new BezierPenPath();
        path.AddCorner(A);
        path.AddSmooth(B, Handle);
        path.AddCorner(C);
        path.AddCorner(D);

        var points = path.Flatten(Tolerance).ToList();

        var indexB = points.IndexOf(B);
        var indexC = points.IndexOf(C);
        Assert.Equal(A, points[0]);
        Assert.True(indexB > 1, "The span into the smooth anchor should be curved.");
        Assert.True(indexC - indexB > 1, "The span out of the smooth anchor should be curved.");
        Assert.Equal(indexC + 1, points.Count - 1);
        Assert.Equal(D, points[^1]);
    }

    [Fact]
    public void Flatten_ClosedAppendsTheReturnSegmentAndEndsAtTheFirstPoint() {
        var path = new BezierPenPath();
        path.AddCorner(A);
        path.AddCorner(B);
        path.AddCorner(C);

        Assert.Equal(new[] { A, B, C }, path.Flatten(Tolerance));
        Assert.Equal(new[] { A, B, C, A }, path.Flatten(Tolerance, closed: true));
    }

    [Fact]
    public void Flatten_ClosedReturnSegmentCurvesIntoTheFirstAnchorsIncomingHandle() {
        var path = new BezierPenPath();
        path.AddSmooth(A, Handle);
        path.AddCorner(B);
        path.AddCorner(C);

        var ring = path.Flatten(Tolerance, closed: true).ToList();

        var indexB = ring.IndexOf(B);
        var indexC = ring.IndexOf(C);
        Assert.Equal(A, ring[0]);
        Assert.Equal(A, ring[^1]);
        Assert.True(indexB > 1, "The span out of the smooth first anchor should be curved.");
        Assert.Equal(indexB + 1, indexC);
        Assert.True(ring.Count - 1 - indexC > 1, "The return segment should curve into the first anchor's incoming handle.");
    }

    [Fact]
    public void Flatten_RemovesConsecutiveDuplicatePoints() {
        var path = new BezierPenPath();
        path.AddCorner(A);
        path.AddCorner(A);
        path.AddCorner(B);
        path.AddCorner(C);
        path.AddCorner(A);

        Assert.Equal(new[] { A, B, C, A }, path.Flatten(Tolerance));
        Assert.Equal(new[] { A, B, C, A }, path.Flatten(Tolerance, closed: true));
    }

    [Fact]
    public void Flatten_ReturnsAnchorPointsAsIsBelowTwoAnchors() {
        var path = new BezierPenPath();
        Assert.Empty(path.Flatten(Tolerance));
        Assert.Empty(path.Flatten(Tolerance, closed: true));

        path.AddSmooth(A, Handle);

        Assert.Equal(new[] { A }, path.Flatten(Tolerance));
        Assert.Equal(new[] { A }, path.Flatten(Tolerance, closed: true));
    }

    [Fact]
    public void SetLastHandle_MakesTheLastAnchorSymmetric() {
        var path = new BezierPenPath();
        path.AddCorner(A);
        path.AddCorner(B);

        path.SetLastHandle(Handle);

        Assert.True(path.Anchors[0].IsCorner);
        var last = path.Anchors[^1];
        Assert.False(last.IsCorner);
        Assert.Equal(B, last.Point);
        Assert.Equal(Handle, last.HandleOut);
        Assert.Equal(
            new GeoPoint(2 * B.Longitude - Handle.Longitude, 2 * B.Latitude - Handle.Latitude),
            last.HandleIn);
    }

    [Fact]
    public void SetLastHandle_AtTheAnchorPointRestoresACorner() {
        var path = new BezierPenPath();
        path.AddSmooth(A, Handle);

        path.SetLastHandle(A);

        var anchor = Assert.Single(path.Anchors);
        Assert.True(anchor.IsCorner);
        Assert.Equal(A, anchor.Point);
    }

    [Fact]
    public void SetLastHandle_ThrowsWhenThePathIsEmpty() {
        Assert.Throws<InvalidOperationException>(() => new BezierPenPath().SetLastHandle(Handle));
    }

    [Fact]
    public void RemoveLastAndClear_TrackCountAndEmptiness() {
        var path = new BezierPenPath();
        Assert.True(path.IsEmpty);
        Assert.Equal(0, path.Count);
        Assert.False(path.RemoveLast());

        path.AddCorner(A);
        path.AddSmooth(B, Handle);
        Assert.False(path.IsEmpty);
        Assert.Equal(2, path.Count);

        Assert.True(path.RemoveLast());
        Assert.Equal(1, path.Count);
        Assert.Equal(A, Assert.Single(path.Anchors).Point);

        path.AddCorner(C);
        path.Clear();
        Assert.True(path.IsEmpty);
        Assert.Equal(0, path.Count);
        Assert.Empty(path.Anchors);
    }

    [Fact]
    public void Anchors_IsALiveReadOnlyView() {
        var path = new BezierPenPath();
        var anchors = path.Anchors;

        path.AddCorner(A);
        path.AddCorner(B);

        Assert.Equal(2, anchors.Count);
        Assert.Equal(B, anchors[1].Point);
        Assert.IsNotType<List<BezierAnchor>>(anchors);
    }

    [Fact]
    public void FlattenPending_IsEmptyWithoutAnchors() {
        Assert.Empty(new BezierPenPath().FlattenPending(B, Tolerance));
    }

    [Fact]
    public void FlattenPending_RunsFromTheLastAnchorToThePointer() {
        var path = new BezierPenPath();
        path.AddCorner(A);
        path.AddSmooth(B, Handle);

        var curved = path.FlattenPending(D, Tolerance);

        Assert.Equal(B, curved[0]);
        Assert.Equal(D, curved[^1]);
        Assert.True(curved.Count > 2, "The outgoing handle should bend the pending segment.");
        Assert.Equal(BezierPenPath.FlattenCubic(B, Handle, D, D, Tolerance), curved);

        path.AddCorner(C);

        Assert.Equal(new[] { C, D }, path.FlattenPending(D, Tolerance));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Flattening_RejectsNonPositiveOrNonFiniteTolerance(double toleranceMeters) {
        var path = new BezierPenPath();
        path.AddCorner(A);
        path.AddCorner(B);

        Assert.Throws<ArgumentOutOfRangeException>(() => path.Flatten(toleranceMeters));
        Assert.Throws<ArgumentOutOfRangeException>(() => path.FlattenPending(C, toleranceMeters));
        Assert.Throws<ArgumentOutOfRangeException>(() => BezierPenPath.FlattenCubic(A, A, B, B, toleranceMeters));
    }

    private static GeoPoint Interpolate(GeoPoint start, GeoPoint end, double fraction) {
        return new GeoPoint(
            start.Longitude + (end.Longitude - start.Longitude) * fraction,
            start.Latitude + (end.Latitude - start.Latitude) * fraction);
    }

    private static GeoPoint Evaluate(GeoPoint p0, GeoPoint c1, GeoPoint c2, GeoPoint p3, double t) {
        var u = 1.0 - t;
        var w0 = u * u * u;
        var w1 = 3.0 * u * u * t;
        var w2 = 3.0 * u * t * t;
        var w3 = t * t * t;
        return new GeoPoint(
            w0 * p0.Longitude + w1 * c1.Longitude + w2 * c2.Longitude + w3 * p3.Longitude,
            w0 * p0.Latitude + w1 * c1.Latitude + w2 * c2.Latitude + w3 * p3.Latitude);
    }

    private static double DistanceToPolyline(LocalPoint point, IReadOnlyList<LocalPoint> polyline) {
        var nearest = double.MaxValue;
        for (var i = 1; i < polyline.Count; i++) {
            nearest = Math.Min(nearest, DistanceToSegment(point, polyline[i - 1], polyline[i]));
        }
        return nearest;
    }

    private static double DistanceToSegment(LocalPoint point, LocalPoint start, LocalPoint end) {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        var along = lengthSquared == 0
            ? 0.0
            : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0.0, 1.0);
        var offsetX = point.X - (start.X + dx * along);
        var offsetY = point.Y - (start.Y + dy * along);
        return Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
    }
}
