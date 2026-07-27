namespace WPF_OpenStreetmap_Editor.Models;

public sealed class MapFeatureSpatialIndex {
    private const double DefaultCellSizeDegrees = 0.05;
    private const double MinCellSizeDegrees = 0.0025;
    private const double MaxCellSizeDegrees = 1.0;
    private const int TargetFeaturesPerCell = 16;
    private const int MinTargetCellCount = 256;
    private const int MaxTargetCellCount = 65_536;
    private const int MaxIndexedCellsPerFeature = 512;
    private const int MaxQueryCells = 65_536;

    private readonly MapFeature[] _features;
    private readonly Dictionary<long, int[]> _cells;
    private readonly int[] _largeFeatureIndexes;
    private readonly int[] _seenStamps;
    private readonly double _cellSizeDegrees;
    private int _queryStamp;

    private MapFeatureSpatialIndex(
        MapFeature[] features,
        Dictionary<long, int[]> cells,
        int[] largeFeatureIndexes,
        double cellSizeDegrees,
        int coordinateCount) {
        _features = features;
        _cells = cells;
        _largeFeatureIndexes = largeFeatureIndexes;
        _cellSizeDegrees = cellSizeDegrees;
        _seenStamps = new int[features.Length];
        CoordinateCount = coordinateCount;
    }

    public int FeatureCount => _features.Length;

    public int CellCount => _cells.Count;

    public int CoordinateCount { get; }

    public double CellSizeDegrees => _cellSizeDegrees;

    public int LargeFeatureCount => _largeFeatureIndexes.Length;

    public static MapFeatureSpatialIndex Build(IReadOnlyList<MapFeature> features) {
        var snapshot = features.ToArray();
        var cellSize = ChooseCellSize(snapshot, out var coordinateCount);
        var cells = new Dictionary<long, List<int>>();
        var largeFeatureIndexes = new List<int>();
        var convertedCells = new Dictionary<long, int[]>();
        if (snapshot.Length == 0) {
            return new MapFeatureSpatialIndex(snapshot, convertedCells, [], cellSize, coordinateCount);
        }

        for (var featureIndex = 0; featureIndex < snapshot.Length; featureIndex++) {
            var feature = snapshot[featureIndex];
            var bounds = feature.Bounds;
            if (!bounds.IsValid) {
                largeFeatureIndexes.Add(featureIndex);
                continue;
            }

            var range = GetCellRange(bounds, cellSize);
            var indexedCellCount = GetCellCount(range);
            if (indexedCellCount <= 0 || indexedCellCount > MaxIndexedCellsPerFeature) {
                largeFeatureIndexes.Add(featureIndex);
                continue;
            }

            for (var y = range.MinY; y <= range.MaxY; y++) {
                for (var x = range.MinX; x <= range.MaxX; x++) {
                    var key = PackCell(x, y);
                    if (!cells.TryGetValue(key, out var bucket)) {
                        bucket = [];
                        cells[key] = bucket;
                    }
                    bucket.Add(featureIndex);
                }
            }
        }

        convertedCells = new Dictionary<long, int[]>(cells.Count);
        foreach (var (key, bucket) in cells) {
            convertedCells[key] = [.. bucket];
        }

        return new MapFeatureSpatialIndex(snapshot, convertedCells, [.. largeFeatureIndexes], cellSize, coordinateCount);
    }

    public IEnumerable<MapFeature> Query(GeoBounds viewport) {
        if (!viewport.IsValid) return _features;

        var range = GetCellRange(viewport, _cellSizeDegrees);
        var queryCellCount = GetCellCount(range);
        if (queryCellCount <= 0 || queryCellCount > MaxQueryCells) {
            return _features;
        }

        return QueryCells(viewport, range);
    }

