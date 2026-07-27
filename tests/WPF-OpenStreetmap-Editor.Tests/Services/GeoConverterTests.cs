using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class GeoConverterTests {
    [Fact]
    public void ClampLatitude_LimitsToWebMercatorBounds() {
        Assert.Equal(GeoConverter.MaxLatitude, GeoConverter.ClampLatitude(90));
        Assert.Equal(-GeoConverter.MaxLatitude, GeoConverter.ClampLatitude(-90));
        Assert.Equal(12.5, GeoConverter.ClampLatitude(12.5));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(4, 16)]
    public void GetTileCount_ReturnsPowerOfTwoForZoom(int zoom, int expected) {
        Assert.Equal(expected, GeoConverter.GetTileCount(zoom));
    }

    [Fact]
    public void LatLonToTileXY_ConvertsOriginAtZoomOne() {
        var (tileX, tileY) = GeoConverter.LatLonToTileXY(0, 0, 1);

        Assert.Equal(1, tileX);
        Assert.Equal(1, tileY);
    }

    [Fact]
    public void PixelConversion_RoundTripsCoordinate() {
        const double lat = 1.3521;
        const double lon = 103.8198;
        const int zoom = 12;

        var (pixelX, pixelY) = GeoConverter.LatLonToPixelXY(lat, lon, zoom);
        var (actualLat, actualLon) = GeoConverter.PixelXYToLatLon(pixelX, pixelY, zoom);

        Assert.Equal(lat, actualLat, precision: 10);
        Assert.Equal(lon, actualLon, precision: 10);
    }
}
