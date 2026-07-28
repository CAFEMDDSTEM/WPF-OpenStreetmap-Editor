using System.Text;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class BingMetadataParserTests {
    [Fact]
    public void Parse_NormalizesTemplateZoomAndCoverageProviders() {
        var metadata = BingMetadataParser.Parse(Encoding.UTF8.GetBytes("""
            {
              "authenticationResultCode": "ValidCredentials",
              "copyright": " Copyright Microsoft ",
              "resourceSets": [{
                "resources": [{
                  "imageUrl": "https://{subdomain}.tiles.example/a{quadkey}.jpeg",
                  "imageUrlSubdomains": ["t0", "T0", "bad/value", "t1"],
                  "zoomMin": -5,
                  "zoomMax": 99,
                  "imageryProviders": [{
                    "attribution": " Regional Provider ",
                    "coverageAreas": [
                      { "zoomMin": 3, "zoomMax": 8, "bbox": [-20, 170, 20, -170] },
                      { "zoomMin": 9, "zoomMax": 4, "bbox": [-20, -20, 20, 20] }
                    ]
                  }]
                }]
              }]
            }
            """));

        Assert.Equal("https://{switch:t0,t1}.tiles.example/a{quadkey}.jpeg", metadata.TileTemplate);
        Assert.Equal(GeoConverter.MinZoom, metadata.MinZoom);
        Assert.Equal(GeoConverter.MaxZoom, metadata.MaxZoom);
        Assert.Equal("Copyright Microsoft", metadata.Copyright);
        var provider = Assert.Single(metadata.ImageryProviders);
        Assert.Equal("Regional Provider", provider.Attribution);
        Assert.Single(provider.CoverageAreas);
        Assert.True(provider.AppliesTo(5, -10, 175, 10, -175));
        Assert.False(provider.AppliesTo(2, -10, 175, 10, -175));
    }

    [Fact]
    public void Parse_PreservesAuthenticationFailure() {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            BingMetadataParser.Parse(Encoding.UTF8.GetBytes("""
                { "authenticationResultCode": "InvalidCredentials" }
                """)));

        Assert.Equal("Bing Maps rejected the supplied key.", exception.Message);
    }

    [Fact]
    public void Parse_RequiresSubdomainsWhenTemplateUsesPlaceholder() {
        var exception = Assert.Throws<InvalidDataException>(() =>
            BingMetadataParser.Parse(Encoding.UTF8.GetBytes("""
                {
                  "copyright": "Copyright Microsoft",
                  "resourceSets": [{ "resources": [{
                    "imageUrl": "https://{subdomain}.tiles.example/a{quadkey}.jpeg",
                    "zoomMin": 1,
                    "zoomMax": 20
                  }] }]
                }
                """)));

        Assert.Equal("Bing metadata did not contain tile subdomains.", exception.Message);
    }

    [Fact]
    public void Parse_PreservesMissingResourceFailure() {
        var exception = Assert.Throws<InvalidDataException>(() =>
            BingMetadataParser.Parse(Encoding.UTF8.GetBytes("""
                { "authenticationResultCode": "ValidCredentials", "resourceSets": [] }
                """)));

        Assert.Equal("Bing metadata did not contain an imagery resource.", exception.Message);
    }
}
