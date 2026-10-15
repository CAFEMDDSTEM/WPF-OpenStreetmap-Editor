using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

/// <summary>
/// Suggests tag keys and values from the preset catalog and from tags already used in the document.
/// Prefix matches rank before substring matches, and document usage ranks before preset-only entries.
/// </summary>
public sealed class TagAutocompleteService {
    private readonly Dictionary<string, HashSet<string>> _presetValuesByKey = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<string> _presetKeys;

    public TagAutocompleteService(IReadOnlyList<TagPreset> presets) {
        ArgumentNullException.ThrowIfNull(presets);

        foreach (var preset in presets) {
            foreach (var (key, value) in preset.Tags) {
                AddPresetValue(key, value);
            }

            foreach (var field in preset.Fields) {
                var values = EnsureKey(field.Key);
                foreach (var choice in field.Choices ?? []) {
                    values.Add(choice.Value);
                }
                if (field.Kind == TagPresetFieldKind.Checkbox) {
                    values.Add("yes");
                    values.Add("no");
                }
            }
        }

        _presetKeys = _presetValuesByKey.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToList();
    }

    public IReadOnlyList<string> SuggestKeys(string? prefix, IEnumerable<MapFeature> documentFeatures, int limit = 12) {
        ArgumentNullException.ThrowIfNull(documentFeatures);

        // The document is scanned once per call; selections change far less often than suggestions are requested.
        var documentKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in documentFeatures) {
            foreach (var key in feature.Attributes.Keys) {
                documentKeys.Add(key);
            }
        }

        return Rank(documentKeys, _presetKeys, prefix, limit);
    }

    public IReadOnlyList<string> SuggestValues(string key, string? prefix, IEnumerable<MapFeature> documentFeatures, int limit = 12) {
        ArgumentNullException.ThrowIfNull(documentFeatures);
        if (string.IsNullOrEmpty(key)) return [];

        var documentValues = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in documentFeatures) {
            if (feature.Attributes.TryGetValue(key, out var value)) documentValues.Add(value);
        }

        var presetValues = _presetValuesByKey.TryGetValue(key, out var values) ? values : [];
        return Rank(documentValues, presetValues, prefix, limit);
    }

    private static IReadOnlyList<string> Rank(
        HashSet<string> documentCandidates,
        IEnumerable<string> presetCandidates,
        string? prefix,
        int limit) {
        if (limit <= 0) return [];

        var query = prefix ?? "";
        var matches = new List<Candidate>();
        foreach (var candidate in documentCandidates) {
            AddMatch(matches, candidate, query, inDocument: true);
        }
        foreach (var candidate in presetCandidates) {
            if (!documentCandidates.Contains(candidate)) AddMatch(matches, candidate, query, inDocument: false);
        }

        matches.Sort(static (left, right) => {
            var byTier = left.Tier.CompareTo(right.Tier);
            if (byTier != 0) return byTier;
            if (left.InDocument != right.InDocument) return left.InDocument ? -1 : 1;

            var byText = StringComparer.OrdinalIgnoreCase.Compare(left.Text, right.Text);
            return byText != 0 ? byText : StringComparer.Ordinal.Compare(left.Text, right.Text);
        });

        return matches
            .Take(limit)
            .Select(static match => match.Text)
            .ToList();
    }

    private static void AddMatch(List<Candidate> matches, string candidate, string query, bool inDocument) {
        if (string.IsNullOrWhiteSpace(candidate)) return;

        if (candidate.StartsWith(query, StringComparison.OrdinalIgnoreCase)) {
            matches.Add(new Candidate(candidate, Tier: 0, inDocument));
        } else if (candidate.Contains(query, StringComparison.OrdinalIgnoreCase)) {
            matches.Add(new Candidate(candidate, Tier: 1, inDocument));
        }
    }

    private void AddPresetValue(string key, string value) {
        if (!string.IsNullOrWhiteSpace(value)) EnsureKey(key).Add(value);
    }

    private HashSet<string> EnsureKey(string key) {
        if (!_presetValuesByKey.TryGetValue(key, out var values)) {
            values = new HashSet<string>(StringComparer.Ordinal);
            _presetValuesByKey[key] = values;
        }

        return values;
    }

    private readonly record struct Candidate(string Text, int Tier, bool InDocument);
}
