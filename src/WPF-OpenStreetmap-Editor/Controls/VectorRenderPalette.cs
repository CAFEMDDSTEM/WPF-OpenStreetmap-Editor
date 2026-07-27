using System.Windows;
using System.Windows.Media;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Controls;

internal sealed record AreaRenderStyle(Brush Fill, Pen? Stroke);

internal sealed record LineRenderStyle(Pen Stroke, Pen? Casing);

internal sealed record PointRenderStyle(Brush Fill, Pen? Stroke, double Radius);

internal sealed record VectorRenderPalette(
    IReadOnlyDictionary<VectorFeatureStyleKind, AreaRenderStyle> AreaStyles,
    IReadOnlyDictionary<VectorFeatureStyleKind, LineRenderStyle> LineStyles,
    IReadOnlyDictionary<VectorFeatureStyleKind, PointRenderStyle> PointStyles,
    Brush SelectionStroke,
    Brush SelectionFill,
    Pen SelectionPen) {
    public static VectorRenderPalette Create(Func<string, object?> findResource) {
        var textBrush = FindBrush(findResource, "Theme.TextBrush", SystemColors.WindowTextBrush);
        var borderBrush = FindBrush(findResource, "Theme.BorderBrush", SystemColors.ActiveBorderBrush);
        var surfaceBrush = FindBrush(findResource, "Theme.SurfaceBrush", SystemColors.WindowBrush);
        var mapBrush = FindBrush(findResource, "Theme.MapBackgroundBrush", SystemColors.ControlBrush);

        var genericArea = CreateAreaStyle(findResource, "GenericArea", mapBrush, borderBrush, 0.8);
        var genericLine = CreateLineStyle(findResource, "GenericLine", textBrush, textBrush, 1.2, 1.2);
        var genericPoint = CreatePointStyle(findResource, "GenericPoint", surfaceBrush, textBrush, 3.5, 1.0);

        var areaStyles = new Dictionary<VectorFeatureStyleKind, AreaRenderStyle> {
            [VectorFeatureStyleKind.GenericArea] = genericArea,
            [VectorFeatureStyleKind.Water] = CreateAreaStyle(findResource, "Water", genericArea.Fill, genericArea.Stroke?.Brush ?? borderBrush, genericArea.Stroke?.Thickness ?? 0.8),
            [VectorFeatureStyleKind.Farmland] = CreateAreaStyle(findResource, "Farmland", genericArea.Fill, genericArea.Stroke?.Brush ?? borderBrush, genericArea.Stroke?.Thickness ?? 0.8),
            [VectorFeatureStyleKind.Forest] = CreateAreaStyle(findResource, "Forest", genericArea.Fill, genericArea.Stroke?.Brush ?? borderBrush, genericArea.Stroke?.Thickness ?? 0.8),
            [VectorFeatureStyleKind.Park] = CreateAreaStyle(findResource, "Park", genericArea.Fill, genericArea.Stroke?.Brush ?? borderBrush, genericArea.Stroke?.Thickness ?? 0.8),
            [VectorFeatureStyleKind.BuiltArea] = CreateAreaStyle(findResource, "BuiltArea", genericArea.Fill, genericArea.Stroke?.Brush ?? borderBrush, genericArea.Stroke?.Thickness ?? 0.8),
            [VectorFeatureStyleKind.Building] = CreateAreaStyle(findResource, "Building", genericArea.Fill, genericArea.Stroke?.Brush ?? borderBrush, genericArea.Stroke?.Thickness ?? 0.8)
        };

        var lineStyles = new Dictionary<VectorFeatureStyleKind, LineRenderStyle> {
            [VectorFeatureStyleKind.GenericLine] = genericLine,
            [VectorFeatureStyleKind.Boundary] = CreateLineStyle(findResource, "Boundary", genericLine.Stroke.Brush, genericLine.Stroke.Brush, genericLine.Stroke.Thickness, genericLine.Stroke.Thickness),
            [VectorFeatureStyleKind.Waterway] = CreateLineStyle(findResource, "Waterway", genericLine.Stroke.Brush, genericLine.Stroke.Brush, genericLine.Stroke.Thickness, genericLine.Stroke.Thickness),
            [VectorFeatureStyleKind.Rail] = CreateLineStyle(findResource, "Rail", genericLine.Stroke.Brush, genericLine.Stroke.Brush, genericLine.Stroke.Thickness, genericLine.Stroke.Thickness),
            [VectorFeatureStyleKind.Path] = CreateLineStyle(findResource, "Path", genericLine.Stroke.Brush, genericLine.Stroke.Brush, genericLine.Stroke.Thickness, genericLine.Stroke.Thickness),
            [VectorFeatureStyleKind.LocalRoad] = CreateLineStyle(findResource, "LocalRoad", genericLine.Stroke.Brush, genericLine.Stroke.Brush, genericLine.Stroke.Thickness, genericLine.Stroke.Thickness),
            [VectorFeatureStyleKind.SecondaryRoad] = CreateLineStyle(findResource, "SecondaryRoad", genericLine.Stroke.Brush, genericLine.Stroke.Brush, genericLine.Stroke.Thickness, genericLine.Stroke.Thickness),
            [VectorFeatureStyleKind.PrimaryRoad] = CreateLineStyle(findResource, "PrimaryRoad", genericLine.Stroke.Brush, genericLine.Stroke.Brush, genericLine.Stroke.Thickness, genericLine.Stroke.Thickness),
            [VectorFeatureStyleKind.Motorway] = CreateLineStyle(findResource, "Motorway", genericLine.Stroke.Brush, genericLine.Stroke.Brush, genericLine.Stroke.Thickness, genericLine.Stroke.Thickness)
        };

        var pointStyles = new Dictionary<VectorFeatureStyleKind, PointRenderStyle> {
            [VectorFeatureStyleKind.GenericPoint] = genericPoint,
            [VectorFeatureStyleKind.Poi] = CreatePointStyle(findResource, "Poi", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.FoodPoint] = CreatePointStyle(findResource, "FoodPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.ParkingPoint] = CreatePointStyle(findResource, "ParkingPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.MedicalPoint] = CreatePointStyle(findResource, "MedicalPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.EducationPoint] = CreatePointStyle(findResource, "EducationPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.TransitPoint] = CreatePointStyle(findResource, "TransitPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.ShopPoint] = CreatePointStyle(findResource, "ShopPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.TourismPoint] = CreatePointStyle(findResource, "TourismPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.Place] = CreatePointStyle(findResource, "Place", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0)
        };

        var selectionStroke = FindBrush(findResource, "Theme.AccentBrush", SystemColors.HighlightBrush);
        var selectionFill = CloneWithOpacity(
            FindBrush(findResource, "Theme.SelectionBrush", SystemColors.HighlightBrush),
            0.35);

        return new VectorRenderPalette(
            areaStyles,
            lineStyles,
            pointStyles,
            selectionStroke,
            selectionFill,
            CreatePen(selectionStroke, 2.0));
    }

    public AreaRenderStyle GetAreaStyle(VectorFeatureStyleKind kind) {
        return AreaStyles.TryGetValue(kind, out var style)
            ? style
            : AreaStyles[VectorFeatureStyleKind.GenericArea];
    }

    public LineRenderStyle GetLineStyle(VectorFeatureStyleKind kind) {
        return LineStyles.TryGetValue(kind, out var style)
            ? style
            : LineStyles[VectorFeatureStyleKind.GenericLine];
    }

    public PointRenderStyle GetPointStyle(VectorFeatureStyleKind kind) {
        return PointStyles.TryGetValue(kind, out var style)
            ? style
            : PointStyles[VectorFeatureStyleKind.GenericPoint];
    }

    private static AreaRenderStyle CreateAreaStyle(
        Func<string, object?> findResource,
        string name,
        Brush fillFallback,
        Brush strokeFallback,
        double strokeThicknessFallback) {
        var fill = FindBrush(findResource, $"Theme.Map.{name}FillBrush", fillFallback);
        var stroke = FindBrush(findResource, $"Theme.Map.{name}StrokeBrush", strokeFallback);
        var strokeThickness = FindDouble(findResource, $"Theme.Map.{name}StrokeThickness", strokeThicknessFallback);
        return new AreaRenderStyle(
            fill,
            CreateOptionalPen(stroke, strokeThickness));
    }

    private static LineRenderStyle CreateLineStyle(
        Func<string, object?> findResource,
        string name,
        Brush strokeFallback,
        Brush casingFallback,
        double strokeThicknessFallback,
        double casingThicknessFallback) {
        var stroke = FindBrush(findResource, $"Theme.Map.{name}StrokeBrush", strokeFallback);
        var strokeThickness = FindDouble(findResource, $"Theme.Map.{name}StrokeThickness", strokeThicknessFallback);
        var casing = FindBrush(findResource, $"Theme.Map.{name}CasingBrush", casingFallback);
        var casingThickness = FindDouble(findResource, $"Theme.Map.{name}CasingThickness", casingThicknessFallback);
        var dashArray = FindDashArray(findResource, $"Theme.Map.{name}DashArray");

        return new LineRenderStyle(
            CreatePen(stroke, strokeThickness, dashArray),
            casingThickness > strokeThickness ? CreatePen(casing, casingThickness) : null);
    }

    private static PointRenderStyle CreatePointStyle(
        Func<string, object?> findResource,
        string name,
        Brush fillFallback,
        Brush strokeFallback,
        double radiusFallback,
        double strokeThicknessFallback) {
        var fill = FindBrush(findResource, $"Theme.Map.{name}FillBrush", fillFallback);
        var stroke = FindBrush(findResource, $"Theme.Map.{name}StrokeBrush", strokeFallback);
        var radius = FindDouble(findResource, $"Theme.Map.{name}Radius", radiusFallback);
        var strokeThickness = FindDouble(findResource, $"Theme.Map.{name}StrokeThickness", strokeThicknessFallback);
        return new PointRenderStyle(
            fill,
            CreateOptionalPen(stroke, strokeThickness),
            Math.Max(1.0, radius));
    }

    private static Brush FindBrush(Func<string, object?> findResource, string key, Brush fallback) {
        return findResource(key) as Brush ?? fallback;
    }

    private static double FindDouble(Func<string, object?> findResource, string key, double fallback) {
        return findResource(key) switch {
            double value when double.IsFinite(value) => value,
            int value => value,
            _ => fallback
        };
    }

    private static IReadOnlyList<double> FindDashArray(Func<string, object?> findResource, string key) {
        return findResource(key) switch {
            double[] values => values,
            DoubleCollection values => values.ToArray(),
            _ => []
        };
    }

    private static Brush CloneWithOpacity(Brush brush, double opacity) {
        var clone = brush.CloneCurrentValue();
        clone.Opacity = opacity;
        FreezeIfPossible(clone);
        return clone;
    }

    internal static Pen? CreateOptionalPen(Brush brush, double thickness) {
        return double.IsFinite(thickness) && thickness > 0
            ? CreatePen(brush, thickness)
            : null;
    }

    internal static Pen CreatePen(
        Brush brush,
        double thickness,
        IReadOnlyList<double>? dashArray = null) {
        var pen = new Pen(brush, Math.Max(0.1, thickness));
        if (dashArray is { Count: > 0 }) {
            pen.DashStyle = new DashStyle(dashArray, 0);
        }

        FreezeIfPossible(pen);
        return pen;
    }

    private static void FreezeIfPossible(Freezable value) {
        if (value.CanFreeze) value.Freeze();
    }
}
