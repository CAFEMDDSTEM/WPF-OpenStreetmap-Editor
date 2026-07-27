using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class VectorFeatureStylerTests {
    [Theory]
    [InlineData("natural", "water", nameof(VectorFeatureStyleKind.Water))]
    [InlineData("building", "yes", nameof(VectorFeatureStyleKind.Building))]
    [InlineData("landuse", "forest", nameof(VectorFeatureStyleKind.Forest))]
    [InlineData("leisure", "park", nameof(VectorFeatureStyleKind.Park))]
    [InlineData("landuse", "farmland", nameof(VectorFeatureStyleKind.Farmland))]
    [InlineData("landuse", "residential", nameof(VectorFeatureStyleKind.BuiltArea))]
    public void GetStyle_ClassifiesOsmAreas(string key, string value, string expectedKindName) {
        var feature = Feature(MapGeometryType.Polygon, key, value);

        var style = VectorFeatureStyler.GetStyle(feature);

        Assert.Equal(Enum.Parse<VectorFeatureStyleKind>(expectedKindName), style.Kind);
        Assert.Equal(VectorFeatureRenderMode.Area, style.RenderMode);
    }

    [Theory]
    [InlineData("motorway", nameof(VectorFeatureStyleKind.Motorway))]
    [InlineData("primary", nameof(VectorFeatureStyleKind.PrimaryRoad))]
    [InlineData("secondary", nameof(VectorFeatureStyleKind.SecondaryRoad))]
    [InlineData("residential", nameof(VectorFeatureStyleKind.LocalRoad))]
    [InlineData("footway", nameof(VectorFeatureStyleKind.Path))]
    public void GetStyle_ClassifiesOsmHighways(string highway, string expectedKindName) {
        var feature = Feature(MapGeometryType.LineString, "highway", highway);

        var style = VectorFeatureStyler.GetStyle(feature);

        Assert.Equal(Enum.Parse<VectorFeatureStyleKind>(expectedKindName), style.Kind);
        Assert.Equal(VectorFeatureRenderMode.Line, style.RenderMode);
    }

    [Theory]
    [InlineData("amenity", "restaurant", nameof(VectorFeatureStyleKind.FoodPoint), nameof(VectorPointSymbolKind.Food))]
    [InlineData("amenity", "parking", nameof(VectorFeatureStyleKind.ParkingPoint), nameof(VectorPointSymbolKind.Parking))]
    [InlineData("amenity", "hospital", nameof(VectorFeatureStyleKind.MedicalPoint), nameof(VectorPointSymbolKind.Medical))]
    [InlineData("amenity", "school", nameof(VectorFeatureStyleKind.EducationPoint), nameof(VectorPointSymbolKind.Education))]
    [InlineData("amenity", "bus_station", nameof(VectorFeatureStyleKind.TransitPoint), nameof(VectorPointSymbolKind.Transit))]
    [InlineData("shop", "supermarket", nameof(VectorFeatureStyleKind.ShopPoint), nameof(VectorPointSymbolKind.Shop))]
    [InlineData("tourism", "museum", nameof(VectorFeatureStyleKind.TourismPoint), nameof(VectorPointSymbolKind.Tourism))]
    [InlineData("place", "city", nameof(VectorFeatureStyleKind.Place), nameof(VectorPointSymbolKind.Place))]
    public void GetStyle_ClassifiesOsmPointSymbols(
        string key,
        string value,
        string expectedKindName,
        string expectedSymbolName) {
        var feature = Feature(MapGeometryType.Point, key, value);

        var style = VectorFeatureStyler.GetStyle(feature);

        Assert.Equal(Enum.Parse<VectorFeatureStyleKind>(expectedKindName), style.Kind);
        Assert.Equal(VectorFeatureRenderMode.Point, style.RenderMode);
        Assert.Equal(Enum.Parse<VectorPointSymbolKind>(expectedSymbolName), style.SymbolKind);
    }

    private static MapFeature Feature(MapGeometryType geometryType, string key, string value) {
        return new MapFeature {
            GeometryType = geometryType,
            Parts = [[new GeoPoint(0, 0), new GeoPoint(1, 0), new GeoPoint(1, 1), new GeoPoint(0, 0)]],
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal) {
                [key] = value
            }
        };
    }
}
