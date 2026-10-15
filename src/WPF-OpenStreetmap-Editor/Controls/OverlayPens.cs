using System.Windows;
using System.Windows.Media;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Controls;

/// <summary>
/// Pens and brushes the interaction overlays derive from the theme's accent brush, rebuilt only
/// when the theme swaps that brush, plus the snap indicator both draw and pen mode show.
/// </summary>
internal sealed class OverlayPens {
    private const double VertexSnapIndicatorRadius = 8.0;
    private const double SegmentSnapIndicatorRadius = 5.0;

    private Brush? _accentBrush;

    public Pen Solid { get; private set; } = null!;
    public Pen Dashed { get; private set; } = null!;
    public Pen Thin { get; private set; } = null!;
    public Brush Fill { get; private set; } = null!;
    public Brush SurfaceFill { get; private set; } = null!;

    /// <summary>Refreshes the cached pens from the element's current theme resources when needed.</summary>
    public void Ensure(FrameworkElement element) {
        var accent = element.TryFindResource("Theme.AccentBrush") as Brush ?? SystemColors.HighlightBrush;
        if (ReferenceEquals(accent, _accentBrush)) return;

        _accentBrush = accent;
        Solid = VectorRenderPalette.CreatePen(accent, 2.0);
        Dashed = VectorRenderPalette.CreatePen(CloneWithOpacity(accent, 0.9), 1.5, [5, 3]);
        Thin = VectorRenderPalette.CreatePen(CloneWithOpacity(accent, 0.8), 1.0);
        Fill = CloneWithOpacity(accent, 0.25);
        SurfaceFill = element.TryFindResource("Theme.SurfaceBrush") as Brush ?? SystemColors.WindowBrush;
    }

    /// <summary>Hollow ring for a vertex target, filled dot for a point on a segment.</summary>
    public void DrawSnapIndicator(DrawingContext drawingContext, SnapTarget snap, GeoViewportProjection projection) {
        var center = projection.GeoToScreen(snap.Point);
        if (snap.Kind == SnapTargetKind.Vertex) {
            drawingContext.DrawEllipse(null, Solid, center, VertexSnapIndicatorRadius, VertexSnapIndicatorRadius);
        } else {
            drawingContext.DrawEllipse(Fill, Solid, center, SegmentSnapIndicatorRadius, SegmentSnapIndicatorRadius);
        }
    }

    public void DrawVertexRing(DrawingContext drawingContext, Point center) {
        drawingContext.DrawEllipse(null, Solid, center, VertexSnapIndicatorRadius, VertexSnapIndicatorRadius);
    }

    private static Brush CloneWithOpacity(Brush brush, double opacity) {
        var clone = brush.CloneCurrentValue();
        clone.Opacity = opacity;
        if (clone.CanFreeze) clone.Freeze();
        return clone;
    }
}
