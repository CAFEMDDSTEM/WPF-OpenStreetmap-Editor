using System.Text.Json;

namespace WPF_OpenStreetmap_Editor.Services;

public static class BetterIdAiTagSuggestionNormalizer {
    private const int MaxSuggestions = 8;
    private const int MaxSources = 4;
    private const int MaxWarnings = 4;
    private const int MaxSummaryChars = 300;
    private const int MaxReasonChars = 240;
    private const int MaxWarningChars = 160;
    private const int MaxSourceTitleChars = 160;
    private const int MaxSourceUrlChars = 2048;
    private const int MaxSourceSnippetChars = 240;

    private static readonly HashSet<string> BlockedTagKeys = new(StringComparer.OrdinalIgnoreCase) {
        "image", "source", "created_by", "attribution", "odbl", "import",
        "timestamp", "version", "changeset", "uid", "user", "visible"
    };

    private static readonly string[] BlockedTagPrefixes = ["source:", "tiger:", "odbl:", "metadata:"];

    public static BetterIdAiNormalizedTagSuggestionResult Normalize(
        BetterIdAiRawTagSuggestionResponse response,
        IReadOnlyDictionary<string, string> existingTags) {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(existingTags);

        var sources = NormalizeSources(response.Sources);
        var suggestions = new List<BetterIdAiNormalizedTagSuggestion>();
        foreach (var suggestion in (response.Suggestions ?? []).Take(64)) {
            if (suggestions.Count == MaxSuggestions) break;

            var key = CleanTagKey(suggestion.Key);
            if (string.IsNullOrWhiteSpace(key) || key.Length > 255 || IsBlockedTagKey(key)) continue;

            var action = string.Equals(suggestion.Action?.Trim(), "remove", StringComparison.OrdinalIgnoreCase)
                ? "remove"
                : "set";
            var value = action == "remove" ? "" : CleanTagValue(suggestion.Value);
            if (action == "set" && (string.IsNullOrWhiteSpace(value) || value.Length > 255)) continue;

            existingTags.TryGetValue(key, out var current);
            if ((action == "set" && current == value) || (action == "remove" && current is null)) continue;

            var confidenceScore = ReadConfidenceScore(suggestion.Confidence);
            var confidenceLabel = ConfidenceLabel(confidenceScore, suggestion.Confidence);
            var shouldSelect = confidenceScore.HasValue
                ? confidenceScore.Value >= 0.6
                : confidenceLabel != "low";
            suggestions.Add(new BetterIdAiNormalizedTagSuggestion(
                key,
                value,
                action,
                current,
                confidenceScore,
                confidenceLabel,
                LimitedText(suggestion.Reason, MaxReasonChars),
                NormalizeSourceUrls(suggestion.Sources),
                suggestion.Selected != false && shouldSelect && action != "remove"));
        }

        return new BetterIdAiNormalizedTagSuggestionResult(
            LimitedText(response.Summary, MaxSummaryChars),
            suggestions,
            sources,
            (response.Warnings ?? [])
                .Select(static warning => LimitedText(warning, MaxWarningChars))
                .Where(static warning => !string.IsNullOrWhiteSpace(warning))
                .Take(MaxWarnings)
                .ToList());
    }

    private static IReadOnlyList<BetterIdAiSuggestionSource> NormalizeSources(
        IReadOnlyList<BetterIdAiSuggestionSource>? sources) {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<BetterIdAiSuggestionSource>();
        foreach (var source in (sources ?? []).Take(32)) {
            if (result.Count == MaxSources) break;

            var rawUrl = LimitedText(source.Url, MaxSourceUrlChars);
            if (!IsHttpUrl(rawUrl, out var normalizedUrl) || !seen.Add(normalizedUrl)) continue;

            result.Add(new BetterIdAiSuggestionSource {
                Title = LimitedText(source.Title, MaxSourceTitleChars),
                Url = normalizedUrl,
                Snippet = LimitedText(source.Snippet, MaxSourceSnippetChars)
            });
        }

        return result;
    }

    private static IReadOnlyList<string> NormalizeSourceUrls(IReadOnlyList<string>? sources) {
        if (sources is null) return [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        return sources
            .Select(static source => LimitedText(source, MaxSourceUrlChars))
            .Where(source => IsHttpUrl(source, out _))
            .Select(source => {
                IsHttpUrl(source, out var normalizedUrl);
                return normalizedUrl;
            })
            .Where(seen.Add)
            .Take(MaxSources)
            .ToList();
    }

    private static double? ReadConfidenceScore(JsonElement confidence) {
        return confidence.ValueKind switch {
            JsonValueKind.Number when confidence.TryGetDouble(out var value) => Math.Clamp(value, 0.0, 1.0),
            JsonValueKind.String when double.TryParse(confidence.GetString(), out var value) => Math.Clamp(value, 0.0, 1.0),
            _ => null
        };
    }

    private static string ConfidenceLabel(double? score, JsonElement confidence) {
        if (score.HasValue) {
            if (score.Value >= 0.8) return "high";
            if (score.Value >= 0.5) return "medium";
            return "low";
        }

        if (confidence.ValueKind == JsonValueKind.String) {
            var label = confidence.GetString()?.Trim().ToLowerInvariant();
            if (label is "high" or "medium" or "low") return label;
        }

        return "low";
    }

    private static bool IsBlockedTagKey(string key) {
        return BlockedTagKeys.Contains(key) ||
            BlockedTagPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string CleanTagKey(string value) {
        return (value ?? "").Trim().Replace("=", "", StringComparison.Ordinal);
    }

    private static string CleanTagValue(string value) {
        return (value ?? "").Trim();
    }

    private static bool IsHttpUrl(string value, out string normalizedUrl) {
        normalizedUrl = "";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(uri.Host)) {
            return false;
        }

        normalizedUrl = uri.AbsoluteUri;
        return true;
    }

    private static string LimitedText(string? value, int maxChars) {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var text = value.Trim();
        return text.Length <= maxChars
            ? text
            : new string(text.Take(maxChars).ToArray());
    }
}
