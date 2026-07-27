using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WPF_OpenStreetmap_Editor.Services;

public enum StartupCheckState {
    Pending,
    Running,
    Passed,
    Warning,
    Failed,
    Skipped
}

public sealed record StartupProgressUpdate(
    string StepId,
    string Title,
    string Detail,
    StartupCheckState State,
    double Progress);

public sealed record StartupCheckResult(
    string StepId,
    string Title,
    string Detail,
    StartupCheckState State);

public sealed class StartupDiagnosticsService : IDisposable {
    private const int TileProbeZoom = 1;
    private const int MaxConcurrentTileProbes = 4;
    private const long MinimumAvailableMemoryBytes = 512L * 1024 * 1024;
    private const double MinimumAvailableMemoryRatio = 0.08;
    private const long MinimumDiskFreeBytes = 512L * 1024 * 1024;
    private const double MinimumDiskFreeRatio = 0.05;
    private static readonly TimeSpan TileProbeTimeout = TimeSpan.FromSeconds(4);
    private readonly AppSettings _settings;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public AppUpdateCheckResult? LastUpdateCheckResult { get; private set; }

    public StartupDiagnosticsService(AppSettings? settings = null, HttpClient? http = null) {
        _settings = settings ?? AppSettingsService.Load();
        AppSettingsService.EnsureDefaults(_settings);

        _ownsHttpClient = http is null;
        _http = http ?? new HttpClient {
            Timeout = Timeout.InfiniteTimeSpan
        };
        if (!_http.DefaultRequestHeaders.UserAgent.Any()) {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("WPF-OpenStreetmap-Editor/0.1");
        }
    }

    public async Task<IReadOnlyList<StartupCheckResult>> RunAsync(
        IProgress<StartupProgressUpdate>? progress = null,
        CancellationToken ct = default) {
        Logger.Startup("启动诊断开始");
        List<StartupCheckResult> results = [];

        results.Add(RunLocalStep(
            progress,
            "paths",
            "初始化应用目录",
            "准备缓存、设置与日志目录",
            0.04,
            0.14,
            EnsureAppDirectories));

        results.Add(RunLocalStep(
            progress,
            "hardware",
            "检查硬件",
            "读取处理器、系统与进程架构",
            0.16,
            0.28,
            CheckHardware));

        results.Add(RunLocalStep(
            progress,
            "memory",
            "检查内存",
            "读取物理内存并报告内存压力",
            0.30,
            0.43,
            CheckMemory));

        results.Add(RunLocalStep(
            progress,
            "disk",
            "检查硬盘",
            "确认应用所在磁盘可用空间",
            0.45,
            0.56,
            CheckDisk));

        results.AddRange(await ProbeTileSourcesAsync(progress, ct).ConfigureAwait(false));
        results.Add(await CheckForUpdatesAsync(progress, ct).ConfigureAwait(false));

        Report(progress, "ready", "准备主界面", "启动检查完成", StartupCheckState.Passed, 1.0);
        Logger.Startup("启动诊断完成");
        return results;
    }

    public async Task<StartupCheckResult> ProbeTileSourceAsync(TileSourcePreset source, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(source.Source)) {
            return new StartupCheckResult(GetTileStepId(source), source.Name, "URL 为空，已跳过", StartupCheckState.Skipped);
        }

        if (RequiresAccessToken(source.Source) && string.IsNullOrWhiteSpace(source.AccessToken)) {
            return new StartupCheckResult(GetTileStepId(source), source.Name, "需要访问令牌，已跳过连通性测试", StartupCheckState.Skipped);
        }

        try {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TileProbeTimeout);
            using var tileService = new TileService(_http);
            tileService.ParseUrlTemplate(source.Source, null);
            tileService.ApplySourceOptions(source.MapMaxZoom, source.ImageMaxZoom, source.NoTileEtags, source.NoTileMd5s);
            await tileService.InitializeSourceAsync(source.AccessToken, timeoutCts.Token).ConfigureAwait(false);

