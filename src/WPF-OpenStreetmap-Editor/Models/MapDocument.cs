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
    private OsmDataset? _originalOsm;
    private GeoBounds? _cachedBounds;
    private MapFeatureSpatialIndex? _spatialIndex;

    public string Name { get; set; } = "未命名地图";
    public string? SourcePath { get; set; }
    public SpatialFileFormat? SourceFormat { get; set; }
    public List<MapFeature> Features { get; } = [];
    public OsmDataset? Osm { get; set; }
    public bool IsDirty { get; set; }
    public int SkippedFeatureCount { get; set; }
    public long Revision { get; private set; }

    public GeoBounds Bounds => _cachedBounds ??= GeoBounds.FromPoints(Features.SelectMany(static feature => feature.Points));
    public IReadOnlyDictionary<string, MapFeature> OriginalFeatures => _originalFeatures;
    public OsmDataset? OriginalOsm => _originalOsm;

    public IEnumerable<MapFeature> QueryFeatures(GeoBounds viewport) {
        return (_spatialIndex ??= MapFeatureSpatialIndex.Build(Features)).Query(viewport);
    }

    public void InvalidateSpatialIndex() {
        _cachedBounds = null;
        _spatialIndex = null;
    }

    public void MarkContentChanged() {
        Revision++;
    }

    public void MarkClean(bool updateOsmHistory = true, bool compactOsmHistory = false) {
        if (updateOsmHistory) CaptureOsmHistory(compactOsmHistory);
        ClearSelection();
        IsDirty = false;
    }

    public void MarkSaved() {
        ClearSelection();
        IsDirty = false;
    }

    public IReadOnlyList<MapFeature> GetDeletedOriginalFeatures() {
        var currentIds = Features.Select(static feature => feature.Id).ToHashSet(StringComparer.Ordinal);
        return _originalFeatures.Values.Where(feature => !currentIds.Contains(feature.Id)).ToList();
    }

    public void ClearOsmHistory() {
        foreach (var feature in _originalFeatures.Values) {
            feature.Osm = null;
        }
        _originalOsm = null;
    }

    internal MapDocument CreateOsmProjection() {
        var projection = new MapDocument {
            Name = Name,
            SourcePath = SourcePath,
            SourceFormat = SourceFormat,
            Osm = Osm?.Clone(),
            IsDirty = IsDirty,
            SkippedFeatureCount = SkippedFeatureCount
        };
        projection.Features.AddRange(Features.Select(static feature => feature.Clone()));
        projection._originalFeatures = _originalFeatures.ToDictionary(
            static item => item.Key,
            static item => item.Value.Clone(),
            StringComparer.Ordinal);
        projection._originalOsm = _originalOsm?.Clone();
        return projection;
    }

    private void CaptureOsmHistory(bool compactOsmHistory) {
        if (compactOsmHistory && Osm is not null) {
            _originalFeatures = Features
                .Where(static feature => feature.Osm is not null)
                .ToDictionary(
                    static feature => feature.Id,
                    static feature => new MapFeature {
                        Id = feature.Id,
                        GeometryType = feature.GeometryType,
                        Osm = feature.Osm?.Clone()
                    },
                    StringComparer.Ordinal);
            _originalOsm = Osm;
            return;
        }

        _originalFeatures = Features.ToDictionary(
            static feature => feature.Id,
            static feature => feature.Clone(),
            StringComparer.Ordinal);
        _originalOsm = Osm?.Clone();
    }

    private void ClearSelection() {
        foreach (var feature in Features) {
            feature.IsSelected = false;
        }
    }
}
