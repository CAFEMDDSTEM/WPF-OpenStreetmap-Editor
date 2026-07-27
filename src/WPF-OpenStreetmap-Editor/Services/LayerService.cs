using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace WPF_OpenStreetmap_Editor.Services;

public static class LayerService {
    private static readonly string LayersFile = AppPaths.LayersFile;

    public static List<string> LoadLayers() {
        try {
            if (!File.Exists(LayersFile))
                return [];

            var json = File.ReadAllText(LayersFile, Encoding.UTF8);
            return JsonSerializer.Deserialize<string[]>(json)?.ToList() ?? [];
        } catch (Exception ex) {
            Logger.Error("Failed to load layers", ex);
            return [];
        }
    }

    public static void SaveLayers(IEnumerable<string> layers) {
        try {
            var json = JsonSerializer.Serialize(layers.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(LayersFile, json, Encoding.UTF8);
        } catch (Exception ex) {
            Logger.Error("Failed to save layers", ex);
        }
    }
}
