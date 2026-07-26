using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace WPF_OpenStreetmap_Editor.Services;

public partial class TileService : IDisposable {
    private readonly HttpClient _http;
    private readonly string _cacheRoot;
    private bool _disposed;

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

    public string GetCachePath(int z, int x, int y) {
        int n = 1 << z;
        int xWrapped = ((x % n) + n) % n;
        int yForUrl = IsTms ? (n - 1) - y : y;

        var dir = Path.Combine(_cacheRoot, z.ToString(), xWrapped.ToString());
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, yForUrl + ".png");
    }

    public async Task<BitmapImage?> GetTileAsync(int z, int x, int y, string? accessToken, CancellationToken ct = default) {
        try {
            if (string.IsNullOrEmpty(TileTemplate)) return null;

            int n = 1 << z;
            int xWrapped = ((x % n) + n) % n;
            int yForUrl = IsTms ? (n - 1) - y : y;
            if (yForUrl < 0 || yForUrl >= n) return null;

            string url = BuildTileUrl(z, x, y, accessToken);
            string cache = GetCachePath(z, x, y);

            if (File.Exists(cache)) {
                return LoadBitmapFromFile(cache);
            }

            Logger.Log(url, "START");
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            Logger.Log(url, resp.StatusCode.ToString());
            if (!resp.IsSuccessStatusCode) return null;

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

            try { File.WriteAllBytes(cache, bytes); }
            catch (Exception ex) { Logger.Error("Failed to write tile cache", ex); }

            return LoadBitmapFromBytes(bytes);
        } catch (OperationCanceledException) {
            return null;
        } catch (Exception ex) {
            Logger.Error($"GetTileAsync failed (z={z}, x={x}, y={y})", ex);
            return null;
        }
    }

    private static BitmapImage LoadBitmapFromFile(string path) {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        using (var fs = File.OpenRead(path)) {
            bi.StreamSource = new MemoryStream();
            fs.CopyTo(bi.StreamSource);
            bi.StreamSource.Position = 0;
        }
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    private static BitmapImage LoadBitmapFromBytes(byte[] bytes) {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = new MemoryStream(bytes);
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    public void ParseUrlTemplate(string url, string? accessToken) {
        if (string.IsNullOrEmpty(url))
            return;

        var template = url;
        IsTms = false;

        if (template.IndexOf("mapbox.com", StringComparison.OrdinalIgnoreCase) >= 0 || template.IndexOf("api.mapbox", StringComparison.OrdinalIgnoreCase) >= 0) {
            IsTms = false;
        }

        template = ApplyAccessToken(template, accessToken);

        if (template.IndexOf("{-y}", StringComparison.OrdinalIgnoreCase) >= 0) {
            IsTms = true;
            template = template.Replace("{-y}", "{y}");
        }

        template = template.Replace("{zoom}", "{z}");

        if (template.IndexOf("tilematrix", StringComparison.OrdinalIgnoreCase) >= 0 || template.IndexOf("tilecol", StringComparison.OrdinalIgnoreCase) >= 0 ||
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
