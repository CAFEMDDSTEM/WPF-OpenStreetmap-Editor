using System.IO;
using System.Reflection;
using System.Text.Json;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Plugins;

public enum PluginLoadStatus {
    Loaded,
    Untrusted,
    Failed
}

public sealed class PluginDescriptor {
    public required string PackageDirectory { get; init; }
    public PluginManifest? Manifest { get; init; }
    public string IconPath { get; init; } = "";
    public string Description { get; init; } = "";
    public PluginLoadStatus Status { get; internal set; }
    public string Error { get; internal set; } = "";

    public string Id => Manifest?.Id ?? Path.GetFileName(PackageDirectory);
    public string Name => Manifest?.Name ?? Path.GetFileName(PackageDirectory);
    public string Version => Manifest?.Version ?? "";
    public string Kind => Manifest?.Kind ?? "unknown";
    public string StatusText => Status switch {
        PluginLoadStatus.Loaded => LocalizationService.Instance.GetString("Plugins.Status.Loaded"),
        PluginLoadStatus.Untrusted => LocalizationService.Instance.GetString("Plugins.Status.Untrusted"),
        _ => LocalizationService.Instance.Format("Plugins.Status.Failed", Error)
    };
}

public sealed record PluginMenuContribution(
    string PluginId,
    string PluginName,
    PluginMenuManifest Menu);

public sealed record PluginToolbarContribution(
    string PluginId,
    string PluginName,
    PluginToolbarManifest Toolbar);

public sealed class PluginHost : IAsyncDisposable {
    private readonly string _pluginsDirectory;
    private readonly PluginManifestReader _manifestReader;
    private readonly PluginTrustStore _trustStore;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private readonly List<LoadedPlugin> _loadedPlugins = [];
    private readonly List<PluginDescriptor> _descriptors = [];

    public PluginHost()
        : this(AppPaths.PluginsDirectory, AppPaths.PluginStateFile) {
    }

    public PluginHost(string pluginsDirectory, string statePath) {
        _pluginsDirectory = Path.GetFullPath(pluginsDirectory);
        _manifestReader = new PluginManifestReader();
        _trustStore = new PluginTrustStore(statePath);
        Installer = new PluginInstaller(_pluginsDirectory, _manifestReader, _trustStore);
    }

    public PluginInstaller Installer { get; }
    public IReadOnlyList<PluginDescriptor> Plugins => _descriptors;

    public IReadOnlyList<PluginMenuContribution> MenuContributions => _loadedPlugins
        .SelectMany(plugin => plugin.Descriptor.Manifest!.Contributions.Menus.Select(menu =>
            new PluginMenuContribution(
                plugin.Descriptor.Manifest.Id,
                plugin.Descriptor.Manifest.Name,
                menu)))
        .ToList();

    public IReadOnlyList<PluginToolbarContribution> ToolbarContributions => _loadedPlugins
        .SelectMany(plugin => plugin.Descriptor.Manifest!.Contributions.Toolbar.Select(toolbarItem =>
            new PluginToolbarContribution(
                plugin.Descriptor.Manifest.Id,
                plugin.Descriptor.Manifest.Name,
                toolbarItem)))
        .OrderBy(contribution => contribution.Toolbar.Order)
        .ThenBy(contribution => contribution.PluginId, StringComparer.Ordinal)
        .ToList();

