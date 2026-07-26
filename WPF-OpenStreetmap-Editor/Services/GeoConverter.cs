using System;

namespace WPF_OpenStreetmap_Editor.Services;

public static class GeoConverter {
    public const int TileSize = 256;
    public const double MaxLatitude = 85.05112878;
    public const int MinZoom = 0;
    public const int MaxZoom = 22;

    public static double ClampLatitude(double lat) =>
        Math.Max(Math.Min(lat, MaxLatitude), -MaxLatitude);

    public static int GetTileCount(int zoom) => 1 << zoom;

    public static (int TileX, int TileY) LatLonToTileXY(double lat, double lon, int zoom) {
        int n = GetTileCount(zoom);
        double latRad = ClampLatitude(lat) * Math.PI / 180.0;
        int tileX = (int)Math.Floor((lon + 180.0) / 360.0 * n);
        int tileY = (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);
        return (tileX, tileY);
    }

    public static (double PixelX, double PixelY) LatLonToPixelXY(double lat, double lon, int zoom) {
        int n = GetTileCount(zoom);
        double world = TileSize * n;
        double latRad = ClampLatitude(lat) * Math.PI / 180.0;
        double px = (lon + 180.0) / 360.0 * world;
        double py = (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * world;
        return (px, py);
    }

    public static (double Lat, double Lon) PixelXYToLatLon(double pixelX, double pixelY, int zoom) {
        int n = GetTileCount(zoom);
        double world = TileSize * n;
        double lon = pixelX / world * 360.0 - 180.0;
        double nPi = Math.PI - 2.0 * Math.PI * pixelY / world;
        double lat = (180.0 / Math.PI) * Math.Atan(Math.Sinh(nPi));
        return (lat, lon);
    }
}
