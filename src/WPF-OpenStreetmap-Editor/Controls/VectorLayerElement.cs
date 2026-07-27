using System.Windows;
using System.Windows.Media;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Controls;

public sealed class VectorLayerElement : FrameworkElement {
    private const double RenderOverscanPixels = 768;
    private const double PanRebaseThresholdPixels = RenderOverscanPixels * 0.5;
    private const double SimplifyDistanceSquared = 0.64;
    private const double CoordinateTolerance = 1e-12;
    private const double PixelTolerance = 0.5;

    private MapDocument? _document;
    private double _centerLatitude;
    private double _centerLongitude;
    private int _zoom;
    private double _panOffsetX;
    private double _panOffsetY;
    private double _drawPanOffsetX;
    private double _drawPanOffsetY;
    private double _lastRenderedWidth;
    private double _lastRenderedHeight;

    public VectorRenderPlan? LastPlan { get; private set; }

    internal bool RequiresDrawingRefresh(
        MapDocument? document,
        double centerLatitude,
        double centerLongitude,
        int zoom,
        double panOffsetX,
        double panOffsetY) {
        return RequiresDrawingRefreshCore(
            _document,
            document,
            _centerLatitude,
            centerLatitude,
            _centerLongitude,
            centerLongitude,
            _zoom,
            zoom,
            _lastRenderedWidth,
            ActualWidth,
            _lastRenderedHeight,
            ActualHeight,
            _drawPanOffsetX,
            panOffsetX,
            _drawPanOffsetY,
            panOffsetY);
    }

