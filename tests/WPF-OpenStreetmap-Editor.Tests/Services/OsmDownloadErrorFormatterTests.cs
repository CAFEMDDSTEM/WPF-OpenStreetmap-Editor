using System.Net;
using System.Net.Http;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class OsmDownloadErrorFormatterTests {
    [Fact]
    public void GetMessage_BadRequest_AsksUserToShrinkSelection() {
        var exception = new HttpRequestException("Bad Request", null, HttpStatusCode.BadRequest);

        var message = OsmDownloadErrorFormatter.GetMessage(exception);

        Assert.Equal("The OSM server rejected this area. Shrink the selected area and try again.", message);
        Assert.DoesNotContain("Bad Request", message);
    }

    [Fact]
    public void GetMessage_TooManyRequests_AsksUserToRetryLater() {
        var exception = new HttpRequestException("Too Many Requests", null, HttpStatusCode.TooManyRequests);

        var message = OsmDownloadErrorFormatter.GetMessage(exception);

        Assert.Equal("The OSM server received too many requests. Try again later.", message);
    }

    [Fact]
    public void GetMessage_TaskCanceled_ReportsTimeout() {
        var message = OsmDownloadErrorFormatter.GetMessage(new TaskCanceledException());

        Assert.Equal("The connection to the OSM server timed out. Check the network and try again.", message);
    }

    [Fact]
    public void GetMessage_HttpRequestWithoutStatus_ReportsNetworkFailure() {
        var message = OsmDownloadErrorFormatter.GetMessage(new HttpRequestException("Connection refused"));

        Assert.Equal("Could not connect to the OSM server. Check the network and try again.", message);
        Assert.DoesNotContain("Connection refused", message);
    }

    [Fact]
    public void GetMessage_SpatialDataLimit_PreservesMessage() {
        const string expected = "选择范围超过安全上限。";

        var message = OsmDownloadErrorFormatter.GetMessage(new SpatialDataLimitException(expected));

        Assert.Equal(expected, message);
    }

    [Fact]
    public void GetMessage_FallbackFailure_ExplainsBothServicesRejectedSelection() {
        var standardError = new HttpRequestException("Bad Request", null, HttpStatusCode.BadRequest);
        var fallbackError = new HttpRequestException("Gateway Timeout", null, HttpStatusCode.GatewayTimeout);

        var message = OsmDownloadErrorFormatter.GetMessage(
            new OsmDownloadFallbackException(standardError, fallbackError));

        Assert.Equal("The OSM standard API and Overpass API could not process this area. Shrink the selected area and try again.", message);
    }
}
