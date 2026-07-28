using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class AtomicFileTests {
    [Fact]
    public void Write_FailurePreservesExistingFileAndRemovesTemporaryFile() {
        var root = Path.Combine(Path.GetTempPath(), "wpf-osm-editor-atomic-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, "original");

        try {
            Assert.Throws<InvalidDataException>(() => AtomicFile.Write(path, temporaryPath => {
                File.WriteAllText(temporaryPath, "partial");
                throw new InvalidDataException("simulated write failure");
            }));

            Assert.Equal("original", File.ReadAllText(path));
            Assert.Single(Directory.EnumerateFiles(root));
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }
}
