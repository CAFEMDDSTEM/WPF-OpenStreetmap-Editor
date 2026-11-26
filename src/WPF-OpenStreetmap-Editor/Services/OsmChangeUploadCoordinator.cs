using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public enum OsmUploadStrategy {
    SingleRequest,
    Chunked,
    IndividualObjects
}

public static class OsmChangeUploadCoordinator {
    public const int DefaultChunkSize = 1000;

    public static async Task<string> UploadAsync(
        OsmApiClient api,
        string apiBaseUrl,
        OsmAccountCredential credential,
        long changesetId,
        MapDocument document,
        OsmChangeBuildResult build,
        OsmUploadStrategy strategy,
        int chunkSize,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(build);
        if (strategy == OsmUploadStrategy.Chunked && chunkSize <= 0) {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be greater than zero.");
        }
        if (!Enum.IsDefined(strategy)) {
            throw new ArgumentOutOfRangeException(nameof(strategy));
        }

        var buildRoot = XDocument.Parse(build.Xml).Root ?? throw new InvalidDataException("OSM change document has no root element.");
        var totalObjects = CountPrimitives(buildRoot);
        Logger.Info($"OSM upload started: strategy={strategy}; objects={totalObjects}; chunkSize={chunkSize}.");
        if (strategy == OsmUploadStrategy.SingleRequest) {
            try {
                var response = await api.UploadChangesAsync(apiBaseUrl, credential, changesetId, build.Xml, ct);
                ValidateResponseCoversBatch(buildRoot, OsmChangeSerializer.ParseDiffMappings(response));
                Logger.Info($"OSM upload single request succeeded: objects={totalObjects}.");
                return response;
            } catch (OsmApiException ex) when (ex.ResponseStatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge) {
                Logger.Info($"OSM single request returned HTTP 413; retrying as chunks of up to {DefaultChunkSize} objects.");
            }
        }

        var batchLimit = strategy switch {
            OsmUploadStrategy.IndividualObjects => 1,
            OsmUploadStrategy.Chunked => chunkSize,
            _ => DefaultChunkSize
        };
        var batches = CreateBatches(build.Xml, batchLimit);
        Logger.Info($"OSM upload split into {batches.Count} initial batches; batchLimit={batchLimit}.");
        var mappings = new Dictionary<(string Type, long OldId), (long Id, int Version)>();
        var diffResults = new List<XElement>();
        foreach (var originalBatch in batches) {
            ct.ThrowIfCancellationRequested();
            var batch = RemapReferences(originalBatch, mappings);
            await UploadBatchAsync(api, apiBaseUrl, credential, changesetId, document, build, batch, mappings, diffResults, ct);
        }

        return new XDocument(new XElement("diffResult", diffResults)).ToString();
    }

    private static async Task<string> UploadBatchAsync(
        OsmApiClient api,
        string apiBaseUrl,
        OsmAccountCredential credential,
        long changesetId,
        MapDocument document,
        OsmChangeBuildResult build,
        XElement batch,
        Dictionary<(string Type, long OldId), (long Id, int Version)> mappings,
        List<XElement> diffResults,
        CancellationToken ct) {
        var batchXml = batch.ToString();
        var objectCount = CountPrimitives(batch);
        var byteCount = Encoding.UTF8.GetByteCount(batchXml);
        try {
            Logger.Info($"OSM upload batch sending: objects={objectCount}; xmlBytes={byteCount}; operation={batch.Elements().First().Name.LocalName}.");
            var response = await api.UploadChangesAsync(apiBaseUrl, credential, changesetId, batchXml, ct);
            var batchMappings = OsmChangeSerializer.ParseDiffMappings(response);
            ValidateResponseCoversBatch(batch, batchMappings);
            foreach (var (key, value) in batchMappings) mappings[key] = value;
            diffResults.AddRange(XDocument.Parse(response).Root?.Elements().Select(static element => new XElement(element)) ?? []);
            OsmChangeSerializer.ApplyPartialDiffResult(document, build, batchXml, mappings);
            Logger.Info($"OSM upload batch succeeded: objects={objectCount}; xmlBytes={byteCount}.");
            return response;
        } catch (OsmApiException ex) when (ex.ResponseStatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge && objectCount > 1) {
            Logger.Info($"OSM upload batch returned HTTP 413: objects={objectCount}; xmlBytes={byteCount}; splitting batch.");
            var children = batch.Elements().FirstOrDefault();
            if (children is null) throw;
            var operations = children.Elements().ToList();
            var middle = operations.Count / 2;
            var left = CreateBatchLike(batch, operations.Take(middle));
            var right = CreateBatchLike(batch, operations.Skip(middle));
            await UploadBatchAsync(api, apiBaseUrl, credential, changesetId, document, build, left, mappings, diffResults, ct);
            var remappedRight = RemapReferences(right, mappings);
            return await UploadBatchAsync(api, apiBaseUrl, credential, changesetId, document, build, remappedRight, mappings, diffResults, ct);
        }
    }