    private static double ChooseCellSize(IReadOnlyList<MapFeature> features, out int coordinateCount) {
        coordinateCount = 0;
        var validFeatureCount = 0;
        var minLongitude = double.MaxValue;
        var minLatitude = double.MaxValue;
        var maxLongitude = double.MinValue;
        var maxLatitude = double.MinValue;

        foreach (var feature in features) {
            coordinateCount += feature.CoordinateCount;
            var bounds = feature.Bounds;
            if (!bounds.IsValid) {
                continue;
            }

            validFeatureCount++;
            minLongitude = Math.Min(minLongitude, bounds.MinLongitude);
            minLatitude = Math.Min(minLatitude, bounds.MinLatitude);
            maxLongitude = Math.Max(maxLongitude, bounds.MaxLongitude);
            maxLatitude = Math.Max(maxLatitude, bounds.MaxLatitude);
        }

        if (validFeatureCount == 0) {
            return DefaultCellSizeDegrees;
        }

        var longitudeSpan = Math.Max(maxLongitude - minLongitude, MinCellSizeDegrees);
        var latitudeSpan = Math.Max(maxLatitude - minLatitude, MinCellSizeDegrees);
        var targetCellCount = Math.Clamp(
            validFeatureCount / TargetFeaturesPerCell,
            MinTargetCellCount,
            MaxTargetCellCount);
        var cellSize = Math.Sqrt(longitudeSpan * latitudeSpan / targetCellCount);
        return Math.Clamp(cellSize, MinCellSizeDegrees, MaxCellSizeDegrees);
    }

    private IEnumerable<MapFeature> QueryCells(GeoBounds viewport, CellRange range) {
        var stamp = GetNextQueryStamp();
        foreach (var featureIndex in _largeFeatureIndexes) {
            var feature = _features[featureIndex];
            if (TryMarkSeen(featureIndex, stamp) && feature.Bounds.Intersects(viewport)) {
                yield return feature;
            }
        }

        for (var y = range.MinY; y <= range.MaxY; y++) {
            for (var x = range.MinX; x <= range.MaxX; x++) {
                if (!_cells.TryGetValue(PackCell(x, y), out var bucket)) continue;

                foreach (var featureIndex in bucket) {
                    var feature = _features[featureIndex];
                    if (TryMarkSeen(featureIndex, stamp) && feature.Bounds.Intersects(viewport)) {
                        yield return feature;
                    }
                }
            }
        }
    }

    private int GetNextQueryStamp() {
        var stamp = Interlocked.Increment(ref _queryStamp);
        if (stamp != int.MaxValue) return stamp;

        Array.Clear(_seenStamps);
        _queryStamp = 1;
        return 1;
    }

    private bool TryMarkSeen(int featureIndex, int stamp) {
        if (_seenStamps[featureIndex] == stamp) return false;

        _seenStamps[featureIndex] = stamp;
        return true;
    }

    private static CellRange GetCellRange(GeoBounds bounds, double cellSizeDegrees) {
        var minLongitude = Math.Clamp(bounds.MinLongitude, -180.0, 180.0);
        var maxLongitude = Math.Clamp(bounds.MaxLongitude, -180.0, 180.0);
        var minLatitude = Math.Clamp(bounds.MinLatitude, -90.0, 90.0);
        var maxLatitude = Math.Clamp(bounds.MaxLatitude, -90.0, 90.0);

        return new CellRange(
            ToCell(minLongitude, cellSizeDegrees),
            ToCell(minLatitude, cellSizeDegrees),
            ToCell(maxLongitude, cellSizeDegrees),
            ToCell(maxLatitude, cellSizeDegrees));
    }

    private static int ToCell(double value, double cellSizeDegrees) {
        return (int)Math.Floor(value / cellSizeDegrees);
    }

    private static long GetCellCount(CellRange range) {
        return (long)(range.MaxX - range.MinX + 1) * (range.MaxY - range.MinY + 1);
    }

    private static long PackCell(int x, int y) {
        return ((long)x << 32) ^ (uint)y;
    }

    private readonly record struct CellRange(int MinX, int MinY, int MaxX, int MaxY);
}
