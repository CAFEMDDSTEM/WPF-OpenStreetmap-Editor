using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class FeatureSearchServiceTests {
    [Fact]
    public void Filter_BlankQueryReturnsAllFeaturesInOrder() {
        var first = CreateFeature("first");
        var second = CreateFeature("second");

        var results = FeatureSearchService.Filter([first, second], "  ");

        Assert.Equal([first, second], results);
    }

    [Fact]
    public void Filter_MatchesIdGeometryAndAttributesCaseInsensitively() {
        var cafe = CreateFeature("local-cafe", MapGeometryType.Point, new Dictionary<string, string> {
            ["amenity"] = "Cafe",
            ["name"] = "Central Perk"
        });
        var park = CreateFeature("park", MapGeometryType.Polygon, new Dictionary<string, string> {
            ["leisure"] = "park"
        });

        Assert.Equal([cafe], FeatureSearchService.Filter([cafe, park], "CENTRAL"));
        Assert.Equal([park], FeatureSearchService.Filter([cafe, park], "polygon"));
        Assert.Equal([cafe], FeatureSearchService.Filter([cafe, park], "local-cafe"));
    }

    [Fact]
    public void Filter_KeyValueTermRequiresMatchingAttributePair() {
        var cafe = CreateFeature("cafe", attributes: new Dictionary<string, string> {
            ["amenity"] = "cafe",
            ["name"] = "Library Cafe"
        });
        var library = CreateFeature("library", attributes: new Dictionary<string, string> {
            ["amenity"] = "library",
            ["name"] = "Cafe Road"
        });

        var results = FeatureSearchService.Filter([cafe, library], "amenity=cafe");

        Assert.Equal([cafe], results);
    }

    [Fact]
    public void Filter_AllTermsMustMatchSameFeature() {
        var namedRoad = CreateFeature("road", MapGeometryType.LineString, new Dictionary<string, string> {
            ["highway"] = "residential",
            ["name"] = "Orchard Road"
        });
        var unnamedRoad = CreateFeature("unnamed", MapGeometryType.LineString, new Dictionary<string, string> {
            ["highway"] = "service"
        });

        var results = FeatureSearchService.Filter([namedRoad, unnamedRoad], "line orchard");

        Assert.Equal([namedRoad], results);
    }

    [Fact]
    public void Filter_MatchesOsmPrimitiveAndVisibility() {
        var hiddenWay = CreateFeature("hidden-way");
        hiddenWay.IsHidden = true;
        hiddenWay.Osm = new OsmFeatureMetadata {
            PrimitiveType = OsmPrimitiveType.Way,
            Id = 42,
            Version = 3
        };
        var visibleNode = CreateFeature("visible-node");

        Assert.Equal([hiddenWay], FeatureSearchService.Filter([hiddenWay, visibleNode], "way/42 hidden"));
        Assert.Equal([visibleNode], FeatureSearchService.Filter([hiddenWay, visibleNode], "visible"));
    }

    private static MapFeature CreateFeature(
        string id,
        MapGeometryType geometryType = MapGeometryType.Point,
        Dictionary<string, string>? attributes = null) {
        return new MapFeature {
            Id = id,
            GeometryType = geometryType,
            Parts = [[new GeoPoint(0, 0)]],
            Attributes = attributes ?? []
        };
    }
}
