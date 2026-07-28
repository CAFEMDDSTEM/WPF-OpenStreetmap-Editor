using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace WPF_OpenStreetmap_Editor.Plugins;

internal sealed class ProcessPluginTransport(
    string entryPath,
    IReadOnlyList<string> arguments,
    string packageDirectory,
    string pluginId,
    int memoryLimitMegabytes,
    bool usePythonInterpreter = false) : IPluginTransport {
    private const int MaximumRpcLineBytes = 1024 * 1024;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private SandboxedPluginProcess? _sandbox;
    private BoundedUtf8LineReader? _standardOutput;
    private Task? _standardErrorTask;

    public string? EffectivePackageDirectory => _sandbox?.PackageDirectory;
    internal bool UsesPythonInterpreter => usePythonInterpreter;

    public Task StartAsync(CancellationToken ct) {
        if (_sandbox is not null) return Task.CompletedTask;
        ct.ThrowIfCancellationRequested();

        try {
            var interpreterPath = usePythonInterpreter
                ? PythonInterpreterLocator.Find()
                : null;
            _sandbox = WindowsPluginSandbox.Start(
                entryPath,
                arguments,
                packageDirectory,
                pluginId,
                memoryLimitMegabytes,
                interpreterPath);
            _standardOutput = new BoundedUtf8LineReader(_sandbox.StandardOutput, MaximumRpcLineBytes);
            _standardErrorTask = DrainStandardErrorAsync(_sandbox.StandardError, _sandbox.Process);
            return Task.CompletedTask;
        } catch {
            _sandbox?.Dispose();
            _sandbox = null;
            throw;
        }
    }

    public async Task<string> RequestAsync(string request, TimeSpan timeout, CancellationToken ct) {
        await _requestLock.WaitAsync(ct).ConfigureAwait(false);
        try {
            var sandbox = _sandbox ?? throw new InvalidOperationException("Plugin process is not running.");
            var standardOutput = _standardOutput ??
                throw new InvalidOperationException("Plugin RPC output is not available.");
            if (sandbox.Process.HasExited) {
                throw new EndOfStreamException(
                    $"Plugin process '{pluginId}' exited with code {sandbox.Process.ExitCode}.");
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            await sandbox.StandardInput.WriteLineAsync(request.AsMemory(), timeoutCts.Token).ConfigureAwait(false);
            await sandbox.StandardInput.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
            string? firstNonProtocolOutput = null;
            for (var lineCount = 0; lineCount < 32; lineCount++) {
                var response = await standardOutput.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
                if (response is null) {
                    if (firstNonProtocolOutput is not null) {
                        throw new InvalidDataException(
                            $"Plugin process '{pluginId}' closed its RPC output before returning JSON-RPC; " +
                            $"first non-protocol output: {FormatOutputPreview(firstNonProtocolOutput)}.");
                    }
                    throw new EndOfStreamException($"Plugin process '{pluginId}' closed its RPC output.");
                }
                if (IsJsonRpcResponse(response)) return response;
                if (!string.IsNullOrWhiteSpace(response)) {
                    firstNonProtocolOutput ??= response;
                    Services.Logger.Error($"Plugin '{pluginId}' stdout: {response}");
                }
            }
            var detail = firstNonProtocolOutput is null
                ? "too many blank output lines"
                : $"too many non-protocol output lines; first output: {FormatOutputPreview(firstNonProtocolOutput)}";
            throw new InvalidDataException($"Plugin process '{pluginId}' produced {detail}.");
        } catch (OperationCanceledException) {
            TerminateProcess();
            if (!ct.IsCancellationRequested) {
                throw new TimeoutException(
                    $"Plugin process '{pluginId}' did not respond in {timeout.TotalMilliseconds:0} ms.");
            }
            throw;
        } catch {
            TerminateProcess();
            throw;
        } finally {
            _requestLock.Release();
        }
    }

    public async ValueTask DisposeAsync() {
        var sandbox = _sandbox;
        _sandbox = null;
        if (sandbox is null) return;

        try {
            sandbox.StandardInput.Close();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await sandbox.Process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            if (!sandbox.Process.HasExited) {
                sandbox.Process.Kill(entireProcessTree: true);
            }
        } finally {
            if (_standardErrorTask is not null) {
                try {
                    await _standardErrorTask.ConfigureAwait(false);
                } catch (Exception ex) {
                    Services.Logger.Error($"Plugin '{pluginId}' stderr reader failed", ex);
                }
            }
            _standardErrorTask = null;
            _standardOutput = null;
            sandbox.Dispose();
            _requestLock.Dispose();
        }
    }

    private static bool IsJsonRpcResponse(string value) {
        try {
            using var json = JsonDocument.Parse(value);
            return json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("jsonrpc", out var version) &&
                version.ValueKind == JsonValueKind.String &&
                version.GetString() == "2.0";
        } catch (JsonException) {
            return false;
        }
    }

    private static string FormatOutputPreview(string value) {
        var preview = value.Length <= 160 ? value : value[..160] + "...";
        return JsonSerializer.Serialize(Services.Logger.RedactSensitiveData(preview));
    }

    private async Task DrainStandardErrorAsync(Stream stream, Process process) {
        var reader = new BoundedUtf8LineReader(stream, MaximumRpcLineBytes);
        try {
            while (!process.HasExited) {
                var line = await reader.ReadLineAsync(CancellationToken.None).ConfigureAwait(false);
                if (line is null) return;
                if (!string.IsNullOrWhiteSpace(line)) {
                    Services.Logger.Error($"Plugin '{pluginId}' stderr: {line}");
                }
            }
        } catch (Exception ex) {
            Services.Logger.Error($"Plugin '{pluginId}' produced invalid stderr output", ex);
            TerminateProcess();
        }
    }

    private void TerminateProcess() {
        var process = _sandbox?.Process;
        if (process is not null && !process.HasExited) {
            process.Kill(entireProcessTree: true);
        }
    }

    internal sealed class BoundedUtf8LineReader(Stream stream, int maximumLineBytes) {
        private static readonly UTF8Encoding Utf8 = new(false, true);
        private readonly byte[] _readBuffer = new byte[4096];
        private readonly byte[] _lineBuffer = new byte[maximumLineBytes];
        private int _readOffset;
        private int _readCount;
        private int _lineCount;

        public async Task<string?> ReadLineAsync(CancellationToken ct) {
            while (true) {
                if (_readOffset >= _readCount) {
                    _readCount = await stream.ReadAsync(_readBuffer, ct).ConfigureAwait(false);
                    _readOffset = 0;
                    if (_readCount == 0) {
                        if (_lineCount == 0) return null;
                        return DecodeLine();
                    }
                }

                var remaining = _readBuffer.AsSpan(_readOffset, _readCount - _readOffset);
                var newlineOffset = remaining.IndexOf((byte)'\n');
                var segmentLength = newlineOffset >= 0 ? newlineOffset : remaining.Length;
                if (_lineCount + segmentLength > _lineBuffer.Length) {
                    throw new InvalidDataException(
                        $"Plugin RPC lines cannot exceed {_lineBuffer.Length / 1024} KB.");
                }
                remaining[..segmentLength].CopyTo(_lineBuffer.AsSpan(_lineCount));
                _lineCount += segmentLength;
                _readOffset += segmentLength;
                if (newlineOffset < 0) continue;

                _readOffset++;
                return DecodeLine();
            }
        }

        private string DecodeLine() {
            var length = _lineCount;
            if (length > 0 && _lineBuffer[length - 1] == (byte)'\r') length--;
            try {
                return Utf8.GetString(_lineBuffer, 0, length);
            } catch (DecoderFallbackException ex) {
                throw new InvalidDataException("Plugin RPC output must be valid UTF-8.", ex);
            } finally {
                _lineCount = 0;
            }
        }
    }
}