    private static List<XElement> CreateBatches(string changeXml, int batchLimit) {
        var source = XDocument.Parse(changeXml).Root ?? throw new InvalidDataException("OSM change document has no root element.");
        var batches = new List<XElement>();
        var phases = new (string Operation, string Type)[] {
            ("create", "node"), ("create", "way"), ("create", "relation"),
            ("modify", "node"), ("modify", "way"), ("modify", "relation"),
            ("delete", "relation"), ("delete", "way"), ("delete", "node")
        };

        foreach (var (operation, type) in phases) {
            var sourceOperation = source.Element(operation);
            if (sourceOperation is null) continue;
            var primitives = sourceOperation.Elements(type).ToList();
            for (var offset = 0; offset < primitives.Count; offset += batchLimit) {
                batches.Add(CreateBatch(source, sourceOperation, primitives.Skip(offset).Take(batchLimit)));
            }
        }
        return batches;
    }

    private static XElement CreateBatch(
        XElement source,
        XElement sourceOperation,
        IEnumerable<XElement> primitives) {
        var root = new XElement(source.Name, source.Attributes());
        var operationElement = new XElement(sourceOperation.Name, sourceOperation.Attributes());
        operationElement.Add(primitives.Select(static primitive => new XElement(primitive)));
        root.Add(operationElement);
        return root;
    }

    private static XElement CreateBatchLike(XElement batch, IEnumerable<XElement> primitives) {
        var root = new XElement(batch.Name, batch.Attributes());
        var operation = batch.Elements().First();
        var operationElement = new XElement(operation.Name, operation.Attributes());
        operationElement.Add(primitives.Select(static primitive => new XElement(primitive)));
        root.Add(operationElement);
        return root;
    }

    private static XElement RemapReferences(
        XElement batch,
        IReadOnlyDictionary<(string Type, long OldId), (long Id, int Version)> mappings) {
        var clone = new XElement(batch);
        foreach (var element in clone.DescendantsAndSelf().Where(static element => element.Name.LocalName is "node" or "way" or "relation")) {
            if (long.TryParse(element.Attribute("id")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
                mappings.TryGetValue((element.Name.LocalName, id), out var mapping)) {
                element.SetAttributeValue("id", mapping.Id);
            }

            foreach (var reference in element.Elements("nd")) {
                if (long.TryParse(reference.Attribute("ref")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nodeId) &&
                    mappings.TryGetValue(("node", nodeId), out var nodeMapping)) {
                    reference.SetAttributeValue("ref", nodeMapping.Id);
                }
            }
            foreach (var member in element.Elements("member")) {
                var type = member.Attribute("type")?.Value;
                if (type is not ("node" or "way" or "relation")) continue;
                if (long.TryParse(member.Attribute("ref")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var memberId) &&
                    mappings.TryGetValue((type, memberId), out var memberMapping)) {
                    member.SetAttributeValue("ref", memberMapping.Id);
                }
            }
        }
        return clone;
    }

    private static int CountPrimitives(XElement root) {
        return root.Descendants().Count(static element => element.Name.LocalName is "node" or "way" or "relation");
    }

    private static void ValidateResponseCoversBatch(
        XElement batch,
        IReadOnlyDictionary<(string Type, long OldId), (long Id, int Version)> mappings) {
        foreach (var primitive in batch.Descendants().Where(static element => element.Name.LocalName is "node" or "way" or "relation")) {
            var id = (long?)primitive.Attribute("id") ?? throw new InvalidDataException("OSM change object is missing an ID.");
            if (!mappings.ContainsKey((primitive.Name.LocalName, id))) {
                throw new InvalidDataException($"OSM API diffResult did not include {primitive.Name.LocalName} {id}.");
            }
        }
    }
}
