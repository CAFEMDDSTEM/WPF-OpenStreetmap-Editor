using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

/// <summary>
/// Applies a tag patch to many features in one undoable step. A null value removes the key and blank keys
/// are ignored. Undo restores each changed feature's previous tags exactly.
/// </summary>
public sealed class PatchFeaturesAttributesCommand : IEditCommand {
    private readonly IReadOnlyList<FeaturePatch> _patches;
    private IReadOnlyList<PreviousTags> _previousStates = [];
    private MapDirtyState? _dirtyState;

    public PatchFeaturesAttributesCommand(IEnumerable<MapFeature> features, IReadOnlyDictionary<string, string?> patch) {
        var sharedPatch = CopyPatch(patch);
        _patches = features
            .Distinct()
            .Select(feature => new FeaturePatch(feature, sharedPatch))
            .ToList();
    }

    private PatchFeaturesAttributesCommand(IReadOnlyList<FeaturePatch> patches) {
        _patches = patches;
    }

    /// <summary>Creates a command with a separate patch per feature, e.g. to move each feature's own value to a new key.</summary>
    public static PatchFeaturesAttributesCommand CreatePerFeature(
        IEnumerable<KeyValuePair<MapFeature, IReadOnlyDictionary<string, string?>>> patches) {
        return new PatchFeaturesAttributesCommand(patches
            .DistinctBy(static item => item.Key)
            .Select(static item => new FeaturePatch(item.Key, CopyPatch(item.Value)))
            .ToList());
    }

    public string Description => "Update feature tags";

    public bool Execute(MapEditDataset dataset) {
        if (dataset.Document is null) return false;

        _dirtyState = dataset.CaptureDirtyState();
        _previousStates = _patches
            .Where(item => dataset.Contains(item.Feature) && HasChanges(item))
            .Select(static item => new PreviousTags(
                item,
                new Dictionary<string, string>(item.Feature.Attributes, StringComparer.Ordinal)))
            .ToList();
        if (_previousStates.Count == 0) return false;

        foreach (var state in _previousStates) {
            Apply(state.Patch);
        }
        dataset.MarkContentChanged(_previousStates.Select(static state => state.Patch.Feature));
        return true;
    }

    public void Undo(MapEditDataset dataset) {
        if (_previousStates.Count == 0) return;

        foreach (var state in _previousStates) {
            var attributes = state.Patch.Feature.Attributes;
            attributes.Clear();
            foreach (var tag in state.Tags) attributes[tag.Key] = tag.Value;
        }
        dataset.MarkContentChanged(_previousStates.Select(static state => state.Patch.Feature));
        dataset.RestoreDirty(_dirtyState);
    }

    private static bool HasChanges(FeaturePatch item) {
        foreach (var (key, value) in item.Patch) {
            var hasCurrent = item.Feature.Attributes.TryGetValue(key, out var current);
            if (value is null) {
                if (hasCurrent) return true;
            } else if (!hasCurrent || current != value) {
                return true;
            }
        }

        return false;
    }

    private static void Apply(FeaturePatch item) {
        foreach (var (key, value) in item.Patch) {
            if (value is null) {
                item.Feature.Attributes.Remove(key);
            } else {
                item.Feature.Attributes[key] = value;
            }
        }
    }

    private static Dictionary<string, string?> CopyPatch(IReadOnlyDictionary<string, string?> patch) {
        var copy = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in patch) {
            if (!string.IsNullOrWhiteSpace(key)) copy[key] = value;
        }

        return copy;
    }

    private sealed record FeaturePatch(MapFeature Feature, IReadOnlyDictionary<string, string?> Patch);

    private sealed record PreviousTags(FeaturePatch Patch, IReadOnlyDictionary<string, string> Tags);
}
