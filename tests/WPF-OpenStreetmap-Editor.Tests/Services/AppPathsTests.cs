using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class AppPathsTests {
    [Fact]
    public void Normalize_ReturnsFullPath() {
        var normalized = AppPaths.Normalize(Path.Combine(".", "docs", "..", "README.md"));

        Assert.Equal(Path.GetFullPath("README.md"), normalized);
    }

    [Fact]
    public void RuntimePaths_AreUnderBaseDirectory() {
        Assert.StartsWith(AppPaths.BaseDirectory, AppPaths.TileCacheDirectory);
        Assert.StartsWith(AppPaths.BaseDirectory, AppPaths.LayersFile);
        Assert.StartsWith(AppPaths.BaseDirectory, AppPaths.TileRequestsLogFile);
    }
}
