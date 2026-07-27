using WPF_OpenStreetmap_Editor.Services;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class TileServiceTests {
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");

    [Fact]
    public void BuildTileUrl_WrapsXAndAppliesAccessToken() {
        using var service = new TileService {
            TileTemplate = "https://tiles.example.com/{z}/{x}/{y}?token={access_token}"
        };

        var url = service.BuildTileUrl(2, -1, 1, "secret");

        Assert.Equal("https://tiles.example.com/2/3/1?token=secret", url);
    }

    [Fact]
    public void BuildTileUrl_AppliesTokenAlias() {
        using var service = new TileService {
            TileTemplate = "https://tiles.example.com/{z}/{x}/{y}?token={token}"
        };

        var url = service.BuildTileUrl(2, 1, 1, "secret");

        Assert.Equal("https://tiles.example.com/2/1/1?token=secret", url);
    }

    [Fact]
    public void BuildTileUrl_TreatsTokenAsDataAndUrlEncodesIt() {
        using var service = new TileService {
            TileTemplate = "https://tiles.example.com/{z}/{x}/{y}?token={access_token}"
        };

        var url = service.BuildTileUrl(2, 1, 1, "abc$& xyz");

        Assert.Equal("https://tiles.example.com/2/1/1?token=abc%24%26%20xyz", url);
    }

    [Fact]
    public void BuildTileUrl_AppliesTmsYFlip() {
        using var service = new TileService {
            TileTemplate = "https://tiles.example.com/{z}/{x}/{y}",
            IsTms = true
        };

        var url = service.BuildTileUrl(2, 1, 0, null);

        Assert.Equal("https://tiles.example.com/2/1/3", url);
    }

    [Fact]
    public void BuildTileUrl_ReturnsEmptyForOutOfRangeY() {
        using var service = new TileService {
            TileTemplate = "https://tiles.example.com/{z}/{x}/{y}"
        };

        Assert.Equal(string.Empty, service.BuildTileUrl(2, 1, 4, null));
    }

    [Fact]
    public void BuildTileUrl_ReturnsEmptyAboveConfiguredMaxZoom() {
        using var service = new TileService();
        service.ParseUrlTemplate("xyz[3]:https://tiles.example.com/{z}/{x}/{y}", null);

        Assert.Equal(string.Empty, service.BuildTileUrl(4, 1, 1, null));
    }

    [Fact]
    public void ParseUrlTemplate_NormalizesCommonPlaceholders() {
        using var service = new TileService();

        service.ParseUrlTemplate("https://tiles.example.com/{zoom}/{TileCol}/{TileRow}", null);

        Assert.Equal("https://tiles.example.com/{z}/{x}/{y}", service.TileTemplate);
        Assert.False(service.IsTms);
    }

    [Fact]
    public void ParseUrlTemplate_DetectsTmsNegativeYAfterX() {
        using var service = new TileService();

        service.ParseUrlTemplate("https://tiles.example.com/{z}/{x}/{-y}", null);

        Assert.Equal("https://tiles.example.com/{z}/{x}/{y}", service.TileTemplate);
        Assert.True(service.IsTms);
    }

    [Fact]
    public void ParseUrlTemplate_AppliesMaxZoomPrefix() {
        using var service = new TileService();

        service.ParseUrlTemplate("xyz[19]:https://tiles.example.com/{zoom}/{x}/{y}", null);

        Assert.Equal("https://tiles.example.com/{z}/{x}/{y}", service.TileTemplate);
        Assert.Equal(19, service.MaxZoom);
        Assert.False(service.IsTms);
    }

    [Theory]
    [InlineData("bing[1,22]:https://example.com/maps/", null)]
    [InlineData("xyz:https://ecn.t3.tiles.virtualearth.net/tiles/a{quadkey}.jpeg?g=1", null)]
    [InlineData("https://maps.example.com/service", "WMS")]
    [InlineData("wms:https://maps.example.com/service", null)]
    public void ParseUrlTemplate_RejectsUnsupportedSources(string template, string? layerType) {
        using var service = new TileService();

        var error = Assert.Throws<NotSupportedException>(() => service.ParseUrlTemplate(template, null, layerType));

        Assert.NotEmpty(error.Message);
        Assert.False(TileSourceDefinition.IsSupported(template, layerType));
    }

    [Fact]
    public void ParseUrlTemplate_RecognizesBingMarkerWithoutUsingItAsATileUrl() {
        using var service = new TileService();

        service.ParseUrlTemplate("bing[1,22]:https://www.bing.com/maps/", null);

        Assert.True(service.IsBing);
        Assert.Null(service.TileTemplate);
        Assert.Equal(1, service.ImageMinZoom);
        Assert.Equal(22, service.ImageMaxZoom);
        Assert.True(TileSourceDefinition.IsSupported("bing[1,22]:https://www.bing.com/maps/"));
    }

    [Fact]
    public async Task InitializeSourceAsync_BingWithoutKeyDoesNotSendRequest() {
        var handler = new BingMetadataHandler(CreateBingMetadataJson());
        using var http = new HttpClient(handler);
        using var service = new TileService(http);
        service.ParseUrlTemplate("bing[1,22]:https://www.bing.com/maps/", null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InitializeSourceAsync(null));

        Assert.Contains("key", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task InitializeSourceAsync_UsesMetadataTemplateAndCachesMetadata() {
        var handler = new BingMetadataHandler(CreateBingMetadataJson());
        using var http = new HttpClient(handler);
        using var service = new TileService(http);
        service.ParseUrlTemplate("bing[1,22]:https://www.bing.com/maps/", null);
        service.ApplySourceOptions(22, 22);
        var cacheIdentityBeforeInitialization = service.CacheIdentity;

        await service.InitializeSourceAsync("abc$& xyz");
        await service.InitializeSourceAsync("abc$& xyz");

        Assert.Equal(1, handler.RequestCount);
        Assert.Contains("include=ImageryProviders", handler.LastRequestUri?.Query, StringComparison.Ordinal);
        Assert.Contains("key=abc%24%26%20xyz", handler.LastRequestUri?.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain("abc$& xyz", service.TileTemplate, StringComparison.Ordinal);
        Assert.Equal(cacheIdentityBeforeInitialization, service.CacheIdentity);
        Assert.Equal(1, service.ImageMinZoom);
        Assert.Equal(20, service.ImageMaxZoom);
        Assert.Equal("https://t0.tiles.virtualearth.net/tiles/a213.jpeg?g=123", service.BuildTileUrl(3, 3, 5, null));
    }

    [Fact]
    public async Task GetAttributions_FiltersBingProvidersByCoverageAndZoom() {
        var handler = new BingMetadataHandler(CreateBingMetadataJson());
        using var http = new HttpClient(handler);
        using var service = new TileService(http);
        service.ParseUrlTemplate("bing[1,22]:https://www.bing.com/maps/", null);
        await service.InitializeSourceAsync("key");

        var inside = service.GetAttributions(6, 15, 25, 25, 35);
        var outside = service.GetAttributions(6, -20, -20, -10, -10);
        var belowProviderZoom = service.GetAttributions(4, 15, 25, 25, 35);

        Assert.Collection(
            inside,
            attribution => Assert.Equal("Copyright Microsoft", attribution.Text),
            attribution => Assert.Equal("Global Provider", attribution.Text),
            attribution => Assert.Equal("Regional Provider", attribution.Text));
        Assert.DoesNotContain(outside, attribution => attribution.Text == "Regional Provider");
        Assert.DoesNotContain(belowProviderZoom, attribution => attribution.Text == "Regional Provider");
    }

    [Theory]
    [InlineData("tms[auto]:https://tiles.example.com/{zoom}/{x}/{y}")]
    [InlineData("tms:https://tiles.example.com/{zoom}/{x}/{y}")]
    public void ParseUrlTemplate_AutoMaxZoomPrefixDefersMaxZoomDetection(string template) {
        using var service = new TileService();

        service.ParseUrlTemplate(template, null);

        Assert.True(service.IsMaxZoomAuto);
        Assert.Equal(GeoConverter.MaxZoom, service.MaxZoom);
    }

    [Fact]
    public async Task ResolveAutoMaxZoomAsync_UsesHighestAvailableTile() {
        var handler = new ZoomProbeHandler(maxAvailableZoom: 3);
        using var http = new HttpClient(handler);
        using var service = new TileService(http) {
            TileTemplate = "https://tiles.example.com/{z}/{x}/{y}"
        };
        service.ParseUrlTemplate("tms[auto]:https://tiles.example.com/{z}/{y}/{x}", null);

        await service.ResolveAutoMaxZoomAsync(0, 0, null);

        Assert.Equal(3, service.MaxZoom);
        Assert.False(service.IsMaxZoomAuto);
    }

    [Fact]
    public void ParseUrlTemplate_TmsPrefixFlipsYForXBeforeYTemplates() {
        using var service = new TileService();

        service.ParseUrlTemplate("tms[19]:https://tiles.example.com/{zoom}/{x}/{y}", null);

        Assert.Equal(19, service.MaxZoom);
        Assert.True(service.IsTms);
        Assert.Equal("https://tiles.example.com/2/1/3", service.BuildTileUrl(2, 1, 0, null));
    }

    [Fact]
    public void ParseUrlTemplate_TmsPrefixKeepsArcGisRowColumnOrder() {
        using var service = new TileService();

        service.ParseUrlTemplate(
            "tms[19]:https://tiles.example.com/arcgis/rest/services/World_Imagery/MapServer/tile/{zoom}/{y}/{x}",
            null);

        Assert.Equal(19, service.MaxZoom);
        Assert.False(service.IsTms);
        Assert.Equal(
            "https://tiles.example.com/arcgis/rest/services/World_Imagery/MapServer/tile/2/0/1",
            service.BuildTileUrl(2, 1, 0, null));
    }

    [Fact]
    public void GetCacheBasePath_UsesNormalizedCacheRootAndWrappedX() {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "wpf-osm-editor-tests", Guid.NewGuid().ToString("N"));
        using var service = new TileService(cacheRoot: cacheRoot);

        var path = service.GetCacheBasePath(2, -1, 1);

        Assert.Equal(Path.Combine(Path.GetFullPath(cacheRoot), "default", "2", "3", "1"), path);
    }

    [Fact]
    public void GetCacheBasePath_SeparatesTileSources() {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "wpf-osm-editor-tests", Guid.NewGuid().ToString("N"));
        using var service = new TileService(cacheRoot: cacheRoot);

        service.TileTemplate = "https://a.example.com/{z}/{x}/{y}";
        var firstPath = service.GetCacheBasePath(2, 1, 1);

        service.TileTemplate = "https://b.example.com/{z}/{x}/{y}";
        var secondPath = service.GetCacheBasePath(2, 1, 1);

        Assert.NotEqual(firstPath, secondPath);
    }

    [Fact]
    public async Task GetTileBytesAsync_CachesDownloadedTileOnDisk() {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "wpf-osm-editor-tests", Guid.NewGuid().ToString("N"));
        var handler = new StubTileHandler(PngBytes);
        using var http = new HttpClient(handler);
        using var service = new TileService(http, cacheRoot) {
            TileTemplate = "https://tiles.example.com/{z}/{x}/{y}"
        };

        try {
            var first = await service.GetTileBytesAsync(2, 1, 1, null);
            var second = await service.GetTileBytesAsync(2, 1, 1, null);

            Assert.Equal(PngBytes, first);
            Assert.Equal(PngBytes, second);
            Assert.Equal(1, handler.RequestCount);
            Assert.NotNull(service.FindCachedFile(2, 1, 1));
        } finally {
            if (Directory.Exists(cacheRoot)) {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetTileBytesAsync_SendsDefaultUserAgent() {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "wpf-osm-editor-tests", Guid.NewGuid().ToString("N"));
        var handler = new StubTileHandler(PngBytes);
        using var http = new HttpClient(handler);
        using var service = new TileService(http, cacheRoot) {
            TileTemplate = "https://tiles.example.com/{z}/{x}/{y}"
        };

        try {
            await service.GetTileBytesAsync(2, 1, 1, null);

            Assert.Equal("WPF-OpenStreetmap-Editor/1.0", handler.LastUserAgent);
        } finally {
            if (Directory.Exists(cacheRoot)) {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetTileBytesAsync_MarksNoTileByEtagAndSkipsRepeatRequest() {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "wpf-osm-editor-tests", Guid.NewGuid().ToString("N"));
        var handler = new StubTileHandler(PngBytes) {
            ETag = "\"no-tile\""
        };
        using var http = new HttpClient(handler);
        using var service = new TileService(http, cacheRoot) {
            TileTemplate = "https://tiles.example.com/{z}/{x}/{y}"
        };
        service.ApplySourceOptions(GeoConverter.MaxZoom, GeoConverter.MaxZoom, ["no-tile"]);

        try {
            var first = await service.GetTileBytesAsync(2, 1, 1, null);
            var second = await service.GetTileBytesAsync(2, 1, 1, null);

            Assert.Null(first);
            Assert.Null(second);
            Assert.Equal(1, handler.RequestCount);
            Assert.Null(service.FindCachedFile(2, 1, 1));
            Assert.NotNull(service.FindNoTileMarker(2, 1, 1));
        } finally {
            if (Directory.Exists(cacheRoot)) {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetTileBytesAsync_MarksNoTileByMd5() {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "wpf-osm-editor-tests", Guid.NewGuid().ToString("N"));
        var handler = new StubTileHandler(PngBytes);
        using var http = new HttpClient(handler);
        using var service = new TileService(http, cacheRoot) {
            TileTemplate = "https://tiles.example.com/{z}/{x}/{y}"
        };
        var md5 = Convert.ToHexString(MD5.HashData(PngBytes)).ToLowerInvariant();
        service.ApplySourceOptions(GeoConverter.MaxZoom, GeoConverter.MaxZoom, noTileMd5s: [md5]);

        try {
            var bytes = await service.GetTileBytesAsync(2, 1, 1, null);

            Assert.Null(bytes);
            Assert.Equal(1, handler.RequestCount);
            Assert.NotNull(service.FindNoTileMarker(2, 1, 1));
        } finally {
            if (Directory.Exists(cacheRoot)) {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TryReadCachedTileAsync_ReadsCachedTile() {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "wpf-osm-editor-tests", Guid.NewGuid().ToString("N"));
        using var service = new TileService(cacheRoot: cacheRoot) {
            TileTemplate = "https://tiles.example.com/{z}/{x}/{y}"
        };

        try {
            var path = service.GetCacheBasePath(2, 1, 1) + ".png";
            await File.WriteAllBytesAsync(path, PngBytes);

            var bytes = await service.TryReadCachedTileAsync(2, 1, 1);

            Assert.Equal(PngBytes, bytes);
        } finally {
            if (Directory.Exists(cacheRoot)) {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetTileBytesAsync_RejectsResponseAboveByteLimit() {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "wpf-osm-editor-tests", Guid.NewGuid().ToString("N"));
        var handler = new StubTileHandler(new byte[TileImageValidator.MaxResponseBytes + 1]);
        using var http = new HttpClient(handler);
        using var service = new TileService(http, cacheRoot) {
            TileTemplate = "https://tiles.example.com/{z}/{x}/{y}"
        };

        try {
            var bytes = await service.GetTileBytesAsync(2, 1, 1, null);

            Assert.Null(bytes);
            Assert.Null(service.FindCachedFile(2, 1, 1));
        } finally {
            if (Directory.Exists(cacheRoot)) {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetTileBytesAsync_RejectsUnsupportedMediaType() {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "wpf-osm-editor-tests", Guid.NewGuid().ToString("N"));
        var handler = new StubTileHandler(PngBytes) { MediaType = "application/octet-stream" };
        using var http = new HttpClient(handler);
        using var service = new TileService(http, cacheRoot) {
            TileTemplate = "https://tiles.example.com/{z}/{x}/{y}"
        };

        try {
            Assert.Null(await service.GetTileBytesAsync(2, 1, 1, null));
            Assert.Null(service.FindCachedFile(2, 1, 1));
        } finally {
            if (Directory.Exists(cacheRoot)) {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetTileBytesAsync_RejectsImageAbovePixelLimit() {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "wpf-osm-editor-tests", Guid.NewGuid().ToString("N"));
        var oversizedPngHeader = Convert.FromHexString(
            "89504E470D0A1A0A0000000D494844520000100000001000");
        var handler = new StubTileHandler(oversizedPngHeader);
        using var http = new HttpClient(handler);
        using var service = new TileService(http, cacheRoot) {
            TileTemplate = "https://tiles.example.com/{z}/{x}/{y}"
        };

        try {
            Assert.Null(await service.GetTileBytesAsync(2, 1, 1, null));
            Assert.Null(service.FindCachedFile(2, 1, 1));
        } finally {
            if (Directory.Exists(cacheRoot)) {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    private sealed class StubTileHandler(byte[] responseBytes) : HttpMessageHandler {
        public string? ETag { get; init; }
        public string? MediaType { get; init; } = "image/png";
        public int RequestCount { get; private set; }
        public string? LastUserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            RequestCount++;
            LastUserAgent = request.Headers.UserAgent.ToString();
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new ByteArrayContent(responseBytes)
            };
            if (!string.IsNullOrEmpty(MediaType)) {
                response.Content.Headers.ContentType = new(MediaType);
            }
            if (!string.IsNullOrEmpty(ETag)) {
                response.Headers.ETag = EntityTagHeaderValue.Parse(ETag);
            }

            return Task.FromResult(response);
        }
    }

    private static string CreateBingMetadataJson() {
        return """
            {
              "authenticationResultCode": "ValidCredentials",
              "copyright": "Copyright Microsoft",
              "resourceSets": [
                {
                  "resources": [
                    {
                      "imageUrl": "https://{subdomain}.tiles.virtualearth.net/tiles/a{quadkey}.jpeg?g=123",
                      "imageUrlSubdomains": ["t0", "t1"],
                      "zoomMin": 1,
                      "zoomMax": 20,
                      "imageryProviders": [
                        {
                          "attribution": "Global Provider",
                          "coverageAreas": [
                            { "zoomMin": 1, "zoomMax": 20, "bbox": [-90, -180, 90, 180] }
                          ]
                        },
                        {
                          "attribution": "Regional Provider",
                          "coverageAreas": [
                            { "zoomMin": 5, "zoomMax": 10, "bbox": [10, 20, 30, 40] }
                          ]
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """;
    }

    private sealed class BingMetadataHandler(string metadataJson) : HttpMessageHandler {
        public int RequestCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new StringContent(metadataJson, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ZoomProbeHandler(int maxAvailableZoom) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var zoom = int.Parse(request.RequestUri!.Segments[^3].TrimEnd('/'));
            var response = zoom <= maxAvailableZoom
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                    Content = new ByteArrayContent(PngBytes)
                }
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            if (response.Content is not null) {
                response.Content.Headers.ContentType = new("image/png");
            }

            return Task.FromResult(response);
        }
    }
}
