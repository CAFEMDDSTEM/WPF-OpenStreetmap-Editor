using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class SimilarFeatureSelectorTests {
    [Fact]
    public void GetPrimaryTag_PrefersHighwayOverName() {
        var tags = Tags(("name", "Main Street"), ("highway", "residential"));

        var primary = SimilarFeatureSelector.GetPrimaryTag(tags);

        Assert.Equal(new KeyValuePair<string, string>("highway", "residential"), primary);
    }

    [Fact]
    public void GetPrimaryTag_FollowsPriorityOrder() {
        var tags = Tags(("amenity", "school"), ("building", "yes"));

        var primary = SimilarFeatureSelector.GetPrimaryTag(tags);

        Assert.Equal(new KeyValuePair<string, string>("building", "yes"), primary);
    }

    [Fact]
    public void GetPrimaryTag_ReturnsNullWithoutPrimaryKey() {
        Assert.Null(SimilarFeatureSelector.GetPrimaryTag(Tags(("name", "x"), ("ref", "1"))));
        Assert.Null(SimilarFeatureSelector.GetPrimaryTag(Tags()));
    }

    [Fact]
    public void AreSimilar_MatchesSamePrimaryTagIgnoringOtherTags() {
        var first = CreateLine([new GeoPoint(0, 0), new GeoPoint(1, 0)], ("highway", "residential"), ("name", "A"));
        var second = CreateLine([new GeoPoint(5, 5), new GeoPoint(6, 5)], ("highway", "residential"), ("name", "B"));

        Assert.True(SimilarFeatureSelector.AreSimilar(first, second));
    }

    [Fact]
    public void AreSimilar_RejectsDifferentPrimaryValueOrKey() {
        var residential = CreateLine([new GeoPoint(0, 0), new GeoPoint(1, 0)], ("highway", "residential"));
        var footway = CreateLine([new GeoPoint(0, 0), new GeoPoint(1, 0)], ("highway", "footway"));
        var rail = CreateLine([new GeoPoint(0, 0), new GeoPoint(1, 0)], ("railway", "rail"));

        Assert.False(SimilarFeatureSelector.AreSimilar(residential, footway));
        Assert.False(SimilarFeatureSelector.AreSimilar(residential, rail));
    }

    [Fact]
    public void AreSimilar_TreatsUntaggedLinesAsSimilarButNotUntaggedAndNamed() {
        var untagged = CreateLine([new GeoPoint(0, 0), new GeoPoint(1, 0)]);
        var alsoUntagged = CreateLine([new GeoPoint(2, 0), new GeoPoint(3, 0)]);
        var named = CreateLine([new GeoPoint(4, 0), new GeoPoint(5, 0)], ("name", "x"));

        Assert.True(SimilarFeatureSelector.AreSimilar(untagged, alsoUntagged));
        Assert.False(SimilarFeatureSelector.AreSimilar(untagged, named));
    }

    [Fact]
    public void AreSimilar_RequiresSameGeometryType() {
        var line = CreateLine([new GeoPoint(0, 0), new GeoPoint(1, 0)], ("highway", "pedestrian"));
        var area = CreateFeature(
            MapGeometryType.Polygon,
            [new GeoPoint(0, 0), new GeoPoint(1, 0), new GeoPoint(1, 1), new GeoPoint(0, 0)],
            ("highway", "pedestrian"));

        Assert.False(SimilarFeatureSelector.AreSimilar(line, area));
    }

    [Fact]
    public void SelectSimilar_ContiguousFollowsSharedVerticesAndStopsAtDissimilarBridge() {
        var (a, b, c, footway, d) = CreateResidentialChain();

        var selected = SimilarFeatureSelector.SelectSimilar(a, [a, b, c, footway, d], contiguous: true);

        Assert.Equal([a, b, c], selected);
    }

    [Fact]
    public void SelectSimilar_NonContiguousReturnsAllSimilarRegardlessOfConnection() {
        var (a, b, c, footway, d) = CreateResidentialChain();

        var selected = SimilarFeatureSelector.SelectSimilar(c, [a, b, c, footway, d], contiguous: false);

        Assert.Equal([c, a, b, d], selected);
    }

    [Fact]
    public void SelectSimilar_ExcludesHiddenFeaturesAndDoesNotBridgeThroughThem() {
        var (a, b, c, footway, d) = CreateResidentialChain();
        b.IsHidden = true;

        var contiguous = SimilarFeatureSelector.SelectSimilar(a, [a, b, c, footway, d], contiguous: true);
        var all = SimilarFeatureSelector.SelectSimilar(a, [a, b, c, footway, d], contiguous: false);

        Assert.Equal([a], contiguous);
        Assert.Equal([a, c, d], all);
    }

    [Fact]
    public void SelectSimilar_ContiguousConnectsThroughSharedOsmNodeWhenCoordinatesDrifted() {
        var tracked = CreateWay([new GeoPoint(0, 0), new GeoPoint(1, 0)], [1, 2], ("highway", "residential"));
        var drifted = CreateWay([new GeoPoint(1.001, 0), new GeoPoint(2, 0)], [2, 3], ("highway", "residential"));
        var detached = CreateWay([new GeoPoint(5, 5), new GeoPoint(6, 5)], [7, 8], ("highway", "residential"));

        var selected = SimilarFeatureSelector.SelectSimilar(tracked, [tracked, drifted, detached], contiguous: true);

        Assert.Equal([tracked, drifted], selected);
    }

    [Fact]
    public void SelectSimilar_PointSeedContiguousReturnsOnlySeed() {
        var seed = CreateFeature(MapGeometryType.Point, [new GeoPoint(0, 0)], ("amenity", "bench"));
        var coincident = CreateFeature(MapGeometryType.Point, [new GeoPoint(0, 0)], ("amenity", "bench"));
        var elsewhere = CreateFeature(MapGeometryType.Point, [new GeoPoint(1, 1)], ("amenity", "bench"));

        var contiguous = SimilarFeatureSelector.SelectSimilar(seed, [seed, coincident, elsewhere], contiguous: true);
        var all = SimilarFeatureSelector.SelectSimilar(seed, [seed, coincident, elsewhere], contiguous: false);

        Assert.Equal([seed], contiguous);
        Assert.Equal([seed, coincident, elsewhere], all);
    }

    /// <summary>a-b-c share end vertices, a footway continues from c, and residential d hangs off the footway.</summary>
    private static (MapFeature A, MapFeature B, MapFeature C, MapFeature Footway, MapFeature D) CreateResidentialChain() {
        var p0 = new GeoPoint(0, 0);
        var p1 = new GeoPoint(1, 0);
        var p2 = new GeoPoint(2, 0);
        var p3 = new GeoPoint(3, 0);
        var p4 = new GeoPoint(4, 0);
        var p5 = new GeoPoint(5, 0);
        return (
            CreateLine([p0, p1], ("highway", "residential")),
            CreateLine([p1, p2], ("highway", "residential")),
            CreateLine([p2, p3], ("highway", "residential")),
            CreateLine([p3, p4], ("highway", "footway")),
            CreateLine([p4, p5], ("highway", "residential")));
    }

    private static Dictionary<string, string> Tags(params (string Key, string Value)[] tags) {
        return tags.ToDictionary(static tag => tag.Key, static tag => tag.Value, StringComparer.Ordinal);
    }

    private static MapFeature CreateLine(List<GeoPoint> points, params (string Key, string Value)[] tags) {
        return CreateFeature(MapGeometryType.LineString, points, tags);
    }

    private static MapFeature CreateFeature(MapGeometryType geometryType, List<GeoPoint> points, params (string Key, string Value)[] tags) {
        var feature = new MapFeature {
            GeometryType = geometryType,
            Parts = [points]
        };
        foreach (var (key, value) in tags) {
            feature.Attributes[key] = value;
        }
        return feature;
    }

    private static MapFeature CreateWay(List<GeoPoint> points, long[] nodeIds, params (string Key, string Value)[] tags) {
        var feature = CreateLine(points, tags);
        feature.Osm = new OsmFeatureMetadata {
            PrimitiveType = OsmPrimitiveType.Way,
            Id = 100,
            Version = 1,
            NodeReferences = points.Select((point, index) => new OsmNodeReference(nodeIds[index], 1, point)).ToList()
        };
        return feature;
    }
}
