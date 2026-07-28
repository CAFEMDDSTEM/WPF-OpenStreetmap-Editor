using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class TileSourceNameServiceTests {
    [Fact]
    public void CreateUniqueName_ReturnsUnusedBaseName() {
        var name = TileSourceNameService.CreateUniqueName([], "Custom source");

        Assert.Equal("Custom source", name);
    }

    [Fact]
    public void CreateUniqueName_UsesFirstAvailableNumericSuffix() {
        TileSourcePreset[] sources = [
            new() { Name = "Custom source" },
            new() { Name = "Custom source 2" },
            new() { Name = "Custom source 4" }
        ];

        var name = TileSourceNameService.CreateUniqueName(sources, "Custom source");

        Assert.Equal("Custom source 3", name);
    }

    [Fact]
    public void CreateUniqueName_PreservesCaseSensitiveComparison() {
        TileSourcePreset[] sources = [new() { Name = "custom source" }];

        var name = TileSourceNameService.CreateUniqueName(sources, "Custom source");

        Assert.Equal("Custom source", name);
    }

    [Fact]
    public void CreateUniqueName_UsesTimestampAfterNumericSuffixesAreExhausted() {
        var sources = Enumerable.Range(1, 999)
            .Select(index => new TileSourcePreset {
                Name = index == 1 ? "Custom source" : $"Custom source {index}"
            })
            .ToList();

        var name = TileSourceNameService.CreateUniqueName(
            sources,
            "Custom source",
            new DateTime(2026, 7, 28, 12, 34, 56));

        Assert.Equal("Custom source 123456", name);
    }
}
