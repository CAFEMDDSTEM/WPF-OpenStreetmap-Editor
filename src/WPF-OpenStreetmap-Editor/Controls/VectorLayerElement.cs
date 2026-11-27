using System.Windows;
using System.Windows.Media;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Controls;

public sealed class VectorLayerElement : FrameworkElement {
    private const double RenderOverscanPixels = 768;
    private const double PanRebaseThresholdPixels = RenderOverscanPixels * 0.5;
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
    private long _documentRevision;
    private long _visualStateRevision;
    private MapDisplayTransform _displayTransform = MapDisplayTransform.Identity;
    private MapFeature? _hoveredFeature;
    private VertexHit? _hoveredVertex;
    private bool _showDownloadedArea = true;

    public VectorRenderPlan? LastPlan { get; private set; }

    public bool ShowDownloadedArea {
        get => _showDownloadedArea;
        set {
            if (_showDownloadedArea == value) return;
            _showDownloadedArea = value;
            InvalidateVisual();
        }
    }

    internal bool RequiresDrawingRefresh(
        MapDocument? document,
        double centerLatitude,
        double centerLongitude,
        int zoom,
        double panOffsetX,
        double panOffsetY,
        long documentRevision,
        long visualStateRevision,
        MapDisplayTransform displayTransform,
        MapFeature? hoveredFeature,
        VertexHit? hoveredVertex) {
        return _documentRevision != documentRevision ||
            _visualStateRevision != visualStateRevision ||
            !ReferenceEquals(_displayTransform, displayTransform) ||
            !ReferenceEquals(_hoveredFeature, hoveredFeature) ||
            !SameVertex(_hoveredVertex, hoveredVertex) ||
            RequiresDrawingRefreshCore(
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
                panOffsetY,
                _documentRevision,
                documentRevision,
                _visualStateRevision,
                visualStateRevision);
    }

    public VectorLayerElement() {
        CacheMode = null;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
    }

    public void UpdateView(
        MapDocument? document,
        double centerLatitude,
        double centerLongitude,
        int zoom,
        double panOffsetX,
        double panOffsetY,
        long documentRevision = 0,
        long visualStateRevision = 0,
        MapDisplayTransform? displayTransform = null,
        MapFeature? hoveredFeature = null,
        VertexHit? hoveredVertex = null) {
        displayTransform ??= MapDisplayTransform.Identity;
        var needsRefresh = RequiresDrawingRefresh(
            document,
            centerLatitude,
            centerLongitude,
            zoom,
            panOffsetX,
            panOffsetY,
            documentRevision,
            visualStateRevision,
            displayTransform,
            hoveredFeature,
            hoveredVertex);

        _document = document;
        _centerLatitude = centerLatitude;
        _centerLongitude = centerLongitude;
        _zoom = zoom;
        _panOffsetX = panOffsetX;
        _panOffsetY = panOffsetY;
        _documentRevision = documentRevision;
        _visualStateRevision = visualStateRevision;
        _displayTransform = displayTransform;
        _hoveredFeature = hoveredFeature;
        _hoveredVertex = hoveredVertex;

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
            _drawPanOffsetY,
            _displayTransform);
        LastPlan = VectorRenderPlanner.Create(_document, GetBufferedViewportBounds(viewport));
        if (ShowDownloadedArea && _document.ActiveDataLayer.DownloadBounds is { IsValid: true } downloadBounds) {
            DrawOutsideDownloadedArea(drawingContext, projection, downloadBounds, viewport);
        }

