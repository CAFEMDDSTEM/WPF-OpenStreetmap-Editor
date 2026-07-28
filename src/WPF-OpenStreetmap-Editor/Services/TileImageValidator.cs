using System;
using System.Buffers.Binary;

namespace WPF_OpenStreetmap_Editor.Services;

/// <summary>瓦片图像验证：检查 PNG/GIF/BMP/JPEG 文件头和尺寸限制</summary>
public static class TileImageValidator {
    public const int MaxResponseBytes = 8 * 1024 * 1024;   // 8 MB
    public const int MaxDimension = 2048;                   // 最大宽/高
    public const long MaxPixelCount = 2048L * 2048;         // 最大像素数

    /// <summary>验证下载字节 + 媒体类型（仅在下载时使用）</summary>
    public static bool TryValidate(byte[] bytes, string? mediaType, out string extension) {
        extension = "";
        if (bytes.Length == 0 || bytes.Length > MaxResponseBytes) return false;
        if (!TryReadImageHeader(bytes, out extension, out var width, out var height)) return false;
        if (!IsAllowedMediaType(mediaType, extension)) return false;

        return width > 0 && height > 0 &&
            width <= MaxDimension && height <= MaxDimension &&
            (long)width * height <= MaxPixelCount;
    }

    /// <summary>验证本地缓存文件（不检查 mediaType）</summary>
    public static bool TryValidateCachedFile(byte[] bytes) {
        if (bytes.Length == 0 || bytes.Length > MaxResponseBytes) return false;
        if (!TryReadImageHeader(bytes, out _, out var width, out var height)) return false;
        return width > 0 && height > 0 &&
            width <= MaxDimension && height <= MaxDimension &&
            (long)width * height <= MaxPixelCount;
    }

    /// <summary>解析 PNG/GIF/BMP/JPEG 文件头，返回扩展名和宽高</summary>
    private static bool TryReadImageHeader(
        ReadOnlySpan<byte> bytes,
        out string extension,
        out int width,
        out int height) {
        extension = "";
        width = 0;
        height = 0;

        // PNG: 8 字节签名 + IHDR 块（12–24）
        if (bytes.Length >= 24 &&
            bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) &&
            bytes.Slice(12, 4).SequenceEqual("IHDR"u8)) {
            extension = ".png";
            width = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
            height = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
            return true;
        }

        // GIF: 6 字节签名 (GIF87a/GIF89a)
        if (bytes.Length >= 10 &&
            (bytes[..6].SequenceEqual("GIF87a"u8) || bytes[..6].SequenceEqual("GIF89a"u8))) {
            extension = ".gif";
            width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(6, 2));
            height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(8, 2));
            return true;
        }

        // BMP: "BM" 签名，偏移 18 开始读 DIB 头
        if (bytes.Length >= 26 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M') {
            extension = ".bmp";
            width = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(18, 4));
            var signedHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(22, 4));
            height = signedHeight == int.MinValue ? 0 : Math.Abs(signedHeight);
            return true;
        }

        // JPEG：扫描 SOF 段获取尺寸
        if (TryReadJpegDimensions(bytes, out width, out height)) {
            extension = ".jpg";
            return true;
        }

        return false;
    }

    /// <summary>遍历 JPEG 段，在 SOF0–SOF15 中读取宽高</summary>
    private static bool TryReadJpegDimensions(ReadOnlySpan<byte> bytes, out int width, out int height) {
        width = 0;
        height = 0;
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8) return false;

        var offset = 2;
        while (offset + 3 < bytes.Length) {
            // 跳过填充字节
            while (offset < bytes.Length && bytes[offset] == 0xFF) offset++;
            if (offset >= bytes.Length) return false;

            var marker = bytes[offset++];
            if (marker is 0xD8 or 0xD9) continue;       // SOI/EOI
            if (marker is 0x01 or >= 0xD0 and <= 0xD7) continue; // 无参数标记
            if (offset + 2 > bytes.Length) return false;

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            if (segmentLength < 2 || offset + segmentLength > bytes.Length) return false;
            if (IsStartOfFrame(marker) && segmentLength >= 7) {
                height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2));
                return true;
            }
            offset += segmentLength;
        }
        return false;
    }

    /// <summary>是否为 JPEG 开始帧标记（SOF0–SOF15）</summary>
    private static bool IsStartOfFrame(byte marker) {
        return marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or
            0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;
    }

    /// <summary>根据媒体类型字符串判断是否与扩展名匹配</summary>
    private static bool IsAllowedMediaType(string? mediaType, string extension) {
        if (string.IsNullOrWhiteSpace(mediaType)) return false;
        return extension switch {
            ".png" => mediaType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Equals("image/x-png", StringComparison.OrdinalIgnoreCase),
            ".jpg" => mediaType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Equals("image/jpg", StringComparison.OrdinalIgnoreCase),
            ".gif" => mediaType.Equals("image/gif", StringComparison.OrdinalIgnoreCase),
            ".bmp" => mediaType.Equals("image/bmp", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Equals("image/x-ms-bmp", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
