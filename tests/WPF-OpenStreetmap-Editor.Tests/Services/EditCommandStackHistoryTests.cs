using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class EditCommandStackHistoryTests {
    [Fact]
    public void MoveToHistoryPosition_UndoesAndRedoesToRequestedPosition() {
        var document = new MapDocument();
        var stack = new EditCommandStack(new MapEditDataset(document));
        var first = CreatePoint(1);
        var second = CreatePoint(2);

        Assert.True(stack.Execute(new AddFeatureCommand(first)));
        Assert.True(stack.Execute(new AddFeatureCommand(second)));
        Assert.Equal(2, stack.HistoryPosition);
        Assert.Equal(
            [(1, "Add feature", true), (2, "Add feature", true)],
            stack.History.Select(item => (item.Position, item.Description, item.IsApplied)).ToList());

        Assert.True(stack.MoveToHistoryPosition(0));
        Assert.Empty(document.Features);
        Assert.All(stack.History, static item => Assert.False(item.IsApplied));

        Assert.True(stack.MoveToHistoryPosition(2));
        Assert.Equal([first, second], document.Features);
    }

    [Fact]
    public void Execute_AfterUndoDiscardsRedoBranch() {
        var document = new MapDocument();
        var stack = new EditCommandStack(new MapEditDataset(document));
        var first = CreatePoint(1);
        var discarded = CreatePoint(2);
        var replacement = CreatePoint(3);

        Assert.True(stack.Execute(new AddFeatureCommand(first)));
        Assert.True(stack.Execute(new AddFeatureCommand(discarded)));
        Assert.True(stack.Undo());
        Assert.True(stack.Execute(new AddFeatureCommand(replacement)));

        Assert.Equal(2, stack.History.Count);
        Assert.Equal(2, stack.HistoryPosition);
        Assert.False(stack.CanRedo);
        Assert.Equal([first, replacement], document.Features);
    }

    [Fact]
    public void MoveToHistoryPosition_RejectsPositionOutsideTimeline() {
        var stack = new EditCommandStack(new MapEditDataset(new MapDocument()));

        Assert.Throws<ArgumentOutOfRangeException>(() => stack.MoveToHistoryPosition(1));
    }

    private static MapFeature CreatePoint(double longitude) {
        return new MapFeature {
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(longitude, 0)]]
        };
    }
}
