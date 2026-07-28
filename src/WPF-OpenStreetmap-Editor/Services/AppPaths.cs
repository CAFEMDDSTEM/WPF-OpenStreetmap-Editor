using System;
using System.IO;

namespace WPF_OpenStreetmap_Editor.Services;

public static class AppPaths {
    public static string BaseDirectory { get; } = Normalize(AppDomain.CurrentDomain.BaseDirectory);

    public static string DataDirectory { get; } = Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WPF-OpenStreetmap-Editor");

    public static string TileCacheDirectory => Combine(DataDirectory, "Cache", "tiles");

    public static string ThemesDirectory => Combine(DataDirectory, "Themes");

    public static string LayersFile => Combine(DataDirectory, "layers.json");

    public static string SettingsFile => Combine(DataDirectory, "settings.json");

    public static string WindowStateFile => Combine(DataDirectory, "window_state.json");

    public static string TileRequestsLogFile => Combine(DataDirectory, "tile_requests.log");

    public static string StartupLogFile => Combine(DataDirectory, "startup.log");

    public static string PluginsDirectory => Combine(DataDirectory, "Plugins");

    public static string PluginStateFile => Combine(DataDirectory, "plugins.state.json");

    public static string OsmAccountsFile => Combine(DataDirectory, "osm.accounts.json");

    public static string DocumentBackupsDirectory => Combine(DataDirectory, "Backups");

    public static string LegacyLayersFile => Combine(BaseDirectory, "layers.json");

    public static string LegacySettingsFile => Combine(BaseDirectory, "settings.json");

    public static string LegacyWindowStateFile => Combine(BaseDirectory, "window_state.json");

    public static string Combine(params string[] parts) => Normalize(Path.Combine(parts));

    public static string Normalize(string path) => Path.GetFullPath(path);

    public static string ResolveReadPath(string currentPath, string legacyPath) {
        return File.Exists(currentPath) || !File.Exists(legacyPath) ? currentPath : legacyPath;
    }
}
