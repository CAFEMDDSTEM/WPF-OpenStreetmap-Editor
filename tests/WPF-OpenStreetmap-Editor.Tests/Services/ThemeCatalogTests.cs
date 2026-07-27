using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class ThemeCatalogTests {
    [Fact]
    public void BuiltInThemes_IncludeManifestIconAndDescription() {
        var themes = ThemeCatalog.CreateBuiltInThemes();

        Assert.Equal(3, themes.Count);
        Assert.All(themes, theme => {
            Assert.True(theme.IsBuiltIn);
            Assert.NotNull(theme.Icon);
            Assert.Equal(128, theme.Icon!.PixelWidth);
            Assert.False(string.IsNullOrWhiteSpace(theme.Description));
        });
    }

    [Fact]
    public void BuiltInThemes_PassThirdPartyPaletteValidation() {
        var root = CreateTestDirectory();

        try {
            foreach (var builtIn in ThemeCatalog.CreateBuiltInThemes()) {
                var package = new ThemeDefinition {
                    Id = $"verified.{builtIn.Id}",
                    Name = builtIn.Name,
                    Author = builtIn.Author,
                    Version = builtIn.Version,
                    BaseTheme = builtIn.BaseTheme,
                    Colors = builtIn.Colors
                };
                var packageDirectory = Path.Combine(root, package.Id);
                WriteLoosePackage(packageDirectory, JsonSerializer.Serialize(package));

                Assert.Equal(package.Id, ThemeCatalog.Read(Path.Combine(packageDirectory, "theme.json")).Id);
            }
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void Read_AcceptsValidThemePackage() {
        var root = CreateTestDirectory();

        try {
            WriteLoosePackage(root, CreateValidThemeJson("community.clear"));

            var theme = ThemeCatalog.Read(Path.Combine(root, "theme.json"));

            Assert.Equal("community.clear", theme.Id);
            Assert.Equal("Clear", theme.Name);
            Assert.Equal("dark", theme.BaseTheme);
            Assert.NotNull(theme.Icon);
            Assert.Equal("A focused theme for editing maps.", theme.Description);
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "theme.json")), theme.SourcePath);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void Read_AcceptsPartialMapStyleOverrides() {
        var root = CreateTestDirectory();

        try {
            var json = CreateValidThemeJson("community.map-style")
                .Replace(
                    "  \"colors\": {",
                    """
                      "mapStyle": {
                        "water": { "fill": "#ABCDEF", "stroke": "#123456", "strokeWidth": 1.25 },
                        "motorway": { "stroke": "#E892A2", "casing": "#DC2A67", "strokeWidth": 4, "casingWidth": 6 },
                        "foodPoint": { "fill": "#FFF3BF", "stroke": "#8C5A00", "radius": 5 }
                      },
                      "colors": {
                    """,
                    StringComparison.Ordinal);
            WriteLoosePackage(root, json);

            var theme = ThemeCatalog.Read(Path.Combine(root, "theme.json"));

            Assert.Equal("#ABCDEF", theme.MapStyle?.Water?.Fill);
            Assert.Equal(6, theme.MapStyle?.Motorway?.CasingWidth);
            Assert.Equal(5, theme.MapStyle?.FoodPoint?.Radius);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void TryRead_RejectsInvalidMapStyleValues() {
        var root = CreateTestDirectory();

        try {
            var json = CreateValidThemeJson("community.invalid-map-style")
                .Replace(
                    "  \"colors\": {",
                    """
                      "mapStyle": {
                        "motorway": { "stroke": "red", "strokeWidth": -1 }
                      },
                      "colors": {
                    """,
                    StringComparison.Ordinal);
            WriteLoosePackage(root, json);

            var loaded = ThemeCatalog.TryRead(Path.Combine(root, "theme.json"), out _, out var error);

            Assert.False(loaded);
            Assert.Contains("mapStyle.motorway.stroke", error);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Theory]
    [InlineData("icon.png")]
    [InlineData("README.md")]
    public void TryRead_RejectsPackageMissingRequiredFile(string missingFile) {
        var root = CreateTestDirectory();

        try {
            WriteLoosePackage(root, CreateValidThemeJson("community.incomplete"));
            File.Delete(Path.Combine(root, missingFile));

            var loaded = ThemeCatalog.TryRead(Path.Combine(root, "theme.json"), out _, out var error);

            Assert.False(loaded);
            Assert.Contains(missingFile, error);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void TryRead_RejectsInvalidPngIcon() {
        var root = CreateTestDirectory();

        try {
            WriteLoosePackage(root, CreateValidThemeJson("community.invalid-icon"));
            File.WriteAllText(Path.Combine(root, "icon.png"), "not a png");

            var loaded = ThemeCatalog.TryRead(Path.Combine(root, "theme.json"), out _, out var error);

            Assert.False(loaded);
            Assert.Contains("PNG", error);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void TryRead_RejectsNonUtf8Description() {
        var root = CreateTestDirectory();

        try {
            WriteLoosePackage(root, CreateValidThemeJson("community.invalid-description"));
            File.WriteAllBytes(Path.Combine(root, "README.md"), [0xC3, 0x28]);

            var loaded = ThemeCatalog.TryRead(Path.Combine(root, "theme.json"), out _, out var error);

            Assert.False(loaded);
            Assert.Contains("UTF-8", error);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void TryRead_RejectsNonUtf8Manifest() {
        var root = CreateTestDirectory();

        try {
            WriteLoosePackage(root, CreateValidThemeJson("community.invalid-manifest"));
            File.WriteAllBytes(Path.Combine(root, "theme.json"), [0xC3, 0x28]);

            var loaded = ThemeCatalog.TryRead(Path.Combine(root, "theme.json"), out _, out var error);

            Assert.False(loaded);
            Assert.Contains("UTF-8", error);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void TryRead_RejectsIconOutsideDimensionLimit() {
        var root = CreateTestDirectory();

        try {
            WriteLoosePackage(root, CreateValidThemeJson("community.small-icon"));
            File.WriteAllBytes(Path.Combine(root, "icon.png"), CreatePng(16, 16));

            var loaded = ThemeCatalog.TryRead(Path.Combine(root, "theme.json"), out _, out var error);

            Assert.False(loaded);
            Assert.Contains("32-512", error);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void TryRead_RejectsLowContrastPalette() {
        var root = CreateTestDirectory();

        try {
            var json = CreateValidThemeJson("community.low-contrast")
                .Replace("\"text\": \"#F4F6F8\"", "\"text\": \"#25282D\"", StringComparison.Ordinal);
            WriteLoosePackage(root, json);

            var loaded = ThemeCatalog.TryRead(Path.Combine(root, "theme.json"), out _, out var error);

            Assert.False(loaded);
            Assert.Contains("对比度", error);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void TryRead_RejectsUnknownExecutableFields() {
        var root = CreateTestDirectory();

        try {
            var json = CreateValidThemeJson("community.executable")
                .Replace("\"colors\":", "\"assembly\": \"theme.dll\",\n  \"colors\":", StringComparison.Ordinal);
            WriteLoosePackage(root, json);

            var loaded = ThemeCatalog.TryRead(Path.Combine(root, "theme.json"), out _, out var error);

            Assert.False(loaded);
            Assert.Contains("JSON 格式无效", error);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void TryRead_RejectsUnsafeBackgroundImagePath() {
        var root = CreateTestDirectory();

        try {
            WriteLoosePackage(root, CreateValidThemeJson("community.unsafe-image", "../background.png"));

            var loaded = ThemeCatalog.TryRead(Path.Combine(root, "theme.json"), out _, out var error);

            Assert.False(loaded);
            Assert.Contains("backgroundImage", error);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void Load_SkipsInvalidDirectoriesAndKeepsBuiltInThemes() {
        var root = CreateTestDirectory();

        try {
            WriteLoosePackage(Path.Combine(root, "valid"), CreateValidThemeJson("community.valid"));
            var invalidDirectory = Path.Combine(root, "invalid");
            Directory.CreateDirectory(invalidDirectory);
            File.WriteAllText(Path.Combine(invalidDirectory, "theme.json"), "{}");
            File.WriteAllText(Path.Combine(root, "legacy.json"), CreateValidThemeJson("community.legacy"));

            var catalog = ThemeCatalog.Load(root);

            Assert.Contains(catalog.Themes, theme => theme.Id == ThemeService.SystemThemeId);
            Assert.Contains(catalog.Themes, theme => theme.Id == ThemeService.LightThemeId);
            Assert.Contains(catalog.Themes, theme => theme.Id == ThemeService.DarkThemeId);
            Assert.Contains(catalog.Themes, theme => theme.Id == "community.valid");
            Assert.DoesNotContain(catalog.Themes, theme => theme.Id == "community.legacy");
            Assert.Single(catalog.Errors);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void Install_WritesCompletePackageAndRejectsDuplicateId() {
        var root = CreateTestDirectory();
        var sourcePath = Path.Combine(root, "download.wosm-theme");
        var themesDirectory = Path.Combine(root, "installed");

        try {
            CreateZipTheme(sourcePath, "community.install");

            var installed = ThemeCatalog.Install(sourcePath, themesDirectory);
            var installedDirectory = Path.Combine(themesDirectory, "community.install");

            Assert.Equal("community.install", installed.Id);
            Assert.True(File.Exists(Path.Combine(installedDirectory, "theme.json")));
            Assert.True(File.Exists(Path.Combine(installedDirectory, "icon.png")));
            Assert.True(File.Exists(Path.Combine(installedDirectory, "README.md")));
            Assert.NotNull(installed.Icon);
            Assert.Throws<IOException>(() => ThemeCatalog.Install(sourcePath, themesDirectory));
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void Install_RejectsStandaloneJson() {
        var root = CreateTestDirectory();
        var sourcePath = Path.Combine(root, "theme.json");

        try {
            File.WriteAllText(sourcePath, CreateValidThemeJson("community.standalone"));

            var error = Assert.Throws<InvalidDataException>(() =>
                ThemeCatalog.Install(sourcePath, Path.Combine(root, "installed")));

            Assert.Contains(".wosm-theme", error.Message);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void Install_ImportsZipThemeWithBackgroundImage() {
        var root = CreateTestDirectory();
        var sourcePath = Path.Combine(root, "theme.zip");
        var themesDirectory = Path.Combine(root, "installed");

        try {
            CreateZipTheme(sourcePath, "community.zip", includeBackground: true);

            var installed = ThemePackageInstaller.Install(sourcePath, themesDirectory);

            Assert.Equal("background.png", installed.BackgroundImage);
            var imagePath = Path.Combine(themesDirectory, "community.zip", "background.png");
            Assert.True(File.Exists(imagePath));
            Assert.True(File.ReadAllBytes(imagePath).AsSpan().StartsWith(
                new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void Install_ImportsSevenZipTheme() {
        var root = CreateTestDirectory();
        var sourcePath = Path.Combine(root, "theme.7z");
        var themesDirectory = Path.Combine(root, "installed");

        try {
            CreateSevenZipTheme(sourcePath, "community.seven-zip");

            var installed = ThemePackageInstaller.Install(sourcePath, themesDirectory);

            Assert.Equal("community.seven-zip", installed.Id);
            Assert.NotNull(installed.Icon);
            Assert.Equal("A focused theme for editing maps.", installed.Description);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Theory]
    [InlineData("theme.json")]
    [InlineData("icon.png")]
    [InlineData("README.md")]
    public void Install_RejectsArchiveMissingRequiredRootFile(string missingFile) {
        var root = CreateTestDirectory();
        var sourcePath = Path.Combine(root, "incomplete.zip");

        try {
            CreateZipTheme(sourcePath, "community.incomplete", omittedFile: missingFile);

            var error = Assert.Throws<InvalidDataException>(() =>
                ThemePackageInstaller.Install(sourcePath, Path.Combine(root, "installed")));

            Assert.Contains(missingFile, error.Message);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void Install_RejectsArchivePathTraversal() {
        var root = CreateTestDirectory();
        var sourcePath = Path.Combine(root, "unsafe.zip");

        try {
            using (var archive = ZipFile.Open(sourcePath, ZipArchiveMode.Create)) {
                WriteZipEntry(archive, "../theme.json", CreateValidThemeJson("community.unsafe"));
            }

            var error = Assert.Throws<InvalidDataException>(() =>
                ThemePackageInstaller.Install(sourcePath, Path.Combine(root, "installed")));

            Assert.Contains("不安全路径", error.Message);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void Install_RejectsNonUtf8ArchiveManifest() {
        var root = CreateTestDirectory();
        var sourcePath = Path.Combine(root, "invalid-utf8.zip");

        try {
            using (var archive = ZipFile.Open(sourcePath, ZipArchiveMode.Create)) {
                WriteZipBytes(archive, "theme.json", [0xC3, 0x28]);
                WriteZipBytes(archive, "icon.png", CreatePng(64, 64));
                WriteZipEntry(archive, "README.md", "Valid description");
            }

            var error = Assert.Throws<InvalidDataException>(() =>
                ThemePackageInstaller.Install(sourcePath, Path.Combine(root, "installed")));

            Assert.Contains("UTF-8", error.Message);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void Install_RejectsRequiredFilesOutsidePackageRoot() {
        var root = CreateTestDirectory();
        var sourcePath = Path.Combine(root, "nested.zip");

        try {
            using (var archive = ZipFile.Open(sourcePath, ZipArchiveMode.Create)) {
                WriteZipEntry(archive, "theme/theme.json", CreateValidThemeJson("community.nested"));
                WriteZipBytes(archive, "theme/icon.png", CreatePng(64, 64));
                WriteZipEntry(archive, "theme/README.md", "Nested files");
            }

            var error = Assert.Throws<InvalidDataException>(() =>
                ThemePackageInstaller.Install(sourcePath, Path.Combine(root, "installed")));

            Assert.Contains("根目录", error.Message);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public void Install_RejectsUndeclaredArchiveFiles() {
        var root = CreateTestDirectory();
        var sourcePath = Path.Combine(root, "extra.zip");

        try {
            CreateZipTheme(sourcePath, "community.extra", includeUnexpectedFile: true);

            var error = Assert.Throws<InvalidDataException>(() =>
                ThemePackageInstaller.Install(sourcePath, Path.Combine(root, "installed")));

            Assert.Contains("未声明", error.Message);
        } finally {
            DeleteTestDirectory(root);
        }
    }

    private static string CreateTestDirectory() {
        var path = Path.Combine(Path.GetTempPath(), "wpf-osm-editor-theme-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTestDirectory(string path) {
        if (Directory.Exists(path)) {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void WriteLoosePackage(string directory, string manifest) {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "theme.json"), manifest);
        File.WriteAllBytes(Path.Combine(directory, "icon.png"), CreatePng(64, 64));
        File.WriteAllText(Path.Combine(directory, "README.md"), "A focused theme for editing maps.");
    }

    private static string CreateValidThemeJson(string id, string? backgroundImage = null) {
        var json = $$"""
            {
              "schemaVersion": 1,
              "id": "{{id}}",
              "name": "Clear",
              "author": "Community",
              "version": "1.0.0",
              "baseTheme": "dark",
              "colors": {
                "window": "#181A1D",
                "surface": "#22252A",
                "surfaceAlt": "#2D3137",
                "text": "#F4F6F8",
                "mutedText": "#B4BBC4",
                "border": "#59616C",
                "accent": "#4CC2FF",
                "accentText": "#0B1B24",
                "selection": "#155778",
                "selectionText": "#FFFFFF",
                "mapBackground": "#343A42"
              }
            }
            """;
        if (backgroundImage is null) return json;

        return json.Replace(
            "  \"colors\": {",
            $"  \"backgroundImage\": \"{backgroundImage}\",{Environment.NewLine}" +
            $"  \"backgroundImageOpacity\": 0.2,{Environment.NewLine}" +
            "  \"colors\": {",
            StringComparison.Ordinal);
    }

    private static void CreateZipTheme(
        string path,
        string id,
        bool includeBackground = false,
        string? omittedFile = null,
        bool includeUnexpectedFile = false) {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        if (omittedFile != "theme.json") {
            WriteZipEntry(
                archive,
                "theme.json",
                CreateValidThemeJson(id, includeBackground ? "assets/background.jpg" : null));
        }
        if (omittedFile != "icon.png") WriteZipBytes(archive, "icon.png", CreatePng(64, 64));
        if (omittedFile != "README.md") {
            WriteZipEntry(archive, "README.md", "A focused theme for editing maps.");
        }
        if (includeBackground) WriteZipBytes(archive, "assets/background.jpg", CreateJpeg(4, 4));
        if (includeUnexpectedFile) WriteZipEntry(archive, "theme.dll", "not executable");
    }

    private static void WriteZipEntry(ZipArchive archive, string path, string content) {
        WriteZipBytes(archive, path, Encoding.UTF8.GetBytes(content));
    }

    private static void WriteZipBytes(ZipArchive archive, string path, byte[] content) {
        var entry = archive.CreateEntry(path);
        using var output = entry.Open();
        output.Write(content);
    }

    private static void CreateSevenZipTheme(string path, string id) {
        using var output = File.Create(path);
        using var writer = WriterFactory.OpenWriter(
            output,
            ArchiveType.SevenZip,
            new SevenZipWriterOptions(CompressionType.LZMA2) { CompressHeader = true });
        WriteSevenZipEntry(writer, "theme.json", Encoding.UTF8.GetBytes(CreateValidThemeJson(id)));
        WriteSevenZipEntry(writer, "icon.png", CreatePng(64, 64));
        WriteSevenZipEntry(writer, "README.md", Encoding.UTF8.GetBytes("A focused theme for editing maps."));
    }

    private static void WriteSevenZipEntry(IWriter writer, string path, byte[] content) {
        using var stream = new MemoryStream(content);
        writer.Write(path, stream, DateTime.UtcNow);
    }

    private static byte[] CreatePng(int width, int height) {
        return CreateImage(width, height, new PngBitmapEncoder());
    }

    private static byte[] CreateJpeg(int width, int height) {
        return CreateImage(width, height, new JpegBitmapEncoder());
    }

    private static byte[] CreateImage(int width, int height, BitmapEncoder encoder) {
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < pixels.Length; index += 4) {
            pixels[index] = 0x60;
            pixels[index + 1] = 0x40;
            pixels[index + 2] = 0x20;
            pixels[index + 3] = 0xFF;
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
