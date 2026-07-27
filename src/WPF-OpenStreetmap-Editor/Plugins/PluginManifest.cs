using System.Text.Json;

namespace WPF_OpenStreetmap_Editor.Plugins;

public enum PluginKind {
    Native,
    Process,
    Addon
}

public sealed class PluginManifest {
    public int SchemaVersion { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Icon { get; set; } = "";
    public string DescriptionFile { get; set; } = "";
    public string Kind { get; set; } = "";
    public PluginRuntimeManifest? Runtime { get; set; }
    public List<string> Hooks { get; set; } = [];
    public PluginContributionsManifest Contributions { get; set; } = new();
}

public sealed class PluginRuntimeManifest {
    public string Entry { get; set; } = "";
    public List<string> Arguments { get; set; } = [];
    public List<string> HostActions { get; set; } = [];
    public int TimeoutMilliseconds { get; set; } = 5000;
    public int MemoryLimitMegabytes { get; set; } = 1024;
}

public sealed class PluginContributionsManifest {
    public List<PluginMenuManifest> Menus { get; set; } = [];
    public List<PluginToolbarManifest> Toolbar { get; set; } = [];
    public List<PluginCommandManifest> Commands { get; set; } = [];
}

public sealed class PluginMenuManifest {
    public string Location { get; set; } = "tools";
    public string Label { get; set; } = "";
    public string Command { get; set; } = "";
}

public sealed class PluginToolbarManifest {
    public string Location { get; set; } = "main";
    public string Icon { get; set; } = "";
    public string ToolTip { get; set; } = "";
    public string Command { get; set; } = "";
    public int Order { get; set; }
}

public sealed class PluginCommandManifest {
    public string Id { get; set; } = "";
    public List<PluginActionManifest> Actions { get; set; } = [];
}

public sealed class PluginActionManifest {
    public string Type { get; set; } = "";
    public JsonElement Arguments { get; set; }
}
