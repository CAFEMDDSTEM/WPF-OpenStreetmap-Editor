using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public static class VertexEditService {
    /// <summary>
    /// Inserts <paramref name="point"/> between vertex <paramref name="segmentStartIndex"/> and the next vertex of the part.
    /// Returns a deep copy; an invalid point or invalid indexes return an unchanged copy.
    /// </summary>
    public static List<List<GeoPoint>> InsertVertex(
        IEnumerable<IEnumerable<GeoPoint>> parts,
        int partIndex,
        int segmentStartIndex,
        GeoPoint point) {
        ArgumentNullException.ThrowIfNull(parts);

        var result = CopyParts(parts);
        if (!point.IsValid ||
            partIndex < 0 ||
            partIndex >= result.Count ||
            segmentStartIndex < 0 ||
            segmentStartIndex >= result[partIndex].Count - 1) {
            return result;
        }

        result[partIndex].Insert(segmentStartIndex + 1, point);
        return result;
    }

    /// <summary>
    /// Removes one vertex and returns a deep copy. For a closed ring, removing index 0 or the closing index removes
    /// both copies and re-closes the ring with the new first vertex.
    /// Returns null when nothing can be removed: invalid indexes, an open part that would keep fewer than 2 vertices,
    /// or a ring that would keep fewer than 3 vertices excluding the closing point.
    /// </summary>
    public static List<List<GeoPoint>>? RemoveVertex(
        IEnumerable<IEnumerable<GeoPoint>> parts,
        int partIndex,
        int pointIndex) {
        ArgumentNullException.ThrowIfNull(parts);

        var result = CopyParts(parts);
        if (partIndex < 0 || partIndex >= result.Count) return null;

        var part = result[partIndex];
        if (pointIndex < 0 || pointIndex >= part.Count) return null;

        if (IsClosedRing(part)) {
            // A ring needs 3 vertices plus the closing point after removal.
            if (part.Count < 5) return null;

            if (pointIndex == 0 || pointIndex == part.Count - 1) {
                part.RemoveAt(part.Count - 1);
                part.RemoveAt(0);
                part.Add(part[0]);
            } else {
                part.RemoveAt(pointIndex);
            }
            return result;
        }

        if (part.Count < 3) return null;

        part.RemoveAt(pointIndex);
        return result;
    }

    /// <summary>
    /// Appends the first vertex to close the part and returns a deep copy.
    /// Parts that are already closed or have fewer than 3 vertices are returned unchanged.
    /// </summary>
    public static List<List<GeoPoint>> CloseRing(IEnumerable<IEnumerable<GeoPoint>> parts, int partIndex) {
        ArgumentNullException.ThrowIfNull(parts);

        var result = CopyParts(parts);
        if (partIndex < 0 || partIndex >= result.Count) return result;

        var part = result[partIndex];
        if (part.Count < 3 || IsClosedRing(part)) return result;

        part.Add(part[0]);
        return result;
    }

    public static bool IsClosedRing(IReadOnlyList<GeoPoint> part) {
        ArgumentNullException.ThrowIfNull(part);

        return part.Count > 2 && part[0] == part[^1];
    }

    private static List<List<GeoPoint>> CopyParts(IEnumerable<IEnumerable<GeoPoint>> parts) {
        return parts.Select(static part => part.ToList()).ToList();
    }
}
