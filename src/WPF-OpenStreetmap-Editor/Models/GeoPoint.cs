namespace WPF_OpenStreetmap_Editor.Models;

public readonly record struct GeoPoint(double Longitude, double Latitude) {
    public bool IsValid =>
        double.IsFinite(Longitude) &&
        double.IsFinite(Latitude) &&
        Longitude is >= -180 and <= 180 &&
        Latitude is >= -90 and <= 90;
}

public readonly record struct GeoBounds(
    double MinLongitude,
    double MinLatitude,
    double MaxLongitude,
    double MaxLatitude) {
    public bool IsValid =>
        double.IsFinite(MinLongitude) &&
        double.IsFinite(MinLatitude) &&
        double.IsFinite(MaxLongitude) &&
        double.IsFinite(MaxLatitude) &&
        MinLongitude <= MaxLongitude &&
        MinLatitude <= MaxLatitude;

    public GeoPoint Center => new(
        (MinLongitude + MaxLongitude) / 2.0,
        (MinLatitude + MaxLatitude) / 2.0);

    public bool Intersects(GeoBounds other) {
        return MinLongitude <= other.MaxLongitude &&
            MaxLongitude >= other.MinLongitude &&
            MinLatitude <= other.MaxLatitude &&
            MaxLatitude >= other.MinLatitude;
    }

    public static GeoBounds FromPoints(IEnumerable<GeoPoint> points) {
        var hasPoint = false;
        var minLongitude = double.MaxValue;
        var minLatitude = double.MaxValue;
        var maxLongitude = double.MinValue;
        var maxLatitude = double.MinValue;

        foreach (var point in points) {
            if (!point.IsValid) continue;
            hasPoint = true;
            minLongitude = Math.Min(minLongitude, point.Longitude);
            minLatitude = Math.Min(minLatitude, point.Latitude);
            maxLongitude = Math.Max(maxLongitude, point.Longitude);
            maxLatitude = Math.Max(maxLatitude, point.Latitude);
        }

        return hasPoint
            ? new GeoBounds(minLongitude, minLatitude, maxLongitude, maxLatitude)
            : new GeoBounds(double.NaN, double.NaN, double.NaN, double.NaN);
    }
}
