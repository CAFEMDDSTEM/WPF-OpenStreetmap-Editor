using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace WPF_OpenStreetmap_Editor.Services;

public enum AppUpdateCheckState {
    UpToDate,
    UpdateAvailable,
    Unavailable
}

public sealed record AppReleaseInfo(
    string Version,
    string Name,
    string ReleaseUrl,
    bool IsPrerelease,
    DateTimeOffset? PublishedAt);

public sealed record AppUpdateCheckResult(
    AppUpdateCheckState State,
    string CurrentVersion,
    AppReleaseInfo? LatestRelease,
    string Detail) {
    public bool IsUpdateAvailable => State == AppUpdateCheckState.UpdateAvailable && LatestRelease is not null;
}

public sealed class AppUpdateService : IDisposable {
    internal const string DefaultReleasesApiUrl =
        "https://api.github.com/repos/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/releases";
    internal const string DefaultReleasesPageUrl =
        "https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/releases";
    private const string DefaultUserAgent = "WPF-OpenStreetmap-Editor/0.1";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(4);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly string _releasesApiUrl;
    private readonly TimeSpan _timeout;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public AppUpdateService(
        HttpClient? http = null,
        string releasesApiUrl = DefaultReleasesApiUrl,
        TimeSpan? timeout = null) {
        _ownsHttpClient = http is null;
        _http = http ?? new HttpClient {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _releasesApiUrl = releasesApiUrl;
        _timeout = timeout ?? DefaultTimeout;

        if (!_http.DefaultRequestHeaders.UserAgent.Any()) {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent);
        }
    }

    public Task<AppUpdateCheckResult> CheckCurrentAssemblyAsync(CancellationToken ct = default) {
        return CheckAsync(GetCurrentVersion(), ct);
    }

    public async Task<AppUpdateCheckResult> CheckAsync(string currentVersion, CancellationToken ct = default) {
        var l = LocalizationService.Instance;
        if (!SemanticVersion.TryParse(currentVersion, out var parsedCurrent)) {
            return new AppUpdateCheckResult(
                AppUpdateCheckState.Unavailable,
                currentVersion,
                null,
                l.Format("Update.CurrentVersionInvalid", currentVersion));
        }

        try {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_timeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, _releasesApiUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) {
                return new AppUpdateCheckResult(
                    AppUpdateCheckState.Unavailable,
                    currentVersion,
                    null,
                    l.Format("Update.HttpFailed", (int)response.StatusCode, response.ReasonPhrase));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
            var releases = await JsonSerializer
                .DeserializeAsync<GitHubRelease[]>(stream, JsonOptions, timeoutCts.Token)
                .ConfigureAwait(false) ?? [];
            var latest = GetLatestRelease(releases);
            if (latest is null) {
                return new AppUpdateCheckResult(
                    AppUpdateCheckState.Unavailable,
                    currentVersion,
                    null,
                    l.GetString("Update.NoRelease"));
            }

            return IsNewerVersion(latest.Version, currentVersion)
                ? new AppUpdateCheckResult(
                    AppUpdateCheckState.UpdateAvailable,
                    currentVersion,
                    latest,
                    l.Format("Update.Available", latest.Version))
                : new AppUpdateCheckResult(
                    AppUpdateCheckState.UpToDate,
                    currentVersion,
                    latest,
                    l.Format("Update.UpToDate", currentVersion));
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw;
        } catch (OperationCanceledException) {
            return new AppUpdateCheckResult(
                AppUpdateCheckState.Unavailable,
                currentVersion,
                null,
                l.GetString("Update.Timeout"));
        } catch (JsonException ex) {
            return new AppUpdateCheckResult(
                AppUpdateCheckState.Unavailable,
                currentVersion,
                null,
                l.Format("Update.JsonFailed", ex.Message));
        } catch (Exception unsafeException) {
            var message = Logger.RedactSensitiveData(unsafeException.Message);
            return new AppUpdateCheckResult(
                AppUpdateCheckState.Unavailable,
                currentVersion,
                null,
                l.Format("Update.GenericFailed", message));
        }
    }

    public void Dispose() {
        if (_disposed) return;

        _disposed = true;
        if (_ownsHttpClient) {
            _http.Dispose();
        }
    }

    internal static bool IsNewerVersion(string candidateVersion, string currentVersion) {
        return SemanticVersion.TryParse(candidateVersion, out var candidate) &&
            SemanticVersion.TryParse(currentVersion, out var current) &&
            candidate.CompareTo(current) > 0;
    }

