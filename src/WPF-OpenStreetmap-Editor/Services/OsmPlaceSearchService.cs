using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed record OsmPlaceSearchResult(string DisplayName, GeoPoint Center, GeoBounds? Bounds);

public sealed class OsmPlaceSearchService : IDisposable {
    private const string SearchEndpoint = "https://nominatim.openstreetmap.org/search";
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public OsmPlaceSearchService(HttpClient? http = null) {
        _ownsHttpClient = http is null;
        _http = http ?? new HttpClient {
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    public async Task<OsmPlaceSearchResult?> SearchAsync(string query, CancellationToken ct = default) {
        query = query.Trim();
        if (query.Length == 0) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildSearchUri(query));
        request.Headers.UserAgent.ParseAdd("WPF-OpenStreetmap-Editor/1.0");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var results = await JsonSerializer.DeserializeAsync<List<NominatimSearchResult>>(stream, JsonOptions, ct);
        var result = results?.FirstOrDefault();
        if (result is null) return null;

        if (!TryParseCoordinate(result.Latitude, out var latitude) ||
            !TryParseCoordinate(result.Longitude, out var longitude)) {
            return null;
        }

        GeoBounds? bounds = null;
        if (result.BoundingBox is { Count: 4 }) {
            var values = result.BoundingBox;
            if (TryParseCoordinate(values[0], out var south) &&
                TryParseCoordinate(values[1], out var north) &&
                TryParseCoordinate(values[2], out var west) &&
                TryParseCoordinate(values[3], out var east)) {
                bounds = new GeoBounds(west, south, east, north);
            }
        }

        return new OsmPlaceSearchResult(
            string.IsNullOrWhiteSpace(result.DisplayName) ? query : result.DisplayName,
            new GeoPoint(longitude, latitude),
            bounds);
    }

    public void Dispose() {
        if (_ownsHttpClient) {
            _http.Dispose();
        }
    }

    private static Uri BuildSearchUri(string query) {
        var language = Uri.EscapeDataString(CultureInfo.CurrentUICulture.Name);
        var escapedQuery = Uri.EscapeDataString(query);
        return new Uri(
            $"{SearchEndpoint}?format=jsonv2&addressdetails=1&limit=1&q={escapedQuery}&accept-language={language}",
            UriKind.Absolute);
    }

    private static bool TryParseCoordinate(string? value, out double result) {
        return double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);
    }

    private sealed record NominatimSearchResult(
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("lat")] string? Latitude,
        [property: JsonPropertyName("lon")] string? Longitude,
        [property: JsonPropertyName("boundingbox")] List<string>? BoundingBox);
}
