using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WPF_OpenStreetmap_Editor.Plugins;

internal sealed class NativePluginTransport(string entryPath, string pluginId) : IPluginTransport {
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private nint _library;
    private NativeInvoke? _invoke;
    private NativeFree? _free;
    private Task<string>? _activeInvocation;
    private bool _canUnload = true;

    public string? EffectivePackageDirectory => null;

    public Task StartAsync(CancellationToken ct) {
        if (_library != 0) return Task.CompletedTask;

        try {
            _library = NativeLibrary.Load(entryPath);
            var getAbiVersion = LoadExport<NativeGetAbiVersion>("wosm_plugin_abi_version");
            var abiVersion = getAbiVersion();
            if (abiVersion != PluginManifestReader.NativeAbiVersion) {
                throw new InvalidOperationException(
                    $"Plugin '{pluginId}' uses native ABI {abiVersion}; expected {PluginManifestReader.NativeAbiVersion}.");
            }

            _invoke = LoadExport<NativeInvoke>("wosm_plugin_invoke");
            _free = LoadExport<NativeFree>("wosm_plugin_free");
            return Task.CompletedTask;
        } catch {
            if (_library != 0) {
                NativeLibrary.Free(_library);
                _library = 0;
            }
            throw;
        }
    }

    public async Task<string> RequestAsync(string request, TimeSpan timeout, CancellationToken ct) {
        await _requestLock.WaitAsync(ct).ConfigureAwait(false);
        try {
            if (!_canUnload) {
                throw new InvalidOperationException(
                    $"Native plugin '{pluginId}' is unavailable after a timed-out in-process call.");
            }

            _activeInvocation = Task.Run(() => Invoke(request), CancellationToken.None);
            try {
                return await _activeInvocation.WaitAsync(timeout, ct).ConfigureAwait(false);
            } catch (TimeoutException) {
                _canUnload = false;
                throw new TimeoutException(
                    $"Native plugin '{pluginId}' did not respond in {timeout.TotalMilliseconds:0} ms. " +
                    "In-process native calls cannot be terminated safely.");
            } catch (OperationCanceledException) {
                if (!_activeInvocation.IsCompleted) {
                    _canUnload = false;
                }
                throw;
            } finally {
                if (_activeInvocation.IsCompleted) {
                    _activeInvocation = null;
                }
            }
        } finally {
            _requestLock.Release();
        }
    }

    public ValueTask DisposeAsync() {
        _invoke = null;
        _free = null;
        if (_library != 0 && _canUnload) {
            NativeLibrary.Free(_library);
            _library = 0;
        }
        // A timed-out native call may still be executing inside this DLL.
        // Keeping it loaded until process exit avoids invalidating its instruction pointers.
        _requestLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private string Invoke(string request) {
        var invoke = _invoke ?? throw new InvalidOperationException("Native plugin is not loaded.");
        var free = _free ?? throw new InvalidOperationException("Native plugin is not loaded.");
        var requestBytes = Encoding.UTF8.GetBytes(request);
        var requestPointer = Marshal.AllocHGlobal(requestBytes.Length);
        nint responsePointer = 0;
        try {
            Marshal.Copy(requestBytes, 0, requestPointer, requestBytes.Length);
            responsePointer = invoke(requestPointer, requestBytes.Length);
            if (responsePointer == 0) {
                throw new InvalidDataException($"Native plugin '{pluginId}' returned a null response.");
            }
            return Marshal.PtrToStringUTF8(responsePointer) ??
                throw new InvalidDataException($"Native plugin '{pluginId}' returned invalid UTF-8.");
        } finally {
            if (responsePointer != 0) {
                free(responsePointer);
            }
            Marshal.FreeHGlobal(requestPointer);
        }
    }

    private T LoadExport<T>(string name) where T : Delegate {
        var address = NativeLibrary.GetExport(_library, name);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeGetAbiVersion();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint NativeInvoke(nint requestUtf8, int requestLength);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NativeFree(nint responseUtf8);
}
