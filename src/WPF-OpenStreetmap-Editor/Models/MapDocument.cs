namespace WPF_OpenStreetmap_Editor.Models;

public enum SpatialFileFormat {
    OsmXml,
    OsmPbf,
    Shapefile,
    GeoJson,
    Gml,
    Kml,
    Kmz,
    Gpx
}

public sealed class MapDocument {
    private Dictionary<string, MapFeature> _originalFeatures = new(StringComparer.Ordinal);
    private GeoBounds? _cachedBounds;
    private MapFeatureSpatialIndex? _spatialIndex;

    public string Name { get; set; } = "未命名地图";
    public string? SourcePath { get; set; }
    public SpatialFileFormat? SourceFormat { get; set; }
    public List<MapFeature> Features { get; } = [];
    public bool IsDirty { get; set; }
    public int SkippedFeatureCount { get; set; }

    public GeoBounds Bounds => _cachedBounds ??= GeoBounds.FromPoints(Features.SelectMany(static feature => feature.Points));
    public IReadOnlyDictionary<string, MapFeature> OriginalFeatures => _originalFeatures;

    public IEnumerable<MapFeature> QueryFeatures(GeoBounds viewport) {
        return (_spatialIndex ??= MapFeatureSpatialIndex.Build(Features)).Query(viewport);
    }

    public void InvalidateSpatialIndex() {
        _cachedBounds = null;
        _spatialIndex = null;
    }

    public void MarkClean() {
        _originalFeatures = Features.ToDictionary(
            static feature => feature.Id,
            static feature => feature.Clone(),
            StringComparer.Ordinal);
        foreach (var feature in Features) {
            feature.IsSelected = false;
        }
        IsDirty = false;
    }

    public IReadOnlyList<MapFeature> GetDeletedOriginalFeatures() {
        var currentIds = Features.Select(static feature => feature.Id).ToHashSet(StringComparer.Ordinal);
        return _originalFeatures.Values.Where(feature => !currentIds.Contains(feature.Id)).ToList();
    }
}
