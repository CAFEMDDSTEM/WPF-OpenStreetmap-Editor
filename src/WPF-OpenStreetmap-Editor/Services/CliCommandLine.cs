using System.Globalization;
using WPF_OpenStreetmap_Editor.Models;

namespace WPF_OpenStreetmap_Editor.Services;

public enum CliCommandKind {
    Help,
    Import,
    Convert,
    Download,
    Changeset,
    Upload
}

public sealed record CliTagFilter(string Key, string Value);

public sealed class CliArgumentException(string message) : Exception(message);

public sealed class CliCommandLine {
    public CliCommandKind Kind { get; set; }
    public string? InputPath { get; set; }
    public string? OutputPath { get; set; }
    public GeoBounds? Bounds { get; set; }
    public string ApiBaseUrl { get; set; } = OsmApiClient.DefaultApiBaseUrl;
    public bool ApiBaseUrlSpecified { get; set; }
    public string? Comment { get; set; }
    public string? AccessToken { get; set; }
    public string? AccessTokenEnvironmentVariable { get; set; }
    public long ChangesetId { get; set; }
    public bool Force { get; set; }
    public bool ConfirmWrite { get; set; }
    public bool DryRun { get; set; }
    public bool TreatInputAsNew { get; set; }
    public int MaxFeatures { get; set; } = 1_000_000;
    public int MaxCoordinates { get; set; } = 8_000_000;
    public List<string> FeatureIds { get; } = [];
    public List<CliTagFilter> TagFilters { get; } = [];

    public static CliCommandLine Parse(IEnumerable<string> args) {
        var tokens = args.ToList();
        if (tokens.Count == 0 || IsHelp(tokens[0])) {
            return new CliCommandLine { Kind = CliCommandKind.Help };
        }

        var command = tokens[0].ToLowerInvariant() switch {
            "help" => new CliCommandLine { Kind = CliCommandKind.Help },
            "import" => new CliCommandLine { Kind = CliCommandKind.Import },
            "convert" => new CliCommandLine { Kind = CliCommandKind.Convert },
            "download" => new CliCommandLine { Kind = CliCommandKind.Download },
            "changeset" => new CliCommandLine { Kind = CliCommandKind.Changeset },
            "upload" => new CliCommandLine { Kind = CliCommandKind.Upload },
            var unknown => throw new CliArgumentException($"Unknown CLI command '{unknown}'.")
        };

        ParseOptions(command, tokens.Skip(1).ToList());
        Validate(command);
        return command;
    }

    private static void ParseOptions(CliCommandLine command, IReadOnlyList<string> tokens) {
        var positionals = new List<string>();

        for (var i = 0; i < tokens.Count; i++) {
            var token = tokens[i];
            switch (token.ToLowerInvariant()) {
                case "--help":
                case "-h":
                case "/?":
                    command.Kind = CliCommandKind.Help;
                    return;
                case "--input":
                case "-i":
                    command.InputPath = ReadValue(tokens, ref i, token);
                    break;
                case "--output":
                case "-o":
                    command.OutputPath = ReadValue(tokens, ref i, token);
                    break;
                case "--bbox":
                    command.Bounds = ParseBounds(ReadValue(tokens, ref i, token));
                    break;
                case "--api-base-url":
                case "--api":
                    command.ApiBaseUrl = ReadValue(tokens, ref i, token);
                    command.ApiBaseUrlSpecified = true;
                    break;
                case "--comment":
                case "-m":
                    command.Comment = ReadValue(tokens, ref i, token);
                    break;
                case "--token":
                    command.AccessToken = ReadValue(tokens, ref i, token);
                    break;
                case "--token-env":
                    command.AccessTokenEnvironmentVariable = ReadValue(tokens, ref i, token);
                    break;
                case "--changeset-id":
                    command.ChangesetId = ParseNonNegativeLong(ReadValue(tokens, ref i, token), token);
                    break;
                case "--feature-id":
                    AddFeatureIds(command, ReadValue(tokens, ref i, token));
                    break;
                case "--tag":
                    command.TagFilters.Add(ParseTagFilter(ReadValue(tokens, ref i, token)));
                    break;
                case "--max-features":
                    command.MaxFeatures = ParsePositiveInt(ReadValue(tokens, ref i, token), token);
                    break;
                case "--max-coordinates":
                    command.MaxCoordinates = ParsePositiveInt(ReadValue(tokens, ref i, token), token);
                    break;
                case "--force":
                case "-f":
                    command.Force = true;
                    break;
                case "--yes":
                case "-y":
                    command.ConfirmWrite = true;
                    break;
                case "--dry-run":
                    command.DryRun = true;
                    break;
                case "--treat-input-as-new":
                    command.TreatInputAsNew = true;
                    break;
                default:
                    if (token.StartsWith("-", StringComparison.Ordinal)) {
                        throw new CliArgumentException($"Unknown option '{token}'.");
                    }
                    positionals.Add(token);
                    break;
            }
        }

        ApplyPositionals(command, positionals);
    }

