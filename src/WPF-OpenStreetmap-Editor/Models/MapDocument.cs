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
    private Dictionary<string, MapFeature> _originalFeatures = new(StringComparer.Ordinal);
    private OsmDataset? _originalOsm;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled map";
    public string? SourcePath { get; set; }
    public SpatialFileFormat? SourceFormat { get; set; }
    public List<MapFeature> Features { get; } = [];
    public OsmDataset? Osm { get; set; }
    public bool IsVisible { get; set; } = true;
    public double Opacity { get; set; } = 1.0;
    public bool IsDirty { get; set; }

    public GeoBounds Bounds => _cachedBounds ??= GeoBounds.FromPoints(Features.SelectMany(static feature => feature.Points));
    public IReadOnlyDictionary<string, MapFeature> OriginalFeatures => _originalFeatures;
    public OsmDataset? OriginalOsm => _originalOsm;

    public IEnumerable<MapFeature> QueryFeatures(GeoBounds viewport) {
        if (!IsVisible || Opacity <= 0) return [];

        return (_spatialIndex ??= MapFeatureSpatialIndex.Build(Features)).Query(viewport);
    }

    public void InvalidateSpatialIndex() {
        _cachedBounds = null;
        _spatialIndex = null;
    }

    public MapDataLayer Clone() {
        var clonedOsm = Osm?.Clone();
        var clone = new MapDataLayer {
            Id = Id,
            Name = Name,
            SourcePath = SourcePath,
            SourceFormat = SourceFormat,
            Osm = clonedOsm,
            IsVisible = IsVisible,
            Opacity = Opacity,
            IsDirty = IsDirty
        };
        clone.Features.AddRange(Features.Select(static feature => feature.Clone()));
        clone._originalFeatures = _originalFeatures.ToDictionary(
            static item => item.Key,
            static item => item.Value.Clone(),
            StringComparer.Ordinal);
        clone._originalOsm = ReferenceEquals(_originalOsm, Osm)
            ? clonedOsm
            : _originalOsm?.Clone();
        return clone;
    }

    internal IReadOnlyList<MapFeature> GetDeletedOriginalFeatures() {
        var currentIds = Features
            .Select(static feature => feature.Id)
            .ToHashSet(StringComparer.Ordinal);
        return _originalFeatures.Values.Where(feature => !currentIds.Contains(feature.Id)).ToList();
    }

    internal void ClearOsmHistory() {
        foreach (var feature in _originalFeatures.Values) {
            feature.Osm = null;
        }
        _originalOsm = null;
    }

    internal void CaptureOsmHistory(bool compactOsmHistory) {
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
}

public sealed class MapDocument {
    private GeoBounds? _cachedBounds;
    private string _name = "Untitled map";
    private string? _sourcePath;
    private SpatialFileFormat? _sourceFormat;
    private MapDataLayer? _activeDataLayer;

    public MapDocument() {
        DataLayers.Add(new MapDataLayer());
    }

    public string Name {
        get => DataLayers.Count == 0 ? _name : EnsureDataLayer().Name;
        set {
            _name = value;
            EnsureDataLayer().Name = value;
        }
    }

    public string? SourcePath {
        get => DataLayers.Count == 0 ? _sourcePath : EnsureDataLayer().SourcePath;
        set {
            _sourcePath = value;
            EnsureDataLayer().SourcePath = value;
        }
    }

    public SpatialFileFormat? SourceFormat {
        get => DataLayers.Count == 0 ? _sourceFormat : EnsureDataLayer().SourceFormat;
        set {
            _sourceFormat = value;
            EnsureDataLayer().SourceFormat = value;
        }
    }

    public List<MapDataLayer> DataLayers { get; } = [];
    public MapDataLayer ActiveDataLayer {
        get => EnsureDataLayer();
        set {
            ArgumentNullException.ThrowIfNull(value);
            if (!DataLayers.Contains(value)) {
                throw new ArgumentException("The active data layer must belong to this document.", nameof(value));
            }

            _activeDataLayer = value;
            SynchronizeMetadata(value);
        }
    }
    public List<MapFeature> Features => ActiveDataLayer.Features;
    public OsmDataset? Osm {
        get => ActiveDataLayer.Osm;
        set => ActiveDataLayer.Osm = value;
    }
    public bool IsDirty {
        get => DataLayers.Any(static layer => layer.IsDirty);
        set => ActiveDataLayer.IsDirty = value;
    }
    public int SkippedFeatureCount { get; set; }
    public long Revision { get; private set; }

    public GeoBounds Bounds => _cachedBounds ??= GeoBounds.FromPoints(
        DataLayers.SelectMany(static layer => layer.Features).SelectMany(static feature => feature.Points));
    public IReadOnlyDictionary<string, MapFeature> OriginalFeatures => ActiveDataLayer.OriginalFeatures;
    public OsmDataset? OriginalOsm => ActiveDataLayer.OriginalOsm;

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
        if (updateOsmHistory) ActiveDataLayer.CaptureOsmHistory(compactOsmHistory);
        ClearSelection();
        ActiveDataLayer.IsDirty = false;
    }

    public void MarkSaved() {
        ClearSelection();
        ActiveDataLayer.IsDirty = false;
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
        return ActiveDataLayer.GetDeletedOriginalFeatures();
    }

    public void ClearOsmHistory() {
        ActiveDataLayer.ClearOsmHistory();
    }

    internal MapDocument CreateSnapshot() {
        return CreateSnapshot(ActiveDataLayer);
    }

    internal MapDocument CreateSnapshot(MapDataLayer activeDataLayer) {
        var activeLayerIndex = DataLayers.IndexOf(activeDataLayer);
        if (activeLayerIndex < 0) {
            throw new ArgumentException("The snapshot layer must belong to this document.", nameof(activeDataLayer));
        }

        var projection = new MapDocument {
            Name = activeDataLayer.Name,
            SourcePath = activeDataLayer.SourcePath,
            SourceFormat = activeDataLayer.SourceFormat,
            SkippedFeatureCount = SkippedFeatureCount
        };
        projection.DataLayers.Clear();
        projection.DataLayers.AddRange(DataLayers.Select(static layer => layer.Clone()));
        projection._activeDataLayer = projection.DataLayers[activeLayerIndex];
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

        if (_activeDataLayer is null || !DataLayers.Contains(_activeDataLayer)) {
            _activeDataLayer = DataLayers[0];
        }

        SynchronizeMetadata(_activeDataLayer);
        return _activeDataLayer;
    }

    private void SynchronizeMetadata(MapDataLayer layer) {
        _name = layer.Name;
        _sourcePath = layer.SourcePath;
        _sourceFormat = layer.SourceFormat;
    }

    private void ClearSelection() {
        foreach (var feature in DataLayers.SelectMany(static layer => layer.Features)) {
            feature.IsSelected = false;
        }
    }
}
