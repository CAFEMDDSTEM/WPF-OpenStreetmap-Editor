using System.Windows;
using System.Windows.Media;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Controls;

/// <summary>
/// Draws the draw-mode rubber band and snap indicator above the vector layer. It is a separate element
/// so pointer movement while drawing repaints two primitives instead of every visible feature.
/// </summary>
public sealed class DrawPreviewElement : FrameworkElement {
    private readonly OverlayPens _pens = new();
    private DrawPreview? _preview;
    private GeoViewportProjection _projection;

    public DrawPreviewElement() {
        IsHitTestVisible = false;
    }

    public void Update(DrawPreview? preview, GeoViewportProjection projection) {
        var wasVisible = _preview is not null;
        _preview = preview;
        _projection = projection;
        if (wasVisible || preview is not null) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext) {
        base.OnRender(drawingContext);
        if (_preview is not { } preview) return;

        _pens.Ensure(this);
        var target = preview.Snap?.Point ?? preview.PointerPoint;
        if (preview.AnchorPoint is { } anchor && anchor.IsValid && target.IsValid) {
            drawingContext.DrawLine(_pens.Dashed, _projection.GeoToScreen(anchor), _projection.GeoToScreen(target));
        }

        if (preview.Snap is { } snap) _pens.DrawSnapIndicator(drawingContext, snap, _projection);
    }
}
