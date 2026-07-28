namespace WPF_OpenStreetmap_Editor.Models;

[Flags]
public enum TagPresetGeometry {
    None = 0,
    Point = 1,
    Line = 2,
    Area = 4,
    Any = Point | Line | Area
}

public enum TagPresetCategory {
    Road,
    Path,
    Building,
    Address,
    Place,
    Amenity,
    Shop,
    LandUse,
    Natural,
    PublicTransport
}

public enum TagPresetFieldKind {
    Text,
    Number,
    Choice,
    Checkbox
}

public enum TagPresetFieldImportance {
    Recommended,
    Optional
}

public sealed record TagPresetChoice(string Value, string Label);

public sealed record TagPresetField(
    string Key,
    string Label,
    TagPresetFieldKind Kind,
    TagPresetFieldImportance Importance = TagPresetFieldImportance.Optional,
    IReadOnlyList<TagPresetChoice>? Choices = null);

public sealed record TagPreset(
    string Id,
    string Name,
    TagPresetCategory Category,
    TagPresetGeometry Geometries,
    IReadOnlyDictionary<string, string> Tags,
    IReadOnlyList<TagPresetField> Fields,
    IReadOnlyList<string> SearchTerms);
