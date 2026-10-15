using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

/// <summary>How a draft line is turned into a feature when drawing ends.</summary>
public enum DraftCompletion {
    /// <summary>Keep an open line; drafts with fewer than two vertices are discarded.</summary>
    Line,
    /// <summary>Close the ring and create a polygon; needs three distinct vertices.</summary>
    ClosedRing,
    /// <summary>Like <see cref="Line"/>, but a single-vertex draft becomes a point feature (JOSM behaviour).</summary>
    SinglePoint
}

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

    public MapFeature? FinishDraftLine(DraftCompletion completion = DraftCompletion.Line) {
        if (DraftLine is null) return null;

        var completedLine = DraftLine;
        DraftLine = null;
        var insertionIndex = Dataset.IndexOf(completedLine);
        Dataset.RemoveFeature(completedLine, markDirty: false);
        if (completedLine.Parts.Count == 0) return null;

        var points = completedLine.Parts[0];
        switch (completion) {
            case DraftCompletion.SinglePoint when points.Count == 1:
                completedLine.GeometryType = MapGeometryType.Point;
                break;
            case DraftCompletion.ClosedRing:
                if (points.Distinct().Count() < 3) return null;
                if (points[0] != points[^1]) points.Add(points[0]);
                completedLine.GeometryType = MapGeometryType.Polygon;
                completedLine.InvalidateGeometry();
                break;
            default:
                if (points.Count < 2) return null;
                break;
        }

        return Execute(new AddFeatureCommand(completedLine, insertionIndex >= 0 ? insertionIndex : null))
            ? completedLine
            : null;
    }

    public bool RemoveLastDraftLinePoint() {
        if (DraftLine is null || DraftLine.Parts.Count == 0 || DraftLine.Parts[0].Count == 0) return false;

        return Dataset.RemovePointAt(DraftLine, 0, DraftLine.Parts[0].Count - 1, markDirty: false);
    }

    public bool CancelDraftLine() {
        if (DraftLine is null) return false;

        var draft = DraftLine;
        DraftLine = null;
        Dataset.RemoveFeature(draft, markDirty: false);
        return true;
    }
}
