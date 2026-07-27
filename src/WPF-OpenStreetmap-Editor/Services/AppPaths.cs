using System;
using System.IO;

namespace WPF_OpenStreetmap_Editor.Services;

public static class AppPaths {
    public static string BaseDirectory { get; } = Normalize(AppDomain.CurrentDomain.BaseDirectory);

    public static string TileCacheDirectory => Combine(BaseDirectory, "Cache", "tiles");

    public static string LayersFile => Combine(BaseDirectory, "layers.json");

    public static string WindowStateFile => Combine(BaseDirectory, "window_state.json");

    public static string TileRequestsLogFile => Combine(BaseDirectory, "tile_requests.log");

    public static string Combine(params string[] parts) => Normalize(Path.Combine(parts));

    public static string Normalize(string path) => Path.GetFullPath(path);
}
