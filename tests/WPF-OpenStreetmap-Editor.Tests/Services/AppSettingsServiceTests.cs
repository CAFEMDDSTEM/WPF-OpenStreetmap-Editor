using WPF_OpenStreetmap_Editor.Services;
using System.Text.Json;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class AppSettingsServiceTests {
    [Fact]
    public void EnsureDefaults_CreatesOneActiveImageLayerFromActiveSource() {
        var settings = new AppSettings();

        AppSettingsService.EnsureDefaults(settings);

        var layer = Assert.Single(settings.ImageLayers);
        Assert.Equal(settings.ActiveLayerId, layer.Id);
        Assert.Equal(settings.ActiveSourceName, layer.SourceName);
        Assert.True(layer.IsVisible);
        Assert.True(layer.IsPrimary);
    }

    [Fact]
    public void EnsureDefaults_KeepsOnlyOnePrimaryLayer() {
        var settings = new AppSettings {
            ImageLayers = [
                new MapImageLayer { Id = "a", Name = "A", SourceName = "A", IsPrimary = true },
                new MapImageLayer { Id = "b", Name = "B", SourceName = "B", IsPrimary = true }
            ]
        };

        AppSettingsService.EnsureDefaults(settings);

        Assert.Single(settings.ImageLayers, layer => layer.IsPrimary);
    }

    [Fact]
    public void EnsureDefaults_MakesDataLayerPrimaryWhenNoPrimaryExists() {
        var settings = new AppSettings {
            ImageLayers = [
                new MapImageLayer { Id = "raster", Name = "Raster", SourceName = "Raster" },
                MapImageLayer.FromDataFile("roads.osm")
            ]
        };
        foreach (var layer in settings.ImageLayers) {
            layer.IsPrimary = false;
        }

        AppSettingsService.EnsureDefaults(settings);

        var primary = Assert.Single(settings.ImageLayers, layer => layer.IsPrimary);
        Assert.Equal(MapLayerKind.Data, primary.Kind);
    }

    [Fact]
    public void EnsureDefaults_MigratesKnownSourceEmbeddedMaxZoomToSeparateFields() {
        var settings = new AppSettings {
            ActiveSourceName = "Esri World Imagery",
            TileSources = [
                new TileSourcePreset {
                    Name = "Esri World Imagery",
                    Source = "tms[19]:https://{switch:services,server}.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer/tile/{zoom}/{y}/{x}",
                    MapMaxZoom = 19,
                    ImageMaxZoom = 19
                }
            ]
        };

        AppSettingsService.EnsureDefaults(settings);

        var source = Assert.Single(settings.TileSources, source => source.Name == "Esri 世界影像");
        Assert.Equal("Esri 世界影像", settings.ActiveSourceName);
        Assert.Equal(GeoConverter.MaxZoom, source.MapMaxZoom);
        Assert.Equal(18, source.ImageMaxZoom);
        Assert.DoesNotContain("[19]", source.Source);
        Assert.True(source.IsKnownSource);
        Assert.NotEmpty(source.AttributionText);
        Assert.StartsWith("https://", source.AttributionUrl);
    }

    [Fact]
    public void EnsureDefaults_DoesNotOverwriteUserEditedSourceUrlByName() {
        var settings = new AppSettings {
            ActiveSourceName = "OpenStreetMap（标准）",
            TileSources = [
                new TileSourcePreset {
                    Name = "OpenStreetMap（标准）",
                    Source = "xyz:https://example.com/custom/{z}/{x}/{y}.png",
                    MapMaxZoom = 22,
                    ImageMaxZoom = 21
                }
            ]
        };

        AppSettingsService.EnsureDefaults(settings);

        var source = Assert.Single(settings.TileSources, source => source.Name == "OpenStreetMap（标准）");
        Assert.Equal("xyz:https://example.com/custom/{z}/{x}/{y}.png", source.Source);
        Assert.Equal(21, source.ImageMaxZoom);
    }

    [Fact]
    public void CreateDefaults_ProvidesOpenStreetMapAttributionAndMetadataBackedBing() {
        var sources = TileSourcePreset.CreateDefaults();

        var osm = Assert.Single(sources, source => source.Name == "OpenStreetMap（标准）");
        Assert.Equal("© OpenStreetMap contributors", osm.AttributionText);
        Assert.Equal("https://www.openstreetmap.org/copyright", osm.AttributionUrl);
        var bing = Assert.Single(sources, source => source.Name == "Bing aerial imagery");
        Assert.Equal("bing[1,22]:https://www.bing.com/maps/", bing.Source);
        Assert.DoesNotContain("virtualearth.net", bing.Source, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, bing.AccessToken);
    }

    [Fact]
    public void EnsureDefaults_MigratesLegacyDirectBingSourceAndLayerToMarker() {
        const string legacySource = "xyz:https://ecn.t0.tiles.virtualearth.net/tiles/a{quadkey}.jpeg?g=1";
        var settings = new AppSettings {
            ActiveSourceName = "Legacy Bing",
            TileSources = [
                new TileSourcePreset {
                    Name = "Legacy Bing",
                    Source = legacySource,
                    MapMaxZoom = 22,
                    ImageMaxZoom = 22,
                    IsKnownSource = true
                }
            ],
            ImageLayers = [
                new MapImageLayer {
                    Name = "Legacy Bing",
                    SourceName = "Legacy Bing",
                    Source = legacySource,
                    IsVisible = true,
                    IsPrimary = true
                }
            ]
        };

        AppSettingsService.EnsureDefaults(settings);

        var bing = Assert.Single(settings.TileSources, source =>
            source.Source == "bing[1,22]:https://www.bing.com/maps/");
        var layer = Assert.Single(settings.ImageLayers);
        Assert.Equal(bing.Name, layer.SourceName);
        Assert.Equal(bing.Source, layer.Source);
        Assert.DoesNotContain(settings.TileSources, source =>
            source.Source.Contains("virtualearth.net/tiles/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TileSourcePreset_SerializationDoesNotPersistAccessToken() {
        const string secret = "do-not-write-this-token";
        var settings = new AppSettings {
            TileSources = [new TileSourcePreset { Name = "Private", AccessToken = secret }]
        };

        var json = JsonSerializer.Serialize(settings);

        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(secret, settings.Clone().TileSources[0].AccessToken);
    }

    [Fact]
    public void TileSourcePreset_DeserializationIgnoresLegacyPlaintextAccessToken() {
        const string secret = "legacy-plaintext-token";
        var json = $$"""
            {
              "TileSources": [
                {
                  "Name": "Legacy",
                  "Source": "xyz:https://tiles.example.test/{z}/{x}/{y}",
                  "AccessToken": "{{secret}}"
                }
              ]
            }
            """;

        var settings = JsonSerializer.Deserialize<AppSettings>(json);

        var source = Assert.Single(Assert.IsType<AppSettings>(settings).TileSources);
        Assert.Equal(string.Empty, source.AccessToken);
    }

    [Fact]
    public void RemovePersistedAccessTokens_RemovesOnlyTokenProperties() {
        const string secret = "legacy-plaintext-token";
        var json = $$"""
            {
              "UnknownSetting": true,
              "TileSources": [
                {
                  "Source": "https://example.test/{z}?access_token={access_token}",
                  "AccessToken": "{{secret}}"
                }
              ]
            }
            """;

        var sanitized = AppSettingsService.RemovePersistedAccessTokens(json);

        Assert.DoesNotContain(secret, sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"AccessToken\"", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UnknownSetting", sanitized, StringComparison.Ordinal);
        Assert.Contains("access_token={access_token}", sanitized, StringComparison.Ordinal);
    }
}
