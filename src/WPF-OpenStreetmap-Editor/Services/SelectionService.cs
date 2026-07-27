using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed class SelectionService {
    private readonly HashSet<MapFeature> _features = [];

    public IReadOnlyCollection<MapFeature> Features => _features;

    public int Count => _features.Count;

    public bool Set(IEnumerable<MapFeature> features) {
        var next = features.Distinct().ToList();
        var changed = next.Count != _features.Count || next.Any(feature => !_features.Contains(feature));

        foreach (var feature in _features) {
            feature.IsSelected = false;
        }

        _features.Clear();
        foreach (var feature in next) {
            feature.IsSelected = true;
            _features.Add(feature);
        }

        return changed;
    }

    public void Clear() {
        Set([]);
    }
}