        var palette = VectorRenderPalette.Create(key => TryFindResource(key));
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        VectorLayerRenderer.Render(
            drawingContext,
            LastPlan.Features,
            projection,
            palette,
            _zoom,
            pixelsPerDip,
            _hoveredFeature,
            _hoveredVertex);
    }

    private GeoBounds GetBufferedViewportBounds(Size viewport) {
        var projection = GeoViewportProjection.Create(
            _centerLatitude,
            _centerLongitude,
            _zoom,
            viewport,
            _drawPanOffsetX,
            _drawPanOffsetY,
            _displayTransform);
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

    private void DrawOutsideDownloadedArea(
        DrawingContext drawingContext,
        GeoViewportProjection projection,
        GeoBounds bounds,
        Size viewport) {
        var downloadedArea = CreateBoundsGeometry(bounds, projection);
        var viewportGeometry = new RectangleGeometry(new Rect(viewport));
        var outsideArea = new CombinedGeometry(GeometryCombineMode.Exclude, viewportGeometry, downloadedArea);
        var brush = CreateHatchBrush();

        drawingContext.PushClip(outsideArea);
        drawingContext.DrawRectangle(brush, null, new Rect(viewport));
        drawingContext.Pop();

        var outlineBrush = (TryFindResource("Theme.DownloadedAreaBrush") as Brush ?? SystemColors.HighlightBrush).CloneCurrentValue();
        var outlinePen = new Pen(outlineBrush, 2.0) { LineJoin = PenLineJoin.Round };
        outlinePen.Freeze();
        drawingContext.DrawGeometry(null, outlinePen, downloadedArea);
    }

    private static Geometry CreateBoundsGeometry(GeoBounds bounds, GeoViewportProjection projection) {
        const int edgeSegments = 24;
        var points = new List<Point>(edgeSegments * 4 + 1);
        for (var index = 0; index <= edgeSegments; index++) {
            var fraction = (double)index / edgeSegments;
            points.Add(projection.GeoToScreen(new GeoPoint(
                bounds.MinLongitude + (bounds.MaxLongitude - bounds.MinLongitude) * fraction,
                bounds.MaxLatitude)));
        }
        for (var index = 1; index <= edgeSegments; index++) {
            var fraction = (double)index / edgeSegments;
            points.Add(projection.GeoToScreen(new GeoPoint(
                bounds.MaxLongitude,
                bounds.MaxLatitude - (bounds.MaxLatitude - bounds.MinLatitude) * fraction)));
        }
        for (var index = 1; index <= edgeSegments; index++) {
            var fraction = (double)index / edgeSegments;
            points.Add(projection.GeoToScreen(new GeoPoint(
                bounds.MaxLongitude - (bounds.MaxLongitude - bounds.MinLongitude) * fraction,
                bounds.MinLatitude)));
        }
        for (var index = 1; index < edgeSegments; index++) {
            var fraction = (double)index / edgeSegments;
            points.Add(projection.GeoToScreen(new GeoPoint(
                bounds.MinLongitude,
                bounds.MinLatitude + (bounds.MaxLatitude - bounds.MinLatitude) * fraction)));
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open()) {
            context.BeginFigure(points[0], isFilled: true, isClosed: true);
            context.PolyLineTo(points.Skip(1).ToArray(), isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        return geometry;
    }

    private Brush CreateHatchBrush() {
        var brush = (TryFindResource("Theme.DownloadedAreaBrush") as Brush ?? SystemColors.HighlightBrush).CloneCurrentValue();
        brush.Opacity *= 0.45;
        brush.Freeze();
        var pen = new Pen(brush, 1.0);
        pen.Freeze();
        var group = new DrawingGroup();
        const double spacing = 14;
        for (var index = -1; index <= 1; index++) {
            var start = index * spacing;
            group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(
                new Point(start, spacing),
                new Point(start + spacing, 0))));
        }
        group.Freeze();

        var hatch = new DrawingBrush(group) {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, spacing, spacing),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, spacing, spacing),
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill
        };
        hatch.Freeze();
        return hatch;
    }

    private void ApplyPanTransform() {
        var offsetX = _panOffsetX - _drawPanOffsetX;
        var offsetY = _panOffsetY - _drawPanOffsetY;
        RenderTransform = AreClose(offsetX, 0, PixelTolerance) && AreClose(offsetY, 0, PixelTolerance)
            ? Transform.Identity
            : new TranslateTransform(offsetX, offsetY);
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
        double panOffsetY,
        long currentDocumentRevision = 0,
        long nextDocumentRevision = 0,
        long currentVisualStateRevision = 0,
        long nextVisualStateRevision = 0) {
        return !ReferenceEquals(currentDocument, nextDocument) ||
            currentDocumentRevision != nextDocumentRevision ||
            currentVisualStateRevision != nextVisualStateRevision ||
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

    private static bool SameVertex(VertexHit? left, VertexHit? right) {
        if (left is null || right is null) return left is null && right is null;

        return ReferenceEquals(left.Feature, right.Feature) &&
            left.PartIndex == right.PartIndex &&
            left.PointIndex == right.PointIndex;
    }
}
