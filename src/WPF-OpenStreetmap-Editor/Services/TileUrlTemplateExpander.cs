using System.Text.RegularExpressions;

namespace WPF_OpenStreetmap_Editor.Services;

internal static class TileUrlTemplateExpander {
    private static readonly string[] DefaultSubdomains = ["a", "b", "c"];

    public static string Expand(string template, int zoom, int x, int y, string? accessToken) {
        var expanded = ApplySubdomain(template, x, y);
        expanded = ApplyAccessToken(expanded, accessToken);
        expanded = ApplyQuadKey(expanded, zoom, x, y);
        return expanded
            .Replace("{z}", zoom.ToString())
            .Replace("{x}", x.ToString())
            .Replace("{y}", y.ToString());
    }

    internal static string ApplySubdomain(string template, int x, int y) {
        var switchMatch = Regex.Match(template, @"\{switch:([^}]+)\}", RegexOptions.IgnoreCase);
        if (switchMatch.Success) {
            var options = switchMatch.Groups[1].Value
                .Split(',')
                .Select(static option => option.Trim())
                .Where(static option => option.Length > 0)
                .ToArray();
            if (options.Length > 0) {
                return template.Replace(switchMatch.Value, options[Math.Abs(x + y) % options.Length]);
            }
            return template;
        }

        if (template.IndexOf("{s}", StringComparison.OrdinalIgnoreCase) < 0) return template;

        var subdomain = DefaultSubdomains[Math.Abs(x + y) % DefaultSubdomains.Length];
        return Regex.Replace(template, @"\{s\}", subdomain, RegexOptions.IgnoreCase);
    }

    private static string ApplyAccessToken(string template, string? accessToken) {
        if (string.IsNullOrEmpty(accessToken)) return template;

        var encodedToken = Uri.EscapeDataString(accessToken);
        var expanded = Regex.Replace(
            template,
            @"\{access_token\}",
            _ => encodedToken,
            RegexOptions.IgnoreCase);
        return Regex.Replace(
            expanded,
            @"\{token\}",
            _ => encodedToken,
            RegexOptions.IgnoreCase);
    }

    internal static string ApplyQuadKey(string template, int zoom, int x, int y) {
        if (template.IndexOf("{quadkey}", StringComparison.OrdinalIgnoreCase) < 0) return template;

        return Regex.Replace(template, @"\{quadkey\}", BuildQuadKey(zoom, x, y), RegexOptions.IgnoreCase);
    }

    private static string BuildQuadKey(int zoom, int x, int y) {
        var quadKey = new char[zoom];
        for (var i = zoom; i > 0; i--) {
            var digit = 0;
            var mask = 1 << (i - 1);
            if ((x & mask) != 0) digit++;
            if ((y & mask) != 0) digit += 2;
            quadKey[zoom - i] = (char)('0' + digit);
        }
        return new string(quadKey);
    }
}
