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
    private readonly MapDisplayTransform _displayTransform;

    private GeoViewportProjection(
        double centerLatitude,
        double centerLongitude,
        int zoom,
        Size viewport,
        double panOffsetX,
        double panOffsetY,
        MapDisplayTransform displayTransform) {
        (_centerPixelX, _centerPixelY) = GeoConverter.LatLonToPixelXY(centerLatitude, centerLongitude, zoom);
        _halfWidth = viewport.Width / 2.0;
        _halfHeight = viewport.Height / 2.0;
        _zoom = zoom;
        _panOffsetX = panOffsetX;
        _panOffsetY = panOffsetY;
        _displayTransform = displayTransform;
    }

    public static GeoViewportProjection Create(
        double centerLatitude,
        double centerLongitude,
        int zoom,
        Size viewport,
        double panOffsetX = 0,
        double panOffsetY = 0,
        MapDisplayTransform? displayTransform = null) {
        return new GeoViewportProjection(
            centerLatitude,
            centerLongitude,
            zoom,
            viewport,
            panOffsetX,
            panOffsetY,
            displayTransform ?? MapDisplayTransform.Identity);
    }

    public Point GeoToScreen(GeoPoint point) {
        var displayPoint = _displayTransform.DocumentToDisplay(point);
        var (pointX, pointY) = GeoConverter.LatLonToPixelXY(displayPoint.Latitude, displayPoint.Longitude, _zoom);
        return new Point(
            _halfWidth + pointX - _centerPixelX + _panOffsetX,
            _halfHeight + pointY - _centerPixelY + _panOffsetY);
    }

    public GeoPoint ScreenToGeo(Point point) {
        var pixelX = _centerPixelX + point.X - _halfWidth - _panOffsetX;
        var pixelY = _centerPixelY + point.Y - _halfHeight - _panOffsetY;
        var (latitude, longitude) = GeoConverter.PixelXYToLatLon(pixelX, pixelY, _zoom);
        return _displayTransform.DisplayToDocument(new GeoPoint(longitude, latitude));
    }
}

public sealed record VertexHit(MapFeature Feature, int PartIndex, int PointIndex, Point ScreenPoint);
public sealed record SegmentHit(
    MapFeature Feature,
    int PartIndex,
    int StartPointIndex,
    int EndPointIndex,
    Point StartScreenPoint,
    Point EndScreenPoint);

public static class VectorMapInteraction {
    public static Point GeoToScreen(
        GeoPoint point,
        double centerLatitude,
        double centerLongitude,
        int zoom,
        Size viewport,
        double panOffsetX = 0,
        double panOffsetY = 0,
        MapDisplayTransform? displayTransform = null) {
        return GeoViewportProjection.Create(
            centerLatitude,
            centerLongitude,
            zoom,
            viewport,
            panOffsetX,
            panOffsetY,
            displayTransform).GeoToScreen(point);
    }

    public static GeoPoint ScreenToGeo(
        Point point,
        double centerLatitude,
        double centerLongitude,
        int zoom,
        Size viewport,
        double panOffsetX = 0,
        double panOffsetY = 0,
        MapDisplayTransform? displayTransform = null) {
        return GeoViewportProjection.Create(
            centerLatitude,
            centerLongitude,
            zoom,
            viewport,
            panOffsetX,
            panOffsetY,
            displayTransform).ScreenToGeo(point);
    }

    public static GeoBounds GetViewportBounds(
        double centerLatitude,
        double centerLongitude,
        int zoom,
        Size viewport,
        double panOffsetX = 0,
        double panOffsetY = 0,
        MapDisplayTransform? displayTransform = null) {
        var projection = GeoViewportProjection.Create(
            centerLatitude,
            centerLongitude,
            zoom,
            viewport,
            panOffsetX,
            panOffsetY,
            displayTransform);
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
        Size viewport,
        MapDisplayTransform? displayTransform = null) {
        var first = ScreenToGeo(screenRect.TopLeft, centerLatitude, centerLongitude, zoom, viewport, displayTransform: displayTransform);
        var second = ScreenToGeo(screenRect.BottomRight, centerLatitude, centerLongitude, zoom, viewport, displayTransform: displayTransform);
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
        double tolerance = 8,
        MapDisplayTransform? displayTransform = null,
        double panOffsetX = 0,
        double panOffsetY = 0) {
        var projection = GeoViewportProjection.Create(
            centerLatitude,
            centerLongitude,
            zoom,
            viewport,
            panOffsetX,
            panOffsetY,
            displayTransform: displayTransform);
        MapFeature? best = null;
        var bestRank = int.MaxValue;
        var bestDistance = double.MaxValue;
        foreach (var feature in features.Where(static item => !item.IsHidden)) {
            foreach (var part in feature.Parts) {
                if (feature.GeometryType == MapGeometryType.Point) {
                    foreach (var point in part) {
                        var distance = (projection.GeoToScreen(point) - screenPoint).Length;
                        AcceptHit(feature, 0, distance);
                    }
                    continue;
                }

                for (var i = 1; i < part.Count; i++) {
                    var start = projection.GeoToScreen(part[i - 1]);
                    var end = projection.GeoToScreen(part[i]);
                    var distance = DistanceToSegment(screenPoint, start, end);
                    AcceptHit(feature, 1, distance);
                }

                if (feature.GeometryType == MapGeometryType.Polygon && IsPointInPolygon(screenPoint, part, projection)) {
                    AcceptHit(feature, 2, 0);
                }
            }
        }
        return best;

        void AcceptHit(MapFeature feature, int rank, double distance) {
            if (distance > tolerance) return;
            if (rank > bestRank) return;
            if (rank == bestRank && distance > bestDistance) return;

            bestRank = rank;
            bestDistance = distance;
            best = feature;
        }
    }

