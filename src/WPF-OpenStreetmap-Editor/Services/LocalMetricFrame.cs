using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

/// <summary>Offset in meters from a <see cref="LocalMetricFrame"/> origin; X grows eastward, Y grows northward.</summary>
internal readonly record struct LocalPoint(double X, double Y);

/// <summary>
/// Equirectangular metric frame around an origin. The longitude scale is fixed at the origin latitude, so the
/// frame is only accurate for geometry spanning a few kilometers around the origin.
/// </summary>
internal readonly record struct LocalMetricFrame(
    GeoPoint Origin,
    double MetersPerDegreeLongitude,
    double MetersPerDegreeLatitude) {
    public const double EarthRadiusMeters = 6378137.0;

    public static LocalMetricFrame Create(GeoPoint origin) {
        var latitudeRadians = GeoConverter.ClampLatitude(origin.Latitude) * Math.PI / 180.0;
        var metersPerDegreeLatitude = EarthRadiusMeters * Math.PI / 180.0;
        var metersPerDegreeLongitude = metersPerDegreeLatitude * Math.Cos(latitudeRadians);
        return new LocalMetricFrame(origin, metersPerDegreeLongitude, metersPerDegreeLatitude);
    }

    public LocalPoint Project(GeoPoint point) {
        return new LocalPoint(
            (point.Longitude - Origin.Longitude) * MetersPerDegreeLongitude,
            (point.Latitude - Origin.Latitude) * MetersPerDegreeLatitude);
    }

    public GeoPoint Unproject(LocalPoint point) {
        var longitudeOffset = Math.Abs(MetersPerDegreeLongitude) <= double.Epsilon
            ? 0
            : point.X / MetersPerDegreeLongitude;
        return new GeoPoint(
            Math.Clamp(Origin.Longitude + longitudeOffset, -180.0, 180.0),
            GeoConverter.ClampLatitude(Origin.Latitude + point.Y / MetersPerDegreeLatitude));
    }

    public LocalPoint GetDelta(GeoPoint start, GeoPoint end) {
        return new LocalPoint(
            (end.Longitude - start.Longitude) * MetersPerDegreeLongitude,
            (end.Latitude - start.Latitude) * MetersPerDegreeLatitude);
    }
}