            var zoom = Math.Clamp(TileProbeZoom, GeoConverter.MinZoom, Math.Max(GeoConverter.MinZoom, tileService.ImageMaxZoom));
            var tileCount = GeoConverter.GetTileCount(zoom);
            var tileX = tileCount / 2;
            var tileY = tileCount / 2;
            var url = tileService.BuildTileUrl(zoom, tileX, tileY, source.AccessToken);
            if (string.IsNullOrWhiteSpace(url)) {
                return new StartupCheckResult(GetTileStepId(source), source.Name, "无法生成探测瓦片 URL", StartupCheckState.Warning);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);

            var host = GetHost(url);
            if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 400) {
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (!string.IsNullOrEmpty(mediaType) &&
                    !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) {
                    return new StartupCheckResult(
                        GetTileStepId(source),
                        source.Name,
                        $"{host} 返回 {mediaType}，可能不是瓦片图像",
                        StartupCheckState.Warning);
                }

                return new StartupCheckResult(
                    GetTileStepId(source),
                    source.Name,
                    $"{host} HTTP {(int)response.StatusCode}",
                    StartupCheckState.Passed);
            }

            return new StartupCheckResult(
                GetTileStepId(source),
                source.Name,
                $"{host} HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                StartupCheckState.Warning);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            return new StartupCheckResult(GetTileStepId(source), source.Name, "连接超时", StartupCheckState.Warning);
        } catch (Exception unsafeException) {
            var ex = new InvalidOperationException(Logger.RedactSensitiveData(unsafeException.Message));
            return new StartupCheckResult(
                GetTileStepId(source),
                source.Name,
                $"无法连接：{ex.Message}",
                StartupCheckState.Warning);
        }
    }

    public static bool IsMemoryLow(ulong totalBytes, ulong availableBytes) {
        if (totalBytes == 0) return false;

        return availableBytes < (ulong)MinimumAvailableMemoryBytes ||
            (double)availableBytes / totalBytes < MinimumAvailableMemoryRatio;
    }

    public static bool IsDiskLow(long totalBytes, long availableBytes) {
        if (totalBytes <= 0) return false;

        return availableBytes < MinimumDiskFreeBytes ||
            (double)availableBytes / totalBytes < MinimumDiskFreeRatio;
    }

    public void Dispose() {
        if (_disposed) return;

        _disposed = true;
        if (_ownsHttpClient) {
            _http.Dispose();
        }
    }

    private async Task<IReadOnlyList<StartupCheckResult>> ProbeTileSourcesAsync(
        IProgress<StartupProgressUpdate>? progress,
        CancellationToken ct) {
        var sources = _settings.TileSources.ToList();
        if (sources.Count == 0) {
            var skipped = new StartupCheckResult("tiles", "测试瓦片层网络", "没有配置瓦片层", StartupCheckState.Skipped);
            Report(progress, skipped.StepId, skipped.Title, skipped.Detail, skipped.State, 0.94);
            return [skipped];
        }

        Report(progress, "tiles", "测试瓦片层网络", $"准备测试 {sources.Count} 个瓦片层", StartupCheckState.Running, 0.58);

        var results = new StartupCheckResult?[sources.Count];
        var completed = 0;
        using var semaphore = new SemaphoreSlim(MaxConcurrentTileProbes, MaxConcurrentTileProbes);
        var tasks = sources.Select(async (source, index) => {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try {
                Report(progress, GetTileStepId(source), source.Name, "正在测试连通性", StartupCheckState.Running, 0.60);
                var result = await ProbeTileSourceAsync(source, ct).ConfigureAwait(false);
                results[index] = result;
                var done = Interlocked.Increment(ref completed);
                var stepProgress = 0.60 + done / (double)sources.Count * 0.32;
                Report(progress, result.StepId, result.Title, result.Detail, result.State, stepProgress);
            } finally {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var completedResults = results.OfType<StartupCheckResult>().ToList();
        var warningCount = completedResults.Count(result => result.State is StartupCheckState.Warning or StartupCheckState.Failed);
        var skippedCount = completedResults.Count(result => result.State == StartupCheckState.Skipped);
        var aggregateState = warningCount == 0 ? StartupCheckState.Passed : StartupCheckState.Warning;
        var detail = warningCount == 0
            ? $"完成 {completedResults.Count} 个瓦片层测试"
            : $"完成 {completedResults.Count} 个瓦片层测试，{warningCount} 个警告，{skippedCount} 个跳过";
        var aggregate = new StartupCheckResult("tiles", "测试瓦片层网络", detail, aggregateState);
        Report(progress, aggregate.StepId, aggregate.Title, aggregate.Detail, aggregate.State, 0.94);

        return [aggregate, .. completedResults];
    }

    private async Task<StartupCheckResult> CheckForUpdatesAsync(
        IProgress<StartupProgressUpdate>? progress,
        CancellationToken ct) {
        const string stepId = "updates";
        const string title = "检查更新";

        Report(progress, stepId, title, "正在连接 GitHub Releases", StartupCheckState.Running, 0.95);
        using var updates = new AppUpdateService(_http);
        LastUpdateCheckResult = await updates.CheckCurrentAssemblyAsync(ct).ConfigureAwait(false);

        var state = LastUpdateCheckResult.State switch {
            AppUpdateCheckState.UpToDate => StartupCheckState.Passed,
            AppUpdateCheckState.UpdateAvailable => StartupCheckState.Warning,
            _ => StartupCheckState.Warning
        };
        var result = new StartupCheckResult(stepId, title, LastUpdateCheckResult.Detail, state);
        Report(progress, result.StepId, result.Title, result.Detail, result.State, 0.98);
        return result;
    }

    private StartupCheckResult RunLocalStep(
        IProgress<StartupProgressUpdate>? progress,
        string stepId,
        string title,
        string runningDetail,
        double startProgress,
        double endProgress,
        Func<StartupCheckResult> run) {
        Report(progress, stepId, title, runningDetail, StartupCheckState.Running, startProgress);
        try {
            var result = run();
            Report(progress, stepId, title, result.Detail, result.State, endProgress);
            return result;
        } catch (Exception ex) {
            var failed = new StartupCheckResult(stepId, title, ex.Message, StartupCheckState.Failed);
            Report(progress, stepId, title, failed.Detail, failed.State, endProgress);
            return failed;
        }
    }

    private StartupCheckResult EnsureAppDirectories() {
        Directory.CreateDirectory(AppPaths.TileCacheDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.SettingsFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.StartupLogFile)!);

        var probePath = Path.Combine(AppPaths.DataDirectory, $".startup_write_probe_{Guid.NewGuid():N}.tmp");
        File.WriteAllText(probePath, "ok");
        File.Delete(probePath);

        return new StartupCheckResult("paths", "初始化应用目录", $"日志：{AppPaths.StartupLogFile}", StartupCheckState.Passed);
    }

    private static StartupCheckResult CheckHardware() {
        var os = RuntimeInformation.OSDescription.Trim();
        var detail = $"{Environment.ProcessorCount} 逻辑处理器，{RuntimeInformation.ProcessArchitecture}，{os}";
        return new StartupCheckResult("hardware", "检查硬件", detail, StartupCheckState.Passed);
    }

    private static StartupCheckResult CheckMemory() {
        var memory = GetMemorySnapshot();
        if (memory.TotalBytes == 0) {
            return new StartupCheckResult("memory", "检查内存", "无法读取物理内存状态", StartupCheckState.Warning);
        }

        var isLow = IsMemoryLow(memory.TotalBytes, memory.AvailableBytes);
        var load = memory.MemoryLoad is null ? "" : $"，负载 {memory.MemoryLoad}%";
        var detail = $"可用 {FormatBytes(memory.AvailableBytes)} / 总计 {FormatBytes(memory.TotalBytes)}{load}";
        return new StartupCheckResult(
            "memory",
            "检查内存",
            detail,
            isLow ? StartupCheckState.Warning : StartupCheckState.Passed);
    }

    private static StartupCheckResult CheckDisk() {
        var root = Path.GetPathRoot(AppPaths.DataDirectory);
        if (string.IsNullOrWhiteSpace(root)) {
            return new StartupCheckResult("disk", "检查硬盘", "无法确定应用所在磁盘", StartupCheckState.Warning);
        }

        var drive = new DriveInfo(root);
        if (!drive.IsReady) {
            return new StartupCheckResult("disk", "检查硬盘", $"{root} 未就绪", StartupCheckState.Failed);
        }

        var isLow = IsDiskLow(drive.TotalSize, drive.AvailableFreeSpace);
        var detail = $"{root} 可用 {FormatBytes((ulong)drive.AvailableFreeSpace)} / 总计 {FormatBytes((ulong)drive.TotalSize)}";
        return new StartupCheckResult(
            "disk",
            "检查硬盘",
            detail,
            isLow ? StartupCheckState.Warning : StartupCheckState.Passed);
    }

    private static MemorySnapshot GetMemorySnapshot() {
        if (OperatingSystem.IsWindows() && TryGetWindowsMemoryStatus(out var windowsMemory)) {
            return windowsMemory;
        }

        var gcInfo = GC.GetGCMemoryInfo();
        if (gcInfo.TotalAvailableMemoryBytes <= 0) {
            return new MemorySnapshot(0, 0, null);
        }

        var total = (ulong)gcInfo.TotalAvailableMemoryBytes;
        var used = (ulong)Math.Max(0, GC.GetTotalMemory(forceFullCollection: false));
        return new MemorySnapshot(total, total > used ? total - used : 0, null);
    }

    private static bool TryGetWindowsMemoryStatus(out MemorySnapshot snapshot) {
        var status = new MemoryStatusEx {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };
        if (!GlobalMemoryStatusEx(ref status)) {
            snapshot = new MemorySnapshot(0, 0, null);
            return false;
        }

        snapshot = new MemorySnapshot(status.TotalPhys, status.AvailPhys, status.MemoryLoad);
        return true;
    }

    private static void Report(
        IProgress<StartupProgressUpdate>? progress,
        string stepId,
        string title,
        string detail,
        StartupCheckState state,
        double progressValue) {
        var normalizedProgress = Math.Clamp(progressValue, 0, 1);
        progress?.Report(new StartupProgressUpdate(stepId, title, detail, state, normalizedProgress));
        Logger.Startup($"{title}: {state} - {detail}");
    }

    private static string GetTileStepId(TileSourcePreset source) {
        return $"tile:{source.Name}";
    }

    private static bool RequiresAccessToken(string source) {
        if (source.IndexOf("{access_token}", StringComparison.OrdinalIgnoreCase) >= 0 ||
            source.IndexOf("{token}", StringComparison.OrdinalIgnoreCase) >= 0) {
            return true;
        }

        try {
            return TileSourceDefinition.Parse(source).IsBing;
        } catch (NotSupportedException) {
            return false;
        }
    }

    private static string GetHost(string url) {
        return Uri.TryCreate(RedactSensitiveQueryValues(url), UriKind.Absolute, out var uri)
            ? uri.Host
            : "未知主机";
    }

    private static string RedactSensitiveQueryValues(string url) {
        return Regex.Replace(
            url,
            @"(?i)([?&](?:access_token|token|key|api_key)=)[^&]+",
            "$1***");
    }

    private static string FormatBytes(ulong bytes) {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    private sealed record MemorySnapshot(ulong TotalBytes, ulong AvailableBytes, uint? MemoryLoad);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

}