    private static void ApplyPositionals(CliCommandLine command, IReadOnlyList<string> positionals) {
        switch (command.Kind) {
            case CliCommandKind.Import:
                command.InputPath ??= positionals.ElementAtOrDefault(0);
                break;
            case CliCommandKind.Convert:
                command.InputPath ??= positionals.ElementAtOrDefault(0);
                command.OutputPath ??= positionals.ElementAtOrDefault(1);
                break;
            case CliCommandKind.Download:
                if (command.Bounds is null && positionals.Count > 0) command.Bounds = ParseBounds(positionals[0]);
                command.OutputPath ??= positionals.ElementAtOrDefault(1);
                break;
            case CliCommandKind.Changeset:
                command.InputPath ??= positionals.ElementAtOrDefault(0);
                command.OutputPath ??= positionals.ElementAtOrDefault(1);
                break;
            case CliCommandKind.Upload:
                command.InputPath ??= positionals.ElementAtOrDefault(0);
                break;
        }
    }

    private static void Validate(CliCommandLine command) {
        if (command.Kind == CliCommandKind.Help) return;

        switch (command.Kind) {
            case CliCommandKind.Import:
                Require(command.InputPath, "import requires --input.");
                break;
            case CliCommandKind.Convert:
                Require(command.InputPath, "convert requires --input.");
                Require(command.OutputPath, "convert requires --output.");
                break;
            case CliCommandKind.Download:
                if (command.Bounds is null) throw new CliArgumentException("download requires --bbox minLon,minLat,maxLon,maxLat.");
                Require(command.OutputPath, "download requires --output.");
                break;
            case CliCommandKind.Changeset:
                Require(command.InputPath, "changeset requires --input.");
                Require(command.OutputPath, "changeset requires --output.");
                break;
            case CliCommandKind.Upload:
                Require(command.InputPath, "upload requires --input.");
                break;
        }
    }

    private static string ReadValue(IReadOnlyList<string> tokens, ref int index, string optionName) {
        if (index + 1 >= tokens.Count) throw new CliArgumentException($"{optionName} requires a value.");
        index++;
        return tokens[index];
    }

    private static GeoBounds ParseBounds(string value) {
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) {
            throw new CliArgumentException("Bounding boxes must use minLon,minLat,maxLon,maxLat.");
        }

        var numbers = new double[4];
        for (var i = 0; i < parts.Length; i++) {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i])) {
                throw new CliArgumentException($"Bounding box value '{parts[i]}' is not a valid number.");
            }
        }

        return new GeoBounds(numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    private static int ParsePositiveInt(string value, string optionName) {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number <= 0) {
            throw new CliArgumentException($"{optionName} must be a positive integer.");
        }

        return number;
    }

    private static long ParseNonNegativeLong(string value, string optionName) {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < 0) {
            throw new CliArgumentException($"{optionName} must be a non-negative integer.");
        }

        return number;
    }

    private static void AddFeatureIds(CliCommandLine command, string value) {
        foreach (var featureId in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
            command.FeatureIds.Add(featureId);
        }
    }

    private static CliTagFilter ParseTagFilter(string value) {
        var separator = value.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1) {
            throw new CliArgumentException("--tag must use key=value.");
        }

        return new CliTagFilter(value[..separator], value[(separator + 1)..]);
    }

    private static void Require(string? value, string message) {
        if (string.IsNullOrWhiteSpace(value)) throw new CliArgumentException(message);
    }

    private static bool IsHelp(string value) {
        return value.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("/?", StringComparison.OrdinalIgnoreCase);
    }
}
