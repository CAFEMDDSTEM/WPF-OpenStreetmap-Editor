using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class TagPresetApplicationServiceTests {
    [Fact]
    public void BuildTagPatch_CombinesPresetTagsAndEnteredValuesAndSkipsBlankFields() {
        var preset = new TagPreset(
            "road.residential",
            "Residential Road",
            TagPresetCategory.Road,
            TagPresetGeometry.Line,
            new Dictionary<string, string>(StringComparer.Ordinal) {
                ["highway"] = "residential"
            },
            [
                new TagPresetField("name", "Name", TagPresetFieldKind.Text),
                new TagPresetField("lanes", "Lanes", TagPresetFieldKind.Number),
                new TagPresetField("ref", "Reference", TagPresetFieldKind.Text)
            ],
            []);
        var fieldValues = new Dictionary<string, string?>(StringComparer.Ordinal) {
            ["name"] = "Main Street",
            ["lanes"] = "",
            ["ref"] = null
        };

        var patch = TagPresetApplicationService.BuildTagPatch(preset, fieldValues);

        Assert.Equal("residential", patch["highway"]);
        Assert.Equal("Main Street", patch["name"]);
        Assert.DoesNotContain("lanes", patch.Keys);
        Assert.DoesNotContain("ref", patch.Keys);
    }

    [Fact]
    public void BuildTagPatch_PreservesExplicitNoForUncheckedStructureField() {
        var preset = new TagPreset(
            "road.residential",
            "Residential Road",
            TagPresetCategory.Road,
            TagPresetGeometry.Line,
            new Dictionary<string, string>(StringComparer.Ordinal) {
                ["highway"] = "residential"
            },
            [new TagPresetField("bridge", "Bridge", TagPresetFieldKind.Checkbox)],
            []);
        var fieldValues = new Dictionary<string, string?>(StringComparer.Ordinal) {
            ["bridge"] = "no"
        };

        var patch = TagPresetApplicationService.BuildTagPatch(preset, fieldValues);

        Assert.Equal("no", patch["bridge"]);
    }
}
