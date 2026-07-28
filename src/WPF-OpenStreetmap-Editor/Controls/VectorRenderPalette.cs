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
    Brush LabelTextBrush,
    Brush LabelBackgroundBrush,
    Pen LabelBorderPen,
    Brush SelectionStroke,
    Brush SelectionFill,
    Pen SelectionPen,
    Brush HoverFill,
    Pen HoverPen,
    Pen HoverWidePen,
    Brush VertexHandleFill,
    Pen VertexHandlePen,
    Pen VertexHandleSelectedPen) {
    public static VectorRenderPalette Create(Func<string, object?> findResource) {
        var textBrush = FindBrush(findResource, "Theme.TextBrush", SystemColors.WindowTextBrush);
        var borderBrush = FindBrush(findResource, "Theme.BorderBrush", SystemColors.ActiveBorderBrush);
        var surfaceBrush = FindBrush(findResource, "Theme.SurfaceBrush", SystemColors.WindowBrush);
        var mapBrush = FindBrush(findResource, "Theme.MapBackgroundBrush", SystemColors.ControlBrush);

        var genericArea = CreateAreaStyle(findResource, "GenericArea", mapBrush, borderBrush, 0.8);
        var genericLine = CreateLineStyle(findResource, "GenericLine", textBrush, textBrush, 1.2, 1.2);
        var genericPoint = CreatePointStyle(findResource, "GenericPoint", surfaceBrush, textBrush, 3.5, 1.0);
        var labelBackground = CloneWithOpacity(surfaceBrush, 0.92);

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
            [VectorFeatureStyleKind.TrackRoad] = CreateLineStyle(findResource, "TrackRoad", genericLine.Stroke.Brush, genericLine.Stroke.Brush, 0.9, 1.6, [6, 4]),
            [VectorFeatureStyleKind.ServiceRoad] = CreateLineStyle(findResource, "ServiceRoad", genericLine.Stroke.Brush, genericLine.Stroke.Brush, 1.0, 2.2),
            [VectorFeatureStyleKind.ResidentialRoad] = CreateLineStyle(findResource, "ResidentialRoad", genericLine.Stroke.Brush, genericLine.Stroke.Brush, 1.8, 3.4),
            [VectorFeatureStyleKind.LivingStreetRoad] = CreateLineStyle(findResource, "LivingStreetRoad", genericLine.Stroke.Brush, genericLine.Stroke.Brush, 1.8, 3.2),
            [VectorFeatureStyleKind.UnclassifiedRoad] = CreateLineStyle(findResource, "UnclassifiedRoad", genericLine.Stroke.Brush, genericLine.Stroke.Brush, 2.0, 3.4),
            [VectorFeatureStyleKind.LocalRoad] = CreateLineStyle(findResource, "LocalRoad", genericLine.Stroke.Brush, genericLine.Stroke.Brush, 2.2, 4.0),
            [VectorFeatureStyleKind.TertiaryRoad] = CreateLineStyle(findResource, "TertiaryRoad", genericLine.Stroke.Brush, genericLine.Stroke.Brush, 2.6, 4.4),
            [VectorFeatureStyleKind.SecondaryRoad] = CreateLineStyle(findResource, "SecondaryRoad", genericLine.Stroke.Brush, genericLine.Stroke.Brush, 3.0, 5.0),
            [VectorFeatureStyleKind.PrimaryRoad] = CreateLineStyle(findResource, "PrimaryRoad", genericLine.Stroke.Brush, genericLine.Stroke.Brush, 3.4, 5.6),
            [VectorFeatureStyleKind.TrunkRoad] = CreateLineStyle(findResource, "TrunkRoad", genericLine.Stroke.Brush, genericLine.Stroke.Brush, 3.8, 6.2),
            [VectorFeatureStyleKind.Motorway] = CreateLineStyle(findResource, "Motorway", genericLine.Stroke.Brush, genericLine.Stroke.Brush, 4.2, 6.8)
        };

        var pointStyles = new Dictionary<VectorFeatureStyleKind, PointRenderStyle> {
            [VectorFeatureStyleKind.GenericPoint] = genericPoint,
            [VectorFeatureStyleKind.Poi] = CreatePointStyle(findResource, "Poi", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.FoodPoint] = CreatePointStyle(findResource, "FoodPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.ParkingPoint] = CreatePointStyle(findResource, "ParkingPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.MedicalPoint] = CreatePointStyle(findResource, "MedicalPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.EducationPoint] = CreatePointStyle(findResource, "EducationPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.TransitPoint] = CreatePointStyle(findResource, "TransitPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.FuelPoint] = CreatePointStyle(findResource, "FuelPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.BankPoint] = CreatePointStyle(findResource, "BankPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.ToiletPoint] = CreatePointStyle(findResource, "ToiletPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.SafetyPoint] = CreatePointStyle(findResource, "SafetyPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.PostPoint] = CreatePointStyle(findResource, "PostPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.HotelPoint] = CreatePointStyle(findResource, "HotelPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.ShopPoint] = CreatePointStyle(findResource, "ShopPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.TourismPoint] = CreatePointStyle(findResource, "TourismPoint", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0),
            [VectorFeatureStyleKind.Place] = CreatePointStyle(findResource, "Place", genericPoint.Fill, genericPoint.Stroke?.Brush ?? textBrush, genericPoint.Radius, genericPoint.Stroke?.Thickness ?? 1.0)
        };

        var selectionStroke = FindBrush(findResource, "Theme.AccentBrush", SystemColors.HighlightBrush);
        var selectionFill = CloneWithOpacity(
            FindBrush(findResource, "Theme.SelectionBrush", SystemColors.HighlightBrush),
            0.35);
        var hoverFill = CloneWithOpacity(
            FindBrush(findResource, "Theme.SelectionBrush", SystemColors.HighlightBrush),
            0.18);
        var vertexHandleFill = CloneWithOpacity(surfaceBrush, 0.92);

        return new VectorRenderPalette(
            areaStyles,
            lineStyles,
            pointStyles,
            textBrush,
            labelBackground,
            CreatePen(borderBrush, 1.0),
            selectionStroke,
            selectionFill,
            CreatePen(selectionStroke, 2.0),
            hoverFill,
            CreatePen(CloneWithOpacity(selectionStroke, 0.95), 2.0),
            CreatePen(CloneWithOpacity(selectionStroke, 0.55), 8.0),
            vertexHandleFill,
            CreatePen(textBrush, 1.0),
            CreatePen(selectionStroke, 1.6));
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
        var fill = CloneWithOpacity(
            FindBrush(findResource, $"Theme.Map.{name}FillBrush", fillFallback),
            GetAreaFillOpacity(name));
        var stroke = CloneWithOpacity(
            FindBrush(findResource, $"Theme.Map.{name}StrokeBrush", strokeFallback),
            0.72);
        var strokeThickness = Math.Max(
            FindDouble(findResource, $"Theme.Map.{name}StrokeThickness", strokeThicknessFallback),
            IsOutlineArea(name) ? 2.4 : 1.3);
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
        double casingThicknessFallback,
        IReadOnlyList<double>? dashArrayFallback = null) {
        var stroke = CloneWithOpacity(
            FindBrush(findResource, $"Theme.Map.{name}StrokeBrush", strokeFallback),
            0.72);
        var strokeThickness = FindDouble(findResource, $"Theme.Map.{name}StrokeThickness", strokeThicknessFallback);
        var casing = CloneWithOpacity(
            FindBrush(findResource, $"Theme.Map.{name}CasingBrush", casingFallback),
            0.34);
        var casingThickness = FindDouble(findResource, $"Theme.Map.{name}CasingThickness", casingThicknessFallback);
        var dashArray = FindDashArray(findResource, $"Theme.Map.{name}DashArray", dashArrayFallback);

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

    private static IReadOnlyList<double> FindDashArray(
        Func<string, object?> findResource,
        string key,
        IReadOnlyList<double>? fallback = null) {
        return findResource(key) switch {
            double[] values => values,
            DoubleCollection values => values.ToArray(),
            _ => fallback ?? []
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

    private static bool IsOutlineArea(string name) {
        return name is "Building" or "BuiltArea" or "GenericArea";
    }

    private static double GetAreaFillOpacity(string name) {
        return IsOutlineArea(name) ? 0.04 : 0.12;
    }
}
