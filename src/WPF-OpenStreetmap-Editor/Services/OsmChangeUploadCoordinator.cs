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
        CancellationToken ct = default,
        Func<CancellationToken, Task<long>>? createChangesetAfterBoundingBoxLimit = null) {
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
        var uploadState = new UploadState(changesetId, createChangesetAfterBoundingBoxLimit);
        Logger.Info($"OSM upload started: strategy={strategy}; objects={totalObjects}; chunkSize={chunkSize}.");
        if (strategy == OsmUploadStrategy.SingleRequest) {
            var xmlBytes = Encoding.UTF8.GetByteCount(build.Xml);
            var objectIds = DescribeObjects(buildRoot);
            try {
                var response = await api.UploadChangesAsync(apiBaseUrl, credential, changesetId, build.Xml, ct);
                ValidateResponseCoversBatch(buildRoot, OsmChangeSerializer.ParseDiffMappings(response));
                Logger.Info($"OSM upload single request succeeded: status=200; objects={totalObjects}; xmlBytes={xmlBytes}; responseBody={FormatLogBody(response)}");
                return response;
            } catch (OsmApiException ex) {
                ex.SetUploadBatchContext("1/1", "single request", objectIds, totalObjects, xmlBytes);
                Logger.Error($"OSM upload single request failed: status={(int)ex.ResponseStatusCode}; objectCount={totalObjects}; objects={objectIds}; xmlBytes={xmlBytes}; responseBody={FormatLogBody(ex.ResponseBody)}");
                if (ex.IsChangesetBoundingBoxLimitExceeded && uploadState.CanCreateChangeset) {
                    var nextChangesetId = await uploadState.CreateNextChangesetAsync(ct);
                    Logger.Info($"OSM changeset bbox limit reached on changeset={changesetId}; retrying single request in changeset={nextChangesetId}.");
                    var retryXml = ChangeChangesetId(build.Xml, nextChangesetId);
                    try {
                        var response = await api.UploadChangesAsync(apiBaseUrl, credential, nextChangesetId, retryXml, ct);
                        ValidateResponseCoversBatch(XDocument.Parse(retryXml).Root!, OsmChangeSerializer.ParseDiffMappings(response));
                        Logger.Info($"OSM upload single request succeeded after changeset rotation: changeset={nextChangesetId}; objectCount={totalObjects}; responseBody={FormatLogBody(response)}");
                        return response;
                    } catch (OsmApiException retryException) {
                        retryException.SetUploadBatchContext("1/1", "single request", objectIds, totalObjects, Encoding.UTF8.GetByteCount(retryXml));
                        Logger.Error($"OSM single request retry in changeset={nextChangesetId} failed: status={(int)retryException.ResponseStatusCode}; responseBody={FormatLogBody(retryException.ResponseBody)}");
                        if (retryException.ResponseStatusCode != System.Net.HttpStatusCode.RequestEntityTooLarge) throw;
                    }
                } else if (ex.ResponseStatusCode != System.Net.HttpStatusCode.RequestEntityTooLarge) {
                    throw;
                }
                if (ex.ResponseStatusCode != System.Net.HttpStatusCode.RequestEntityTooLarge) throw;
                Logger.Info($"OSM single request returned HTTP 413; retrying as chunks of up to {DefaultChunkSize} objects.");
            } catch (Exception ex) {
                Logger.Error($"OSM upload single request failed before a valid diffResult was applied: objectCount={totalObjects}; objects={objectIds}; xmlBytes={xmlBytes}; error={ex.Message}", ex);
                throw;
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
        var successfulBatches = new List<string>();
        for (var index = 0; index < batches.Count; index++) {
            ct.ThrowIfCancellationRequested();
            var batch = ChangeChangesetId(RemapReferences(batches[index], mappings), uploadState.CurrentChangesetId);
            await UploadBatchAsync(
                api,
                apiBaseUrl,
                credential,
                uploadState,
                document,
                build,
                batch,
                $"{index + 1}/{batches.Count}",
                mappings,
                diffResults,
                successfulBatches,
                ct);
        }

        return new XDocument(new XElement("diffResult", diffResults)).ToString();
    }

    private static async Task<string> UploadBatchAsync(
        OsmApiClient api,
        string apiBaseUrl,
        OsmAccountCredential credential,
        UploadState uploadState,
        MapDocument document,
        OsmChangeBuildResult build,
        XElement batch,
        string batchLabel,
        Dictionary<(string Type, long OldId), (long Id, int Version)> mappings,
        List<XElement> diffResults,
        List<string> successfulBatches,
        CancellationToken ct,
        bool allowChangesetRotation = true) {
        var batchXml = batch.ToString();
        var objectCount = CountPrimitives(batch);
        var byteCount = Encoding.UTF8.GetByteCount(batchXml);
        var operation = batch.Elements().First().Name.LocalName;
        var objectIds = DescribeObjects(batch);
        var responseBody = "";
        Logger.Info($"OSM upload batch sending: batch={batchLabel}; operation={operation}; objectCount={objectCount}; objects={objectIds}; xmlBytes={byteCount}.");
        try {
            responseBody = await api.UploadChangesAsync(apiBaseUrl, credential, uploadState.CurrentChangesetId, batchXml, ct);
            var batchMappings = OsmChangeSerializer.ParseDiffMappings(responseBody);
            ValidateResponseCoversBatch(batch, batchMappings);
            foreach (var (key, value) in batchMappings) mappings[key] = value;
            diffResults.AddRange(XDocument.Parse(responseBody).Root?.Elements().Select(static element => new XElement(element)) ?? []);
            OsmChangeSerializer.ApplyPartialDiffResult(document, build, batchXml, mappings);
            successfulBatches.Add(batchLabel);
            Logger.Info($"OSM upload batch succeeded: batch={batchLabel}; status=200; operation={operation}; objectCount={objectCount}; objects={objectIds}; xmlBytes={byteCount}; responseBody={FormatLogBody(responseBody)}");
            return responseBody;
        } catch (OsmApiException ex) {
            var successfulBatchList = string.Join(", ", successfulBatches);
            ex.SetUploadBatchContext(batchLabel, operation, objectIds, objectCount, byteCount, successfulBatchList);
            Logger.Error($"OSM upload batch failed: changeset={uploadState.CurrentChangesetId}; batch={batchLabel}; status={(int)ex.ResponseStatusCode}; operation={operation}; objectCount={objectCount}; objects={objectIds}; xmlBytes={byteCount}; successfulBatches={successfulBatchList}; responseBody={FormatLogBody(ex.ResponseBody)}");
            if (ex.IsChangesetBoundingBoxLimitExceeded && allowChangesetRotation && uploadState.CanCreateChangeset) {
                var previousChangesetId = uploadState.CurrentChangesetId;
                var nextChangesetId = await uploadState.CreateNextChangesetAsync(ct);
                Logger.Info($"OSM changeset bbox limit reached: batch={batchLabel}; oldChangeset={previousChangesetId}; retryChangeset={nextChangesetId}; objectCount={objectCount}.");
                var retryBatch = ChangeChangesetId(batch, nextChangesetId);
                return await UploadBatchAsync(api, apiBaseUrl, credential, uploadState, document, build, retryBatch, batchLabel, mappings, diffResults, successfulBatches, ct, allowChangesetRotation: false);
            }
            if (ex.ResponseStatusCode != System.Net.HttpStatusCode.RequestEntityTooLarge || objectCount <= 1) throw;

            Logger.Info($"OSM upload batch returned HTTP 413: batch={batchLabel}; objectCount={objectCount}; xmlBytes={byteCount}; splitting batch.");
            var children = batch.Elements().FirstOrDefault();
            if (children is null) throw;
            var operations = children.Elements().ToList();
            var middle = operations.Count / 2;
            var left = CreateBatchLike(batch, operations.Take(middle));
            var right = CreateBatchLike(batch, operations.Skip(middle));
            await UploadBatchAsync(api, apiBaseUrl, credential, uploadState, document, build, left, $"{batchLabel}.1", mappings, diffResults, successfulBatches, ct);
            var remappedRight = ChangeChangesetId(RemapReferences(right, mappings), uploadState.CurrentChangesetId);
            return await UploadBatchAsync(api, apiBaseUrl, credential, uploadState, document, build, remappedRight, $"{batchLabel}.2", mappings, diffResults, successfulBatches, ct);
        } catch (Exception ex) {
            Logger.Error($"OSM upload batch failed before a valid diffResult was applied: batch={batchLabel}; operation={operation}; objectCount={objectCount}; objects={objectIds}; xmlBytes={byteCount}; responseBody={FormatLogBody(responseBody)}; error={ex.Message}", ex);
            throw;
        }
    }

    private static XElement ChangeChangesetId(XElement batch, long changesetId) {
        var clone = new XElement(batch);
        foreach (var primitive in clone.Descendants().Where(static element => element.Name.LocalName is "node" or "way" or "relation")) {
            primitive.SetAttributeValue("changeset", changesetId);
        }
        return clone;
    }

    private static string ChangeChangesetId(string changeXml, long changesetId) {
        var root = XDocument.Parse(changeXml).Root ?? throw new InvalidDataException("OSM change document has no root element.");
        foreach (var primitive in root.Descendants().Where(static element => element.Name.LocalName is "node" or "way" or "relation")) {
            primitive.SetAttributeValue("changeset", changesetId);
        }
        return root.ToString();
    }

    private static string FormatLogBody(string body) {
        return body.Replace('\r', ' ').Replace('\n', ' ');
    }

    private sealed class UploadState(
        long changesetId,
        Func<CancellationToken, Task<long>>? createChangeset) {
        public long CurrentChangesetId { get; private set; } = changesetId;
        public bool CanCreateChangeset => createChangeset is not null;

        public async Task<long> CreateNextChangesetAsync(CancellationToken ct) {
            var create = createChangeset ?? throw new InvalidOperationException("No changeset fallback is configured.");
            CurrentChangesetId = await create(ct);
            return CurrentChangesetId;
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

    private static string DescribeObjects(XElement batch) {
        var primitives = batch.Descendants().Where(static element => element.Name.LocalName is "node" or "way" or "relation").ToList();
        var descriptions = primitives.Take(8)
            .Select(static element => $"{element.Name.LocalName} {element.Attribute("id")?.Value ?? "?"}")
            .ToList();
        if (primitives.Count > descriptions.Count) descriptions.Add($"... +{primitives.Count - descriptions.Count}");
        return string.Join(", ", descriptions);
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
