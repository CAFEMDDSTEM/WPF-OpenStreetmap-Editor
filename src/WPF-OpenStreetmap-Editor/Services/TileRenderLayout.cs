using System;
using System.Runtime.CompilerServices;

namespace WPF_OpenStreetmap_Editor.Services;

/// <summary>瓦片在视图中的位置</summary>
public readonly record struct TilePlacement(double Left, double Top, double Width, double Height);

/// <summary>可视瓦片索引范围（含边界）</summary>
public readonly record struct TileRange(int StartX, int EndX, int StartY, int EndY);

/// <summary>瓦片布局计算：位置偏移、可视范围、像素对齐</summary>
public static class TileRenderLayout {
    /// <summary>计算单个瓦片的 Canvas 位置（相对于视口中心像素）</summary>
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

    /// <summary>计算瓦片在渲染级别不同时的缩放位置（renderZoom ≥ tileZoom）</summary>
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

    /// <summary>
    /// 计算当前视口可见的瓦片索引范围（带额外缓冲区）。
    /// TileX 和 TileY 基于 Slippy Map 标准（原点左上，X 右 Y 下）。
    /// </summary>
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

    /// <summary>将值对齐到最近的设备像素（抗亚像素模糊）</summary>
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
