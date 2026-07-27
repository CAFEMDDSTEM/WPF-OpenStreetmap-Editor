using WPF_OpenStreetmap_Editor.Services;

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

    private sealed class StubTileHandler(byte[] responseBytes) : HttpMessageHandler {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            RequestCount++;
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new ByteArrayContent(responseBytes)
            };
            return Task.FromResult(response);
        }
    }
}
