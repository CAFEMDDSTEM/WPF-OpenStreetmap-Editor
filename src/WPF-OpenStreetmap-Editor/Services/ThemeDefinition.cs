using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed class ThemeDefinition {
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Author { get; init; } = "";
    public string Version { get; init; } = "1.0";
    public string BaseTheme { get; init; } = "light";
    public string BackgroundImage { get; init; } = "";
    public double BackgroundImageOpacity { get; init; } = 0.18;
    public ThemeColors Colors { get; init; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThemeMapStyle? MapStyle { get; init; }

    [JsonIgnore]
    public bool IsBuiltIn { get; init; }

    [JsonIgnore]
    public string? SourcePath { get; init; }

    [JsonIgnore]
    public BitmapSource? Icon { get; init; }

    [JsonIgnore]
    public string Description { get; init; } = "";
}

public sealed class ThemeColors {
    public string Window { get; init; } = "";
    public string Surface { get; init; } = "";
    public string SurfaceAlt { get; init; } = "";
    public string Text { get; init; } = "";
    public string MutedText { get; init; } = "";
    public string Border { get; init; } = "";
    public string Accent { get; init; } = "";
    public string AccentText { get; init; } = "";
    public string Selection { get; init; } = "";
    public string SelectionText { get; init; } = "";
    public string MapBackground { get; init; } = "";
    public string Error { get; init; } = "#C42B1C";
}

public sealed record ThemeCatalogResult(
    IReadOnlyList<ThemeDefinition> Themes,
    IReadOnlyList<string> Errors);
