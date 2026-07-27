using System.IO;
using System.Text.Json;

namespace WPF_OpenStreetmap_Editor.Plugins;

internal interface IPluginTransport : IAsyncDisposable {
    string? EffectivePackageDirectory { get; }
    Task StartAsync(CancellationToken ct);
    Task<string> RequestAsync(string request, TimeSpan timeout, CancellationToken ct);
}

internal sealed class PluginRpcRuntime(
    PluginManifest manifest,
    IPluginTransport transport) : IPluginRuntime {
    public const int ProtocolVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly TimeSpan _timeout = TimeSpan.FromMilliseconds(
        manifest.Runtime?.TimeoutMilliseconds ?? 5000);
    private long _nextRequestId;
    private bool _started;

    public async Task InitializeAsync(PluginRuntimeContext context, CancellationToken ct) {
        await transport.StartAsync(ct).ConfigureAwait(false);
        _started = true;
        await InvokeAsync("initialize", new {
            protocolVersion = ProtocolVersion,
            host = new {
                name = context.HostName,
                version = context.HostVersion
            },
            plugin = new {
                id = manifest.Id,
                version = manifest.Version,
                packageDirectory = transport.EffectivePackageDirectory ?? context.PackageDirectory
            }
        }, ct).ConfigureAwait(false);
    }

    public Task<PluginInvocationResult> InvokeHookAsync(
        string hook,
        JsonElement payload,
        CancellationToken ct) {
        if (!manifest.Hooks.Contains(hook, StringComparer.Ordinal)) {
            return Task.FromResult(PluginInvocationResult.Empty);
        }

        return InvokeAsync("hook", new { name = hook, payload }, ct);
    }

    public Task<PluginInvocationResult> ExecuteCommandAsync(
        string commandId,
        JsonElement payload,
        CancellationToken ct) {
        return InvokeAsync("command.execute", new { command = commandId, payload }, ct);
    }

    public async ValueTask DisposeAsync() {
        if (_started) {
            try {
                await InvokeAsync("shutdown", new { }, CancellationToken.None).ConfigureAwait(false);
            } catch (Exception ex) {
                Services.Logger.Error($"Plugin '{manifest.Id}' failed to shut down cleanly", ex);
            }
        }

        await transport.DisposeAsync().ConfigureAwait(false);
        _started = false;
    }

    private async Task<PluginInvocationResult> InvokeAsync(string method, object parameters, CancellationToken ct) {
        var requestId = Interlocked.Increment(ref _nextRequestId);
        var request = JsonSerializer.Serialize(new {
            jsonrpc = "2.0",
            id = requestId,
            method,
            @params = parameters
        });
        var responseJson = await transport.RequestAsync(request, _timeout, ct).ConfigureAwait(false);

        JsonDocument response;
        try {
            response = JsonDocument.Parse(responseJson);
        } catch (JsonException ex) {
            var preview = responseJson.Length <= 160 ? responseJson : responseJson[..160] + "...";
            preview = Services.Logger.RedactSensitiveData(preview);
            throw InvalidResponse(
                $"the response is not valid JSON ({ex.Message}); first output: {JsonSerializer.Serialize(preview)}",
                ex);
        }

        using (response) {
            var root = response.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                throw InvalidResponse("the response must be a JSON object");
            }
            if (!root.TryGetProperty("jsonrpc", out var version) ||
                version.ValueKind != JsonValueKind.String ||
                version.GetString() != "2.0") {
                throw InvalidResponse("the jsonrpc property must be \"2.0\"");
            }
            if (!root.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.Number ||
                !id.TryGetInt64(out var responseId)) {
                throw InvalidResponse("the response id must be an integer");
            }
            if (responseId != requestId) {
                throw InvalidResponse($"response id {responseId} does not match request id {requestId}");
            }

            var hasResult = root.TryGetProperty("result", out var result);
            var hasError = root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null;
            if (hasResult == hasError) {
                throw InvalidResponse("the response must contain exactly one of result or error");
            }
            if (hasError) {
                ThrowRpcError(error);
            }
            if (result.ValueKind == JsonValueKind.Null) {
                return PluginInvocationResult.Empty;
            }
            if (result.ValueKind != JsonValueKind.Object) {
                throw InvalidResponse("the result must be an object or null");
            }

            try {
                var rpcResult = result.Deserialize<PluginRpcResult>(JsonOptions);
                if (rpcResult?.Actions is null) return PluginInvocationResult.Empty;
                if (rpcResult.Actions.Count > 512) {
                    throw InvalidResponse("the result contains too many host actions");
                }
                foreach (var action in rpcResult.Actions) {
                    if (action is null) {
                        throw InvalidResponse("host action arrays cannot contain null values");
                    }
                    try {
                        PluginManifestReader.ValidateHostAction(action);
                    } catch (PluginManifestException ex) {
                        throw InvalidResponse(ex.Message, ex);
                    }
                    if (manifest.Runtime is null ||
                        !manifest.Runtime.HostActions.Contains(action.Type, StringComparer.Ordinal)) {
                        throw InvalidResponse($"host action '{action.Type}' was not declared in runtime.hostActions");
                    }
                }
                return new PluginInvocationResult(rpcResult.Actions.Select(action => action!).ToList());
            } catch (JsonException ex) {
                throw InvalidResponse($"the result is malformed ({ex.Message})", ex);
            }
        }
    }

    private void ThrowRpcError(JsonElement error) {
        if (error.ValueKind != JsonValueKind.Object ||
            !error.TryGetProperty("code", out var codeValue) ||
            codeValue.ValueKind != JsonValueKind.Number ||
            !codeValue.TryGetInt32(out var code) ||
            !error.TryGetProperty("message", out var messageValue) ||
            messageValue.ValueKind != JsonValueKind.String) {
            throw InvalidResponse("the error object must contain an integer code and string message");
        }

        throw new InvalidOperationException(
            $"Plugin '{manifest.Id}' RPC error {code}: {messageValue.GetString()}");
    }

    private InvalidDataException InvalidResponse(string reason, Exception? innerException = null) {
        return new InvalidDataException(
            $"Plugin '{manifest.Id}' returned invalid JSON-RPC: {reason}.",
            innerException);
    }

    private sealed class PluginRpcResult {
        public List<PluginActionManifest?>? Actions { get; set; } = [];
    }
}
