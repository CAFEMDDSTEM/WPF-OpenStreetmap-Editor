using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WPF_OpenStreetmap_Editor.Services;

public partial class TileService : IDisposable {
    private readonly HttpClient _http;
    private readonly string _cacheRoot;
    private bool _disposed;

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteLocks = new();

    public string? TileTemplate { get; set; }
    public bool IsTms { get; set; }

    public TileService(HttpClient? http = null) {
        _http = http ?? new HttpClient();
        _cacheRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "tiles");
    }

    public string BuildTileUrl(int z, int x, int y, string? accessToken) {
        if (string.IsNullOrEmpty(TileTemplate))
            throw new InvalidOperationException("Tile template is not set");

        int n = 1 << z;
        int xWrapped = ((x % n) + n) % n;
        int yForUrl = IsTms ? (n - 1) - y : y;

        if (yForUrl < 0 || yForUrl >= n)
            return string.Empty;

        var template = ApplySubdomains(TileTemplate, xWrapped, yForUrl);
        template = ApplyAccessToken(template, accessToken);

        return template
            .Replace("{z}", z.ToString())
            .Replace("{x}", xWrapped.ToString())
            .Replace("{y}", yForUrl.ToString());
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
            var subs = new[] { "a", "b", "c" };
            var sub = subs[(Math.Abs(x + y) % subs.Length)];
            result = Regex.Replace(result, @"\{s\}", sub, RegexOptions.IgnoreCase);
        }

        return result;
    }

    private static string ApplyAccessToken(string template, string? accessToken) {
        if (!string.IsNullOrEmpty(accessToken) &&
            template.IndexOf("{access_token}", StringComparison.OrdinalIgnoreCase) >= 0) {
            return template.Replace("{access_token}", accessToken);
        }
        return template;
    }

    private static string DetectExtension(byte[] bytes) {
        if (bytes.Length < 4) return ".bin";
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return ".png";
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ".jpg";
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return ".gif";
        if (bytes[0] == 0x42 && bytes[1] == 0x4D) return ".bmp";
        return ".bin";
    }

    public string GetCacheBasePath(int z, int x, int y) {
        int n = 1 << z;
        int xWrapped = ((x % n) + n) % n;

        var dir = Path.Combine(_cacheRoot, z.ToString(), xWrapped.ToString());
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, y.ToString());
    }

    public string? FindCachedFile(int z, int x, int y) {
        var basePath = GetCacheBasePath(z, x, y);
        foreach (var ext in ImageExtensions) {
            var path = basePath + ext;
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    public async Task<byte[]?> GetTileBytesAsync(int z, int x, int y, string? accessToken, CancellationToken ct = default) {
        try {
            if (string.IsNullOrEmpty(TileTemplate)) return null;

            var cached = FindCachedFile(z, x, y);
            if (cached != null) {
                return File.ReadAllBytes(cached);
            }

            string cacheKey = $"{z}/{x}/{y}";
            var semaphore = WriteLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try {
                cached = FindCachedFile(z, x, y);
                if (cached != null) {
                    return File.ReadAllBytes(cached);
                }

                string url = BuildTileUrl(z, x, y, accessToken);
                if (string.IsNullOrEmpty(url)) return null;

                System.Diagnostics.Debug.WriteLine($"TILE: z={z} x={x} y={y} url={url}");
                Logger.Log(url, "START");
                using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                Logger.Log(url, resp.StatusCode.ToString());
                if (!resp.IsSuccessStatusCode) return null;

                var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                if (bytes.Length == 0) return null;

                var ext = DetectExtension(bytes);
                if (ext == ".bin") {
                    Logger.Error($"Unknown image format from {url} (first bytes: {BitConverter.ToString(bytes.Take(16).ToArray())})");
                    return null;
                }

                var cachePath = GetCacheBasePath(z, x, y) + ext;
                try { File.WriteAllBytes(cachePath, bytes); }
                catch (Exception ex) { Logger.Error("Failed to write tile cache", ex); }

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

    public void ParseUrlTemplate(string url, string? accessToken) {
        if (string.IsNullOrEmpty(url))
            return;

        var template = url;
        IsTms = false;

        template = ApplyAccessToken(template, accessToken);

        if (template.IndexOf("{-y}", StringComparison.OrdinalIgnoreCase) >= 0) {
            int xPos = template.IndexOf("{x}", StringComparison.OrdinalIgnoreCase);
            int negYPos = template.IndexOf("{-y}", StringComparison.OrdinalIgnoreCase);
            // TMS:  {z}/{x}/{-y}  → {-y} after {x}, flip Y
            // ArcGIS: {z}/{-y}/{x} → {-y} before {x}, NO flip
            IsTms = xPos >= 0 && negYPos > xPos;
            template = template.Replace("{-y}", "{y}");
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

    public void Dispose() {
        if (!_disposed) {
            _disposed = true;
            _http.Dispose();
        }
    }
}
