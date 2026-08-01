using System.Net;
using System.Net.Sockets;
using System.Text;
using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public class OAuth20ServiceTests {
    [Fact]
    public async Task CallbackServer_ReturnsSessionWhenStateMatches() {
        var port = GetFreePort();
        using var server = OAuthCallbackServer.Start(port, $"http://127.0.0.1:{port}/oauth_authorization");
        var receiveTask = server.WaitForCallbackAsync("expected-state", CancellationToken.None);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var request = "GET /oauth_authorization?state=expected-state&code=abc123 HTTP/1.1\r\n" +
            $"Host: 127.0.0.1:{port}\r\nConnection: close\r\n\r\n";
        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(request));

        using var session = await receiveTask;
        Assert.True(session.IsCallback("expected-state"));
        Assert.Equal("abc123", session.Query["code"]);
        Assert.Equal("expected-state", session.Query["state"]);
    }

    [Fact]
    public async Task CallbackServer_IgnoresUnrelatedRequestsAndWaitsForMatchingOne() {
        var port = GetFreePort();
        using var server = OAuthCallbackServer.Start(port, $"http://127.0.0.1:{port}/oauth_authorization");
        var receiveTask = server.WaitForCallbackAsync("expected-state", CancellationToken.None);

        using (var first = new TcpClient()) {
            await first.ConnectAsync(IPAddress.Loopback, port);
            var faviconRequest = "GET /favicon.ico HTTP/1.1\r\n" +
                $"Host: 127.0.0.1:{port}\r\nConnection: close\r\n\r\n";
            await first.GetStream().WriteAsync(Encoding.ASCII.GetBytes(faviconRequest));
        }

        using (var second = new TcpClient()) {
            await second.ConnectAsync(IPAddress.Loopback, port);
            var callbackRequest = "GET /oauth_authorization?state=expected-state&code=xyz HTTP/1.1\r\n" +
                $"Host: 127.0.0.1:{port}\r\nConnection: close\r\n\r\n";
            await second.GetStream().WriteAsync(Encoding.ASCII.GetBytes(callbackRequest));
        }

        using var session = await receiveTask;
        Assert.True(session.IsCallback("expected-state"));
        Assert.Equal("xyz", session.Query["code"]);
    }

    private static int GetFreePort() {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
