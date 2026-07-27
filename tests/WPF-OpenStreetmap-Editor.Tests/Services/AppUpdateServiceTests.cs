using System.Net;
using System.Text;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class AppUpdateServiceTests {
    [Fact]
    public async Task CheckAsync_ReturnsUpdateAvailableForNewerRelease() {
        var handler = new JsonResponseHandler("""
            [
              {
                "tag_name": "v0.1.0-beta.2",
                "name": "WOSM v0.1.0-beta.2",
                "html_url": "https://example.com/releases/v0.1.0-beta.2",
                "draft": false,
                "prerelease": true,
                "published_at": "2026-07-28T00:00:00Z"
              }
            ]
            """);
        using var http = new HttpClient(handler);
        using var updates = new AppUpdateService(http, "https://example.com/releases");

        var result = await updates.CheckAsync("0.1.0-beta.1");

        Assert.Equal(AppUpdateCheckState.UpdateAvailable, result.State);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("v0.1.0-beta.2", result.LatestRelease?.Version);
        Assert.Equal("https://example.com/releases", handler.LastRequestUri?.ToString());
        Assert.Equal("WPF-OpenStreetmap-Editor/0.1", handler.LastUserAgent);
    }

    [Fact]
    public async Task CheckAsync_ReturnsUpToDateWhenCurrentVersionMatchesLatest() {
        var handler = new JsonResponseHandler("""
            [
              {
                "tag_name": "v0.2.0",
                "name": "WOSM v0.2.0",
                "html_url": "https://example.com/releases/v0.2.0",
                "draft": false,
                "prerelease": false,
                "published_at": "2026-07-28T00:00:00Z"
              }
            ]
            """);
        using var http = new HttpClient(handler);
        using var updates = new AppUpdateService(http, "https://example.com/releases");

        var result = await updates.CheckAsync("0.2.0");

        Assert.Equal(AppUpdateCheckState.UpToDate, result.State);
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal("v0.2.0", result.LatestRelease?.Version);
    }

    [Fact]
    public async Task CheckAsync_IgnoresDraftsAndInvalidTags() {
        var handler = new JsonResponseHandler("""
            [
              {
                "tag_name": "v9.0.0",
                "html_url": "https://example.com/releases/v9.0.0",
                "draft": true,
                "prerelease": false
              },
              {
                "tag_name": "nightly",
                "html_url": "https://example.com/releases/nightly",
                "draft": false,
                "prerelease": false
              },
              {
                "tag_name": "v0.2.0",
                "html_url": "https://example.com/releases/v0.2.0",
                "draft": false,
                "prerelease": false
              }
            ]
            """);
        using var http = new HttpClient(handler);
        using var updates = new AppUpdateService(http, "https://example.com/releases");

        var result = await updates.CheckAsync("0.1.0");

        Assert.Equal(AppUpdateCheckState.UpdateAvailable, result.State);
        Assert.Equal("v0.2.0", result.LatestRelease?.Version);
    }

    [Fact]
    public async Task CheckAsync_ReturnsUnavailableForHttpFailure() {
        var handler = new JsonResponseHandler("[]", HttpStatusCode.Forbidden);
        using var http = new HttpClient(handler);
        using var updates = new AppUpdateService(http, "https://example.com/releases");

        var result = await updates.CheckAsync("0.1.0");

        Assert.Equal(AppUpdateCheckState.Unavailable, result.State);
        Assert.Null(result.LatestRelease);
        Assert.Contains("HTTP 403", result.Detail);
    }

    [Theory]
    [InlineData("0.1.0-beta.2", "0.1.0-beta.1", true)]
    [InlineData("0.1.0-beta.10", "0.1.0-beta.2", true)]
    [InlineData("0.1.0", "0.1.0-beta.10", true)]
    [InlineData("0.1.0-beta.1", "0.1.0", false)]
    [InlineData("v0.2.0", "0.1.9", true)]
    public void IsNewerVersion_HandlesSemanticVersionOrdering(
        string candidateVersion,
        string currentVersion,
        bool expected) {
        Assert.Equal(expected, AppUpdateService.IsNewerVersion(candidateVersion, currentVersion));
    }

    private sealed class JsonResponseHandler(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler {
        public Uri? LastRequestUri { get; private set; }
        public string? LastUserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) {
            LastRequestUri = request.RequestUri;
            LastUserAgent = request.Headers.UserAgent.ToString();
            var response = new HttpResponseMessage(statusCode) {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
