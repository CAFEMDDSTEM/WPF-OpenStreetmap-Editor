using System.Windows;
using System.Windows.Media;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Controls;

internal static class VectorLayerRenderer {
    private const double SimplifyDistanceSquared = 0.64;

    public static void Render(
        DrawingContext drawingContext,
        IReadOnlyList<MapFeature> features,
        GeoViewportProjection projection,
        VectorRenderPalette palette) {
        var styledFeatures = features
            .Select(static feature => new StyledFeature(feature, VectorFeatureStyler.GetStyle(feature)))
            .OrderBy(static item => item.Style.LayerOrder)
            .ToList();

        DrawAreaFeatures(drawingContext, styledFeatures, projection, palette);
        DrawLineFeatures(drawingContext, styledFeatures, projection, palette, drawCasing: true);
        DrawLineFeatures(drawingContext, styledFeatures, projection, palette, drawCasing: false);
        DrawPointFeatures(drawingContext, styledFeatures, projection, palette);
        DrawSelectionFeatures(drawingContext, styledFeatures, projection, palette);
    }

    private static void DrawAreaFeatures(
        DrawingContext drawingContext,
        IReadOnlyList<StyledFeature> features,
        GeoViewportProjection projection,
        VectorRenderPalette palette) {
        foreach (var styledFeature in features) {
            if (styledFeature.Style.RenderMode != VectorFeatureRenderMode.Area) continue;

            var areaStyle = palette.GetAreaStyle(styledFeature.Style.Kind);
            foreach (var part in styledFeature.Feature.Parts) {
                if (part.Count < 3) continue;

                drawingContext.DrawGeometry(
                    areaStyle.Fill,
                    areaStyle.Stroke,
                    CreateGeometry(part, closed: true, projection));
            }
        }
    }

    private static void DrawLineFeatures(
        DrawingContext drawingContext,
        IReadOnlyList<StyledFeature> features,
        GeoViewportProjection projection,
        VectorRenderPalette palette,
        bool drawCasing) {
        foreach (var styledFeature in features) {
            if (styledFeature.Style.RenderMode != VectorFeatureRenderMode.Line) continue;

            var lineStyle = palette.GetLineStyle(styledFeature.Style.Kind);
            var pen = drawCasing ? lineStyle.Casing : lineStyle.Stroke;
            if (pen is null) continue;

            foreach (var part in styledFeature.Feature.Parts) {
                if (part.Count < 2) continue;

                drawingContext.DrawGeometry(
                    null,
                    pen,
                    CreateGeometry(part, closed: false, projection));
            }
        }
    }

    private static void DrawPointFeatures(
        DrawingContext drawingContext,
        IReadOnlyList<StyledFeature> features,
        GeoViewportProjection projection,
        VectorRenderPalette palette) {
        foreach (var styledFeature in features) {
            if (styledFeature.Style.RenderMode != VectorFeatureRenderMode.Point) continue;

            var pointStyle = palette.GetPointStyle(styledFeature.Style.Kind);
            foreach (var part in styledFeature.Feature.Parts) {
                foreach (var point in part) {
                    VectorPointSymbolRenderer.Draw(
                        drawingContext,
                        projection.GeoToScreen(point),
                        styledFeature.Style.SymbolKind,
                        pointStyle);
                }
            }
        }
    }

    private static void DrawSelectionFeatures(
        DrawingContext drawingContext,
        IReadOnlyList<StyledFeature> features,
        GeoViewportProjection projection,
        VectorRenderPalette palette) {
        foreach (var styledFeature in features.Where(static item => item.Feature.IsSelected)) {
            switch (styledFeature.Style.RenderMode) {
                case VectorFeatureRenderMode.Area:
                    DrawSelectedArea(drawingContext, styledFeature.Feature, projection, palette);
                    break;
                case VectorFeatureRenderMode.Line:
                    DrawSelectedLine(drawingContext, styledFeature, projection, palette);
                    break;
                case VectorFeatureRenderMode.Point:
                    DrawSelectedPoint(drawingContext, styledFeature.Feature, projection, palette);
                    break;
            }
        }
    }

    private static void DrawSelectedArea(
        DrawingContext drawingContext,
        MapFeature feature,
        GeoViewportProjection projection,
        VectorRenderPalette palette) {
        foreach (var part in feature.Parts) {
            if (part.Count < 3) continue;

            drawingContext.DrawGeometry(
                palette.SelectionFill,
                palette.SelectionPen,
                CreateGeometry(part, closed: true, projection));
        }
    }

    private static void DrawSelectedLine(
        DrawingContext drawingContext,
        StyledFeature styledFeature,
        GeoViewportProjection projection,
        VectorRenderPalette palette) {
        var lineStyle = palette.GetLineStyle(styledFeature.Style.Kind);
        var pen = VectorRenderPalette.CreatePen(palette.SelectionStroke, Math.Max(lineStyle.Stroke.Thickness + 3.0, 4.0));
        foreach (var part in styledFeature.Feature.Parts) {
            if (part.Count < 2) continue;

            drawingContext.DrawGeometry(
                null,
                pen,
                CreateGeometry(part, closed: false, projection));
        }
    }

    private static void DrawSelectedPoint(
        DrawingContext drawingContext,
        MapFeature feature,
        GeoViewportProjection projection,
        VectorRenderPalette palette) {
        foreach (var part in feature.Parts) {
            foreach (var point in part) {
                var screen = projection.GeoToScreen(point);
                drawingContext.DrawEllipse(
                    palette.SelectionFill,
                    palette.SelectionPen,
                    screen,
                    6.0,
                    6.0);
            }
        }
    }

    private static StreamGeometry CreateGeometry(
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

    private sealed record StyledFeature(MapFeature Feature, VectorFeatureStyle Style);
}
