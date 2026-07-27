using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Windows.Media.Imaging;
using Json5Core;
using MahApps.Metro.IconPacks;

namespace WPF_OpenStreetmap_Editor.Plugins;

public sealed partial class PluginManifestReader {
    public const int CurrentSchemaVersion = 1;
    public const int NativeAbiVersion = 1;
    public const string ManifestFileName = "plugin.json5";
    private const long MaximumManifestBytes = 256 * 1024;
    private const long MaximumIconBytes = 2 * 1024 * 1024;
    private const long MaximumDescriptionBytes = 64 * 1024;
    private const int MaximumIconDimension = 4096;
    private const long MaximumIconPixels = 16L * 1024 * 1024;
    private const int MaximumIdentifierLength = 128;
    private const int MaximumDisplayTextLength = 256;
    private const int MaximumContributionCount = 512;
    private const int MaximumToolbarOrder = 10000;
    private static readonly HashSet<string> SupportedIconExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".png",
        ".ico",
        ".jpg",
        ".jpeg"
    };
    private static readonly HashSet<string> SupportedDescriptionExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".md",
        ".txt"
    };
    private static readonly HashSet<string> SupportedHooks = new(StringComparer.Ordinal) {
        PluginHooks.ApplicationStarted,
        PluginHooks.MainWindowLoaded,
        PluginHooks.ApplicationStopping
    };
    private static readonly HashSet<string> SupportedHostActions = new(StringComparer.Ordinal) {
        PluginActionTypes.ShowMessage,
        PluginActionTypes.OpenUrl,
        PluginActionTypes.AddImagery,
        PluginActionTypes.ManageOsmAccounts,
        PluginActionTypes.DownloadOsm,
        PluginActionTypes.UploadOsm
    };
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    public PluginManifest Read(string manifestPath) {
        var fullManifestPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullManifestPath)) {
            throw new PluginManifestException($"Plugin manifest does not exist: {fullManifestPath}");
        }
        var manifestLength = new FileInfo(fullManifestPath).Length;
        if (manifestLength <= 0 || manifestLength > MaximumManifestBytes) {
            throw new PluginManifestException(
                $"Plugin manifest must be between 1 byte and {MaximumManifestBytes / 1024} KB.");
        }

        PluginManifest? manifest;
        try {
            var json5Tree = Json5.Parse(File.ReadAllText(fullManifestPath));
            var standardJson = JsonSerializer.Serialize(json5Tree);
            manifest = JsonSerializer.Deserialize<PluginManifest>(standardJson, JsonOptions);
        } catch (Exception ex) when (ex is not PluginManifestException) {
            throw new PluginManifestException($"Invalid JSON5 manifest: {ex.Message}", ex);
        }

        if (manifest is null) {
            throw new PluginManifestException("Plugin manifest is empty.");
        }

        Validate(manifest, Path.GetDirectoryName(fullManifestPath)!);
        return manifest;
    }

    public static PluginKind ParseKind(string value) {
        return (value ?? "").Trim().ToLowerInvariant() switch {
            "native" => PluginKind.Native,
            "process" => PluginKind.Process,
            "addon" => PluginKind.Addon,
            _ => throw new PluginManifestException($"Unsupported plugin kind '{value}'.")
        };
    }

    public static string ResolvePackagePath(string packageDirectory, string relativePath) {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) {
            throw new PluginManifestException("Plugin file paths must be relative paths inside the package.");
        }

        var packageRoot = Path.GetFullPath(packageDirectory);
        var resolved = Path.GetFullPath(Path.Combine(packageRoot, relativePath));
        var relative = Path.GetRelativePath(packageRoot, resolved);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative)) {
            throw new PluginManifestException("Plugin file path escapes the package directory.");
        }

        return resolved;
    }

    public static string ResolveIconPath(PluginManifest manifest, string packageDirectory) {
        return ValidatePackageFile(
            packageDirectory,
            manifest.Icon,
            "icon",
            SupportedIconExtensions,
            MaximumIconBytes);
    }

    public static string ReadDescription(PluginManifest manifest, string packageDirectory) {
        var descriptionPath = ValidatePackageFile(
            packageDirectory,
            manifest.DescriptionFile,
            "description",
            SupportedDescriptionExtensions,
            MaximumDescriptionBytes);
        try {
            var description = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(descriptionPath)).Trim();
            if (string.IsNullOrWhiteSpace(description)) {
                throw new PluginManifestException("Plugin description file cannot be empty.");
            }
            return description;
        } catch (DecoderFallbackException ex) {
            throw new PluginManifestException("Plugin description file must contain valid UTF-8 text.", ex);
        }
    }

    private static void Validate(PluginManifest manifest, string packageDirectory) {
        NormalizeCollections(manifest);
        if (manifest.SchemaVersion != CurrentSchemaVersion) {
            throw new PluginManifestException(
                $"Unsupported plugin schema version {manifest.SchemaVersion}; expected {CurrentSchemaVersion}.");
        }

        if (!PluginIdRegex().IsMatch(manifest.Id)) {
            throw new PluginManifestException(
                "Plugin id must contain lowercase letters, digits, dots, or hyphens and include at least one separator.");
        }
        if (manifest.Id.Length > MaximumIdentifierLength) {
            throw new PluginManifestException($"Plugin id cannot exceed {MaximumIdentifierLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > MaximumDisplayTextLength) {
            throw new PluginManifestException("Plugin name is required.");
        }

        if (manifest.Version.Length > 64 || !SemanticVersionRegex().IsMatch(manifest.Version)) {
            throw new PluginManifestException("Plugin version must use semantic versioning.");
        }

        ValidateIcon(manifest, packageDirectory);
        _ = ReadDescription(manifest, packageDirectory);

        var kind = ParseKind(manifest.Kind);
        if (kind == PluginKind.Addon) {
            if (manifest.Runtime is not null && !string.IsNullOrWhiteSpace(manifest.Runtime.Entry)) {
                throw new PluginManifestException("Addon plugins cannot declare an executable entry.");
            }
            if (manifest.Hooks.Count > 0) {
                throw new PluginManifestException("Addon plugins cannot subscribe to runtime hooks.");
            }
        } else {
            ValidateRuntime(manifest.Runtime, kind, packageDirectory);
        }

        foreach (var hook in manifest.Hooks) {
            if (!SupportedHooks.Contains(hook)) {
                throw new PluginManifestException($"Unsupported hook '{hook}'.");
            }
        }

        var commandIds = new HashSet<string>(StringComparer.Ordinal);
        if (manifest.Contributions.Commands.Count > MaximumContributionCount ||
            manifest.Contributions.Menus.Count > MaximumContributionCount ||
            manifest.Contributions.Toolbar.Count > MaximumContributionCount) {
            throw new PluginManifestException(
                $"Plugins cannot declare more than {MaximumContributionCount} commands, menus, or toolbar items.");
        }
        foreach (var command in manifest.Contributions.Commands) {
            if (string.IsNullOrWhiteSpace(command.Id) ||
                command.Id.Length > MaximumIdentifierLength ||
                !commandIds.Add(command.Id)) {
                throw new PluginManifestException("Plugin command ids must be non-empty and unique.");
            }

            if (kind == PluginKind.Addon) {
                foreach (var action in command.Actions) {
                    ValidateHostAction(action);
                }
            }
        }

        foreach (var menu in manifest.Contributions.Menus) {
            if (!string.Equals(menu.Location, "tools", StringComparison.Ordinal)) {
                throw new PluginManifestException($"Unsupported menu location '{menu.Location}'.");
            }
            if (string.IsNullOrWhiteSpace(menu.Label) || string.IsNullOrWhiteSpace(menu.Command)) {
                throw new PluginManifestException("Menu contributions require both label and command.");
            }
            if (menu.Label.Length > MaximumDisplayTextLength ||
                menu.Command.Length > MaximumIdentifierLength) {
                throw new PluginManifestException("Menu labels or command ids exceed the supported length.");
            }
            if (kind == PluginKind.Addon && !commandIds.Contains(menu.Command)) {
                throw new PluginManifestException($"Addon menu references unknown command '{menu.Command}'.");
            }
        }

        foreach (var toolbarItem in manifest.Contributions.Toolbar) {
            if (!string.Equals(toolbarItem.Location, "main", StringComparison.Ordinal)) {
                throw new PluginManifestException(
                    $"Unsupported toolbar location '{toolbarItem.Location}'.");
            }
            if (string.IsNullOrWhiteSpace(toolbarItem.Icon) ||
                !ToolbarIconRegex().IsMatch(toolbarItem.Icon) ||
                !Enum.TryParse<PackIconLucideKind>(toolbarItem.Icon, ignoreCase: false, out _)) {
                throw new PluginManifestException(
                    $"Toolbar icon '{toolbarItem.Icon}' is not a supported Lucide icon identifier.");
            }
            if (string.IsNullOrWhiteSpace(toolbarItem.ToolTip) ||
                string.IsNullOrWhiteSpace(toolbarItem.Command)) {
                throw new PluginManifestException(
                    "Toolbar contributions require both tooltip and command.");
            }
            if (toolbarItem.ToolTip.Length > MaximumDisplayTextLength ||
                toolbarItem.Command.Length > MaximumIdentifierLength) {
                throw new PluginManifestException(
                    "Toolbar tooltips or command ids exceed the supported length.");
            }
            if (toolbarItem.Order is < -MaximumToolbarOrder or > MaximumToolbarOrder) {
                throw new PluginManifestException(
                    $"Toolbar order must be between {-MaximumToolbarOrder} and {MaximumToolbarOrder}.");
            }
            if (kind == PluginKind.Addon && !commandIds.Contains(toolbarItem.Command)) {
                throw new PluginManifestException(
                    $"Addon toolbar item references unknown command '{toolbarItem.Command}'.");
            }
        }
    }

    internal static void ValidateHostAction(PluginActionManifest action) {
        if (!SupportedHostActions.Contains(action.Type)) {
            throw new PluginManifestException($"Unsupported host action '{action.Type}'.");
        }
        if (action.Arguments.ValueKind != JsonValueKind.Object) {
            throw new PluginManifestException($"Host action '{action.Type}' requires an arguments object.");
        }

        var requiredName = action.Type switch {
            PluginActionTypes.ShowMessage => "message",
            PluginActionTypes.OpenUrl or PluginActionTypes.AddImagery => "url",
            _ => null
        };
        if (requiredName is null) return;

        if (!action.Arguments.TryGetProperty(requiredName, out var requiredValue) ||
            requiredValue.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(requiredValue.GetString())) {
            throw new PluginManifestException(
                $"Host action '{action.Type}' requires a non-empty string '{requiredName}'.");
        }

        if (action.Type == PluginActionTypes.OpenUrl &&
            (!Uri.TryCreate(requiredValue.GetString(), UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https"))) {
            throw new PluginManifestException("Addon openUrl actions require an absolute HTTP or HTTPS URL.");
        }
    }

    private static void NormalizeCollections(PluginManifest manifest) {
        manifest.Id ??= "";
        manifest.Name ??= "";
        manifest.Version ??= "";
        manifest.Icon ??= "";
        manifest.DescriptionFile ??= "";
        manifest.Kind ??= "";
        manifest.Hooks ??= [];
        manifest.Contributions ??= new PluginContributionsManifest();
        manifest.Contributions.Menus ??= [];
        manifest.Contributions.Toolbar ??= [];
        manifest.Contributions.Commands ??= [];
        if (manifest.Contributions.Menus.Any(static menu => menu is null) ||
            manifest.Contributions.Toolbar.Any(static toolbarItem => toolbarItem is null) ||
            manifest.Contributions.Commands.Any(static command => command is null)) {
            throw new PluginManifestException("Plugin contribution arrays cannot contain null values.");
        }
        if (manifest.Runtime is not null) {
            manifest.Runtime.Entry ??= "";
            manifest.Runtime.Arguments ??= [];
            manifest.Runtime.HostActions ??= [];
            if (manifest.Runtime.Arguments.Any(static argument => argument is null)) {
                throw new PluginManifestException("Plugin runtime arguments cannot contain null values.");
            }
            if (manifest.Runtime.HostActions.Any(static action => action is null)) {
                throw new PluginManifestException("Plugin host action arrays cannot contain null values.");
            }
        }
        foreach (var menu in manifest.Contributions.Menus) {
            menu.Location ??= "";
            menu.Label ??= "";
            menu.Command ??= "";
        }
        foreach (var toolbarItem in manifest.Contributions.Toolbar) {
            toolbarItem.Location ??= "";
            toolbarItem.Icon ??= "";
            toolbarItem.ToolTip ??= "";
            toolbarItem.Command ??= "";
        }
        foreach (var command in manifest.Contributions.Commands) {
            command.Id ??= "";
            command.Actions ??= [];
            if (command.Actions.Count > MaximumContributionCount) {
                throw new PluginManifestException(
                    $"Plugin commands cannot declare more than {MaximumContributionCount} actions.");
            }
            if (command.Actions.Any(static action => action is null)) {
                throw new PluginManifestException("Plugin action arrays cannot contain null values.");
            }
            foreach (var action in command.Actions) {
                action.Type ??= "";
            }
        }
    }

    private static void ValidateRuntime(
        PluginRuntimeManifest? runtime,
        PluginKind kind,
        string packageDirectory) {
        if (runtime is null || string.IsNullOrWhiteSpace(runtime.Entry)) {
            throw new PluginManifestException($"{kind} plugins require runtime.entry.");
        }

        runtime.TimeoutMilliseconds = Math.Clamp(runtime.TimeoutMilliseconds, 250, 30000);
        runtime.MemoryLimitMegabytes = Math.Clamp(runtime.MemoryLimitMegabytes, 128, 4096);
        if (runtime.Entry.Length > 512 || runtime.Arguments.Count > 128 ||
            runtime.Arguments.Any(static argument => argument.Length > 4096) ||
            runtime.HostActions.Count > MaximumContributionCount) {
            throw new PluginManifestException("Plugin runtime entry or arguments exceed the supported limits.");
        }
        var hostActions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in runtime.HostActions) {
            if (!SupportedHostActions.Contains(action)) {
                throw new PluginManifestException($"Unsupported host action permission '{action}'.");
            }
            if (!hostActions.Add(action)) {
                throw new PluginManifestException($"Duplicate host action permission '{action}'.");
            }
        }
        var entryPath = ResolvePackagePath(packageDirectory, runtime.Entry);
        if (!File.Exists(entryPath)) {
            throw new PluginManifestException($"Plugin entry does not exist: {runtime.Entry}");
        }

        var expectedExtension = kind == PluginKind.Native ? ".dll" : ".exe";
        if (!string.Equals(Path.GetExtension(entryPath), expectedExtension, StringComparison.OrdinalIgnoreCase)) {
            throw new PluginManifestException($"{kind} plugin entry must be a {expectedExtension} file.");
        }
    }

    private static void ValidateIcon(PluginManifest manifest, string packageDirectory) {
        var iconPath = ResolveIconPath(manifest, packageDirectory);
        try {
            using var stream = new FileStream(iconPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.DelayCreation | BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.None);
            if (decoder.Frames.Count == 0 || decoder.Frames.Count > 64) {
                throw new PluginManifestException("Plugin icon must contain between 1 and 64 bitmap frames.");
            }
            foreach (var frame in decoder.Frames) {
                if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 ||
                    frame.PixelWidth > MaximumIconDimension || frame.PixelHeight > MaximumIconDimension ||
                    (long)frame.PixelWidth * frame.PixelHeight > MaximumIconPixels) {
                    throw new PluginManifestException(
                        $"Plugin icon dimensions cannot exceed {MaximumIconDimension} pixels or " +
                        $"{MaximumIconPixels:N0} total pixels.");
                }
            }
        } catch (PluginManifestException) {
            throw;
        } catch (Exception ex) {
            throw new PluginManifestException("Plugin icon is not a valid supported bitmap.", ex);
        }
    }

    private static string ValidatePackageFile(
        string packageDirectory,
        string relativePath,
        string fileKind,
        IReadOnlySet<string> supportedExtensions,
        long maximumBytes) {
        if (relativePath.Length > 512) {
            throw new PluginManifestException($"Plugin {fileKind} path exceeds the supported length.");
        }

        var path = ResolvePackagePath(packageDirectory, relativePath);
        if (!supportedExtensions.Contains(Path.GetExtension(path))) {
            throw new PluginManifestException(
                $"Plugin {fileKind} must use one of: {string.Join(", ", supportedExtensions)}.");
        }
        if (!File.Exists(path)) {
            throw new PluginManifestException($"Plugin {fileKind} file does not exist: {relativePath}");
        }

        EnsurePathHasNoReparsePoints(packageDirectory, path);
        var length = new FileInfo(path).Length;
        if (length <= 0 || length > maximumBytes) {
            throw new PluginManifestException(
                $"Plugin {fileKind} file must be between 1 byte and {maximumBytes / 1024} KB.");
        }
        return path;
    }

    private static void EnsurePathHasNoReparsePoints(string packageDirectory, string filePath) {
        var currentPath = Path.GetFullPath(packageDirectory);
        if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0) {
            throw new PluginManifestException("Plugin package paths cannot contain symbolic links or reparse points.");
        }

        foreach (var segment in Path.GetRelativePath(currentPath, filePath)
                     .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries)) {
            currentPath = Path.Combine(currentPath, segment);
            if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0) {
                throw new PluginManifestException(
                    "Plugin package paths cannot contain symbolic links or reparse points.");
            }
        }
    }

    [GeneratedRegex("^[a-z0-9]+(?:[.-][a-z0-9]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex PluginIdRegex();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ToolbarIconRegex();

    [GeneratedRegex("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionRegex();
}

public sealed class PluginManifestException : Exception {
    public PluginManifestException(string message) : base(message) {
    }

    public PluginManifestException(string message, Exception innerException) : base(message, innerException) {
    }
}
