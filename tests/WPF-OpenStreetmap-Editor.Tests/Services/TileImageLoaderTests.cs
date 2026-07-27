using System.Net;
using System.Net.Http.Headers;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class TileImageLoaderTests {
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");

    [Fact]
    public async Task LoadAsync_ReusesDecodedImageFromMemoryCache() {
        var cacheRoot = CreateCacheRoot();
        var handler = new ControlledTileHandler(PngBytes);
        using var http = new HttpClient(handler);
        using var service = CreateTileService(http, cacheRoot);
        var loader = new TileImageLoader(new TileMemoryCache(maxEntries: 8, maxBytes: 1024));

        try {
            var first = await loader.LoadAsync(service, 2, 1, 1, accessToken: null);
            var second = await loader.LoadAsync(service, 2, 1, 1, accessToken: null);

            Assert.NotNull(first);
            Assert.Same(first, second);
            Assert.True(first.IsFrozen);
            Assert.Equal(1, handler.RequestCount);
        } finally {
            DeleteCacheRoot(cacheRoot);
        }
    }

    [Fact]
    public async Task LoadAsync_DeduplicatesConcurrentRequests() {
        var cacheRoot = CreateCacheRoot();
        var handler = new ControlledTileHandler(PngBytes, waitForRelease: true);
        using var http = new HttpClient(handler);
        using var service = CreateTileService(http, cacheRoot);
        var loader = new TileImageLoader(new TileMemoryCache(maxEntries: 8, maxBytes: 1024));

        try {
            var firstTask = loader.LoadAsync(service, 3, 4, 2, accessToken: null);
            await handler.RequestStarted;
            var secondTask = loader.LoadAsync(service, 3, 4, 2, accessToken: null);

            handler.Release();
            var images = await Task.WhenAll(firstTask, secondTask);

            Assert.NotNull(images[0]);
            Assert.Same(images[0], images[1]);
            Assert.Equal(1, handler.RequestCount);
        } finally {
            DeleteCacheRoot(cacheRoot);
        }
    }

    [Fact]
    public async Task LoadAsync_CancelledCallerDoesNotCancelSharedRequest() {
        var cacheRoot = CreateCacheRoot();
        var handler = new ControlledTileHandler(PngBytes, waitForRelease: true);
        using var http = new HttpClient(handler);
        using var service = CreateTileService(http, cacheRoot);
        var loader = new TileImageLoader(new TileMemoryCache(maxEntries: 8, maxBytes: 1024));
        using var cancellation = new CancellationTokenSource();

        try {
            var cancelledTask = loader.LoadAsync(service, 3, 4, 2, accessToken: null, cancellation.Token);
            await handler.RequestStarted;
            var survivingTask = loader.LoadAsync(service, 3, 4, 2, accessToken: null);

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledTask);
            handler.Release();

            var image = await survivingTask;
            Assert.NotNull(image);
            Assert.True(image.IsFrozen);
            Assert.Equal(1, handler.RequestCount);
        } finally {
            DeleteCacheRoot(cacheRoot);
        }
    }

    private static TileService CreateTileService(HttpClient http, string cacheRoot) {
        return new TileService(http, cacheRoot) {
            TileTemplate = "https://tiles.example.com/{z}/{x}/{y}.png"
        };
    }

    private static string CreateCacheRoot() {
        return Path.Combine(Path.GetTempPath(), "wpf-osm-editor-tests", Guid.NewGuid().ToString("N"));
    }

    private static void DeleteCacheRoot(string cacheRoot) {
        if (Directory.Exists(cacheRoot)) {
            Directory.Delete(cacheRoot, recursive: true);
        }
    }

    private sealed class ControlledTileHandler(byte[] responseBytes, bool waitForRelease = false) : HttpMessageHandler {
        private readonly TaskCompletionSource<bool> _requestStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);
        public Task RequestStarted => _requestStarted.Task;

        public void Release() {
            _release.TrySetResult(true);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) {
            Interlocked.Increment(ref _requestCount);
            _requestStarted.TrySetResult(true);
            if (waitForRelease) {
                await _release.Task.WaitAsync(cancellationToken);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new ByteArrayContent(responseBytes)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return response;
        }
    }
}
