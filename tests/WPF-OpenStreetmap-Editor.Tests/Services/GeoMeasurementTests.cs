using System.Globalization;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class GeoMeasurementTests {
    private const double MetersPerDegree = GeoMeasurement.EarthRadiusMeters * Math.PI / 180.0;

    [Fact]
    public void DistanceMeters_OneDegreeOfLatitudeIsAboutHundredElevenKilometers() {
        var distance = GeoMeasurement.DistanceMeters(new GeoPoint(0, 0), new GeoPoint(0, 1));

        Assert.Equal(MetersPerDegree, distance, 1e-6);
        Assert.Equal(111_195, distance, 1.0);
        Assert.Equal(distance, GeoMeasurement.DistanceMeters(new GeoPoint(0, 1), new GeoPoint(0, 0)), 1e-9);
        Assert.Equal(0, GeoMeasurement.DistanceMeters(new GeoPoint(10, 50), new GeoPoint(10, 50)));
    }

    [Fact]
    public void DistanceMeters_ShrinksWithLatitudeAlongParallels() {
        var equator = GeoMeasurement.DistanceMeters(new GeoPoint(0, 0), new GeoPoint(1, 0));
        var north = GeoMeasurement.DistanceMeters(new GeoPoint(0, 60), new GeoPoint(1, 60));

        Assert.Equal(equator / 2, north, 100.0);
    }

    [Theory]
    [InlineData(0.0, 1.0, 0.0)]
    [InlineData(1.0, 0.0, 90.0)]
    [InlineData(0.0, -1.0, 180.0)]
    [InlineData(-1.0, 0.0, 270.0)]
    public void BearingDegrees_MeasuresClockwiseFromNorth(double longitude, double latitude, double expected) {
        var bearing = GeoMeasurement.BearingDegrees(new GeoPoint(0, 0), new GeoPoint(longitude, latitude));

        Assert.Equal(expected, bearing, 1e-9);
    }

    [Fact]
    public void LengthMeters_SumsSegmentDistances() {
        var points = new List<GeoPoint> { new(10, 50), new(10.001, 50), new(10.001, 50.001) };

        var length = GeoMeasurement.LengthMeters(points);

        var expected = GeoMeasurement.DistanceMeters(points[0], points[1]) + GeoMeasurement.DistanceMeters(points[1], points[2]);
        Assert.Equal(expected, length, 1e-9);
        Assert.Equal(0, GeoMeasurement.LengthMeters([points[0]]));
        Assert.Equal(0, GeoMeasurement.LengthMeters([]));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(50.0)]
    public void AreaSquareMeters_OfHundredMeterSquareIsAboutTenThousand(double latitude) {
        var ring = CreateSquare(new GeoPoint(10, latitude), 100);

        var area = GeoMeasurement.AreaSquareMeters(ring);

        Assert.Equal(10_000, area, 100.0);
    }

    [Fact]
    public void AreaSquareMeters_IgnoresWindingAndClosure() {
        var closed = CreateSquare(new GeoPoint(10, 50), 100);
        var open = closed.Take(4).ToList();
        var reversed = closed.AsEnumerable().Reverse().ToList();

        var area = GeoMeasurement.AreaSquareMeters(closed);

        Assert.Equal(area, GeoMeasurement.AreaSquareMeters(open), 1e-6);
        Assert.Equal(area, GeoMeasurement.AreaSquareMeters(reversed), 1e-6);
    }

    [Fact]
    public void AreaSquareMeters_ReturnsZeroWithoutThreeDistinctVertices() {
        var a = new GeoPoint(10, 50);
        var b = new GeoPoint(10.001, 50.001);

        Assert.Equal(0, GeoMeasurement.AreaSquareMeters([a, b]));
        Assert.Equal(0, GeoMeasurement.AreaSquareMeters([a, b, a, b]));
        Assert.Equal(0, GeoMeasurement.AreaSquareMeters([]));
    }

    [Theory]
    [InlineData(0.0, "0.0 m")]
    [InlineData(12.34, "12.3 m")]
    [InlineData(99.96, "100 m")]
    [InlineData(123.4, "123 m")]
    [InlineData(999.6, "1.00 km")]
    [InlineData(1234.0, "1.23 km")]
    [InlineData(12_345_678.0, "12345.68 km")]
    public void FormatLength_SwitchesUnitsByMagnitude(double meters, string expected) {
        Assert.Equal(expected, GeoMeasurement.FormatLength(meters, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(0.0, "0 m²")]
    [InlineData(123.4, "123 m²")]
    [InlineData(9_999.4, "9999 m²")]
    [InlineData(12_345.0, "1.23 ha")]
    [InlineData(999_999.0, "100.00 ha")]
    [InlineData(1_234_567.0, "1.23 km²")]
    public void FormatArea_SwitchesUnitsByMagnitude(double squareMeters, string expected) {
        Assert.Equal(expected, GeoMeasurement.FormatArea(squareMeters, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Format_UsesTheProvidedCulture() {
        var german = CultureInfo.GetCultureInfo("de-DE");

        Assert.Equal("12,3 m", GeoMeasurement.FormatLength(12.34, german));
        Assert.Equal("1,23 km²", GeoMeasurement.FormatArea(1_234_567, german));
    }

    [Fact]
    public void Measure_TotalsMixedFeaturesAndSubtractsInnerRings() {
        var point = new MapFeature { GeometryType = MapGeometryType.Point, Parts = [[new GeoPoint(10, 50)]] };
        var linePoints = new List<GeoPoint> { new(10, 50), new(10.001, 50), new(10.001, 50.001) };
        var line = new MapFeature { GeometryType = MapGeometryType.LineString, Parts = [linePoints] };
        var outer = CreateSquare(new GeoPoint(10, 50), 100);
        var inner = CreateSquare(new GeoPoint(10, 50), 20);
        var polygon = new MapFeature { GeometryType = MapGeometryType.Polygon, Parts = [outer, inner] };

        var measurement = FeatureMeasurementService.Measure([point, line, polygon]);

        Assert.Equal(3, measurement.FeatureCount);
        Assert.Equal(1 + 3 + 4 + 4, measurement.VertexCount);
        var expectedLength = GeoMeasurement.LengthMeters(linePoints) + GeoMeasurement.LengthMeters(outer) + GeoMeasurement.LengthMeters(inner);
        Assert.Equal(expectedLength, measurement.LengthMeters, 1e-9);
        Assert.Equal(10_000 - 400, measurement.AreaSquareMeters, 100.0);
    }

    [Fact]
    public void Measure_ClosesOpenPolygonRingsForPerimeterAndClampsNegativeArea() {
        var openRing = CreateSquare(new GeoPoint(10, 50), 100).Take(4).ToList();
        var polygon = new MapFeature { GeometryType = MapGeometryType.Polygon, Parts = [openRing] };
        var inverted = new MapFeature {
            GeometryType = MapGeometryType.Polygon,
            Parts = [CreateSquare(new GeoPoint(10, 50), 20), CreateSquare(new GeoPoint(10, 50), 100)]
        };

        var measurement = FeatureMeasurementService.Measure([polygon]);
        var invertedMeasurement = FeatureMeasurementService.Measure([inverted]);

        Assert.Equal(4, measurement.VertexCount);
        Assert.Equal(400, measurement.LengthMeters, 1.0);
        Assert.Equal(0, invertedMeasurement.AreaSquareMeters);
    }

    [Fact]
    public void Measure_ReturnsZerosForNoFeatures() {
        Assert.Equal(new FeatureMeasurement(0, 0, 0, 0), FeatureMeasurementService.Measure([]));
    }

    private static List<GeoPoint> CreateSquare(GeoPoint center, double sideMeters) {
        var halfLatitude = sideMeters / 2 / MetersPerDegree;
        var halfLongitude = halfLatitude / Math.Cos(center.Latitude * Math.PI / 180.0);
        return [
            new GeoPoint(center.Longitude - halfLongitude, center.Latitude - halfLatitude),
            new GeoPoint(center.Longitude + halfLongitude, center.Latitude - halfLatitude),
            new GeoPoint(center.Longitude + halfLongitude, center.Latitude + halfLatitude),
            new GeoPoint(center.Longitude - halfLongitude, center.Latitude + halfLatitude),
            new GeoPoint(center.Longitude - halfLongitude, center.Latitude - halfLatitude)
        ];
    }
}
