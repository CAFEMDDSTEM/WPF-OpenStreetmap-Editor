using System.Windows;
using System.Windows.Media;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Controls;

/// <summary>
/// Pen-mode overlay state in document coordinates: the flattened committed curve, the pending segment
/// to the pointer, the anchors with their handles, the snap target under the pointer, and the first
/// anchor when hovering it would close the ring.
/// </summary>
public sealed record PenPreview(
    IReadOnlyList<GeoPoint> CommittedPath,
    IReadOnlyList<GeoPoint> PendingPath,
    IReadOnlyList<BezierAnchor> Anchors,
    SnapTarget? Snap,
    GeoPoint? ClosingAnchor = null);

/// <summary>Draws the pen tool's curve, anchors, handles and snap indicator above the vector layer.</summary>
public sealed class PenPreviewElement : FrameworkElement {
    private const double AnchorHalfSize = 3.5;
    private const double HandleRadius = 2.5;

    private readonly OverlayPens _pens = new();
    private PenPreview? _preview;
    private GeoViewportProjection _projection;

    public PenPreviewElement() {
        IsHitTestVisible = false;
    }

    public void Update(PenPreview? preview, GeoViewportProjection projection) {
        var wasVisible = _preview is not null;
        _preview = preview;
        _projection = projection;
        if (wasVisible || preview is not null) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext) {
        base.OnRender(drawingContext);
        if (_preview is not { } preview) return;

        _pens.Ensure(this);
        DrawPolyline(drawingContext, preview.CommittedPath, _pens.Solid);
        DrawPolyline(drawingContext, preview.PendingPath, _pens.Dashed);

        foreach (var anchor in preview.Anchors) {
            var center = _projection.GeoToScreen(anchor.Point);
            foreach (var handle in new[] { anchor.HandleIn, anchor.HandleOut }) {
                if (handle is not { } handlePoint) continue;

                var handleScreen = _projection.GeoToScreen(handlePoint);
                drawingContext.DrawLine(_pens.Thin, center, handleScreen);
                drawingContext.DrawEllipse(_pens.SurfaceFill, _pens.Thin, handleScreen, HandleRadius, HandleRadius);
            }
            drawingContext.DrawRectangle(
                _pens.SurfaceFill,
                _pens.Solid,
                new Rect(center.X - AnchorHalfSize, center.Y - AnchorHalfSize, AnchorHalfSize * 2, AnchorHalfSize * 2));
        }

        if (preview.ClosingAnchor is { } closingAnchor) _pens.DrawVertexRing(drawingContext, _projection.GeoToScreen(closingAnchor));
        if (preview.Snap is { } snap) _pens.DrawSnapIndicator(drawingContext, snap, _projection);
    }

    private void DrawPolyline(DrawingContext drawingContext, IReadOnlyList<GeoPoint> points, Pen pen) {
        if (points.Count < 2) return;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open()) {
            context.BeginFigure(_projection.GeoToScreen(points[0]), false, false);
            for (var i = 1; i < points.Count; i++) {
                context.LineTo(_projection.GeoToScreen(points[i]), true, false);
            }
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }
}
