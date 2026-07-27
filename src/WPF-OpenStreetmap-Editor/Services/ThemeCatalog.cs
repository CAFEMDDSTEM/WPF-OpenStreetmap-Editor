using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WPF_OpenStreetmap_Editor.Services;

public static partial class ThemeCatalog {
    private const int CurrentSchemaVersion = 1;
    internal const string ManifestName = "theme.json";
    internal const string IconName = "icon.png";
    internal const string DescriptionName = "README.md";
    internal const long MaximumThemeFileBytes = 64 * 1024;
    internal const long MaximumImageFileBytes = 8 * 1024 * 1024;
    internal const long MaximumIconFileBytes = 1024 * 1024;
    internal const long MaximumDescriptionFileBytes = 32 * 1024;
    internal const int MaximumDescriptionCharacters = 8000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> ReservedIds = new(StringComparer.OrdinalIgnoreCase) {
        ThemeService.SystemThemeId,
        ThemeService.LightThemeId,
        ThemeService.DarkThemeId
    };
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };
    private static readonly JsonSerializerOptions WriteOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private static readonly Lazy<IReadOnlyList<ThemeDefinition>> BuiltInThemes = new(CreateBuiltInThemeCatalog);

    public static ThemeCatalogResult Load(string themesDirectory) {
        List<ThemeDefinition> themes = [.. CreateBuiltInThemes()];
        List<string> errors = [];

        if (!Directory.Exists(themesDirectory)) {
            return new ThemeCatalogResult(themes, errors);
        }

        IReadOnlyList<string> themePaths;
        try {
            themePaths = Directory.EnumerateDirectories(themesDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => !Path.GetFileName(path).StartsWith('.'))
                .Select(path => Path.Combine(path, ManifestName))
                .ToList();
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            errors.Add($"无法扫描主题目录：{ex.Message}");
            return new ThemeCatalogResult(themes, errors);
        }

        foreach (var path in themePaths) {
            if (!TryRead(path, out var theme, out var error)) {
                errors.Add($"{Path.GetFileName(Path.GetDirectoryName(path))}: {error}");
                continue;
            }

            if (themes.Any(candidate => string.Equals(candidate.Id, theme.Id, StringComparison.OrdinalIgnoreCase))) {
                errors.Add($"{Path.GetFileName(Path.GetDirectoryName(path))}: 主题 ID “{theme.Id}” 重复");
                continue;
            }

            themes.Add(theme);
        }

        return new ThemeCatalogResult(themes, errors);
    }

    public static ThemeDefinition Install(string sourcePath, string themesDirectory) {
        return ThemePackageInstaller.Install(sourcePath, themesDirectory);
    }

    public static ThemeDefinition Read(string path) {
        if (!TryRead(path, out var theme, out var error)) {
            throw new InvalidDataException(error);
        }

        return theme;
    }

    public static bool TryRead(string path, out ThemeDefinition theme, out string error) {
        theme = null!;
        error = "";

        try {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length <= 0 || stream.Length > MaximumThemeFileBytes) {
                error = $"文件必须小于 {MaximumThemeFileBytes / 1024} KB 且不能为空";
                return false;
            }

            using var reader = new StreamReader(stream, StrictUtf8, detectEncodingFromByteOrderMarks: true);
            if (!TryParse(reader.ReadToEnd(), Path.GetFullPath(path), out var parsed, out error)) return false;

            var directory = Path.GetDirectoryName(path) ?? "";
            var iconPath = Path.Combine(directory, IconName);
            var descriptionPath = Path.Combine(directory, DescriptionName);
            if (!File.Exists(iconPath)) {
                error = $"主题目录缺少 {IconName}";
                return false;
            }
            if (!File.Exists(descriptionPath)) {
                error = $"主题目录缺少 {DescriptionName}";
                return false;
            }

            theme = CopyWithPackageData(
                parsed,
                ThemePackageInstaller.LoadIcon(iconPath),
                ReadDescription(descriptionPath));
            return true;
        } catch (DecoderFallbackException) {
            error = $"{ManifestName} 必须使用有效的 UTF-8 编码";
            return false;
        } catch (JsonException ex) {
            error = $"JSON 格式无效：{ex.Message}";
            return false;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) {
            error = $"无法读取主题：{ex.Message}";
            return false;
        }
    }

    internal static bool TryParse(
        string json,
        string? sourcePath,
        out ThemeDefinition theme,
        out string error,
        bool allowReservedId = false) {
        theme = null!;
        error = "";

        try {
            var parsed = JsonSerializer.Deserialize<ThemeDefinition>(json, JsonOptions);
            if (parsed is null) {
                error = "主题内容为空";
                return false;
            }

            error = Validate(parsed, allowReservedId);
            if (!string.IsNullOrEmpty(error)) return false;

            if (!TryNormalizeAssetPath(parsed.BackgroundImage, out var backgroundImage)) {
                error = "backgroundImage 必须是包内 PNG 或 JPEG 相对路径";
                return false;
            }

            if (sourcePath is not null && !string.IsNullOrEmpty(backgroundImage)) {
                var imagePath = ResolveAssetPath(sourcePath, backgroundImage);
                if (!File.Exists(imagePath)) {
                    error = $"背景图片不存在：{backgroundImage}";
                    return false;
                }

                if (new FileInfo(imagePath).Length > MaximumImageFileBytes) {
                    error = $"背景图片不能超过 {MaximumImageFileBytes / 1024 / 1024} MB";
                    return false;
                }
            }

            theme = new ThemeDefinition {
                SchemaVersion = parsed.SchemaVersion,
                Id = parsed.Id,
                Name = parsed.Name.Trim(),
                Author = parsed.Author.Trim(),
                Version = parsed.Version.Trim(),
                BaseTheme = parsed.BaseTheme,
                BackgroundImage = backgroundImage,
                BackgroundImageOpacity = parsed.BackgroundImageOpacity,
                Colors = parsed.Colors,
                SourcePath = sourcePath
            };
            return true;
        } catch (JsonException ex) {
            error = $"JSON 格式无效：{ex.Message}";
            return false;
        }
    }

    internal static void Write(string path, ThemeDefinition theme) {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, theme, WriteOptions);
    }

    internal static string ResolveAssetPath(string manifestPath, string assetPath) {
        var directory = Path.GetDirectoryName(manifestPath) ?? "";
        return Path.GetFullPath(Path.Combine(directory, assetPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    public static IReadOnlyList<ThemeDefinition> CreateBuiltInThemes() {
        return BuiltInThemes.Value;
    }

    internal static string DecodeManifest(byte[] bytes) {
        try {
            return StrictUtf8.GetString(bytes).TrimStart('\uFEFF');
        } catch (DecoderFallbackException ex) {
            throw new InvalidDataException($"{ManifestName} 必须使用有效的 UTF-8 编码。", ex);
        }
    }

    private static IReadOnlyList<ThemeDefinition> CreateBuiltInThemeCatalog() {
        return [
            ReadBuiltInTheme(ThemeService.SystemThemeId),
            ReadBuiltInTheme(ThemeService.LightThemeId),
            ReadBuiltInTheme(ThemeService.DarkThemeId)
        ];
    }

    internal static string ReadDescription(string path) {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return DecodeDescription(ReadBounded(stream, MaximumDescriptionFileBytes, DescriptionName));
    }

    internal static string DecodeDescription(byte[] bytes) {
        string description;
        try {
            description = StrictUtf8.GetString(bytes);
        } catch (DecoderFallbackException ex) {
            throw new InvalidDataException($"{DescriptionName} 必须使用有效的 UTF-8 编码。", ex);
        }

        description = description.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (description.Length == 0) throw new InvalidDataException($"{DescriptionName} 不能为空。");
        if (description.Length > MaximumDescriptionCharacters) {
            throw new InvalidDataException($"{DescriptionName} 不能超过 {MaximumDescriptionCharacters} 个字符。");
        }
        if (description.Contains('\0')) throw new InvalidDataException($"{DescriptionName} 不能包含空字符。");
        return description;
    }

    private static ThemeDefinition ReadBuiltInTheme(string id) {
        var manifestBytes = ReadBuiltInResource(id, ManifestName, MaximumThemeFileBytes);
        var manifest = DecodeManifest(manifestBytes);

        if (!TryParse(manifest, null, out var theme, out var error, allowReservedId: true) || theme.Id != id) {
            throw new InvalidDataException($"内置主题 {id} 无效：{error}");
        }

        var icon = ThemePackageInstaller.DecodeIcon(ReadBuiltInResource(id, IconName, MaximumIconFileBytes));
        var description = DecodeDescription(ReadBuiltInResource(id, DescriptionName, MaximumDescriptionFileBytes));
        return CopyWithPackageData(theme, icon, description, isBuiltIn: true);
    }

    private static byte[] ReadBuiltInResource(string id, string fileName, long maximumBytes) {
        var assembly = typeof(ThemeCatalog).Assembly;
        var suffix = $".Themes.BuiltIn.{id}.{fileName}";
        var resourceName = assembly.GetManifestResourceNames().SingleOrDefault(name =>
            name.EndsWith(suffix, StringComparison.Ordinal));
        if (resourceName is null) throw new InvalidDataException($"内置主题 {id} 缺少 {fileName}。");

        using var stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidDataException($"无法读取内置主题 {id} 的 {fileName}。");
        return ReadBounded(stream, maximumBytes, fileName);
    }

    private static byte[] ReadBounded(Stream stream, long maximumBytes, string fileName) {
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        while (true) {
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            total += read;
            if (total > maximumBytes) throw new InvalidDataException($"{fileName} 超过大小限制。");
            memory.Write(buffer, 0, read);
        }
        if (total == 0) throw new InvalidDataException($"{fileName} 不能为空。");
        return memory.ToArray();
    }

    private static ThemeDefinition CopyWithPackageData(
        ThemeDefinition theme,
        System.Windows.Media.Imaging.BitmapSource icon,
        string description,
        bool isBuiltIn = false) {
        return new ThemeDefinition {
            SchemaVersion = theme.SchemaVersion,
            Id = theme.Id,
            Name = theme.Name,
            Author = theme.Author,
            Version = theme.Version,
            BaseTheme = theme.BaseTheme,
            BackgroundImage = theme.BackgroundImage,
            BackgroundImageOpacity = theme.BackgroundImageOpacity,
            Colors = theme.Colors,
            IsBuiltIn = isBuiltIn,
            SourcePath = theme.SourcePath,
            Icon = icon,
            Description = description
        };
    }

    private static string Validate(ThemeDefinition theme, bool allowReservedId) {
        if (theme.SchemaVersion != CurrentSchemaVersion) {
            return $"不支持 schemaVersion {theme.SchemaVersion}";
        }

        if (!ThemeIdRegex().IsMatch(theme.Id ?? "") || (!allowReservedId && ReservedIds.Contains(theme.Id ?? ""))) {
            return "id 必须由 1-64 个小写字母、数字、点或连字符组成，且不能使用内置主题 ID";
        }

        if (!HasValidLength(theme.Name, 1, 64)) return "name 长度必须为 1-64 个字符";
        if (!HasValidLength(theme.Author, 1, 64)) return "author 长度必须为 1-64 个字符";
        if (!HasValidLength(theme.Version, 1, 32)) return "version 长度必须为 1-32 个字符";
        if (theme.BaseTheme is not ("light" or "dark")) return "baseTheme 必须是 light 或 dark";
        if (!double.IsFinite(theme.BackgroundImageOpacity) ||
            theme.BackgroundImageOpacity < 0 ||
            theme.BackgroundImageOpacity > 0.35) {
            return "backgroundImageOpacity 必须是 0 到 0.35 之间的数值";
        }
        if (theme.Colors is null) return "colors 不能为空";

        var colors = theme.Colors;
        var colorValues = new Dictionary<string, string> {
            ["window"] = colors.Window,
            ["surface"] = colors.Surface,
            ["surfaceAlt"] = colors.SurfaceAlt,
            ["text"] = colors.Text,
            ["mutedText"] = colors.MutedText,
            ["border"] = colors.Border,
            ["accent"] = colors.Accent,
            ["accentText"] = colors.AccentText,
            ["selection"] = colors.Selection,
            ["selectionText"] = colors.SelectionText,
            ["mapBackground"] = colors.MapBackground
        };
        foreach (var (name, value) in colorValues) {
            if (!ColorRegex().IsMatch(value ?? "")) return $"colors.{name} 必须是 #RRGGBB 颜色";
        }

        if (Contrast(colors.Text, colors.Window) < 4.5 || Contrast(colors.Text, colors.Surface) < 4.5) {
            return "正文与 window、surface 的对比度必须至少为 4.5:1";
        }

        if (Contrast(colors.MutedText, colors.Surface) < 4.5) {
            return "次要文字与 surface 的对比度必须至少为 4.5:1";
        }

        if (Contrast(colors.AccentText, colors.Accent) < 4.5) {
            return "强调文字与 accent 的对比度必须至少为 4.5:1";
        }

        if (Contrast(colors.SelectionText, colors.Selection) < 4.5) {
            return "选中文字与 selection 的对比度必须至少为 4.5:1";
        }

        return "";
    }

    private static bool HasValidLength(string? value, int minimum, int maximum) {
        var length = value?.Trim().Length ?? 0;
        return length >= minimum && length <= maximum;
    }

    internal static bool TryNormalizeAssetPath(string? value, out string normalized) {
        normalized = (value ?? "").Trim().Replace('\\', '/');
        if (string.IsNullOrEmpty(normalized)) return true;
        if (normalized.Length > 160 || normalized.StartsWith('/') || normalized.Contains(':')) return false;

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or "..")) return false;

        normalized = string.Join('/', segments);
        var extension = Path.GetExtension(normalized);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static double Contrast(string foreground, string background) {
        var foregroundLuminance = RelativeLuminance(foreground);
        var backgroundLuminance = RelativeLuminance(background);
        var lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        var darker = Math.Min(foregroundLuminance, backgroundLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string color) {
        var red = ParseChannel(color, 1);
        var green = ParseChannel(color, 3);
        var blue = ParseChannel(color, 5);
        return 0.2126 * Linearize(red) + 0.7152 * Linearize(green) + 0.0722 * Linearize(blue);
    }

    private static double ParseChannel(string color, int startIndex) {
        return int.Parse(color.AsSpan(startIndex, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
    }

    private static double Linearize(double channel) {
        return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9.-]{0,63})$", RegexOptions.CultureInvariant)]
    private static partial Regex ThemeIdRegex();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorRegex();
}
