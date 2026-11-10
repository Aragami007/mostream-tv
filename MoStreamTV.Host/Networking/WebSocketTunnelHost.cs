using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MoStreamTV.Host.Networking;

internal sealed partial class WebSocketTunnelHost : IAsyncDisposable
{
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private const string BrowserInfoHtml =
        "<!doctype html><html><body style=\"font-family:Segoe UI,sans-serif;padding:24px\">" +
        "<h2>MoStream TV Watch Party Tunnel</h2>" +
        "<p>Copy this address (using <b>wss://</b>) and click <b>Paste invite</b> under <b>Join watch party</b> in MoStream TV.</p>" +
        "</body></html>";

    private readonly CloudflaredRuntime _runtime;
    private readonly List<string> _recentOutput = [];
    private readonly object _outputLock = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _sessionCts;
    private Task? _acceptLoopTask;
    private Process? _process;
    private int _targetPort;
    private bool _stopping;

    public WebSocketTunnelHost(CloudflaredRuntime runtime)
    {
        _runtime = runtime;
    }

    public bool IsRunning => _process is { HasExited: false } && _listener is not null;
    public string PublicWssUrl { get; private set; } = string.Empty;

    public event EventHandler<string>? TunnelExited;

    [GeneratedRegex(@"https://[a-z0-9-]+\.trycloudflare\.com", RegexOptions.IgnoreCase)]
    private static partial Regex TryCloudflareUrlRegex();

    public async Task<string> StartAsync(
        int targetPort,
        Action<string>? statusCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            return PublicWssUrl;

        var cloudflaredExe = await _runtime.EnsureAvailableAsync(statusCallback, cancellationToken);
        statusCallback?.Invoke("Opening Internet watch party tunnel...");

        _stopping = false;
        _targetPort = targetPort;
        PublicWssUrl = string.Empty;
        lock (_outputLock)
            _recentOutput.Clear();

        var cts = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var bridgePort = ((IPEndPoint)listener.LocalEndpoint).Port;

        _sessionCts = cts;
        _listener = listener;
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(listener, cts.Token), CancellationToken.None);

        var urlTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registeredTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var startInfo = new ProcessStartInfo
        {
            FileName = cloudflaredExe,
            WorkingDirectory = Path.GetDirectoryName(cloudflaredExe) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("tunnel");
        startInfo.ArgumentList.Add("--url");
        startInfo.ArgumentList.Add($"http://127.0.0.1:{bridgePort}");
        startInfo.ArgumentList.Add("--no-autoupdate");

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        void HandleLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            lock (_outputLock)
            {
                _recentOutput.Add(line);
                if (_recentOutput.Count > 20)
                    _recentOutput.RemoveAt(0);
            }

            var match = TryCloudflareUrlRegex().Match(line);
            if (match.Success)
                urlTcs.TrySetResult(match.Value);

            if (line.Contains("Registered tunnel connection", StringComparison.OrdinalIgnoreCase))
                registeredTcs.TrySetResult(true);
        }

        process.OutputDataReceived += (_, args) => HandleLine(args.Data);
        process.ErrorDataReceived += (_, args) => HandleLine(args.Data);
        process.Exited += (_, _) =>
        {
            var tail = GetRecentOutputTail();
            urlTcs.TrySetException(new InvalidOperationException(
                string.IsNullOrWhiteSpace(tail)
                    ? $"The Internet tunnel exited unexpectedly (code {process.ExitCode})."
                    : $"The Internet tunnel could not start: {tail}"));
            registeredTcs.TrySetResult(false);

            if (!_stopping && ReferenceEquals(_process, process))
            {
                var summary = string.IsNullOrWhiteSpace(tail)
                    ? $"Internet tunnel closed (exit code {process.ExitCode})."
                    : $"Internet tunnel closed: {tail}";
                TunnelExited?.Invoke(this, summary);
            }
        };

        if (!process.Start())
        {
            await StopAsync();
            throw new InvalidOperationException("Windows could not start the built-in Internet tunnel (cloudflared).");
        }

