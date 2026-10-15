using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

/// <summary>
/// One anchor of a pen-tool path. A corner anchor has no handles; a smooth anchor carries the
/// incoming and outgoing Bézier control points in document coordinates.
/// </summary>
public sealed record BezierAnchor(GeoPoint Point, GeoPoint? HandleIn, GeoPoint? HandleOut) {
    public bool IsCorner => HandleIn is null && HandleOut is null;

    public static BezierAnchor Corner(GeoPoint point) => new(point, null, null);

    /// <summary>Smooth anchor whose handles mirror each other through the anchor, as produced by a press-and-drag.</summary>
    public static BezierAnchor Symmetric(GeoPoint point, GeoPoint handleOut) {
        var handleIn = new GeoPoint(
            2.0 * point.Longitude - handleOut.Longitude,
            2.0 * point.Latitude - handleOut.Latitude);
        return new BezierAnchor(point, handleIn, handleOut);
    }
}