    public VectorLayerElement() {
        CacheMode = new BitmapCache();
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    public void UpdateView(
        MapDocument? document,
        double centerLatitude,
        double centerLongitude,
        int zoom,
        double panOffsetX,
        double panOffsetY) {
        var needsRefresh = RequiresDrawingRefresh(
            document,
            centerLatitude,
            centerLongitude,
            zoom,
            panOffsetX,
            panOffsetY);

        _document = document;
        _centerLatitude = centerLatitude;
        _centerLongitude = centerLongitude;
        _zoom = zoom;
        _panOffsetX = panOffsetX;
        _panOffsetY = panOffsetY;

        if (needsRefresh) {
            _drawPanOffsetX = panOffsetX;
            _drawPanOffsetY = panOffsetY;
            InvalidateVisual();
        }

        ApplyPanTransform();
    }

    protected override void OnRender(DrawingContext drawingContext) {
        base.OnRender(drawingContext);
        if (_document is null || ActualWidth <= 0 || ActualHeight <= 0) {
            LastPlan = null;
            _lastRenderedWidth = ActualWidth;
            _lastRenderedHeight = ActualHeight;
            return;
        }

        var viewport = new Size(ActualWidth, ActualHeight);
        _lastRenderedWidth = viewport.Width;
        _lastRenderedHeight = viewport.Height;
        var projection = GeoViewportProjection.Create(
            _centerLatitude,
            _centerLongitude,
            _zoom,
            viewport,
            _drawPanOffsetX,
            _drawPanOffsetY);
        LastPlan = VectorRenderPlanner.Create(_document, GetBufferedViewportBounds(viewport));

        var normalBrush = FindBrush("Theme.TextBrush", SystemColors.WindowTextBrush);
        var selectedBrush = FindBrush("Theme.AccentBrush", SystemColors.HighlightBrush);
        var fillBrush = FindBrush("Theme.SelectionBrush", SystemColors.HighlightBrush).CloneCurrentValue();
        fillBrush.Opacity = 0.3;
        fillBrush.Freeze();
        var normalPen = new Pen(normalBrush, 1.5);
        var selectedPen = new Pen(selectedBrush, 3);
        normalPen.Freeze();
        selectedPen.Freeze();

        foreach (var feature in LastPlan.Features) {
            var pen = feature.IsSelected ? selectedPen : normalPen;
            foreach (var part in feature.Parts) {
                if (part.Count == 0) continue;
                if (feature.GeometryType == MapGeometryType.Point) {
                    foreach (var point in part) {
                        var screen = projection.GeoToScreen(point);
                        drawingContext.DrawEllipse(feature.IsSelected ? selectedBrush : normalBrush, null, screen, 3.5, 3.5);
                    }
                    continue;
                }

                var geometry = CreateGeometry(part, feature.GeometryType == MapGeometryType.Polygon, projection);
                drawingContext.DrawGeometry(
                    feature.GeometryType == MapGeometryType.Polygon ? fillBrush : null,
                    pen,
                    geometry);
            }
        }
    }

    private GeoBounds GetBufferedViewportBounds(Size viewport) {
        var projection = GeoViewportProjection.Create(
            _centerLatitude,
            _centerLongitude,
            _zoom,
            viewport,
            _drawPanOffsetX,
            _drawPanOffsetY);
        var topLeft = projection.ScreenToGeo(new Point(-RenderOverscanPixels, -RenderOverscanPixels));
        var bottomRight = projection.ScreenToGeo(new Point(
            viewport.Width + RenderOverscanPixels,
            viewport.Height + RenderOverscanPixels));
        return new GeoBounds(
            Math.Min(topLeft.Longitude, bottomRight.Longitude),
            Math.Min(topLeft.Latitude, bottomRight.Latitude),
            Math.Max(topLeft.Longitude, bottomRight.Longitude),
            Math.Max(topLeft.Latitude, bottomRight.Latitude));
    }

    private StreamGeometry CreateGeometry(
        IReadOnlyList<GeoPoint> part,
        bool closed,
        GeoViewportProjection projection) {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open()) {
            var first = projection.GeoToScreen(part[0]);
            var lastEmitted = first;
            context.BeginFigure(first, closed, closed);
            for (var i = 1; i < part.Count; i++) {
                var screen = projection.GeoToScreen(part[i]);
                var isLast = i == part.Count - 1;
                if (!isLast && (screen - lastEmitted).LengthSquared < SimplifyDistanceSquared) {
                    continue;
                }

                context.LineTo(screen, true, false);
                lastEmitted = screen;
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private void ApplyPanTransform() {
        var offsetX = _panOffsetX - _drawPanOffsetX;
        var offsetY = _panOffsetY - _drawPanOffsetY;
        RenderTransform = AreClose(offsetX, 0, PixelTolerance) && AreClose(offsetY, 0, PixelTolerance)
            ? Transform.Identity
            : new TranslateTransform(offsetX, offsetY);
    }

    private Brush FindBrush(string key, Brush fallback) {
        return TryFindResource(key) as Brush ?? fallback;
    }

    internal static bool RequiresDrawingRefreshCore(
        MapDocument? currentDocument,
        MapDocument? nextDocument,
        double currentCenterLatitude,
        double nextCenterLatitude,
        double currentCenterLongitude,
        double nextCenterLongitude,
        int currentZoom,
        int nextZoom,
        double lastRenderedWidth,
        double actualWidth,
        double lastRenderedHeight,
        double actualHeight,
        double drawPanOffsetX,
        double panOffsetX,
        double drawPanOffsetY,
        double panOffsetY) {
        return !ReferenceEquals(currentDocument, nextDocument) ||
            !AreClose(currentCenterLatitude, nextCenterLatitude, CoordinateTolerance) ||
            !AreClose(currentCenterLongitude, nextCenterLongitude, CoordinateTolerance) ||
            currentZoom != nextZoom ||
            !AreClose(lastRenderedWidth, actualWidth, PixelTolerance) ||
            !AreClose(lastRenderedHeight, actualHeight, PixelTolerance) ||
            Math.Abs(panOffsetX - drawPanOffsetX) > PanRebaseThresholdPixels ||
            Math.Abs(panOffsetY - drawPanOffsetY) > PanRebaseThresholdPixels;
    }

    private static bool AreClose(double left, double right, double tolerance) {
        return Math.Abs(left - right) < tolerance;
    }
}
