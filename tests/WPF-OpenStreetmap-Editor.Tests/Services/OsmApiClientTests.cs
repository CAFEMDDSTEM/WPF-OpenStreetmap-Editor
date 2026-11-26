using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class OsmApiClientTests {
    [Fact]
    public void ValidateBounds_AcceptsSmallValidSelection() {
        OsmApiClient.ValidateBounds(new GeoBounds(103.8, 1.3, 103.9, 1.4));
    }

    [Fact]
    public void ValidateBounds_RejectsSelectionAboveApiLimit() {
        Assert.Throws<InvalidDataException>(() =>
            OsmApiClient.ValidateBounds(new GeoBounds(0, 0, 1, 1)));
    }

    [Fact]
    public async Task GetUserDisplayNameAsync_UsesBasicCredentialHeader() {
        var handler = new CapturingHttpHandler(request => {
            Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
            Assert.Equal(
                Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:secret")),
                request.Headers.Authorization?.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("<osm><user display_name=\"Alice\" /></osm>")
            };
        });
        var api = new OsmApiClient(new HttpClient(handler));

        var displayName = await api.GetUserDisplayNameAsync(
            "http://127.0.0.1/",
            new OsmAccountCredential(OsmAuthenticationMethod.BasicPassword, "alice", "secret"));

        Assert.Equal("Alice", displayName);
    }

    [Fact]
    public async Task CreateChangesetAsync_UsesOAuthBearerHeaderAndVersionedCreatedByTag() {
        var handler = new CapturingHttpHandler(request => {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("token", request.Headers.Authorization?.Parameter);
            var tags = XDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())
                .Descendants("tag")
                .ToDictionary(
                    tag => tag.Attribute("k")!.Value,
                    tag => tag.Attribute("v")!.Value,
                    StringComparer.Ordinal);
            Assert.Equal(
                $"WPF-OpenStreetmap-Editor+{HelpContentService.GetVersionText(typeof(OsmApiClient).Assembly)}",
                tags["created_by"]);
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("123")
            };
        });
        var api = new OsmApiClient(new HttpClient(handler));

        var changesetId = await api.CreateChangesetAsync("http://127.0.0.1/", "token", "test");

        Assert.Equal(123, changesetId);
    }

    [Fact]
    public async Task UploadChangesAsync_RetriesOversizedDiffAsGzip() {
        var requestCount = 0;
        var handler = new CapturingHttpHandler(request => {
            requestCount++;
            if (requestCount == 1) {
                Assert.Empty(request.Content!.Headers.ContentEncoding);
                return new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge) {
                    Content = new StringContent("Request too large")
                };
            }

            Assert.Equal("gzip", Assert.Single(request.Content!.Headers.ContentEncoding));
            using var compressed = new MemoryStream(request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            Assert.Equal("<osmChange />", reader.ReadToEnd());
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("<diffResult />")
            };
        });
        var api = new OsmApiClient(new HttpClient(handler));

        var response = await api.UploadChangesAsync("http://127.0.0.1/", "token", 123, "<osmChange />");

        Assert.Equal("<diffResult />", response);
        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task UploadChangesAsync_ReportsOriginal413WhenServerRejectsGzip() {
        var requestCount = 0;
        var handler = new CapturingHttpHandler(request => {
            requestCount++;
            return requestCount == 1
                ? new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge) {
                    Content = new StringContent("Request too large")
                }
                : new HttpResponseMessage(HttpStatusCode.UnsupportedMediaType);
        });
        var api = new OsmApiClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<OsmApiException>(() => api.UploadChangesAsync(
            "http://127.0.0.1/",
            "token",
            123,
            "<osmChange />"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, exception.ResponseStatusCode);
        Assert.Equal("Request too large", exception.ResponseBody);
        Assert.Equal(2, requestCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Invalid access token", false)]
    [InlineData(HttpStatusCode.Unauthorized, "Missing write_api permission", true)]
    [InlineData(HttpStatusCode.Forbidden, "Not authorized", true)]
    public async Task UploadChangesAsync_PreservesAuthorizationFailureDetails(
        HttpStatusCode statusCode,
        string responseBody,
        bool expectedWritePermissionFailure) {
        var handler = new CapturingHttpHandler(_ => new HttpResponseMessage(statusCode) {
            Content = new StringContent(responseBody)
        });
        var api = new OsmApiClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<OsmApiException>(() => api.UploadChangesAsync(
            "http://127.0.0.1/",
            "token",
            123,
            "<osmChange />"));

        Assert.Equal(statusCode, exception.ResponseStatusCode);
        Assert.Equal(responseBody, exception.ResponseBody);
        Assert.Equal(expectedWritePermissionFailure, exception.IsInsufficientWritePermission);
    }

    [Fact]
    public async Task CreateChangesetAsync_WritesSourceAndReviewTags() {
        var handler = new CapturingHttpHandler(request => {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var tags = XDocument.Parse(body)
                .Descendants("tag")
                .ToDictionary(
                    tag => tag.Attribute("k")!.Value,
                    tag => tag.Attribute("v")!.Value,
                    StringComparer.Ordinal);
            Assert.Equal("survey", tags["source"]);
            Assert.Equal("yes", tags["review_requested"]);
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("124")
            };
        });
        var api = new OsmApiClient(new HttpClient(handler));

        var changesetId = await api.CreateChangesetAsync(
            "http://127.0.0.1/",
            new OsmAccountCredential(OsmAuthenticationMethod.OAuth2, "", "token"),
            "test",
            "survey",
            reviewRequested: true);

        Assert.Equal(124, changesetId);
    }

    private sealed class CapturingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            return Task.FromResult(responseFactory(request));
        }
    }
}
