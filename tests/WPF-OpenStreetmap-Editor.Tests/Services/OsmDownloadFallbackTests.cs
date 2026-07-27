using System.Net;
using System.Net.Http;
using System.Text;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class OsmDownloadFallbackTests {
    [Fact]
    public async Task DownloadMapAsync_BadRequest_RetriesWithOverpass() {
        var handler = new RecordingHandler(request =>
            request.RequestUri!.Host == "overpass-api.de"
                ? CreateResponse(HttpStatusCode.OK, "<osm version=\"0.6\" />")
                : CreateResponse(HttpStatusCode.BadRequest, "too many nodes"));
        var client = new OsmApiClient(new HttpClient(handler));

        var bytes = await client.DownloadMapAsync(
            OsmApiClient.DefaultApiBaseUrl,
            new GeoBounds(113.594947, 24.811502, 113.600752, 24.815013));

        Assert.Equal("<osm version=\"0.6\" />", Encoding.UTF8.GetString(bytes));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("api.openstreetmap.org", handler.Requests[0].Host);
        Assert.Equal("overpass-api.de", handler.Requests[1].Host);
    }

    [Fact]
    public async Task DownloadMapAsync_AboveStandardLimit_UsesOverpassDirectly() {
        var handler = new RecordingHandler(_ => CreateResponse(HttpStatusCode.OK, "<osm />"));
        var client = new OsmApiClient(new HttpClient(handler));

        await client.DownloadMapAsync(
            OsmApiClient.DefaultApiBaseUrl,
            new GeoBounds(0, 0, 1, 1));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("overpass-api.de", request.Host);
    }

    [Fact]
    public async Task DownloadMapAsync_BothServicesFail_ThrowsFallbackException() {
        var handler = new RecordingHandler(request =>
            request.RequestUri!.Host == "overpass-api.de"
                ? CreateResponse(HttpStatusCode.GatewayTimeout, "timeout")
                : CreateResponse(HttpStatusCode.BadRequest, "too many nodes"));
        var client = new OsmApiClient(new HttpClient(handler));

        await Assert.ThrowsAsync<OsmDownloadFallbackException>(() =>
            client.DownloadMapAsync(
                OsmApiClient.DefaultApiBaseUrl,
                new GeoBounds(113.594947, 24.811502, 113.600752, 24.815013)));
    }

    [Fact]
    public void ValidateDownloadBounds_RejectsUnsafeOverpassSelection() {
        var error = Assert.Throws<InvalidDataException>(() =>
            OsmApiClient.ValidateDownloadBounds(new GeoBounds(0, 0, 10, 10)));

        Assert.Contains("安全下载", error.Message);
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string body) {
        return new HttpResponseMessage(statusCode) {
            Content = new StringContent(body, Encoding.UTF8, "text/xml")
        };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(responseFactory(request));
        }
    }
}
