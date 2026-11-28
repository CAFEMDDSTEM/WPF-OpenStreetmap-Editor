using System.Windows;
using System.Windows.Media;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Controls;

internal static class VectorLayerRenderer {
    private const double SimplifyDistanceSquared = 0.64;
    private const double VertexHandleRadius = 2.75;
    private const double SelectedVertexHandleRadius = 4.5;

    public static void Render(
        DrawingContext drawingContext,
        IReadOnlyList<MapFeature> features,
        GeoViewportProjection projection,
        VectorRenderPalette palette,
        int zoom,
        double pixelsPerDip,
        MapFeature? hoveredFeature = null,
        VertexHit? hoveredVertex = null) {
        var styledFeatures = features
            .Select(static feature => new StyledFeature(feature, VectorFeatureStyler.GetStyle(feature)))
            .OrderBy(static item => item.Style.LayerOrder)
            .ToList();

        DrawAreaFeatures(drawingContext, styledFeatures, projection, palette);
        DrawLineFeatureGroup(drawingContext, styledFeatures, projection, palette, LineDepth.Tunnel);
        DrawLineFeatureGroup(drawingContext, styledFeatures, projection, palette, LineDepth.Surface);
        DrawLineFeatureGroup(drawingContext, styledFeatures, projection, palette, LineDepth.Bridge);
        DrawPointFeatures(drawingContext, styledFeatures, projection, palette);
        DrawHoveredFeature(drawingContext, hoveredFeature, projection, palette);
        VectorLabelRenderer.Render(drawingContext, features, projection, palette, zoom, pixelsPerDip);
        DrawSelectionFeatures(drawingContext, styledFeatures, projection, palette);
        DrawVertexHandles(drawingContext, styledFeatures, projection, palette, hoveredVertex);
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

    private static void DrawLineFeatureGroup(
        DrawingContext drawingContext,
        IReadOnlyList<StyledFeature> features,
        GeoViewportProjection projection,
        VectorRenderPalette palette,
        LineDepth depth) {
        var lineFeatures = features
            .Where(item => item.Style.RenderMode == VectorFeatureRenderMode.Line && GetLineDepth(item.Style) == depth)
            .ToList();

        foreach (var styledFeature in lineFeatures) {
            DrawStructureUnderlay(drawingContext, styledFeature, projection, palette);
        }

        DrawLineFeatures(drawingContext, lineFeatures, projection, palette, drawCasing: true);
        DrawLineFeatures(drawingContext, lineFeatures, projection, palette, drawCasing: false);

        foreach (var styledFeature in lineFeatures) {
            DrawFordOverlay(drawingContext, styledFeature, projection, palette);
            DrawTerrainMarks(drawingContext, styledFeature, projection, palette);
        }
    }

    private static void DrawLineFeatures(
        DrawingContext drawingContext,
        IReadOnlyList<StyledFeature> features,
        GeoViewportProjection projection,
        VectorRenderPalette palette,
        bool drawCasing) {
        foreach (var styledFeature in features) {
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

    private static void DrawStructureUnderlay(
        DrawingContext drawingContext,
        StyledFeature styledFeature,
        GeoViewportProjection projection,
        VectorRenderPalette palette) {
        var modifiers = styledFeature.Style.Modifiers;
        var isBridge = (modifiers & VectorFeatureModifier.Bridge) != 0;
        var isTunnel = (modifiers & VectorFeatureModifier.Tunnel) != 0;
        if (!isBridge && !isTunnel) return;

        var lineStyle = palette.GetLineStyle(styledFeature.Style.Kind);
        var pen = VectorRenderPalette.CreatePen(
            isBridge ? palette.BridgeBrush : palette.TunnelBrush,
            lineStyle.Stroke.Thickness + (isBridge ? 4.0 : 5.0));
        DrawModifierGeometry(drawingContext, styledFeature.Feature, projection, pen);
    }

    private static void DrawFordOverlay(
        DrawingContext drawingContext,
        StyledFeature styledFeature,
        GeoViewportProjection projection,
        VectorRenderPalette palette) {
        if ((styledFeature.Style.Modifiers & VectorFeatureModifier.Ford) == 0) return;

        var pen = new Pen(palette.FordBrush, 2.0) {
            DashStyle = new DashStyle([9, 9], 9)
        };
        if (pen.CanFreeze) pen.Freeze();
        DrawModifierGeometry(drawingContext, styledFeature.Feature, projection, pen);
    }

    private static void DrawTerrainMarks(
        DrawingContext drawingContext,
        StyledFeature styledFeature,
        GeoViewportProjection projection,
        VectorRenderPalette palette) {
        var modifiers = styledFeature.Style.Modifiers;
        var embankment = (modifiers & VectorFeatureModifier.Embankment) != 0;
        var cutting = (modifiers & VectorFeatureModifier.Cutting) != 0;
        if (!embankment && !cutting) return;

        var lineStyle = palette.GetLineStyle(styledFeature.Style.Kind);
        var edgeOffset = Math.Max(lineStyle.Stroke.Thickness, lineStyle.Casing?.Thickness ?? 0) / 2.0;
        var pen = VectorRenderPalette.CreatePen(palette.TerrainMarkBrush, 1.0);
        foreach (var part in styledFeature.Feature.Parts) {
            if (part.Count < 2) continue;
            if (embankment) {
                drawingContext.DrawGeometry(null, pen, CreateHachureGeometry(part, projection, edgeOffset, 1));
            }
            if (cutting) {
                drawingContext.DrawGeometry(null, pen, CreateHachureGeometry(part, projection, edgeOffset, -1));
            }
        }
    }

    private static void DrawModifierGeometry(
        DrawingContext drawingContext,
        MapFeature feature,
        GeoViewportProjection projection,
        Pen pen) {
        foreach (var part in feature.Parts) {
            if (part.Count < 2) continue;
            drawingContext.DrawGeometry(null, pen, CreateGeometry(part, closed: false, projection));
        }
    }

    private static StreamGeometry CreateHachureGeometry(
        IReadOnlyList<GeoPoint> part,
        GeoViewportProjection projection,
        double edgeOffset,
        double side) {
        const double spacing = 12.0;
        const double markLength = 4.5;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open()) {
            var distanceToNextMark = spacing / 2.0;
            for (var i = 1; i < part.Count; i++) {
                var start = projection.GeoToScreen(part[i - 1]);
                var end = projection.GeoToScreen(part[i]);
                var segment = end - start;
                var segmentLength = segment.Length;
                if (segmentLength <= 0) continue;

                var tangent = segment / segmentLength;
                var normal = new Vector(-tangent.Y * side, tangent.X * side);
                var distanceOnSegment = distanceToNextMark;
                while (distanceOnSegment <= segmentLength) {
                    var center = start + tangent * distanceOnSegment;
                    var markStart = center + normal * edgeOffset;
                    var markEnd = center + normal * (edgeOffset + markLength);
                    context.BeginFigure(markStart, false, false);
                    context.LineTo(markEnd, true, false);
                    distanceOnSegment += spacing;
                }

                distanceToNextMark = distanceOnSegment - segmentLength;
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private static LineDepth GetLineDepth(VectorFeatureStyle style) {
        if ((style.Modifiers & VectorFeatureModifier.Bridge) != 0) return LineDepth.Bridge;
        if ((style.Modifiers & VectorFeatureModifier.Tunnel) != 0) return LineDepth.Tunnel;
        return LineDepth.Surface;
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

    private static void DrawVertexHandles(
        DrawingContext drawingContext,
        IReadOnlyList<StyledFeature> features,
        GeoViewportProjection projection,
        VectorRenderPalette palette,
        VertexHit? hoveredVertex) {
        foreach (var styledFeature in features) {
            if (styledFeature.Style.RenderMode == VectorFeatureRenderMode.Point) continue;

            var radius = styledFeature.Feature.IsSelected
                ? SelectedVertexHandleRadius
                : VertexHandleRadius;
            var fill = styledFeature.Feature.IsSelected
                ? palette.SelectionFill
                : palette.VertexHandleFill;
            var pen = styledFeature.Feature.IsSelected
                ? palette.VertexHandleSelectedPen
                : palette.VertexHandlePen;

            foreach (var part in styledFeature.Feature.Parts) {
                if (part.Count == 0) continue;

                var count = IsClosedRing(part) ? part.Count - 1 : part.Count;
                for (var i = 0; i < count; i++) {
                    var isHovered = hoveredVertex is not null &&
                        ReferenceEquals(hoveredVertex.Feature, styledFeature.Feature) &&
                        hoveredVertex.PartIndex == styledFeature.Feature.Parts.IndexOf(part) &&
                        hoveredVertex.PointIndex == i;
                    drawingContext.DrawEllipse(
                        isHovered ? palette.HoverFill : fill,
                        isHovered ? palette.HoverPen : pen,
                        projection.GeoToScreen(part[i]),
                        isHovered ? radius + 2.0 : radius,
                        isHovered ? radius + 2.0 : radius);
                }
            }
        }
    }

    private static void DrawHoveredFeature(
        DrawingContext drawingContext,
        MapFeature? feature,
        GeoViewportProjection projection,
        VectorRenderPalette palette) {
        if (feature is null || feature.IsHidden) return;

        foreach (var part in feature.Parts) {
            switch (feature.GeometryType) {
                case MapGeometryType.Point:
                    foreach (var point in part) {
                        drawingContext.DrawEllipse(
                            palette.HoverFill,
                            palette.HoverPen,
                            projection.GeoToScreen(point),
                            7.0,
                            7.0);
                    }
                    break;
                case MapGeometryType.LineString when part.Count >= 2:
                    drawingContext.DrawGeometry(
                        null,
                        palette.HoverWidePen,
                        CreateGeometry(part, closed: false, projection));
                    break;
                case MapGeometryType.Polygon when part.Count >= 3:
                    drawingContext.DrawGeometry(
                        null,
                        palette.HoverWidePen,
                        CreateGeometry(part, closed: true, projection));
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

    private static bool IsClosedRing(IReadOnlyList<GeoPoint> part) {
        return part.Count > 2 && part[0] == part[^1];
    }

    private enum LineDepth {
        Tunnel,
        Surface,
        Bridge
    }

    private sealed record StyledFeature(MapFeature Feature, VectorFeatureStyle Style);
}
