using System.Globalization;

namespace WPF_OpenStreetmap_Editor.Services;

internal enum EditKeyboardCommandKind {
    DrawLine,
    Rotate,
    Move,
    Extrude
}

internal enum EditKeyboardExtrudeMode {
    Free,
    X,
    Y,
    Segment,
    InnerSquare
}

internal readonly record struct EditKeyboardCommand(
    EditKeyboardCommandKind Kind,
    double? RotationDegrees,
    double MoveEastDecimeters,
    double MoveNorthDecimeters,
    bool HasMoveDistance,
    EditKeyboardExtrudeMode ExtrudeMode = EditKeyboardExtrudeMode.Free,
    double ExtrudeDistanceDecimeters = 0,
    bool HasExtrudeDistance = false);

internal static class EditKeyboardCommandParser {
    public static bool TryParse(string commandText, out EditKeyboardCommand command) {
        command = default;
        var text = new string(commandText
            .Where(static character => !char.IsWhiteSpace(character))
            .Select(static character => char.ToLowerInvariant(character))
            .ToArray());
        if (text.Length == 0) return false;

        return text[0] switch {
            'a' => TryParseDrawLine(text, out command),
            'r' => TryParseRotate(text, out command),
            'm' => TryParseMove(text, out command),
            'e' => TryParseExtrude(text, out command),
            _ => false
        };
    }

    private static bool TryParseDrawLine(string text, out EditKeyboardCommand command) {
        command = new EditKeyboardCommand(EditKeyboardCommandKind.DrawLine, null, 0, 0, false);
        return text.Length == 1;
    }

    private static bool TryParseRotate(string text, out EditKeyboardCommand command) {
        command = default;
        if (text.Length == 1) {
            command = new EditKeyboardCommand(EditKeyboardCommandKind.Rotate, null, 0, 0, false);
            return true;
        }

        if (!double.TryParse(text[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees)) return false;

        command = new EditKeyboardCommand(EditKeyboardCommandKind.Rotate, degrees, 0, 0, false);
        return true;
    }

    private static bool TryParseMove(string text, out EditKeyboardCommand command) {
        command = default;
        if (text.Length == 1) {
            command = new EditKeyboardCommand(EditKeyboardCommandKind.Move, null, 0, 0, false);
            return true;
        }

        var eastDecimeters = 0.0;
        var northDecimeters = 0.0;
        var hasDistance = false;
        var index = 1;
        while (index < text.Length) {
            var axis = text[index];
            if (axis is not ('x' or 'y')) return false;
            index++;

            if (!TryReadNumber(text, ref index, out var value)) return false;
            if (index + 1 < text.Length && text[index] == 'd' && text[index + 1] == 'm') index += 2;

            if (axis == 'x') eastDecimeters += value;
            else northDecimeters += value;
            hasDistance = true;
        }

        command = new EditKeyboardCommand(
            EditKeyboardCommandKind.Move,
            null,
            eastDecimeters,
            northDecimeters,
            hasDistance);
        return true;
    }

    private static bool TryParseExtrude(string text, out EditKeyboardCommand command) {
        command = default;
        if (text.Length == 1) {
            command = new EditKeyboardCommand(EditKeyboardCommandKind.Extrude, null, 0, 0, false);
            return true;
        }

        var mode = text[1] switch {
            'x' => EditKeyboardExtrudeMode.X,
            'y' => EditKeyboardExtrudeMode.Y,
            's' => EditKeyboardExtrudeMode.Segment,
            _ => (EditKeyboardExtrudeMode?)null
        };
        if (mode is null) return false;

        var index = 2;
        if (mode.Value == EditKeyboardExtrudeMode.Segment &&
            index < text.Length &&
            text[index] == 's') {
            mode = EditKeyboardExtrudeMode.InnerSquare;
            index++;
        }

        if (index == text.Length) {
            command = new EditKeyboardCommand(
                EditKeyboardCommandKind.Extrude,
                null,
                0,
                0,
                false,
                mode.Value);
            return true;
        }

        if (!TryReadNumber(text, ref index, out var distanceDecimeters) || index != text.Length) return false;

        command = new EditKeyboardCommand(
            EditKeyboardCommandKind.Extrude,
            null,
            0,
            0,
            false,
            mode.Value,
            distanceDecimeters,
            HasExtrudeDistance: true);
        return true;
    }

    private static bool TryReadNumber(string text, ref int index, out double value) {
        value = 0;
        var start = index;
        if (index < text.Length && text[index] is '+' or '-') index++;

        var hasDigit = false;
        while (index < text.Length && char.IsDigit(text[index])) {
            hasDigit = true;
            index++;
        }

        if (index < text.Length && text[index] == '.') {
            index++;
            while (index < text.Length && char.IsDigit(text[index])) {
                hasDigit = true;
                index++;
            }
        }

        return hasDigit &&
            double.TryParse(text[start..index], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
