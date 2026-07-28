using System;

namespace WPF_OpenStreetmap_Editor.Services;

/// <summary>地理坐标 ↔ 瓦片/像素坐标的转换（Slippy Map / Web Mercator EPSG:3857）</summary>
public static class GeoConverter {
    public const int TileSize = 256;            // 标准瓦片尺寸（像素）
    public const double MaxLatitude = 85.05112878; // Web Mercator 有效纬度上限
    public const int MinZoom = 0;
    public const int MaxZoom = 22;

    /// <summary>将纬度限制在 Web Mercator 有效范围内</summary>
    public static double ClampLatitude(double lat) => Math.Max(Math.Min(lat, MaxLatitude), -MaxLatitude);

    /// <summary>给定 zoom 级别的瓦片总数（2^zoom）</summary>
    public static int GetTileCount(int zoom) => 1 << zoom;

    /// <summary>经纬度 → Slippy 瓦片索引 (TileX, TileY)</summary>
    public static (int TileX, int TileY) LatLonToTileXY(double lat, double lon, int zoom) {
        var n = GetTileCount(zoom);
        var latRad = ClampLatitude(lat) * Math.PI / 180.0;
        var tileX = (int)Math.Floor((lon + 180.0) / 360.0 * n);
        var tileY = (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);
        return (tileX, tileY);
    }

    /// <summary>经纬度 → 世界像素坐标 (0 … worldSize)，用于精确定位</summary>
    public static (double PixelX, double PixelY) LatLonToPixelXY(double lat, double lon, int zoom) {
        var n = GetTileCount(zoom);
        var world = TileSize * n;
        var latRad = ClampLatitude(lat) * Math.PI / 180.0;
        var px = (lon + 180.0) / 360.0 * world;
        var py = (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * world;
        return (px, py);
    }

    /// <summary>世界像素坐标 → 经纬度</summary>
    public static (double Lat, double Lon) PixelXYToLatLon(double pixelX, double pixelY, int zoom) {
        var n = GetTileCount(zoom);
        var world = TileSize * n;
        var lon = pixelX / world * 360.0 - 180.0;
        var nPi = Math.PI - 2.0 * Math.PI * pixelY / world;
        var lat = (180.0 / Math.PI) * Math.Atan(Math.Sinh(nPi));
        return (lat, lon);
    }
}
