namespace WPF_OpenStreetmap_Editor.Services;

public sealed class MainWindowViewModel {
    public EditorSession EditorSession { get; } = new();

    public SelectionService Selection { get; } = new();
}
