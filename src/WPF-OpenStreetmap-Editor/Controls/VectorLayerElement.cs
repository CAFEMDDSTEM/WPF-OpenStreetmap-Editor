using System.Windows;
using System.Windows.Media;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Controls;

public sealed class VectorLayerElement : FrameworkElement {
    private const double RenderOverscanPixels = 768;
    private const double PanRebaseThresholdPixels = RenderOverscanPixels * 0.5;
    private const double SimplifyDistanceSquared = 0.64;

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
        var viewChanged = !ReferenceEquals(_document, document) ||
            !AreClose(_centerLatitude, centerLatitude) ||
            !AreClose(_centerLongitude, centerLongitude) ||
            _zoom != zoom;
        var viewportChanged = !AreClose(_lastRenderedWidth, ActualWidth) ||
            !AreClose(_lastRenderedHeight, ActualHeight);
        var panNeedsRebase =
            Math.Abs(panOffsetX - _drawPanOffsetX) > PanRebaseThresholdPixels ||
            Math.Abs(panOffsetY - _drawPanOffsetY) > PanRebaseThresholdPixels;

        _document = document;
        _centerLatitude = centerLatitude;
        _centerLongitude = centerLongitude;
        _zoom = zoom;
        _panOffsetX = panOffsetX;
        _panOffsetY = panOffsetY;

        if (viewChanged || viewportChanged || panNeedsRebase) {
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
        RenderTransform = AreClose(offsetX, 0) && AreClose(offsetY, 0)
            ? Transform.Identity
            : new TranslateTransform(offsetX, offsetY);
    }

    private Brush FindBrush(string key, Brush fallback) {
        return TryFindResource(key) as Brush ?? fallback;
    }

    private static bool AreClose(double left, double right) {
        return Math.Abs(left - right) < 0.5;
    }
}
