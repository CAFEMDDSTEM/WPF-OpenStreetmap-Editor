using System.Globalization;

namespace WPF_OpenStreetmap_Editor.Services;

internal static class SettingsFieldParser {
    public static bool TryParseZoom(string text, out int zoom) {
        if (int.TryParse(text.Trim(), out zoom)) {
            zoom = Math.Clamp(zoom, GeoConverter.MinZoom, GeoConverter.MaxZoom);
            return true;
        }

        zoom = GeoConverter.MaxZoom;
        return false;
    }

    public static bool TryParseDouble(string text, out double value) {
        return double.TryParse(
                text.Trim(),
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out value) ||
            double.TryParse(
                text.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
    }

    public static bool TryParseInteger(string text, out int value) {
        return int.TryParse(
                text.Trim(),
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out value) ||
            int.TryParse(
                text.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
    }

    public static bool TryParseIntegerInRange(string text, int min, int max, out int value) {
        if (!TryParseInteger(text, out value) || value < min || value > max) {
            value = min;
            return false;
        }

        return true;
    }

    public static List<string> ParseSignatures(string text) {
        return text
            .Split(["\r\n", "\n", "\r", ",", ";"], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }
}
