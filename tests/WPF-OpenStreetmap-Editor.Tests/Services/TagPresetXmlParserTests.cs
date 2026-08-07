using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class TagPresetXmlParserTests {
    [Fact]
    public void Parse_ParsesGroupItemTagsAndGeometry() {
        const string xml = """
            <presets xmlns="http://josm.openstreetmap.de/tagging-preset-1.0">
                <group name="Highways">
                    <item name="Motorway" icon="presets/way_motorway.svg" type="way">
                        <key key="highway" value="motorway" />
                        <text key="ref" text="Reference" />
                        <combo key="surface" text="Surface" values="asphalt,concrete" />
                    </item>
                </group>
            </presets>
            """;

        var set = TagPresetXmlParser.Parse(xml);

        var preset = Assert.Single(set.Presets);
        Assert.Equal("Motorway", preset.Name);
        Assert.Equal(TagPresetGeometry.Line, preset.Geometries);
        Assert.Equal(TagPresetCategory.Road, preset.Category);
        Assert.Equal("presets/way_motorway.svg", preset.Icon);
        Assert.Equal("motorway", preset.Tags["highway"]);
        Assert.Equal(2, preset.Fields.Count);
    }

    [Fact]
    public void Parse_ExpandsChunkReferencesAndBuildsNestedGroupKeys() {
        const string xml = """
            <presets xmlns="http://josm.openstreetmap.de/tagging-preset-1.0">
                <chunk id="base_tags">
                    <key key="source" value="survey" />
                    <combo key="layer" text="Layer" values="0,1,-1" />
                </chunk>
                <group name="Highways">
                    <group name="Streets">
                        <item name="Residential" type="way,closedway">
                            <reference ref="base_tags" />
                            <key key="highway" value="residential" />
                        </item>
                    </group>
                </group>
            </presets>
            """;

        var set = TagPresetXmlParser.Parse(xml);

        var preset = Assert.Single(set.Presets);
        Assert.Equal("residential", preset.Tags["highway"]);
        Assert.Equal("survey", preset.Tags["source"]);
        Assert.Equal(TagPresetGeometry.Line | TagPresetGeometry.Area, preset.Geometries);

        var streets = Assert.Single(set.RootGroups);
        Assert.Equal("Highways", streets.Name);
        var sub = Assert.Single(streets.Groups);
        Assert.Equal("Highways/Streets", sub.Key);
        Assert.Single(sub.Items);
        Assert.Equal("xml:Highways/Streets/Residential", preset.Id);
    }

    [Theory]
    [InlineData("node", TagPresetGeometry.Point)]
    [InlineData("way", TagPresetGeometry.Line)]
    [InlineData("closedway", TagPresetGeometry.Line | TagPresetGeometry.Area)]
    [InlineData("area", TagPresetGeometry.Area)]
    [InlineData("multipolygon", TagPresetGeometry.Area)]
    [InlineData("node,way,area", TagPresetGeometry.Point | TagPresetGeometry.Line | TagPresetGeometry.Area)]
    [InlineData(null, TagPresetGeometry.Any)]
    public void ParseGeometries_MapsTypes(string? type, TagPresetGeometry expected) {
        Assert.Equal(expected, TagPresetXmlParser.ParseGeometries(type));
    }

    [Fact]
    public void Parse_ClassifiesByTagsWhenAvailable() {
        const string xml = """
            <presets xmlns="http://josm.openstreetmap.de/tagging-preset-1.0">
                <group name="Other">
                    <item name="House" type="node,closedway">
                        <key key="building" value="house" />
                        <text key="maxspeed" text="Speed limit" />
                    </item>
                </group>
            </presets>
            """;

        var preset = Assert.Single(TagPresetXmlParser.Parse(xml).Presets);

        Assert.Equal(TagPresetCategory.Building, preset.Category);
        Assert.Equal(TagPresetFieldKind.Number, Assert.Single(preset.Fields).Kind);
    }

    [Fact]
    public void Parse_UniqueIdsForDuplicateNamesWithinSameGroup() {
        const string xml = """
            <presets xmlns="http://josm.openstreetmap.de/tagging-preset-1.0">
                <group name="Places">
                    <item name="Pump"><key key="amenity" value="pump" /></item>
                    <item name="Pump"><key key="craft" value="pump" /></item>
                </group>
            </presets>
            """;

        var set = TagPresetXmlParser.Parse(xml);

        Assert.Equal(2, set.Presets.Count);
        Assert.Equal(2, set.Presets.Select(static preset => preset.Id).Distinct().Count());
        var duplicate = Assert.Single(set.Presets, preset => preset.Id.EndsWith("#2", StringComparison.Ordinal));
        Assert.Equal("pump", duplicate.Tags["craft"]);
    }

    [Fact]
    public void Parse_ComboListEntriesBecomeChoices() {
        const string xml = """
            <presets xmlns="http://josm.openstreetmap.de/tagging-preset-1.0">
                <item name="Cycleway" type="way">
                    <key key="highway" value="cycleway" />
                    <combo key="surface" text="Surface">
                        <list_entry value="asphalt" short_description="Asphalt" />
                        <list_entry value="gravel" short_description="Gravel" />
                    </combo>
                </item>
            </presets>
            """;

        var preset = Assert.Single(TagPresetXmlParser.Parse(xml).Presets);
        var surface = Assert.Single(preset.Fields, field => field.Key == "surface");

        Assert.Equal(TagPresetFieldKind.Choice, surface.Kind);
        Assert.Equal(2, surface.Choices!.Count);
        Assert.Equal("Asphalt", surface.Choices[0].Label);
        Assert.Equal("gravel", surface.Choices[1].Value);
    }

    [Fact]
    public void BundledDefaults_ParseTheBundledJosmPresetFile() {
        var source = XmlTagPresetSource.CreateBundled();

        Assert.Equal(12, source.RootGroups.Count);
        Assert.True(source.Presets.Count > 900);

        var motorway = source.FindPreset("xml:Highways/Streets/Motorway");
        Assert.NotNull(motorway);
        Assert.Equal("Motorway", motorway!.Name);
        Assert.Equal("motorway", motorway.Tags["highway"]);
        Assert.Equal(TagPresetGeometry.Line, motorway.Geometries);
    }
}
