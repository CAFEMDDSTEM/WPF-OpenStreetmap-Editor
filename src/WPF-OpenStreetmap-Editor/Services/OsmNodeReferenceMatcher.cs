using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

internal readonly record struct OsmNodeReferenceMatch(OsmNodeReference Reference, int OriginalIndex);

internal static class OsmNodeReferenceMatcher {
    public static IReadOnlyList<OsmNodeReference?> Match(
        IReadOnlyList<GeoPoint> points,
        IReadOnlyList<OsmNodeReference>? originalNodes) {
        return MatchWithIndexes(points, originalNodes)
            .Select(static match => match?.Reference)
            .ToArray();
    }

    public static IReadOnlyList<OsmNodeReferenceMatch?> MatchEditedWay(
        IReadOnlyList<GeoPoint> points,
        IReadOnlyList<OsmNodeReference>? originalNodes) {
        if (originalNodes is not null && points.Count == originalNodes.Count) {
            return TryMatchReorderedPoints(points, originalNodes) ??
                originalNodes
                    .Select((reference, index) => (OsmNodeReferenceMatch?)new OsmNodeReferenceMatch(reference, index))
                    .ToArray();
        }

        return MatchWithIndexes(points, originalNodes);
    }

    public static IReadOnlyList<OsmNodeReferenceMatch?> MatchWithIndexes(
        IReadOnlyList<GeoPoint> points,
        IReadOnlyList<OsmNodeReference>? originalNodes) {
        var matches = new OsmNodeReferenceMatch?[points.Count];
        if (originalNodes is null || originalNodes.Count == 0 || points.Count == 0) return matches;

        var indexesByPoint = new Dictionary<GeoPoint, Queue<int>>();
        for (var i = 0; i < originalNodes.Count; i++) {
            var point = originalNodes[i].Point;
            if (!indexesByPoint.TryGetValue(point, out var indexes)) {
                indexes = new Queue<int>();
                indexesByPoint[point] = indexes;
            }

            indexes.Enqueue(i);
        }

        var minimumOriginalIndex = 0;
        for (var i = 0; i < points.Count; i++) {
            if (!indexesByPoint.TryGetValue(points[i], out var indexes)) continue;

            while (indexes.Count > 0 && indexes.Peek() < minimumOriginalIndex) {
                indexes.Dequeue();
            }

            if (indexes.Count == 0) continue;

            var originalIndex = indexes.Dequeue();
            minimumOriginalIndex = originalIndex + 1;
            matches[i] = new OsmNodeReferenceMatch(originalNodes[originalIndex], originalIndex);
        }

        return matches;
    }

    private static IReadOnlyList<OsmNodeReferenceMatch?>? TryMatchReorderedPoints(
        IReadOnlyList<GeoPoint> points,
        IReadOnlyList<OsmNodeReference> originalNodes) {
        if (points.Count == 0) return [];
        if (IsSameOrder(points, originalNodes)) return null;

        var indexesByPoint = new Dictionary<GeoPoint, Queue<int>>();
        for (var i = 0; i < originalNodes.Count; i++) {
            var point = originalNodes[i].Point;
            if (!indexesByPoint.TryGetValue(point, out var indexes)) {
                indexes = new Queue<int>();
                indexesByPoint[point] = indexes;
            }

            indexes.Enqueue(i);
        }

        var matches = new OsmNodeReferenceMatch?[points.Count];
        for (var i = 0; i < points.Count; i++) {
            if (!indexesByPoint.TryGetValue(points[i], out var indexes) || indexes.Count == 0) {
                return null;
            }

            var originalIndex = indexes.Dequeue();
            matches[i] = new OsmNodeReferenceMatch(originalNodes[originalIndex], originalIndex);
        }

        return indexesByPoint.Values.All(static indexes => indexes.Count == 0)
            ? matches
            : null;
    }

    private static bool IsSameOrder(IReadOnlyList<GeoPoint> points, IReadOnlyList<OsmNodeReference> originalNodes) {
        for (var i = 0; i < points.Count; i++) {
            if (points[i] != originalNodes[i].Point) return false;
        }

        return true;
    }
}
