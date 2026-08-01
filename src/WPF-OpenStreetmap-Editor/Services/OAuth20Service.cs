using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WPF_OpenStreetmap_Editor.Services;

/// <summary>
/// OAuth 2.0 Authorization Code + PKCE (S256) flow against the OpenStreetMap OAuth
/// endpoint, with a temporary loopback HTTP listener (like JOSM). The user is never
/// asked to copy links or tokens: the default browser is opened, the authorization
/// callback is received locally, and the code is exchanged for an access token.
/// </summary>
public sealed class OAuth20Service : IOsmAuthorizationLoginService {
    public const string AuthorizeEndpoint = "https://www.openstreetmap.org/oauth2/authorize";
    public const string TokenEndpoint = "https://www.openstreetmap.org/oauth2/token";
    public const string CallbackPath = "/oauth_authorization";
    public const int DefaultPort = 8111;
    public const int MaxPortAttempts = 10;
    public const string DefaultClientId = "8aNtmJVOyAGmqmcNugbM3m35K33-nlfv2_fm0Q-bYJM";
    public const string DefaultRedirectUri = "http://127.0.0.1:8111/oauth_authorization";

    internal static readonly TimeSpan CallbackTimeout = TimeSpan.FromMinutes(2);

    public static readonly IReadOnlyList<string> DefaultScopes = [
        "read_prefs", "write_prefs", "write_api", "read_gpx", "write_gpx", "write_notes"
    ];

    private readonly HttpClient _httpClient;

