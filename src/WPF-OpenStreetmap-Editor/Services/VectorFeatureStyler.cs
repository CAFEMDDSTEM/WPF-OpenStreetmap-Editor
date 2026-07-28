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
    Fuel,
    Bank,
    Toilet,
    Safety,
    Post,
    Hotel,
    Shop,
    Tourism,
    Recreation,
    Nature,
    Culture,
    Office,
    Craft,
    Emergency,
    Utility,
    Power,
    Water,
    Barrier,
    Air,
    Religion,
    Industrial,
    Home
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
    TrackRoad,
    ServiceRoad,
    ResidentialRoad,
    LivingStreetRoad,
    UnclassifiedRoad,
    LocalRoad,
    TertiaryRoad,
    SecondaryRoad,
    PrimaryRoad,
    TrunkRoad,
    Motorway,
    GenericPoint,
    Poi,
    FoodPoint,
    ParkingPoint,
    MedicalPoint,
    EducationPoint,
    TransitPoint,
    FuelPoint,
    BankPoint,
    ToiletPoint,
    SafetyPoint,
    PostPoint,
    HotelPoint,
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
        VectorFeatureStyleKind.TrackRoad => 128,
        VectorFeatureStyleKind.ServiceRoad => 130,
        VectorFeatureStyleKind.ResidentialRoad => 132,
        VectorFeatureStyleKind.LivingStreetRoad => 134,
        VectorFeatureStyleKind.UnclassifiedRoad => 136,
        VectorFeatureStyleKind.LocalRoad => 138,
        VectorFeatureStyleKind.TertiaryRoad => 140,
        VectorFeatureStyleKind.SecondaryRoad => 145,
        VectorFeatureStyleKind.PrimaryRoad => 150,
        VectorFeatureStyleKind.TrunkRoad => 155,
        VectorFeatureStyleKind.Motorway => 160,
        VectorFeatureStyleKind.GenericPoint => 200,
        VectorFeatureStyleKind.Poi => 210,
        VectorFeatureStyleKind.FoodPoint => 211,
        VectorFeatureStyleKind.ParkingPoint => 211,
        VectorFeatureStyleKind.MedicalPoint => 211,
        VectorFeatureStyleKind.EducationPoint => 211,
        VectorFeatureStyleKind.TransitPoint => 211,
        VectorFeatureStyleKind.FuelPoint => 211,
        VectorFeatureStyleKind.BankPoint => 211,
        VectorFeatureStyleKind.ToiletPoint => 211,
        VectorFeatureStyleKind.SafetyPoint => 211,
        VectorFeatureStyleKind.PostPoint => 211,
        VectorFeatureStyleKind.HotelPoint => 211,
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
            return GetAmenityPointStyle(amenity);
        }

        if (TryGetAttribute(feature, "healthcare", out _)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.MedicalPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Medical);
        }

        if (TryGetAttribute(feature, "emergency", out var emergency)) {
            return new VectorFeatureStyle(
                IsMedicalEmergency(emergency) ? VectorFeatureStyleKind.MedicalPoint : VectorFeatureStyleKind.SafetyPoint,
                VectorFeatureRenderMode.Point,
                IsMedicalEmergency(emergency) ? VectorPointSymbolKind.Medical : VectorPointSymbolKind.Emergency);
        }

        if (TryGetAttribute(feature, "shop", out _)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.ShopPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Shop);
        }

        if (TryGetAttribute(feature, "tourism", out var tourism)) {
            if (IsHotelTourism(tourism)) {
                return new VectorFeatureStyle(VectorFeatureStyleKind.HotelPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Hotel);
            }
            if (IsCultureTourism(tourism)) {
                return new VectorFeatureStyle(VectorFeatureStyleKind.TourismPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Culture);
            }
            if (IsOutdoorTourism(tourism)) {
                return new VectorFeatureStyle(VectorFeatureStyleKind.TourismPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Nature);
            }

            return new VectorFeatureStyle(VectorFeatureStyleKind.TourismPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Tourism);
        }

        if (TryGetAttribute(feature, "highway", out var highway) && Normalize(highway) == "bus_stop") {
            return new VectorFeatureStyle(VectorFeatureStyleKind.TransitPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Transit);
        }
        if (TryGetAttribute(feature, "highway", out highway) && IsHighwayPoint(highway)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.Poi, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Barrier);
        }

        if (TryGetAttribute(feature, "public_transport", out var publicTransport) &&
            Normalize(publicTransport) is "station" or "platform" or "stop_position") {
            return new VectorFeatureStyle(VectorFeatureStyleKind.TransitPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Transit);
        }

        if (TryGetAttribute(feature, "railway", out var railway) &&
            Normalize(railway) is "station" or "halt" or "tram_stop") {
            return new VectorFeatureStyle(VectorFeatureStyleKind.TransitPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Transit);
        }

        if (TryGetAttribute(feature, "aeroway", out _)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.TransitPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Air);
        }

        if (TryGetAttribute(feature, "aerialway", out _)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.TransitPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Transit);
        }

        if (TryGetAttribute(feature, "leisure", out _)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.TourismPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Recreation);
        }

        if (HasAnyAttribute(feature, "sport", "playground", "golf", "attraction", "climbing")) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.TourismPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Recreation);
        }

        if (TryGetAttribute(feature, "historic", out _)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.TourismPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Culture);
        }

        if (TryGetAttribute(feature, "office", out var office)) {
            return new VectorFeatureStyle(
                IsGovernmentOffice(office) ? VectorFeatureStyleKind.BankPoint : VectorFeatureStyleKind.Poi,
                VectorFeatureRenderMode.Point,
                IsGovernmentOffice(office) ? VectorPointSymbolKind.Bank : VectorPointSymbolKind.Office);
        }

        if (TryGetAttribute(feature, "craft", out _)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.Poi, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Craft);
        }

        if (TryGetAttribute(feature, "natural", out var natural)) {
            return new VectorFeatureStyle(
                IsWaterNatural(natural) ? VectorFeatureStyleKind.TourismPoint : VectorFeatureStyleKind.Poi,
                VectorFeatureRenderMode.Point,
                IsWaterNatural(natural) ? VectorPointSymbolKind.Water : VectorPointSymbolKind.Nature);
        }

        if (TryGetAttribute(feature, "waterway", out _)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.TourismPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Water);
        }

        if (TryGetAttribute(feature, "power", out _)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.Poi, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Power);
        }

        if (TryGetAttribute(feature, "barrier", out _)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.Poi, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Barrier);
        }

        if (TryGetAttribute(feature, "man_made", out var manMade)) {
            return new VectorFeatureStyle(
                VectorFeatureStyleKind.Poi,
                VectorFeatureRenderMode.Point,
                IsIndustrialManMade(manMade) ? VectorPointSymbolKind.Industrial : VectorPointSymbolKind.Utility);
        }

        if (TryGetAttribute(feature, "building", out var building)) {
            return new VectorFeatureStyle(
                VectorFeatureStyleKind.Poi,
                VectorFeatureRenderMode.Point,
                IsHomeBuilding(building) ? VectorPointSymbolKind.Home : VectorPointSymbolKind.Industrial);
        }

        if (TryGetAttribute(feature, "military", out _)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.SafetyPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Safety);
        }

        if (HasAnyAttribute(feature, "seamark", "seamark:type")) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.TourismPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Water);
        }

        if (HasAnyAttribute(feature, "public_transport", "entrance", "advertising", "club")) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.Poi, VectorFeatureRenderMode.Point);
        }

        return new VectorFeatureStyle(VectorFeatureStyleKind.GenericPoint, VectorFeatureRenderMode.Point);
    }

    private static VectorFeatureStyle GetAmenityPointStyle(string amenity) {
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
        if (IsFuelAmenity(amenity)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.FuelPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Fuel);
        }
        if (IsBankAmenity(amenity)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.BankPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Bank);
        }
        if (IsToiletAmenity(amenity)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.ToiletPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Toilet);
        }
        if (IsSafetyAmenity(amenity)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.SafetyPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Safety);
        }
        if (IsPostAmenity(amenity)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.PostPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Post);
        }
        if (IsReligionAmenity(amenity)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.TourismPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Religion);
        }
        if (IsRecreationAmenity(amenity)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.TourismPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Recreation);
        }
        if (IsWasteAmenity(amenity)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.Poi, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Utility);
        }
        if (IsCommunityAmenity(amenity)) {
            return new VectorFeatureStyle(VectorFeatureStyleKind.TourismPoint, VectorFeatureRenderMode.Point, VectorPointSymbolKind.Culture);
        }

        return new VectorFeatureStyle(VectorFeatureStyleKind.Poi, VectorFeatureRenderMode.Point);
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
            "motorway" or "motorway_link" => VectorFeatureStyleKind.Motorway,
            "trunk" or "trunk_link" => VectorFeatureStyleKind.TrunkRoad,
            "primary" or "primary_link" => VectorFeatureStyleKind.PrimaryRoad,
            "secondary" or "secondary_link" => VectorFeatureStyleKind.SecondaryRoad,
            "tertiary" or "tertiary_link" => VectorFeatureStyleKind.TertiaryRoad,
            "unclassified" => VectorFeatureStyleKind.UnclassifiedRoad,
            "residential" => VectorFeatureStyleKind.ResidentialRoad,
            "living_street" => VectorFeatureStyleKind.LivingStreetRoad,
            "service" => VectorFeatureStyleKind.ServiceRoad,
            "track" => VectorFeatureStyleKind.TrackRoad,
            "footway" or "path" or "cycleway" or "bridleway" or "steps" or "pedestrian" => VectorFeatureStyleKind.Path,
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

    private static bool IsFuelAmenity(string value) {
        return Normalize(value) is "fuel" or "charging_station";
    }

    private static bool IsBankAmenity(string value) {
        return Normalize(value) is "bank" or "atm" or "bureau_de_change";
    }

    private static bool IsToiletAmenity(string value) {
        return Normalize(value) is "toilets" or "shower";
    }

    private static bool IsSafetyAmenity(string value) {
        return Normalize(value) is "police" or "fire_station" or "ranger_station";
    }

    private static bool IsPostAmenity(string value) {
        return Normalize(value) is "post_office" or "post_box";
    }

    private static bool IsReligionAmenity(string value) {
        return Normalize(value) is "place_of_worship" or "grave_yard" or "crematorium";
    }

    private static bool IsRecreationAmenity(string value) {
        return Normalize(value) is "arts_centre" or "cinema" or "theatre" or "casino" or "social_centre" or "community_centre" or "events_venue";
    }

    private static bool IsWasteAmenity(string value) {
        return Normalize(value) is "recycling" or "waste_basket" or "waste_disposal" or "waste_transfer_station" or "sanitary_dump_station";
    }

    private static bool IsCommunityAmenity(string value) {
        return Normalize(value) is "townhall" or "courthouse" or "embassy" or "public_bookcase" or "marketplace";
    }

    private static bool IsMedicalEmergency(string value) {
        return Normalize(value) is "ambulance_station" or "defibrillator" or "first_aid_kit";
    }

    private static bool IsHotelTourism(string value) {
        return Normalize(value) is "hotel" or "motel" or "hostel" or "guest_house" or "apartment";
    }

    private static bool IsCultureTourism(string value) {
        return Normalize(value) is "museum" or "gallery" or "artwork" or "information";
    }

    private static bool IsOutdoorTourism(string value) {
        return Normalize(value) is "camp_site" or "camp_pitch" or "picnic_site" or "viewpoint" or "wilderness_hut" or "alpine_hut";
    }

    private static bool IsHighwayPoint(string value) {
        return Normalize(value) is "crossing" or "traffic_signals" or "stop" or "give_way" or "turning_circle" or "mini_roundabout" or "milestone";
    }

    private static bool IsGovernmentOffice(string value) {
        return Normalize(value) is "government" or "administrative" or "diplomatic" or "embassy" or "consulate";
    }

    private static bool IsWaterNatural(string value) {
        return Normalize(value) is "water" or "bay" or "spring" or "hot_spring" or "geyser";
    }

    private static bool IsIndustrialManMade(string value) {
        return Normalize(value) is "works" or "factory" or "industrial" or "chimney" or "crane" or "kiln" or "silo" or "storage_tank" or "wastewater_plant" or "water_works";
    }

    private static bool IsHomeBuilding(string value) {
        return Normalize(value) is "house" or "detached" or "apartments" or "residential" or "bungalow" or "dormitory" or "hotel" or "cabin";
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
