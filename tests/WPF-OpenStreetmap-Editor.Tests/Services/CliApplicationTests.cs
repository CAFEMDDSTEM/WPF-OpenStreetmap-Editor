using System.Globalization;
using System.Net.Http;
using System.Xml.Linq;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class CliApplicationTests {
    [Fact]
    public void Parse_DownloadReadsBoundingBoxAndOutputPath() {
        var command = CliCommandLine.Parse([
            "download",
            "--bbox",
            "103.8,1.3,103.9,1.4",
            "--output",
            "data.osm"
        ]);

        Assert.Equal(CliCommandKind.Download, command.Kind);
        Assert.Equal(new GeoBounds(103.8, 1.3, 103.9, 1.4), command.Bounds);
        Assert.Equal("data.osm", command.OutputPath);
    }

    [Fact]
    public void Parse_ChangesetAcceptsRepeatedFeatureAndTagFilters() {
        var command = CliCommandLine.Parse([
            "changeset",
            "--input",
            "map.geojson",
            "--output",
            "preview.osc",
            "--feature-id",
            "a,b",
            "--feature-id",
            "c",
            "--tag",
            "highway=service"
        ]);

        Assert.Equal(["a", "b", "c"], command.FeatureIds);
        var filter = Assert.Single(command.TagFilters);
        Assert.Equal("highway", filter.Key);
        Assert.Equal("service", filter.Value);
    }

    [Fact]
    public async Task RunAsync_ConvertWritesRequestedOutput() {
        var root = CreateTempDirectory();
        var inputPath = Path.Combine(root, "input.geojson");
        var outputPath = Path.Combine(root, "output.gpx");
        File.WriteAllText(inputPath, CreatePointGeoJson("survey-point", "Survey point"));

        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        var app = new CliApplication(new HttpClient(new RejectingHttpHandler()), output, error);

        var exitCode = await app.RunAsync(["convert", "--input", inputPath, "--output", outputPath]);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("<gpx", File.ReadAllText(outputPath), StringComparison.Ordinal);
        Assert.Contains("Converted 1 features", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ChangesetAppliesFeatureSelection() {
        var root = CreateTempDirectory();
        var inputPath = Path.Combine(root, "input.geojson");
        var outputPath = Path.Combine(root, "preview.osc");
        File.WriteAllText(inputPath, CreateTwoPointGeoJson());

        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        var app = new CliApplication(new HttpClient(new RejectingHttpHandler()), output, error);

        var exitCode = await app.RunAsync([
            "changeset",
            "--input",
            inputPath,
            "--output",
            outputPath,
            "--feature-id",
            "keep"
        ]);

        Assert.Equal(0, exitCode);
        var xml = XDocument.Parse(File.ReadAllText(outputPath));
        Assert.Single(xml.Descendants("node"));
        Assert.Contains(xml.Descendants("tag"), tag => tag.Attribute("v")?.Value == "Keep me");
        Assert.DoesNotContain(xml.Descendants("tag"), tag => tag.Attribute("v")?.Value == "Skip me");
    }

    [Fact]
    public async Task RunAsync_UploadWithoutYesStopsBeforeNetworkAccess() {
        var root = CreateTempDirectory();
        var inputPath = Path.Combine(root, "input.geojson");
        File.WriteAllText(inputPath, CreatePointGeoJson("survey-point", "Survey point"));

        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        var app = new CliApplication(new HttpClient(new RejectingHttpHandler()), output, error);

        var exitCode = await app.RunAsync(["upload", "--input", inputPath, "--comment", "test upload"]);

        Assert.Equal(2, exitCode);
        Assert.Contains("--yes", error.ToString(), StringComparison.Ordinal);
    }

    private static string CreateTempDirectory() {
        var path = Path.Combine(Path.GetTempPath(), "wpf-osm-editor-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreatePointGeoJson(string id, string name) {
        return $$"""
            {
              "type": "FeatureCollection",
              "features": [
                {
                  "type": "Feature",
                  "id": "{{id}}",
                  "properties": {
                    "name": "{{name}}"
                  },
                  "geometry": {
                    "type": "Point",
                    "coordinates": [103.8, 1.3]
                  }
                }
              ]
            }
            """;
    }

    private static string CreateTwoPointGeoJson() {
        return """
            {
              "type": "FeatureCollection",
              "features": [
                {
                  "type": "Feature",
                  "id": "keep",
                  "properties": {
                    "name": "Keep me"
                  },
                  "geometry": {
                    "type": "Point",
                    "coordinates": [103.8, 1.3]
                  }
                },
                {
                  "type": "Feature",
                  "id": "skip",
                  "properties": {
                    "name": "Skip me"
                  },
                  "geometry": {
                    "type": "Point",
                    "coordinates": [103.9, 1.4]
                  }
                }
              ]
            }
            """;
    }

    private sealed class RejectingHttpHandler : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            throw new InvalidOperationException("Network access should not be used by this test.");
        }
    }
}
