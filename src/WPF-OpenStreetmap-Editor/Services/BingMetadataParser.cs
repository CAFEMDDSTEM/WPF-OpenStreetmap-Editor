using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WPF_OpenStreetmap_Editor.Services;

internal static class BingMetadataParser {
    public static BingMetadata Parse(ReadOnlyMemory<byte> bytes) {
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
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
        var template = NormalizeTileTemplate(imageUrl, subdomains);
        var minZoom = Math.Clamp(GetRequiredInt32(resource, "zoomMin"), GeoConverter.MinZoom, GeoConverter.MaxZoom);
        var maxZoom = Math.Clamp(GetRequiredInt32(resource, "zoomMax"), GeoConverter.MinZoom, GeoConverter.MaxZoom);
        if (minZoom > maxZoom) {
            throw new InvalidDataException("Bing metadata returned an invalid zoom range.");
        }

        var copyright = GetRequiredString(root, "copyright");
        var providers = ParseImageryProviders(resource, minZoom, maxZoom);
        return new BingMetadata(template, minZoom, maxZoom, copyright, providers);
    }

    private static string NormalizeTileTemplate(string imageUrl, IReadOnlyList<string> subdomains) {
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

        var sampleUrl = TileUrlTemplateExpander.ApplyQuadKey(
            TileUrlTemplateExpander.ApplySubdomain(template, 0, 0),
            1,
            0,
            0);
        if (!Uri.TryCreate(sampleUrl, UriKind.Absolute, out var sampleUri) || sampleUri.Scheme != Uri.UriSchemeHttps) {
            throw new InvalidDataException("Bing metadata returned an invalid HTTPS tile URL.");
        }
        return template;
    }

    private static IReadOnlyList<BingImageryProvider> ParseImageryProviders(
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
                    if (TryParseCoverageArea(areaElement, defaultMinZoom, defaultMaxZoom, out var area)) {
                        coverageAreas.Add(area);
                    }
                }
            }
            providers.Add(new BingImageryProvider(attribution, coverageAreas));
        }
        return providers;
    }

    private static bool TryParseCoverageArea(
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
        if (!parent.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array) return false;
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
        if (!parent.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array) return [];
        return array.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString()?.Trim() ?? "")
            .Where(static item => item.Length > 0);
    }
}

internal sealed record BingMetadata(
    string TileTemplate,
    int MinZoom,
    int MaxZoom,
    string Copyright,
    IReadOnlyList<BingImageryProvider> ImageryProviders);

internal sealed record BingImageryProvider(string Attribution, IReadOnlyList<BingCoverageArea> CoverageAreas) {
    public bool AppliesTo(int zoom, double south, double west, double north, double east) {
        return CoverageAreas.Count == 0 ||
            CoverageAreas.Any(area => area.Intersects(zoom, south, west, north, east));
    }
}

internal readonly record struct BingCoverageArea(
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