    internal static string GetCurrentVersion(Assembly? assembly = null) {
        return HelpContentService.GetVersionText(assembly ?? typeof(AppUpdateService).Assembly);
    }

    private static AppReleaseInfo? GetLatestRelease(GitHubRelease[] releases) {
        return releases
            .Where(static release => !release.Draft)
            .Select(static release => TryCreateParsedRelease(release, out var parsed) ? parsed : null)
            .OfType<ParsedRelease>()
            .OrderByDescending(static release => release.Version)
            .FirstOrDefault()
            ?.Info;
    }

    private static bool TryCreateParsedRelease(GitHubRelease release, out ParsedRelease? parsed) {
        parsed = null;
        var versionText = release.TagName;
        if (string.IsNullOrWhiteSpace(versionText) ||
            !SemanticVersion.TryParse(versionText, out var version)) {
            return false;
        }

        parsed = new ParsedRelease(
            version,
            new AppReleaseInfo(
                versionText,
                string.IsNullOrWhiteSpace(release.Name) ? versionText : release.Name,
                string.IsNullOrWhiteSpace(release.HtmlUrl) ? DefaultReleasesPageUrl : release.HtmlUrl,
                release.Prerelease,
                release.PublishedAt));
        return true;
    }

    private sealed record ParsedRelease(SemanticVersion Version, AppReleaseInfo Info);

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt);

    private sealed record SemanticVersion(
        int Major,
        int Minor,
        int Patch,
        string? Prerelease) : IComparable<SemanticVersion> {
        public int CompareTo(SemanticVersion? other) {
            if (other is null) return 1;

            var core = Major.CompareTo(other.Major);
            if (core != 0) return core;

            core = Minor.CompareTo(other.Minor);
            if (core != 0) return core;

            core = Patch.CompareTo(other.Patch);
            if (core != 0) return core;

            if (string.IsNullOrWhiteSpace(Prerelease)) {
                return string.IsNullOrWhiteSpace(other.Prerelease) ? 0 : 1;
            }

            if (string.IsNullOrWhiteSpace(other.Prerelease)) return -1;

            return ComparePrerelease(Prerelease, other.Prerelease);
        }

        public static bool TryParse(string value, out SemanticVersion version) {
            version = default!;
            if (string.IsNullOrWhiteSpace(value)) return false;

            var trimmed = value.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 1) {
                trimmed = trimmed[1..];
            }

            var buildIndex = trimmed.IndexOf('+', StringComparison.Ordinal);
            if (buildIndex >= 0) {
                trimmed = trimmed[..buildIndex];
            }

            string? prerelease = null;
            var prereleaseIndex = trimmed.IndexOf('-', StringComparison.Ordinal);
            if (prereleaseIndex >= 0) {
                prerelease = trimmed[(prereleaseIndex + 1)..];
                trimmed = trimmed[..prereleaseIndex];
                if (string.IsNullOrWhiteSpace(prerelease)) return false;
            }

            var parts = trimmed.Split('.');
            if (parts.Length is < 2 or > 4) return false;
            if (!TryParsePart(parts[0], out var major) ||
                !TryParsePart(parts[1], out var minor)) {
                return false;
            }

            var patch = 0;
            if (parts.Length >= 3 && !TryParsePart(parts[2], out patch)) return false;

            version = new SemanticVersion(major, minor, patch, prerelease);
            return true;
        }

        private static int ComparePrerelease(string left, string right) {
            var leftParts = left.Split('.');
            var rightParts = right.Split('.');
            var count = Math.Max(leftParts.Length, rightParts.Length);
            for (var i = 0; i < count; i++) {
                if (i >= leftParts.Length) return -1;
                if (i >= rightParts.Length) return 1;

                var leftIsNumber = TryParsePart(leftParts[i], out var leftNumber);
                var rightIsNumber = TryParsePart(rightParts[i], out var rightNumber);
                if (leftIsNumber && rightIsNumber) {
                    var numberResult = leftNumber.CompareTo(rightNumber);
                    if (numberResult != 0) return numberResult;
                    continue;
                }

                if (leftIsNumber) return -1;
                if (rightIsNumber) return 1;

                var textResult = string.CompareOrdinal(leftParts[i], rightParts[i]);
                if (textResult != 0) return textResult;
            }

            return 0;
        }

        private static bool TryParsePart(string value, out int part) {
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out part) && part >= 0;
        }
    }
}
