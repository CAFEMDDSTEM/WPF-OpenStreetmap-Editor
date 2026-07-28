namespace WPF_OpenStreetmap_Editor.Services;

public interface IEditCommand {
    string Description { get; }

    bool Execute(MapEditDataset dataset);

    void Undo(MapEditDataset dataset);
}

public sealed record EditHistoryEntry(int Position, string Description, bool IsApplied);

public sealed class EditCommandStack {
    private readonly MapEditDataset _dataset;
    private readonly List<IEditCommand> _history = [];
    private int _position;

    public EditCommandStack(MapEditDataset dataset) {
        _dataset = dataset;
    }

    public event EventHandler? Changed;

    public bool CanUndo => _position > 0;

    public bool CanRedo => _position < _history.Count;

    public string? UndoDescription => CanUndo ? _history[_position - 1].Description : null;

    public string? RedoDescription => CanRedo ? _history[_position].Description : null;

    public int HistoryPosition => _position;

    public IReadOnlyList<EditHistoryEntry> History => _history
        .Select((command, index) => new EditHistoryEntry(index + 1, command.Description, index < _position))
        .ToList();

    public bool Execute(IEditCommand command) {
        ArgumentNullException.ThrowIfNull(command);
        if (!command.Execute(_dataset)) return false;

        if (_position < _history.Count) {
            _history.RemoveRange(_position, _history.Count - _position);
        }
        _history.Add(command);
        _position++;
        OnChanged();
        return true;
    }

    public bool Undo() {
        if (!CanUndo) return false;

        var command = _history[_position - 1];
        command.Undo(_dataset);
        _position--;
        OnChanged();
        return true;
    }

    public bool Redo() {
        if (!CanRedo) return false;

        var command = _history[_position];
        if (!command.Execute(_dataset)) {
            OnChanged();
            return false;
        }

        _position++;
        OnChanged();
        return true;
    }

    public bool MoveToHistoryPosition(int targetPosition) {
        if (targetPosition < 0 || targetPosition > _history.Count) {
            throw new ArgumentOutOfRangeException(nameof(targetPosition));
        }
        if (targetPosition == _position) return true;

        while (_position > targetPosition) {
            if (!Undo()) return false;
        }
        while (_position < targetPosition) {
            if (!Redo()) return false;
        }
        return true;
    }

    public void Clear() {
        if (_history.Count == 0) return;

        _history.Clear();
        _position = 0;
        OnChanged();
    }

    private void OnChanged() {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
