using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace WPF_OpenStreetmap_Editor.Services;

public static class LayerService {
    public static List<string> LoadLayers() {
        try {
            var layersFile = AppPaths.ResolveReadPath(AppPaths.LayersFile, AppPaths.LegacyLayersFile);
            if (!File.Exists(layersFile))
                return [];

            var json = File.ReadAllText(layersFile, Encoding.UTF8);
            return JsonSerializer.Deserialize<string[]>(json)?.ToList() ?? [];
        } catch (Exception ex) {
            Logger.Error("Failed to load layers", ex);
            return [];
        }
    }

    public static void SaveLayers(IEnumerable<string> layers) {
        try {
            var json = JsonSerializer.Serialize(layers.ToList(), new JsonSerializerOptions { WriteIndented = true });
            Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.LayersFile)!);
            File.WriteAllText(AppPaths.LayersFile, json, Encoding.UTF8);
        } catch (Exception ex) {
            Logger.Error("Failed to save layers", ex);
        }
    }
}
