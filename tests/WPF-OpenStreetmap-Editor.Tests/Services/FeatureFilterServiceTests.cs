using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class FeatureFilterServiceTests {
    [Fact]
    public void Evaluate_UsesSearchSemanticsAndCombinesHideAndDimEffects() {
        var feature = Feature("cafe", ("amenity", "cafe"), ("name", "Corner Cafe"));
        var filters = new[] {
            Filter("poi", "amenity=cafe", FeatureFilterEffect.Dim),
            Filter("named", "corner", FeatureFilterEffect.Hide)
        };

        var result = FeatureFilterService.Evaluate(feature, filters);

        Assert.True(result.IsHidden);
        Assert.True(result.IsDimmed);
        Assert.Equal(["poi", "named"], result.MatchingFilterIds);
    }

    [Fact]
    public void Evaluate_AppliesInverseAndIgnoresDisabledFilters() {
        var feature = Feature("road", ("highway", "residential"));
        var filters = new[] {
            Filter("non-buildings", "building", FeatureFilterEffect.Hide, inverse: true),
            Filter("disabled", "highway", FeatureFilterEffect.Dim, enabled: false)
        };

        var result = FeatureFilterService.Evaluate(feature, filters);

        Assert.True(result.IsHidden);
        Assert.False(result.IsDimmed);
        Assert.Equal(["non-buildings"], result.MatchingFilterIds);
    }

    [Fact]
    public void Evaluate_NoMatchReturnsSharedVisibleResult() {
        var result = FeatureFilterService.Evaluate(Feature("road"), [Filter("water", "natural=water")]);

        Assert.Same(FeatureFilterResult.Visible, result);
    }

    [Fact]
    public void EvaluateAll_IndexesResultsByStableFeatureId() {
        var road = Feature("road", ("highway", "service"));
        var lake = Feature("lake", ("natural", "water"));

        var results = FeatureFilterService.EvaluateAll(
            [road, lake],
            [Filter("hide-water", "natural=water")]);

        Assert.False(results["road"].IsHidden);
        Assert.True(results["lake"].IsHidden);
    }

    private static FeatureFilterDefinition Filter(
        string id,
        string query,
        FeatureFilterEffect effect = FeatureFilterEffect.Hide,
        bool inverse = false,
        bool enabled = true) {
        return new FeatureFilterDefinition {
            Id = id,
            Query = query,
            Effect = effect,
            IsInverse = inverse,
            IsEnabled = enabled
        };
    }

    private static MapFeature Feature(string id, params (string Key, string Value)[] tags) {
        return new MapFeature {
            Id = id,
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(0, 0)]],
            Attributes = tags.ToDictionary(static tag => tag.Key, static tag => tag.Value)
        };
    }
}
