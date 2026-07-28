using System.Text;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class OsmPlaceSearchServiceTests {
    [Fact]
    public async Task SearchAsync_RequestsNominatimAndParsesTopResult() {
        var handler = new CapturingHandler(request => {
            Assert.Equal("nominatim.openstreetmap.org", request.RequestUri!.Host);
            Assert.Contains("format=jsonv2", request.RequestUri.Query, StringComparison.Ordinal);
            Assert.Contains("limit=1", request.RequestUri.Query, StringComparison.Ordinal);
            Assert.Contains("addressdetails=1", request.RequestUri.Query, StringComparison.Ordinal);
            Assert.Contains("q=Singapore", request.RequestUri.Query, StringComparison.Ordinal);
            Assert.Contains("WPF-OpenStreetmap-Editor/1.0", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new StringContent("""
                    [
                      {
                        "display_name": "Singapore",
                        "lat": "1.3521",
                        "lon": "103.8198",
                        "boundingbox": ["1.2046", "1.4784", "103.685", "104.012"]
                      }
                    ]
                    """, Encoding.UTF8, "application/json")
            };
        });
        using var service = new OsmPlaceSearchService(new HttpClient(handler));

        var result = await service.SearchAsync("Singapore");

        Assert.NotNull(result);
        Assert.Equal("Singapore", result!.DisplayName);
        Assert.Equal(103.8198, result.Center.Longitude, 4);
        Assert.Equal(1.3521, result.Center.Latitude, 4);
        Assert.NotNull(result.Bounds);
        Assert.Equal(103.685, result.Bounds!.Value.MinLongitude, 3);
        Assert.Equal(1.2046, result.Bounds.Value.MinLatitude, 4);
        Assert.Equal(104.012, result.Bounds.Value.MaxLongitude, 3);
        Assert.Equal(1.4784, result.Bounds.Value.MaxLatitude, 4);
        Assert.Equal(1, handler.RequestCount);
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            RequestCount++;
            return Task.FromResult(responseFactory(request));
        }
    }
}
