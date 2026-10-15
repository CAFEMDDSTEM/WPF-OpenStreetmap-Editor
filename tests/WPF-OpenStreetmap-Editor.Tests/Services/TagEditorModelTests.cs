using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class TagEditorModelTests {
    [Fact]
    public void BuildRows_SingleFeature_ListsTagsOrderedByKey() {
        var feature = CreateFeature("feature", ("name", "Main"), ("highway", "residential"), ("Lit", "yes"));

        var rows = TagEditorModel.BuildRows([feature]);

        Assert.Equal(
            [
                new TagEditorRow("Lit", "yes", false, 1),
                new TagEditorRow("highway", "residential", false, 1),
                new TagEditorRow("name", "Main", false, 1)
            ],
            rows);
    }

    [Fact]
    public void BuildRows_MultipleFeatures_MarksDifferingAndMissingValues() {
        var first = CreateFeature("first", ("highway", "residential"), ("name", "North"), ("lit", "yes"));
        var second = CreateFeature("second", ("highway", "residential"), ("name", "South"));
        var third = CreateFeature("third", ("highway", "residential"), ("name", "North"));

        var rows = TagEditorModel.BuildRows([first, second, third]);

        Assert.Equal(
            [
                new TagEditorRow("highway", "residential", false, 3),
                new TagEditorRow("lit", null, true, 1),
                new TagEditorRow("name", null, true, 3)
            ],
            rows);
    }

    [Fact]
    public void BuildRows_NoFeatures_ReturnsNoRows() {
        Assert.Empty(TagEditorModel.BuildRows([]));
    }

    [Fact]
    public void RowsEqual_ComparesRowContent() {
        var feature = CreateFeature("feature", ("highway", "residential"));
        var original = TagEditorModel.BuildRows([feature]);
        var same = TagEditorModel.BuildRows([CreateFeature("other", ("highway", "residential"))]);
        feature.Attributes["highway"] = "service";
        var changedValue = TagEditorModel.BuildRows([feature]);
        feature.Attributes["name"] = "Alley";
        var changedCount = TagEditorModel.BuildRows([feature]);

        Assert.True(TagEditorModel.RowsEqual(original, same));
        Assert.False(TagEditorModel.RowsEqual(original, changedValue));
        Assert.False(TagEditorModel.RowsEqual(original, changedCount));
    }

    [Fact]
    public void CreateSetCommand_SetsValueOnEveryFeature() {
        var first = CreateFeature("first", ("highway", "residential"));
        var second = CreateFeature("second");
        var editor = CreateEditor(first, second);

        var command = TagEditorModel.CreateSetCommand([first, second], "highway", "primary");

        Assert.NotNull(command);
        Assert.True(editor.Execute(command));
        Assert.Equal("primary", first.Attributes["highway"]);
        Assert.Equal("primary", second.Attributes["highway"]);
    }

    [Fact]
    public void CreateSetCommand_BlankKeyReturnsNull_BlankValueRemovesKey() {
        var feature = CreateFeature("feature", ("highway", "residential"));
        var editor = CreateEditor(feature);

        Assert.Null(TagEditorModel.CreateSetCommand([feature], "  ", "primary"));
        Assert.Null(TagEditorModel.CreateSetCommand([], "highway", "primary"));

        var remove = TagEditorModel.CreateSetCommand([feature], "highway", " ");
        Assert.NotNull(remove);
        Assert.True(editor.Execute(remove));
        Assert.Empty(feature.Attributes);
    }

    [Fact]
    public void CreateRemoveCommand_RemovesKeyWherePresent_AndIsNoOpOtherwise() {
        var first = CreateFeature("first", ("highway", "residential"), ("name", "North"));
        var second = CreateFeature("second", ("highway", "residential"));
        var editor = CreateEditor(first, second);

        var remove = TagEditorModel.CreateRemoveCommand([first, second], "name");
        Assert.NotNull(remove);
        Assert.True(editor.Execute(remove));
        Assert.False(first.Attributes.ContainsKey("name"));
        Assert.Equal("residential", second.Attributes["highway"]);

        var missing = TagEditorModel.CreateRemoveCommand([first, second], "missing");
        Assert.NotNull(missing);
        Assert.False(editor.Execute(missing));
        Assert.Null(TagEditorModel.CreateRemoveCommand([first], ""));
    }

    [Fact]
    public void CreateRenameKeyCommand_KeepsEachFeaturesValue_AndUndoesAtomically() {
        var first = CreateFeature("first", ("addr:street", "Main"), ("name", "A"));
        var second = CreateFeature("second", ("addr:street", "High"));
        var untouched = CreateFeature("untouched", ("name", "C"));
        var editor = CreateEditor(first, second, untouched);
        var stack = editor.CommandStack;

        var command = TagEditorModel.CreateRenameKeyCommand([first, second, untouched], "addr:street", "addr:place");

        Assert.NotNull(command);
        Assert.True(stack.Execute(command));
        Assert.Equal(Tags(("addr:place", "Main"), ("name", "A")), first.Attributes);
        Assert.Equal(Tags(("addr:place", "High")), second.Attributes);
        Assert.Equal(Tags(("name", "C")), untouched.Attributes);
        Assert.Single(stack.History);

        Assert.True(stack.Undo());
        Assert.Equal(Tags(("addr:street", "Main"), ("name", "A")), first.Attributes);
        Assert.Equal(Tags(("addr:street", "High")), second.Attributes);
        Assert.Equal(Tags(("name", "C")), untouched.Attributes);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void CreateRenameKeyCommand_OverwritesExistingTargetKey_AndUndoRestoresIt() {
        var feature = CreateFeature("feature", ("old", "1"), ("new", "2"));
        var editor = CreateEditor(feature);

        var command = TagEditorModel.CreateRenameKeyCommand([feature], "old", "new");

        Assert.NotNull(command);
        Assert.True(editor.Execute(command));
        Assert.Equal(Tags(("new", "1")), feature.Attributes);

        Assert.True(editor.Undo());
        Assert.Equal(Tags(("old", "1"), ("new", "2")), feature.Attributes);
    }

    [Fact]
    public void CreateRenameKeyCommand_ReturnsNullForSameBlankOrAbsentKeys() {
        var feature = CreateFeature("feature", ("highway", "residential"));

        Assert.Null(TagEditorModel.CreateRenameKeyCommand([feature], "highway", "highway"));
        Assert.Null(TagEditorModel.CreateRenameKeyCommand([feature], "highway", " "));
        Assert.Null(TagEditorModel.CreateRenameKeyCommand([feature], "", "highway"));
        Assert.Null(TagEditorModel.CreateRenameKeyCommand([feature], "missing", "highway"));
    }

    private static EditorSession CreateEditor(params MapFeature[] features) {
        var document = new MapDocument();
        document.Features.AddRange(features);
        document.MarkClean();
        var editor = new EditorSession();
        editor.ReplaceDocument(document);
        return editor;
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
}
