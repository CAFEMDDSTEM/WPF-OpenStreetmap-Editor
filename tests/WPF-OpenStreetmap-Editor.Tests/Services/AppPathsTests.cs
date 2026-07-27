using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class AppPathsTests {
    [Fact]
    public void Normalize_ReturnsFullPath() {
        var normalized = AppPaths.Normalize(Path.Combine(".", "docs", "..", "README.md"));

        Assert.Equal(Path.GetFullPath("README.md"), normalized);
    }

    [Fact]
    public void RuntimePaths_AreUnderLocalApplicationData() {
        var expectedDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WPF-OpenStreetmap-Editor");

        Assert.Equal(Path.GetFullPath(expectedDataDirectory), AppPaths.DataDirectory);
        Assert.StartsWith(AppPaths.DataDirectory, AppPaths.TileCacheDirectory);
        Assert.StartsWith(AppPaths.DataDirectory, AppPaths.LayersFile);
        Assert.StartsWith(AppPaths.DataDirectory, AppPaths.SettingsFile);
        Assert.StartsWith(AppPaths.DataDirectory, AppPaths.WindowStateFile);
        Assert.StartsWith(AppPaths.DataDirectory, AppPaths.TileRequestsLogFile);
        Assert.StartsWith(AppPaths.DataDirectory, AppPaths.StartupLogFile);
    }

    [Fact]
    public void ResolveReadPath_UsesLegacyOnlyUntilCurrentFileExists() {
        var root = Path.Combine(Path.GetTempPath(), "wpf-osm-editor-tests", Guid.NewGuid().ToString("N"));
        var currentPath = Path.Combine(root, "current.json");
        var legacyPath = Path.Combine(root, "legacy.json");

        try {
            Directory.CreateDirectory(root);
            File.WriteAllText(legacyPath, "legacy");
            Assert.Equal(legacyPath, AppPaths.ResolveReadPath(currentPath, legacyPath));

            File.WriteAllText(currentPath, "current");
            Assert.Equal(currentPath, AppPaths.ResolveReadPath(currentPath, legacyPath));
        } finally {
            if (Directory.Exists(root)) {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
