using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

/// <summary>
/// One tag editor row. <see cref="Value"/> is the value shared by every selected feature; it is null with
/// <see cref="IsDifferent"/> set when the values differ or some features lack the key.
/// </summary>
public sealed record TagEditorRow(string Key, string? Value, bool IsDifferent, int FeatureCount);

/// <summary>Builds tag editor rows for a selection and turns edits into undoable commands.</summary>
public static class TagEditorModel {
    public static IReadOnlyList<TagEditorRow> BuildRows(IReadOnlyList<MapFeature> features) {
        ArgumentNullException.ThrowIfNull(features);

        var builders = new Dictionary<string, RowBuilder>(StringComparer.Ordinal);
        foreach (var feature in features) {
            foreach (var (key, value) in feature.Attributes) {
                if (builders.TryGetValue(key, out var builder)) {
                    builder.Add(value);
                } else {
                    builders[key] = new RowBuilder(value);
                }
            }
        }

        return builders
            .OrderBy(static item => item.Key, StringComparer.Ordinal)
            .Select(item => item.Value.ToRow(item.Key, features.Count))
            .ToList();
    }

    /// <summary>Sets the key on every feature. A blank value removes the key instead, as in JOSM.</summary>
    public static IEditCommand? CreateSetCommand(IReadOnlyList<MapFeature> features, string key, string value) {
        ArgumentNullException.ThrowIfNull(features);
        if (features.Count == 0 || string.IsNullOrWhiteSpace(key)) return null;

        return new PatchFeaturesAttributesCommand(features, new Dictionary<string, string?>(StringComparer.Ordinal) {
            [key] = string.IsNullOrWhiteSpace(value) ? null : value
        });
    }

    public static IEditCommand? CreateRemoveCommand(IReadOnlyList<MapFeature> features, string key) {
        ArgumentNullException.ThrowIfNull(features);
        if (features.Count == 0 || string.IsNullOrWhiteSpace(key)) return null;

        return new PatchFeaturesAttributesCommand(features, new Dictionary<string, string?>(StringComparer.Ordinal) {
            [key] = null
        });
    }

    /// <summary>Moves each feature's own value from <paramref name="oldKey"/> to <paramref name="newKey"/>, overwriting any existing value there.</summary>
    public static IEditCommand? CreateRenameKeyCommand(IReadOnlyList<MapFeature> features, string oldKey, string newKey) {
        ArgumentNullException.ThrowIfNull(features);
        if (string.IsNullOrWhiteSpace(oldKey) || string.IsNullOrWhiteSpace(newKey) || oldKey == newKey) return null;

        var patches = new List<KeyValuePair<MapFeature, IReadOnlyDictionary<string, string?>>>();
        foreach (var feature in features) {
            if (!feature.Attributes.TryGetValue(oldKey, out var value)) continue;

            patches.Add(KeyValuePair.Create<MapFeature, IReadOnlyDictionary<string, string?>>(
                feature,
                new Dictionary<string, string?>(StringComparer.Ordinal) {
                    [oldKey] = null,
                    [newKey] = value
                }));
        }

        return patches.Count == 0 ? null : PatchFeaturesAttributesCommand.CreatePerFeature(patches);
    }

    public static bool RowsEqual(IReadOnlyList<TagEditorRow> left, IReadOnlyList<TagEditorRow> right) {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (ReferenceEquals(left, right)) return true;
        if (left.Count != right.Count) return false;

        for (var i = 0; i < left.Count; i++) {
            if (left[i] != right[i]) return false;
        }

        return true;
    }

    private sealed class RowBuilder {
        private string? _value;
        private bool _isDifferent;
        private int _count = 1;

        public RowBuilder(string value) {
            _value = value;
        }

        public void Add(string value) {
            _count++;
            if (_isDifferent || string.Equals(_value, value, StringComparison.Ordinal)) return;

            _isDifferent = true;
            _value = null;
        }

        public TagEditorRow ToRow(string key, int selectionCount) {
            var isDifferent = _isDifferent || _count < selectionCount;
            return new TagEditorRow(key, isDifferent ? null : _value, isDifferent, _count);
        }
    }
}
