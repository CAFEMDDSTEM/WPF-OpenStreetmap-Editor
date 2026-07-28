using System.IO;
using System.Text.Json;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Plugins;

public sealed class PluginTrustStore {
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _statePath;
    private readonly object _sync = new();

    public PluginTrustStore(string statePath) {
        _statePath = Path.GetFullPath(statePath);
    }

    public bool IsTrusted(string pluginId, string fingerprint) {
        lock (_sync) {
            var state = Load();
            return state.Plugins.TryGetValue(pluginId, out var record) &&
                record.Enabled &&
                string.Equals(record.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Trust(string pluginId, string fingerprint) {
        lock (_sync) {
            var state = Load();
            state.Plugins[pluginId] = new PluginTrustRecord {
                Enabled = true,
                Fingerprint = fingerprint
            };
            Save(state);
        }
    }

    private PluginTrustState Load() {
        try {
            if (!File.Exists(_statePath)) return new PluginTrustState();

            return JsonSerializer.Deserialize<PluginTrustState>(File.ReadAllText(_statePath)) ??
                new PluginTrustState();
        } catch (Exception ex) {
            Logger.Error("Plugin trust state could not be read; executable plugins will require confirmation", ex);
            return new PluginTrustState();
        }
    }

    private void Save(PluginTrustState state) {
        AtomicFile.WriteAllText(_statePath, JsonSerializer.Serialize(state, JsonOptions));
    }

    private sealed class PluginTrustState {
        public Dictionary<string, PluginTrustRecord> Plugins { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class PluginTrustRecord {
        public bool Enabled { get; set; }
        public string Fingerprint { get; set; } = "";
    }
}
