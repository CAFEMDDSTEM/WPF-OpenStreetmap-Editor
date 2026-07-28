using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class TileUrlTemplateExpanderTests {
    [Fact]
    public void Expand_AppliesCustomSubdomainAndCoordinates() {
        var result = TileUrlTemplateExpander.Expand(
            "https://{switch:one, two, three}.example/{z}/{x}/{y}",
            3,
            3,
            5,
            null);

        Assert.Equal("https://three.example/3/3/5", result);
    }

    [Fact]
    public void Expand_AppliesDefaultSubdomainCaseInsensitively() {
        var result = TileUrlTemplateExpander.Expand("https://{S}.example/{z}/{x}/{y}", 2, 1, 1, null);

        Assert.Equal("https://c.example/2/1/1", result);
    }

    [Fact]
    public void Expand_AppliesQuadKeyAndUrlEncodedTokenAliases() {
        var result = TileUrlTemplateExpander.Expand(
            "https://tiles.example/a{QUADKEY}?primary={access_token}&alias={token}",
            3,
            3,
            5,
            "abc$& xyz");

        Assert.Equal(
            "https://tiles.example/a213?primary=abc%24%26%20xyz&alias=abc%24%26%20xyz",
            result);
    }

    [Fact]
    public void Expand_LeavesTokenPlaceholderWhenTokenIsEmpty() {
        var result = TileUrlTemplateExpander.Expand(
            "https://tiles.example/{z}/{x}/{y}?token={access_token}",
            1,
            0,
            1,
            "");

        Assert.Equal("https://tiles.example/1/0/1?token={access_token}", result);
    }
}
