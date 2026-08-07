using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class PresetNameLocalizerTests {
    public PresetNameLocalizerTests() {
        PresetNameLocalizer.ClearCache();
    }

    [Theory]
    [InlineData("de", "Motorway", null, "Autobahn")]
    [InlineData("de", "Highways", null, "Straßen")]
    [InlineData("ru", "Motorway", null, "Автомагистраль")]
    [InlineData("ja", "Pedestrian Crossing", null, "横断歩道")]
    public void GetNameForLanguage_TranslatesKnownNames(string languageId, string name, string? context, string expected) {
        Assert.Equal(expected, PresetNameLocalizer.GetNameForLanguage(languageId, name, context));
    }

    [Fact]
    public void GetNameForLanguage_ContextBeatsPlainName() {
        // "Island" has both a plain translation and a name_context-specific one in the JOSM dataset.
        Assert.Equal("Insel", PresetNameLocalizer.GetNameForLanguage("de", "Island", null));
        Assert.Equal("Verkehrsinsel", PresetNameLocalizer.GetNameForLanguage("de", "Island", "traffic_calming"));
    }

    [Theory]
    [InlineData("de", "SomeMadeUpPresetName", "SomeMadeUpPresetName")]
    [InlineData("ru", "SomeMadeUpPresetName", "SomeMadeUpPresetName")]
    [InlineData("ja", "SomeMadeUpPresetName", "SomeMadeUpPresetName")]
    public void GetNameForLanguage_FallsBackToEnglishForMissingTranslations(string languageId, string name, string expected) {
        Assert.Equal(expected, PresetNameLocalizer.GetNameForLanguage(languageId, name, null));
    }

    [Theory]
    [InlineData("en", "Motorway", "Motorway")]
    [InlineData("en", "Island", "Island")]
    [InlineData(null, "Motorway", "Motorway")]
    [InlineData("", "Motorway", "Motorway")]
    public void GetNameForLanguage_EnglishAndEmptyLanguageReturnName(string? languageId, string name, string expected) {
        Assert.Equal(expected, PresetNameLocalizer.GetNameForLanguage(languageId ?? "", name, null));
    }

    [Fact]
    public void GetName_NullNameReturnsEmptyString() {
        Assert.Equal("", PresetNameLocalizer.GetName(null, null));
    }

    [Fact]
    public void BundledParser_ExposesNameContextForLocalizedDisplay() {
        var set = TagPresetXmlParser.Parse("""
            <presets xmlns="http://josm.openstreetmap.de/tagging-preset-1.0">
                <group name="Highways">
                    <item name="Island" name_context="traffic_calming" type="node,way">
                        <key key="traffic_calming" value="island" />
                    </item>
                </group>
            </presets>
            """);

        var preset = Assert.Single(set.Presets);
        Assert.Equal("traffic_calming", preset.NameContext);
        Assert.Equal("Island", preset.DisplayName);
        Assert.Equal(
            "Verkehrsinsel",
            PresetNameLocalizer.GetNameForLanguage("de", preset.Name, preset.NameContext));
    }

    [Fact]
    public void BundledParser_ExposesGroupNameContext() {
        var set = TagPresetXmlParser.Parse("""
            <presets xmlns="http://josm.openstreetmap.de/tagging-preset-1.0">
                <group name="Water" name_context="main group" icon="presets/nautical/waterway_river.svg">
                    <item name="Lake"><key key="natural" value="water" /></item>
                </group>
            </presets>
            """);

        var group = Assert.Single(set.RootGroups);
        Assert.Equal("main group", group.NameContext);
        Assert.Equal("Water", group.DisplayName);
        Assert.Equal(
            "Wasser",
            PresetNameLocalizer.GetNameForLanguage("de", group.Name, group.NameContext));
    }
}
