namespace WPF_OpenStreetmap_Editor.Models;

public enum MapGeometryType {
    Point,
    LineString,
    Polygon
}

public enum OsmPrimitiveType {
    Node,
    Way
}

public sealed class OsmFeatureMetadata {
    public OsmPrimitiveType PrimitiveType { get; set; }
    public long Id { get; set; }
    public int Version { get; set; }
    public List<OsmNodeReference> NodeReferences { get; set; } = [];
}

public sealed record OsmNodeReference(long Id, int Version, GeoPoint Point);

public sealed class MapFeature {
    private GeoBounds? _cachedBounds;
    private int? _cachedCoordinateCount;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public MapGeometryType GeometryType { get; init; }
    public List<List<GeoPoint>> Parts { get; init; } = [];
    public Dictionary<string, string> Attributes { get; init; } = new(StringComparer.Ordinal);
    public OsmFeatureMetadata? Osm { get; set; }
    public bool IsHidden { get; set; }
    public bool IsSelected { get; set; }

    public IEnumerable<GeoPoint> Points => Parts.SelectMany(static part => part);
    public GeoBounds Bounds => _cachedBounds ??= GeoBounds.FromPoints(Points);
    public int CoordinateCount => _cachedCoordinateCount ??= Parts.Sum(static part => part.Count);

    public void InvalidateGeometry() {
        _cachedBounds = null;
        _cachedCoordinateCount = null;
    }

    public MapFeature Clone() {
        return new MapFeature {
            Id = Id,
            GeometryType = GeometryType,
            Parts = Parts.Select(static part => part.ToList()).ToList(),
            Attributes = new Dictionary<string, string>(Attributes, StringComparer.Ordinal),
            Osm = Osm is null ? null : new OsmFeatureMetadata {
                PrimitiveType = Osm.PrimitiveType,
                Id = Osm.Id,
                Version = Osm.Version,
                NodeReferences = Osm.NodeReferences.ToList()
            },
            IsHidden = IsHidden,
            IsSelected = IsSelected
        };
    }
}
