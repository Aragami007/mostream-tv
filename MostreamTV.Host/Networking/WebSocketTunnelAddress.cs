using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;

namespace MostreamTV.Host.Networking;

internal static class WebSocketTunnelAddress
{
    private static readonly HttpClient SharedDohHttpClient = new() { Timeout = TimeSpan.FromSeconds(4) };

    public static bool LooksLikeTunnelAddress(string? rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
            return false;

        var trimmed = rawInput.Trim();
        return trimmed.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || ContainsTryCloudflareHost(trimmed);
    }

    public static bool TryNormalize(string? rawInput, out Uri? normalizedUri, out string? error)
    {
        normalizedUri = null;
        error = null;

        if (!LooksLikeTunnelAddress(rawInput))
            return false;

        var trimmed = rawInput!.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
            trimmed = $"wss://{trimmed}";

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) || string.IsNullOrWhiteSpace(parsed.Host))
        {
            error = "Enter a valid invite link (for example wss://example.trycloudflare.com).";
            return false;
        }

        var scheme = parsed.Scheme.ToLowerInvariant() switch
        {
            "https" or "wss" => "wss",
            "http" or "ws" => "ws",
            _ => string.Empty,
        };

        if (string.IsNullOrEmpty(scheme))
        {
            error = "Invite link must start with wss:// or https://.";
            return false;
        }

        var builder = new UriBuilder(parsed)
        {
            Scheme = scheme,
            Port = parsed.IsDefaultPort ? -1 : parsed.Port,
            Path = string.IsNullOrEmpty(parsed.AbsolutePath) ? "/" : parsed.AbsolutePath,
        };
        normalizedUri = builder.Uri;
        return true;
    }

    public static string FormatDisplayUrl(Uri uri)
    {
        var baseUrl = uri.IsDefaultPort
            ? $"{uri.Scheme}://{uri.Host}"
            : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        return uri.AbsolutePath is "/" or "" ? baseUrl : $"{baseUrl}{uri.AbsolutePath}";
    }

    public static async Task<IPAddress[]> ResolveIpv4AddressesAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
            return literal.AddressFamily == AddressFamily.InterNetwork ? [literal] : [];

        // New *.trycloudflare.com names often hit NXDOMAIN negative caches in ISP/Windows DNS
        // right after the host creates the tunnel, so try DoH (1.1.1.1 -> dns.google) first.
        if (host.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase))
        {
            var dohAddresses = await QueryDohAsync($"https://1.1.1.1/dns-query?name={Uri.EscapeDataString(host)}&type=A", cancellationToken);
            if (dohAddresses.Length > 0)
                return dohAddresses;

            dohAddresses = await QueryDohAsync($"https://dns.google/resolve?name={Uri.EscapeDataString(host)}&type=A", cancellationToken);
            if (dohAddresses.Length > 0)
                return dohAddresses;
        }

        try
        {
            var systemAddresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken);
            if (systemAddresses.Length > 0)
                return systemAddresses;
        }
        catch (SocketException)
        {
        }

        if (host.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase))
        {
            // All Cloudflare Quick Tunnels terminate at Cloudflare edge Anycast IPv4s;
            // SslStream + HTTP Host header still carry the exact subdomain.
            try
            {
                var edgeFallback = await Dns.GetHostAddressesAsync("trycloudflare.com", AddressFamily.InterNetwork, cancellationToken);
                if (edgeFallback.Length > 0)
                    return edgeFallback;
            }
            catch (SocketException)
            {
            }
        }

        return [];
    }

    private static async Task<IPAddress[]> QueryDohAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/dns-json");
            using var response = await SharedDohHttpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return [];

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("Answer", out var answers))
                return [];

            var results = new List<IPAddress>();
            foreach (var answer in answers.EnumerateArray())
            {
                if (answer.TryGetProperty("type", out var recordType)
                    && recordType.GetInt32() == 1
                    && answer.TryGetProperty("data", out var data)
                    && IPAddress.TryParse(data.GetString(), out var address)
                    && address.AddressFamily == AddressFamily.InterNetwork)
                {
                    results.Add(address);
                }
            }

            return [.. results];
        }
        catch
        {
            return [];
        }
    }

    private static bool ContainsTryCloudflareHost(string value)
    {
        var withoutPath = value.Split('/')[0];
        var hostOnly = withoutPath.Split(':')[0];
        return hostOnly.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class WebSocketStreamRelay
{
    public static async Task RelayAsync(WebSocket webSocket, TcpClient tcpClient, CancellationToken cancellationToken)
    {
        tcpClient.NoDelay = true;
        await using var networkStream = tcpClient.GetStream();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var wsToTcp = PumpWebSocketToTcpAsync(webSocket, tcpClient, networkStream, linkedCts.Token);
        var tcpToWs = PumpTcpToWebSocketAsync(webSocket, networkStream, linkedCts.Token);

        await Task.WhenAny(wsToTcp, tcpToWs);
        var remaining = wsToTcp.IsCompleted ? tcpToWs : wsToTcp;
        await Task.WhenAny(remaining, Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None));
        linkedCts.Cancel();

        try
        {
            await Task.WhenAll(wsToTcp, tcpToWs);
        }
        catch
        {
            // Ignore shutdown/abort exceptions during bidirectional teardown.
        }
    }

    private static async Task PumpWebSocketToTcpAsync(
        WebSocket webSocket,
        TcpClient tcpClient,
        NetworkStream networkStream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[32768];
        try
        {
            while (!cancellationToken.IsCancellationRequested
                && webSocket.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                var result = await webSocket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    try { tcpClient.Client.Shutdown(SocketShutdown.Send); } catch { }
                    if (webSocket.State == WebSocketState.CloseReceived)
                    {
                        using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        try { await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", closeCts.Token); } catch { }
                    }
                    break;
                }

                if (result.Count > 0)
                {
                    await networkStream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
                    if (result.EndOfMessage)
                        await networkStream.FlushAsync(cancellationToken);
                }
            }
        }
        catch (Exception ex) when (IsExpectedDisconnect(ex))
        {
        }
    }

    private static async Task PumpTcpToWebSocketAsync(
        WebSocket webSocket,
        NetworkStream networkStream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[32768];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await networkStream.ReadAsync(buffer, cancellationToken);
                if (bytesRead <= 0)
                {
                    if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    {
                        using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        try { await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "eof", closeCts.Token); } catch { }
                    }
                    break;
                }

                await webSocket.SendAsync(
                    buffer.AsMemory(0, bytesRead),
                    WebSocketMessageType.Binary,
                    endOfMessage: true,
                    cancellationToken);
            }
        }
        catch (Exception ex) when (IsExpectedDisconnect(ex))
        {
        }
    }

    private static bool IsExpectedDisconnect(Exception exception) =>
        exception is OperationCanceledException
            or IOException
            or SocketException
            or WebSocketException
            or ObjectDisposedException;
}
