using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace WPF_OpenStreetmap_Editor.Plugins;

internal sealed partial class JavaPluginPackageBuilder {
    private const string PluginJarDirectoryName = "josm-plugins";
    private const string JosmCoreDirectoryName = "josm-core";
    private const string PluginCommandId = "josm.inspect";
    private static readonly byte[] DefaultIconBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private static readonly HashSet<string> SupportedIconExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".png",
        ".ico",
        ".jpg",
        ".jpeg"
    };

    public static void CreatePackage(string sourceJarPath, string packageDirectory) {
        Directory.CreateDirectory(packageDirectory);
        var metadata = ReadJosmMetadata(sourceJarPath);
        if (string.IsNullOrWhiteSpace(metadata.PluginClass)) {
            throw new InvalidDataException(
                "The .jar package is neither a WOSM plugin archive nor a JOSM plugin with Plugin-Class metadata.");
        }

        CopyJavaBridgeRuntime(packageDirectory);

        var pluginJarDirectory = Path.Combine(packageDirectory, PluginJarDirectoryName);
        Directory.CreateDirectory(pluginJarDirectory);
        var safeJarName = SanitizeFileName(Path.GetFileName(sourceJarPath));
        File.Copy(sourceJarPath, Path.Combine(pluginJarDirectory, safeJarName), overwrite: false);

        CopyLocalJosmCore(packageDirectory);
        WriteIcon(sourceJarPath, metadata, packageDirectory);
        WriteDescription(sourceJarPath, metadata, packageDirectory);
        WriteManifest(sourceJarPath, metadata, packageDirectory);
    }

    private static JosmPluginMetadata ReadJosmMetadata(string sourceJarPath) {
        using var archive = ZipFile.OpenRead(sourceJarPath);
        var entry = archive.GetEntry("META-INF/MANIFEST.MF") ??
            archive.GetEntry("MANIFEST.MF");
        if (entry is null) {
            throw new InvalidDataException("JOSM plugin jars must contain META-INF/MANIFEST.MF.");
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        var attributes = ParseManifestAttributes(reader.ReadToEnd());
        attributes.TryGetValue("Plugin-Class", out var pluginClass);
        attributes.TryGetValue("Plugin-Description", out var description);
        attributes.TryGetValue("Plugin-Version", out var version);
        attributes.TryGetValue("Plugin-Date", out var date);
        attributes.TryGetValue("Plugin-Icon", out var icon);
        attributes.TryGetValue("Plugin-Link", out var link);
        attributes.TryGetValue("Plugin-Mainversion", out var mainVersion);
        attributes.TryGetValue("Plugin-Canloadatruntime", out var canLoadAtRuntime);
        attributes.TryGetValue("Author", out var author);
        return new JosmPluginMetadata(
            pluginClass ?? "",
            description ?? "",
            version ?? "",
            date ?? "",
            icon ?? "",
            link ?? "",
            mainVersion ?? "",
            author ?? "",
            string.Equals(canLoadAtRuntime, "true", StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string> ParseManifestAttributes(string manifestText) {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentName = null;
        var currentValue = new StringBuilder();
        using var reader = new StringReader(manifestText.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n'));
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            if (line.Length == 0) {
                FlushAttribute();
                currentName = null;
                currentValue.Clear();
                continue;
            }
            if (line[0] == ' ' && currentName is not null) {
                currentValue.Append(line[1..]);
                continue;
            }

            FlushAttribute();
            currentValue.Clear();
            var separator = line.IndexOf(':');
            if (separator <= 0) {
                currentName = null;
                currentValue.Clear();
                continue;
            }
            currentName = line[..separator];
            var valueOffset = separator + 1;
            if (valueOffset < line.Length && line[valueOffset] == ' ') valueOffset++;
            currentValue.Append(line[valueOffset..]);
        }
        FlushAttribute();
        return attributes;

        void FlushAttribute() {
            if (currentName is not null) {
                attributes[currentName] = currentValue.ToString().Trim();
            }
        }
    }

    private static void CopyLocalJosmCore(string packageDirectory) {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData)) return;

        var candidates = new[] {
            Path.Combine(localAppData, "JOSM", "app", "josm-custom.jar"),
            Path.Combine(localAppData, "JOSM", "app", "josm.jar")
        };
        var coreJar = candidates.FirstOrDefault(File.Exists);
        if (coreJar is null) return;

        var coreDirectory = Path.Combine(packageDirectory, JosmCoreDirectoryName);
        Directory.CreateDirectory(coreDirectory);
        File.Copy(coreJar, Path.Combine(coreDirectory, Path.GetFileName(coreJar)), overwrite: false);
    }

    private static void CopyJavaBridgeRuntime(string packageDirectory) {
        var bridgeRuntimeDirectory = JavaSupportRuntimeLocator.FindBridgeRuntimeDirectory();
        var targetDirectory = Path.Combine(packageDirectory, JavaSupportRuntimeLocator.BridgeRuntimeDirectoryName);
        Directory.CreateDirectory(targetDirectory);
        foreach (var file in Directory.EnumerateFiles(bridgeRuntimeDirectory, "*", SearchOption.AllDirectories)) {
            var relativePath = Path.GetRelativePath(bridgeRuntimeDirectory, file);
            var destination = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: false);
        }
    }

    private static void WriteIcon(string sourceJarPath, JosmPluginMetadata metadata, string packageDirectory) {
        if (!string.IsNullOrWhiteSpace(metadata.IconPath) &&
            SupportedIconExtensions.Contains(Path.GetExtension(metadata.IconPath))) {
            using var archive = ZipFile.OpenRead(sourceJarPath);
            var entry = archive.GetEntry(metadata.IconPath.Replace('\\', '/'));
            if (entry is not null && entry.Length is > 0 and <= 2 * 1024 * 1024) {
                var extension = Path.GetExtension(metadata.IconPath).ToLowerInvariant();
                entry.ExtractToFile(Path.Combine(packageDirectory, "icon" + extension), overwrite: false);
                return;
            }
        }

        File.WriteAllBytes(Path.Combine(packageDirectory, "icon.png"), DefaultIconBytes);
    }

    private static void WriteDescription(string sourceJarPath, JosmPluginMetadata metadata, string packageDirectory) {
        var builder = new StringBuilder();
        builder.AppendLine($"# {GetDisplayName(sourceJarPath, metadata)}");
        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(metadata.Description)) {
            builder.AppendLine(metadata.Description);
            builder.AppendLine();
        }
        builder.AppendLine("Imported through the WOSM Java/JOSM compatibility bridge.");
        builder.AppendLine();
        builder.AppendLine($"- JOSM plugin class: `{metadata.PluginClass}`");
        if (!string.IsNullOrWhiteSpace(metadata.JosmMainVersion)) {
            builder.AppendLine($"- Required JOSM main version: `{metadata.JosmMainVersion}`");
        }
        if (!string.IsNullOrWhiteSpace(metadata.Author)) {
            builder.AppendLine($"- Author: {metadata.Author}");
        }
        if (!string.IsNullOrWhiteSpace(metadata.Link)) {
            builder.AppendLine($"- Link: {metadata.Link}");
        }
        File.WriteAllText(Path.Combine(packageDirectory, "description.md"), builder.ToString(), new UTF8Encoding(false));
    }

    private static void WriteManifest(string sourceJarPath, JosmPluginMetadata metadata, string packageDirectory) {
        var iconFileName = Directory.EnumerateFiles(packageDirectory, "icon.*")
            .Select(Path.GetFileName)
            .First(fileName => fileName is not null);
        var manifest = $$"""
            {
              schemaVersion: 1,
              id: '{{CreatePluginId(sourceJarPath, metadata)}}',
              name: '{{EscapeJson5(GetDisplayName(sourceJarPath, metadata))}}',
              version: '{{CreateSemanticVersion(metadata.Version, sourceJarPath)}}',
              icon: '{{EscapeJson5(iconFileName!)}}',
              descriptionFile: 'description.md',
              kind: 'process',
              hooks: ['application.started', 'application.stopping'],
              runtime: {
                entry: '{{JavaSupportRuntimeLocator.BridgeExecutableRelativePath}}',
                arguments: ['--plugins', 'josm-plugins', '--josm-core', 'josm-core'],
                hostActions: ['showMessage'],
                timeoutMilliseconds: 10000,
                memoryLimitMegabytes: 1536,
              },
              contributions: {
                menus: [
                  { location: 'tools', label: 'JOSM: {{EscapeJson5(GetDisplayName(sourceJarPath, metadata))}}', command: '{{PluginCommandId}}' },
                ],
              },
            }
            """;
        File.WriteAllText(
            Path.Combine(packageDirectory, PluginManifestReader.ManifestFileName),
            manifest,
            new UTF8Encoding(false));
    }

    private static string CreatePluginId(string sourceJarPath, JosmPluginMetadata metadata) {
        var source = !string.IsNullOrWhiteSpace(metadata.PluginClass)
            ? metadata.PluginClass
            : Path.GetFileNameWithoutExtension(sourceJarPath);
        var normalized = PluginIdPartRegex()
            .Replace(source.ToLowerInvariant(), "-")
            .Trim('-', '.');
        normalized = normalized.Replace("..", ".", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized)) {
            normalized = "plugin";
        }

        const string prefix = "org.wosm.josm.";
        var maximumPartLength = 128 - prefix.Length;
        if (normalized.Length > maximumPartLength) {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant()[..12];
            normalized = normalized[..Math.Max(1, maximumPartLength - hash.Length - 1)].Trim('-', '.') + "-" + hash;
        }
        return prefix + normalized;
    }

    private static string CreateSemanticVersion(string pluginVersion, string sourceJarPath) {
        if (SemanticVersionRegex().IsMatch(pluginVersion)) {
            return pluginVersion;
        }
        if (int.TryParse(pluginVersion, out var numericVersion) && numericVersion >= 0) {
            return $"{numericVersion}.0.0";
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceJarPath))).ToLowerInvariant()[..12];
        return $"0.1.0+josm.{hash}";
    }

    private static string GetDisplayName(string sourceJarPath, JosmPluginMetadata metadata) {
        var fromClass = metadata.PluginClass.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (!string.IsNullOrWhiteSpace(fromClass)) {
            return fromClass.EndsWith("Plugin", StringComparison.Ordinal)
                ? fromClass[..^"Plugin".Length]
                : fromClass;
        }
        return Path.GetFileNameWithoutExtension(sourceJarPath);
    }

    private static string SanitizeFileName(string fileName) {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Select(character =>
            invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "plugin.jar" : sanitized;
    }

    private static string EscapeJson5(string value) {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
    }

    private sealed record JosmPluginMetadata(
        string PluginClass,
        string Description,
        string Version,
        string Date,
        string IconPath,
        string Link,
        string JosmMainVersion,
        string Author,
        bool CanLoadAtRuntime);

    [GeneratedRegex("[^a-z0-9.-]+", RegexOptions.CultureInvariant)]
    private static partial Regex PluginIdPartRegex();

    [GeneratedRegex("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionRegex();
}
