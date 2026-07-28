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

public sealed class MapDataLayer {
    private GeoBounds? _cachedBounds;
    private MapFeatureSpatialIndex? _spatialIndex;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled map";
    public string? SourcePath { get; set; }
    public SpatialFileFormat? SourceFormat { get; set; }
    public List<MapFeature> Features { get; } = [];
    public OsmDataset? Osm { get; set; }
    public bool IsVisible { get; set; } = true;
    public double Opacity { get; set; } = 1.0;

    public GeoBounds Bounds => _cachedBounds ??= GeoBounds.FromPoints(Features.SelectMany(static feature => feature.Points));

    public IEnumerable<MapFeature> QueryFeatures(GeoBounds viewport) {
        if (!IsVisible || Opacity <= 0) return [];

        return (_spatialIndex ??= MapFeatureSpatialIndex.Build(Features)).Query(viewport);
    }

    public void InvalidateSpatialIndex() {
        _cachedBounds = null;
        _spatialIndex = null;
    }

    public MapDataLayer Clone() {
        var clone = new MapDataLayer {
            Id = Id,
            Name = Name,
            SourcePath = SourcePath,
            SourceFormat = SourceFormat,
            Osm = Osm?.Clone(),
            IsVisible = IsVisible,
            Opacity = Opacity
        };
        clone.Features.AddRange(Features.Select(static feature => feature.Clone()));
        return clone;
    }
}

public sealed class MapDocument {
    private Dictionary<string, MapFeature> _originalFeatures = new(StringComparer.Ordinal);
    private OsmDataset? _originalOsm;
    private GeoBounds? _cachedBounds;
    private string _name = "Untitled map";
    private string? _sourcePath;
    private SpatialFileFormat? _sourceFormat;

    public MapDocument() {
        DataLayers.Add(new MapDataLayer());
    }

    public string Name {
        get => _name;
        set {
            _name = value;
            if (DataLayers.Count == 1) DataLayers[0].Name = value;
        }
    }

    public string? SourcePath {
        get => _sourcePath;
        set {
            _sourcePath = value;
            if (DataLayers.Count == 1) DataLayers[0].SourcePath = value;
        }
    }

    public SpatialFileFormat? SourceFormat {
        get => _sourceFormat;
        set {
            _sourceFormat = value;
            if (DataLayers.Count == 1) DataLayers[0].SourceFormat = value;
        }
    }

    public List<MapDataLayer> DataLayers { get; } = [];
    public MapDataLayer ActiveDataLayer => EnsureDataLayer();
    public List<MapFeature> Features => ActiveDataLayer.Features;
    public OsmDataset? Osm {
        get => ActiveDataLayer.Osm;
        set => ActiveDataLayer.Osm = value;
    }
    public bool IsDirty { get; set; }
    public int SkippedFeatureCount { get; set; }
    public long Revision { get; private set; }

    public GeoBounds Bounds => _cachedBounds ??= GeoBounds.FromPoints(
        DataLayers.SelectMany(static layer => layer.Features).SelectMany(static feature => feature.Points));
    public IReadOnlyDictionary<string, MapFeature> OriginalFeatures => _originalFeatures;
    public OsmDataset? OriginalOsm => _originalOsm;

    public IEnumerable<MapFeature> QueryFeatures(GeoBounds viewport) {
        return DataLayers.SelectMany(layer => layer.QueryFeatures(viewport));
    }

    public void InvalidateSpatialIndex() {
        _cachedBounds = null;
        foreach (var layer in DataLayers) layer.InvalidateSpatialIndex();
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

    public MapDataLayer AddDataLayer(MapDataLayer layer) {
        ArgumentNullException.ThrowIfNull(layer);

        DataLayers.Add(layer);
        InvalidateSpatialIndex();
        MarkContentChanged();
        return layer;
    }

    public MapDataLayer? FindDataLayer(MapFeature feature) {
        ArgumentNullException.ThrowIfNull(feature);

        return DataLayers.FirstOrDefault(layer => layer.Features.Contains(feature));
    }

    public IReadOnlyList<MapFeature> GetDeletedOriginalFeatures() {
        var currentIds = DataLayers
            .SelectMany(static layer => layer.Features)
            .Select(static feature => feature.Id)
            .ToHashSet(StringComparer.Ordinal);
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
        projection.DataLayers.Clear();
        projection.DataLayers.AddRange(DataLayers.Select(static layer => layer.Clone()));
        projection._originalFeatures = _originalFeatures.ToDictionary(
            static item => item.Key,
            static item => item.Value.Clone(),
            StringComparer.Ordinal);
        projection._originalOsm = _originalOsm?.Clone();
        return projection;
    }

    private MapDataLayer EnsureDataLayer() {
        if (DataLayers.Count == 0) {
            DataLayers.Add(new MapDataLayer {
                Name = Name,
                SourcePath = SourcePath,
                SourceFormat = SourceFormat
            });
        }

        return DataLayers[0];
    }

    private void CaptureOsmHistory(bool compactOsmHistory) {
        var features = DataLayers.SelectMany(static layer => layer.Features);
        if (compactOsmHistory && Osm is not null) {
            _originalFeatures = features
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

        _originalFeatures = features.ToDictionary(
            static feature => feature.Id,
            static feature => feature.Clone(),
            StringComparer.Ordinal);
        _originalOsm = Osm?.Clone();
    }

    private void ClearSelection() {
        foreach (var feature in DataLayers.SelectMany(static layer => layer.Features)) {
            feature.IsSelected = false;
        }
    }
}
