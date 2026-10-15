using System.Windows;
using System.Windows.Media;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Controls;

/// <summary>
/// Draws the draw-mode rubber band and snap indicator above the vector layer. It is a separate element
/// so pointer movement while drawing repaints two primitives instead of every visible feature.
/// </summary>
public sealed class DrawPreviewElement : FrameworkElement {
    private const double VertexSnapIndicatorRadius = 8.0;
    private const double SegmentSnapIndicatorRadius = 5.0;

    private DrawPreview? _preview;
    private GeoViewportProjection _projection;
    private Brush? _accentBrush;
    private Pen? _rubberBandPen;
    private Pen? _snapIndicatorPen;
    private Brush? _snapFill;

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

        EnsurePens();
        var target = preview.Snap?.Point ?? preview.PointerPoint;
        if (preview.AnchorPoint is { } anchor && anchor.IsValid && target.IsValid) {
            drawingContext.DrawLine(_rubberBandPen, _projection.GeoToScreen(anchor), _projection.GeoToScreen(target));
        }

        if (preview.Snap is not { } snap) return;

        var center = _projection.GeoToScreen(snap.Point);
        if (snap.Kind == SnapTargetKind.Vertex) {
            drawingContext.DrawEllipse(null, _snapIndicatorPen, center, VertexSnapIndicatorRadius, VertexSnapIndicatorRadius);
        } else {
            drawingContext.DrawEllipse(_snapFill, _snapIndicatorPen, center, SegmentSnapIndicatorRadius, SegmentSnapIndicatorRadius);
        }
    }

    // Pens derive from the theme's accent brush and are rebuilt only when the theme swaps the brush.
    private void EnsurePens() {
        var accent = TryFindResource("Theme.AccentBrush") as Brush ?? SystemColors.HighlightBrush;
        if (ReferenceEquals(accent, _accentBrush) && _rubberBandPen is not null) return;

        _accentBrush = accent;
        _rubberBandPen = VectorRenderPalette.CreatePen(CloneWithOpacity(accent, 0.9), 1.5, [5, 3]);
        _snapIndicatorPen = VectorRenderPalette.CreatePen(accent, 2.0);
        _snapFill = CloneWithOpacity(accent, 0.25);
    }

    private static Brush CloneWithOpacity(Brush brush, double opacity) {
        var clone = brush.CloneCurrentValue();
        clone.Opacity = opacity;
        if (clone.CanFreeze) clone.Freeze();
        return clone;
    }
}
