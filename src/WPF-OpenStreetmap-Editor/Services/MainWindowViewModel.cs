using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed class WorkbenchViewModel : INotifyPropertyChanged {
    private string _documentStatus = "";
    private string _editorModeStatus = "";
    private string _pointerStatus = "";
    private string _featureCount = "0";
    private string _activeLayerStatus = "";
    private bool _isRightPanelVisible = true;
    private double _rightPanelWidth = 380;

    public WorkbenchViewModel() {
        Selection.Changed += (_, _) => OnPropertyChanged(nameof(SelectedCount));
        EditorSession.CommandStack.Changed += (_, _) => {
            OnPropertyChanged(nameof(History));
            OnPropertyChanged(nameof(HistoryPosition));
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public EditorSession EditorSession { get; } = new();

    public SelectionService Selection { get; } = new();

    public int SelectedCount => Selection.Count;

    public IReadOnlyList<EditHistoryEntry> History => EditorSession.CommandStack.History;

    public int HistoryPosition => EditorSession.CommandStack.HistoryPosition;

    public string DocumentStatus {
        get => _documentStatus;
        set => SetField(ref _documentStatus, value);
    }

    public string EditorModeStatus {
        get => _editorModeStatus;
        set => SetField(ref _editorModeStatus, value);
    }

    public string PointerStatus {
        get => _pointerStatus;
        set => SetField(ref _pointerStatus, value);
    }

    public string FeatureCount {
        get => _featureCount;
        set => SetField(ref _featureCount, value);
    }

    public string ActiveLayerStatus {
        get => _activeLayerStatus;
        set => SetField(ref _activeLayerStatus, value);
    }

    public bool IsRightPanelVisible {
        get => _isRightPanelVisible;
        set => SetField(ref _isRightPanelVisible, value);
    }

    public double RightPanelWidth {
        get => _rightPanelWidth;
        set => SetField(ref _rightPanelWidth, Math.Clamp(value, 280, 640));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
