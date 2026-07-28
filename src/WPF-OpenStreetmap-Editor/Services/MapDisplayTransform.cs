using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed class MapDisplayAlignmentOptions {
    public string ProjectionId { get; init; } = ProjectionService.Wgs84Id;
    public string CustomProjectionWkt { get; init; } = "";
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
}

public sealed class MapDisplayTransform {
    public static readonly MapDisplayTransform Identity = new(
        static point => point,
        static point => point);

    private readonly Func<GeoPoint, GeoPoint> _documentToDisplay;
    private readonly Func<GeoPoint, GeoPoint> _displayToDocument;

    private MapDisplayTransform(
        Func<GeoPoint, GeoPoint> documentToDisplay,
        Func<GeoPoint, GeoPoint> displayToDocument) {
        _documentToDisplay = documentToDisplay;
        _displayToDocument = displayToDocument;
    }

    public GeoPoint DocumentToDisplay(GeoPoint point) => _documentToDisplay(point);

    public GeoPoint DisplayToDocument(GeoPoint point) => _displayToDocument(point);

    public static MapDisplayTransform Create(MapDisplayAlignmentOptions? options) {
        if (options is null || IsIdentity(options)) return Identity;

        var projectionId = ProjectionService.NormalizeProjectionId(options.ProjectionId);
        var customWkt = options.CustomProjectionWkt?.Trim() ?? "";
        var fromWgs84 = ProjectionService.CreateCoordinateTransform(
            ProjectionService.Wgs84Id,
            projectionId,
            targetCustomWkt: customWkt);
        var toWgs84 = ProjectionService.CreateCoordinateTransform(
            projectionId,
            ProjectionService.Wgs84Id,
            sourceCustomWkt: customWkt);

        return new MapDisplayTransform(
            point => TransformDocumentToDisplay(point, fromWgs84, toWgs84, options.OffsetX, options.OffsetY),
            point => TransformDisplayToDocument(point, fromWgs84, toWgs84, options.OffsetX, options.OffsetY));
    }

    private static GeoPoint TransformDocumentToDisplay(
        GeoPoint point,
        Func<double, double, (double X, double Y)> fromWgs84,
        Func<double, double, (double X, double Y)> toWgs84,
        double offsetX,
        double offsetY) {
        if (!point.IsValid) return point;

        var projected = fromWgs84(point.Longitude, point.Latitude);
        var display = toWgs84(projected.X + offsetX, projected.Y + offsetY);
        return ClampDisplayPoint(display.X, display.Y);
    }

    private static GeoPoint TransformDisplayToDocument(
        GeoPoint point,
        Func<double, double, (double X, double Y)> fromWgs84,
        Func<double, double, (double X, double Y)> toWgs84,
        double offsetX,
        double offsetY) {
        if (!point.IsValid) return point;

        var projected = fromWgs84(point.Longitude, point.Latitude);
        var document = toWgs84(projected.X - offsetX, projected.Y - offsetY);
        return ClampDisplayPoint(document.X, document.Y);
    }

    private static bool IsIdentity(MapDisplayAlignmentOptions options) {
        return ProjectionService.NormalizeProjectionId(options.ProjectionId) == ProjectionService.Wgs84Id &&
            Math.Abs(options.OffsetX) < double.Epsilon &&
            Math.Abs(options.OffsetY) < double.Epsilon;
    }

    private static GeoPoint ClampDisplayPoint(double longitude, double latitude) {
        if (!double.IsFinite(longitude) || !double.IsFinite(latitude)) return new GeoPoint(double.NaN, double.NaN);

        return new GeoPoint(
            Math.Clamp(longitude, -180.0, 180.0),
            GeoConverter.ClampLatitude(latitude));
    }
}
