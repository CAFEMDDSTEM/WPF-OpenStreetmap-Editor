using System.Windows;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public readonly struct GeoViewportProjection {
    private readonly double _centerPixelX;
    private readonly double _centerPixelY;
    private readonly double _halfWidth;
    private readonly double _halfHeight;
    private readonly int _zoom;
    private readonly double _panOffsetX;
    private readonly double _panOffsetY;

    private GeoViewportProjection(
        double centerLatitude,
        double centerLongitude,
        int zoom,
        Size viewport,
        double panOffsetX,
        double panOffsetY) {
        (_centerPixelX, _centerPixelY) = GeoConverter.LatLonToPixelXY(centerLatitude, centerLongitude, zoom);
        _halfWidth = viewport.Width / 2.0;
        _halfHeight = viewport.Height / 2.0;
        _zoom = zoom;
        _panOffsetX = panOffsetX;
        _panOffsetY = panOffsetY;
    }

    public static GeoViewportProjection Create(
        double centerLatitude,
        double centerLongitude,
        int zoom,
        Size viewport,
        double panOffsetX = 0,
        double panOffsetY = 0) {
        return new GeoViewportProjection(centerLatitude, centerLongitude, zoom, viewport, panOffsetX, panOffsetY);
    }

    public Point GeoToScreen(GeoPoint point) {
        var (pointX, pointY) = GeoConverter.LatLonToPixelXY(point.Latitude, point.Longitude, _zoom);
        return new Point(
            _halfWidth + pointX - _centerPixelX + _panOffsetX,
            _halfHeight + pointY - _centerPixelY + _panOffsetY);
    }

    public GeoPoint ScreenToGeo(Point point) {
        var pixelX = _centerPixelX + point.X - _halfWidth - _panOffsetX;
        var pixelY = _centerPixelY + point.Y - _halfHeight - _panOffsetY;
        var (latitude, longitude) = GeoConverter.PixelXYToLatLon(pixelX, pixelY, _zoom);
        return new GeoPoint(longitude, latitude);
    }
}

public static class VectorMapInteraction {
    public static Point GeoToScreen(
        GeoPoint point,
        double centerLatitude,
        double centerLongitude,
        int zoom,
        Size viewport,
        double panOffsetX = 0,
        double panOffsetY = 0) {
        return GeoViewportProjection.Create(
            centerLatitude,
            centerLongitude,
            zoom,
            viewport,
            panOffsetX,
            panOffsetY).GeoToScreen(point);
    }

    public static GeoPoint ScreenToGeo(
        Point point,
        double centerLatitude,
        double centerLongitude,
        int zoom,
        Size viewport,
        double panOffsetX = 0,
        double panOffsetY = 0) {
        return GeoViewportProjection.Create(
            centerLatitude,
            centerLongitude,
            zoom,
            viewport,
            panOffsetX,
            panOffsetY).ScreenToGeo(point);
    }

    public static GeoBounds GetViewportBounds(
        double centerLatitude,
        double centerLongitude,
        int zoom,
        Size viewport,
        double panOffsetX = 0,
        double panOffsetY = 0) {
        var projection = GeoViewportProjection.Create(centerLatitude, centerLongitude, zoom, viewport, panOffsetX, panOffsetY);
        var topLeft = projection.ScreenToGeo(new Point(0, 0));
        var bottomRight = projection.ScreenToGeo(new Point(viewport.Width, viewport.Height));
        return new GeoBounds(
            Math.Min(topLeft.Longitude, bottomRight.Longitude),
            Math.Min(topLeft.Latitude, bottomRight.Latitude),
            Math.Max(topLeft.Longitude, bottomRight.Longitude),
            Math.Max(topLeft.Latitude, bottomRight.Latitude));
    }

    public static GeoBounds ScreenRectToGeoBounds(
        Rect screenRect,
        double centerLatitude,
        double centerLongitude,
        int zoom,
        Size viewport) {
        var first = ScreenToGeo(screenRect.TopLeft, centerLatitude, centerLongitude, zoom, viewport);
        var second = ScreenToGeo(screenRect.BottomRight, centerLatitude, centerLongitude, zoom, viewport);
        return new GeoBounds(
            Math.Min(first.Longitude, second.Longitude),
            Math.Min(first.Latitude, second.Latitude),
            Math.Max(first.Longitude, second.Longitude),
            Math.Max(first.Latitude, second.Latitude));
    }

    public static GeoPoint GetCenterAfterPan(GeoPoint center, Vector dragDelta, int zoom) {
        var (centerX, centerY) = GeoConverter.LatLonToPixelXY(center.Latitude, center.Longitude, zoom);
        return PixelToClampedGeo(centerX - dragDelta.X, centerY - dragDelta.Y, zoom);
    }

