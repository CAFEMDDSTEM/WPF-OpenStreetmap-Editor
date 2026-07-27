using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

internal enum VectorFeatureRenderMode {
    Area,
    Line,
    Point
}

internal enum VectorPointSymbolKind {
    Circle,
    Place,
    Food,
    Parking,
    Medical,
    Education,
    Transit,
    Shop,
    Tourism
}

internal enum VectorFeatureStyleKind {
    GenericArea,
    Water,
    Farmland,
    Forest,
    Park,
    BuiltArea,
    Building,
    GenericLine,
    Boundary,
    Waterway,
    Rail,
    Path,
    LocalRoad,
    SecondaryRoad,
    PrimaryRoad,
    Motorway,
    GenericPoint,
    Poi,
    FoodPoint,
    ParkingPoint,
    MedicalPoint,
    EducationPoint,
    TransitPoint,
    ShopPoint,
    TourismPoint,
    Place
}

internal readonly record struct VectorFeatureStyle(
    VectorFeatureStyleKind Kind,
    VectorFeatureRenderMode RenderMode,
    VectorPointSymbolKind SymbolKind = VectorPointSymbolKind.Circle) {
    public int LayerOrder => Kind switch {
        VectorFeatureStyleKind.GenericArea => 10,
        VectorFeatureStyleKind.Farmland => 20,
        VectorFeatureStyleKind.Forest => 25,
        VectorFeatureStyleKind.Park => 30,
        VectorFeatureStyleKind.BuiltArea => 35,
        VectorFeatureStyleKind.Water => 40,
        VectorFeatureStyleKind.Building => 50,
        VectorFeatureStyleKind.GenericLine => 100,
        VectorFeatureStyleKind.Boundary => 110,
        VectorFeatureStyleKind.Waterway => 115,
        VectorFeatureStyleKind.Rail => 120,
        VectorFeatureStyleKind.Path => 125,
        VectorFeatureStyleKind.LocalRoad => 130,
        VectorFeatureStyleKind.SecondaryRoad => 140,
        VectorFeatureStyleKind.PrimaryRoad => 150,
        VectorFeatureStyleKind.Motorway => 160,
        VectorFeatureStyleKind.GenericPoint => 200,
        VectorFeatureStyleKind.Poi => 210,
        VectorFeatureStyleKind.FoodPoint => 211,
        VectorFeatureStyleKind.ParkingPoint => 211,
        VectorFeatureStyleKind.MedicalPoint => 211,
        VectorFeatureStyleKind.EducationPoint => 211,
        VectorFeatureStyleKind.TransitPoint => 211,
        VectorFeatureStyleKind.ShopPoint => 211,
        VectorFeatureStyleKind.TourismPoint => 211,
        VectorFeatureStyleKind.Place => 220,
        _ => 100
    };
}

internal static class VectorFeatureStyler {
    public static VectorFeatureStyle GetStyle(MapFeature feature) {
        ArgumentNullException.ThrowIfNull(feature);

        if (feature.GeometryType == MapGeometryType.Point) {
            return GetPointStyle(feature);
        }

        if (feature.GeometryType == MapGeometryType.Polygon) {
            var areaStyle = GetAreaStyle(feature);
            if (areaStyle.HasValue) return areaStyle.Value;
        }

        if (TryGetAttribute(feature, "highway", out var highway)) {
            if (feature.GeometryType == MapGeometryType.Polygon && IsAreaExplicitlyYes(feature)) {
                return new VectorFeatureStyle(VectorFeatureStyleKind.BuiltArea, VectorFeatureRenderMode.Area);
            }

            return new VectorFeatureStyle(GetHighwayKind(highway), VectorFeatureRenderMode.Line);
        }

        if (TryGetAttribute(feature, "waterway", out _)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.Waterway, VectorFeatureRenderMode.Line);
        }

