using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class DocumentExportPathServiceTests {
    [Fact]
    public void CreateDefaultFileName_UsesSourceFileName() {
        var document = new MapDocument {
            Name = "ignored.osm",
            SourcePath = Path.Combine("data", "city.osm")
        };

        var fileName = DocumentExportPathService.CreateDefaultFileName(document, ".geojson");

        Assert.Equal("city.geojson", fileName);
    }

    [Fact]
    public void CreateDefaultFileName_FallsBackToMap() {
        var document = new MapDocument { Name = "" };

        var fileName = DocumentExportPathService.CreateDefaultFileName(document, ".gpx");

        Assert.Equal("map.gpx", fileName);
    }

    [Fact]
    public void CreateSiblingPath_UsesSourceDirectoryAndRequestedExtension() {
        var sourcePath = Path.Combine(Path.GetTempPath(), "documents", "city.osm");
        var document = new MapDocument { SourcePath = sourcePath };

        var exportPath = DocumentExportPathService.CreateSiblingPath(document, ".gpx");

        Assert.Equal(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sourcePath))!, "city.gpx"), exportPath);
    }

    [Fact]
    public void CreateSiblingPath_WithoutSourcePathReturnsNull() {
        Assert.Null(DocumentExportPathService.CreateSiblingPath(new MapDocument(), ".gpx"));
    }

    [Fact]
    public void CreateParentPath_UsesParentDirectoryAndOriginalFileName() {
        var sourcePath = Path.Combine(Path.GetTempPath(), "exports", "nested", "city.geojson");
        var document = new MapDocument { SourcePath = sourcePath };

        var exportPath = DocumentExportPathService.CreateParentPath(document);

        Assert.Equal(Path.Combine(Path.GetTempPath(), "exports", "city.geojson"), exportPath);
    }
}
