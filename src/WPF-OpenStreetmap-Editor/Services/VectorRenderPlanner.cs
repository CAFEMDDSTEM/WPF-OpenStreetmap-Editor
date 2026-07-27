using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed record VectorRenderPlan(
    IReadOnlyList<MapFeature> Features,
    int HiddenCount,
    int OutsideViewportCount,
    int BudgetOmittedCount,
    int CoordinateCount);

public static class VectorRenderPlanner {
    public const int DefaultFeatureBudget = 12_000;
    public const int DefaultCoordinateBudget = 220_000;
    public const int DefaultPlanningScanBudget = 80_000;

    public static VectorRenderPlan Create(
        MapDocument document,
        GeoBounds viewport,
        int featureBudget = DefaultFeatureBudget,
        int coordinateBudget = DefaultCoordinateBudget,
        int planningScanBudget = DefaultPlanningScanBudget) {
        ArgumentNullException.ThrowIfNull(document);
        if (planningScanBudget <= 0) throw new ArgumentOutOfRangeException(nameof(planningScanBudget));

        return CreateCore(
            document.QueryFeatures(viewport),
            viewport,
            featureBudget,
            coordinateBudget,
            planningScanBudget);
    }

    public static VectorRenderPlan Create(
        IEnumerable<MapFeature> features,
        GeoBounds viewport,
        int featureBudget = DefaultFeatureBudget,
        int coordinateBudget = DefaultCoordinateBudget) {
        return CreateCore(features, viewport, featureBudget, coordinateBudget, int.MaxValue);
    }

    private static VectorRenderPlan CreateCore(
        IEnumerable<MapFeature> features,
        GeoBounds viewport,
        int featureBudget,
        int coordinateBudget,
        int planningScanBudget) {
        if (featureBudget <= 0) throw new ArgumentOutOfRangeException(nameof(featureBudget));
        if (coordinateBudget <= 0) throw new ArgumentOutOfRangeException(nameof(coordinateBudget));

        var visible = new List<MapFeature>();
        var hidden = 0;
        var outside = 0;
        var omitted = 0;
        var coordinates = 0;
        var scanned = 0;
        foreach (var feature in features) {
            if (scanned++ >= planningScanBudget) {
                omitted++;
                break;
            }
            if (feature.IsHidden) {
                hidden++;
                continue;
            }
            if (viewport.IsValid && !feature.Bounds.Intersects(viewport)) {
                outside++;
                continue;
            }

            var featureCoordinates = feature.CoordinateCount;
            if (visible.Count >= featureBudget || coordinates + featureCoordinates > coordinateBudget) {
                omitted++;
                continue;
            }
            visible.Add(feature);
            coordinates += featureCoordinates;
        }

        return new VectorRenderPlan(visible, hidden, outside, omitted, coordinates);
    }
}
