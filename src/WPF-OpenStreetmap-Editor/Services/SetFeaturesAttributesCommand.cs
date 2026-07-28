using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed class SetFeaturesAttributesCommand : IEditCommand {
    private readonly IReadOnlyList<MapFeature> _features;
    private readonly Dictionary<string, string> _attributes;
    private IReadOnlyList<FeatureAttributeState> _previousStates = [];
    private MapDirtyState? _dirtyState;

    public SetFeaturesAttributesCommand(
        IEnumerable<MapFeature> features,
        IReadOnlyDictionary<string, string> attributes) {
        _features = features.Distinct().ToList();
        _attributes = new Dictionary<string, string>(attributes, StringComparer.Ordinal);
    }

    public string Description => "Paste feature tags";

    public bool Execute(MapEditDataset dataset) {
        if (dataset.Document is null || _attributes.Count == 0) return false;

        _dirtyState = dataset.CaptureDirtyState();
        _previousStates = _features
            .Where(dataset.Contains)
            .Where(HasChanges)
            .Select(static feature => new FeatureAttributeState(
                feature,
                new Dictionary<string, string>(feature.Attributes, StringComparer.Ordinal)))
            .ToList();
        if (_previousStates.Count == 0) return false;

        foreach (var state in _previousStates) {
            foreach (var attribute in _attributes) {
                state.Feature.Attributes[attribute.Key] = attribute.Value;
            }
        }
        dataset.MarkContentChanged(_previousStates.Select(static state => state.Feature));
        return true;
    }

    public void Undo(MapEditDataset dataset) {
        foreach (var state in _previousStates) {
            state.Feature.Attributes.Clear();
            foreach (var attribute in state.Attributes) {
                state.Feature.Attributes[attribute.Key] = attribute.Value;
            }
        }

        if (_previousStates.Count == 0) return;
        dataset.MarkContentChanged(_previousStates.Select(static state => state.Feature));
        dataset.RestoreDirty(_dirtyState);
    }

    private bool HasChanges(MapFeature feature) {
        foreach (var attribute in _attributes) {
            if (!feature.Attributes.TryGetValue(attribute.Key, out var existingValue) ||
                existingValue != attribute.Value) {
                return true;
            }
        }

        return false;
    }

    private sealed record FeatureAttributeState(
        MapFeature Feature,
        IReadOnlyDictionary<string, string> Attributes);
}
