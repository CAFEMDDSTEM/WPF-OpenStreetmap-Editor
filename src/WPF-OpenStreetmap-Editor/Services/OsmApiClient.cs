using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed class OsmApiClient(HttpClient httpClient) {
    public const string DefaultApiBaseUrl = "https://api.openstreetmap.org/";
    public const string DefaultOverpassMapUrl = "https://overpass-api.de/api/map";
    public const double MaximumDownloadArea = 0.25;
    public const double MaximumOverpassDownloadArea = 25;
    private const long MaximumDownloadBytes = 128L * 1024 * 1024;

    public async Task<byte[]> DownloadMapAsync(string apiBaseUrl, GeoBounds bounds, CancellationToken ct = default) {
        return await DownloadMapAsync(apiBaseUrl, bounds, progress: null, ct);
    }

    public async Task<byte[]> DownloadMapAsync(
        string apiBaseUrl,
        GeoBounds bounds,
        IProgress<OsmDownloadStage>? progress,
        CancellationToken ct = default) {
        ValidateDownloadBounds(bounds);
        var query = string.Join(',', new[] {
            bounds.MinLongitude,
            bounds.MinLatitude,
            bounds.MaxLongitude,
            bounds.MaxLatitude
        }.Select(static value => value.ToString("R", CultureInfo.InvariantCulture)));

        var area = GetArea(bounds);
        if (area > MaximumDownloadArea) {
            progress?.Report(OsmDownloadStage.OverpassFallback);
            return await DownloadResponseAsync(new Uri($"{DefaultOverpassMapUrl}?bbox={query}"), ct);
        }

        progress?.Report(OsmDownloadStage.StandardApi);
        try {
            return await DownloadResponseAsync(CreateUri(apiBaseUrl, $"api/0.6/map?bbox={query}"), ct);
        } catch (HttpRequestException ex) when (CanFallbackToOverpass(ex.StatusCode)) {
            Logger.Error("OSM map API rejected the selected bounds; retrying with Overpass", ex);
            progress?.Report(OsmDownloadStage.OverpassFallback);
            try {
                return await DownloadResponseAsync(new Uri($"{DefaultOverpassMapUrl}?bbox={query}"), ct);
            } catch (Exception fallbackError) when (fallbackError is not OperationCanceledException) {
                throw new OsmDownloadFallbackException(ex, fallbackError);
            }
        }
    }

    private async Task<byte[]> DownloadResponseAsync(Uri uri, CancellationToken ct) {
        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumDownloadBytes) {
            throw new SpatialDataLimitException("OSM 下载响应超过 128 MB 安全上限，请缩小框选区域。");
        }

        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true) {
            var read = await input.ReadAsync(buffer, ct);
            if (read == 0) break;
            if (output.Length + read > MaximumDownloadBytes) {
                throw new SpatialDataLimitException("OSM 下载响应超过 128 MB 安全上限，请缩小框选区域。");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return output.ToArray();
    }

    public async Task<string> GetUserDisplayNameAsync(
        string apiBaseUrl,
        string accessToken,
        CancellationToken ct = default) {
        return await GetUserDisplayNameAsync(
            apiBaseUrl,
            new OsmAccountCredential(OsmAuthenticationMethod.OAuth2, "", accessToken),
            ct);
    }

    public async Task<string> GetUserDisplayNameAsync(
        string apiBaseUrl,
        OsmAccountCredential credential,
        CancellationToken ct = default) {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, apiBaseUrl, "api/0.6/user/details", credential);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return xml.Descendants("user").FirstOrDefault()?.Attribute("display_name")?.Value ?? "已验证账号";
    }

    public async Task<long> CreateChangesetAsync(
        string apiBaseUrl,
        string accessToken,
        string comment,
        CancellationToken ct = default) {
        return await CreateChangesetAsync(
            apiBaseUrl,
            new OsmAccountCredential(OsmAuthenticationMethod.OAuth2, "", accessToken),
            comment,
            source: null,
            reviewRequested: false,
            ct);
    }

    public async Task<long> CreateChangesetAsync(
        string apiBaseUrl,
        OsmAccountCredential credential,
        string comment,
        CancellationToken ct = default) {
        return await CreateChangesetAsync(apiBaseUrl, credential, comment, source: null, reviewRequested: false, ct);
    }

    public async Task<long> CreateChangesetAsync(
        string apiBaseUrl,
        OsmAccountCredential credential,
        string comment,
        string? source = null,
        bool reviewRequested = false,
        CancellationToken ct = default) {
        var changeset = new XElement(
            "changeset",
            new XElement("tag", new XAttribute("k", "created_by"), new XAttribute("v", "WPF-OpenStreetmap-Editor")),
            new XElement("tag", new XAttribute("k", "comment"), new XAttribute("v", comment)));
        if (!string.IsNullOrWhiteSpace(source)) {
            changeset.Add(new XElement("tag", new XAttribute("k", "source"), new XAttribute("v", source.Trim())));
        }
        if (reviewRequested) {
            changeset.Add(new XElement("tag", new XAttribute("k", "review_requested"), new XAttribute("v", "yes")));
        }

        var body = new XDocument(new XElement("osm", changeset)).ToString();
        using var request = CreateAuthorizedRequest(HttpMethod.Put, apiBaseUrl, "api/0.6/changeset/create", credential, body);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var text = (await response.Content.ReadAsStringAsync(ct)).Trim();
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : throw new InvalidDataException("OSM API 返回了无效的变更集 ID。");
    }

    public async Task<string> UploadChangesAsync(
        string apiBaseUrl,
        string accessToken,
        long changesetId,
        string changeXml,
        CancellationToken ct = default) {
        return await UploadChangesAsync(
            apiBaseUrl,
            new OsmAccountCredential(OsmAuthenticationMethod.OAuth2, "", accessToken),
            changesetId,
            changeXml,
            ct);
    }

    public async Task<string> UploadChangesAsync(
        string apiBaseUrl,
        OsmAccountCredential credential,
        long changesetId,
        string changeXml,
        CancellationToken ct = default) {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Post,
            apiBaseUrl,
            $"api/0.6/changeset/{changesetId}/upload",
            credential,
            changeXml);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task CloseChangesetAsync(
        string apiBaseUrl,
        string accessToken,
        long changesetId,
        CancellationToken ct = default) {
        await CloseChangesetAsync(
            apiBaseUrl,
            new OsmAccountCredential(OsmAuthenticationMethod.OAuth2, "", accessToken),
            changesetId,
            ct);
    }

    public async Task CloseChangesetAsync(
        string apiBaseUrl,
        OsmAccountCredential credential,
        long changesetId,
        CancellationToken ct = default) {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Put,
            apiBaseUrl,
            $"api/0.6/changeset/{changesetId}/close",
            credential);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public static void ValidateBounds(GeoBounds bounds) {
        ValidateCoordinates(bounds);
        var area = GetArea(bounds);
        if (area > MaximumDownloadArea) {
            throw new InvalidDataException($"框选区域面积为 {area:0.####} 平方度，超过 OSM API 的 {MaximumDownloadArea:0.##} 平方度限制。");
        }
    }

    public static void ValidateDownloadBounds(GeoBounds bounds) {
        ValidateCoordinates(bounds);
        var area = GetArea(bounds);
        if (area > MaximumOverpassDownloadArea) {
            throw new InvalidDataException(
                $"框选区域面积为 {area:0.####} 平方度，超过可安全下载的 {MaximumOverpassDownloadArea:0.##} 平方度上限。请缩小范围后重试。");
        }
    }

    public static bool RequiresOverpassFallback(GeoBounds bounds) {
        ValidateCoordinates(bounds);
        return GetArea(bounds) > MaximumDownloadArea;
    }

    private static void ValidateCoordinates(GeoBounds bounds) {
        if (!bounds.IsValid || bounds.MinLongitude < -180 || bounds.MaxLongitude > 180 ||
            bounds.MinLatitude < -90 || bounds.MaxLatitude > 90 ||
            bounds.MinLongitude == bounds.MaxLongitude || bounds.MinLatitude == bounds.MaxLatitude) {
            throw new InvalidDataException("OSM 下载范围无效。");
        }
    }

    private static double GetArea(GeoBounds bounds) {
        return (bounds.MaxLongitude - bounds.MinLongitude) * (bounds.MaxLatitude - bounds.MinLatitude);
    }

    private static bool CanFallbackToOverpass(HttpStatusCode? statusCode) {
        return statusCode is HttpStatusCode.BadRequest or
            HttpStatusCode.RequestEntityTooLarge or
            HttpStatusCode.RequestUriTooLong;
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string apiBaseUrl,
        string relativePath,
        OsmAccountCredential credential,
        string? body = null) {
        var request = new HttpRequestMessage(method, CreateUri(apiBaseUrl, relativePath));
        credential.ApplyTo(request);
        request.Headers.UserAgent.ParseAdd("WPF-OpenStreetmap-Editor/0.1");
        if (body is not null) request.Content = new StringContent(body, new UTF8Encoding(false), "text/xml");
        return request;
    }

    private static Uri CreateUri(string apiBaseUrl, string relativePath) {
        var account = new OsmAccount { DisplayName = "API", ApiBaseUrl = apiBaseUrl };
        OsmAccountStore.Validate(account);
        var baseUri = new Uri(apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/");
        return new Uri(baseUri, relativePath);
    }
}

public enum OsmDownloadStage {
    StandardApi,
    OverpassFallback,
    Importing
}

public sealed class OsmDownloadFallbackException(Exception standardApiError, Exception fallbackError)
    : Exception("OSM 标准接口和 Overpass 回退接口均未能下载该区域。", fallbackError) {
    public Exception StandardApiError { get; } = standardApiError;
}
