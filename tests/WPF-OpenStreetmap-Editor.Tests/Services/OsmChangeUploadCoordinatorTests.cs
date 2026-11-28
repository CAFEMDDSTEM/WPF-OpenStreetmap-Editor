using System.Net;
using System.Net.Http;
using System.Xml.Linq;
using WPF_OpenStreetmap_Editor.Models;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class OsmChangeUploadCoordinatorTests {
    [Fact]
    public async Task UploadAsync_IndividualStrategyRemapsCreatedNodesBeforeUploadingWay() {
        var document = CreateWayDocument();
        var build = OsmChangeSerializer.Build(document, 10);
        var nextId = 100L;
        var requests = new List<XElement>();
        var uploadProgress = new List<OsmUploadProgress>();
        var handler = new CapturingHttpHandler(async request => {
            var change = XDocument.Parse(await request.Content!.ReadAsStringAsync());
            requests.Add(change.Root!);
            var results = change.Root!.Descendants()
                .Where(static element => element.Name.LocalName is "node" or "way" or "relation")
                .Select(element => new XElement(
                    element.Name.LocalName,
                    new XAttribute("old_id", element.Attribute("id")!.Value),
                    new XAttribute("new_id", nextId++),
                    new XAttribute("new_version", "1")));
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(new XDocument(new XElement("diffResult", results)).ToString())
            };
        });
        var api = new OsmApiClient(new HttpClient(handler));

        var response = await OsmChangeUploadCoordinator.UploadAsync(
            api,
            "http://127.0.0.1/",
            new OsmAccountCredential(OsmAuthenticationMethod.OAuth2, "", "token"),
            10,
            document,
            build,
            OsmUploadStrategy.IndividualObjects,
            OsmChangeUploadCoordinator.DefaultChunkSize,
            progress: uploadProgress.Add);
        OsmChangeSerializer.ApplyDiffResult(document, build, response);

        Assert.Equal(3, requests.Count);
        Assert.Equal(3, uploadProgress.Count);
        Assert.Equal(3, uploadProgress[^1].CompletedObjectCount);
        Assert.Equal(3, uploadProgress[^1].TotalObjectCount);
        Assert.Equal(["node", "node", "way"], requests.SelectMany(static root => root.Elements().Single().Elements().Select(static item => item.Name.LocalName)));
        var wayRequest = requests[2].Element("create")!.Element("way")!;
        Assert.All(wayRequest.Elements("nd"), node => Assert.True(long.Parse(node.Attribute("ref")!.Value) > 0));
        Assert.Equal(102, document.Features.Single().Osm!.Id);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public async Task UploadAsync_WhenChunkFails_PreservesSuccessfulChunkAndPendingEdits() {
        var document = CreateWayDocument();
        var build = OsmChangeSerializer.Build(document, 10);
        var requestCount = 0;
        var handler = new CapturingHttpHandler(async request => {
            requestCount++;
            if (requestCount == 2) {
                return new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge) {
                    Content = new StringContent("Size limit exceeded")
                };
            }
            if (requestCount == 3) {
                return new HttpResponseMessage(HttpStatusCode.UnsupportedMediaType) {
                    Content = new StringContent("Gzip is not supported")
                };
            }

            var change = XDocument.Parse(await request.Content!.ReadAsStringAsync());
            var node = change.Root!.Element("create")!.Element("node")!;
            var oldId = node.Attribute("id")!.Value;
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent($"<diffResult><node old_id='{oldId}' new_id='100' new_version='1' /></diffResult>")
            };
        });
        var api = new OsmApiClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<OsmApiException>(() => OsmChangeUploadCoordinator.UploadAsync(
            api,
            "http://127.0.0.1/",
            new OsmAccountCredential(OsmAuthenticationMethod.OAuth2, "", "token"),
            10,
            document,
            build,
            OsmUploadStrategy.IndividualObjects,
            OsmChangeUploadCoordinator.DefaultChunkSize));

        Assert.Equal(3, requestCount);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, exception.ResponseStatusCode);
        Assert.Equal("2/3", exception.UploadBatchLabel);
        Assert.Equal("create", exception.UploadBatchOperation);
        Assert.Equal("Size limit exceeded", exception.ResponseBody);
        Assert.Equal(1, exception.UploadBatchObjectCount);
        Assert.True(exception.UploadBatchXmlBytes > 0);
        Assert.Equal("1/3", exception.UploadSuccessfulBatches);
        Assert.Contains("node", exception.UploadBatchObjects);
        Assert.Contains(100, document.Osm!.Nodes.Keys);
        Assert.True(document.IsDirty);
        var retry = OsmChangeSerializer.Build(document, 11);
        Assert.Equal(2, retry.TotalCount);
        var retryXml = XDocument.Parse(retry.Xml);
        Assert.DoesNotContain(retryXml.Root!.Element("create")!.Elements("node"), node => node.Attribute("id")?.Value == "100");
        Assert.Single(retryXml.Root!.Element("create")!.Elements("way"));
    }

    [Fact]
    public async Task UploadAsync_SingleRequestFallsBackToChunksAfter413() {
        var document = CreateWayDocument();
        var build = OsmChangeSerializer.Build(document, 10);
        var requestCount = 0;
        var successfulBatchSizes = new List<int>();
        var handler = new CapturingHttpHandler(async request => {
            requestCount++;
            if (requestCount <= 4) {
                return new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge) {
                    Content = new StringContent("request too large")
                };
            }

            var change = XDocument.Parse(await request.Content!.ReadAsStringAsync());
            var primitives = change.Root!.Descendants()
                .Where(static element => element.Name.LocalName is "node" or "way" or "relation")
                .ToList();
            successfulBatchSizes.Add(primitives.Count);
            var results = primitives.Select(element => new XElement(
                    element.Name.LocalName,
                    new XAttribute("old_id", element.Attribute("id")!.Value),
                    new XAttribute("new_id", 100 + requestCount),
                    new XAttribute("new_version", "1")));
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(new XDocument(new XElement("diffResult", results)).ToString())
            };
        });
        var api = new OsmApiClient(new HttpClient(handler));

        var response = await OsmChangeUploadCoordinator.UploadAsync(
            api,
            "http://127.0.0.1/",
            new OsmAccountCredential(OsmAuthenticationMethod.OAuth2, "", "token"),
            10,
            document,
            build,
            OsmUploadStrategy.SingleRequest,
            OsmChangeUploadCoordinator.DefaultChunkSize);

        OsmChangeSerializer.ApplyDiffResult(document, build, response);

        Assert.Equal(7, requestCount);
        Assert.Equal([1, 1, 1], successfulBatchSizes);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public async Task UploadAsync_WhenChangesetBoundingBoxLimitIsReached_RetriesInFreshChangeset() {
        var document = new MapDocument();
        document.Features.Add(new MapFeature {
            Id = "new-node",
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(1, 1)]],
            Attributes = new Dictionary<string, string> { ["amenity"] = "bench" }
        });
        var build = OsmChangeSerializer.Build(document, 10);
        var uploadPaths = new List<string>();
        var retriedChangeset = 0L;
        var handler = new CapturingHttpHandler(async request => {
            if (request.RequestUri!.AbsolutePath.EndsWith("/changeset/create", StringComparison.Ordinal)) {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("11") };
            }

            uploadPaths.Add(request.RequestUri.AbsolutePath);
            if (request.RequestUri.AbsolutePath.EndsWith("/changeset/10/upload", StringComparison.Ordinal)) {
                return new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge) {
                    Content = new StringContent("Changeset bounding box size limit exceeded.")
                };
            }

            var change = XDocument.Parse(await request.Content!.ReadAsStringAsync());
            var node = Assert.Single(change.Root!.Element("create")!.Elements("node"));
            retriedChangeset = long.Parse(node.Attribute("changeset")!.Value);
            var oldId = node.Attribute("id")!.Value;
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent($"<diffResult><node old_id='{oldId}' new_id='100' new_version='1' /></diffResult>")
            };
        });
        var api = new OsmApiClient(new HttpClient(handler));

        var response = await OsmChangeUploadCoordinator.UploadAsync(
            api,
            "http://127.0.0.1/",
            new OsmAccountCredential(OsmAuthenticationMethod.OAuth2, "", "token"),
            10,
            document,
            build,
            OsmUploadStrategy.IndividualObjects,
            OsmChangeUploadCoordinator.DefaultChunkSize,
            createChangesetAfterBoundingBoxLimit: token =>
                api.CreateChangesetAsync("http://127.0.0.1/", new OsmAccountCredential(OsmAuthenticationMethod.OAuth2, "", "token"), "continued", token));
        OsmChangeSerializer.ApplyDiffResult(document, build, response);

        Assert.Equal(3, uploadPaths.Count);
        Assert.Equal("/api/0.6/changeset/10/upload", uploadPaths[0]);
        Assert.Equal("/api/0.6/changeset/10/upload", uploadPaths[1]);
        Assert.Equal("/api/0.6/changeset/11/upload", uploadPaths[2]);
        Assert.Equal(11, retriedChangeset);
        Assert.Equal(100, document.Features.Single().Osm!.Id);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public async Task UploadAsync_WhenFreshChangesetAlsoRejectsSingleObject_DoesNotRotateAgain() {
        var document = new MapDocument();
        document.Features.Add(new MapFeature {
            Id = "new-node",
            GeometryType = MapGeometryType.Point,
            Parts = [[new GeoPoint(1, 1)]],
            Attributes = new Dictionary<string, string> { ["amenity"] = "bench" }
        });
        document.IsDirty = true;
        var build = OsmChangeSerializer.Build(document, 10);
        var changesetCreationCount = 0;
        var uploadRequestCount = 0;
        var handler = new CapturingHttpHandler(_ => {
            uploadRequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge) {
                Content = new StringContent("Changeset bounding box size limit exceeded.")
            });
        });
        var api = new OsmApiClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<OsmApiException>(() => OsmChangeUploadCoordinator.UploadAsync(
            api,
            "http://127.0.0.1/",
            new OsmAccountCredential(OsmAuthenticationMethod.OAuth2, "", "token"),
            10,
            document,
            build,
            OsmUploadStrategy.IndividualObjects,
            OsmChangeUploadCoordinator.DefaultChunkSize,
            createChangesetAfterBoundingBoxLimit: _ => {
                changesetCreationCount++;
                return Task.FromResult(11L);
            }));

        Assert.Equal(1, changesetCreationCount);
        Assert.Equal(4, uploadRequestCount);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, exception.ResponseStatusCode);
        Assert.Equal("1/1", exception.UploadBatchLabel);
        Assert.True(document.IsDirty);
    }

    [Fact]
    public void ApplyDiffResult_WhenServerOmitsAnObject_DoesNotMarkDocumentClean() {
        var document = CreateWayDocument();
        document.IsDirty = true;
        var build = OsmChangeSerializer.Build(document, 10);

        Assert.Throws<InvalidDataException>(() => OsmChangeSerializer.ApplyDiffResult(document, build, "<diffResult />"));

        Assert.True(document.IsDirty);
    }

    private static MapDocument CreateWayDocument() {
        var document = new MapDocument();
        document.Features.Add(new MapFeature {
            Id = "new-way",
            GeometryType = MapGeometryType.LineString,
            Parts = [[new GeoPoint(1, 1), new GeoPoint(2, 2)]],
            Attributes = new Dictionary<string, string> { ["highway"] = "service" }
        });
        return document;
    }

    private sealed class CapturingHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            return responseFactory(request);
        }
    }
}
