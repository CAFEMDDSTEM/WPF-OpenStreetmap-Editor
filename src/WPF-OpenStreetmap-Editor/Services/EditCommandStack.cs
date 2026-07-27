namespace WPF_OpenStreetmap_Editor.Services;

public interface IEditCommand {
    string Description { get; }

    bool Execute(MapEditDataset dataset);

    void Undo(MapEditDataset dataset);
}

public sealed class EditCommandStack {
    private readonly MapEditDataset _dataset;
    private readonly Stack<IEditCommand> _undoStack = [];
    private readonly Stack<IEditCommand> _redoStack = [];

    public EditCommandStack(MapEditDataset dataset) {
        _dataset = dataset;
    }

    public event EventHandler? Changed;

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    public string? UndoDescription => _undoStack.TryPeek(out var command) ? command.Description : null;

    public string? RedoDescription => _redoStack.TryPeek(out var command) ? command.Description : null;

    public bool Execute(IEditCommand command) {
        if (!command.Execute(_dataset)) return false;

        _undoStack.Push(command);
        _redoStack.Clear();
        OnChanged();
        return true;
    }

    public bool Undo() {
        if (!_undoStack.TryPop(out var command)) return false;

        command.Undo(_dataset);
        _redoStack.Push(command);
        OnChanged();
        return true;
    }

    public bool Redo() {
        if (!_redoStack.TryPop(out var command)) return false;

        if (!command.Execute(_dataset)) {
            OnChanged();
            return false;
        }

        _undoStack.Push(command);
        OnChanged();
        return true;
    }

    public void Clear() {
        if (_undoStack.Count == 0 && _redoStack.Count == 0) return;

        _undoStack.Clear();
        _redoStack.Clear();
        OnChanged();
    }

    private void OnChanged() {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
