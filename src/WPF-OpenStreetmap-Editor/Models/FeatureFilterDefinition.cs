namespace WPF_OpenStreetmap_Editor.Models;

public enum FeatureFilterEffect {
    Hide,
    Dim
}

public sealed record FeatureFilterDefinition {
    public required string Id { get; init; }
    public string Query { get; init; } = "";
    public bool IsEnabled { get; init; } = true;
    public bool IsInverse { get; init; }
    public FeatureFilterEffect Effect { get; init; } = FeatureFilterEffect.Hide;
}

public sealed record FeatureFilterResult(
    bool IsHidden,
    bool IsDimmed,
    IReadOnlyList<string> MatchingFilterIds) {
    public static FeatureFilterResult Visible { get; } = new(false, false, []);
}
