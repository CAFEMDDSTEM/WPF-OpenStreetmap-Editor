namespace WPF_OpenStreetmap_Editor.Services;

public static class PresetIconCatalog {
    public static string? Resolve(string? icon, string? name) {
        var hint = $"{icon} {name}".ToLowerInvariant();
        if (ContainsAny(hint, "motorway", "trunk", "primary", "secondary", "tertiary", "unclassified")) return "Route";
        if (ContainsAny(hint, "cycleway", "bicycle", "bike")) return "Bike";
        if (ContainsAny(hint, "footway", "footpath", "path", "steps", "pedestrian", "bridleway", "trail", "hiking")) return "Footprints";
        if (ContainsAny(hint, "building", "house", "residential", "apartment")) return "Building2";
        if (ContainsAny(hint, "addr", "address", "postal", "housenumber")) return "Mail";
        if (ContainsAny(hint, "place", "village", "town", "city", "locality", "hamlet", "suburb")) return "MapPin";
        if (ContainsAny(hint, "water", "river", "lake", "stream", "harbour", "wetland")) return "Waves";
        if (ContainsAny(hint, "forest", "wood", "tree", "natural", "park")) return "TreePine";
        if (ContainsAny(hint, "shop", "store", "supermarket", "market", "mall")) return "ShoppingBag";
        if (ContainsAny(hint, "restaurant", "cafe", "food", "fast_food", "bar", "pub")) return "Utensils";
        if (ContainsAny(hint, "school", "kindergarten", "university", "college", "library")) return "GraduationCap";
        if (ContainsAny(hint, "parking", "garage")) return "SquareParking";
        if (ContainsAny(hint, "railway", "station", "train", "tram", "subway", "aerialway")) return "TrainFront";
        if (ContainsAny(hint, "amenity", "facility")) return "CircleDot";
        if (ContainsAny(hint, "landuse", "land use", "field", "farm", "meadow")) return "Sprout";
        return name is not null && name.Length > 0 ? "Tags" : null;
    }

    private static bool ContainsAny(string hint, params string[] keywords) {
        return keywords.Any(keyword => hint.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
