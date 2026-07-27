using System.Text.Json;

namespace WPF_OpenStreetmap_Editor.Plugins;

public sealed record PluginRuntimeContext(
    string HostName,
    string HostVersion,
    string PackageDirectory,
    PluginManifest Manifest);

public sealed record PluginInvocationResult(IReadOnlyList<PluginActionManifest> Actions) {
    public static PluginInvocationResult Empty { get; } = new([]);
}

public sealed record PluginActionRequest(
    string PluginId,
    string PluginName,
    PluginActionManifest Action);

internal interface IPluginRuntime : IAsyncDisposable {
    Task InitializeAsync(PluginRuntimeContext context, CancellationToken ct);
    Task<PluginInvocationResult> InvokeHookAsync(string hook, JsonElement payload, CancellationToken ct);
    Task<PluginInvocationResult> ExecuteCommandAsync(string commandId, JsonElement payload, CancellationToken ct);
}

internal sealed class AddonPluginRuntime(PluginManifest manifest) : IPluginRuntime {
    public Task InitializeAsync(PluginRuntimeContext context, CancellationToken ct) => Task.CompletedTask;

    public Task<PluginInvocationResult> InvokeHookAsync(string hook, JsonElement payload, CancellationToken ct) {
        return Task.FromResult(PluginInvocationResult.Empty);
    }

    public Task<PluginInvocationResult> ExecuteCommandAsync(
        string commandId,
        JsonElement payload,
        CancellationToken ct) {
        var command = manifest.Contributions.Commands.FirstOrDefault(
            candidate => string.Equals(candidate.Id, commandId, StringComparison.Ordinal));
        return Task.FromResult(command is null
            ? PluginInvocationResult.Empty
            : new PluginInvocationResult(command.Actions));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
