using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class GeometryAlignServiceTests {
    private const double MeterTolerance = 1e-6;

    [Fact]
    public void AlignInCircle_PlacesVerticesAtMeanRadiusAndKeepsRingClosed() {
        var ring = CreateSkewedRing();

        var result = GeometryAlignService.AlignInCircle([ring]);

        Assert.True(result.IsSuccess, result.Error);
        var aligned = Assert.Single(result.Parts!);
        Assert.Equal(ring.Count, aligned.Count);
        Assert.Equal(aligned[0], aligned[^1]);
        var frame = CreateFrame(ring.Take(ring.Count - 1));
        var center = new LocalPoint(0, 0);
        var originalRadii = ring.Take(ring.Count - 1).Select(point => Distance(frame.Project(point), center)).ToList();
        var radii = aligned.Take(aligned.Count - 1).Select(point => Distance(frame.Project(point), center)).ToList();
        Assert.All(radii, radius => Assert.Equal(originalRadii.Average(), radius, MeterTolerance));
        for (var i = 0; i < radii.Count; i++) {
            Assert.Equal(Angle(frame.Project(ring[i]), center), Angle(frame.Project(aligned[i]), center), 1e-9);
        }
    }

    [Fact]
    public void AlignInCircle_RequiresThreeDistinctVertices() {
        var result = GeometryAlignService.AlignInCircle([[new GeoPoint(10, 50), new GeoPoint(10.001, 50), new GeoPoint(10, 50)]]);

        Assert.False(result.IsSuccess);
        Assert.Contains("three distinct vertices", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AlignInLine_KeepsEndpointsAndProjectsInteriorOntoTheirAxis() {
        var line = new List<GeoPoint> {
            new(10.0000, 50.0000),
            new(10.0004, 50.0003),
            new(10.0009, 49.9998),
            new(10.0014, 50.0002)
        };

        var result = GeometryAlignService.AlignInLine([line]);

        Assert.True(result.IsSuccess, result.Error);
        var aligned = Assert.Single(result.Parts!);
        Assert.Equal(line.Count, aligned.Count);
        Assert.Equal(line[0], aligned[0]);
        Assert.Equal(line[^1], aligned[^1]);
        Assert.NotEqual(line[1], aligned[1]);
        var frame = CreateFrame(line);
        foreach (var point in aligned) {
            Assert.Equal(0, DistanceToLine(frame, point, aligned[0], aligned[^1]), MeterTolerance);
        }
    }

    [Fact]
    public void AlignInLine_RefusesClosedRings() {
        var ring = new List<GeoPoint> {
            new(10.0000, 50.0000),
            new(10.0010, 50.0006),
            new(10.0020, 50.0013),
            new(10.0010, 50.0007),
            new(10.0000, 50.0000)
        };

        var result = GeometryAlignService.AlignInLine([ring]);

        Assert.False(result.IsSuccess);
        Assert.Contains("open line", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AlignInLine_RequiresThreeVerticesForOpenLine() {
        var result = GeometryAlignService.AlignInLine([[new GeoPoint(10, 50), new GeoPoint(10.001, 50)]]);

        Assert.False(result.IsSuccess);
        Assert.Contains("three vertices", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void DistributeVertices_SpacesInteriorVerticesEquallyBetweenFixedEndpoints() {
        var line = new List<GeoPoint> {
            new(10.0000, 50.0000),
            new(10.0001, 50.0003),
            new(10.0007, 50.0001),
            new(10.0009, 50.0004),
            new(10.0014, 50.0002)
        };

        var result = GeometryAlignService.DistributeVertices([line]);

        Assert.True(result.IsSuccess, result.Error);
        var distributed = Assert.Single(result.Parts!);
        Assert.Equal(line.Count, distributed.Count);
        Assert.Equal(line[0], distributed[0]);
        Assert.Equal(line[^1], distributed[^1]);
        var frame = CreateFrame(line);
        var local = distributed.Select(frame.Project).ToList();
        var expectedSpacing = Distance(local[0], local[^1]) / (local.Count - 1);
        for (var i = 1; i < local.Count; i++) {
            Assert.Equal(expectedSpacing, Distance(local[i - 1], local[i]), MeterTolerance);
            Assert.Equal(0, DistanceToLine(frame, distributed[i], line[0], line[^1]), MeterTolerance);
        }
    }

    [Fact]
    public void DistributeVertices_RejectsClosedRing() {
        var result = GeometryAlignService.DistributeVertices([CreateSkewedRing()]);

        Assert.False(result.IsSuccess);
        Assert.Contains("open line", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MirrorAxis.Vertical)]
    [InlineData(MirrorAxis.Horizontal)]
    public void Mirror_ReflectsAcrossBoundingBoxCenterAndIsAnInvolution(MirrorAxis axis) {
        var ring = CreateSkewedRing();

        var result = GeometryAlignService.Mirror([ring], axis);

        Assert.True(result.IsSuccess, result.Error);
        var mirrored = Assert.Single(result.Parts!);
        Assert.Equal(ring.Count, mirrored.Count);
        Assert.Equal(mirrored[0], mirrored[^1]);
        var frame = CreateFrame(ring);
        var local = ring.Select(frame.Project).ToList();
        var flipEastWest = axis == MirrorAxis.Vertical;
        var reflectedSum = flipEastWest
            ? local.Min(static point => point.X) + local.Max(static point => point.X)
            : local.Min(static point => point.Y) + local.Max(static point => point.Y);
        for (var i = 0; i < ring.Count; i++) {
            var expected = flipEastWest
                ? new LocalPoint(reflectedSum - local[i].X, local[i].Y)
                : new LocalPoint(local[i].X, reflectedSum - local[i].Y);
            Assert.Equal(0, Distance(expected, frame.Project(mirrored[i])), MeterTolerance);
        }

        var restored = Assert.Single(GeometryAlignService.Mirror([mirrored], axis).Parts!);
        for (var i = 0; i < ring.Count; i++) {
            Assert.Equal(ring[i].Longitude, restored[i].Longitude, 1e-9);
            Assert.Equal(ring[i].Latitude, restored[i].Latitude, 1e-9);
        }
    }

    [Fact]
    public void Mirror_UsesOneAxisForAllPartsSoHolesStayInsideTheOuterRing() {
        var outerCenter = new GeoPoint(10, 50);
        var outer = CreateSquare(outerCenter, 0.0020);
        var hole = CreateSquare(new GeoPoint(10.0005, 50.0003), 0.0002);

        var result = GeometryAlignService.Mirror([outer, hole], MirrorAxis.Vertical);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Parts!.Count);
        // The outer square is symmetric around its center, so the shared axis is X = 0 in this frame.
        var frame = LocalMetricFrame.Create(outerCenter);
        var mirroredHoleCenter = Centroid(result.Parts[1].Take(4).Select(frame.Project).ToList());
        var holeCenter = Centroid(hole.Take(4).Select(frame.Project).ToList());
        Assert.Equal(-holeCenter.X, mirroredHoleCenter.X, MeterTolerance);
        Assert.Equal(holeCenter.Y, mirroredHoleCenter.Y, MeterTolerance);
    }

    [Fact]
    public void Mirror_LeavesSingleVertexUnchanged() {
        var point = new GeoPoint(10.123, 50.456);

        var result = GeometryAlignService.Mirror([[point]], MirrorAxis.Vertical);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(point, Assert.Single(Assert.Single(result.Parts!)));
    }

    [Fact]
    public void CreateCircle_FromSquareProducesRegularClosedRingThroughOriginalVertices() {
        var square = CreateSquare(new GeoPoint(10, 50), 0.0010);

        var result = GeometryAlignService.CreateCircle([square]);

        Assert.True(result.IsSuccess, result.Error);
        var ring = Assert.Single(result.Parts!);
        Assert.Equal(17, ring.Count);
        Assert.Equal(ring[0], ring[^1]);
        var frame = CreateFrame(square.Take(4));
        var center = new LocalPoint(0, 0);
        var radius = Distance(frame.Project(square[0]), center);
        Assert.All(ring, point => Assert.Equal(radius, Distance(frame.Project(point), center), MeterTolerance));
        Assert.Equal(0, Distance(frame.Project(square[0]), frame.Project(ring[0])), MeterTolerance);
        foreach (var corner in square.Take(4)) {
            Assert.Contains(ring, point => Distance(frame.Project(point), frame.Project(corner)) < MeterTolerance);
        }
        var vertices = ring.Take(ring.Count - 1).Select(frame.Project).ToList();
        for (var i = 1; i < vertices.Count; i++) {
            Assert.Equal(Distance(vertices[0], vertices[1]), Distance(vertices[i - 1], vertices[i]), MeterTolerance);
        }
    }

    [Fact]
    public void CreateCircle_PreservesWindingDirection() {
        var counterClockwise = CreateSquare(new GeoPoint(10, 50), 0.0010);
        var clockwise = counterClockwise.AsEnumerable().Reverse().ToList();
        var frame = CreateFrame(counterClockwise.Take(4));

        var counterClockwiseRing = Assert.Single(GeometryAlignService.CreateCircle([counterClockwise]).Parts!);
        var clockwiseRing = Assert.Single(GeometryAlignService.CreateCircle([clockwise]).Parts!);

        Assert.True(SignedArea(counterClockwiseRing.Select(frame.Project).ToList()) > 0);
        Assert.True(SignedArea(clockwiseRing.Select(frame.Project).ToList()) < 0);
        Assert.Equal(0, Distance(frame.Project(clockwise[0]), frame.Project(clockwiseRing[0])), MeterTolerance);
    }

    [Fact]
    public void CreateCircle_FromArcRecoversTheFullCircle() {
        // A quarter arc of a 50 m circle: the centroid of these points is nowhere near the true center.
        var origin = new GeoPoint(10, 50);
        var frame = LocalMetricFrame.Create(origin);
        var trueCenter = new LocalPoint(0, 0);
        var arc = new[] { 0.0, 30.0, 60.0, 90.0 }
            .Select(degrees => degrees * Math.PI / 180.0)
            .Select(angle => frame.Unproject(new LocalPoint(50 * Math.Cos(angle), 50 * Math.Sin(angle))))
            .ToList();

        var result = GeometryAlignService.CreateCircle([arc]);

        Assert.True(result.IsSuccess, result.Error);
        var ring = Assert.Single(result.Parts!);
        Assert.Equal(ring[0], ring[^1]);
        Assert.True(ring.Count >= 17);
        Assert.All(ring, point => Assert.Equal(50, Distance(frame.Project(point), trueCenter), 1e-3));
        Assert.Equal(0, Distance(frame.Project(arc[0]), frame.Project(ring[0])), 1e-3);
        foreach (var arcPoint in arc) {
            Assert.Contains(ring, point => Distance(frame.Project(point), frame.Project(arcPoint)) < 1e-3);
        }
    }

    [Fact]
    public void CreateCircle_RejectsCollinearVertices() {
        var result = GeometryAlignService.CreateCircle([[new GeoPoint(10, 50), new GeoPoint(10.001, 50), new GeoPoint(10.002, 50)]]);

        Assert.False(result.IsSuccess);
        Assert.Contains("collinear", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateCircle_FromTwoVerticesUsesThemAsDiameter() {
        var first = new GeoPoint(10.0000, 50.0000);
        var second = new GeoPoint(10.0014, 50.0000);

        var result = GeometryAlignService.CreateCircle([[first, second]]);

        Assert.True(result.IsSuccess, result.Error);
        var ring = Assert.Single(result.Parts!);
        Assert.Equal(17, ring.Count);
        Assert.Equal(ring[0], ring[^1]);
        var frame = CreateFrame([first, second]);
        var diameter = Distance(frame.Project(first), frame.Project(second));
        Assert.Equal(0, Distance(frame.Project(first), frame.Project(ring[0])), MeterTolerance);
        Assert.Equal(0, Distance(frame.Project(second), frame.Project(ring[8])), MeterTolerance);
        var vertices = ring.Take(ring.Count - 1).Select(frame.Project).ToList();
        var widest = vertices.SelectMany(a => vertices.Select(b => Distance(a, b))).Max();
        Assert.Equal(diameter, widest, MeterTolerance);
        Assert.All(vertices, point => Assert.Equal(diameter / 2, Distance(point, Centroid(vertices)), MeterTolerance));
    }

    [Fact]
    public void CreateCircle_RequiresTwoDistinctVertices() {
        var point = new GeoPoint(10, 50);

        var result = GeometryAlignService.CreateCircle([[point, point]]);

        Assert.False(result.IsSuccess);
        Assert.Contains("two distinct vertices", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateCircle_RejectsMinimumVertexCountBelowThree() {
        Assert.Throws<ArgumentOutOfRangeException>(() => GeometryAlignService.CreateCircle([CreateSkewedRing()], 2));
    }

    [Fact]
    public void Operations_RejectEmptyGeometry() {
        var operations = new Func<IEnumerable<IEnumerable<GeoPoint>>, GeometryEditResult>[] {
            GeometryAlignService.AlignInCircle,
            GeometryAlignService.AlignInLine,
            GeometryAlignService.DistributeVertices,
            parts => GeometryAlignService.Mirror(parts, MirrorAxis.Horizontal),
            parts => GeometryAlignService.CreateCircle(parts)
        };

        foreach (var operation in operations) {
            Assert.False(operation([]).IsSuccess);
            Assert.False(operation([[]]).IsSuccess);
        }
    }

    [Fact]
    public void Operations_DoNotMutateInput() {
        var ring = CreateSkewedRing();
        var line = ring.Take(4).ToList();
        var parts = new List<List<GeoPoint>> { ring, line };
        var snapshot = parts.Select(static part => part.ToList()).ToList();

        GeometryAlignService.AlignInCircle(parts);
        GeometryAlignService.AlignInLine(parts);
        GeometryAlignService.DistributeVertices([line]);
        GeometryAlignService.Mirror(parts, MirrorAxis.Vertical);
        GeometryAlignService.CreateCircle(parts);

        Assert.Equal(snapshot.Count, parts.Count);
        for (var i = 0; i < parts.Count; i++) {
            Assert.Equal(snapshot[i], parts[i]);
        }
    }

    private static List<GeoPoint> CreateSkewedRing() {
        return [
            new GeoPoint(10.0000, 50.0000),
            new GeoPoint(10.0014, 50.0002),
            new GeoPoint(10.0012, 50.0010),
            new GeoPoint(10.0002, 50.0009),
            new GeoPoint(10.0000, 50.0000)
        ];
    }

    private static List<GeoPoint> CreateSquare(GeoPoint center, double halfSideDegrees) {
        var longitudeHalfSide = halfSideDegrees / Math.Cos(center.Latitude * Math.PI / 180.0);
        return [
            new GeoPoint(center.Longitude - longitudeHalfSide, center.Latitude - halfSideDegrees),
            new GeoPoint(center.Longitude + longitudeHalfSide, center.Latitude - halfSideDegrees),
            new GeoPoint(center.Longitude + longitudeHalfSide, center.Latitude + halfSideDegrees),
            new GeoPoint(center.Longitude - longitudeHalfSide, center.Latitude + halfSideDegrees),
            new GeoPoint(center.Longitude - longitudeHalfSide, center.Latitude - halfSideDegrees)
        ];
    }

    private static LocalMetricFrame CreateFrame(IEnumerable<GeoPoint> points) {
        var list = points.ToList();
        return LocalMetricFrame.Create(new GeoPoint(
            list.Average(static point => point.Longitude),
            list.Average(static point => point.Latitude)));
    }

    private static LocalPoint Centroid(IReadOnlyList<LocalPoint> points) {
        return new LocalPoint(points.Average(static point => point.X), points.Average(static point => point.Y));
    }

    private static double Distance(LocalPoint first, LocalPoint second) {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Angle(LocalPoint point, LocalPoint center) {
        return Math.Atan2(point.Y - center.Y, point.X - center.X);
    }

    private static double DistanceToLine(LocalMetricFrame frame, GeoPoint point, GeoPoint start, GeoPoint end) {
        var local = frame.Project(point);
        var localStart = frame.Project(start);
        var localEnd = frame.Project(end);
        var dx = localEnd.X - localStart.X;
        var dy = localEnd.Y - localStart.Y;
        var cross = (local.X - localStart.X) * dy - (local.Y - localStart.Y) * dx;
        return Math.Abs(cross) / Math.Sqrt(dx * dx + dy * dy);
    }

    private static double SignedArea(IReadOnlyList<LocalPoint> points) {
        var sum = 0.0;
        for (var i = 0; i < points.Count; i++) {
            var current = points[i];
            var next = points[(i + 1) % points.Count];
            sum += current.X * next.Y - next.X * current.Y;
        }
        return sum / 2.0;
    }
}
