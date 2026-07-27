using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class TileDiskCacheTests {
    [Fact]
    public void Trim_RemovesExpiredFilesThenOldestFilesUntilWithinSizeLimit() {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "wpf-osm-editor-tests", Guid.NewGuid().ToString("N"));
        var now = new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc);

        try {
            Directory.CreateDirectory(cacheRoot);
            var expired = WriteFile(cacheRoot, "expired.tile", 4, now.AddDays(-31));
            var oldest = WriteFile(cacheRoot, "oldest.tile", 6, now.AddDays(-2));
            var newest = WriteFile(cacheRoot, "newest.tile", 6, now.AddDays(-1));

            TileDiskCache.Trim(cacheRoot, maxBytes: 6, maxAge: TimeSpan.FromDays(30), nowUtc: now);

            Assert.False(File.Exists(expired));
            Assert.False(File.Exists(oldest));
            Assert.True(File.Exists(newest));
        } finally {
            if (Directory.Exists(cacheRoot)) {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    private static string WriteFile(string root, string name, int bytes, DateTime lastWriteTimeUtc) {
        var path = Path.Combine(root, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        return path;
    }
}
