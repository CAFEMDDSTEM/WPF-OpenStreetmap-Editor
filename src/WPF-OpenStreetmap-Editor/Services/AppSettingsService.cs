using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed class AppSettings {
    public string ThemeId { get; set; } = ThemeService.SystemThemeId;
    public string LanguageId { get; set; } = LocalizationService.SystemLanguageId;
    public string ActiveSourceName { get; set; } = "Esri 世界影像";
    public string ActiveLayerId { get; set; } = "";
    public int MapMaxZoom { get; set; } = GeoConverter.MaxZoom;
    public bool ExperimentalSmoothZoom { get; set; }
    public List<TileSourcePreset> TileSources { get; set; } = TileSourcePreset.CreateDefaults();
    public List<MapImageLayer> ImageLayers { get; set; } = [];

    public TileSourcePreset GetActiveSource() {
        return TileSources.FirstOrDefault(source => source.Name == ActiveSourceName) ??
            TileSources.FirstOrDefault() ??
            TileSourcePreset.CreateDefaults()[0];
    }

    public MapImageLayer? GetActiveLayer() {
        return ImageLayers.FirstOrDefault(layer => layer.IsPrimary) ??
            ImageLayers.FirstOrDefault(layer => layer.Id == ActiveLayerId) ??
            ImageLayers.LastOrDefault(layer => layer.IsVisible) ??
            ImageLayers.LastOrDefault();
    }

    public TileSourcePreset? GetSourceForLayer(MapImageLayer? layer) {
        if (layer is null) return null;

        return TileSources.FirstOrDefault(source => source.Name == layer.SourceName) ??
            TileSources.FirstOrDefault(source => string.Equals(source.Source, layer.Source, StringComparison.Ordinal));
    }

    public AppSettings Clone() {
        return new AppSettings {
            ThemeId = ThemeId,
            LanguageId = LanguageId,
            ActiveSourceName = ActiveSourceName,
            ActiveLayerId = ActiveLayerId,
            MapMaxZoom = MapMaxZoom,
            ExperimentalSmoothZoom = ExperimentalSmoothZoom,
            TileSources = [.. TileSources.Select(static source => source.Clone())],
            ImageLayers = [.. ImageLayers.Select(static layer => layer.Clone())]
        };
    }
}

public sealed class TileSourcePreset {
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public int MapMaxZoom { get; set; } = GeoConverter.MaxZoom;
    public int ImageMaxZoom { get; set; } = GeoConverter.MaxZoom;

    [JsonIgnore]
    public string AccessToken { get; set; } = "";
    public string AttributionText { get; set; } = "";
    public string AttributionUrl { get; set; } = "";
    public bool IsVisible { get; set; } = true;
    public bool IsKnownSource { get; set; }
    public List<string> NoTileEtags { get; set; } = [];
    public List<string> NoTileMd5s { get; set; } = [];

