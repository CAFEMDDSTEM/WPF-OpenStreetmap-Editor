using System;
using System.Collections.Generic;
using System.Globalization;
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

        var template = ApplySubdomains(TileTemplate, xWrapped, yForUrl);
        template = ApplyAccessToken(template, accessToken);
        template = ApplyQuadKey(template, z, xWrapped, yForUrl);

        return template
            .Replace("{z}", z.ToString())
            .Replace("{x}", xWrapped.ToString())
            .Replace("{y}", yForUrl.ToString());
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

    private static string ApplySubdomains(string template, int x, int y) {
        var result = template;

        var switchMatch = Regex.Match(result, @"\{switch:([^}]+)\}", RegexOptions.IgnoreCase);
        if (switchMatch.Success) {
            var opts = switchMatch.Groups[1].Value
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();
            if (opts.Length > 0) {
                var sub = opts[(Math.Abs(x + y) % opts.Length)];
                result = result.Replace(switchMatch.Value, sub);
            }
        } else if (result.IndexOf("{s}", StringComparison.OrdinalIgnoreCase) >= 0) {
            string[] subs = ["a", "b", "c"];
            var sub = subs[(Math.Abs(x + y) % subs.Length)];
            result = Regex.Replace(result, @"\{s\}", sub, RegexOptions.IgnoreCase);
        }

        return result;
    }

    private static string ApplyQuadKey(string template, int z, int x, int y) {
        if (template.IndexOf("{quadkey}", StringComparison.OrdinalIgnoreCase) < 0) return template;

        var quadKey = BuildQuadKey(z, x, y);
        return Regex.Replace(template, @"\{quadkey\}", quadKey, RegexOptions.IgnoreCase);
    }

    private static string BuildQuadKey(int z, int x, int y) {
        var quadKey = new char[z];
        for (var i = z; i > 0; i--) {
            var digit = 0;
            var mask = 1 << (i - 1);
            if ((x & mask) != 0) digit++;
            if ((y & mask) != 0) digit += 2;
            quadKey[z - i] = (char)('0' + digit);
        }

        return new string(quadKey);
    }

    private static string ApplyAccessToken(string template, string? accessToken) {
        if (string.IsNullOrEmpty(accessToken)) {
            return template;
        }

        var encodedToken = Uri.EscapeDataString(accessToken);
        var result = Regex.Replace(
            template,
            @"\{access_token\}",
            _ => encodedToken,
            RegexOptions.IgnoreCase);
        return Regex.Replace(
            result,
            @"\{token\}",
            _ => encodedToken,
            RegexOptions.IgnoreCase);
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

            using var json = JsonDocument.Parse(bytes);
            return ParseBingMetadata(json.RootElement);
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

    private static BingMetadata ParseBingMetadata(JsonElement root) {
        if (root.TryGetProperty("authenticationResultCode", out var authenticationResult) &&
            !string.Equals(authenticationResult.GetString(), "ValidCredentials", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Bing Maps rejected the supplied key.");
        }

        if (!TryGetFirstArrayItem(root, "resourceSets", out var resourceSet) ||
            !TryGetFirstArrayItem(resourceSet, "resources", out var resource)) {
            throw new InvalidDataException("Bing metadata did not contain an imagery resource.");
        }

        var imageUrl = GetRequiredString(resource, "imageUrl");
        var subdomains = GetStringArray(resource, "imageUrlSubdomains")
            .Where(static value => Regex.IsMatch(value, @"^[a-zA-Z0-9.-]+$"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var template = NormalizeBingTileTemplate(imageUrl, subdomains);
        var minZoom = Math.Clamp(GetRequiredInt32(resource, "zoomMin"), GeoConverter.MinZoom, GeoConverter.MaxZoom);
        var maxZoom = Math.Clamp(GetRequiredInt32(resource, "zoomMax"), GeoConverter.MinZoom, GeoConverter.MaxZoom);
        if (minZoom > maxZoom) {
            throw new InvalidDataException("Bing metadata returned an invalid zoom range.");
        }

        var copyright = GetRequiredString(root, "copyright");
        var providers = ParseBingImageryProviders(resource, minZoom, maxZoom);
        return new BingMetadata(template, minZoom, maxZoom, copyright, providers);
    }

    private static string NormalizeBingTileTemplate(string imageUrl, IReadOnlyList<string> subdomains) {
        var template = Regex.Replace(
            imageUrl,
            @"\{culture\}",
            _ => Uri.EscapeDataString(CultureInfo.CurrentUICulture.Name),
            RegexOptions.IgnoreCase);
        if (template.IndexOf("{subdomain}", StringComparison.OrdinalIgnoreCase) >= 0) {
            if (subdomains.Count == 0) {
                throw new InvalidDataException("Bing metadata did not contain tile subdomains.");
            }

            var switchValue = $"{{switch:{string.Join(',', subdomains)}}}";
            template = Regex.Replace(template, @"\{subdomain\}", _ => switchValue, RegexOptions.IgnoreCase);
        }

        if (template.IndexOf("{quadkey}", StringComparison.OrdinalIgnoreCase) < 0) {
            throw new InvalidDataException("Bing metadata tile URL did not contain a quadkey placeholder.");
        }

        var sampleUrl = ApplyQuadKey(ApplySubdomains(template, 0, 0), 1, 0, 0);
        if (!Uri.TryCreate(sampleUrl, UriKind.Absolute, out var sampleUri) || sampleUri.Scheme != Uri.UriSchemeHttps) {
            throw new InvalidDataException("Bing metadata returned an invalid HTTPS tile URL.");
        }

        return template;
    }

    private static IReadOnlyList<BingImageryProvider> ParseBingImageryProviders(
        JsonElement resource,
        int defaultMinZoom,
        int defaultMaxZoom) {
        if (!resource.TryGetProperty("imageryProviders", out var providersElement) ||
            providersElement.ValueKind != JsonValueKind.Array) {
            return [];
        }

        List<BingImageryProvider> providers = [];
        foreach (var providerElement in providersElement.EnumerateArray()) {
            if (!providerElement.TryGetProperty("attribution", out var attributionElement)) continue;

            var attribution = attributionElement.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(attribution)) continue;

            List<BingCoverageArea> coverageAreas = [];
            if (providerElement.TryGetProperty("coverageAreas", out var coverageAreasElement) &&
                coverageAreasElement.ValueKind == JsonValueKind.Array) {
                foreach (var areaElement in coverageAreasElement.EnumerateArray()) {
                    if (TryParseBingCoverageArea(areaElement, defaultMinZoom, defaultMaxZoom, out var area)) {
                        coverageAreas.Add(area);
                    }
                }
            }

            providers.Add(new BingImageryProvider(attribution, coverageAreas));
        }

        return providers;
    }

    private static bool TryParseBingCoverageArea(
        JsonElement element,
        int defaultMinZoom,
        int defaultMaxZoom,
        out BingCoverageArea coverageArea) {
        coverageArea = default;
        if (!element.TryGetProperty("bbox", out var bboxElement) ||
            bboxElement.ValueKind != JsonValueKind.Array) {
            return false;
        }

        var coordinates = bboxElement.EnumerateArray().ToArray();
        if (coordinates.Length != 4 || coordinates.Any(static coordinate => !coordinate.TryGetDouble(out _))) {
            return false;
        }

        var south = coordinates[0].GetDouble();
        var west = coordinates[1].GetDouble();
        var north = coordinates[2].GetDouble();
        var east = coordinates[3].GetDouble();
        if (!double.IsFinite(south) || !double.IsFinite(west) ||
            !double.IsFinite(north) || !double.IsFinite(east) || south > north) {
            return false;
        }

        var minZoom = element.TryGetProperty("zoomMin", out var minZoomElement) && minZoomElement.TryGetInt32(out var parsedMinZoom)
            ? parsedMinZoom
            : defaultMinZoom;
        var maxZoom = element.TryGetProperty("zoomMax", out var maxZoomElement) && maxZoomElement.TryGetInt32(out var parsedMaxZoom)
            ? parsedMaxZoom
            : defaultMaxZoom;
        if (minZoom > maxZoom) return false;

        coverageArea = new BingCoverageArea(minZoom, maxZoom, south, west, north, east);
        return true;
    }

    private static bool TryGetFirstArrayItem(JsonElement parent, string propertyName, out JsonElement item) {
        item = default;
        if (!parent.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array) {
            return false;
        }

        var enumerator = array.EnumerateArray();
        if (!enumerator.MoveNext()) return false;

        item = enumerator.Current;
        return true;
    }

    private static string GetRequiredString(JsonElement parent, string propertyName) {
        if (!parent.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String) {
            throw new InvalidDataException($"Bing metadata did not contain {propertyName}.");
        }

        var value = element.GetString()?.Trim();
        return !string.IsNullOrEmpty(value)
            ? value
            : throw new InvalidDataException($"Bing metadata did not contain {propertyName}.");
    }

    private static int GetRequiredInt32(JsonElement parent, string propertyName) {
        if (!parent.TryGetProperty(propertyName, out var element) || !element.TryGetInt32(out var value)) {
            throw new InvalidDataException($"Bing metadata did not contain {propertyName}.");
        }

        return value;
    }

    private static IEnumerable<string> GetStringArray(JsonElement parent, string propertyName) {
        if (!parent.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array) {
            return [];
        }

        return array.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString()?.Trim() ?? "")
            .Where(static item => item.Length > 0);
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

        var template = ApplySubdomains(TileTemplate, 0, 0);
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

    private sealed record BingMetadata(
        string TileTemplate,
        int MinZoom,
        int MaxZoom,
        string Copyright,
        IReadOnlyList<BingImageryProvider> ImageryProviders);

    private sealed record BingImageryProvider(string Attribution, IReadOnlyList<BingCoverageArea> CoverageAreas) {
        public bool AppliesTo(int zoom, double south, double west, double north, double east) {
            return CoverageAreas.Count == 0 ||
                CoverageAreas.Any(area => area.Intersects(zoom, south, west, north, east));
        }
    }

    private readonly record struct BingCoverageArea(
        int MinZoom,
        int MaxZoom,
        double South,
        double West,
        double North,
        double East) {
        public bool Intersects(int zoom, double south, double west, double north, double east) {
            if (zoom < MinZoom || zoom > MaxZoom || north < South || south > North) return false;

            return LongitudeRangesIntersect(west, east, West, East);
        }

        private static bool LongitudeRangesIntersect(double firstWest, double firstEast, double secondWest, double secondEast) {
            var firstRanges = GetLongitudeRanges(firstWest, firstEast);
            var secondRanges = GetLongitudeRanges(secondWest, secondEast);
            return firstRanges.Any(first => secondRanges.Any(second =>
                first.West <= second.East && first.East >= second.West));
        }

        private static IReadOnlyList<(double West, double East)> GetLongitudeRanges(double west, double east) {
            if (east - west >= 360) return [(-180, 180)];

            var normalizedWest = NormalizeLongitude(west);
            var normalizedEast = NormalizeLongitude(east);
            return normalizedWest <= normalizedEast
                ? [(normalizedWest, normalizedEast)]
                : [(normalizedWest, 180), (-180, normalizedEast)];
        }

        private static double NormalizeLongitude(double longitude) {
            var normalized = (longitude + 180) % 360;
            if (normalized < 0) normalized += 360;
            return normalized - 180;
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
