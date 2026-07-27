using System.IO;
using System.Net;
using System.Net.Http;

namespace WPF_OpenStreetmap_Editor.Services;

public static class OsmDownloadErrorFormatter {
    public static string GetMessage(Exception exception) {
        var l = LocalizationService.Instance;
        return exception switch {
            SpatialDataLimitException or InvalidDataException => exception.Message,
            OsmDownloadFallbackException =>
                l.GetString("Osm.Download.Error.FallbackFailed"),
            HttpRequestException { StatusCode: HttpStatusCode.BadRequest } =>
                l.GetString("Osm.Download.Error.BadRequest"),
            HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } =>
                l.GetString("Osm.Download.Error.TooManyRequests"),
            HttpRequestException { StatusCode: { } statusCode } =>
                l.Format("Osm.Download.Error.HttpStatus", (int)statusCode),
            TaskCanceledException or TimeoutException =>
                l.GetString("Osm.Download.Error.Timeout"),
            HttpRequestException =>
                l.GetString("Osm.Download.Error.Connection"),
            _ => l.GetString("Osm.Download.Error.Generic")
        };
    }
}
