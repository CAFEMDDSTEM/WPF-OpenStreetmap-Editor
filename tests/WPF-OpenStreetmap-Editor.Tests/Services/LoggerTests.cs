using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class LoggerTests {
    [Fact]
    public void RedactSensitiveData_RemovesQueryJsonAndHeaderSecrets() {
        const string secret = "super-secret";
        var message = $"https://example.test/tile?access_token={secret}&x=1 " +
            $"{{\"AccessToken\":\"{secret}\"}} Authorization: Bearer {secret}";

        var redacted = Logger.RedactSensitiveData(message);

        Assert.DoesNotContain(secret, redacted, StringComparison.Ordinal);
        Assert.Contains("access_token=***", redacted, StringComparison.Ordinal);
        Assert.Contains("\"AccessToken\":\"***\"", redacted, StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer ***", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactSensitiveData_HandlesDollarCharactersWithoutReplacementExpansion() {
        var redacted = Logger.RedactSensitiveData("https://example.test/?api_key=abc$1xyz");

        Assert.Equal("https://example.test/?api_key=***", redacted);
    }
}
