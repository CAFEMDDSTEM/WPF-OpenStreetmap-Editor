using System.Windows;
using System.Windows.Media;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Controls;

internal static class VectorPointSymbolRenderer {
    public static void Draw(
        DrawingContext drawingContext,
        Point center,
        VectorPointSymbolKind symbolKind,
        PointRenderStyle style) {
        drawingContext.DrawEllipse(style.Fill, style.Stroke, center, style.Radius, style.Radius);

        if (symbolKind == VectorPointSymbolKind.Circle) return;

        var iconBrush = style.Stroke?.Brush ?? Brushes.Black;
        var iconPen = VectorRenderPalette.CreatePen(iconBrush, Math.Max(0.8, style.Radius * 0.18));
        var radius = style.Radius;
        switch (symbolKind) {
            case VectorPointSymbolKind.Place:
                drawingContext.DrawEllipse(null, iconPen, center, radius * 0.42, radius * 0.42);
                break;
            case VectorPointSymbolKind.Food:
                DrawFood(drawingContext, center, radius, iconPen);
                break;
            case VectorPointSymbolKind.Parking:
                DrawParking(drawingContext, center, radius, iconPen);
                break;
            case VectorPointSymbolKind.Medical:
                DrawMedical(drawingContext, center, radius, iconPen);
                break;
            case VectorPointSymbolKind.Education:
                DrawEducation(drawingContext, center, radius, iconBrush, iconPen);
                break;
            case VectorPointSymbolKind.Transit:
                DrawTransit(drawingContext, center, radius, iconBrush, iconPen);
                break;
            case VectorPointSymbolKind.Shop:
                DrawShop(drawingContext, center, radius, iconBrush, iconPen);
                break;
            case VectorPointSymbolKind.Tourism:
                DrawTourism(drawingContext, center, radius, iconBrush, iconPen);
                break;
        }
    }

    private static void DrawFood(DrawingContext drawingContext, Point center, double radius, Pen pen) {
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.22, center.Y - radius * 0.45), new Point(center.X - radius * 0.22, center.Y + radius * 0.45));
        drawingContext.DrawLine(pen, new Point(center.X + radius * 0.18, center.Y - radius * 0.45), new Point(center.X + radius * 0.18, center.Y + radius * 0.45));
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.38, center.Y - radius * 0.25), new Point(center.X - radius * 0.06, center.Y - radius * 0.25));
    }

    private static void DrawParking(DrawingContext drawingContext, Point center, double radius, Pen pen) {
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.28, center.Y + radius * 0.45), new Point(center.X - radius * 0.28, center.Y - radius * 0.45));
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.28, center.Y - radius * 0.45), new Point(center.X + radius * 0.2, center.Y - radius * 0.45));
        drawingContext.DrawLine(pen, new Point(center.X + radius * 0.2, center.Y - radius * 0.45), new Point(center.X + radius * 0.2, center.Y));
        drawingContext.DrawLine(pen, new Point(center.X + radius * 0.2, center.Y), new Point(center.X - radius * 0.28, center.Y));
    }

    private static void DrawMedical(DrawingContext drawingContext, Point center, double radius, Pen pen) {
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.45, center.Y), new Point(center.X + radius * 0.45, center.Y));
        drawingContext.DrawLine(pen, new Point(center.X, center.Y - radius * 0.45), new Point(center.X, center.Y + radius * 0.45));
    }

    private static void DrawEducation(
        DrawingContext drawingContext,
        Point center,
        double radius,
        Brush brush,
        Pen pen) {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open()) {
            context.BeginFigure(new Point(center.X - radius * 0.5, center.Y - radius * 0.08), true, true);
            context.LineTo(new Point(center.X, center.Y - radius * 0.42), true, false);
            context.LineTo(new Point(center.X + radius * 0.5, center.Y - radius * 0.08), true, false);
            context.LineTo(new Point(center.X, center.Y + radius * 0.18), true, false);
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(brush, null, geometry);
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.3, center.Y + radius * 0.34), new Point(center.X + radius * 0.3, center.Y + radius * 0.34));
    }

    private static void DrawTransit(
        DrawingContext drawingContext,
        Point center,
        double radius,
        Brush brush,
        Pen pen) {
        var body = new Rect(center.X - radius * 0.46, center.Y - radius * 0.3, radius * 0.92, radius * 0.58);
        drawingContext.DrawRectangle(null, pen, body);
        drawingContext.DrawEllipse(brush, null, new Point(center.X - radius * 0.25, center.Y + radius * 0.34), radius * 0.1, radius * 0.1);
        drawingContext.DrawEllipse(brush, null, new Point(center.X + radius * 0.25, center.Y + radius * 0.34), radius * 0.1, radius * 0.1);
    }

    private static void DrawShop(
        DrawingContext drawingContext,
        Point center,
        double radius,
        Brush brush,
        Pen pen) {
        var bag = new Rect(center.X - radius * 0.34, center.Y - radius * 0.12, radius * 0.68, radius * 0.56);
        drawingContext.DrawRectangle(null, pen, bag);
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.2, center.Y - radius * 0.12), new Point(center.X - radius * 0.08, center.Y - radius * 0.38));
        drawingContext.DrawLine(pen, new Point(center.X + radius * 0.08, center.Y - radius * 0.38), new Point(center.X + radius * 0.2, center.Y - radius * 0.12));
        drawingContext.DrawEllipse(brush, null, center, radius * 0.08, radius * 0.08);
    }

    private static void DrawTourism(
        DrawingContext drawingContext,
        Point center,
        double radius,
        Brush brush,
        Pen pen) {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open()) {
            context.BeginFigure(new Point(center.X, center.Y - radius * 0.5), true, true);
            context.LineTo(new Point(center.X + radius * 0.42, center.Y), true, false);
            context.LineTo(new Point(center.X, center.Y + radius * 0.5), true, false);
            context.LineTo(new Point(center.X - radius * 0.42, center.Y), true, false);
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(brush, null, geometry);
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.18, center.Y), new Point(center.X + radius * 0.18, center.Y));
    }
}