        if (TryGetAttribute(feature, "railway", out var railway) && IsActiveRailway(railway)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.Rail, VectorFeatureRenderMode.Line);
        }

        if (TryGetAttribute(feature, "boundary", out _)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.Boundary, VectorFeatureRenderMode.Line);
        }

        return feature.GeometryType == MapGeometryType.Polygon
            ? new VectorFeatureStyle(VectorFeatureStyleKind.GenericArea, VectorFeatureRenderMode.Area)
            : new VectorFeatureStyle(VectorFeatureStyleKind.GenericLine, VectorFeatureRenderMode.Line);
    }

    private static VectorFeatureStyle GetPointStyle(MapFeature feature) {
        if (TryGetAttribute(feature, "place", out _)) {
            return new VectorFeatureStyle(
                VectorFeatureStyleKind.Place,
                VectorFeatureRenderMode.Point,
                VectorPointSymbolKind.Place);
        }

        if (TryGetAttribute(feature, "amenity", out var amenity)) {
            if (IsFoodAmenity(amenity)) {
                return new VectorFeatureStyle(VectorFeatureStyleKind.FoodPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Food);
            }
            if (IsParkingAmenity(amenity)) {
                return new VectorFeatureStyle(VectorFeatureStyleKind.ParkingPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Parking);
            }
            if (IsMedicalAmenity(amenity)) {
                return new VectorFeatureStyle(VectorFeatureStyleKind.MedicalPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Medical);
            }
            if (IsEducationAmenity(amenity)) {
                return new VectorFeatureStyle(VectorFeatureStyleKind.EducationPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Education);
            }
            if (IsTransitAmenity(amenity)) {
                return new VectorFeatureStyle(VectorFeatureStyleKind.TransitPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Transit);
            }

            return new VectorFeatureStyle(VectorFeatureStyleKind.Poi, VectorFeatureRenderMode.Point);
        }

        if (TryGetAttribute(feature, "shop", out _)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.ShopPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Shop);
        }

        if (TryGetAttribute(feature, "tourism", out _)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.TourismPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Tourism);
        }

        if (TryGetAttribute(feature, "railway", out var railway) &&
            Normalize(railway) is "station" or "halt" or "tram_stop") {
            return new VectorFeatureStyle(VectorFeatureStyleKind.TransitPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Transit);
        }

        if (HasAnyAttribute(feature, "historic", "leisure", "office", "public_transport", "emergency")) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.Poi, VectorFeatureRenderMode.Point);
        }

        return new VectorFeatureStyle(VectorFeatureStyleKind.GenericPoint, VectorFeatureRenderMode.Point);
    }

    private static VectorFeatureStyle? GetAreaStyle(MapFeature feature) {
        if (IsWaterArea(feature)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.Water, VectorFeatureRenderMode.Area);
        }

        if (TryGetAttribute(feature, "building", out _)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.Building, VectorFeatureRenderMode.Area);
        }

        if (IsParkArea(feature)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.Park, VectorFeatureRenderMode.Area);
        }

        if (IsForestArea(feature)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.Forest, VectorFeatureRenderMode.Area);
        }

        if (IsFarmlandArea(feature)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.Farmland, VectorFeatureRenderMode.Area);
        }

        if (IsBuiltArea(feature)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.BuiltArea, VectorFeatureRenderMode.Area);
        }

        return null;
    }

    private static bool IsWaterArea(MapFeature feature) {
        return HasAttributeValue(feature, "natural", "water", "bay", "strait") ||
            HasAttributeValue(feature, "waterway", "riverbank", "dock") ||
            HasAttributeValue(feature, "landuse", "reservoir", "basin") ||
            HasAttributeValue(feature, "reservoir", "yes");
    }

    private static bool IsParkArea(MapFeature feature) {
        return HasAttributeValue(feature, "leisure", "park", "garden", "playground", "pitch", "recreation_ground", "nature_reserve") ||
            HasAttributeValue(feature, "landuse", "village_green", "grass", "recreation_ground") ||
            HasAttributeValue(feature, "boundary", "national_park");
    }

    private static bool IsForestArea(MapFeature feature) {
        return HasAttributeValue(feature, "natural", "wood", "forest", "scrub", "heath") ||
            HasAttributeValue(feature, "landuse", "forest") ||
            HasAttributeValue(feature, "leaf_type", "broadleaved", "needleleaved", "mixed");
    }

    private static bool IsFarmlandArea(MapFeature feature) {
        return HasAttributeValue(feature, "landuse", "farmland", "farmyard", "meadow", "orchard", "vineyard", "plant_nursery") ||
            HasAttributeValue(feature, "natural", "grassland");
    }

    private static bool IsBuiltArea(MapFeature feature) {
        return HasAttributeValue(feature, "landuse", "residential", "commercial", "industrial", "retail", "construction", "brownfield", "garages") ||
            HasAttributeValue(feature, "amenity", "parking", "school", "university", "hospital");
    }

    private static VectorFeatureStyleKind GetHighwayKind(string highway) {
        return Normalize(highway) switch {
            "motorway" or "motorway_link" or "trunk" or "trunk_link" => VectorFeatureStyleKind.Motorway,
            "primary" or "primary_link" => VectorFeatureStyleKind.PrimaryRoad,
            "secondary" or "secondary_link" or "tertiary" or "tertiary_link" => VectorFeatureStyleKind.SecondaryRoad,
            "footway" or "path" or "cycleway" or "bridleway" or "steps" or "track" or "pedestrian" => VectorFeatureStyleKind.Path,
            _ => VectorFeatureStyleKind.LocalRoad
        };
    }

    private static bool IsActiveRailway(string railway) {
        return Normalize(railway) is not ("abandoned" or "razed" or "dismantled" or "proposed" or "construction");
    }

    private static bool IsFoodAmenity(string value) {
        return Normalize(value) is "restaurant" or "cafe" or "fast_food" or "food_court" or "bar" or "pub" or "biergarten" or "ice_cream";
    }

    private static bool IsParkingAmenity(string value) {
        return Normalize(value) is "parking" or "parking_space" or "bicycle_parking" or "motorcycle_parking";
    }

    private static bool IsMedicalAmenity(string value) {
        return Normalize(value) is "hospital" or "clinic" or "doctors" or "dentist" or "pharmacy" or "veterinary";
    }

    private static bool IsEducationAmenity(string value) {
        return Normalize(value) is "school" or "university" or "college" or "kindergarten" or "library";
    }

    private static bool IsTransitAmenity(string value) {
        return Normalize(value) is "bus_station" or "ferry_terminal" or "taxi";
    }

    private static bool IsAreaExplicitlyYes(MapFeature feature) {
        return HasAttributeValue(feature, "area", "yes");
    }

    private static bool HasAnyAttribute(MapFeature feature, params string[] keys) {
        return keys.Any(key => TryGetAttribute(feature, key, out _));
    }

    private static bool HasAttributeValue(MapFeature feature, string key, params string[] expectedValues) {
        return TryGetAttribute(feature, key, out var value) &&
            expectedValues.Any(expected => string.Equals(Normalize(value), expected, StringComparison.Ordinal));
    }

    private static bool TryGetAttribute(MapFeature feature, string key, out string value) {
        if (feature.Attributes.TryGetValue(key, out value!)) return true;

        foreach (var attribute in feature.Attributes) {
            if (string.Equals(attribute.Key, key, StringComparison.OrdinalIgnoreCase)) {
                value = attribute.Value;
                return true;
            }
        }

        value = "";
        return false;
    }

    private static string Normalize(string value) {
        return value.Trim().ToLowerInvariant();
    }
}