    public static List<TileSourcePreset> CreateDefaults() {
        return [
            new() {
                Name = "Esri 世界影像",
                Source = "tms:https://{switch:services,server}.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer/tile/{zoom}/{y}/{x}",
                MapMaxZoom = GeoConverter.MaxZoom,
                ImageMaxZoom = 18,
                IsKnownSource = true,
                AttributionText = "Source: Esri World Imagery",
                AttributionUrl = "https://www.esri.com/en-us/legal/terms/full-master-agreement",
                NoTileEtags = ["\"vvvvvvvvvvvvf\""],
                NoTileMd5s = ["f27d9de7f80c13501f470595e327aa6d"]
            },
            new() {
                Name = "Esri 清晰世界影像（测试版）",
                Source = "tms:https://clarity.maptiles.arcgis.com/arcgis/rest/services/World_Imagery/MapServer/tile/{zoom}/{y}/{x}",
                MapMaxZoom = GeoConverter.MaxZoom,
                ImageMaxZoom = 18,
                IsKnownSource = true,
                AttributionText = "Source: Esri World Imagery (Clarity)",
                AttributionUrl = "https://www.esri.com/en-us/legal/terms/full-master-agreement",
                NoTileEtags = ["\"vvvvvvvvvvvvf\""],
                NoTileMd5s = ["f27d9de7f80c13501f470595e327aa6d"]
            },
            new() {
                Name = "Mapbox 卫星",
                Source = "xyz:https://api.mapbox.com/styles/v1/mapbox/satellite-v9/tiles/256/{z}/{x}/{y}?access_token={access_token}",
                MapMaxZoom = GeoConverter.MaxZoom,
                ImageMaxZoom = 22,
                IsKnownSource = true,
                AttributionText = "© Mapbox © OpenStreetMap contributors",
                AttributionUrl = "https://www.mapbox.com/about/maps/"
            },
            new() {
                Name = "OpenAerialMap 融合图层，由 Kontur.io 提供",
                Source = "xyz:https://tiles.openaerialmap.org/5a9c5d7c6d1a6b0010b81b70/0/{z}/{x}/{y}.png",
                MapMaxZoom = GeoConverter.MaxZoom,
                ImageMaxZoom = 19,
                IsKnownSource = true,
                AttributionText = "© OpenAerialMap contributors",
                AttributionUrl = "https://openaerialmap.org/"
            },
            new() {
                Name = "OpenStreetMap（标准）",
                Source = "xyz:https://tile.openstreetmap.org/{z}/{x}/{y}.png",
                MapMaxZoom = GeoConverter.MaxZoom,
                ImageMaxZoom = 19,
                IsKnownSource = true,
                AttributionText = "© OpenStreetMap contributors",
                AttributionUrl = "https://www.openstreetmap.org/copyright"
            },
            new() {
                Name = "OpenTopoMap",
                Source = "xyz:https://{switch:a,b,c}.tile.opentopomap.org/{z}/{x}/{y}.png",
                MapMaxZoom = GeoConverter.MaxZoom,
                ImageMaxZoom = 17,
                IsKnownSource = true,
                AttributionText = "Map data © OpenStreetMap contributors, SRTM | Map style © OpenTopoMap (CC-BY-SA)",
                AttributionUrl = "https://opentopomap.org/about"
            }
        ];
    }

    public TileSourcePreset Clone() {
        return new TileSourcePreset {
            Name = Name,
            Source = Source,
            MapMaxZoom = MapMaxZoom,
            ImageMaxZoom = ImageMaxZoom,
            AccessToken = AccessToken,
            AttributionText = AttributionText,
            AttributionUrl = AttributionUrl,
            IsVisible = IsVisible,
            IsKnownSource = IsKnownSource,
            NoTileEtags = [.. NoTileEtags],
            NoTileMd5s = [.. NoTileMd5s]
        };
    }
}

public sealed class MapImageLayer {
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string Source { get; set; } = "";
    public bool IsVisible { get; set; } = true;
    public bool IsPrimary { get; set; }
    public MapLayerKind Kind { get; set; } = MapLayerKind.Raster;
    public double Opacity { get; set; } = 1.0;
    public bool HasTransparency { get; set; }
    public string DataPath { get; set; } = "";

    [JsonIgnore]
    public string VisibilityLabel => IsVisible ? "隐藏" : "显示";

    [JsonIgnore]
    public string KindLabel => Kind == MapLayerKind.Data ? "数据" : "影像";

    public static MapImageLayer FromSource(TileSourcePreset source) {
        return new MapImageLayer {
            Id = Guid.NewGuid().ToString("N"),
            Name = source.Name,
            SourceName = source.Name,
            Source = source.Source,
            IsVisible = true,
            Kind = MapLayerKind.Raster,
            Opacity = 1.0
        };
    }

    public static MapImageLayer FromDataFile(string path) {
        return new MapImageLayer {
            Id = Guid.NewGuid().ToString("N"),
            Name = Path.GetFileName(path),
            SourceName = "",
            Source = "",
            IsVisible = true,
            IsPrimary = true,
            Kind = MapLayerKind.Data,
            Opacity = 1.0,
            DataPath = path
        };
    }

    public MapImageLayer Clone() {
        return new MapImageLayer {
            Id = Id,
            Name = Name,
            SourceName = SourceName,
            Source = Source,
            IsVisible = IsVisible,
            IsPrimary = IsPrimary,
            Kind = Kind,
            Opacity = Opacity,
            HasTransparency = HasTransparency,
            DataPath = DataPath
        };
    }
}