    public async Task ReloadAsync(CancellationToken ct = default) {
        await _reloadLock.WaitAsync(ct).ConfigureAwait(false);
        try {
            await DisposeLoadedPluginsAsync().ConfigureAwait(false);
            _descriptors.Clear();
            Directory.CreateDirectory(_pluginsDirectory);

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var packageDirectory in Directory.EnumerateDirectories(_pluginsDirectory).Order(StringComparer.Ordinal)) {
                ct.ThrowIfCancellationRequested();
                var manifestPath = Path.Combine(packageDirectory, PluginManifestReader.ManifestFileName);
                if (!File.Exists(manifestPath)) continue;

                PluginDescriptor descriptor;
                try {
                    var manifest = _manifestReader.Read(manifestPath);
                    descriptor = new PluginDescriptor {
                        PackageDirectory = packageDirectory,
                        Manifest = manifest,
                        IconPath = PluginManifestReader.ResolveIconPath(manifest, packageDirectory),
                        Description = PluginManifestReader.ReadDescription(manifest, packageDirectory),
                        Status = PluginLoadStatus.Failed
                    };
                    _descriptors.Add(descriptor);

                    if (!seenIds.Add(manifest.Id)) {
                        throw new PluginManifestException($"Duplicate plugin id '{manifest.Id}'.");
                    }

                    var kind = PluginManifestReader.ParseKind(manifest.Kind);
                    if (kind == PluginKind.Native) {
                        var fingerprint = PluginPackageFingerprint.Compute(packageDirectory);
                        if (!_trustStore.IsTrusted(manifest.Id, fingerprint)) {
                            descriptor.Status = PluginLoadStatus.Untrusted;
                            continue;
                        }
                    }

                    var runtime = CreateRuntime(manifest, packageDirectory, kind);
                    try {
                        await runtime.InitializeAsync(CreateRuntimeContext(manifest, packageDirectory), ct)
                            .ConfigureAwait(false);
                        descriptor.Status = PluginLoadStatus.Loaded;
                        _loadedPlugins.Add(new LoadedPlugin(descriptor, runtime));
                    } catch {
                        await runtime.DisposeAsync().ConfigureAwait(false);
                        throw;
                    }
                } catch (Exception ex) {
                    descriptor = _descriptors.LastOrDefault(item => item.PackageDirectory == packageDirectory) ??
                        new PluginDescriptor {
                            PackageDirectory = packageDirectory,
                            Status = PluginLoadStatus.Failed
                        };
                    if (!_descriptors.Contains(descriptor)) {
                        _descriptors.Add(descriptor);
                    }
                    descriptor.Status = PluginLoadStatus.Failed;
                    descriptor.Error = ex.Message;
                    Logger.Error($"Failed to load plugin package '{packageDirectory}'", ex);
                }
            }
        } finally {
            _reloadLock.Release();
        }
    }

    public async Task TrustAndReloadAsync(string pluginId, CancellationToken ct = default) {
        var descriptor = _descriptors.FirstOrDefault(plugin => plugin.Id == pluginId) ??
            throw new InvalidOperationException($"Plugin '{pluginId}' was not found.");
        if (descriptor.Manifest is null) {
            throw new InvalidOperationException("An invalid plugin package cannot be trusted.");
        }

        var kind = PluginManifestReader.ParseKind(descriptor.Manifest.Kind);
        if (kind == PluginKind.Native) {
            _trustStore.Trust(pluginId, PluginPackageFingerprint.Compute(descriptor.PackageDirectory));
        }
        await ReloadAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PluginActionRequest>> PublishAsync(
        string hook,
        object? payload = null,
        CancellationToken ct = default) {
        var payloadElement = JsonSerializer.SerializeToElement(payload ?? new { });
        var actions = new List<PluginActionRequest>();
        foreach (var plugin in _loadedPlugins.ToList()) {
            try {
                var result = await plugin.Runtime.InvokeHookAsync(hook, payloadElement, ct).ConfigureAwait(false);
                actions.AddRange(result.Actions.Select(action => new PluginActionRequest(
                    plugin.Descriptor.Id,
                    plugin.Descriptor.Name,
                    action)));
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                throw;
            } catch (Exception ex) {
                Logger.Error($"Plugin '{plugin.Descriptor.Id}' failed hook '{hook}'", ex);
            }
        }
        return actions;
    }

    public async Task<PluginInvocationResult> ExecuteCommandAsync(
        string pluginId,
        string commandId,
        object? payload = null,
        CancellationToken ct = default) {
        var plugin = _loadedPlugins.FirstOrDefault(item => item.Descriptor.Id == pluginId);
        if (plugin is null) {
            var descriptor = _descriptors.FirstOrDefault(item => item.Id == pluginId);
            if (descriptor is { Status: PluginLoadStatus.Failed } &&
                !string.IsNullOrWhiteSpace(descriptor.Error)) {
                throw new InvalidOperationException(
                    $"Plugin '{pluginId}' failed to load: {descriptor.Error}");
            }
            throw new InvalidOperationException($"Plugin '{pluginId}' is not loaded.");
        }
        return await plugin.Runtime.ExecuteCommandAsync(
            commandId,
            JsonSerializer.SerializeToElement(payload ?? new { }),
            ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() {
        await _reloadLock.WaitAsync().ConfigureAwait(false);
        try {
            await DisposeLoadedPluginsAsync().ConfigureAwait(false);
        } finally {
            _reloadLock.Release();
            _reloadLock.Dispose();
        }
    }

    private static IPluginRuntime CreateRuntime(
        PluginManifest manifest,
        string packageDirectory,
        PluginKind kind) {
        if (kind == PluginKind.Addon) {
            return new AddonPluginRuntime(manifest);
        }

        var runtime = manifest.Runtime!;
        var entryPath = PluginManifestReader.ResolvePackagePath(packageDirectory, runtime.Entry);
        IPluginTransport transport = kind == PluginKind.Native
            ? new NativePluginTransport(entryPath, manifest.Id)
            : new ProcessPluginTransport(
                entryPath,
                runtime.Arguments,
                packageDirectory,
                manifest.Id,
                runtime.MemoryLimitMegabytes);
        return new PluginRpcRuntime(manifest, transport);
    }

    private static PluginRuntimeContext CreateRuntimeContext(PluginManifest manifest, string packageDirectory) {
        var assemblyName = Assembly.GetExecutingAssembly().GetName();
        return new PluginRuntimeContext(
            assemblyName.Name ?? "WPF-OpenStreetmap-Editor",
            assemblyName.Version?.ToString() ?? "0.0.0",
            packageDirectory,
            manifest);
    }

    private async Task DisposeLoadedPluginsAsync() {
        foreach (var plugin in _loadedPlugins.AsEnumerable().Reverse()) {
            try {
                await plugin.Runtime.DisposeAsync().ConfigureAwait(false);
            } catch (Exception ex) {
                Logger.Error($"Failed to dispose plugin '{plugin.Descriptor.Id}'", ex);
            }
        }
        _loadedPlugins.Clear();
    }

    private sealed record LoadedPlugin(PluginDescriptor Descriptor, IPluginRuntime Runtime);
}
