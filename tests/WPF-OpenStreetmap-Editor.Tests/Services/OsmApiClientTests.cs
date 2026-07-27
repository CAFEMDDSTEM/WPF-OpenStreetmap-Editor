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
    public async Task CreateChangesetAsync_UsesOAuthBearerHeader() {
        var handler = new CapturingHttpHandler(request => {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("token", request.Headers.Authorization?.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("123")
            };
        });
        var api = new OsmApiClient(new HttpClient(handler));

        var changesetId = await api.CreateChangesetAsync("http://127.0.0.1/", "token", "test");

        Assert.Equal(123, changesetId);
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
