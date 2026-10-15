using System.Collections.ObjectModel;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class TagAutocompleteServiceTests {
    private static readonly IReadOnlyList<TagPreset> Presets = [
        CreatePreset("road", Tags(("highway", "residential")), [
            new TagPresetField("surface", "Surface", TagPresetFieldKind.Choice, Choices: [new("asphalt", "Asphalt"), new("gravel", "Gravel")]),
            new TagPresetField("oneway", "One way", TagPresetFieldKind.Checkbox)
        ]),
        CreatePreset("building", Tags(("building", "yes")), [new TagPresetField("name", "Name", TagPresetFieldKind.Text)])
    ];

    [Fact]
    public void SuggestKeys_RanksPrefixMatchesBeforeSubstringMatches() {
        var service = new TagAutocompleteService(Presets);
        var features = new[] { CreateFeature(("width", "3"), ("maxweight", "7.5")) };

        var keys = service.SuggestKeys("w", features);

        Assert.Equal(["width", "maxweight", "highway", "oneway"], keys);
    }

    [Fact]
    public void SuggestKeys_RanksDocumentKeysBeforePresetOnlyKeys() {
        var service = new TagAutocompleteService(Presets);
        var features = new[] { CreateFeature(("zzz", "1"), ("highway", "track")) };

        var keys = service.SuggestKeys("", features);

        Assert.Equal(["highway", "zzz", "building", "name", "oneway", "surface"], keys);
    }

    [Fact]
    public void SuggestKeys_IsCaseInsensitiveAndRespectsLimit() {
        var service = new TagAutocompleteService(Presets);

        Assert.Equal(["highway"], service.SuggestKeys("HIGH", []));
        Assert.Equal(2, service.SuggestKeys("", [], limit: 2).Count);
        Assert.Empty(service.SuggestKeys("", [], limit: 0));
    }

    [Fact]
    public void SuggestValues_UsesPresetTagsChoicesAndCheckboxValues() {
        var service = new TagAutocompleteService(Presets);

        Assert.Equal(["residential"], service.SuggestValues("highway", "res", []));
        Assert.Equal(["asphalt", "gravel"], service.SuggestValues("surface", null, []));
        Assert.Equal(["no", "yes"], service.SuggestValues("oneway", "", []));
        Assert.Empty(service.SuggestValues("unknown", "", []));
        Assert.Empty(service.SuggestValues("", "", []));
    }

    [Fact]
    public void SuggestValues_RanksDocumentValuesFirstWithoutDuplicates() {
        var service = new TagAutocompleteService(Presets);
        var features = new[] {
            CreateFeature(("highway", "service")),
            CreateFeature(("highway", "residential")),
            CreateFeature(("highway", "service"))
        };

        var values = service.SuggestValues("highway", "", features);

        Assert.Equal(["residential", "service"], values);
    }

    [Fact]
    public void BuiltInCatalog_SuggestsResidentialForHighway() {
        var service = new TagAutocompleteService(TagPresetCatalog.All);

        Assert.Contains("residential", service.SuggestValues("highway", "res", []));
        Assert.Contains("highway", service.SuggestKeys("hi", []));
    }

    private static TagPreset CreatePreset(string id, IReadOnlyDictionary<string, string> tags, IReadOnlyList<TagPresetField> fields) {
        return new TagPreset(id, id, TagPresetCategory.Custom, TagPresetGeometry.Any, tags, fields, []);
    }

    private static IReadOnlyDictionary<string, string> Tags(params (string Key, string Value)[] tags) {
        return new ReadOnlyDictionary<string, string>(
            tags.ToDictionary(static tag => tag.Key, static tag => tag.Value, StringComparer.Ordinal));
    }

    private static MapFeature CreateFeature(params (string Key, string Value)[] tags) {
        var feature = new MapFeature {
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(0, 0)]]
        };
        foreach (var (key, value) in tags) feature.Attributes[key] = value;
        return feature;
    }
}
