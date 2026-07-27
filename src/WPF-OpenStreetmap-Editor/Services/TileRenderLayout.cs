using System;
using System.Runtime.CompilerServices;

namespace WPF_OpenStreetmap_Editor.Services;

public readonly record struct TilePlacement(double Left, double Top, double Width, double Height);

public readonly record struct TileRange(int StartX, int EndX, int StartY, int EndY);

public static class TileRenderLayout {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TilePlacement GetTilePlacement(
        int tileX,
        int tileY,
        double centerPixelX,
        double centerPixelY,
        double viewportWidth,
        double viewportHeight) {
        var left = tileX * GeoConverter.TileSize - centerPixelX + viewportWidth / 2.0;
        var top = tileY * GeoConverter.TileSize - centerPixelY + viewportHeight / 2.0;

        return new TilePlacement(left, top, GeoConverter.TileSize, GeoConverter.TileSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TilePlacement GetTilePlacement(
        int tileX,
        int tileY,
        int tileZoom,
        int renderZoom,
        double centerPixelX,
        double centerPixelY,
        double viewportWidth,
        double viewportHeight) {
        var zoomDelta = Math.Max(0, renderZoom - tileZoom);
        var scale = 1 << zoomDelta;
        var tileSize = GeoConverter.TileSize * (double)scale;
        var left = tileX * tileSize - centerPixelX + viewportWidth / 2.0;
        var top = tileY * tileSize - centerPixelY + viewportHeight / 2.0;

        return new TilePlacement(left, top, tileSize, tileSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TileRange GetVisibleTileRange(
        double centerPixelX,
        double centerPixelY,
        double viewportWidth,
        double viewportHeight,
        int zoom,
        int tileBuffer) {
        var tileMargin = Math.Max(0, tileBuffer) * GeoConverter.TileSize;
        var startX = (int)Math.Floor((centerPixelX - viewportWidth / 2.0 - tileMargin) / GeoConverter.TileSize);
        var endX = (int)Math.Floor((centerPixelX + viewportWidth / 2.0 + tileMargin) / GeoConverter.TileSize);
        var startY = (int)Math.Floor((centerPixelY - viewportHeight / 2.0 - tileMargin) / GeoConverter.TileSize);
        var endY = (int)Math.Floor((centerPixelY + viewportHeight / 2.0 + tileMargin) / GeoConverter.TileSize);
        var maxTile = GeoConverter.GetTileCount(zoom) - 1;

        return new TileRange(
            Math.Clamp(startX, 0, maxTile),
            Math.Clamp(endX, 0, maxTile),
            Math.Clamp(startY, 0, maxTile),
            Math.Clamp(endY, 0, maxTile));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double SnapToDevicePixel(double value, double dpiScale) {
        dpiScale = NormalizeDpiScale(dpiScale);
        return Math.Round(value * dpiScale, MidpointRounding.AwayFromZero) / dpiScale;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double NormalizeDpiScale(double dpiScale) {
        return double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1.0;
    }
}