    public static GeoPoint GetCenterAfterZoom(
        GeoPoint center,
        int oldZoom,
        int newZoom,
        Point anchor,
        Size viewport) {
        var anchoredPoint = ScreenToGeo(anchor, center.Latitude, center.Longitude, oldZoom, viewport);
        var (anchorX, anchorY) = GeoConverter.LatLonToPixelXY(
            anchoredPoint.Latitude,
            anchoredPoint.Longitude,
            newZoom);
        return PixelToClampedGeo(
            anchorX - anchor.X + viewport.Width / 2.0,
            anchorY - anchor.Y + viewport.Height / 2.0,
            newZoom);
    }

    public static MapFeature? HitTest(
        IEnumerable<MapFeature> features,
        Point screenPoint,
        double centerLatitude,
        double centerLongitude,
        int zoom,
        Size viewport,
        double tolerance = 8) {
        var projection = GeoViewportProjection.Create(centerLatitude, centerLongitude, zoom, viewport);
        MapFeature? best = null;
        var bestDistance = tolerance;
        foreach (var feature in features.Where(static item => !item.IsHidden)) {
            foreach (var part in feature.Parts) {
                if (feature.GeometryType == MapGeometryType.Point) {
                    foreach (var point in part) {
                        var distance = (projection.GeoToScreen(point) - screenPoint).Length;
                        if (distance <= bestDistance) {
                            bestDistance = distance;
                            best = feature;
                        }
                    }
                    continue;
                }

                for (var i = 1; i < part.Count; i++) {
                    var start = projection.GeoToScreen(part[i - 1]);
                    var end = projection.GeoToScreen(part[i]);
                    var distance = DistanceToSegment(screenPoint, start, end);
                    if (distance <= bestDistance) {
                        bestDistance = distance;
                        best = feature;
                    }
                }

                if (feature.GeometryType == MapGeometryType.Polygon && IsPointInPolygon(screenPoint, part, projection)) {
                    bestDistance = 0;
                    best = feature;
                }
            }
        }
        return best;
    }

    public static IReadOnlyList<MapFeature> FindWithin(
        IEnumerable<MapFeature> features,
        Rect screenRect,
        double centerLatitude,
        double centerLongitude,
        int zoom,
        Size viewport) {
        var projection = GeoViewportProjection.Create(centerLatitude, centerLongitude, zoom, viewport);
        return features.Where(feature =>
            !feature.IsHidden &&
            feature.Points.Any(point => screenRect.Contains(projection.GeoToScreen(point))))
            .ToList();
    }

    public static int GetFitZoom(GeoBounds bounds, Size viewport, int maximumZoom) {
        if (!bounds.IsValid || viewport.Width <= 0 || viewport.Height <= 0) return GeoConverter.MinZoom;
        for (var zoom = maximumZoom; zoom >= GeoConverter.MinZoom; zoom--) {
            var topLeft = GeoConverter.LatLonToPixelXY(bounds.MaxLatitude, bounds.MinLongitude, zoom);
            var bottomRight = GeoConverter.LatLonToPixelXY(bounds.MinLatitude, bounds.MaxLongitude, zoom);
            if (Math.Abs(bottomRight.PixelX - topLeft.PixelX) <= viewport.Width * 0.85 &&
                Math.Abs(bottomRight.PixelY - topLeft.PixelY) <= viewport.Height * 0.85) {
                return zoom;
            }
        }
        return GeoConverter.MinZoom;
    }

    private static double DistanceToSegment(Point point, Point start, Point end) {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared;
        if (lengthSquared == 0) return (point - start).Length;
        var t = Math.Clamp(Vector.Multiply(point - start, segment) / lengthSquared, 0, 1);
        return (point - (start + segment * t)).Length;
    }

    private static bool IsPointInPolygon(
        Point point,
        IReadOnlyList<GeoPoint> ring,
        GeoViewportProjection projection) {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++) {
            var current = projection.GeoToScreen(ring[i]);
            var previous = projection.GeoToScreen(ring[j]);
            if ((current.Y > point.Y) != (previous.Y > point.Y) &&
                point.X < (previous.X - current.X) * (point.Y - current.Y) / (previous.Y - current.Y) + current.X) {
                inside = !inside;
            }
        }
        return inside;
    }

    private static GeoPoint PixelToClampedGeo(double pixelX, double pixelY, int zoom) {
        var worldSize = GeoConverter.TileSize * (double)GeoConverter.GetTileCount(zoom);
        var (latitude, longitude) = GeoConverter.PixelXYToLatLon(
            Math.Clamp(pixelX, 0, worldSize),
            Math.Clamp(pixelY, 0, worldSize),
            zoom);
        return new GeoPoint(Math.Clamp(longitude, -180, 180), GeoConverter.ClampLatitude(latitude));
    }
}
