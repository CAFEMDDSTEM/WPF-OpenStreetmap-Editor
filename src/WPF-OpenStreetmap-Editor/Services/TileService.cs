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

/// <summary>
/// 核心瓦片服务：管理瓦片 URL 构建、缓存读写、Bing/ArcGIS 元数据、并发控制与维护。
/// </summary>
public partial class TileService : IDisposable {
    // ===== 常量 =====
    private const int DefaultMaxConnectionsPerServer = 16;
    private const string BingMetadataEndpoint = "https://dev.virtualearth.net/REST/v1/Imagery/Metadata/Aerial";
    private const string BingTermsUrl = "https://www.microsoft.com/maps/product/terms.html";
    private const string NoTileExtension = ".notile";
    private const string DefaultUserAgent = "WPF-OpenStreetmap-Editor/1.0";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2);
    private const int WriteLockStripeCount = 256;

    // ===== 运行时状态 =====
    private readonly HttpClient _http;                  // HTTP 客户端（共享或自有）
    private readonly string _cacheRoot;                 // 磁盘缓存根目录
    private readonly bool _ownsHttpClient;              // 是否拥有 HttpClient 生命周期
    private readonly HashSet<string> _noTileEtags = new(StringComparer.OrdinalIgnoreCase);  // 已知的无瓦片 ETag
    private readonly HashSet<string> _noTileMd5s = new(StringComparer.OrdinalIgnoreCase);   // 已知的无瓦片内容 MD5
    private readonly SemaphoreSlim _sourceInitializationLock = new(1, 1); // Bing 初始化锁
    private IReadOnlyList<BingImageryProvider> _bingImageryProviders = []; // Bing 图片来源提供商
    private string _bingCopyright = "";                 // Bing 版权信息
    private bool _sourceInitialized;                    // Bing 元数据是否已加载
    private bool _disposed;

    // ===== 静态缓存 =====
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp"];
    private static readonly SemaphoreSlim[] WriteLocks = Enumerable.Range(0, WriteLockStripeCount)
        .Select(static _ => new SemaphoreSlim(1, 1))
        .ToArray();  // 256 个写锁槽位，按哈希分配减少锁竞争

    // ===== 公开属性 =====
    public string? TileTemplate { get; set; }           // 当前瓦片 URL 模板
    public bool IsTms { get; set; }                     // 是否 TMS（Y 翻转）
    public int ImageMinZoom { get; private set; } = GeoConverter.MinZoom;
    public int MapMaxZoom { get; private set; } = GeoConverter.MaxZoom;
    public int ImageMaxZoom { get; private set; } = GeoConverter.MaxZoom;
    public int MaxZoom => ImageMaxZoom;
    public bool IsMaxZoomAuto { get; private set; }     // 是否自动检测最大缩放级别
    public bool IsBing { get; private set; }            // 当前是否 Bing 图源
    public string CacheIdentity => CreateCacheIdentity(IsBing ? "bing:aerial" : TileTemplate, IsTms);

    /// <summary>构造函数：初始化 HTTP 客户端、缓存目录，并触发磁盘缓存维护</summary>
    public TileService(HttpClient? http = null, string? cacheRoot = null) {
        _ownsHttpClient = http is null;
        _http = http ?? CreateDefaultHttpClient();
        EnsureDefaultHeaders(_http);
        _cacheRoot = AppPaths.Normalize(cacheRoot ?? AppPaths.TileCacheDirectory);
        if (string.Equals(_cacheRoot, AppPaths.TileCacheDirectory, StringComparison.OrdinalIgnoreCase)) {
            TileDiskCache.ScheduleMaintenance(_cacheRoot);
        }
    }

    /// <summary>构建瓦片的最终 HTTP URL：处理 TMS/Y 翻转、子域名、AccessToken、QuadKey 及模板替换</summary>
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

    /// <summary>初始化 Bing 图源：从 Bing 元数据 API 获取瓦片模板、缩放范围和版权信息</summary>
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

    /// <summary>获取当前视口范围内的 Bing 版权归属信息</summary>
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

    /// <summary>应用图源配置：缩放范围、已知的无瓦片 ETag/MD5 黑名单</summary>
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

    // ===== URL 模板辅助 =====

    /// <summary>替换 {switch:...} 或 {s} 子域名占位符，用瓦片坐标哈希选择子域名</summary>
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

    /// <summary>替换 {quadkey} 占位符为 Bing QuadKey</summary>
    private static string ApplyQuadKey(string template, int z, int x, int y) {
        if (template.IndexOf("{quadkey}", StringComparison.OrdinalIgnoreCase) < 0) return template;

        var quadKey = BuildQuadKey(z, x, y);
        return Regex.Replace(template, @"\{quadkey\}", quadKey, RegexOptions.IgnoreCase);
    }

    /// <summary>构建 Bing QuadKey：将 (z,x,y) 编码为四叉树字符串</summary>
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

    /// <summary>替换 {access_token} / {token} 占位符为 URL 编码后的 Token</summary>
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

    // ===== 磁盘缓存 =====

    /// <summary>获取缓存路径（自动创建目录）</summary>
    public string GetCacheBasePath(int z, int x, int y) {
        return GetCacheBasePath(z, x, y, createDirectory: true);
    }

    /// <summary>获取缓存路径：按 CacheIdentity/z/x/y 组织，x 做环绕归一化</summary>
    private string GetCacheBasePath(int z, int x, int y, bool createDirectory) {
        var n = 1 << z;
        var xWrapped = ((x % n) + n) % n;

        var dir = Path.Combine(_cacheRoot, CacheIdentity, z.ToString(), xWrapped.ToString());
        if (createDirectory) {
            Directory.CreateDirectory(dir);
        }

        return Path.Combine(dir, y.ToString());
    }

    /// <summary>查找缓存中是否存在该瓦片的已知图片格式文件</summary>
    public string? FindCachedFile(int z, int x, int y) {
        var basePath = GetCacheBasePath(z, x, y, createDirectory: false);
        foreach (var ext in ImageExtensions) {
            var path = basePath + ext;
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    /// <summary>查找该瓦片是否被标记为"无瓦片"</summary>
    public string? FindNoTileMarker(int z, int x, int y) {
        var path = GetCacheBasePath(z, x, y, createDirectory: false) + NoTileExtension;
        return File.Exists(path) ? path : null;
    }

    /// <summary>同步读取已缓存的瓦片文件，经校验后返回字节数组</summary>
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

    /// <summary>异步读取已缓存的瓦片文件，带校验（适用于大量瓦片时的非阻塞 I/O）</summary>
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

    // ===== 瓦片字节数据获取（核心链路） =====

    /// <summary>
    /// 获取瓦片字节数组的主入口：
    /// 1) 检查范围 + 无瓦片标记 → 2) 异步读缓存 → 3) 写锁（防重复下载）→ 4) HTTP 下载 → 5) 校验 → 6) 写缓存
    /// </summary>
    public async Task<byte[]?> GetTileBytesAsync(int z, int x, int y, string? accessToken, CancellationToken ct = default) {
        try {
            if (string.IsNullOrEmpty(TileTemplate)) return null;
            if (z < ImageMinZoom || z > ImageMaxZoom) return null;
            if (FindNoTileMarker(z, x, y) is not null) return null;

            // 尝试读缓存
            var cachedBytes = await TryReadCachedTileAsync(z, x, y, ct).ConfigureAwait(false);
            if (cachedBytes is not null) return cachedBytes;

            // 取写锁（按 cacheKey 哈希到 256 个槽位之一）
            var n = 1 << z;
            var xWrapped = ((x % n) + n) % n;
            var cacheKey = $"{CacheIdentity}/{z}/{xWrapped}/{y}";
            var semaphore = GetWriteLock(cacheKey);
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try {
                // 双重检查：取锁后再次检查无瓦片标记和缓存
                if (FindNoTileMarker(z, x, y) is not null) return null;

                cachedBytes = await TryReadCachedTileAsync(z, x, y, ct).ConfigureAwait(false);
                if (cachedBytes is not null) return cachedBytes;

                // 构建 URL 并发送 HTTP 请求
                var url = BuildTileUrl(z, x, y, accessToken);
                if (string.IsNullOrEmpty(url)) return null;

                Logger.Log(url, "START");
                using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                Logger.Log(url, resp.StatusCode.ToString());
                if (!resp.IsSuccessStatusCode) return null;

                // 读取并校验响应体
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

                // 写入磁盘缓存
                var cachePath = GetCacheBasePath(z, x, y) + ext;
                try {
                    File.WriteAllBytes(cachePath, bytes);
                    if (string.Equals(_cacheRoot, AppPaths.TileCacheDirectory, StringComparison.OrdinalIgnoreCase)) {
                        TileDiskCache.ScheduleMaintenance(_cacheRoot);
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

    // ===== 无瓦片标记 =====

    /// <summary>写一个 .notile 标记文件，表示该瓦片在此缩放级别不存在，避免重复 HTTP 请求</summary>
    private void MarkNoTile(int z, int x, int y) {
        try {
            var markerPath = GetCacheBasePath(z, x, y) + NoTileExtension;
            File.WriteAllText(markerPath, "No tile at this zoom level", Encoding.UTF8);
        } catch (Exception ex) {
            Logger.Error($"Failed to write no-tile marker (z={z}, x={x}, y={y})", ex);
        }
    }

    /// <summary>根据 ETag 或 MD5 判断服务器返回的是否是"无瓦片"响应</summary>
    private bool IsNoTileResponse(HttpResponseMessage response, byte[] bytes) {
        var etag = NormalizeSignature(response.Headers.ETag?.Tag);
        if (!string.IsNullOrEmpty(etag) && _noTileEtags.Contains(etag)) {
            return true;
        }

        if (_noTileMd5s.Count == 0) return false;

        var md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
        return _noTileMd5s.Contains(md5);
    }

    // ===== 并发控制 =====

    /// <summary>按 cacheKey 哈希取写锁，分布到 256 个 SemaphoreSlim 上</summary>
    private static SemaphoreSlim GetWriteLock(string cacheKey) {
        var hash = (uint)StringComparer.Ordinal.GetHashCode(cacheKey);
        return WriteLocks[hash % WriteLockStripeCount];
    }

    // ===== HTTP 响应读取 =====

    /// <summary>将 HTTP 内容流读取到字节数组，同时校验不超 MaxResponseBytes</summary>
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

    // ===== 最大缩放自动检测 =====

    /// <summary>探测 ArcGIS 元数据或通过二分试探确定图源支持的最大缩放级别</summary>
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

    // ===== URL 模板解析 =====

    /// <summary>解析 URL 模板：识别图源类型（Bing/XYZ/TMS/ArcGIS/WMTS），替换占位符，设置 IsTms/IsBing/缩放范围</summary>
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

        // 处理 {-y}：{z}/{-y}/{x} → ArcGIS，{z}/{x}/{-y} → TMS
        if (template.IndexOf("{-y}", StringComparison.OrdinalIgnoreCase) >= 0) {
            var xPos = template.IndexOf("{x}", StringComparison.OrdinalIgnoreCase);
            var negYPos = template.IndexOf("{-y}", StringComparison.OrdinalIgnoreCase);
            IsTms = xPos >= 0 && negYPos > xPos;
            template = template.Replace("{-y}", "{y}");
        } else if (source.ForceTmsYFlip && IsXBeforeY(template)) {
            IsTms = true;
        }

        template = template.Replace("{zoom}", "{z}");

        // WMTS 占位符归一化
        if (template.IndexOf("tilematrix", StringComparison.OrdinalIgnoreCase) >= 0 ||
            template.IndexOf("tilecol", StringComparison.OrdinalIgnoreCase) >= 0 ||
            template.IndexOf("tilerow", StringComparison.OrdinalIgnoreCase) >= 0) {
            template = Regex.Replace(template, @"\{?TileMatrix\}?", "{z}", RegexOptions.IgnoreCase);
            template = Regex.Replace(template, @"\{?TileCol\}?", "{x}", RegexOptions.IgnoreCase);
            template = Regex.Replace(template, @"\{?TileRow\}?", "{y}", RegexOptions.IgnoreCase);
        }

        TileTemplate = template;
    }

    // ===== Bing 元数据加载 =====

    /// <summary>从 Bing Maps API 获取瓦片元数据（URL 模板、子域名、缩放范围、版权）</summary>
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

    /// <summary>解析 Bing 元数据 JSON，提取瓦片模板、缩放范围和版权/图片来源列表</summary>
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

    /// <summary>归一化 Bing 瓦片模板：替换 {culture}、{subdomain}，校验 {quadkey} 和 HTTPS</summary>
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

    /// <summary>解析 Bing 图片来源提供商列表（含归属文本和覆盖区域）</summary>
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

    /// <summary>解析 Bing 单个覆盖区域（bbox + zoom 范围）</summary>
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

    // ===== JSON 辅助 =====

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

    // ===== ArcGIS 最大缩放检测 =====

    /// <summary>从 ArcGIS REST 元数据端点探测最大缩放级别</summary>
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

    /// <summary>从瓦片模板构建 ArcGIS REST 服务元数据 URL（将 /tile/{z}/{y}/{x} 替换为 ?f=pjson）</summary>
    private string? TryBuildArcGisMetadataUrl() {
        if (string.IsNullOrEmpty(TileTemplate)) return null;

        var template = ApplySubdomains(TileTemplate, 0, 0);
        var tileIndex = template.IndexOf("/tile/", StringComparison.OrdinalIgnoreCase);
        if (tileIndex < 0) return null;

        return template[..tileIndex] + "?f=pjson";
    }

    // ===== 瓦片可用性探测（用于自动缩放级别检测） =====

    /// <summary>从 upperZoom 向下试探，找到第一个能成功返回有效瓦片的缩放级别</summary>
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

    /// <summary>检查特定瓦片是否可以正常下载并解码</summary>
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

    /// <summary>获取某坐标在某缩放级别下的采样瓦片坐标</summary>
    private static (int X, int Y) GetSampleTile(double lat, double lon, int zoom) {
        var n = GeoConverter.GetTileCount(zoom);
        var (pixelX, pixelY) = GeoConverter.LatLonToPixelXY(lat, lon, zoom);
        var tileX = (int)Math.Clamp(Math.Floor(pixelX / GeoConverter.TileSize), 0, n - 1);
        var tileY = (int)Math.Clamp(Math.Floor(pixelY / GeoConverter.TileSize), 0, n - 1);
        return (tileX, tileY);
    }

    // ===== 工具方法 =====

    /// <summary>判断模板中 {x} 是否在 {y} 之前（用于 TMS/XYZ 判断）</summary>
    private static bool IsXBeforeY(string template) {
        var xPos = template.IndexOf("{x}", StringComparison.OrdinalIgnoreCase);
        var yPos = template.IndexOf("{y}", StringComparison.OrdinalIgnoreCase);
        return xPos >= 0 && yPos > xPos;
    }

    /// <summary>创建默认 HttpClient：支持 GZip/Brotli 解压、连接池复用、HTTP/2</summary>
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

    /// <summary>确保 User-Agent 头部已设置</summary>
    private static void EnsureDefaultHeaders(HttpClient http) {
        if (!http.DefaultRequestHeaders.UserAgent.Any()) {
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
        }
    }

    /// <summary>归一化 ETag/MD5 签名字符串（去引号、转小写）</summary>
    private static string NormalizeSignature(string? signature) {
        return signature?.Trim().Trim('"').ToLowerInvariant() ?? "";
    }

    /// <summary>创建缓存标识（SHA256 哈希的前 16 位十六进制），区分 TMS/XYZ 和 User-Agent</summary>
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

    // ===== 内部数据结构 =====

    private sealed record BingMetadata(
        string TileTemplate, int MinZoom, int MaxZoom, string Copyright,
        IReadOnlyList<BingImageryProvider> ImageryProviders);

    private sealed record BingImageryProvider(string Attribution, IReadOnlyList<BingCoverageArea> CoverageAreas) {
        public bool AppliesTo(int zoom, double south, double west, double north, double east) {
            return CoverageAreas.Count == 0 ||
                CoverageAreas.Any(area => area.Intersects(zoom, south, west, north, east));
        }
    }

    private readonly record struct BingCoverageArea(
        int MinZoom, int MaxZoom, double South, double West, double North, double East) {
        public bool Intersects(int zoom, double south, double west, double north, double east) {
            if (zoom < MinZoom || zoom > MaxZoom || north < South || south > North) return false;
            return LongitudeRangesIntersect(west, east, West, East);
        }

        private static bool LongitudeRangesIntersect(
            double firstWest, double firstEast, double secondWest, double secondEast) {
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
