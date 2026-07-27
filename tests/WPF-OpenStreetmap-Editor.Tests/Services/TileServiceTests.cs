using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class TileServiceTests {
    [Fact]
    public void BuildTileUrl_WrapsXAndAppliesAccessToken() {
        using var service = new TileService {
            TileTemplate = "https://tiles.example.com/{z}/{x}/{y}?token={access_token}"
        };

        var url = service.BuildTileUrl(2, -1, 1, "secret");

        Assert.Equal("https://tiles.example.com/2/3/1?token=secret", url);
    }

    [Fact]
    public void BuildTileUrl_AppliesTmsYFlip() {
        using var service = new TileService {
            TileTemplate = "https://tiles.example.com/{z}/{x}/{y}",
            IsTms = true
        };

        var url = service.BuildTileUrl(2, 1, 0, null);

        Assert.Equal("https://tiles.example.com/2/1/3", url);
    }

    [Fact]
    public void BuildTileUrl_ReturnsEmptyForOutOfRangeY() {
        using var service = new TileService {
            TileTemplate = "https://tiles.example.com/{z}/{x}/{y}"
        };

        Assert.Equal(string.Empty, service.BuildTileUrl(2, 1, 4, null));
    }

    [Fact]
    public void ParseUrlTemplate_NormalizesCommonPlaceholders() {
        using var service = new TileService();

        service.ParseUrlTemplate("https://tiles.example.com/{zoom}/{TileCol}/{TileRow}", null);

        Assert.Equal("https://tiles.example.com/{z}/{x}/{y}", service.TileTemplate);
        Assert.False(service.IsTms);
    }

    [Fact]
    public void ParseUrlTemplate_DetectsTmsNegativeYAfterX() {
        using var service = new TileService();

        service.ParseUrlTemplate("https://tiles.example.com/{z}/{x}/{-y}", null);

        Assert.Equal("https://tiles.example.com/{z}/{x}/{y}", service.TileTemplate);
        Assert.True(service.IsTms);
    }

    [Fact]
    public void GetCacheBasePath_UsesNormalizedCacheRootAndWrappedX() {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "wpf-osm-editor-tests", Guid.NewGuid().ToString("N"));
        using var service = new TileService(cacheRoot: cacheRoot);

        var path = service.GetCacheBasePath(2, -1, 1);

        Assert.Equal(Path.Combine(Path.GetFullPath(cacheRoot), "2", "3", "1"), path);
    }
}
