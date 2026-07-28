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
    [InlineData("trunk", nameof(VectorFeatureStyleKind.TrunkRoad))]
    [InlineData("primary", nameof(VectorFeatureStyleKind.PrimaryRoad))]
    [InlineData("secondary", nameof(VectorFeatureStyleKind.SecondaryRoad))]
    [InlineData("tertiary", nameof(VectorFeatureStyleKind.TertiaryRoad))]
    [InlineData("unclassified", nameof(VectorFeatureStyleKind.UnclassifiedRoad))]
    [InlineData("residential", nameof(VectorFeatureStyleKind.ResidentialRoad))]
    [InlineData("living_street", nameof(VectorFeatureStyleKind.LivingStreetRoad))]
    [InlineData("service", nameof(VectorFeatureStyleKind.ServiceRoad))]
    [InlineData("track", nameof(VectorFeatureStyleKind.TrackRoad))]
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
    [InlineData("tourism", "museum", nameof(VectorFeatureStyleKind.TourismPoint), nameof(VectorPointSymbolKind.Culture))]
    [InlineData("tourism", "hotel", nameof(VectorFeatureStyleKind.HotelPoint), nameof(VectorPointSymbolKind.Hotel))]
    [InlineData("amenity", "fuel", nameof(VectorFeatureStyleKind.FuelPoint), nameof(VectorPointSymbolKind.Fuel))]
    [InlineData("amenity", "bank", nameof(VectorFeatureStyleKind.BankPoint), nameof(VectorPointSymbolKind.Bank))]
    [InlineData("amenity", "toilets", nameof(VectorFeatureStyleKind.ToiletPoint), nameof(VectorPointSymbolKind.Toilet))]
    [InlineData("amenity", "police", nameof(VectorFeatureStyleKind.SafetyPoint), nameof(VectorPointSymbolKind.Safety))]
    [InlineData("amenity", "post_office", nameof(VectorFeatureStyleKind.PostPoint), nameof(VectorPointSymbolKind.Post))]
    [InlineData("place", "city", nameof(VectorFeatureStyleKind.Place), nameof(VectorPointSymbolKind.Place))]
    [InlineData("healthcare", "dentist", nameof(VectorFeatureStyleKind.MedicalPoint), nameof(VectorPointSymbolKind.Medical))]
    [InlineData("emergency", "fire_hydrant", nameof(VectorFeatureStyleKind.SafetyPoint), nameof(VectorPointSymbolKind.Emergency))]
    [InlineData("amenity", "place_of_worship", nameof(VectorFeatureStyleKind.TourismPoint), nameof(VectorPointSymbolKind.Religion))]
    [InlineData("amenity", "recycling", nameof(VectorFeatureStyleKind.Poi), nameof(VectorPointSymbolKind.Utility))]
    [InlineData("leisure", "playground", nameof(VectorFeatureStyleKind.TourismPoint), nameof(VectorPointSymbolKind.Recreation))]
    [InlineData("sport", "soccer", nameof(VectorFeatureStyleKind.TourismPoint), nameof(VectorPointSymbolKind.Recreation))]
    [InlineData("historic", "castle", nameof(VectorFeatureStyleKind.TourismPoint), nameof(VectorPointSymbolKind.Culture))]
    [InlineData("office", "company", nameof(VectorFeatureStyleKind.Poi), nameof(VectorPointSymbolKind.Office))]
    [InlineData("craft", "carpenter", nameof(VectorFeatureStyleKind.Poi), nameof(VectorPointSymbolKind.Craft))]
    [InlineData("natural", "peak", nameof(VectorFeatureStyleKind.Poi), nameof(VectorPointSymbolKind.Nature))]
    [InlineData("natural", "water", nameof(VectorFeatureStyleKind.TourismPoint), nameof(VectorPointSymbolKind.Water))]
    [InlineData("waterway", "waterfall", nameof(VectorFeatureStyleKind.TourismPoint), nameof(VectorPointSymbolKind.Water))]
    [InlineData("power", "tower", nameof(VectorFeatureStyleKind.Poi), nameof(VectorPointSymbolKind.Power))]
    [InlineData("barrier", "gate", nameof(VectorFeatureStyleKind.Poi), nameof(VectorPointSymbolKind.Barrier))]
    [InlineData("aeroway", "aerodrome", nameof(VectorFeatureStyleKind.TransitPoint), nameof(VectorPointSymbolKind.Air))]
    [InlineData("man_made", "tower", nameof(VectorFeatureStyleKind.Poi), nameof(VectorPointSymbolKind.Utility))]
    [InlineData("building", "house", nameof(VectorFeatureStyleKind.Poi), nameof(VectorPointSymbolKind.Home))]
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

    [Fact]
    public void GetStyle_KeepsResidentialAndServiceRoadsSeparate() {
        var residential = VectorFeatureStyler.GetStyle(Feature(MapGeometryType.LineString, "highway", "residential"));
        var service = VectorFeatureStyler.GetStyle(Feature(MapGeometryType.LineString, "highway", "service"));

        Assert.Equal(VectorFeatureStyleKind.ResidentialRoad, residential.Kind);
        Assert.Equal(VectorFeatureStyleKind.ServiceRoad, service.Kind);
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