    public OAuth20Service(HttpClient? httpClient = null) {
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any()) {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WPF-OpenStreetmap-Editor/1.0");
        }
    }

    public async Task<OsmAuthorizationResult> SignInAsync(
        OsmAccount account,
        OsmAuthorizationRequest request,
        CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(request.ClientId)) {
            throw new InvalidDataException(Loc("Osm.Auth.MissingClientId"));
        }

        var state = CreateRandomToken();
        var codeVerifier = CreateRandomToken();
        var codeChallenge = CreateCodeChallenge(codeVerifier);

        using var server = StartListener(request);
        OpenBrowser(BuildAuthorizeUrl(request, server.RedirectUri, state, codeChallenge));
        try {
            var code = await ReceiveAuthorizationCodeAsync(server, state, ct);
            var accessToken = await ExchangeCodeAsync(request, server.RedirectUri, code, codeVerifier, ct);
            var displayName = await FetchDisplayNameAsync(account, accessToken, ct);
            return new OsmAuthorizationResult(displayName, accessToken, ExpiresAt: null);
        } finally {
            server.Dispose();
        }
    }

    private static string BuildAuthorizeUrl(
        OsmAuthorizationRequest request,
        string redirectUri,
        string state,
        string codeChallenge) {
        var scopes = request.Scopes is { Count: > 0 } ? request.Scopes : DefaultScopes;
        var query = string.Join('&', new[] {
            "response_type=code",
            $"client_id={Uri.EscapeDataString(request.ClientId.Trim())}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            $"scope={Uri.EscapeDataString(string.Join(' ', scopes))}",
            $"state={Uri.EscapeDataString(state)}",
            "code_challenge_method=S256",
            $"code_challenge={Uri.EscapeDataString(codeChallenge)}"
        });
        return $"{AuthorizeEndpoint}?{query}";
    }

    private static OAuthCallbackServer StartListener(OsmAuthorizationRequest request) {
        if (!string.IsNullOrWhiteSpace(request.RedirectUri)) {
            var uri = new Uri(request.RedirectUri);
            if (!uri.IsLoopback) {
                throw new InvalidDataException(Loc("Osm.Auth.InvalidRedirectUri"));
            }
            try {
                return OAuthCallbackServer.Start(uri.Port, uri.GetLeftPart(UriPartial.Path));
            } catch (SocketException) {
                throw new InvalidDataException(Loc("Osm.Auth.PortInUse", uri.Port));
            }
        }

        var startPort = request.Port;
        for (var attempt = 0; attempt < MaxPortAttempts; attempt++) {
            try {
                var port = startPort + attempt;
                return OAuthCallbackServer.Start(port, $"http://127.0.0.1:{port}{CallbackPath}");
            } catch (SocketException) when (attempt + 1 < MaxPortAttempts) {
                // The port is occupied; try the next one.
            }
        }
        throw new InvalidDataException(Loc("Osm.Auth.PortInUse", startPort));
    }

    private static async Task<string> ReceiveAuthorizationCodeAsync(
        OAuthCallbackServer server,
        string expectedState,
        CancellationToken ct) {
        using var session = await server.WaitForCallbackAsync(expectedState, ct);
        var parameters = session.Query;
        if (!string.IsNullOrEmpty(parameters.GetValueOrDefault("error"))) {
            session.Respond(400, "Authorization denied", Loc("Osm.Auth.AuthorizationDenied"));
            throw new InvalidDataException(Loc("Osm.Auth.AuthorizationDenied"));
        }
        var code = parameters.GetValueOrDefault("code");
        if (string.IsNullOrWhiteSpace(code)) {
            session.Respond(400, "Bad Request", Loc("Osm.Auth.MissingAuthorizationCode"));
            throw new InvalidDataException(Loc("Osm.Auth.MissingAuthorizationCode"));
        }
        session.Respond(200, "Success", "Authorization complete. You can close this tab and return to the application.");
        return code;
    }

    private async Task<string> ExchangeCodeAsync(
        OsmAuthorizationRequest request,
        string redirectUri,
        string code,
        string codeVerifier,
        CancellationToken ct) {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["grant_type"] = "authorization_code",
            ["client_id"] = request.ClientId.Trim(),
            ["redirect_uri"] = redirectUri,
            ["code"] = code,
            ["code_verifier"] = codeVerifier
        };
        if (!string.IsNullOrWhiteSpace(request.ClientSecret)) {
            parameters["client_secret"] = request.ClientSecret.Trim();
        }

        using var content = new FormUrlEncodedContent(parameters);
        using var response = await _httpClient.PostAsync(TokenEndpoint, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = TryParseJson(body);
        if (!response.IsSuccessStatusCode) {
            var detail = json?["error_description"]?.GetValue<string>()
                ?? json?["error"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(detail)) {
                detail = string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)response.StatusCode}" : TrimErrorBody(body);
            }
            throw new InvalidDataException(Loc("Osm.Auth.TokenExchangeFailed", detail));
        }
        var accessToken = json?["access_token"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(accessToken)) {
            throw new InvalidDataException(Loc("Osm.Auth.TokenExchangeFailed", "missing access_token"));
        }
        return accessToken;
    }

    private static string TrimErrorBody(string body) {
        var text = body.Trim();
        return text.Length > 200 ? text[..200] + "…" : text;
    }

    private async Task<string> FetchDisplayNameAsync(OsmAccount account, string accessToken, CancellationToken ct) {
        return await new OsmApiClient(_httpClient).GetUserDisplayNameAsync(account.ApiBaseUrl, accessToken, ct);
    }

    private static void OpenBrowser(string url) {
        try {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                FileName = url,
                UseShellExecute = true
            });
        } catch (Exception ex) {
            throw new InvalidDataException(Loc("Osm.Auth.OpenBrowserFailed", ex.Message));
        }
    }

    private static string CreateRandomToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string CreateCodeChallenge(string codeVerifier) =>
        Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static JsonNode? TryParseJson(string json) {
        try {
            return JsonNode.Parse(json);
        } catch (JsonException) {
            return null;
        }
    }

    internal static string Loc(string key, params object?[] args) =>
        LocalizationService.Instance.Format(key, args);
}

/// <summary>
/// Minimal loopback HTTP server that receives exactly one authorization callback.
/// A plain <see cref="TcpListener"/> is used (like JOSM) because it requires no
/// URL ACL registration and never needs administrator rights on Windows.
/// </summary>
internal sealed class OAuthCallbackServer : IDisposable {
    private readonly TcpListener _listener;

    private OAuthCallbackServer(TcpListener listener, string redirectUri) {
        _listener = listener;
        RedirectUri = redirectUri;
    }

