using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class TagPresetCatalogTests {
    [Fact]
    public void All_CoversEveryPlannedCategoryWithUniqueIds() {
        var categories = TagPresetCatalog.All.Select(static preset => preset.Category).Distinct();

        Assert.Equal(Enum.GetValues<TagPresetCategory>().Order(), categories.Order());
        Assert.Equal(TagPresetCatalog.All.Count, TagPresetCatalog.All.Select(static preset => preset.Id).Distinct().Count());
    }

    [Fact]
    public void Search_MatchesNamesKeywordsTagsAndFieldsCaseInsensitively() {
        Assert.Contains(TagPresetCatalog.Search("GROCERY"), preset => preset.Id == "shop.supermarket");
        Assert.Contains(TagPresetCatalog.Search("highway=residential"), preset => preset.Id == "road.residential");
        Assert.Contains(TagPresetCatalog.Search("opening hours"), preset => preset.Id == "shop.convenience");
    }

    [Fact]
    public void Search_RequiresAllTermsAndFiltersByGeometry() {
        var results = TagPresetCatalog.Search("public transport", MapGeometryType.Polygon);

        var platform = Assert.Single(results);
        Assert.Equal("public_transport.platform", platform.Id);
        Assert.DoesNotContain(results, preset => preset.Id == "public_transport.stop_position");
    }

    [Fact]
    public void Presets_ExposeFixedTagsAndTypedRecommendedFields() {
        var address = Assert.Single(TagPresetCatalog.All, preset => preset.Id == "address");
        var houseNumber = Assert.Single(address.Fields, field => field.Key == "addr:housenumber");
        var road = Assert.Single(TagPresetCatalog.All, preset => preset.Id == "road.residential");
        var surface = Assert.Single(road.Fields, field => field.Key == "surface");

        Assert.Equal(TagPresetFieldImportance.Recommended, houseNumber.Importance);
        Assert.Equal(TagPresetFieldKind.Choice, surface.Kind);
        Assert.NotEmpty(surface.Choices!);
        Assert.Equal("residential", road.Tags["highway"]);
    }

    [Theory]
    [InlineData("building.generic", MapGeometryType.Polygon, true)]
    [InlineData("building.generic", MapGeometryType.Point, false)]
    [InlineData("amenity.restaurant", MapGeometryType.Point, true)]
    [InlineData("amenity.restaurant", MapGeometryType.LineString, false)]
    public void SupportsGeometry_UsesPresetGeometryFlags(string presetId, MapGeometryType geometry, bool expected) {
        var preset = Assert.Single(TagPresetCatalog.All, item => item.Id == presetId);

        Assert.Equal(expected, TagPresetCatalog.SupportsGeometry(preset, geometry));
    }
}
