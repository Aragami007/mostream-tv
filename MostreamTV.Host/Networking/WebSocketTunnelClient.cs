using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;

namespace MostreamTV.Host.Networking;

internal sealed class WebSocketTunnelClient : IAsyncDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _sessionCts;
    private HttpMessageInvoker? _invoker;
    private Task? _acceptLoopTask;
    private ClientWebSocket? _prewarmedSocket;

    public int LocalPort { get; private set; }

    public async Task<int> StartAsync(
        Uri targetUri,
        Action<string>? statusCallback = null,
        CancellationToken cancellationToken = default)
    {
        await StopAsync();

        _invoker = CreateInvoker();
        statusCallback?.Invoke($"Connecting to watch party tunnel ({WebSocketTunnelAddress.FormatDisplayUrl(targetUri)})...");

        // Pre-connect and verify the WebSocket route before launching SyncplayConsole so invalid
        // or newly created tunnel URLs fail cleanly or retry DNS/edge propagation up front.
        _prewarmedSocket = await ConnectWithRetryAsync(targetUri, _invoker, statusCallback, cancellationToken);

        var cts = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        LocalPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        _sessionCts = cts;
        _listener = listener;
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(listener, targetUri, cts.Token), CancellationToken.None);
        return LocalPort;
    }

    public async Task StopAsync()
    {
        LocalPort = 0;

        var prewarmed = Interlocked.Exchange(ref _prewarmedSocket, null);
        prewarmed?.Dispose();

        var cts = _sessionCts;
        _sessionCts = null;
        var listener = _listener;
        _listener = null;
        var acceptTask = _acceptLoopTask;
        _acceptLoopTask = null;
        var invoker = _invoker;
        _invoker = null;

        try { cts?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }

        if (acceptTask is not null)
        {
            try { await acceptTask; } catch { }
        }

        invoker?.Dispose();
        cts?.Dispose();
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task AcceptLoopAsync(TcpListener listener, Uri targetUri, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? localClient = null;
            try
            {
                localClient = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleLocalClientAsync(localClient, targetUri, cancellationToken), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                localClient?.Dispose();
                break;
            }
            catch (SocketException)
            {
                localClient?.Dispose();
                if (cancellationToken.IsCancellationRequested)
                    break;
            }
            catch
            {
                localClient?.Dispose();
            }
        }
    }

    private async Task HandleLocalClientAsync(TcpClient localClient, Uri targetUri, CancellationToken cancellationToken)
    {
        using (localClient)
        {
            ClientWebSocket? webSocket = Interlocked.Exchange(ref _prewarmedSocket, null);
            try
            {
                var invoker = _invoker;
                if (invoker is null)
                    return;

                webSocket ??= await ConnectWithRetryAsync(targetUri, invoker, null, cancellationToken);
                await WebSocketStreamRelay.RelayAsync(webSocket, localClient, cancellationToken);
            }
            catch
            {
                // Ignore per-connection drop; SyncplayConsole reconnects automatically if the session is still open.
            }
            finally
            {
                webSocket?.Dispose();
            }
        }
    }

    private static readonly int[] RetryDelaysMs = [1000, 1200, 1500, 1800, 2000, 2500];

    private static async Task<ClientWebSocket> ConnectWithRetryAsync(
        Uri targetUri,
        HttpMessageInvoker invoker,
        Action<string>? statusCallback,
        CancellationToken cancellationToken)
    {
        var maxAttempts = RetryDelaysMs.Length + 1;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await ConnectWebSocketOnceAsync(targetUri, invoker, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt >= maxAttempts)
                    break;

                statusCallback?.Invoke($"Waiting for watch party tunnel route (attempt {attempt + 1}/{maxAttempts})...");
                await Task.Delay(TimeSpan.FromMilliseconds(RetryDelaysMs[attempt - 1]), cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Could not connect to watch party tunnel {WebSocketTunnelAddress.FormatDisplayUrl(targetUri)}: {lastException?.Message}",
            lastException);
    }

    private static HttpMessageInvoker CreateInvoker()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, token) =>
            {
                var addresses = await WebSocketTunnelAddress.ResolveIpv4AddressesAsync(context.DnsEndPoint.Host, token);
                foreach (var address in addresses)
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token);
                        socket.NoDelay = true;
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                    }
                }

                throw new SocketException((int)SocketError.HostNotFound);
            }
        };

        return new HttpMessageInvoker(handler, disposeHandler: true);
    }

    private static async Task<ClientWebSocket> ConnectWebSocketOnceAsync(
        Uri targetUri,
        HttpMessageInvoker invoker,
        CancellationToken cancellationToken)
    {
        var clientWebSocket = new ClientWebSocket();
        clientWebSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        try
        {
            await clientWebSocket.ConnectAsync(targetUri, invoker, cancellationToken);
            return clientWebSocket;
        }
        catch
        {
            clientWebSocket.Dispose();
            throw;
        }
    }
}
