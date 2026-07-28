using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Controls;

internal static class VectorLabelRenderer {
    private const double PointLabelMinimumZoom = 10;
    private const double LineLabelMinimumZoom = 11;
    private const double AreaLabelMinimumZoom = 11;
    private static readonly Typeface LabelTypeface = new(SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface ShieldTypeface = new(SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public static void Render(
        DrawingContext drawingContext,
        IReadOnlyList<MapFeature> features,
        GeoViewportProjection projection,
        VectorRenderPalette palette,
        int zoom,
        double pixelsPerDip) {
        foreach (var feature in features) {
            if (feature.IsHidden) continue;

            var labels = MapFeatureLabeler.GetLabels(feature);
            if (labels.Count == 0) continue;

            if (!ShouldRender(feature, zoom)) continue;

            var anchor = GetAnchor(feature, projection);
            DrawLabels(drawingContext, feature, labels, anchor, palette, pixelsPerDip);
        }
    }

    private static bool ShouldRender(MapFeature feature, int zoom) {
        return feature.GeometryType switch {
            MapGeometryType.Point => zoom >= PointLabelMinimumZoom,
            MapGeometryType.LineString => zoom >= LineLabelMinimumZoom,
            MapGeometryType.Polygon => zoom >= AreaLabelMinimumZoom,
            _ => false
        };
    }

    private static Point GetAnchor(MapFeature feature, GeoViewportProjection projection) {
        return feature.GeometryType switch {
            MapGeometryType.Point => GetPointAnchor(feature, projection),
            MapGeometryType.Polygon => projection.GeoToScreen(feature.Bounds.Center),
            _ => GetLineAnchor(feature, projection)
        };
    }

    private static Point GetPointAnchor(MapFeature feature, GeoViewportProjection projection) {
        return feature.Points.Any()
            ? projection.GeoToScreen(feature.Points.First())
            : new Point();
    }

    private static Point GetLineAnchor(MapFeature feature, GeoViewportProjection projection) {
        var bestPart = feature.Parts
            .Where(static part => part.Count >= 2)
            .OrderByDescending(part => GetProjectedLength(part, projection))
            .FirstOrDefault();

        if (bestPart is null || bestPart.Count == 0) {
            return feature.Points.Any()
                ? projection.GeoToScreen(feature.Points.First())
                : new Point();
        }

        return GetPointAlongPolyline(bestPart, projection);
    }

    private static double GetProjectedLength(IReadOnlyList<GeoPoint> part, GeoViewportProjection projection) {
        var length = 0.0;
        var previous = projection.GeoToScreen(part[0]);
        for (var i = 1; i < part.Count; i++) {
            var current = projection.GeoToScreen(part[i]);
            length += (current - previous).Length;
            previous = current;
        }
        return length;
    }

    private static Point GetPointAlongPolyline(IReadOnlyList<GeoPoint> part, GeoViewportProjection projection) {
        if (part.Count == 1) {
            return projection.GeoToScreen(part[0]);
        }

        var points = part.Select(projection.GeoToScreen).ToList();
        var totalLength = 0.0;
        for (var i = 1; i < points.Count; i++) {
            totalLength += (points[i] - points[i - 1]).Length;
        }

        if (totalLength <= 0) {
            return points[0];
        }

        var target = totalLength / 2.0;
        var traversed = 0.0;
        for (var i = 1; i < points.Count; i++) {
            var segment = points[i] - points[i - 1];
            var segmentLength = segment.Length;
            if (segmentLength <= 0) continue;

            if (traversed + segmentLength >= target) {
                var ratio = (target - traversed) / segmentLength;
                return points[i - 1] + segment * ratio;
            }

            traversed += segmentLength;
        }

        return points[^1];
    }

    private static void DrawLabels(
        DrawingContext drawingContext,
        MapFeature feature,
        IReadOnlyList<MapFeatureLabel> labels,
        Point anchor,
        VectorRenderPalette palette,
        double pixelsPerDip) {
        if (labels.Count == 1) {
            var offset = feature.GeometryType switch {
                MapGeometryType.Point => -14,
                MapGeometryType.LineString => -10,
                MapGeometryType.Polygon => -4,
                _ => 0
            };
            DrawLabel(drawingContext, labels[0], anchor, palette, pixelsPerDip, offset);
            return;
        }

        var primary = labels[0];
        var secondary = labels[1];
        DrawLabel(drawingContext, primary, anchor, palette, pixelsPerDip, -12, additionalPaddingX: 1);
        DrawLabel(drawingContext, secondary, anchor, palette, pixelsPerDip, 12);
    }

    private static void DrawLabel(
        DrawingContext drawingContext,
        MapFeatureLabel label,
        Point anchor,
        VectorRenderPalette palette,
        double pixelsPerDip,
        double verticalOffset,
        double additionalPaddingX = 0) {
        var text = NormalizeLabel(label.Text, label.Kind);
        if (string.IsNullOrWhiteSpace(text)) return;

        var fontSize = label.Kind == MapFeatureLabelKind.Ref ? 11.0 : 12.0;
        var typeface = label.Kind == MapFeatureLabelKind.Ref ? ShieldTypeface : LabelTypeface;
        var formatted = CreateFormattedText(text, typeface, fontSize, palette.LabelTextBrush, pixelsPerDip);
        var width = Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
        var height = Math.Ceiling(formatted.Height);
        var x = anchor.X - width / 2.0;
        var y = anchor.Y - height / 2.0 + verticalOffset;

        if (label.Kind == MapFeatureLabelKind.Ref) {
            var paddingX = 6.0 + additionalPaddingX;
            var paddingY = 2.0;
            var rect = new Rect(x - paddingX, y - paddingY, width + paddingX * 2, height + paddingY * 2);
            drawingContext.DrawRoundedRectangle(palette.LabelBackgroundBrush, palette.LabelBorderPen, rect, 3.0, 3.0);
            drawingContext.DrawText(formatted, new Point(rect.X + (rect.Width - width) / 2.0, rect.Y + (rect.Height - height) / 2.0));
            return;
        }

        drawingContext.DrawText(formatted, new Point(x, y));
    }

    private static FormattedText CreateFormattedText(
        string text,
        Typeface typeface,
        double fontSize,
        Brush foreground,
        double pixelsPerDip) {
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            foreground,
            pixelsPerDip);
    }

    private static string NormalizeLabel(string text, MapFeatureLabelKind kind) {
        var trimmed = text.Trim();
        if (kind == MapFeatureLabelKind.Ref) {
            trimmed = string.Join("/", trimmed.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return trimmed.Length <= 48 ? trimmed : trimmed[..45] + "...";
    }
}
