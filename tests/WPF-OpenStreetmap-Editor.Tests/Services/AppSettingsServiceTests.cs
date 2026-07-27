using WPF_OpenStreetmap_Editor.Services;
using System.Text.Json;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class AppSettingsServiceTests {
    [Fact]
    public void Clone_PreservesThemeSelection() {
        var settings = new AppSettings { ThemeId = "community.high-contrast" };

        var clone = settings.Clone();

        Assert.Equal("community.high-contrast", clone.ThemeId);
    }

    [Fact]
    public void Clone_PreservesImportProjectionSettings() {
        var settings = new AppSettings {
            DefaultImportProjectionId = ProjectionService.CustomWktId,
            CustomImportProjectionWkt = "GEOGCS[\"Custom\",DATUM[\"D\",SPHEROID[\"S\",6378137,298.257223563]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]]"
        };

        var clone = settings.Clone();

        Assert.Equal(ProjectionService.CustomWktId, clone.DefaultImportProjectionId);
        Assert.Equal(settings.CustomImportProjectionWkt, clone.CustomImportProjectionWkt);
    }

    [Fact]
    public void EnsureDefaults_NormalizesImportProjectionSettings() {
        var settings = new AppSettings {
            DefaultImportProjectionId = "EPSG900913",
            CustomImportProjectionWkt = null!
        };

        AppSettingsService.EnsureDefaults(settings);

        Assert.Equal(ProjectionService.WebMercatorId, settings.DefaultImportProjectionId);
        Assert.Equal(string.Empty, settings.CustomImportProjectionWkt);
    }

    [Fact]
    public void EnsureDefaults_ReplacesEmptyThemeWithSystemTheme() {
        var settings = new AppSettings { ThemeId = " " };

        AppSettingsService.EnsureDefaults(settings);

        Assert.Equal(ThemeService.SystemThemeId, settings.ThemeId);
    }

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

    [Theory]
    [InlineData(-0.25, 0.0)]
    [InlineData(1.25, 1.0)]
    public void EnsureDefaults_ClampsLayerOpacity(double opacity, double expected) {
        var settings = new AppSettings();
        AppSettingsService.EnsureDefaults(settings);
        settings.ImageLayers[0].Opacity = opacity;

        AppSettingsService.EnsureDefaults(settings);

        Assert.Equal(expected, settings.ImageLayers[0].Opacity);
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
    public void MoveImageLayer_MovesLayerDownUsingOriginalInsertIndex() {
        var settings = CreateLayerOrderSettings();

        var moved = AppSettingsService.MoveImageLayer(settings, settings.ImageLayers[0], 3);

        Assert.True(moved);
        Assert.Equal(["b", "c", "a", "d"], settings.ImageLayers.Select(static layer => layer.Id));
    }

    [Fact]
    public void MoveImageLayer_MovesLayerUp() {
        var settings = CreateLayerOrderSettings();

        var moved = AppSettingsService.MoveImageLayer(settings, settings.ImageLayers[3], 1);

        Assert.True(moved);
        Assert.Equal(["a", "d", "b", "c"], settings.ImageLayers.Select(static layer => layer.Id));
    }

    [Fact]
    public void MoveImageLayer_ReturnsFalseWhenLayerStaysInPlace() {
        var settings = CreateLayerOrderSettings();

        var moved = AppSettingsService.MoveImageLayer(settings, settings.ImageLayers[1], 2);

        Assert.False(moved);
        Assert.Equal(["a", "b", "c", "d"], settings.ImageLayers.Select(static layer => layer.Id));
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
    public void CreateDefaults_ProvidesOpenStreetMapAttributionWithoutBing() {
        var sources = TileSourcePreset.CreateDefaults();

        var osm = Assert.Single(sources, source => source.Name == "OpenStreetMap（标准）");
        Assert.Equal("© OpenStreetMap contributors", osm.AttributionText);
        Assert.Equal("https://www.openstreetmap.org/copyright", osm.AttributionUrl);
        Assert.DoesNotContain(sources, source =>
            source.Source.StartsWith("bing:", StringComparison.OrdinalIgnoreCase) ||
            source.Source.StartsWith("bing[", StringComparison.OrdinalIgnoreCase) ||
            source.Source.Contains("virtualearth.net", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnsureDefaults_DoesNotRewriteLegacyBingSourceAndLayer() {
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

        var bing = Assert.Single(settings.TileSources, source => source.Name == "Legacy Bing");
        var layer = Assert.Single(settings.ImageLayers);
        Assert.Equal(bing.Name, layer.SourceName);
        Assert.Equal(legacySource, bing.Source);
        Assert.Equal(legacySource, layer.Source);
        Assert.DoesNotContain(settings.TileSources, source =>
            source.Source.StartsWith("bing:", StringComparison.OrdinalIgnoreCase) ||
            source.Source.StartsWith("bing[", StringComparison.OrdinalIgnoreCase));
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

    private static AppSettings CreateLayerOrderSettings() {
        return new AppSettings {
            ImageLayers = [
                Layer("a"),
                Layer("b"),
                Layer("c"),
                Layer("d")
            ]
        };
    }

    private static MapImageLayer Layer(string id) {
        return new MapImageLayer {
            Id = id,
            Name = id,
            SourceName = id,
            Source = $"xyz:https://tiles.example.com/{id}/{{z}}/{{x}}/{{y}}.png"
        };
    }
}
