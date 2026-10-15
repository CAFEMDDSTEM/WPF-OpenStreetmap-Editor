using System.Globalization;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

internal static class TopologyValidationSupport {
    private const double EarthRadiusMeters = 6_371_008.8;
    private const double MetersPerDegree = EarthRadiusMeters * Math.PI / 180.0;

    public static string FormatIdList(IReadOnlyCollection<long> ids, int maximum) {
        var listed = string.Join(", ", ids.Take(maximum).Select(static id => id.ToString(CultureInfo.InvariantCulture)));
        return ids.Count > maximum ? listed + ", …" : listed;
    }

    // Equirectangular projection around the mean latitude: accurate to far below a metre at the
    // few-metre scale the topology rules work with, and much cheaper than a geodesic distance.
    public static double DistanceToSegmentMeters(GeoPoint point, GeoPoint start, GeoPoint end) {
        var cosine = Math.Cos((point.Latitude + start.Latitude + end.Latitude) / 3.0 * Math.PI / 180.0);
        var px = (point.Longitude - start.Longitude) * MetersPerDegree * cosine;
        var py = (point.Latitude - start.Latitude) * MetersPerDegree;
        var ex = (end.Longitude - start.Longitude) * MetersPerDegree * cosine;
        var ey = (end.Latitude - start.Latitude) * MetersPerDegree;
        var lengthSquared = ex * ex + ey * ey;
        var scale = lengthSquared == 0 ? 0.0 : Math.Clamp((px * ex + py * ey) / lengthSquared, 0.0, 1.0);
        var dx = px - scale * ex;
        var dy = py - scale * ey;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static GeoBounds BoundsAround(GeoPoint point, double meters) {
        var latitudeDelta = meters / MetersPerDegree;
        // Clamping the cosine keeps the longitude span finite next to the poles.
        var longitudeDelta = latitudeDelta / Math.Max(Math.Cos(point.Latitude * Math.PI / 180.0), 0.01);
        return new GeoBounds(
            point.Longitude - longitudeDelta,
            point.Latitude - latitudeDelta,
            point.Longitude + longitudeDelta,
            point.Latitude + latitudeDelta);
    }

    // A proper crossing needs each segment's endpoints strictly on opposite sides of the other segment.
    // Segments that only touch are deliberately excluded: a shared node is a connection, and an end node
    // lying on another way without sharing a node is the end-node rule's finding, not a crossing.
    public static bool SegmentsCross(GeoPoint a, GeoPoint b, GeoPoint c, GeoPoint d) {
        return ValidationRuleSupport.OppositeSigns(ValidationRuleSupport.Cross(a, b, c), ValidationRuleSupport.Cross(a, b, d)) &&
            ValidationRuleSupport.OppositeSigns(ValidationRuleSupport.Cross(c, d, a), ValidationRuleSupport.Cross(c, d, b));
    }
}

internal readonly record struct WaySegment(long WayId, GeoPoint Start, GeoPoint End) {
    public GeoBounds Bounds => new(
        Math.Min(Start.Longitude, End.Longitude),
        Math.Min(Start.Latitude, End.Latitude),
        Math.Max(Start.Longitude, End.Longitude),
        Math.Max(Start.Latitude, End.Latitude));
}

// Uniform grid hash over way segments. A segment is registered in every cell its bounding box touches,
// so two segments with overlapping boxes always share a cell and a query never misses a candidate.
// Covering the box costs more cells than walking the line for long diagonal segments, but highway
// segments are short enough that the simpler cover wins.
internal sealed class SegmentGrid {
    // About 55 m of latitude per cell: a handful of segments per cell even in dense urban data.
    public const double CellSizeDegrees = 0.0005;

    private readonly List<WaySegment> _segments = [];
    private readonly Dictionary<(int Column, int Row), List<int>> _cells = [];

    public IReadOnlyList<WaySegment> Segments => _segments;

    public void Add(WaySegment segment) {
        var index = _segments.Count;
        _segments.Add(segment);
        foreach (var cell in CellsCovering(segment.Bounds)) {
            if (!_cells.TryGetValue(cell, out var indices)) {
                indices = [];
                _cells[cell] = indices;
            }
            indices.Add(index);
        }
    }

    // Indices of every segment whose bounding box may intersect the query bounds, each reported once.
    public IEnumerable<int> Query(GeoBounds bounds) {
        var seen = new HashSet<int>();
        foreach (var cell in CellsCovering(bounds)) {
            if (!_cells.TryGetValue(cell, out var indices)) continue;
            foreach (var index in indices) {
                if (seen.Add(index)) yield return index;
            }
        }
    }

    private static IEnumerable<(int Column, int Row)> CellsCovering(GeoBounds bounds) {
        var minColumn = CellIndex(bounds.MinLongitude);
        var maxColumn = CellIndex(bounds.MaxLongitude);
        var minRow = CellIndex(bounds.MinLatitude);
        var maxRow = CellIndex(bounds.MaxLatitude);
        for (var column = minColumn; column <= maxColumn; column++) {
            for (var row = minRow; row <= maxRow; row++) {
                yield return (column, row);
            }
        }
    }

    private static int CellIndex(double degrees) => (int)Math.Floor(degrees / CellSizeDegrees);
}
