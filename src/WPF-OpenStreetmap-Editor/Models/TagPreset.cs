namespace WPF_OpenStreetmap_Editor.Models;

using WPF_OpenStreetmap_Editor.Services;

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
    PublicTransport,
    Custom
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
    IReadOnlyList<string> SearchTerms,
    string? Icon = null,
    string? NameContext = null) {
    /// <summary>The localized display name for the current UI language, falling back to the English name.</summary>
    public string DisplayName => PresetNameLocalizer.GetName(Name, NameContext);
}

public sealed record TagPresetGroup(
    string Key,
    string Name,
    string? Icon,
    IReadOnlyList<TagPresetGroup> Groups,
    IReadOnlyList<TagPreset> Items,
    string? NameContext = null) {
    /// <summary>The localized display name for the current UI language, falling back to the English name.</summary>
    public string DisplayName => PresetNameLocalizer.GetName(Name, NameContext);
}

public sealed class PresetToolbarButton {
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? PresetId { get; set; }
    public string? GroupKey { get; set; }
    public string Label { get; set; } = "";
    public string? Icon { get; set; }

    public PresetToolbarButton Clone() {
        return new PresetToolbarButton {
            Id = Id,
            PresetId = PresetId,
            GroupKey = GroupKey,
            Label = Label,
            Icon = Icon
        };
    }
}
