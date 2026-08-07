using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class PresetToolbarServiceTests {
    private static readonly PresetService Service = new([
        BuiltInPresetSource.Instance,
        XmlTagPresetSource.FromXml("""
            <presets xmlns="http://josm.openstreetmap.de/tagging-preset-1.0">
                <group name="Highways">
                    <group name="Streets">
                        <item name="Motorway" type="way">
                            <key key="highway" value="motorway" />
                        </item>
                        <item name="Primary" type="way">
                            <key key="highway" value="primary" />
                        </item>
                    </group>
                </group>
            </presets>
            """)
    ]);

    [Fact]
    public void Resolve_BuiltInPresetBecomesSingleActionWithDefaultLabel() {
        var buttons = new List<PresetToolbarButton> {
            new() { PresetId = "road.residential" }
        };

        var action = Assert.Single(PresetToolbarService.Resolve(buttons, Service));

        Assert.Equal(PresetToolbarActionKind.Single, action.Kind);
        Assert.Equal("Residential Road", action.Label);
        Assert.NotNull(action.Preset);
        Assert.Equal("residential", action.Preset!.Tags["highway"]);
    }

    [Fact]
    public void Resolve_GroupBecomesGroupActionWithFlattenedItems() {
        var buttons = new List<PresetToolbarButton> {
            new() { GroupKey = "Highways/Streets", Label = "Streets" }
        };

        var action = Assert.Single(PresetToolbarService.Resolve(buttons, Service));

        Assert.Equal(PresetToolbarActionKind.Group, action.Kind);
        Assert.Equal(2, action.GroupPresets.Count);
        Assert.Equal("Streets", action.Label);
    }

    [Fact]
    public void Resolve_SkipsUnresolvableEntries() {
        var buttons = new List<PresetToolbarButton> {
            new() { PresetId = "does.not.exist" },
            new() { GroupKey = "Missing/Group" },
            new() { PresetId = "path.footway" }
        };

        var action = Assert.Single(PresetToolbarService.Resolve(buttons, Service));

        Assert.Equal("path.footway", action.Preset!.Id);
    }

    [Fact]
    public void RemoveUnresolvable_PrunesStaleButtons() {
        var buttons = new List<PresetToolbarButton> {
            new() { PresetId = "road.residential" },
            new() { PresetId = "gone.preset" },
            new() { GroupKey = "Highways/Streets" }
        };

        var changed = PresetToolbarService.RemoveUnresolvable(buttons, Service);

        Assert.True(changed);
        Assert.Equal(2, buttons.Count);
        Assert.DoesNotContain(buttons, button => button.PresetId == "gone.preset");
    }

    [Fact]
    public void RemoveUnresolvable_ReturnsFalseWhenAllResolve() {
        var buttons = new List<PresetToolbarButton> {
            new() { PresetId = "road.residential" }
        };

        var changed = PresetToolbarService.RemoveUnresolvable(buttons, Service);

        Assert.False(changed);
        Assert.Single(buttons);
    }
}
