using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class PatchFeaturesAttributesCommandTests {
    [Fact]
    public void Execute_SetsAndRemovesKeysOnEveryFeature_AndUndoRestoresTagsExactly() {
        var first = CreateFeature("first", ("highway", "residential"), ("name", "North"));
        var second = CreateFeature("second", ("highway", "service"), ("surface", "gravel"));
        var document = CreateDocument(first, second);
        var editor = new EditorSession();
        editor.ReplaceDocument(document);
        var patch = new Dictionary<string, string?>(StringComparer.Ordinal) {
            ["highway"] = "primary",
            ["name"] = null,
            ["lit"] = "yes"
        };

        Assert.True(editor.Execute(new PatchFeaturesAttributesCommand([first, second], patch)));

        Assert.Equal(Tags(("highway", "primary"), ("lit", "yes")), first.Attributes);
        Assert.Equal(Tags(("highway", "primary"), ("lit", "yes"), ("surface", "gravel")), second.Attributes);
        Assert.True(document.IsDirty);

        Assert.True(editor.Undo());

        Assert.Equal(Tags(("highway", "residential"), ("name", "North")), first.Attributes);
        Assert.Equal(Tags(("highway", "service"), ("surface", "gravel")), second.Attributes);
        Assert.False(document.IsDirty);

        Assert.True(editor.Redo());
        Assert.Equal(Tags(("highway", "primary"), ("lit", "yes")), first.Attributes);
    }

    [Fact]
    public void Execute_ReturnsFalseWhenNoFeatureWouldChange() {
        var feature = CreateFeature("feature", ("highway", "primary"));
        var document = CreateDocument(feature);
        var editor = new EditorSession();
        editor.ReplaceDocument(document);
        var patch = new Dictionary<string, string?>(StringComparer.Ordinal) {
            ["highway"] = "primary",
            ["name"] = null
        };

        Assert.False(editor.Execute(new PatchFeaturesAttributesCommand([feature], patch)));

        Assert.Equal(Tags(("highway", "primary")), feature.Attributes);
        Assert.False(document.IsDirty);
        Assert.False(editor.CommandStack.CanUndo);
    }

    [Fact]
    public void Execute_IgnoresBlankKeys() {
        var feature = CreateFeature("feature", ("highway", "primary"));
        var document = CreateDocument(feature);
        var editor = new EditorSession();
        editor.ReplaceDocument(document);
        var blankOnly = new Dictionary<string, string?>(StringComparer.Ordinal) {
            [""] = "x",
            ["   "] = null
        };
        var mixed = new Dictionary<string, string?>(StringComparer.Ordinal) {
            [" "] = "x",
            ["name"] = "Main Street"
        };

        Assert.False(editor.Execute(new PatchFeaturesAttributesCommand([feature], blankOnly)));
        Assert.True(editor.Execute(new PatchFeaturesAttributesCommand([feature], mixed)));

        Assert.Equal(Tags(("highway", "primary"), ("name", "Main Street")), feature.Attributes);
    }

    [Fact]
    public void Execute_SkipsFeaturesOutsideTheDocument() {
        var inside = CreateFeature("inside", ("highway", "primary"));
        var outside = CreateFeature("outside", ("highway", "primary"));
        var document = CreateDocument(inside);
        var editor = new EditorSession();
        editor.ReplaceDocument(document);
        var patch = new Dictionary<string, string?>(StringComparer.Ordinal) { ["highway"] = "secondary" };

        Assert.True(editor.Execute(new PatchFeaturesAttributesCommand([inside, outside], patch)));

        Assert.Equal("secondary", inside.Attributes["highway"]);
        Assert.Equal("primary", outside.Attributes["highway"]);
    }

    [Fact]
    public void CreatePerFeature_AppliesEachFeaturesOwnPatch_AndUndoesTogether() {
        var first = CreateFeature("first", ("old", "1"));
        var second = CreateFeature("second", ("old", "2"));
        var document = CreateDocument(first, second);
        var editor = new EditorSession();
        editor.ReplaceDocument(document);
        var command = PatchFeaturesAttributesCommand.CreatePerFeature([
            KeyValuePair.Create<MapFeature, IReadOnlyDictionary<string, string?>>(first, Patch(("old", null), ("new", "1"))),
            KeyValuePair.Create<MapFeature, IReadOnlyDictionary<string, string?>>(second, Patch(("old", null), ("new", "2")))
        ]);

        Assert.True(editor.Execute(command));
        Assert.Equal(Tags(("new", "1")), first.Attributes);
        Assert.Equal(Tags(("new", "2")), second.Attributes);
        Assert.Single(editor.CommandStack.History);

        Assert.True(editor.Undo());
        Assert.Equal(Tags(("old", "1")), first.Attributes);
        Assert.Equal(Tags(("old", "2")), second.Attributes);
    }

    private static MapDocument CreateDocument(params MapFeature[] features) {
        var document = new MapDocument();
        document.Features.AddRange(features);
        document.MarkClean();
        return document;
    }

    private static MapFeature CreateFeature(string id, params (string Key, string Value)[] tags) {
        var feature = new MapFeature {
            Id = id,
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(0, 0)]]
        };
        foreach (var (key, value) in tags) feature.Attributes[key] = value;
        return feature;
    }

    private static Dictionary<string, string> Tags(params (string Key, string Value)[] tags) {
        return tags.ToDictionary(static tag => tag.Key, static tag => tag.Value, StringComparer.Ordinal);
    }

    private static Dictionary<string, string?> Patch(params (string Key, string? Value)[] tags) {
        return tags.ToDictionary(static tag => tag.Key, static tag => tag.Value, StringComparer.Ordinal);
    }
}
