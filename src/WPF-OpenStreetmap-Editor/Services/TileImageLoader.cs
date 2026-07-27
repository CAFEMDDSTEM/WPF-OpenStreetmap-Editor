using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed class TileImageLoader {
    private const int SharedMaxEntries = 768;
    private const long SharedMaxBytes = 192L * 1024 * 1024;
    private readonly TileMemoryCache _memoryCache;
    private readonly ConcurrentDictionary<string, Lazy<Task<BitmapSource?>>> _inFlight = new();

    public static TileImageLoader Shared { get; } = new(
        new TileMemoryCache(SharedMaxEntries, SharedMaxBytes));

    public TileImageLoader(TileMemoryCache memoryCache) {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
    }

    public void Clear() {
        _memoryCache.Clear();
    }

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

        var candidate = new Lazy<Task<BitmapSource?>>(
            () => LoadAndCacheAsync(tileService, cacheKey, zoom, tileX, tileY, accessToken),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var request = _inFlight.GetOrAdd(cacheKey, candidate);
        var sharedTask = request.Value;

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

    private static string CreateCacheKey(string sourceIdentity, int zoom, int tileX, int tileY) {
        if (zoom >= GeoConverter.MinZoom && zoom <= GeoConverter.MaxZoom) {
            var tileCount = GeoConverter.GetTileCount(zoom);
            tileX = ((tileX % tileCount) + tileCount) % tileCount;
        }

        return $"{sourceIdentity}|{zoom}/{tileX}/{tileY}";
    }

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