    public static IReadOnlyList<MapFeature> FindWithin(
        IEnumerable<MapFeature> features,
        Rect screenRect,
        double centerLatitude,
        double centerLongitude,
        int zoom,
        Size viewport,
        MapDisplayTransform? displayTransform = null,
        double panOffsetX = 0,
        double panOffsetY = 0) {
        var projection = GeoViewportProjection.Create(
            centerLatitude,
            centerLongitude,
            zoom,
            viewport,
            panOffsetX,
            panOffsetY,
            displayTransform: displayTransform);
        return features.Where(feature =>
            !feature.IsHidden &&
            feature.Points.Any(point => screenRect.Contains(projection.GeoToScreen(point))))
            .ToList();
    }

    public static VertexHit? HitTestVertex(
        IEnumerable<MapFeature> features,
        Point screenPoint,
        double centerLatitude,
        double centerLongitude,
        int zoom,
        Size viewport,
        double tolerance = 8,
        MapDisplayTransform? displayTransform = null,
        double panOffsetX = 0,
        double panOffsetY = 0) {
        var projection = GeoViewportProjection.Create(
            centerLatitude,
            centerLongitude,
            zoom,
            viewport,
            panOffsetX,
            panOffsetY,
            displayTransform: displayTransform);
        VertexHit? best = null;
        var bestDistance = tolerance;
        foreach (var feature in features.Where(static item => !item.IsHidden)) {
            if (feature.GeometryType == MapGeometryType.Point) continue;

            for (var partIndex = 0; partIndex < feature.Parts.Count; partIndex++) {
                var part = feature.Parts[partIndex];
                var pointCount = IsClosedRing(part) ? part.Count - 1 : part.Count;
                for (var pointIndex = 0; pointIndex < pointCount; pointIndex++) {
                    var vertexScreen = projection.GeoToScreen(part[pointIndex]);
                    var distance = (vertexScreen - screenPoint).Length;
                    if (distance <= bestDistance) {
                        bestDistance = distance;
                        best = new VertexHit(feature, partIndex, pointIndex, vertexScreen);
                    }
                }
            }
        }

        return best;
    }

    public static SegmentHit? HitTestSegment(
        IEnumerable<MapFeature> features,
        Point screenPoint,
        double centerLatitude,
        double centerLongitude,
        int zoom,
        Size viewport,
        double tolerance = 8,
        MapDisplayTransform? displayTransform = null,
        double panOffsetX = 0,
        double panOffsetY = 0) {
        var projection = GeoViewportProjection.Create(
            centerLatitude,
            centerLongitude,
            zoom,
            viewport,
            panOffsetX,
            panOffsetY,
            displayTransform: displayTransform);
        SegmentHit? best = null;
        var bestDistance = tolerance;
        foreach (var feature in features.Where(static item => !item.IsHidden && item.GeometryType != MapGeometryType.Point)) {
            for (var partIndex = 0; partIndex < feature.Parts.Count; partIndex++) {
                var part = feature.Parts[partIndex];
                for (var pointIndex = 1; pointIndex < part.Count; pointIndex++) {
                    var start = projection.GeoToScreen(part[pointIndex - 1]);
                    var end = projection.GeoToScreen(part[pointIndex]);
                    var distance = DistanceToSegment(screenPoint, start, end);
                    if (distance <= bestDistance) {
                        bestDistance = distance;
                        best = new SegmentHit(feature, partIndex, pointIndex - 1, pointIndex, start, end);
                    }
                }
            }
        }

        return best;
    }

    public static int GetFitZoom(
        GeoBounds bounds,
        Size viewport,
        int maximumZoom,
        MapDisplayTransform? displayTransform = null) {
        if (!bounds.IsValid || viewport.Width <= 0 || viewport.Height <= 0) return GeoConverter.MinZoom;
        var displayBounds = ToDisplayBounds(bounds, displayTransform ?? MapDisplayTransform.Identity);
        if (!displayBounds.IsValid) return GeoConverter.MinZoom;

        for (var zoom = maximumZoom; zoom >= GeoConverter.MinZoom; zoom--) {
            var topLeft = GeoConverter.LatLonToPixelXY(displayBounds.MaxLatitude, displayBounds.MinLongitude, zoom);
            var bottomRight = GeoConverter.LatLonToPixelXY(displayBounds.MinLatitude, displayBounds.MaxLongitude, zoom);
            if (Math.Abs(bottomRight.PixelX - topLeft.PixelX) <= viewport.Width * 0.85 &&
                Math.Abs(bottomRight.PixelY - topLeft.PixelY) <= viewport.Height * 0.85) {
                return zoom;
            }
        }
        return GeoConverter.MinZoom;
    }

    public static GeoBounds ToDisplayBounds(GeoBounds bounds, MapDisplayTransform displayTransform) {
        if (!bounds.IsValid) return bounds;

        var points = new[] {
            displayTransform.DocumentToDisplay(new GeoPoint(bounds.MinLongitude, bounds.MinLatitude)),
            displayTransform.DocumentToDisplay(new GeoPoint(bounds.MinLongitude, bounds.MaxLatitude)),
            displayTransform.DocumentToDisplay(new GeoPoint(bounds.MaxLongitude, bounds.MinLatitude)),
            displayTransform.DocumentToDisplay(new GeoPoint(bounds.MaxLongitude, bounds.MaxLatitude)),
            displayTransform.DocumentToDisplay(bounds.Center)
        };
        return GeoBounds.FromPoints(points);
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

    private static bool IsClosedRing(IReadOnlyList<GeoPoint> part) {
        return part.Count > 2 && part[0] == part[^1];
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
