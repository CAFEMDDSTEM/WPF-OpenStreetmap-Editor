using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace WPF_OpenStreetmap_Editor.Services;

public enum OsmAuthenticationMethod {
    OAuth2,
    BasicPassword
}

public sealed record OsmAccountCredential(
    OsmAuthenticationMethod Method,
    string UserName,
    string Secret) {
    public void ApplyTo(HttpRequestMessage request) {
        if (string.IsNullOrWhiteSpace(Secret)) {
            throw new InvalidDataException(Method == OsmAuthenticationMethod.BasicPassword
                ? "OSM 账号缺少密码。"
                : "OSM 账号缺少访问令牌。");
        }

        request.Headers.Authorization = Method switch {
            OsmAuthenticationMethod.OAuth2 => new AuthenticationHeaderValue("Bearer", Secret.Trim()),
            OsmAuthenticationMethod.BasicPassword => CreateBasicHeader(),
            _ => throw new InvalidDataException("不支持的 OSM 认证方式。")
        };
    }

    private AuthenticationHeaderValue CreateBasicHeader() {
        if (string.IsNullOrWhiteSpace(UserName)) throw new InvalidDataException("OSM 账号缺少用户名。");
        var bytes = Encoding.UTF8.GetBytes($"{UserName}:{Secret}");
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
    }
}

public static class OsmAuthenticationMethodDisplay {
    public static string GetName(OsmAuthenticationMethod method) {
        return method switch {
            OsmAuthenticationMethod.OAuth2 => "OAuth 2.0",
            OsmAuthenticationMethod.BasicPassword => "账号密码",
            _ => "未知"
        };
    }
}

public sealed record OsmAuthorizationRequest(
    string ClientId,
    string RedirectUri,
    IReadOnlyList<string> Scopes);

public sealed record OsmAuthorizationResult(
    string DisplayName,
    string AccessToken,
    DateTimeOffset? ExpiresAt);

public interface IOsmAuthorizationLoginService {
    Task<OsmAuthorizationResult> SignInAsync(
        OsmAccount account,
        OsmAuthorizationRequest request,
        CancellationToken ct = default);
}
