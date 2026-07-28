using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace WPF_OpenStreetmap_Editor.Plugins;

internal sealed class PythonScriptPluginTransport(
    string scriptPath,
    IReadOnlyList<string> arguments,
    string packageDirectory,
    string pluginId) : IPluginTransport {
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private Process? _process;
    private Task? _standardErrorTask;

    public string? EffectivePackageDirectory => packageDirectory;

    public Task StartAsync(CancellationToken ct) {
        if (_process is not null) return Task.CompletedTask;
        ct.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo {
            FileName = "python",
            WorkingDirectory = packageDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-u");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments) {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

        try {
            _process = Process.Start(startInfo) ??
                throw new InvalidOperationException("Python process did not start.");
            _standardErrorTask = DrainStandardErrorAsync(_process);
            return Task.CompletedTask;
        } catch (Win32Exception ex) {
            throw new InvalidOperationException(
                "Python process plugins require Python on PATH. Install Python 3.11+ or make python.exe discoverable.",
                ex);
        } catch {
            _process?.Dispose();
            _process = null;
            throw;
        }
    }

    public async Task<string> RequestAsync(string request, TimeSpan timeout, CancellationToken ct) {
        await _requestLock.WaitAsync(ct).ConfigureAwait(false);
        try {
            var process = _process ?? throw new InvalidOperationException("Python plugin process is not running.");
            if (process.HasExited) {
                throw new EndOfStreamException(
                    $"Python plugin process '{pluginId}' exited with code {process.ExitCode}.");
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            await process.StandardInput.WriteLineAsync(request.AsMemory(), timeoutCts.Token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(timeoutCts.Token).ConfigureAwait(false);

            string? firstNonProtocolOutput = null;
            for (var lineCount = 0; lineCount < 32; lineCount++) {
                var response = await process.StandardOutput.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
                if (response is null) {
                    if (firstNonProtocolOutput is not null) {
                        throw new InvalidDataException(
                            $"Python plugin process '{pluginId}' closed before returning JSON-RPC; " +
                            $"first non-protocol output: {FormatOutputPreview(firstNonProtocolOutput)}.");
                    }
                    throw new EndOfStreamException($"Python plugin process '{pluginId}' closed its RPC output.");
                }
                if (IsJsonRpcResponse(response)) return response;
                if (!string.IsNullOrWhiteSpace(response)) {
                    firstNonProtocolOutput ??= response;
                    Services.Logger.Error($"Python plugin '{pluginId}' stdout: {response}");
                }
            }

            var detail = firstNonProtocolOutput is null
                ? "too many blank output lines"
                : $"too many non-protocol output lines; first output: {FormatOutputPreview(firstNonProtocolOutput)}";
            throw new InvalidDataException($"Python plugin process '{pluginId}' produced {detail}.");
        } catch (OperationCanceledException) {
            TerminateProcess();
            if (!ct.IsCancellationRequested) {
                throw new TimeoutException(
                    $"Python plugin process '{pluginId}' did not respond in {timeout.TotalMilliseconds:0} ms.");
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
        var process = _process;
        _process = null;
        if (process is null) return;

        try {
            process.StandardInput.Close();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            if (!process.HasExited) {
                process.Kill(entireProcessTree: true);
            }
        } finally {
            if (_standardErrorTask is not null) {
                try {
                    await _standardErrorTask.ConfigureAwait(false);
                } catch (Exception ex) {
                    Services.Logger.Error($"Python plugin '{pluginId}' stderr reader failed", ex);
                }
            }
            _standardErrorTask = null;
            process.Dispose();
            _requestLock.Dispose();
        }
    }

    private async Task DrainStandardErrorAsync(Process process) {
        try {
            while (!process.HasExited) {
                var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                if (line is null) return;
                if (!string.IsNullOrWhiteSpace(line)) {
                    Services.Logger.Error($"Python plugin '{pluginId}' stderr: {line}");
                }
            }
        } catch (Exception ex) {
            Services.Logger.Error($"Python plugin '{pluginId}' produced invalid stderr output", ex);
            TerminateProcess();
        }
    }

    private void TerminateProcess() {
        var process = _process;
        if (process is not null && !process.HasExited) {
            process.Kill(entireProcessTree: true);
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
}
