using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed class CliApplication {
    private const string DefaultTokenEnvironmentVariable = "OSM_ACCESS_TOKEN";
    private readonly HttpClient _httpClient;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly Func<OsmAccountStore> _accountStoreFactory;

    public CliApplication()
        : this(new HttpClient { Timeout = TimeSpan.FromMinutes(3) }, Console.Out, Console.Error) {
    }

    public CliApplication(
        HttpClient httpClient,
        TextWriter output,
        TextWriter error,
        Func<OsmAccountStore>? accountStoreFactory = null) {
        _httpClient = httpClient;
        _output = output;
        _error = error;
        _accountStoreFactory = accountStoreFactory ?? (() => new OsmAccountStore());
    }

    public async Task<int> RunAsync(IEnumerable<string> args, CancellationToken ct = default) {
        try {
            var command = CliCommandLine.Parse(args);
            return await ExecuteAsync(command, ct);
        } catch (CliArgumentException ex) {
            _error.WriteLine(ex.Message);
            _error.WriteLine("Run 'WPF-OpenStreetmap-Editor.exe help' for usage.");
            return 2;
        } catch (OperationCanceledException) {
            _error.WriteLine("Operation canceled.");
            return 130;
        } catch (Exception ex) {
            _error.WriteLine(ex.Message);
            return 1;
        }
    }

    public static MapDocument ApplyFeatureSelection(MapDocument document, CliCommandLine command) {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(command);

        if (command.FeatureIds.Count == 0 && command.TagFilters.Count == 0) return document;

        var selectedIds = command.FeatureIds.ToHashSet(StringComparer.Ordinal);
        var selected = document.Features
            .Where(feature => MatchesFeatureSelection(feature, selectedIds, command.TagFilters))
            .Select(static feature => feature.Clone())
            .ToList();
        if (selected.Count == 0) {
            throw new InvalidDataException("No features matched the requested CLI selection.");
        }

        var filtered = new MapDocument {
            Name = document.Name,
            SourcePath = document.SourcePath,
            SourceFormat = document.SourceFormat,
            SkippedFeatureCount = document.SkippedFeatureCount
        };
        filtered.Features.AddRange(selected);
        filtered.MarkClean();
        return filtered;
    }

    private async Task<int> ExecuteAsync(CliCommandLine command, CancellationToken ct) {
        switch (command.Kind) {
            case CliCommandKind.Help:
                _output.WriteLine(GetHelpText());
                return 0;
            case CliCommandKind.Import:
                return await ImportAsync(command, ct);
            case CliCommandKind.Convert:
                return await ConvertAsync(command, ct);
            case CliCommandKind.Download:
                return await DownloadAsync(command, ct);
            case CliCommandKind.Changeset:
                return await WriteChangesetAsync(command, ct);
            case CliCommandKind.Upload:
                return await UploadAsync(command, ct);
            default:
                throw new CliArgumentException("Unsupported CLI command.");
        }
    }

    private async Task<int> ImportAsync(CliCommandLine command, CancellationToken ct) {
        var document = await LoadDocumentAsync(command, ct);
        WriteSummary(document);
        return 0;
    }

    private async Task<int> ConvertAsync(CliCommandLine command, CancellationToken ct) {
        var outputPath = PrepareOutputPath(command.OutputPath!, command.Force);
        var document = await LoadDocumentAsync(command, ct);
        await SpatialDataService.SaveAsync(document, outputPath, ct);
        _output.WriteLine($"Converted {document.Features.Count.ToString("N0", CultureInfo.InvariantCulture)} features to {outputPath}.");
        return 0;
    }

    private async Task<int> DownloadAsync(CliCommandLine command, CancellationToken ct) {
        var outputPath = PrepareOutputPath(command.OutputPath!, command.Force);
        var activeAccount = command.ApiBaseUrlSpecified ? null : _accountStoreFactory().GetActive();
        var apiBaseUrl = ResolveApiBaseUrl(command, activeAccount);
        var api = new OsmApiClient(_httpClient);
        var progress = new Progress<OsmDownloadStage>(stage => _output.WriteLine($"Stage: {stage}"));
        var bytes = await api.DownloadMapAsync(apiBaseUrl, command.Bounds!.Value, progress, ct);
        await WriteBytesAsync(outputPath, bytes, command.Force, ct);
        _output.WriteLine($"Downloaded {bytes.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes to {outputPath}.");
        return 0;
    }

    private async Task<int> WriteChangesetAsync(CliCommandLine command, CancellationToken ct) {
        var outputPath = PrepareOutputPath(command.OutputPath!, command.Force);
        var document = await LoadDocumentAsync(command, ct);
        var changes = BuildChanges(document, command.ChangesetId);
        await WriteTextAsync(outputPath, changes.Xml, command.Force, ct);
        WriteChangeSummary(changes, $"Wrote OSM change preview to {outputPath}.");
        return 0;
    }

    private async Task<int> UploadAsync(CliCommandLine command, CancellationToken ct) {
        var document = await LoadDocumentAsync(command, ct);
        var preview = BuildChanges(document, command.ChangesetId);

        if (command.DryRun) {
            if (!string.IsNullOrWhiteSpace(command.OutputPath)) {
                var outputPath = PrepareOutputPath(command.OutputPath, command.Force);
                await WriteTextAsync(outputPath, preview.Xml, command.Force, ct);
                WriteChangeSummary(preview, $"Wrote dry-run OSM change preview to {outputPath}.");
            } else {
                WriteChangeSummary(preview, "Dry run completed without contacting OSM.");
            }
            return 0;
        }

        if (!command.ConfirmWrite) {
            throw new CliArgumentException("Real OSM upload requires --yes. Use --dry-run or the changeset command to preview first.");
        }
        if (string.IsNullOrWhiteSpace(command.Comment)) {
            throw new CliArgumentException("Real OSM upload requires --comment.");
        }

        var store = ShouldLoadAccountStore(command) ? _accountStoreFactory() : null;
        var activeAccount = store?.GetActive();
        var apiBaseUrl = ResolveApiBaseUrl(command, activeAccount);
        var accessToken = ResolveAccessToken(command, store, activeAccount);
        if (string.IsNullOrWhiteSpace(accessToken)) {
            throw new CliArgumentException("Real OSM upload requires --token, --token-env, OSM_ACCESS_TOKEN, or an active WOSM account token.");
        }

        var api = new OsmApiClient(_httpClient);
        long? changesetId = null;
        try {
            changesetId = await api.CreateChangesetAsync(apiBaseUrl, accessToken, command.Comment, ct);
            var changes = BuildChanges(document, changesetId.Value);
            var response = await api.UploadChangesAsync(apiBaseUrl, accessToken, changesetId.Value, changes.Xml, ct);
            OsmChangeSerializer.ApplyDiffResult(document, changes, response);
            WriteChangeSummary(changes, $"Uploaded OSM changes to changeset {changesetId.Value.ToString(CultureInfo.InvariantCulture)}.");
        } finally {
            if (changesetId.HasValue) {
                await api.CloseChangesetAsync(apiBaseUrl, accessToken, changesetId.Value, CancellationToken.None);
            }
        }

        return 0;
    }

    private async Task<MapDocument> LoadDocumentAsync(CliCommandLine command, CancellationToken ct) {
        var document = await SpatialDataService.ImportAsync(
            command.InputPath!,
            new SpatialImportOptions {
                MaxFeatures = command.MaxFeatures,
                MaxCoordinates = command.MaxCoordinates
            },
            ct: ct);
        document = ApplyFeatureSelection(document, command);
        if (command.TreatInputAsNew) MarkInputAsNewOsmObjects(document);
        return document;
    }

    private static OsmChangeBuildResult BuildChanges(MapDocument document, long changesetId) {
        var changes = OsmChangeSerializer.Build(document, changesetId);
        if (changes.TotalCount == 0) {
            throw new InvalidDataException("No OSM changes were found in the selected input.");
        }

        return changes;
    }

    private void WriteSummary(MapDocument document) {
        var coordinateCount = document.Features.Sum(static feature => feature.CoordinateCount);
        _output.WriteLine($"Name: {document.Name}");
        _output.WriteLine($"Format: {document.SourceFormat?.ToString() ?? "Unknown"}");
        _output.WriteLine($"Features: {document.Features.Count.ToString("N0", CultureInfo.InvariantCulture)}");
        _output.WriteLine($"Coordinates: {coordinateCount.ToString("N0", CultureInfo.InvariantCulture)}");
        if (document.SkippedFeatureCount > 0) {
            _output.WriteLine($"Skipped: {document.SkippedFeatureCount.ToString("N0", CultureInfo.InvariantCulture)}");
        }

        var bounds = document.Bounds;
        if (bounds.IsValid) {
            _output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Bounds: {bounds.MinLongitude:R},{bounds.MinLatitude:R},{bounds.MaxLongitude:R},{bounds.MaxLatitude:R}"));
        }
    }

    private void WriteChangeSummary(OsmChangeBuildResult changes, string message) {
        _output.WriteLine(message);
        _output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Changes: create={changes.CreateCount}, modify={changes.ModifyCount}, delete={changes.DeleteCount}, total={changes.TotalCount}"));
    }

    private static string PrepareOutputPath(string path, bool force) {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath) && !force) {
            throw new IOException($"Output file already exists: {fullPath}. Use --force to overwrite it.");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        return fullPath;
    }

    private static async Task WriteBytesAsync(string path, byte[] bytes, bool force, CancellationToken ct) {
        await using var stream = new FileStream(
            path,
            force ? FileMode.Create : FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        await stream.WriteAsync(bytes, ct);
    }

    private static async Task WriteTextAsync(string path, string text, bool force, CancellationToken ct) {
        var bytes = new UTF8Encoding(false).GetBytes(text);
        await WriteBytesAsync(path, bytes, force, ct);
    }

    private static bool MatchesFeatureSelection(
        MapFeature feature,
        IReadOnlySet<string> selectedIds,
        IReadOnlyList<CliTagFilter> tagFilters) {
        if (selectedIds.Count > 0 && !selectedIds.Contains(feature.Id)) return false;
        foreach (var filter in tagFilters) {
            if (!feature.Attributes.TryGetValue(filter.Key, out var value) ||
                !string.Equals(value, filter.Value, StringComparison.Ordinal)) {
                return false;
            }
        }

        return true;
    }

    private static void MarkInputAsNewOsmObjects(MapDocument document) {
        foreach (var feature in document.Features) {
            feature.Osm = null;
        }
        document.IsDirty = true;
    }

    private static bool ShouldLoadAccountStore(CliCommandLine command) {
        return !command.ApiBaseUrlSpecified ||
            (string.IsNullOrWhiteSpace(command.AccessToken) &&
                string.IsNullOrWhiteSpace(command.AccessTokenEnvironmentVariable) &&
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DefaultTokenEnvironmentVariable)));
    }

    private static string ResolveApiBaseUrl(CliCommandLine command, OsmAccount? activeAccount) {
        return command.ApiBaseUrlSpecified
            ? command.ApiBaseUrl
            : activeAccount?.ApiBaseUrl ?? OsmApiClient.DefaultApiBaseUrl;
    }

    private static string? ResolveAccessToken(
        CliCommandLine command,
        OsmAccountStore? store,
        OsmAccount? activeAccount) {
        if (!string.IsNullOrWhiteSpace(command.AccessToken)) return command.AccessToken.Trim();

        var envName = string.IsNullOrWhiteSpace(command.AccessTokenEnvironmentVariable)
            ? DefaultTokenEnvironmentVariable
            : command.AccessTokenEnvironmentVariable;
        var envToken = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(envToken)) return envToken.Trim();

        return activeAccount is null ? null : store?.GetAccessToken(activeAccount);
    }

    private static string GetHelpText() {
        return """
            WOSM Command Line

            Usage:
              WPF-OpenStreetmap-Editor.exe
              WPF-OpenStreetmap-Editor.exe gui --fullscreen
              WPF-OpenStreetmap-Editor.exe import --input map.geojson
              WPF-OpenStreetmap-Editor.exe convert --input map.geojson --output map.gpx [--force]
              WPF-OpenStreetmap-Editor.exe download --bbox minLon,minLat,maxLon,maxLat --output data.osm [--api-base-url url]
              WPF-OpenStreetmap-Editor.exe changeset --input map.geojson --output preview.osc [--feature-id id] [--tag key=value]
              WPF-OpenStreetmap-Editor.exe upload --input map.geojson --dry-run [--output preview.osc]
              WPF-OpenStreetmap-Editor.exe upload --input map.geojson --comment "Add surveyed paths" --token-env OSM_ACCESS_TOKEN --yes

            Common options:
              --feature-id id           Select one feature id; repeat or comma-separate for more.
              --tag key=value           Select features with an exact tag match; repeat for more filters.
              --max-features n          Import safety limit. Default: 1000000.
              --max-coordinates n       Import safety limit. Default: 8000000.
              --treat-input-as-new      Ignore imported OSM ids and upload selected features as creates.
              --force                   Allow output file overwrite.

            OSM upload is a real write. It requires --yes, --comment, and an access token from
            --token, --token-env, OSM_ACCESS_TOKEN, or the active WOSM account.
            """;
    }
}
