using System.IO;
using System.Net;
using System.Net.Http;

namespace WPF_OpenStreetmap_Editor.Services;

public static class OsmDownloadErrorFormatter {
    public static string GetMessage(Exception exception) {
        return exception switch {
            SpatialDataLimitException or InvalidDataException => exception.Message,
            OsmDownloadFallbackException =>
                "OSM 标准接口和 Overpass API 都无法处理这个范围。请缩小选择区域后重试。",
            HttpRequestException { StatusCode: HttpStatusCode.BadRequest } =>
                "OSM 服务器拒绝了这个范围。请缩小选择区域后重试。",
            HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } =>
                "OSM 服务器请求过于频繁。请稍后重试。",
            HttpRequestException { StatusCode: { } statusCode } =>
                $"OSM 服务器请求失败（HTTP {(int)statusCode}）。请稍后重试。",
            TaskCanceledException or TimeoutException =>
                "连接 OSM 服务器超时。请检查网络后重试。",
            HttpRequestException =>
                "无法连接 OSM 服务器。请检查网络后重试。",
            _ => "下载 OSM 数据失败。请稍后重试。"
        };
    }
}