        _process = process;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(25));
            using var reg = timeoutCts.Token.Register(() =>
                urlTcs.TrySetException(new TimeoutException(
                    "Timed out creating an Internet tunnel. Check your internet connection or uncheck 'Share over Internet' for local Wi-Fi.")));

            var httpsUrl = await urlTcs.Task;
            var wssUrl = $"wss://{new Uri(httpsUrl).Host}";
            PublicWssUrl = wssUrl;

            // Give Cloudflare edge a brief window to finish registering and propagating the tunnel route.
            var completed = await Task.WhenAny(registeredTcs.Task, Task.Delay(TimeSpan.FromSeconds(4), timeoutCts.Token));
            if (completed == registeredTcs.Task && await registeredTcs.Task)
                await Task.Delay(TimeSpan.FromMilliseconds(900), timeoutCts.Token);
            return wssUrl;
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        _stopping = true;
        PublicWssUrl = string.Empty;

        var cts = _sessionCts;
        _sessionCts = null;
        var listener = _listener;
        _listener = null;
        var acceptTask = _acceptLoopTask;
        _acceptLoopTask = null;
        var process = _process;
        _process = null;

        try { cts?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }

        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token);
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        if (acceptTask is not null)
        {
            try { await acceptTask; } catch { }
        }

        cts?.Dispose();
        _stopping = false;
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? incoming = null;
            try
            {
                incoming = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleBridgeClientAsync(incoming, _targetPort, cancellationToken), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                incoming?.Dispose();
                break;
            }
            catch (SocketException)
            {
                incoming?.Dispose();
                if (cancellationToken.IsCancellationRequested)
                    break;
            }
            catch
            {
                incoming?.Dispose();
            }
        }
    }

    private static async Task HandleBridgeClientAsync(TcpClient incomingClient, int targetPort, CancellationToken cancellationToken)
    {
        using (incomingClient)
        {
            incomingClient.NoDelay = true;
            await using var clientStream = incomingClient.GetStream();

            var headerText = await ReadHttpHeaderAsync(clientStream, cancellationToken);
            if (string.IsNullOrEmpty(headerText))
                return;

            var wsKey = ExtractWebSocketKey(headerText);
            if (string.IsNullOrEmpty(wsKey))
            {
                var bodyBytes = Encoding.UTF8.GetBytes(BrowserInfoHtml);
                var httpResponse =
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: text/html; charset=utf-8\r\n" +
                    $"Content-Length: {bodyBytes.Length}\r\n" +
                    "Connection: close\r\n\r\n";
                var headerBytes = Encoding.ASCII.GetBytes(httpResponse);
                await clientStream.WriteAsync(headerBytes, cancellationToken);
                await clientStream.WriteAsync(bodyBytes, cancellationToken);
                await clientStream.FlushAsync(cancellationToken);
                return;
            }

            var acceptHash = SHA1.HashData(Encoding.ASCII.GetBytes(wsKey + WebSocketGuid));
            var acceptValue = Convert.ToBase64String(acceptHash);
            var upgradeResponse =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {acceptValue}\r\n\r\n";
            var upgradeBytes = Encoding.ASCII.GetBytes(upgradeResponse);
            await clientStream.WriteAsync(upgradeBytes, cancellationToken);
            await clientStream.FlushAsync(cancellationToken);

            using var webSocket = WebSocket.CreateFromStream(
                clientStream,
                isServer: true,
                subProtocol: null,
                keepAliveInterval: TimeSpan.FromSeconds(20));

            using var backendClient = new TcpClient();
            await backendClient.ConnectAsync(IPAddress.Loopback, targetPort, cancellationToken);

            await WebSocketStreamRelay.RelayAsync(webSocket, backendClient, cancellationToken);
        }
    }

    private static async Task<string?> ReadHttpHeaderAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var headerBytes = new List<byte>(512);
        var singleByte = new byte[1];
        while (headerBytes.Count < 16384)
        {
            var read = await stream.ReadAsync(singleByte, cancellationToken);
            if (read <= 0)
                return null;

            headerBytes.Add(singleByte[0]);
            var count = headerBytes.Count;
            if (count >= 4
                && headerBytes[count - 4] == '\r'
                && headerBytes[count - 3] == '\n'
                && headerBytes[count - 2] == '\r'
                && headerBytes[count - 1] == '\n')
            {
                return Encoding.ASCII.GetString([.. headerBytes]);
            }
        }

        return null;
    }

    private static string? ExtractWebSocketKey(string headerText)
    {
        foreach (var line in headerText.Split(["\r\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                return line["Sec-WebSocket-Key:".Length..].Trim();
        }

        return null;
    }

    private string GetRecentOutputTail()
    {
        lock (_outputLock)
            return string.Join(" | ", _recentOutput.TakeLast(4));
    }
}
