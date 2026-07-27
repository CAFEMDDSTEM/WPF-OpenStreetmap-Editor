using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class LocalizationServiceTests {
    private static readonly string[] LanguageIds = ["en", "zh-Hans", "zh-Hant", "ja", "de"];

    [Theory]
    [InlineData(null, LocalizationService.SystemLanguageId)]
    [InlineData("", LocalizationService.SystemLanguageId)]
    [InlineData("system", LocalizationService.SystemLanguageId)]
    [InlineData("SYSTEM", LocalizationService.SystemLanguageId)]
    [InlineData("en-US", "en")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("ja-JP", "ja")]
    [InlineData("de-DE", "de")]
    [InlineData("fr-FR", LocalizationService.SystemLanguageId)]
    public void NormalizeLanguageId_MapsSupportedAliases(string? languageId, string expected) {
        Assert.Equal(expected, LocalizationService.NormalizeLanguageId(languageId));
    }

    [Theory]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-SG", "zh-Hans")]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("zh-HK", "zh-Hant")]
    [InlineData("ja-JP", "ja")]
    [InlineData("de-DE", "de")]
    [InlineData("fr-FR", "en")]
    public void ResolveSystemLanguageId_MapsSupportedCultures(string cultureName, string expected) {
        Assert.Equal(expected, LocalizationService.ResolveSystemLanguageId(CultureInfo.GetCultureInfo(cultureName)));
    }

    [Fact]
    public void AppSettingsClone_PreservesLanguageSelection() {
        var settings = new AppSettings { LanguageId = "ja" };

        var clone = settings.Clone();

        Assert.Equal("ja", clone.LanguageId);
    }

    [Fact]
    public void EnsureDefaults_NormalizesInvalidLanguageToSystem() {
        var settings = new AppSettings { LanguageId = "fr-FR" };

        AppSettingsService.EnsureDefaults(settings);

        Assert.Equal(LocalizationService.SystemLanguageId, settings.LanguageId);
    }

    [Fact]
    public void ResourceDictionaries_HaveSameKeysAndNoDuplicates() {
        var english = ReadResourceStrings("en");
        Assert.NotEmpty(english);

        foreach (var languageId in LanguageIds) {
            var strings = ReadResourceStrings(languageId);

            Assert.Equal(english.Keys.Order(), strings.Keys.Order());
            Assert.All(strings.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        }
    }

    [Fact]
    public void ResourceDictionaries_PreserveFormatPlaceholders() {
        var english = ReadResourceStrings("en");

        foreach (var languageId in LanguageIds.Where(static id => id != "en")) {
            var strings = ReadResourceStrings(languageId);
            foreach (var (key, value) in english) {
                Assert.Equal(GetFormatPlaceholders(value), GetFormatPlaceholders(strings[key]));
            }
        }
    }

    private static Dictionary<string, string> ReadResourceStrings(string languageId) {
        var path = Path.Combine(GetRepositoryRoot(), "src", "WPF-OpenStreetmap-Editor", "Localization", $"Strings.{languageId}.xaml");
        var document = XDocument.Load(path);
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var element in document.Root?.Elements() ?? []) {
            var key = (string?)element.Attribute(x + "Key");
            if (key is null) continue;

            Assert.True(strings.TryAdd(key, element.Value), $"Duplicate localization key '{key}' in {path}.");
        }

        return strings;
    }

    private static string[] GetFormatPlaceholders(string value) {
        return Regex.Matches(value, @"\{\d+(?::[^}]*)?\}")
            .Select(static match => match.Value)
            .Order()
            .ToArray();
    }

    private static string GetRepositoryRoot() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) {
            if (File.Exists(Path.Combine(directory.FullName, "WPF-OpenStreetmap-Editor.slnx"))) {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root from the test output directory.");
    }
}
