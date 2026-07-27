using System.Net;
using System.Net.Http;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class OsmDownloadErrorFormatterTests {
    [Fact]
    public void GetMessage_BadRequest_AsksUserToShrinkSelection() {
        var exception = new HttpRequestException("Bad Request", null, HttpStatusCode.BadRequest);

        var message = OsmDownloadErrorFormatter.GetMessage(exception);

        Assert.Equal("OSM 服务器拒绝了这个范围。请缩小选择区域后重试。", message);
        Assert.DoesNotContain("Bad Request", message);
    }

    [Fact]
    public void GetMessage_TooManyRequests_AsksUserToRetryLater() {
        var exception = new HttpRequestException("Too Many Requests", null, HttpStatusCode.TooManyRequests);

        var message = OsmDownloadErrorFormatter.GetMessage(exception);

        Assert.Equal("OSM 服务器请求过于频繁。请稍后重试。", message);
    }

    [Fact]
    public void GetMessage_TaskCanceled_ReportsTimeout() {
        var message = OsmDownloadErrorFormatter.GetMessage(new TaskCanceledException());

        Assert.Equal("连接 OSM 服务器超时。请检查网络后重试。", message);
    }

    [Fact]
    public void GetMessage_HttpRequestWithoutStatus_ReportsNetworkFailure() {
        var message = OsmDownloadErrorFormatter.GetMessage(new HttpRequestException("Connection refused"));

        Assert.Equal("无法连接 OSM 服务器。请检查网络后重试。", message);
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

        Assert.Equal("OSM 标准接口和 Overpass API 都无法处理这个范围。请缩小选择区域后重试。", message);
    }
}
