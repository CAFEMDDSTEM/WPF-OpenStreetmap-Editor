using System.Globalization;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

/// <summary>Great-circle measurements on a sphere of mean Earth radius, plus unit formatting for status text.</summary>
public static class GeoMeasurement {
    public const double EarthRadiusMeters = 6_371_008.8;

    public static double DistanceMeters(GeoPoint a, GeoPoint b) {
        var latitudeA = ToRadians(a.Latitude);
        var latitudeB = ToRadians(b.Latitude);
        var sinHalfLatitude = Math.Sin((latitudeB - latitudeA) / 2.0);
        var sinHalfLongitude = Math.Sin(ToRadians(b.Longitude - a.Longitude) / 2.0);
        var haversine = sinHalfLatitude * sinHalfLatitude +
            Math.Cos(latitudeA) * Math.Cos(latitudeB) * sinHalfLongitude * sinHalfLongitude;
        return 2.0 * EarthRadiusMeters * Math.Asin(Math.Sqrt(Math.Min(1.0, haversine)));
    }

    /// <summary>Initial bearing from <paramref name="from"/> to <paramref name="to"/> in degrees: 0 = north, 90 = east.</summary>
    public static double BearingDegrees(GeoPoint from, GeoPoint to) {
        var latitudeFrom = ToRadians(from.Latitude);
        var latitudeTo = ToRadians(to.Latitude);
        var deltaLongitude = ToRadians(to.Longitude - from.Longitude);
        var y = Math.Sin(deltaLongitude) * Math.Cos(latitudeTo);
        var x = Math.Cos(latitudeFrom) * Math.Sin(latitudeTo) -
            Math.Sin(latitudeFrom) * Math.Cos(latitudeTo) * Math.Cos(deltaLongitude);
        var degrees = Math.Atan2(y, x) * 180.0 / Math.PI;
        return (degrees + 360.0) % 360.0;
    }

    public static double LengthMeters(IReadOnlyList<GeoPoint> points) {
        ArgumentNullException.ThrowIfNull(points);

        var length = 0.0;
        for (var i = 1; i < points.Count; i++) {
            length += DistanceMeters(points[i - 1], points[i]);
        }
        return length;
    }

    /// <summary>
    /// Spherical excess area (Chamberlain &amp; Duquette). The ring may be open or closed and wound either way;
    /// rings crossing the antimeridian are not handled.
    /// </summary>
    public static double AreaSquareMeters(IReadOnlyList<GeoPoint> ring) {
        ArgumentNullException.ThrowIfNull(ring);

        var count = ring.Count > 1 && ring[0] == ring[^1] ? ring.Count - 1 : ring.Count;
        if (count < 3 || ring.Take(count).Distinct().Count() < 3) return 0.0;

        var sum = 0.0;
        for (var i = 0; i < count; i++) {
            var previous = ring[(i + count - 1) % count];
            var next = ring[(i + 1) % count];
            sum += ToRadians(next.Longitude - previous.Longitude) * Math.Sin(ToRadians(ring[i].Latitude));
        }
        return Math.Abs(sum) * EarthRadiusMeters * EarthRadiusMeters / 2.0;
    }

    public static string FormatLength(double meters, IFormatProvider? provider = null) {
        provider ??= CultureInfo.CurrentCulture;
        // Thresholds use the rounded value so "100.0 m" and "1000 m" never appear.
        if (Math.Round(meters, 1, MidpointRounding.AwayFromZero) < 100) return string.Create(provider, $"{meters:F1} m");
        if (Math.Round(meters, MidpointRounding.AwayFromZero) < 1000) return string.Create(provider, $"{meters:F0} m");
        return string.Create(provider, $"{meters / 1000.0:F2} km");
    }

    public static string FormatArea(double squareMeters, IFormatProvider? provider = null) {
        provider ??= CultureInfo.CurrentCulture;
        if (Math.Round(squareMeters, MidpointRounding.AwayFromZero) < 10_000) {
            return string.Create(provider, $"{squareMeters:F0} m²");
        }
        if (squareMeters < 1_000_000) return string.Create(provider, $"{squareMeters / 10_000.0:F2} ha");
        return string.Create(provider, $"{squareMeters / 1_000_000.0:F2} km²");
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
