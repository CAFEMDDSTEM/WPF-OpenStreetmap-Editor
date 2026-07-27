using System.IO;
using System.Text;

namespace WPF_OpenStreetmap_Editor.Plugins;

public static class BuiltInPluginCatalog {
    public const string OsmTransferPluginId = "org.openstreetmap.transfer";
    private const string OsmTransferIconFileName = "icon.jpg";
    private const string OsmTransferDescriptionFileName = "description.md";
    private const string OsmTransferIconResourceName =
        "WPF_OpenStreetmap_Editor.Plugins.BuiltIn.OsmTransfer.icon.jpg";

    public static void EnsureInstalled(string pluginsDirectory) {
        var packageDirectory = Path.Combine(pluginsDirectory, OsmTransferPluginId);
        var manifestPath = Path.Combine(packageDirectory, PluginManifestReader.ManifestFileName);
        Directory.CreateDirectory(packageDirectory);
        WriteTextIfChanged(manifestPath, OsmTransferManifest);
        WriteTextIfChanged(
            Path.Combine(packageDirectory, OsmTransferDescriptionFileName),
            OsmTransferDescription);
        WriteEmbeddedResourceIfChanged(
            Path.Combine(packageDirectory, OsmTransferIconFileName),
            OsmTransferIconResourceName);
    }

    private static void WriteTextIfChanged(string path, string content) {
        if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == content) return;
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private static void WriteEmbeddedResourceIfChanged(string path, string resourceName) {
        using var stream = typeof(BuiltInPluginCatalog).Assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException($"Missing built-in plugin resource '{resourceName}'.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var content = buffer.ToArray();
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content)) return;
        File.WriteAllBytes(path, content);
    }

    private const string OsmTransferManifest = """
        {
          schemaVersion: 1,
          id: 'org.openstreetmap.transfer',
          name: 'OpenStreetMap 传输',
          version: '1.0.0',
          icon: 'icon.jpg',
          descriptionFile: 'description.md',
          kind: 'addon',
          contributions: {
            toolbar: [
              {
                location: 'main',
                icon: 'Download',
                tooltip: '打开 OSM 下载窗口 (Ctrl+Shift+↓)',
                command: 'download',
                order: 10
              },
              {
                location: 'main',
                icon: 'Upload',
                tooltip: '上传当前更改到 OSM (Ctrl+Shift+↑)',
                command: 'upload',
                order: 20
              }
            ],
            menus: [
              { location: 'tools', label: '下载 OSM 数据', command: 'download' },
              { location: 'tools', label: '上传到 OSM', command: 'upload' },
              { location: 'tools', label: 'OSM 账号...', command: 'accounts' }
            ],
            commands: [
              { id: 'download', actions: [{ type: 'downloadOsm', arguments: {} }] },
              { id: 'upload', actions: [{ type: 'uploadOsm', arguments: {} }] },
              { id: 'accounts', actions: [{ type: 'manageOsmAccounts', arguments: {} }] }
            ]
          }
        }
        """;

    private const string OsmTransferDescription = """
        # OpenStreetMap 传输

        按框选范围下载 OSM 数据、上传当前变更并切换多个 OSM 账号。
        """;
}
