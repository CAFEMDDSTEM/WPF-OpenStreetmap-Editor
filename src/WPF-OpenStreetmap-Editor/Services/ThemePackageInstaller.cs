using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace WPF_OpenStreetmap_Editor.Services;

public static class ThemePackageInstaller {
    private const long MaximumArchiveBytes = 20 * 1024 * 1024;
    private const long MaximumExpandedBytes = 32 * 1024 * 1024;
    private const int MaximumEntries = 64;
    private const int MaximumImageDimension = 4096;
    private const long MaximumImagePixels = 8L * 1024 * 1024;
    private const int MinimumIconDimension = 32;
    private const int MaximumIconDimension = 512;
    private const string InstalledImageName = "background.png";
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".wosm-theme",
        ".zip",
        ".7z"
    };

    public static ThemeDefinition Install(string sourcePath, string themesDirectory) {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath)) throw new FileNotFoundException("主题包不存在。", fullSourcePath);
        if (!SupportedExtensions.Contains(Path.GetExtension(fullSourcePath))) {
            throw new InvalidDataException("主题包必须使用 .wosm-theme、.zip 或 .7z 格式。");
        }

        ThemePackage package;
        try {
            package = ReadArchiveTheme(fullSourcePath);
        } catch (Exception ex) when (ex is InvalidFormatException or NotSupportedException) {
            throw new InvalidDataException("文件不是有效的 ZIP 或 7z 主题包。", ex);
        }

        var installedThemes = ThemeCatalog.Load(themesDirectory).Themes;
        if (installedThemes.Any(candidate =>
            string.Equals(candidate.Id, package.Theme.Id, StringComparison.OrdinalIgnoreCase))) {
            throw new IOException($"主题 “{package.Theme.Id}” 已安装，请先删除旧版本。");
        }

        Directory.CreateDirectory(themesDirectory);
        var destinationDirectory = Path.Combine(themesDirectory, package.Theme.Id);
        if (Directory.Exists(destinationDirectory) || File.Exists(destinationDirectory)) {
            throw new IOException($"主题目录 “{package.Theme.Id}” 已存在。");
        }

        var stagingDirectory = Path.Combine(themesDirectory, $".install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        try {
            var installedTheme = CopyForInstallation(package.Theme, package.Background is not null);
            ThemeCatalog.Write(Path.Combine(stagingDirectory, ThemeCatalog.ManifestName), installedTheme);
            WritePng(
                Path.Combine(stagingDirectory, ThemeCatalog.IconName),
                package.Icon,
                ThemeCatalog.MaximumIconFileBytes,
                "图标");
            WriteDescription(Path.Combine(stagingDirectory, ThemeCatalog.DescriptionName), package.Description);
            if (package.Background is not null) {
                WritePng(
                    Path.Combine(stagingDirectory, InstalledImageName),
                    package.Background,
                    ThemeCatalog.MaximumImageFileBytes,
                    "背景图片");
            }

            Directory.Move(stagingDirectory, destinationDirectory);
            return ThemeCatalog.Read(Path.Combine(destinationDirectory, ThemeCatalog.ManifestName));
        } catch {
            TryDeleteDirectory(stagingDirectory);
            throw;
        }
    }

    private static ThemePackage ReadArchiveTheme(string archivePath) {
        var archiveInfo = new FileInfo(archivePath);
        if (archiveInfo.Length <= 0 || archiveInfo.Length > MaximumArchiveBytes) {
            throw new InvalidDataException($"主题包必须小于 {MaximumArchiveBytes / 1024 / 1024} MB 且不能为空。");
        }

        using var archive = ArchiveFactory.OpenArchive(archivePath, ReaderOptions.ForFilePath);
        var entries = archive.Entries.Where(entry => !entry.IsDirectory).ToList();
        if (entries.Count == 0 || entries.Count > MaximumEntries) {
            throw new InvalidDataException($"主题包文件数量必须为 1-{MaximumEntries} 个。");
        }

        long expandedBytes = 0;
        var normalizedEntries = new Dictionary<string, IArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) {
            if (entry.IsEncrypted) throw new InvalidDataException("不支持加密主题包。");
            if (!TryNormalizeArchivePath(entry.Key, out var normalizedPath)) {
                throw new InvalidDataException($"主题包包含不安全路径：{entry.Key}");
            }

            if (entry.Size < 0 || entry.Size > MaximumExpandedBytes - expandedBytes) {
                throw new InvalidDataException($"主题包解压后不能超过 {MaximumExpandedBytes / 1024 / 1024} MB。");
            }
            expandedBytes += entry.Size;

            if (!normalizedEntries.TryAdd(normalizedPath, entry)) {
                throw new InvalidDataException($"主题包包含重复路径：{normalizedPath}");
            }
        }

        var manifestEntry = GetRequiredRootEntry(normalizedEntries, ThemeCatalog.ManifestName);
        var iconEntry = GetRequiredRootEntry(normalizedEntries, ThemeCatalog.IconName);
        var descriptionEntry = GetRequiredRootEntry(normalizedEntries, ThemeCatalog.DescriptionName);

        string manifestJson;
        using (var manifestStream = manifestEntry.OpenEntryStream()) {
            var manifestBytes = ReadBounded(manifestStream, ThemeCatalog.MaximumThemeFileBytes);
            manifestJson = ThemeCatalog.DecodeManifest(manifestBytes);
        }

        if (!ThemeCatalog.TryParse(manifestJson, null, out var theme, out var error)) {
            throw new InvalidDataException(error);
        }

        BitmapSource icon;
        using (var iconStream = iconEntry.OpenEntryStream()) {
            icon = DecodeIcon(ReadBounded(iconStream, ThemeCatalog.MaximumIconFileBytes));
        }

        string description;
        using (var descriptionStream = descriptionEntry.OpenEntryStream()) {
            description = ThemeCatalog.DecodeDescription(
                ReadBounded(descriptionStream, ThemeCatalog.MaximumDescriptionFileBytes));
        }

        BitmapSource? background = null;
        if (!string.IsNullOrEmpty(theme.BackgroundImage)) {
            if (!normalizedEntries.TryGetValue(theme.BackgroundImage, out var imageEntry) ||
                !normalizedEntries.Keys.Contains(theme.BackgroundImage, StringComparer.Ordinal)) {
                throw new InvalidDataException($"主题包缺少背景图片：{theme.BackgroundImage}");
            }

            using var imageStream = imageEntry.OpenEntryStream();
            background = DecodeBackground(ReadBounded(imageStream, ThemeCatalog.MaximumImageFileBytes));
        }

        var allowedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ThemeCatalog.ManifestName,
            ThemeCatalog.IconName,
            ThemeCatalog.DescriptionName
        };
        if (!string.IsNullOrEmpty(theme.BackgroundImage)) allowedEntries.Add(theme.BackgroundImage);
        var unexpectedEntry = normalizedEntries.Keys.FirstOrDefault(path => !allowedEntries.Contains(path));
        if (unexpectedEntry is not null) {
            throw new InvalidDataException($"主题包包含未声明的文件：{unexpectedEntry}");
        }

        return new ThemePackage(theme, icon, description, background);
    }

    internal static BitmapSource LoadIcon(string imagePath) {
        using var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return DecodeIcon(ReadBounded(stream, ThemeCatalog.MaximumIconFileBytes));
    }

    internal static BitmapSource LoadBackgroundImage(string imagePath) {
        using var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return DecodeBackground(ReadBounded(stream, ThemeCatalog.MaximumImageFileBytes));
    }

    internal static BitmapSource DecodeIcon(byte[] imageBytes) {
        if (!HasPngSignature(imageBytes)) {
            throw new InvalidDataException($"{ThemeCatalog.IconName} 必须是 PNG 文件。");
        }

        return DecodeBitmap(imageBytes, "主题图标", (width, height) =>
            width != height || width < MinimumIconDimension || width > MaximumIconDimension
                ? $"{ThemeCatalog.IconName} 必须是 {MinimumIconDimension}-{MaximumIconDimension} 像素的正方形 PNG。"
                : null);
    }

    private static BitmapSource DecodeBackground(byte[] imageBytes) {
        if (!HasPngSignature(imageBytes) && !HasJpegSignature(imageBytes)) {
            throw new InvalidDataException("背景图片必须是 PNG 或 JPEG 文件。");
        }

        return DecodeBitmap(imageBytes, "背景图片", (width, height) =>
            width > MaximumImageDimension ||
            height > MaximumImageDimension ||
            (long)width * height > MaximumImagePixels
                ? $"背景图片最大尺寸为 {MaximumImageDimension} x {MaximumImageDimension}，且不能超过 {MaximumImagePixels:N0} 像素。"
                : null);
    }

    private static BitmapSource DecodeBitmap(
        byte[] imageBytes,
        string displayName,
        Func<int, int, string?> validateDimensions) {
        try {
            using var stream = new MemoryStream(imageBytes, writable: false);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.None);
            var frame = decoder.Frames.FirstOrDefault() ?? throw new InvalidDataException($"{displayName}没有可读取的画面。");
            if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0) {
                throw new InvalidDataException($"{displayName}尺寸无效。");
            }
            var dimensionError = validateDimensions(frame.PixelWidth, frame.PixelHeight);
            if (dimensionError is not null) throw new InvalidDataException(dimensionError);

            var normalized = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Pbgra32, null, 0);
            normalized.Freeze();
            return normalized;
        } catch (Exception ex) when (ex is not InvalidDataException) {
            throw new InvalidDataException($"{displayName}不是有效的图片文件。", ex);
        }
    }

    private static void WritePng(string path, BitmapSource image, long maximumBytes, string displayName) {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        if (memory.Length > maximumBytes) {
            throw new InvalidDataException($"规范化后的{displayName}超过大小限制。");
        }

        memory.Position = 0;
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        memory.CopyTo(output);
    }

    private static void WriteDescription(string path, string description) {
        var bytes = new UTF8Encoding(false).GetBytes(description + Environment.NewLine);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        output.Write(bytes);
    }

    private static ThemeDefinition CopyForInstallation(ThemeDefinition theme, bool hasBackground) {
        return new ThemeDefinition {
            SchemaVersion = theme.SchemaVersion,
            Id = theme.Id,
            Name = theme.Name,
            Author = theme.Author,
            Version = theme.Version,
            BaseTheme = theme.BaseTheme,
            BackgroundImage = hasBackground ? InstalledImageName : "",
            BackgroundImageOpacity = theme.BackgroundImageOpacity,
            Colors = theme.Colors
        };
    }

    private static byte[] ReadBounded(Stream stream, long maximumBytes) {
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        while (true) {
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0) break;

            total += read;
            if (total > maximumBytes) throw new InvalidDataException($"主题包内文件超过 {maximumBytes / 1024} KB 限制。");
            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    private static IArchiveEntry GetRequiredRootEntry(
        IReadOnlyDictionary<string, IArchiveEntry> entries,
        string fileName) {
        if (!entries.TryGetValue(fileName, out var entry) ||
            !entries.Keys.Contains(fileName, StringComparer.Ordinal)) {
            throw new InvalidDataException($"主题包根目录缺少名称完全匹配的 {fileName}。");
        }
        return entry;
    }

    private static bool HasPngSignature(byte[] bytes) {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        return bytes.AsSpan().StartsWith(signature);
    }

    private static bool HasJpegSignature(byte[] bytes) {
        return bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
    }

    private static bool TryNormalizeArchivePath(string? value, out string normalized) {
        normalized = (value ?? "").Trim().Replace('\\', '/');
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 200 || normalized.StartsWith('/') || normalized.Contains(':')) {
            return false;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or "..")) return false;

        normalized = string.Join('/', segments);
        return true;
    }

    private static void TryDeleteDirectory(string path) {
        try {
            if (Directory.Exists(path)) {
                Directory.Delete(path, recursive: true);
            }
        } catch {
        }
    }

    private sealed record ThemePackage(
        ThemeDefinition Theme,
        BitmapSource Icon,
        string Description,
        BitmapSource? Background);
}
