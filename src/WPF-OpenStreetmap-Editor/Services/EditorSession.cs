using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed class EditorSession {
    public EditorSession() {
        CommandStack = new EditCommandStack(Dataset);
    }

    public MapEditDataset Dataset { get; } = new();

    public EditCommandStack CommandStack { get; }

    public MapDocument? Document => Dataset.Document;

    public MapFeature? DraftLine { get; private set; }

    public bool HasDraftLine => DraftLine is not null;

    public void ReplaceDocument(MapDocument? document) {
        CancelDraftLine();
        Dataset.ReplaceDocument(document);
        CommandStack.Clear();
    }

    public MapDocument EnsureDocument() {
        return Dataset.EnsureDocument();
    }

    public bool Execute(IEditCommand command) {
        return CommandStack.Execute(command);
    }

    public bool Undo() {
        return CommandStack.Undo();
    }

    public bool Redo() {
        return CommandStack.Redo();
    }

    public bool AddDraftLinePoint(GeoPoint point) {
        if (!point.IsValid) return false;

        if (DraftLine is null) {
            DraftLine = new MapFeature {
                GeometryType = MapGeometryType.LineString,
                Parts = [[]]
            };
            Dataset.AddFeature(DraftLine, markDirty: false);
        }

        return Dataset.AppendPoint(DraftLine, 0, point, markDirty: false).HasValue;
    }

    public MapFeature? FinishDraftLine() {
        if (DraftLine is null) return null;

        var completedLine = DraftLine;
        DraftLine = null;
        var insertionIndex = Dataset.IndexOf(completedLine);
        Dataset.RemoveFeature(completedLine, markDirty: false);

        if (completedLine.Parts.Count == 0 || completedLine.Parts[0].Count < 2) return null;

        return Execute(new AddFeatureCommand(completedLine, insertionIndex >= 0 ? insertionIndex : null))
            ? completedLine
            : null;
    }

    public bool CancelDraftLine() {
        if (DraftLine is null) return false;

        var draft = DraftLine;
        DraftLine = null;
        Dataset.RemoveFeature(draft, markDirty: false);
        return true;
    }
}
