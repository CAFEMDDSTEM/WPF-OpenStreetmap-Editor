using System.Collections.ObjectModel;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public static class TagPresetCatalog {
    private static readonly IReadOnlyList<TagPresetChoice> AccessChoices = Choices(
        ("yes", "Allowed"), ("no", "Prohibited"), ("private", "Private"), ("permissive", "Permissive"));
    private static readonly IReadOnlyList<TagPresetChoice> SurfaceChoices = Choices(
        ("asphalt", "Asphalt"), ("concrete", "Concrete"), ("paved", "Paved"),
        ("gravel", "Gravel"), ("dirt", "Dirt"), ("ground", "Ground"));
    private static readonly IReadOnlyList<TagPreset> Presets = Array.AsReadOnly(new[] {
        Preset("road.residential", "Residential Road", TagPresetCategory.Road, TagPresetGeometry.Line,
            Tags(("highway", "residential")), [Name(), Field("maxspeed", "Speed limit", TagPresetFieldKind.Number), Choice("surface", "Surface", SurfaceChoices)],
            "street", "highway"),
        Preset("road.service", "Service Road", TagPresetCategory.Road, TagPresetGeometry.Line,
            Tags(("highway", "service")), [Name(), Choice("access", "Access", AccessChoices), Choice("surface", "Surface", SurfaceChoices)],
            "driveway", "alley"),
        Preset("path.footway", "Footway", TagPresetCategory.Path, TagPresetGeometry.Line,
            Tags(("highway", "footway")), [Name(), Choice("surface", "Surface", SurfaceChoices), Choice("access", "Access", AccessChoices)],
            "walking", "sidewalk"),
        Preset("path.cycleway", "Cycleway", TagPresetCategory.Path, TagPresetGeometry.Line,
            Tags(("highway", "cycleway")), [Name(), Choice("surface", "Surface", SurfaceChoices), Choice("foot", "Pedestrian access", AccessChoices)],
            "bicycle", "bike"),
        Preset("building.generic", "Building", TagPresetCategory.Building, TagPresetGeometry.Area,
            Tags(("building", "yes")), [Name(), Field("building:levels", "Levels", TagPresetFieldKind.Number), Field("height", "Height", TagPresetFieldKind.Number)],
            "structure"),
        Preset("building.house", "House", TagPresetCategory.Building, TagPresetGeometry.Area,
            Tags(("building", "house")), [Name(), Field("building:levels", "Levels", TagPresetFieldKind.Number), Field("addr:housenumber", "House number", TagPresetFieldKind.Text)],
            "home", "residential"),
        Preset("address", "Address", TagPresetCategory.Address, TagPresetGeometry.Point | TagPresetGeometry.Area,
            Tags(), [Required("addr:housenumber", "House number"), Required("addr:street", "Street"), Field("addr:city", "City", TagPresetFieldKind.Text), Field("addr:postcode", "Postcode", TagPresetFieldKind.Text)],
            "postal", "house number"),
        Preset("place.locality", "Locality", TagPresetCategory.Place, TagPresetGeometry.Point | TagPresetGeometry.Area,
            Tags(("place", "locality")), [Required("name", "Name"), Field("population", "Population", TagPresetFieldKind.Number)],
            "named place"),
        Preset("place.village", "Village", TagPresetCategory.Place, TagPresetGeometry.Point | TagPresetGeometry.Area,
            Tags(("place", "village")), [Required("name", "Name"), Field("population", "Population", TagPresetFieldKind.Number)],
            "settlement"),
        Preset("amenity.restaurant", "Restaurant", TagPresetCategory.Amenity, TagPresetGeometry.Point | TagPresetGeometry.Area,
            Tags(("amenity", "restaurant")), [Name(), Field("cuisine", "Cuisine", TagPresetFieldKind.Text), Checkbox("takeaway", "Takeaway")],
            "food", "dining"),
        Preset("amenity.school", "School", TagPresetCategory.Amenity, TagPresetGeometry.Point | TagPresetGeometry.Area,
            Tags(("amenity", "school")), [Name(), Field("operator", "Operator", TagPresetFieldKind.Text), Field("website", "Website", TagPresetFieldKind.Text)],
            "education"),
        Preset("amenity.parking", "Parking", TagPresetCategory.Amenity, TagPresetGeometry.Point | TagPresetGeometry.Area,
            Tags(("amenity", "parking")), [Name(), Choice("access", "Access", AccessChoices), Field("capacity", "Capacity", TagPresetFieldKind.Number)],
            "car park"),
        Preset("shop.supermarket", "Supermarket", TagPresetCategory.Shop, TagPresetGeometry.Point | TagPresetGeometry.Area,
            Tags(("shop", "supermarket")), [Name(), Field("brand", "Brand", TagPresetFieldKind.Text), Field("opening_hours", "Opening hours", TagPresetFieldKind.Text)],
            "grocery", "store"),
        Preset("shop.convenience", "Convenience Store", TagPresetCategory.Shop, TagPresetGeometry.Point | TagPresetGeometry.Area,
            Tags(("shop", "convenience")), [Name(), Field("brand", "Brand", TagPresetFieldKind.Text), Field("opening_hours", "Opening hours", TagPresetFieldKind.Text)],
            "grocery", "minimart"),
        Preset("landuse.residential", "Residential Land Use", TagPresetCategory.LandUse, TagPresetGeometry.Area,
            Tags(("landuse", "residential")), [Name()], "housing", "neighbourhood"),
        Preset("landuse.forest", "Managed Forest", TagPresetCategory.LandUse, TagPresetGeometry.Area,
            Tags(("landuse", "forest")), [Name(), Field("leaf_type", "Leaf type", TagPresetFieldKind.Text)],
            "forestry", "woodland"),
        Preset("natural.wood", "Natural Wood", TagPresetCategory.Natural, TagPresetGeometry.Point | TagPresetGeometry.Area,
            Tags(("natural", "wood")), [Name(), Field("leaf_type", "Leaf type", TagPresetFieldKind.Text)],
            "forest", "trees"),
        Preset("natural.water", "Water Body", TagPresetCategory.Natural, TagPresetGeometry.Area,
            Tags(("natural", "water")), [Name(), Field("water", "Water type", TagPresetFieldKind.Text)],
            "lake", "pond", "reservoir"),
        Preset("public_transport.platform", "Public Transport Platform", TagPresetCategory.PublicTransport, TagPresetGeometry.Point | TagPresetGeometry.Line | TagPresetGeometry.Area,
            Tags(("public_transport", "platform")), [Name(), Field("ref", "Reference", TagPresetFieldKind.Text), Checkbox("shelter", "Shelter")],
            "bus stop", "railway", "transit"),
        Preset("public_transport.stop_position", "Stop Position", TagPresetCategory.PublicTransport, TagPresetGeometry.Point,
            Tags(("public_transport", "stop_position")), [Name(), Field("ref", "Reference", TagPresetFieldKind.Text)],
            "bus", "tram", "train", "transit")
    });

    public static IReadOnlyList<TagPreset> All => Presets;

    public static IReadOnlyList<TagPreset> Search(string? query, MapGeometryType? geometry = null) {
        var terms = string.IsNullOrWhiteSpace(query)
            ? []
            : query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return Presets
            .Where(preset => geometry is null || SupportsGeometry(preset, geometry.Value))
            .Where(preset => terms.All(term => Matches(preset, term)))
            .OrderBy(static preset => preset.Category)
            .ThenBy(static preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool SupportsGeometry(TagPreset preset, MapGeometryType geometry) {
        ArgumentNullException.ThrowIfNull(preset);
        var flag = geometry switch {
            MapGeometryType.Point => TagPresetGeometry.Point,
            MapGeometryType.LineString => TagPresetGeometry.Line,
            MapGeometryType.Polygon => TagPresetGeometry.Area,
            _ => TagPresetGeometry.None
        };
        return (preset.Geometries & flag) != 0;
    }

    private static bool Matches(TagPreset preset, string term) {
        return Contains(preset.Id, term) ||
            Contains(preset.Name, term) ||
            Contains(preset.Category.ToString(), term) ||
            preset.SearchTerms.Any(value => Contains(value, term)) ||
            preset.Tags.Any(tag =>
                Contains(tag.Key, term) ||
                Contains(tag.Value, term) ||
                Contains($"{tag.Key}={tag.Value}", term)) ||
            preset.Fields.Any(field => Contains(field.Key, term) || Contains(field.Label, term));
    }

    private static bool Contains(string value, string term) =>
        value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static TagPreset Preset(
        string id,
        string name,
        TagPresetCategory category,
        TagPresetGeometry geometries,
        IReadOnlyDictionary<string, string> tags,
        IReadOnlyList<TagPresetField> fields,
        params string[] searchTerms) {
        return new TagPreset(id, name, category, geometries, tags, fields, Array.AsReadOnly(searchTerms));
    }

    private static IReadOnlyDictionary<string, string> Tags(params (string Key, string Value)[] tags) {
        return new ReadOnlyDictionary<string, string>(tags.ToDictionary(
            static tag => tag.Key,
            static tag => tag.Value,
            StringComparer.Ordinal));
    }

    private static TagPresetField Name() =>
        Required("name", "Name");

    private static TagPresetField Required(string key, string label) =>
        Field(key, label, TagPresetFieldKind.Text, TagPresetFieldImportance.Recommended);

    private static TagPresetField Field(
        string key,
        string label,
        TagPresetFieldKind kind,
        TagPresetFieldImportance importance = TagPresetFieldImportance.Optional) {
        return new TagPresetField(key, label, kind, importance);
    }

    private static TagPresetField Choice(string key, string label, IReadOnlyList<TagPresetChoice> choices) =>
        new(key, label, TagPresetFieldKind.Choice, TagPresetFieldImportance.Optional, choices);

    private static TagPresetField Checkbox(string key, string label) =>
        Field(key, label, TagPresetFieldKind.Checkbox);

    private static IReadOnlyList<TagPresetChoice> Choices(params (string Value, string Label)[] choices) =>
        Array.AsReadOnly(choices.Select(static choice => new TagPresetChoice(choice.Value, choice.Label)).ToArray());
}
