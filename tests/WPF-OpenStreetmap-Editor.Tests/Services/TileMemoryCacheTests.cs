using System.Windows.Media;
using System.Windows.Media.Imaging;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class TileMemoryCacheTests {
    [Fact]
    public void Add_EvictsLeastRecentlyUsedEntry() {
        var cache = new TileMemoryCache(maxEntries: 2, maxBytes: 1024);
        var first = CreateBitmap();
        var second = CreateBitmap();
        var third = CreateBitmap();

        cache.Add("first", first);
        cache.Add("second", second);
        Assert.True(cache.TryGetValue("first", out _));
        cache.Add("third", third);

        Assert.True(cache.TryGetValue("first", out _));
        Assert.True(cache.TryGetValue("third", out _));
        Assert.False(cache.TryGetValue("second", out _));
    }

    [Fact]
    public void Add_TrimsToByteBudget() {
        var cache = new TileMemoryCache(maxEntries: 10, maxBytes: 20);

        cache.Add("first", CreateBitmap(width: 2, height: 2));
        cache.Add("second", CreateBitmap(width: 2, height: 2));

        Assert.Equal(1, cache.Count);
        Assert.False(cache.TryGetValue("first", out _));
        Assert.True(cache.TryGetValue("second", out _));
        Assert.True(cache.CurrentBytes <= 20);
    }

    [Fact]
    public void Clear_RemovesCachedEntries() {
        var cache = new TileMemoryCache(maxEntries: 2, maxBytes: 1024);
        cache.Add("tile", CreateBitmap());

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.CurrentBytes);
        Assert.False(cache.TryGetValue("tile", out _));
    }

    [Fact]
    public async Task AddAndTryGet_HandleConcurrentAccess() {
        var cache = new TileMemoryCache(maxEntries: 16, maxBytes: 1024);

        var tasks = Enumerable.Range(0, 64)
            .Select(i => Task.Run(() => {
                var key = $"tile-{i}";
                cache.Add(key, CreateBitmap());
                cache.TryGetValue(key, out _);
            }));

        await Task.WhenAll(tasks);

        Assert.True(cache.Count <= 16);
        Assert.True(cache.CurrentBytes <= 1024);
    }

    private static BitmapSource CreateBitmap(int width = 1, int height = 1) {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }
}