public static class AppSettingsService {
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load() {
        try {
            var settingsPath = AppPaths.ResolveReadPath(AppPaths.SettingsFile, AppPaths.LegacySettingsFile);
            if (!File.Exists(settingsPath)) {
                return new AppSettings();
            }

            var json = File.ReadAllText(settingsPath);
            var containsPersistedToken = ContainsPersistedAccessToken(json);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            EnsureDefaults(settings);
            if (containsPersistedToken) {
                TryRemovePersistedAccessTokens(settingsPath, json);
            }
            if (containsPersistedToken ||
                !string.Equals(settingsPath, AppPaths.SettingsFile, StringComparison.OrdinalIgnoreCase)) {
                Save(settings);
            }
            return settings;
        } catch (Exception ex) {
            Logger.Error("Failed to load settings", ex);
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings) {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.SettingsFile)!);
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(settings, JsonOptions));
        } catch (Exception ex) {
            Logger.Error("Failed to save settings", ex);
        }
    }

    public static void EnsureDefaults(AppSettings settings) {
        if (string.IsNullOrWhiteSpace(settings.ThemeId)) {
            settings.ThemeId = ThemeService.SystemThemeId;
        }

        settings.LanguageId = LocalizationService.NormalizeLanguageId(settings.LanguageId);

        var defaults = TileSourcePreset.CreateDefaults();

        foreach (var preset in defaults) {
            var existing = settings.TileSources.FirstOrDefault(source => SourcesMatch(source.Source, preset.Source));
            var sourceMatchesPreset = existing is not null;
            existing ??= settings.TileSources.FirstOrDefault(source => source.Name == preset.Name);
            if (existing is null) {
                settings.TileSources.Add(preset);
                continue;
            }

            var oldName = existing.Name;
            var oldSource = existing.Source;
            var isLegacyKnownSource = IsLegacyKnownSource(existing, preset);
            if (sourceMatchesPreset || isLegacyKnownSource) {
                existing.Name = preset.Name;
                existing.Source = preset.Source;
                existing.AttributionText = preset.AttributionText;
                existing.AttributionUrl = preset.AttributionUrl;
            }

            if (settings.ActiveSourceName == oldName) {
                settings.ActiveSourceName = existing.Name;
            }

            existing.IsKnownSource |= preset.IsKnownSource;
            existing.MapMaxZoom = Math.Max(existing.MapMaxZoom, preset.MapMaxZoom);
            if (isLegacyKnownSource ||
                ((HasEmbeddedMaxZoomPrefix(oldSource) || existing.ImageMaxZoom == GeoConverter.MaxZoom) &&
                    preset.ImageMaxZoom != GeoConverter.MaxZoom)) {
                existing.ImageMaxZoom = preset.ImageMaxZoom;
            }

            if (existing.NoTileEtags.Count == 0 && preset.NoTileEtags.Count > 0) {
                existing.NoTileEtags = [.. preset.NoTileEtags];
            }

            if (existing.NoTileMd5s.Count == 0 && preset.NoTileMd5s.Count > 0) {
                existing.NoTileMd5s = [.. preset.NoTileMd5s];
            }
        }

        if (settings.ImageLayers.Count == 0) {
            settings.ImageLayers.Add(MapImageLayer.FromSource(settings.GetActiveSource()));
        }

        settings.MapMaxZoom = Math.Clamp(settings.MapMaxZoom, GeoConverter.MinZoom, GeoConverter.MaxZoom);
        foreach (var source in settings.TileSources) {
            source.MapMaxZoom = Math.Clamp(source.MapMaxZoom, GeoConverter.MinZoom, GeoConverter.MaxZoom);
            source.ImageMaxZoom = Math.Clamp(source.ImageMaxZoom, GeoConverter.MinZoom, GeoConverter.MaxZoom);
        }

        foreach (var layer in settings.ImageLayers) {
            if (string.IsNullOrWhiteSpace(layer.Id)) {
                layer.Id = Guid.NewGuid().ToString("N");
            }

            layer.Opacity = Math.Clamp(layer.Opacity, 0.0, 1.0);
            var source = settings.GetSourceForLayer(layer);
            if (source is null || layer.Kind == MapLayerKind.Data) continue;

            layer.SourceName = source.Name;
            layer.Source = source.Source;
            if (string.IsNullOrWhiteSpace(layer.Name)) {
                layer.Name = source.Name;
            }
        }

        EnsureSinglePrimaryLayer(settings);
        settings.ActiveLayerId = settings.GetActiveLayer()?.Id ?? "";
        settings.ActiveSourceName = settings.GetSourceForLayer(settings.GetActiveLayer())?.Name ??
            settings.GetActiveSource().Name;
    }

    public static void EnsureSinglePrimaryLayer(AppSettings settings) {
        var primary = settings.ImageLayers.FirstOrDefault(layer => layer.IsPrimary) ??
            settings.ImageLayers.FirstOrDefault(layer => layer.Kind == MapLayerKind.Data) ??
            settings.ImageLayers.FirstOrDefault(layer => layer.Id == settings.ActiveLayerId) ??
            settings.ImageLayers.FirstOrDefault();
        foreach (var layer in settings.ImageLayers) {
            layer.IsPrimary = ReferenceEquals(layer, primary);
        }
    }

    public static bool MoveImageLayer(AppSettings settings, MapImageLayer layer, int insertIndex) {
        var oldIndex = settings.ImageLayers.FindIndex(candidate =>
            ReferenceEquals(candidate, layer) || candidate.Id == layer.Id);
        if (oldIndex < 0) return false;

        insertIndex = Math.Clamp(insertIndex, 0, settings.ImageLayers.Count);
        if (insertIndex > oldIndex) {
            insertIndex--;
        }

        if (insertIndex == oldIndex) return false;

        var settingsLayer = settings.ImageLayers[oldIndex];
        settings.ImageLayers.RemoveAt(oldIndex);
        settings.ImageLayers.Insert(Math.Clamp(insertIndex, 0, settings.ImageLayers.Count), settingsLayer);
        return true;
    }

    private static bool SourcesMatch(string left, string right) {
        return string.Equals(
            NormalizeSourceForDefaultMatch(left),
            NormalizeSourceForDefaultMatch(right),
            StringComparison.Ordinal);
    }

    private static string NormalizeSourceForDefaultMatch(string source) {
        var value = source.Trim();
        var schemeIndex = value.IndexOf("://", StringComparison.Ordinal);
        var prefixEnd = value.IndexOf(':');
        if (schemeIndex > 0 && prefixEnd >= 0 && prefixEnd < schemeIndex) {
            value = value[(prefixEnd + 1)..];
        }

        return value;
    }

    private static bool HasEmbeddedMaxZoomPrefix(string source) {
        var value = source.Trim();
        var schemeIndex = value.IndexOf("://", StringComparison.Ordinal);
        var prefixEnd = value.IndexOf(':');
        return schemeIndex > 0 &&
            prefixEnd > 0 &&
            prefixEnd < schemeIndex &&
            value[..prefixEnd].Contains('[', StringComparison.Ordinal) &&
            value[..prefixEnd].Contains(']', StringComparison.Ordinal);
    }

    private static bool IsLegacyKnownSource(TileSourcePreset source, TileSourcePreset preset) {
        return HasEmbeddedMaxZoomPrefix(source.Source) &&
            SourcesMatch(source.Source, preset.Source);
    }

    private static bool ContainsPersistedAccessToken(string json) {
        using var document = JsonDocument.Parse(json);
        return ContainsPersistedAccessToken(document.RootElement);
    }

    private static bool ContainsPersistedAccessToken(JsonElement element) {
        if (element.ValueKind == JsonValueKind.Object) {
            foreach (var property in element.EnumerateObject()) {
                if (property.Name.Equals("AccessToken", StringComparison.OrdinalIgnoreCase) ||
                    ContainsPersistedAccessToken(property.Value)) {
                    return true;
                }
            }
        } else if (element.ValueKind == JsonValueKind.Array) {
            foreach (var item in element.EnumerateArray()) {
                if (ContainsPersistedAccessToken(item)) return true;
            }
        }

        return false;
    }

    private static void TryRemovePersistedAccessTokens(string path, string json) {
        try {
            File.WriteAllText(path, RemovePersistedAccessTokens(json));
        } catch (Exception ex) {
            Logger.Error("Failed to remove a legacy access token from settings", ex);
        }
    }

    internal static string RemovePersistedAccessTokens(string json) {
        var root = JsonNode.Parse(json) ?? throw new JsonException("Settings JSON is empty");
        RemovePersistedAccessTokens(root);
        return root.ToJsonString(JsonOptions);
    }

    private static bool RemovePersistedAccessTokens(JsonNode node) {
        var removed = false;
        if (node is JsonObject obj) {
            foreach (var property in obj.ToList()) {
                if (property.Key.Equals("AccessToken", StringComparison.OrdinalIgnoreCase)) {
                    obj.Remove(property.Key);
                    removed = true;
                } else if (property.Value is not null) {
                    removed |= RemovePersistedAccessTokens(property.Value);
                }
            }
        } else if (node is JsonArray array) {
            foreach (var item in array) {
                if (item is not null) removed |= RemovePersistedAccessTokens(item);
            }
        }

        return removed;
    }
}