    public string RedirectUri { get; }

    public static OAuthCallbackServer Start(int port, string redirectUri) {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        return new OAuthCallbackServer(listener, redirectUri);
    }

    public async Task<OAuthCallbackSession> WaitForCallbackAsync(string expectedState, CancellationToken ct) {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(OAuth20Service.CallbackTimeout);
        try {
            while (true) {
                var client = await _listener.AcceptTcpClientAsync(timeoutCts.Token);
                var session = new OAuthCallbackSession(client);
                if (session.IsCallback(expectedState)) {
                    return session;
                }
                session.Respond(404, "Not Found", "This is not an OpenStreetMap authorization callback.");
                session.Dispose();
            }
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            throw new InvalidDataException(OAuth20Service.Loc("Osm.Auth.NoCallbackReceived"));
        }
    }

    public void Dispose() {
        try {
            _listener.Stop();
        } catch {
            // Ignore shutdown errors.
        }
    }
}

internal sealed class OAuthCallbackSession : IDisposable {
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private bool _responded;

    public OAuthCallbackSession(TcpClient client) {
        _client = client;
        _stream = client.GetStream();
        Query = ReadRequest();
    }

    public IReadOnlyDictionary<string, string> Query { get; }

    public bool IsCallback(string expectedState) {
        return Query.TryGetValue("state", out var state) &&
            string.Equals(state, expectedState, StringComparison.Ordinal);
    }

    private IReadOnlyDictionary<string, string> ReadRequest() {
        var request = ReadHttpRequestHead();
        var pathAndQuery = GetRequestPath(request);
        var separator = pathAndQuery.IndexOf('?');
        return separator < 0 ? new Dictionary<string, string>() : ParseQuery(pathAndQuery[(separator + 1)..]);
    }

    public void Respond(int statusCode, string title, string message) {
        if (_responded) return;
        _responded = true;
        var titleEscaped = EscapeHtml(title);
        var messageEscaped = EscapeHtml(message);
        var body = $"<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>{titleEscaped}</title></head><body><h1>{titleEscaped}</h1><p>{messageEscaped}</p></body></html>";
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var statusLine = statusCode switch {
            200 => "HTTP/1.1 200 OK",
            400 => "HTTP/1.1 400 Bad Request",
            _ => $"HTTP/1.1 {statusCode}"
        };
        var header = $"{statusLine}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        try {
            _stream.Write(headerBytes, 0, headerBytes.Length);
            _stream.Write(bodyBytes, 0, bodyBytes.Length);
            _stream.Flush();
        } catch (IOException) {
            // The browser may have already closed the connection.
        }
    }

    public void Dispose() {
        _stream.Dispose();
        _client.Dispose();
    }

    private string ReadHttpRequestHead() {
        using var buffer = new MemoryStream();
        var oneByte = new byte[1];
        while (buffer.Length < 8192) {
            var read = _stream.Read(oneByte, 0, 1);
            if (read == 0) break;
            buffer.WriteByte(oneByte[0]);
            var tail = buffer.GetBuffer();
            var length = (int)buffer.Length;
            if (length >= 4 &&
                tail[length - 4] == '\r' && tail[length - 3] == '\n' &&
                tail[length - 2] == '\r' && tail[length - 1] == '\n') {
                break;
            }
        }
        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    private static string GetRequestPath(string requestHead) {
        var firstLine = requestHead.Split('\n', 2)[0].TrimEnd('\r');
        var parts = firstLine.Split(' ', 3);
        return parts.Length >= 2 ? parts[1] : "/";
    }

    private static IReadOnlyDictionary<string, string> ParseQuery(string query) {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries)) {
            var equalsIndex = part.IndexOf('=');
            if (equalsIndex < 0) {
                result[Uri.UnescapeDataString(part)] = "";
            } else {
                result[Uri.UnescapeDataString(part[..equalsIndex])] =
                    Uri.UnescapeDataString(part[(equalsIndex + 1)..]);
            }
        }
        return result;
    }

    private static string EscapeHtml(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
