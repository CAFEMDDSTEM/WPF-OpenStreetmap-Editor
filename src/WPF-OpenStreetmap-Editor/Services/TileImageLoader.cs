using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;

namespace WPF_OpenStreetmap_Editor.Services;

/// <summary>
/// 瓦片图片异步加载器：集成内存缓存 + 去重（相同 key 的并发请求只执行一次下载解码）。
/// 优先走 TileMemoryCache LRU 缓存，未命中时通过 TileService 下载字节并解码为 BitmapSource。
/// </summary>
public sealed class TileImageLoader {
    private const int SharedMaxEntries = 768;
    private const long SharedMaxBytes = 192L * 1024 * 1024;   // 192 MB
    private readonly TileMemoryCache _memoryCache;
    private readonly ConcurrentDictionary<string, Lazy<Task<BitmapSource?>>> _inFlight = new(); // 去重字典

    /// <summary>全局共享实例（768 条目 / 192 MB）</summary>
    public static TileImageLoader Shared { get; } = new(
        new TileMemoryCache(SharedMaxEntries, SharedMaxBytes));

    public TileImageLoader(TileMemoryCache memoryCache) {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
    }

    /// <summary>清空内存缓存</summary>
    public void Clear() {
        _memoryCache.Clear();
    }

    /// <summary>
    /// 异步加载瓦片 BitmapSource：
    /// 1) 查内存缓存 → 命中直接返回
    /// 2) 通过 _inFlight 去重（相同 key 的并发请求共享同一个 Task）
    /// 3) 未命中时调用 LoadAndCacheAsync 下载并解码
    /// </summary>
    public async Task<BitmapSource?> LoadAsync(
        TileService tileService,
        int zoom,
        int tileX,
        int tileY,
        string? accessToken,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(tileService);
        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = CreateCacheKey(tileService.CacheIdentity, zoom, tileX, tileY);
        if (_memoryCache.TryGetValue(cacheKey, out var cached)) {
            return cached;
        }

        // 去重：相同 cacheKey 的并发请求共享同一个下载+解码任务
        var candidate = new Lazy<Task<BitmapSource?>>(
            () => LoadAndCacheAsync(tileService, cacheKey, zoom, tileX, tileY, accessToken),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var request = _inFlight.GetOrAdd(cacheKey, candidate);
        var sharedTask = request.Value;

        // 只有成功插入的调用方负责在完成后清理 _inFlight 条目
        if (ReferenceEquals(request, candidate)) {
            _ = sharedTask.ContinueWith(
                _ => _inFlight.TryRemove(
                    new KeyValuePair<string, Lazy<Task<BitmapSource?>>>(cacheKey, request)),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return await sharedTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>从 TileService 获取字节 → 后台解码 → 加入内存缓存</summary>
    private async Task<BitmapSource?> LoadAndCacheAsync(
        TileService tileService,
        string cacheKey,
        int zoom,
        int tileX,
        int tileY,
        string? accessToken) {
        var bytes = await tileService
            .GetTileBytesAsync(zoom, tileX, tileY, accessToken, CancellationToken.None)
            .ConfigureAwait(false);
        if (bytes is null) return null;

        var source = await Task.Run(() => DecodeTile(bytes)).ConfigureAwait(false);
        if (source is not null) {
            _memoryCache.Add(cacheKey, source);
        }
        return source;
    }

    /// <summary>构建缓存键：图源标识 + zoom/tileX/tileY，X 做环绕归一化</summary>
    private static string CreateCacheKey(string sourceIdentity, int zoom, int tileX, int tileY) {
        if (zoom >= GeoConverter.MinZoom && zoom <= GeoConverter.MaxZoom) {
            var tileCount = GeoConverter.GetTileCount(zoom);
            tileX = ((tileX % tileCount) + tileCount) % tileCount;
        }
        return $"{sourceIdentity}|{zoom}/{tileX}/{tileY}";
    }

    /// <summary>将字节数组解码为 WPF BitmapImage（使用 MemoryStream + BitmapCacheOption.OnLoad）</summary>
    private static BitmapSource? DecodeTile(byte[] data) {
        try {
            using var stream = new MemoryStream(data, writable: false);
            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.StreamSource = stream;
            source.EndInit();
            source.Freeze();
            return source;
        } catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or ArgumentException) {
            Logger.Error("Failed to decode tile image", ex);
            return null;
        }
    }
}
