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
            OsmChangeUploadCoordinator.DefaultChunkSize);
        OsmChangeSerializer.ApplyDiffResult(document, build, response);

        Assert.Equal(3, requests.Count);
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
                return new HttpResponseMessage(HttpStatusCode.InternalServerError) {
                    Content = new StringContent("temporary server failure")
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

        await Assert.ThrowsAsync<OsmApiException>(() => OsmChangeUploadCoordinator.UploadAsync(
            api,
            "http://127.0.0.1/",
            new OsmAccountCredential(OsmAuthenticationMethod.OAuth2, "", "token"),
            10,
            document,
            build,
            OsmUploadStrategy.IndividualObjects,
            OsmChangeUploadCoordinator.DefaultChunkSize));

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
