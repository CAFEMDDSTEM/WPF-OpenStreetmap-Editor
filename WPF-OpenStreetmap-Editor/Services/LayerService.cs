using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace WPF_OpenStreetmap_Editor.Services;

public static class LayerService {
    private static readonly string LayersFile = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "layers.json");

    public static List<string> LoadLayers() {
        try {
            if (!File.Exists(LayersFile))
                return new List<string>();

            var json = File.ReadAllText(LayersFile, Encoding.UTF8);
            return JsonSerializer.Deserialize<string[]>(json)?.ToList() ?? new List<string>();
        } catch (Exception ex) {
            Logger.Error("Failed to load layers", ex);
            return new List<string>();
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
