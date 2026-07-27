namespace WPF_OpenStreetmap_Editor.Models;

public sealed class MapFeatureSpatialIndex {
    private const double CellSizeDegrees = 0.05;
    private const int MaxIndexedCellsPerFeature = 128;
    private const int MaxQueryCells = 4096;

    private readonly MapFeature[] _features;
    private readonly Dictionary<long, List<MapFeature>> _cells;
    private readonly List<MapFeature> _largeFeatures;

    private MapFeatureSpatialIndex(
        MapFeature[] features,
        Dictionary<long, List<MapFeature>> cells,
        List<MapFeature> largeFeatures,
        int coordinateCount) {
        _features = features;
        _cells = cells;
        _largeFeatures = largeFeatures;
        CoordinateCount = coordinateCount;
    }

    public int FeatureCount => _features.Length;

    public int CoordinateCount { get; }

    public static MapFeatureSpatialIndex Build(IReadOnlyList<MapFeature> features) {
        var snapshot = features.ToArray();
        var cells = new Dictionary<long, List<MapFeature>>();
        var largeFeatures = new List<MapFeature>();
        var coordinateCount = 0;

        foreach (var feature in snapshot) {
            coordinateCount += feature.CoordinateCount;
            var bounds = feature.Bounds;
            if (!bounds.IsValid) {
                largeFeatures.Add(feature);
                continue;
            }

            var range = GetCellRange(bounds);
            var indexedCellCount = GetCellCount(range);
            if (indexedCellCount <= 0 || indexedCellCount > MaxIndexedCellsPerFeature) {
                largeFeatures.Add(feature);
                continue;
            }

            for (var y = range.MinY; y <= range.MaxY; y++) {
                for (var x = range.MinX; x <= range.MaxX; x++) {
                    var key = PackCell(x, y);
                    if (!cells.TryGetValue(key, out var bucket)) {
                        bucket = [];
                        cells[key] = bucket;
                    }
                    bucket.Add(feature);
                }
            }
        }

        return new MapFeatureSpatialIndex(snapshot, cells, largeFeatures, coordinateCount);
    }

    public IEnumerable<MapFeature> Query(GeoBounds viewport) {
        if (!viewport.IsValid) return _features;

        var range = GetCellRange(viewport);
        var queryCellCount = GetCellCount(range);
        if (queryCellCount <= 0 || queryCellCount > MaxQueryCells) {
            return _features;
        }

        return QueryCells(viewport, range);
    }

    private IEnumerable<MapFeature> QueryCells(GeoBounds viewport, CellRange range) {
        var seen = new HashSet<MapFeature>();
        foreach (var feature in _largeFeatures) {
            if (feature.Bounds.Intersects(viewport) && seen.Add(feature)) {
                yield return feature;
            }
        }

        for (var y = range.MinY; y <= range.MaxY; y++) {
            for (var x = range.MinX; x <= range.MaxX; x++) {
                if (!_cells.TryGetValue(PackCell(x, y), out var bucket)) continue;

                foreach (var feature in bucket) {
                    if (seen.Add(feature) && feature.Bounds.Intersects(viewport)) {
                        yield return feature;
                    }
                }
            }
        }
    }

    private static CellRange GetCellRange(GeoBounds bounds) {
        var minLongitude = Math.Clamp(bounds.MinLongitude, -180.0, 180.0);
        var maxLongitude = Math.Clamp(bounds.MaxLongitude, -180.0, 180.0);
        var minLatitude = Math.Clamp(bounds.MinLatitude, -90.0, 90.0);
        var maxLatitude = Math.Clamp(bounds.MaxLatitude, -90.0, 90.0);

        return new CellRange(
            ToCell(minLongitude),
            ToCell(minLatitude),
            ToCell(maxLongitude),
            ToCell(maxLatitude));
    }

    private static int ToCell(double value) {
        return (int)Math.Floor(value / CellSizeDegrees);
    }

    private static long GetCellCount(CellRange range) {
        return (long)(range.MaxX - range.MinX + 1) * (range.MaxY - range.MinY + 1);
    }

    private static long PackCell(int x, int y) {
        return ((long)x << 32) ^ (uint)y;
    }

    private readonly record struct CellRange(int MinX, int MinY, int MaxX, int MaxY);
}
