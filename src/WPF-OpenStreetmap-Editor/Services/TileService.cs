using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WPF_OpenStreetmap_Editor.Services;

public partial class TileService : IDisposable {
    private const int DefaultMaxConnectionsPerServer = 16;
    private const string BingMetadataEndpoint = "https://dev.virtualearth.net/REST/v1/Imagery/Metadata/Aerial";
    private const string BingTermsUrl = "https://www.microsoft.com/maps/product/terms.html";
    private const string NoTileExtension = ".notile";
    private const string DefaultUserAgent = "WPF-OpenStreetmap-Editor/1.0";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2);
    private const int WriteLockStripeCount = 256;
    private readonly HttpClient _http;
    private readonly string _cacheRoot;
    private readonly bool _ownsHttpClient;
    private readonly TimeSpan _cacheMaxAge;
    private readonly HashSet<string> _noTileEtags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _noTileMd5s = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _sourceInitializationLock = new(1, 1);
    private IReadOnlyList<BingImageryProvider> _bingImageryProviders = [];
    private string _bingCopyright = "";
    private bool _sourceInitialized;
    private bool _disposed;

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp"];
    private static readonly SemaphoreSlim[] WriteLocks = Enumerable.Range(0, WriteLockStripeCount)
        .Select(static _ => new SemaphoreSlim(1, 1))
        .ToArray();

    public string? TileTemplate { get; set; }
    public bool IsTms { get; set; }
    public int ImageMinZoom { get; private set; } = GeoConverter.MinZoom;
    public int MapMaxZoom { get; private set; } = GeoConverter.MaxZoom;
    public int ImageMaxZoom { get; private set; } = GeoConverter.MaxZoom;
    public int MaxZoom => ImageMaxZoom;
    public bool IsMaxZoomAuto { get; private set; }
    public bool IsBing { get; private set; }
    public string CacheIdentity => CreateCacheIdentity(IsBing ? "bing:aerial" : TileTemplate, IsTms);

    public TileService(HttpClient? http = null, string? cacheRoot = null, TimeSpan? cacheMaxAge = null) {
        _ownsHttpClient = http is null;
        _http = http ?? CreateDefaultHttpClient();
        EnsureDefaultHeaders(_http);
        _cacheRoot = AppPaths.Normalize(cacheRoot ?? AppPaths.TileCacheDirectory);
        _cacheMaxAge = cacheMaxAge ?? TileDiskCache.DefaultMaxAge;
        if (string.Equals(_cacheRoot, AppPaths.TileCacheDirectory, StringComparison.OrdinalIgnoreCase)) {
            TileDiskCache.ScheduleMaintenance(_cacheRoot, TileDiskCache.DefaultMaxBytes, _cacheMaxAge);
        }
    }

    public string BuildTileUrl(int z, int x, int y, string? accessToken) {
        if (string.IsNullOrEmpty(TileTemplate))
            throw new InvalidOperationException("Tile template is not set");
        if (z < ImageMinZoom || z > ImageMaxZoom) return string.Empty;

        var n = 1 << z;
        var xWrapped = ((x % n) + n) % n;
        var yForUrl = IsTms ? (n - 1) - y : y;

        if (yForUrl < 0 || yForUrl >= n)
            return string.Empty;

        return TileUrlTemplateExpander.Expand(TileTemplate, z, xWrapped, yForUrl, accessToken);
    }

    public async Task InitializeSourceAsync(string? accessToken, CancellationToken ct = default) {
        if (!IsBing || _sourceInitialized) return;
        if (string.IsNullOrWhiteSpace(accessToken)) {
            throw new InvalidOperationException("Bing aerial imagery requires a user-supplied Bing Maps key.");
        }

        await _sourceInitializationLock.WaitAsync(ct).ConfigureAwait(false);
        try {
            if (_sourceInitialized) return;

            var metadata = await LoadBingMetadataAsync(accessToken, ct).ConfigureAwait(false);
            TileTemplate = metadata.TileTemplate;
            ImageMinZoom = Math.Max(ImageMinZoom, metadata.MinZoom);
            ImageMaxZoom = Math.Min(ImageMaxZoom, metadata.MaxZoom);
            if (ImageMinZoom > ImageMaxZoom) {
                throw new InvalidDataException("Bing metadata returned an invalid zoom range.");
            }

            _bingCopyright = metadata.Copyright;
            _bingImageryProviders = metadata.ImageryProviders;
            _sourceInitialized = true;
        } finally {
            _sourceInitializationLock.Release();
        }
    }

    public IReadOnlyList<TileAttribution> GetAttributions(
        int zoom,
        double south,
        double west,
        double north,
        double east) {
        if (!IsBing || !_sourceInitialized) return [];

        List<TileAttribution> attributions = [];
        if (!string.IsNullOrWhiteSpace(_bingCopyright)) {
            attributions.Add(new TileAttribution(_bingCopyright, BingTermsUrl));
        }

        foreach (var provider in _bingImageryProviders) {
            if (string.IsNullOrWhiteSpace(provider.Attribution) ||
                !provider.AppliesTo(zoom, south, west, north, east)) {
                continue;
            }

            var attribution = new TileAttribution(provider.Attribution, "");
            if (!attributions.Contains(attribution)) {
                attributions.Add(attribution);
            }
        }

        return attributions;
    }

    public void ApplySourceOptions(
        int mapMaxZoom,
        int imageMaxZoom,
        IEnumerable<string>? noTileEtags = null,
        IEnumerable<string>? noTileMd5s = null) {
        MapMaxZoom = Math.Clamp(mapMaxZoom, GeoConverter.MinZoom, GeoConverter.MaxZoom);
        ImageMaxZoom = Math.Clamp(imageMaxZoom, GeoConverter.MinZoom, GeoConverter.MaxZoom);

        _noTileEtags.Clear();
        _noTileMd5s.Clear();
        foreach (var etag in noTileEtags ?? []) {
            var normalized = NormalizeSignature(etag);
            if (!string.IsNullOrEmpty(normalized)) _noTileEtags.Add(normalized);
        }

        foreach (var md5 in noTileMd5s ?? []) {
            var normalized = NormalizeSignature(md5);
            if (!string.IsNullOrEmpty(normalized)) _noTileMd5s.Add(normalized);
        }
    }

    public string GetCacheBasePath(int z, int x, int y) {
        return GetCacheBasePath(z, x, y, createDirectory: true);
    }

    private string GetCacheBasePath(int z, int x, int y, bool createDirectory) {
        var n = 1 << z;
        var xWrapped = ((x % n) + n) % n;

        var dir = Path.Combine(_cacheRoot, CacheIdentity, z.ToString(), xWrapped.ToString());
        if (createDirectory) {
            Directory.CreateDirectory(dir);
        }

        return Path.Combine(dir, y.ToString());
    }

    public string? FindCachedFile(int z, int x, int y) {
        var basePath = GetCacheBasePath(z, x, y, createDirectory: false);
        foreach (var ext in ImageExtensions) {
            var path = basePath + ext;
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    public string? FindNoTileMarker(int z, int x, int y) {
        var path = GetCacheBasePath(z, x, y, createDirectory: false) + NoTileExtension;
        return File.Exists(path) ? path : null;
    }

    public byte[]? TryReadCachedTile(int z, int x, int y) {
        try {
            var cached = FindCachedFile(z, x, y);
            if (cached is null) return null;

            var info = new FileInfo(cached);
            if (info.Length <= 0 || info.Length > TileImageValidator.MaxResponseBytes) return null;
            var bytes = File.ReadAllBytes(cached);
            return TileImageValidator.TryValidateCachedFile(bytes) ? bytes : null;
        } catch (Exception ex) {
            Logger.Error($"Failed to read tile cache (z={z}, x={x}, y={y})", ex);
            return null;
        }
    }

    public async Task<byte[]?> TryReadCachedTileAsync(int z, int x, int y, CancellationToken ct = default) {
        try {
            var cached = FindCachedFile(z, x, y);
            if (cached is null) return null;

            await using var stream = new FileStream(
                cached,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length <= 0 || stream.Length > TileImageValidator.MaxResponseBytes) return null;

            var bytes = new byte[(int)stream.Length];
            var offset = 0;
            while (offset < bytes.Length) {
                var read = await stream.ReadAsync(bytes.AsMemory(offset), ct).ConfigureAwait(false);
                if (read == 0) break;

                offset += read;
            }

            var result = offset == bytes.Length ? bytes : bytes[..offset];
            return TileImageValidator.TryValidateCachedFile(result) ? result : null;
        } catch (OperationCanceledException) {
            return null;
        } catch (Exception ex) {
            Logger.Error($"Failed to read tile cache (z={z}, x={x}, y={y})", ex);
            return null;
        }
    }

    public async Task<byte[]?> GetTileBytesAsync(int z, int x, int y, string? accessToken, CancellationToken ct = default) {
        try {
            if (string.IsNullOrEmpty(TileTemplate)) return null;
            if (z < ImageMinZoom || z > ImageMaxZoom) return null;
            if (FindNoTileMarker(z, x, y) is not null) return null;

            var cachedBytes = await TryReadCachedTileAsync(z, x, y, ct).ConfigureAwait(false);
            if (cachedBytes is not null) return cachedBytes;

            var n = 1 << z;
            var xWrapped = ((x % n) + n) % n;
            var cacheKey = $"{CacheIdentity}/{z}/{xWrapped}/{y}";
            var semaphore = GetWriteLock(cacheKey);
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try {
                if (FindNoTileMarker(z, x, y) is not null) return null;

                cachedBytes = await TryReadCachedTileAsync(z, x, y, ct).ConfigureAwait(false);
                if (cachedBytes is not null) return cachedBytes;

                var url = BuildTileUrl(z, x, y, accessToken);
                if (string.IsNullOrEmpty(url)) return null;

                Logger.Log(url, "START");
                using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                Logger.Log(url, resp.StatusCode.ToString());
                if (!resp.IsSuccessStatusCode) return null;

                var bytes = await ReadContentWithinLimitAsync(resp.Content, ct).ConfigureAwait(false);
                if (bytes is null) {
                    Logger.Error($"Rejected oversized tile response (z={z}, x={x}, y={y})");
                    return null;
                }
                if (IsNoTileResponse(resp, bytes)) {
                    Logger.Log(url, "NO_TILE");
                    MarkNoTile(z, x, y);
                    return null;
                }

                var mediaType = resp.Content.Headers.ContentType?.MediaType;
                if (!TileImageValidator.TryValidate(bytes, mediaType, out var ext)) {
                    Logger.Error($"Rejected invalid tile image (z={z}, x={x}, y={y}, mediaType={mediaType ?? "missing"})");
                    return null;
                }

                var cachePath = GetCacheBasePath(z, x, y) + ext;
                try {
                    File.WriteAllBytes(cachePath, bytes);
                    if (string.Equals(_cacheRoot, AppPaths.TileCacheDirectory, StringComparison.OrdinalIgnoreCase)) {
                        TileDiskCache.ScheduleMaintenance(_cacheRoot, TileDiskCache.DefaultMaxBytes, _cacheMaxAge);
                    }
                } catch (Exception ex) {
                    Logger.Error("Failed to write tile cache", ex);
                }

                return bytes;
            } finally {
                semaphore.Release();
            }
        } catch (OperationCanceledException) {
            return null;
        } catch (Exception ex) {
            Logger.Error($"GetTileBytesAsync failed (z={z}, x={x}, y={y})", ex);
            return null;
        }
    }

    private void MarkNoTile(int z, int x, int y) {
        try {
            var markerPath = GetCacheBasePath(z, x, y) + NoTileExtension;
            File.WriteAllText(markerPath, "No tile at this zoom level", Encoding.UTF8);
        } catch (Exception ex) {
            Logger.Error($"Failed to write no-tile marker (z={z}, x={x}, y={y})", ex);
        }
    }

    private bool IsNoTileResponse(HttpResponseMessage response, byte[] bytes) {
        var etag = NormalizeSignature(response.Headers.ETag?.Tag);
        if (!string.IsNullOrEmpty(etag) && _noTileEtags.Contains(etag)) {
            return true;
        }

        if (_noTileMd5s.Count == 0) return false;

        var md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
        return _noTileMd5s.Contains(md5);
    }

    private static SemaphoreSlim GetWriteLock(string cacheKey) {
        var hash = (uint)StringComparer.Ordinal.GetHashCode(cacheKey);
        return WriteLocks[hash % WriteLockStripeCount];
    }

    private static async Task<byte[]?> ReadContentWithinLimitAsync(
        HttpContent content,
        CancellationToken ct) {
        if (content.Headers.ContentLength is > TileImageValidator.MaxResponseBytes) return null;

        await using var source = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true) {
            var read = await source.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0) break;
            if (destination.Length + read > TileImageValidator.MaxResponseBytes) return null;

            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        return destination.Length == 0 ? null : destination.ToArray();
    }

    public async Task ResolveAutoMaxZoomAsync(
        double sampleLat,
        double sampleLon,
        string? accessToken,
        CancellationToken ct = default) {
        if (!IsMaxZoomAuto || string.IsNullOrEmpty(TileTemplate)) return;

        var metadataMaxZoom = await TryDetectArcGisMaxZoomAsync(ct).ConfigureAwait(false);
        var upperZoom = metadataMaxZoom ?? GeoConverter.MaxZoom;
        var detectedZoom = await ProbeMaxAvailableZoomAsync(upperZoom, sampleLat, sampleLon, accessToken, ct)
            .ConfigureAwait(false);

        ImageMaxZoom = detectedZoom ?? upperZoom;
        IsMaxZoomAuto = false;
    }

    public void ParseUrlTemplate(string url, string? accessToken, string? layerType = null) {
        if (string.IsNullOrEmpty(url))
            return;

        var source = TileSourceDefinition.Parse(url, layerType);
        var template = source.Template;
        ImageMinZoom = source.ImageMinZoom;
        ImageMaxZoom = source.ImageMaxZoom;
        MapMaxZoom = GeoConverter.MaxZoom;
        IsMaxZoomAuto = source.IsMaxZoomAuto;
        IsTms = false;
        IsBing = source.IsBing;
        _sourceInitialized = false;
        _bingCopyright = "";
        _bingImageryProviders = [];

        if (IsBing) {
            TileTemplate = null;
            return;
        }

        if (template.IndexOf("{-y}", StringComparison.OrdinalIgnoreCase) >= 0) {
            var xPos = template.IndexOf("{x}", StringComparison.OrdinalIgnoreCase);
            var negYPos = template.IndexOf("{-y}", StringComparison.OrdinalIgnoreCase);
            // TMS:  {z}/{x}/{-y}  → {-y} after {x}, flip Y
            // ArcGIS: {z}/{-y}/{x} → {-y} before {x}, NO flip
            IsTms = xPos >= 0 && negYPos > xPos;
            template = template.Replace("{-y}", "{y}");
        } else if (source.ForceTmsYFlip && IsXBeforeY(template)) {
            IsTms = true;
        }

        template = template.Replace("{zoom}", "{z}");

        if (template.IndexOf("tilematrix", StringComparison.OrdinalIgnoreCase) >= 0 ||
            template.IndexOf("tilecol", StringComparison.OrdinalIgnoreCase) >= 0 ||
            template.IndexOf("tilerow", StringComparison.OrdinalIgnoreCase) >= 0) {
            template = Regex.Replace(template, @"\{?TileMatrix\}?", "{z}", RegexOptions.IgnoreCase);
            template = Regex.Replace(template, @"\{?TileCol\}?", "{x}", RegexOptions.IgnoreCase);
            template = Regex.Replace(template, @"\{?TileRow\}?", "{y}", RegexOptions.IgnoreCase);
        }

        TileTemplate = template;
    }

    private async Task<BingMetadata> LoadBingMetadataAsync(string accessToken, CancellationToken ct) {
        var metadataUrl = $"{BingMetadataEndpoint}?include=ImageryProviders&output=json&uriScheme=https&key={Uri.EscapeDataString(accessToken)}";
        try {
            using var response = await _http
                .GetAsync(metadataUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                throw new InvalidOperationException($"Bing metadata request failed with HTTP {(int)response.StatusCode}.");
            }

            var bytes = await ReadContentWithinLimitAsync(response.Content, ct).ConfigureAwait(false);
            if (bytes is null) {
                throw new InvalidDataException("Bing metadata response was empty or too large.");
            }

            return BingMetadataParser.Parse(bytes);
        } catch (OperationCanceledException) {
            throw;
        } catch (InvalidDataException) {
            throw;
        } catch (InvalidOperationException) {
            throw;
        } catch (Exception ex) {
            Logger.Error("Failed to load Bing imagery metadata", ex);
            throw new InvalidOperationException("Bing imagery metadata could not be loaded.");
        }
    }

    private async Task<int?> TryDetectArcGisMaxZoomAsync(CancellationToken ct) {
        var metadataUrl = TryBuildArcGisMetadataUrl();
        if (metadataUrl is null) return null;

        try {
            using var response = await _http.GetAsync(metadataUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var bytes = await ReadContentWithinLimitAsync(response.Content, ct).ConfigureAwait(false);
            if (bytes is null) return null;

            using var json = JsonDocument.Parse(bytes);
            if (!json.RootElement.TryGetProperty("tileInfo", out var tileInfo) ||
                !tileInfo.TryGetProperty("lods", out var lods) ||
                lods.ValueKind != JsonValueKind.Array) {
                return null;
            }

            int? maxZoom = null;
            foreach (var lod in lods.EnumerateArray()) {
                if (!lod.TryGetProperty("level", out var levelElement) ||
                    !levelElement.TryGetInt32(out var level)) {
                    continue;
                }

                maxZoom = Math.Max(maxZoom ?? level, level);
            }

            return maxZoom is { } zoom
                ? Math.Clamp(zoom, GeoConverter.MinZoom, GeoConverter.MaxZoom)
                : null;
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            Logger.Error($"Failed to detect ArcGIS max zoom from {metadataUrl}", ex);
            return null;
        }
    }

    private string? TryBuildArcGisMetadataUrl() {
        if (string.IsNullOrEmpty(TileTemplate)) return null;

        var template = TileUrlTemplateExpander.ApplySubdomain(TileTemplate, 0, 0);
        var tileIndex = template.IndexOf("/tile/", StringComparison.OrdinalIgnoreCase);
        if (tileIndex < 0) return null;

        return template[..tileIndex] + "?f=pjson";
    }

    private async Task<int?> ProbeMaxAvailableZoomAsync(
        int upperZoom,
        double sampleLat,
        double sampleLon,
        string? accessToken,
        CancellationToken ct) {
        upperZoom = Math.Clamp(upperZoom, GeoConverter.MinZoom, GeoConverter.MaxZoom);
        for (var zoom = upperZoom; zoom >= GeoConverter.MinZoom; zoom--) {
            var (tileX, tileY) = GetSampleTile(sampleLat, sampleLon, zoom);
            if (await IsTileAvailableAsync(zoom, tileX, tileY, accessToken, ct).ConfigureAwait(false)) {
                return zoom;
            }
        }

        return null;
    }

    private async Task<bool> IsTileAvailableAsync(
        int z,
        int x,
        int y,
        string? accessToken,
        CancellationToken ct) {
        var url = BuildTileUrl(z, x, y, accessToken);
        if (string.IsNullOrEmpty(url)) return false;

        try {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return false;

            var bytes = await ReadContentWithinLimitAsync(response.Content, ct).ConfigureAwait(false);
            if (bytes is null) return false;
            if (IsNoTileResponse(response, bytes)) {
                MarkNoTile(z, x, y);
                return false;
            }

            return TileImageValidator.TryValidate(
                bytes,
                response.Content.Headers.ContentType?.MediaType,
                out _);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            Logger.Error($"Failed to probe tile availability (z={z}, x={x}, y={y})", ex);
            return false;
        }
    }

    private static (int X, int Y) GetSampleTile(double lat, double lon, int zoom) {
        var n = GeoConverter.GetTileCount(zoom);
        var (pixelX, pixelY) = GeoConverter.LatLonToPixelXY(lat, lon, zoom);
        var tileX = (int)Math.Clamp(Math.Floor(pixelX / GeoConverter.TileSize), 0, n - 1);
        var tileY = (int)Math.Clamp(Math.Floor(pixelY / GeoConverter.TileSize), 0, n - 1);
        return (tileX, tileY);
    }

    private static bool IsXBeforeY(string template) {
        var xPos = template.IndexOf("{x}", StringComparison.OrdinalIgnoreCase);
        var yPos = template.IndexOf("{y}", StringComparison.OrdinalIgnoreCase);
        return xPos >= 0 && yPos > xPos;
    }

    private static HttpClient CreateDefaultHttpClient() {
        var handler = new SocketsHttpHandler {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            MaxConnectionsPerServer = DefaultMaxConnectionsPerServer,
            PooledConnectionLifetime = PooledConnectionLifetime,
            PooledConnectionIdleTimeout = PooledConnectionIdleTimeout
        };

        return new HttpClient(handler) {
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Timeout = DefaultTimeout
        };
    }

    private static void EnsureDefaultHeaders(HttpClient http) {
        if (!http.DefaultRequestHeaders.UserAgent.Any()) {
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
        }
    }

    private static string NormalizeSignature(string? signature) {
        return signature?.Trim().Trim('"').ToLowerInvariant() ?? "";
    }

    private static string CreateCacheIdentity(string? template, bool isTms) {
        if (string.IsNullOrEmpty(template)) return "default";

        var policyKey = template.Contains("tile.openstreetmap.org", StringComparison.OrdinalIgnoreCase)
            ? $"|ua={DefaultUserAgent}"
            : "";
        var key = $"{(isTms ? "tms" : "xyz")}|{template}{policyKey}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }

    public void Dispose() {
        if (!_disposed) {
            _disposed = true;
            if (_ownsHttpClient) {
                _http.Dispose();
            }
            _sourceInitializationLock.Dispose();
        }
    }

}

public sealed record TileAttribution(string Text, string Url);

public sealed record TileSourceDefinition(
    string Template,
    int ImageMinZoom,
    int ImageMaxZoom,
    bool ForceTmsYFlip,
    bool IsMaxZoomAuto,
    bool IsBing) {
    private static readonly Regex PrefixRegex = new(
        @"^(?<type>[a-zA-Z][a-zA-Z0-9_-]*)(?:\[(?<zoomRange>auto|\d{1,2}(?:,\d{1,2})?)\])?:(?<template>https?://.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool IsSupported(string value, string? layerType = null) {
        try {
            Parse(value, layerType);
            return true;
        } catch (NotSupportedException) {
            return false;
        }
    }

    public static TileSourceDefinition Parse(string value, string? layerType = null) {
        var template = value.Trim();
        var type = layerType?.Trim();
        var imageMinZoom = GeoConverter.MinZoom;
        var imageMaxZoom = GeoConverter.MaxZoom;
        var isMaxZoomAuto = false;

        var match = PrefixRegex.Match(template);
        if (match.Success) {
            type = match.Groups["type"].Value;
            template = match.Groups["template"].Value;
            var zoomRangeText = match.Groups["zoomRange"].Value;
            if (string.Equals(zoomRangeText, "auto", StringComparison.OrdinalIgnoreCase)) {
                isMaxZoomAuto = true;
            } else if (TryParseZoomRange(zoomRangeText, out var parsedMinZoom, out var parsedMaxZoom)) {
                imageMinZoom = parsedMinZoom;
                imageMaxZoom = parsedMaxZoom;
            } else if (string.Equals(type, "TMS", StringComparison.OrdinalIgnoreCase)) {
                isMaxZoomAuto = true;
            }
        }

        if (string.Equals(type, "WMS", StringComparison.OrdinalIgnoreCase)) {
            throw new NotSupportedException("WMS 图层尚未实现，请使用 XYZ 或 TMS 瓦片图源。");
        }

        var isBing = string.Equals(type, "BING", StringComparison.OrdinalIgnoreCase);
        if (isBing && !IsBingMarker(template)) {
            throw new NotSupportedException("Bing sources must use the official https://www.bing.com/maps/ marker.");
        }
        if (!isBing && template.Contains("virtualearth.net/tiles/", StringComparison.OrdinalIgnoreCase)) {
            throw new NotSupportedException("Bing Maps 直连瓦片已停用；请改用受支持的 Azure Maps 或其他合法图源。");
        }

        var forceTmsYFlip = string.Equals(type, "TMS", StringComparison.OrdinalIgnoreCase);
        return new TileSourceDefinition(template, imageMinZoom, imageMaxZoom, forceTmsYFlip, isMaxZoomAuto, isBing);
    }

    private static bool IsBingMarker(string template) {
        if (!Uri.TryCreate(template, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) {
            return false;
        }

        var isBingHost = uri.Host.Equals("bing.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("www.bing.com", StringComparison.OrdinalIgnoreCase);
        return isBingHost &&
            uri.IsDefaultPort &&
            uri.UserInfo.Length == 0 &&
            uri.Query.Length == 0 &&
            uri.Fragment.Length == 0 &&
            uri.AbsolutePath.Equals("/maps/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseZoomRange(string value, out int minZoom, out int maxZoom) {
        minZoom = GeoConverter.MinZoom;
        maxZoom = GeoConverter.MaxZoom;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && int.TryParse(parts[0], out var parsedMaxZoom)) {
            maxZoom = Math.Clamp(parsedMaxZoom, GeoConverter.MinZoom, GeoConverter.MaxZoom);
            return true;
        }

        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var parsedMinZoom) &&
            int.TryParse(parts[1], out parsedMaxZoom)) {
            minZoom = Math.Clamp(parsedMinZoom, GeoConverter.MinZoom, GeoConverter.MaxZoom);
            maxZoom = Math.Clamp(parsedMaxZoom, minZoom, GeoConverter.MaxZoom);
            return true;
        }

        return false;
    }
}
