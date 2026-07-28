using System;

namespace WPF_OpenStreetmap_Editor.Services;

public enum TileSourceSafetyWarningKind {
    None,
    Google,
    Amap,
    Baidu,
    Gcj02,
    Proprietary
}

public static class TileSourceSafetyService {
    public static TileSourceSafetyWarningKind GetWarningKind(string? name, string? source) {
        var combined = $"{name} {source}".Trim();
        var uri = TryGetTileUri(source);

        if (MatchesGoogle(combined, uri)) return TileSourceSafetyWarningKind.Google;
        if (MatchesAmap(combined, uri)) return TileSourceSafetyWarningKind.Amap;
        if (MatchesBaidu(combined, uri)) return TileSourceSafetyWarningKind.Baidu;
        if (MatchesGcj02Provider(combined, uri)) return TileSourceSafetyWarningKind.Gcj02;
        if (MatchesProprietaryProvider(combined, uri)) return TileSourceSafetyWarningKind.Proprietary;
        return TileSourceSafetyWarningKind.None;
    }

    public static string GetWarningMessage(TileSourceSafetyWarningKind kind) {
        return kind switch {
            TileSourceSafetyWarningKind.Google => LocalizationService.Instance.GetString("Settings.SourceSafetyGoogle"),
            TileSourceSafetyWarningKind.Amap => LocalizationService.Instance.GetString("Settings.SourceSafetyAmap"),
            TileSourceSafetyWarningKind.Baidu => LocalizationService.Instance.GetString("Settings.SourceSafetyBaidu"),
            TileSourceSafetyWarningKind.Gcj02 => LocalizationService.Instance.GetString("Settings.SourceSafetyGcj02"),
            TileSourceSafetyWarningKind.Proprietary => LocalizationService.Instance.GetString("Settings.SourceSafetyProprietary"),
            _ => ""
        };
    }

    private static bool MatchesGoogle(string text, Uri? uri) {
        return ContainsAny(text, "google", "谷歌") ||
            HostContains(uri, "google", "googleapis", "gstatic");
    }

    private static bool MatchesAmap(string text, Uri? uri) {
        return ContainsAny(text, "amap", "高德", "autonavi", "gaode") ||
            HostContains(uri, "amap", "autonavi");
    }

    private static bool MatchesBaidu(string text, Uri? uri) {
        return ContainsAny(text, "baidu", "百度", "bdimg") ||
            HostContains(uri, "baidu", "bdimg");
    }

    private static bool MatchesGcj02Provider(string text, Uri? uri) {
        return ContainsAny(text, "tencent maps", "qq maps", "soso maps", "sogou", "go2map", "qihu", "qihoo", "360地图", "腾讯", "搜狗", "搜搜地图") ||
            HostEqualsOrEndsWith(uri, "qq.com", "gtimg.com", "soso.com", "sogou.com", "go2map.com", "map.so.com", "qihucdn.com", "qhimg.com");
    }

    private static bool MatchesProprietaryProvider(string text, Uri? uri) {
        return ContainsAny(text, "apple maps", "apple mapkit", "苹果地图", "here maps", "heremaps", "tomtom", "yandex maps", "yandex map") ||
            HostEqualsOrEndsWith(uri, "maps.apple.com", "apple-mapkit.com", "here.com", "hereapi.com", "tomtom.com", "tomtom.com.cn", "yandex.ru", "yandex.net", "yandex.com");
    }

    private static bool ContainsAny(string text, params string[] needles) {
        foreach (var needle in needles) {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static bool HostContains(Uri? uri, params string[] needles) {
        if (uri is null) return false;

        foreach (var needle in needles) {
            if (uri.Host.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static bool HostEqualsOrEndsWith(Uri? uri, params string[] domains) {
        if (uri is null) return false;

        foreach (var domain in domains) {
            if (uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static Uri? TryGetTileUri(string? source) {
        if (string.IsNullOrWhiteSpace(source)) return null;

        var trimmed = source.Trim();
        var httpIndex = trimmed.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
        var httpsIndex = trimmed.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
        var startIndex = httpIndex >= 0 && httpsIndex >= 0
            ? Math.Min(httpIndex, httpsIndex)
            : Math.Max(httpIndex, httpsIndex);
        if (startIndex < 0) return null;

        return Uri.TryCreate(trimmed[startIndex..], UriKind.Absolute, out var uri) ? uri : null;
    }
}
