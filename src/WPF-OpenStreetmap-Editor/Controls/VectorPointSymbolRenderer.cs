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
            case VectorPointSymbolKind.Fuel:
                DrawFuel(drawingContext, center, radius, iconBrush, iconPen);
                break;
            case VectorPointSymbolKind.Bank:
                DrawBank(drawingContext, center, radius, iconBrush, iconPen);
                break;
            case VectorPointSymbolKind.Toilet:
                DrawToilet(drawingContext, center, radius, iconPen);
                break;
            case VectorPointSymbolKind.Safety:
                DrawSafety(drawingContext, center, radius, iconBrush, iconPen);
                break;
            case VectorPointSymbolKind.Post:
                DrawPost(drawingContext, center, radius, iconPen);
                break;
            case VectorPointSymbolKind.Hotel:
                DrawHotel(drawingContext, center, radius, iconBrush, iconPen);
                break;
            case VectorPointSymbolKind.Shop:
                DrawShop(drawingContext, center, radius, iconBrush, iconPen);
                break;
            case VectorPointSymbolKind.Tourism:
                DrawTourism(drawingContext, center, radius, iconBrush, iconPen);
                break;
            case VectorPointSymbolKind.Recreation:
                DrawRecreation(drawingContext, center, radius, iconBrush);
                break;
            case VectorPointSymbolKind.Nature:
                DrawNature(drawingContext, center, radius, iconBrush, iconPen);
                break;
            case VectorPointSymbolKind.Culture:
                DrawCulture(drawingContext, center, radius, iconBrush, iconPen);
                break;
            case VectorPointSymbolKind.Office:
                DrawOffice(drawingContext, center, radius, iconPen);
                break;
            case VectorPointSymbolKind.Craft:
                DrawCraft(drawingContext, center, radius, iconPen);
                break;
            case VectorPointSymbolKind.Emergency:
                DrawEmergency(drawingContext, center, radius, iconBrush, iconPen);
                break;
            case VectorPointSymbolKind.Utility:
                DrawUtility(drawingContext, center, radius, iconBrush, iconPen);
                break;
            case VectorPointSymbolKind.Power:
                DrawPower(drawingContext, center, radius, iconBrush);
                break;
            case VectorPointSymbolKind.Water:
                DrawWater(drawingContext, center, radius, iconPen);
                break;
            case VectorPointSymbolKind.Barrier:
                DrawBarrier(drawingContext, center, radius, iconBrush, iconPen);
                break;
            case VectorPointSymbolKind.Air:
                DrawAir(drawingContext, center, radius, iconPen);
                break;
            case VectorPointSymbolKind.Religion:
                DrawReligion(drawingContext, center, radius, iconPen);
                break;
            case VectorPointSymbolKind.Industrial:
                DrawIndustrial(drawingContext, center, radius, iconBrush, iconPen);
                break;
            case VectorPointSymbolKind.Home:
                DrawHome(drawingContext, center, radius, iconBrush, iconPen);
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

    private static void DrawFuel(
        DrawingContext drawingContext,
        Point center,
        double radius,
        Brush brush,
        Pen pen) {
        var pump = new Rect(center.X - radius * 0.32, center.Y - radius * 0.38, radius * 0.46, radius * 0.72);
        drawingContext.DrawRectangle(null, pen, pump);
        drawingContext.DrawLine(pen, new Point(center.X + radius * 0.14, center.Y - radius * 0.2), new Point(center.X + radius * 0.32, center.Y - radius * 0.34));
        drawingContext.DrawLine(pen, new Point(center.X + radius * 0.32, center.Y - radius * 0.34), new Point(center.X + radius * 0.32, center.Y + radius * 0.12));
        drawingContext.DrawRectangle(brush, null, new Rect(center.X - radius * 0.22, center.Y - radius * 0.2, radius * 0.14, radius * 0.2));
    }

    private static void DrawBank(
        DrawingContext drawingContext,
        Point center,
        double radius,
        Brush brush,
        Pen pen) {
        var roof = new StreamGeometry();
        using (var context = roof.Open()) {
            context.BeginFigure(new Point(center.X - radius * 0.5, center.Y - radius * 0.16), true, true);
            context.LineTo(new Point(center.X, center.Y - radius * 0.48), true, false);
            context.LineTo(new Point(center.X + radius * 0.5, center.Y - radius * 0.16), true, false);
        }
        roof.Freeze();
        drawingContext.DrawGeometry(brush, pen, roof);

        var baseRect = new Rect(center.X - radius * 0.44, center.Y - radius * 0.08, radius * 0.88, radius * 0.42);
        drawingContext.DrawRectangle(null, pen, baseRect);
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.22, center.Y - radius * 0.08), new Point(center.X - radius * 0.22, center.Y + radius * 0.34));
        drawingContext.DrawLine(pen, new Point(center.X, center.Y - radius * 0.08), new Point(center.X, center.Y + radius * 0.34));
        drawingContext.DrawLine(pen, new Point(center.X + radius * 0.22, center.Y - radius * 0.08), new Point(center.X + radius * 0.22, center.Y + radius * 0.34));
    }

    private static void DrawToilet(DrawingContext drawingContext, Point center, double radius, Pen pen) {
        drawingContext.DrawEllipse(null, pen, new Point(center.X - radius * 0.16, center.Y - radius * 0.12), radius * 0.12, radius * 0.18);
        drawingContext.DrawEllipse(null, pen, new Point(center.X + radius * 0.16, center.Y - radius * 0.12), radius * 0.12, radius * 0.18);
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.28, center.Y + radius * 0.2), new Point(center.X - radius * 0.06, center.Y - radius * 0.02));
        drawingContext.DrawLine(pen, new Point(center.X + radius * 0.28, center.Y + radius * 0.2), new Point(center.X + radius * 0.06, center.Y - radius * 0.02));
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.12, center.Y + radius * 0.12), new Point(center.X + radius * 0.12, center.Y + radius * 0.12));
    }

    private static void DrawSafety(
        DrawingContext drawingContext,
        Point center,
        double radius,
        Brush brush,
        Pen pen) {
        var shield = new StreamGeometry();
        using (var context = shield.Open()) {
            context.BeginFigure(new Point(center.X, center.Y - radius * 0.48), true, true);
            context.LineTo(new Point(center.X + radius * 0.34, center.Y - radius * 0.26), true, false);
            context.LineTo(new Point(center.X + radius * 0.28, center.Y + radius * 0.26), true, false);
            context.LineTo(new Point(center.X, center.Y + radius * 0.48), true, false);
            context.LineTo(new Point(center.X - radius * 0.28, center.Y + radius * 0.26), true, false);
            context.LineTo(new Point(center.X - radius * 0.34, center.Y - radius * 0.26), true, false);
        }
        shield.Freeze();
        drawingContext.DrawGeometry(brush, pen, shield);
    }

    private static void DrawPost(DrawingContext drawingContext, Point center, double radius, Pen pen) {
        var envelope = new Rect(center.X - radius * 0.46, center.Y - radius * 0.26, radius * 0.92, radius * 0.52);
        drawingContext.DrawRectangle(null, pen, envelope);
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.46, center.Y - radius * 0.26), new Point(center.X, center.Y + radius * 0.04));
        drawingContext.DrawLine(pen, new Point(center.X + radius * 0.46, center.Y - radius * 0.26), new Point(center.X, center.Y + radius * 0.04));
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.46, center.Y + radius * 0.26), new Point(center.X - radius * 0.04, center.Y - radius * 0.02));
        drawingContext.DrawLine(pen, new Point(center.X + radius * 0.46, center.Y + radius * 0.26), new Point(center.X + radius * 0.04, center.Y - radius * 0.02));
    }

    private static void DrawHotel(
        DrawingContext drawingContext,
        Point center,
        double radius,
        Brush brush,
        Pen pen) {
        drawingContext.DrawRectangle(null, pen, new Rect(center.X - radius * 0.46, center.Y - radius * 0.2, radius * 0.92, radius * 0.46));
        drawingContext.DrawRectangle(brush, null, new Rect(center.X - radius * 0.36, center.Y - radius * 0.06, radius * 0.22, radius * 0.12));
        drawingContext.DrawRectangle(brush, null, new Rect(center.X - radius * 0.08, center.Y - radius * 0.12, radius * 0.28, radius * 0.18));
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.46, center.Y + radius * 0.26), new Point(center.X + radius * 0.46, center.Y + radius * 0.26));
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

    private static void DrawRecreation(DrawingContext drawingContext, Point center, double radius, Brush brush) {
        drawingContext.DrawGeometry(brush, null, CreatePolygon(center, radius, [
            new Point(0, -0.48),
            new Point(0.14, -0.14),
            new Point(0.5, -0.12),
            new Point(0.22, 0.1),
            new Point(0.32, 0.45),
            new Point(0, 0.25),
            new Point(-0.32, 0.45),
            new Point(-0.22, 0.1),
            new Point(-0.5, -0.12),
            new Point(-0.14, -0.14)
        ]));
    }

    private static void DrawNature(
        DrawingContext drawingContext,
        Point center,
        double radius,
        Brush brush,
        Pen pen) {
        drawingContext.DrawGeometry(brush, null, CreatePolygon(center, radius, [
            new Point(0, -0.5),
            new Point(0.4, 0.1),
            new Point(0.14, 0.1),
            new Point(0.34, 0.42),
            new Point(-0.34, 0.42),
            new Point(-0.14, 0.1),
            new Point(-0.4, 0.1)
        ]));
        drawingContext.DrawLine(pen, new Point(center.X, center.Y + radius * 0.08), new Point(center.X, center.Y + radius * 0.5));
    }

    private static void DrawCulture(
        DrawingContext drawingContext,
        Point center,
        double radius,
        Brush brush,
        Pen pen) {
        drawingContext.DrawGeometry(brush, pen, CreatePolygon(center, radius, [
            new Point(-0.46, -0.14),
            new Point(0, -0.48),
            new Point(0.46, -0.14)
        ]));
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.38, center.Y + radius * 0.36), new Point(center.X + radius * 0.38, center.Y + radius * 0.36));
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.24, center.Y - radius * 0.08), new Point(center.X - radius * 0.24, center.Y + radius * 0.32));
        drawingContext.DrawLine(pen, new Point(center.X, center.Y - radius * 0.08), new Point(center.X, center.Y + radius * 0.32));
        drawingContext.DrawLine(pen, new Point(center.X + radius * 0.24, center.Y - radius * 0.08), new Point(center.X + radius * 0.24, center.Y + radius * 0.32));
    }

    private static void DrawOffice(DrawingContext drawingContext, Point center, double radius, Pen pen) {
        drawingContext.DrawRectangle(null, pen, new Rect(center.X - radius * 0.42, center.Y - radius * 0.12, radius * 0.84, radius * 0.52));
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.18, center.Y - radius * 0.12), new Point(center.X - radius * 0.18, center.Y - radius * 0.3));
        drawingContext.DrawLine(pen, new Point(center.X + radius * 0.18, center.Y - radius * 0.12), new Point(center.X + radius * 0.18, center.Y - radius * 0.3));
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.18, center.Y - radius * 0.3), new Point(center.X + radius * 0.18, center.Y - radius * 0.3));
    }

    private static void DrawCraft(DrawingContext drawingContext, Point center, double radius, Pen pen) {
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.36, center.Y + radius * 0.36), new Point(center.X + radius * 0.34, center.Y - radius * 0.34));
        drawingContext.DrawEllipse(null, pen, new Point(center.X + radius * 0.28, center.Y - radius * 0.28), radius * 0.15, radius * 0.15);
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.42, center.Y + radius * 0.2), new Point(center.X - radius * 0.2, center.Y + radius * 0.42));
    }

    private static void DrawEmergency(
        DrawingContext drawingContext,
        Point center,
        double radius,
        Brush brush,
        Pen pen) {
        drawingContext.DrawGeometry(brush, null, CreatePolygon(center, radius, [
            new Point(0, -0.48),
            new Point(0.42, 0.26),
            new Point(-0.42, 0.26)
        ]));
        drawingContext.DrawLine(pen, new Point(center.X, center.Y - radius * 0.24), new Point(center.X, center.Y + radius * 0.08));
        drawingContext.DrawEllipse(pen.Brush, null, new Point(center.X, center.Y + radius * 0.25), radius * 0.06, radius * 0.06);
    }

    private static void DrawUtility(
        DrawingContext drawingContext,
        Point center,
        double radius,
        Brush brush,
        Pen pen) {
        drawingContext.DrawEllipse(null, pen, center, radius * 0.32, radius * 0.32);
        drawingContext.DrawEllipse(brush, null, center, radius * 0.1, radius * 0.1);
        for (var i = 0; i < 8; i++) {
            var angle = Math.PI * 2 * i / 8.0;
            var inner = new Point(center.X + Math.Cos(angle) * radius * 0.36, center.Y + Math.Sin(angle) * radius * 0.36);
            var outer = new Point(center.X + Math.Cos(angle) * radius * 0.5, center.Y + Math.Sin(angle) * radius * 0.5);
            drawingContext.DrawLine(pen, inner, outer);
        }
    }

    private static void DrawPower(DrawingContext drawingContext, Point center, double radius, Brush brush) {
        drawingContext.DrawGeometry(brush, null, CreatePolygon(center, radius, [
            new Point(0.02, -0.5),
            new Point(-0.3, 0.04),
            new Point(-0.04, 0.04),
            new Point(-0.16, 0.5),
            new Point(0.34, -0.14),
            new Point(0.08, -0.14)
        ]));
    }

    private static void DrawWater(DrawingContext drawingContext, Point center, double radius, Pen pen) {
        DrawWave(drawingContext, center, radius, pen, -0.18);
        DrawWave(drawingContext, center, radius, pen, 0.1);
        DrawWave(drawingContext, center, radius, pen, 0.38);
    }

    private static void DrawBarrier(
        DrawingContext drawingContext,
        Point center,
        double radius,
        Brush brush,
        Pen pen) {
        drawingContext.DrawRectangle(null, pen, new Rect(center.X - radius * 0.48, center.Y - radius * 0.2, radius * 0.96, radius * 0.4));
        drawingContext.DrawRectangle(brush, null, new Rect(center.X - radius * 0.32, center.Y - radius * 0.16, radius * 0.18, radius * 0.32));
        drawingContext.DrawRectangle(brush, null, new Rect(center.X + radius * 0.14, center.Y - radius * 0.16, radius * 0.18, radius * 0.32));
    }

    private static void DrawAir(DrawingContext drawingContext, Point center, double radius, Pen pen) {
        drawingContext.DrawLine(pen, new Point(center.X, center.Y - radius * 0.5), new Point(center.X, center.Y + radius * 0.48));
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.5, center.Y + radius * 0.02), new Point(center.X + radius * 0.5, center.Y + radius * 0.02));
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.24, center.Y + radius * 0.36), new Point(center.X + radius * 0.24, center.Y + radius * 0.36));
    }

    private static void DrawReligion(DrawingContext drawingContext, Point center, double radius, Pen pen) {
        drawingContext.DrawLine(pen, new Point(center.X, center.Y - radius * 0.48), new Point(center.X, center.Y + radius * 0.42));
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.32, center.Y - radius * 0.16), new Point(center.X + radius * 0.32, center.Y - radius * 0.16));
        drawingContext.DrawLine(pen, new Point(center.X - radius * 0.2, center.Y + radius * 0.42), new Point(center.X + radius * 0.2, center.Y + radius * 0.42));
    }

    private static void DrawIndustrial(
        DrawingContext drawingContext,
        Point center,
        double radius,
        Brush brush,
        Pen pen) {
        drawingContext.DrawGeometry(brush, pen, CreatePolygon(center, radius, [
            new Point(-0.48, 0.36),
            new Point(-0.48, -0.04),
            new Point(-0.2, -0.2),
            new Point(-0.2, -0.04),
            new Point(0.08, -0.2),
            new Point(0.08, -0.04),
            new Point(0.42, -0.04),
            new Point(0.42, 0.36)
        ]));
        drawingContext.DrawLine(pen, new Point(center.X + radius * 0.26, center.Y - radius * 0.04), new Point(center.X + radius * 0.26, center.Y - radius * 0.42));
    }

    private static void DrawHome(
        DrawingContext drawingContext,
        Point center,
        double radius,
        Brush brush,
        Pen pen) {
        drawingContext.DrawGeometry(brush, pen, CreatePolygon(center, radius, [
            new Point(-0.46, -0.02),
            new Point(0, -0.44),
            new Point(0.46, -0.02),
            new Point(0.34, -0.02),
            new Point(0.34, 0.42),
            new Point(-0.34, 0.42),
            new Point(-0.34, -0.02)
        ]));
    }

    private static void DrawWave(DrawingContext drawingContext, Point center, double radius, Pen pen, double yOffset) {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open()) {
            context.BeginFigure(new Point(center.X - radius * 0.46, center.Y + radius * yOffset), false, false);
            context.QuadraticBezierTo(
                new Point(center.X - radius * 0.23, center.Y + radius * (yOffset - 0.18)),
                new Point(center.X, center.Y + radius * yOffset),
                true,
                false);
            context.QuadraticBezierTo(
                new Point(center.X + radius * 0.23, center.Y + radius * (yOffset + 0.18)),
                new Point(center.X + radius * 0.46, center.Y + radius * yOffset),
                true,
                false);
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static StreamGeometry CreatePolygon(Point center, double radius, IReadOnlyList<Point> points) {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open()) {
            context.BeginFigure(new Point(center.X + points[0].X * radius, center.Y + points[0].Y * radius), true, true);
            for (var i = 1; i < points.Count; i++) {
                context.LineTo(new Point(center.X + points[i].X * radius, center.Y + points[i].Y * radius), true, false);
            }
        }
        geometry.Freeze();
        return geometry;
    }
}
