using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed class BetterIdAiClient {
    public const string DefaultBaseUrl = "https://map.osm.asia/api/osm-ai/";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly IReadOnlyList<string> DefaultTextProviderOrder = ["deepseek", "openai", "mimo"];
    private static readonly IReadOnlyList<string> DefaultSearchProviderOrder = ["openai", "kimi"];
    private readonly HttpClient _http;
    private readonly Uri _baseUri;

    public BetterIdAiClient(HttpClient http, string baseUrl = DefaultBaseUrl) {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _baseUri = new Uri(baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/");
    }

    public async Task<BetterIdAiStatus> GetStatusAsync(CancellationToken ct = default) {
        using var response = await _http.GetAsync(CreateUri("status"), ct).ConfigureAwait(false);
        return await ReadJsonAsync<BetterIdAiStatus>(response, ct).ConfigureAwait(false);
    }

    public Task<string> SummarizeChangesAsync(
        OsmAiChangesetSummary summary,
        CancellationToken ct = default) {
        return SummarizeChangesAsync(summary, DefaultTextProviderOrder, ct);
    }

    public async Task<string> SummarizeChangesAsync(
        OsmAiChangesetSummary summary,
        IReadOnlyList<string> providerOrder,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(summary);

        var response = await PostJsonAsync<BetterIdAiSummaryResponse>(
            "summarize",
            new BetterIdAiSummaryRequest(summary, providerOrder),
            ct).ConfigureAwait(false);
        var text = (response.Summary ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text)) {
            throw new BetterIdAiException("AI 生成的变更说明为空。");
        }

        return text;
    }

    public Task<BetterIdAiRawTagSuggestionResponse> GetTagSuggestionsAsync(
        BetterIdAiTagSuggestionRequest request,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(request);
        return PostJsonAsync<BetterIdAiRawTagSuggestionResponse>("tag-suggestions", request, ct);
    }

    public static BetterIdAiTagSuggestionRequest CreateTagSuggestionRequest(
        string description,
        IReadOnlyDictionary<string, string> tags,
        string geometry,
        BetterIdAiLocation? location,
        string locale = "zh-CN") {
        return new BetterIdAiTagSuggestionRequest {
            Description = LimitedText(description, 1200),
            Tags = tags
                .Where(static item =>
                    !string.IsNullOrWhiteSpace(item.Key) &&
                    !string.IsNullOrWhiteSpace(item.Value) &&
                    item.Key.Length <= 255 &&
                    item.Value.Length <= 255)
                .Take(100)
                .ToDictionary(static item => item.Key, static item => item.Value, StringComparer.Ordinal),
            Geometry = geometry,
            Location = location,
            Locale = locale,
            WebSearch = true,
            ProviderOrder = DefaultSearchProviderOrder,
            TextProviderOrder = DefaultTextProviderOrder
        };
    }

    private async Task<T> PostJsonAsync<T>(string path, object payload, CancellationToken ct) {
        using var response = await _http
            .PostAsJsonAsync(CreateUri(path), payload, JsonOptions, ct)
            .ConfigureAwait(false);
        return await ReadJsonAsync<T>(response, ct).ConfigureAwait(false);
    }

    private async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct) {
        if (!response.IsSuccessStatusCode) {
            var error = await TryReadErrorAsync(response, ct).ConfigureAwait(false);
            throw new BetterIdAiException(
                string.IsNullOrWhiteSpace(error) ? $"BetterID AI 请求失败，HTTP {(int)response.StatusCode}。" : error,
                response.StatusCode);
        }

        var value = await response.Content
            .ReadFromJsonAsync<T>(JsonOptions, ct)
            .ConfigureAwait(false);
        return value ?? throw new BetterIdAiException("BetterID AI 返回为空。", response.StatusCode);
    }

    private static async Task<string?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken ct) {
        try {
            var error = await response.Content
                .ReadFromJsonAsync<BetterIdAiError>(JsonOptions, ct)
                .ConfigureAwait(false);
            return error?.Error;
        } catch (JsonException) {
            return null;
        } catch (NotSupportedException) {
            return null;
        }
    }

    private Uri CreateUri(string path) => new(_baseUri, path);

    private static string LimitedText(string? value, int maxChars) {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var text = value.Trim();
        return text.Length <= maxChars
            ? text
            : new string(text.Take(maxChars).ToArray());
    }
}

public sealed class BetterIdAiException : Exception {
    public BetterIdAiException(string message, HttpStatusCode? statusCode = null) : base(message) {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}

public sealed record BetterIdAiStatus(
    bool Ai,
    bool Translate,
    bool Search,
    bool Visual,
    BetterIdAiProviders? Providers);

public sealed record BetterIdAiProviders(
    IReadOnlyList<string>? Text,
    IReadOnlyList<string>? Search,
    IReadOnlyList<string>? Visual);

public sealed record BetterIdAiLocation(double Lat, double Lon);

public sealed class BetterIdAiTagSuggestionRequest {
    public string Description { get; init; } = "";

    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public object? Geometry { get; init; }

    public BetterIdAiLocation? Location { get; init; }

    public string Locale { get; init; } = "zh-CN";

    [JsonPropertyName("web_search")]
    public bool WebSearch { get; init; } = true;

    [JsonPropertyName("provider_order")]
    public IReadOnlyList<string> ProviderOrder { get; init; } = ["openai", "kimi"];

    [JsonPropertyName("text_provider_order")]
    public IReadOnlyList<string> TextProviderOrder { get; init; } = ["deepseek", "openai", "mimo"];
}

public sealed class BetterIdAiRawTagSuggestionResponse {
    public string Summary { get; init; } = "";

    public IReadOnlyList<BetterIdAiRawTagSuggestion>? Suggestions { get; init; } = [];

    public IReadOnlyList<BetterIdAiSuggestionSource>? Sources { get; init; } = [];

    public IReadOnlyList<string>? Warnings { get; init; } = [];
}

public sealed class BetterIdAiRawTagSuggestion {
    public string Key { get; init; } = "";

    public string Value { get; init; } = "";

    public string Reason { get; init; } = "";

    public JsonElement Confidence { get; init; }

    public string? Action { get; init; }

    public IReadOnlyList<string>? Sources { get; init; }

    public bool? Selected { get; init; }
}

public sealed class BetterIdAiSuggestionSource {
    public string Title { get; init; } = "";

    public string Url { get; init; } = "";

    public string? Snippet { get; init; }
}

public sealed record BetterIdAiNormalizedTagSuggestion(
    string Key,
    string Value,
    string Action,
    string? CurrentValue,
    double? ConfidenceScore,
    string ConfidenceLabel,
    string Reason,
    IReadOnlyList<string> Sources,
    bool Selected) {
    public string ProposedText => Action == "remove" ? $"{Key} - 删除" : $"{Key}={Value}";
}

public sealed record BetterIdAiNormalizedTagSuggestionResult(
    string Summary,
    IReadOnlyList<BetterIdAiNormalizedTagSuggestion> Suggestions,
    IReadOnlyList<BetterIdAiSuggestionSource> Sources,
    IReadOnlyList<string> Warnings);

internal sealed record BetterIdAiSummaryRequest(
    OsmAiChangesetSummary Summary,
    [property: JsonPropertyName("provider_order")] IReadOnlyList<string> ProviderOrder);

internal sealed record BetterIdAiSummaryResponse(string? Summary);

internal sealed record BetterIdAiError(string? Error);
