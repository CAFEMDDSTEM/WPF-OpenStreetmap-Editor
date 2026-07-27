using System.Net;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class StartupDiagnosticsServiceTests {
    [Fact]
    public async Task ProbeTileSourceAsync_ReturnsPassedForSuccessfulImageResponse() {
        var handler = new ProbeHandler(HttpStatusCode.OK, "image/png");
        using var http = new HttpClient(handler);
        using var diagnostics = new StartupDiagnosticsService(new AppSettings { TileSources = [] }, http);
        var source = new TileSourcePreset {
            Name = "OSM",
            Source = "xyz:https://tiles.example.com/{z}/{x}/{y}.png",
            MapMaxZoom = GeoConverter.MaxZoom,
            ImageMaxZoom = 19
        };

        var result = await diagnostics.ProbeTileSourceAsync(source);

        Assert.Equal(StartupCheckState.Passed, result.State);
        Assert.Equal("https://tiles.example.com/1/1/1.png", handler.LastRequestUri?.ToString());
        Assert.False(handler.SentNoCache);
    }

    [Fact]
    public async Task ProbeTileSourceAsync_ReturnsWarningForHttpFailure() {
        var handler = new ProbeHandler(HttpStatusCode.Forbidden, "text/plain");
        using var http = new HttpClient(handler);
        using var diagnostics = new StartupDiagnosticsService(new AppSettings { TileSources = [] }, http);
        var source = new TileSourcePreset {
            Name = "Blocked",
            Source = "xyz:https://tiles.example.com/{z}/{x}/{y}.png",
            MapMaxZoom = GeoConverter.MaxZoom,
            ImageMaxZoom = 19
        };

        var result = await diagnostics.ProbeTileSourceAsync(source);

        Assert.Equal(StartupCheckState.Warning, result.State);
        Assert.Contains("HTTP 403", result.Detail);
    }

    [Fact]
    public async Task ProbeTileSourceAsync_SkipsSourceWithoutRequiredToken() {
        var handler = new ProbeHandler(HttpStatusCode.OK, "image/png");
        using var http = new HttpClient(handler);
        using var diagnostics = new StartupDiagnosticsService(new AppSettings { TileSources = [] }, http);
        var source = new TileSourcePreset {
            Name = "Token",
            Source = "xyz:https://tiles.example.com/{z}/{x}/{y}.png?access_token={access_token}",
            MapMaxZoom = GeoConverter.MaxZoom,
            ImageMaxZoom = 19
        };

        var result = await diagnostics.ProbeTileSourceAsync(source);

        Assert.Equal(StartupCheckState.Skipped, result.State);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ProbeTileSourceAsync_SkipsBingWithoutUserKey() {
        var handler = new ProbeHandler(HttpStatusCode.OK, "image/jpeg");
        using var http = new HttpClient(handler);
        using var diagnostics = new StartupDiagnosticsService(new AppSettings { TileSources = [] }, http);
        var source = new TileSourcePreset {
            Name = "Bing aerial imagery",
            Source = "bing[1,22]:https://www.bing.com/maps/",
            MapMaxZoom = GeoConverter.MaxZoom,
            ImageMaxZoom = GeoConverter.MaxZoom
        };

        var result = await diagnostics.ProbeTileSourceAsync(source);

        Assert.Equal(StartupCheckState.Skipped, result.State);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public void IsMemoryLow_UsesAvailableBytesAndRatio() {
        Assert.False(StartupDiagnosticsService.IsMemoryLow(8UL * 1024 * 1024 * 1024, 2UL * 1024 * 1024 * 1024));
        Assert.True(StartupDiagnosticsService.IsMemoryLow(8UL * 1024 * 1024 * 1024, 256UL * 1024 * 1024));
        Assert.True(StartupDiagnosticsService.IsMemoryLow(64UL * 1024 * 1024 * 1024, 3UL * 1024 * 1024 * 1024));
    }

    [Fact]
    public void IsDiskLow_UsesAvailableBytesAndRatio() {
        Assert.False(StartupDiagnosticsService.IsDiskLow(100L * 1024 * 1024 * 1024, 20L * 1024 * 1024 * 1024));
        Assert.True(StartupDiagnosticsService.IsDiskLow(100L * 1024 * 1024 * 1024, 256L * 1024 * 1024));
        Assert.True(StartupDiagnosticsService.IsDiskLow(100L * 1024 * 1024 * 1024, 2L * 1024 * 1024 * 1024));
    }

    private sealed class ProbeHandler(HttpStatusCode statusCode, string mediaType) : HttpMessageHandler {
        public int RequestCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public bool SentNoCache { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            SentNoCache = request.Headers.CacheControl?.NoCache == true;
            var response = new HttpResponseMessage(statusCode) {
                Content = new ByteArrayContent([])
            };
            response.Content.Headers.ContentType = new(mediaType);
            return Task.FromResult(response);
        }
    }
}
