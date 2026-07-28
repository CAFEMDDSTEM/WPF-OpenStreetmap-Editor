using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class TileRenderLayoutTests {
    [Fact]
    public void EnumerateRequestsByDistance_OrdersTilesFromViewportCenter() {
        var range = new TileRange(0, 2, 0, 0);

        var requests = TileRenderLayout.EnumerateRequestsByDistance(
            range,
            centerPixelX: GeoConverter.TileSize * 1.5,
            centerPixelY: GeoConverter.TileSize * 0.5).ToList();

        Assert.Equal([1, 0, 2], requests.Select(static request => request.X));
        Assert.Equal(0, requests[0].Distance);
    }

    [Theory]
    [InlineData(2, 4, true)]
    [InlineData(5, 4, true)]
    [InlineData(1, 4, false)]
    [InlineData(2, 7, false)]
    public void Contains_IncludesRangeBoundaries(int tileX, int tileY, bool expected) {
        var range = new TileRange(2, 5, 3, 6);

        Assert.Equal(expected, TileRenderLayout.Contains(range, tileX, tileY));
    }

    [Fact]
    public void GetTilePlacement_AdjacentTilesShareExactEdges() {
        var first = TileRenderLayout.GetTilePlacement(3, 4, 123.37, 456.61, 1023.5, 767.25);
        var right = TileRenderLayout.GetTilePlacement(4, 4, 123.37, 456.61, 1023.5, 767.25);
        var below = TileRenderLayout.GetTilePlacement(3, 5, 123.37, 456.61, 1023.5, 767.25);

        Assert.Equal(first.Left + first.Width, right.Left, precision: 10);
        Assert.Equal(first.Top + first.Height, below.Top, precision: 10);
    }

    [Fact]
    public void GetTilePlacement_PreservesTileGrid() {
        var placement = TileRenderLayout.GetTilePlacement(0, 0, 0.3, 0.3, 512, 512);

        Assert.Equal(255.7, placement.Left, precision: 10);
        Assert.Equal(255.7, placement.Top, precision: 10);
        Assert.Equal(256, placement.Width);
        Assert.Equal(256, placement.Height);
    }

    [Fact]
    public void GetTilePlacement_ScalesLowerZoomTileInRenderZoomSpace() {
        var placement = TileRenderLayout.GetTilePlacement(
            tileX: 1,
            tileY: 1,
            tileZoom: 1,
            renderZoom: 3,
            centerPixelX: 1024,
            centerPixelY: 1024,
            viewportWidth: 512,
            viewportHeight: 512);

        Assert.Equal(256, placement.Left);
        Assert.Equal(256, placement.Top);
        Assert.Equal(1024, placement.Width);
        Assert.Equal(1024, placement.Height);
    }

    [Fact]
    public void GetVisibleTileRange_ClampsToWorldBounds() {
        var range = TileRenderLayout.GetVisibleTileRange(128, 128, 1920, 1080, zoom: 0, tileBuffer: 1);

        Assert.Equal(0, range.StartX);
        Assert.Equal(0, range.EndX);
        Assert.Equal(0, range.StartY);
        Assert.Equal(0, range.EndY);
    }

    [Fact]
    public void SnapToDevicePixel_UsesDpiGrid() {
        var snapped = TileRenderLayout.SnapToDevicePixel(10.33, 1.25);

        Assert.Equal(10.4, snapped, precision: 10);
    }

    [Fact]
    public void GetViewportCoverage_MergesOverlappingPlacements() {
        var coverage = TileRenderLayout.GetViewportCoverage(
            [
                new TilePlacement(0, 0, 100, 100),
                new TilePlacement(50, 0, 100, 100)
            ],
            viewportWidth: 200,
            viewportHeight: 100);

        Assert.Equal(0.75, coverage, precision: 10);
    }

    [Fact]
    public void GetViewportCoverage_ReportsPartialViewportCoverage() {
        var coverage = TileRenderLayout.GetViewportCoverage(
            [new TilePlacement(100, 100, 256, 256)],
            viewportWidth: 1024,
            viewportHeight: 768);

        Assert.InRange(coverage, 0.08, 0.09);
    }
}
