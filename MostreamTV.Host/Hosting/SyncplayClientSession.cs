using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MostreamTV.Host.Networking;

namespace MostreamTV.Host.Hosting;

internal sealed record WatchPartyUser(
    string Username,
    bool IsSelf,
    bool IsController,
    bool? IsReady,
    string? Filename,
    double DurationSeconds,
    string DurationText,
    long SizeBytes,
    string SizeText,
    bool Differs,
    string DiffDetail);

internal sealed record WatchPartyRoom(
    string Name,
    bool IsCurrent,
    IReadOnlyList<WatchPartyUser> Users);

internal sealed record WatchPartyUserListUpdate(
    string CurrentUser,
    string CurrentRoom,
    bool? CurrentReady,
    string? CurrentFile,
    string? CurrentFilePath,
    IReadOnlyList<string> KnownRooms,
    IReadOnlyList<WatchPartyRoom> Rooms);

internal sealed record WatchPartyPlaylistUpdate(
    IReadOnlyList<string> Items,
    int? CurrentIndex,
    string? CurrentFilename,
    bool Enabled);

internal sealed record WatchPartyPlayerState(
    bool? Paused,
    double PositionSeconds,
    double DurationSeconds,
    string? Filename,
    string? FilePath);

internal sealed record WatchPartyMessage(
    string Kind,
    string Text,
    string Timestamp);

internal sealed record WatchPartyChat(
    string Username,
    string Message,
    string Timestamp);

internal sealed class SyncplayClientSession : IAsyncDisposable
{
    private const string BridgePrefix = "@@MOSTREAM@@";

    private readonly SyncplayRuntime _runtime;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _startupLock = new();
    private readonly List<string> _startupErrors = [];
    private Process? _process;
    private WebSocketTunnelClient? _tunnelClient;
    private TaskCompletionSource<bool>? _connectTcs;
    private bool _stopping;
    private bool _connected;

    public SyncplayClientSession(SyncplayRuntime runtime)
    {
        _runtime = runtime;
    }

    public bool IsRunning => _process is { HasExited: false };

    public event EventHandler? Connected;
    public event EventHandler? PlayerReady;
    public event EventHandler<WatchPartyMessage>? MessageReceived;
    public event EventHandler<WatchPartyChat>? ChatReceived;
    public event EventHandler<WatchPartyUserListUpdate>? UserListUpdated;
    public event EventHandler<WatchPartyPlaylistUpdate>? PlaylistUpdated;
    public event EventHandler<WatchPartyPlayerState>? PlayerStateUpdated;
    public event EventHandler<bool>? AutoPlayUpdated;
    public event EventHandler<string>? RoomUpdated;
    public event EventHandler? ClientExited;

    public async Task StartAsync(
        HostConfiguration configuration,
        Action<string>? statusCallback = null,
        CancellationToken cancellationToken = default)
    {
        configuration.Validate();
        if (IsRunning)
            throw new InvalidOperationException("The watch-party client is already running.");

        await _runtime.PrepareBridgeAsync(cancellationToken);

        string targetEndpoint;
        if (configuration.TryGetTunnelUri(out var tunnelUri, out _) && tunnelUri is not null)
        {
            var tunnelClient = new WebSocketTunnelClient();
            try
            {
                var localPort = await tunnelClient.StartAsync(tunnelUri, statusCallback, cancellationToken);
                _tunnelClient = tunnelClient;
                targetEndpoint = $"127.0.0.1:{localPort}";
            }
            catch
            {
                await tunnelClient.DisposeAsync();
                throw;
            }

            statusCallback?.Invoke($"Joining {configuration.FormatTargetEndpoint()} and opening media player...");
        }
        else
        {
            targetEndpoint = configuration.FormatTargetEndpoint();
        }

        lock (_startupLock)
            _startupErrors.Clear();
        _connected = false;
        _stopping = false;
        var connectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _connectTcs = connectTcs;

        var startInfo = new ProcessStartInfo
        {
            FileName = _runtime.ConsoleClientExecutable,
            WorkingDirectory = _runtime.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.Environment["MOSTREAM_BRIDGE"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";
        startInfo.Environment["PYTHONWARNINGS"] = "ignore";
        var wmpHostExe = ResolveWmpHostExecutable();
        if (!string.IsNullOrEmpty(wmpHostExe))
            startInfo.Environment["MOSTREAM_WMP_HOST_EXE"] = wmpHostExe;

        startInfo.ArgumentList.Add("--no-gui");
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add(targetEndpoint);
        startInfo.ArgumentList.Add("--name");
        startInfo.ArgumentList.Add(configuration.HostName);
        startInfo.ArgumentList.Add("--room");
        startInfo.ArgumentList.Add(configuration.RoomName);
        if (!string.IsNullOrEmpty(configuration.Password))
        {
            startInfo.ArgumentList.Add("--password");
            startInfo.ArgumentList.Add(configuration.Password);
        }
        startInfo.ArgumentList.Add("--player-path");
        startInfo.ArgumentList.Add(configuration.PlayerPath);
        startInfo.ArgumentList.Add("--no-store");
        if (!string.IsNullOrWhiteSpace(configuration.MediaPath))
            startInfo.ArgumentList.Add(configuration.MediaPath);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += OnOutputDataReceived;
        process.ErrorDataReceived += OnErrorDataReceived;
        process.Exited += OnProcessExited;

        if (!process.Start())
        {
            process.Dispose();
            _connectTcs = null;
            await DisposeTunnelClientAsync();
            throw new InvalidOperationException("Windows could not start the Mostream TV watch-party engine.");
        }

        _process = process;
        ChildProcessGuard.Attach(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
            using var registration = timeoutCts.Token.Register(() =>
                connectTcs.TrySetException(new TimeoutException(
                    configuration.IsHost
                        ? "Timed out connecting to the local watch party."
                        : $"Timed out connecting to {configuration.FormatTargetEndpoint()}. Check the server address, port, and firewall.")));

            await connectTcs.Task;
        }
        catch
        {
            await StopAsync();
            throw;
        }
        finally
        {
            _connectTcs = null;
        }
    }

    public async Task StopAsync()
    {
        var process = _process;
        _process = null;
        _stopping = true;
        _connected = false;
        _connectTcs?.TrySetCanceled();

        try
        {
            if (process is { HasExited: false })
            {
                try
                {
                    await SendCommandCoreAsync(process, BuildCommandJson("stop"));
                    process.StandardInput.Close();
                }
                catch
                {
                    // Stdin may already be closed if the process is shutting down.
                }

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2.5));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
        }
        catch
        {
            // Ignore process-exit races or kill failures during teardown.
        }
        finally
        {
            if (process is not null)
            {
                process.OutputDataReceived -= OnOutputDataReceived;
                process.ErrorDataReceived -= OnErrorDataReceived;
                process.Exited -= OnProcessExited;
                process.Dispose();
            }

            await DisposeTunnelClientAsync();
            _stopping = false;
        }
    }

    private async Task DisposeTunnelClientAsync()
    {
        var tunnelClient = Interlocked.Exchange(ref _tunnelClient, null);
        if (tunnelClient is not null)
            await tunnelClient.DisposeAsync();
    }

    public Task OpenFileAsync(string path, bool resetPosition = false, bool addToPlaylist = false) =>
        SendCommandAsync("open_file", writer =>
        {
            writer.WriteString("path", path);
            writer.WriteBoolean("resetPosition", resetPosition);
            writer.WriteBoolean("addToPlaylist", addToPlaylist);
        });

    public Task AddPlaylistItemsAsync(IEnumerable<string> paths, bool openIfIdle = true)
    {
        var items = paths.ToArray();
        return SendCommandAsync("add_playlist_items", writer =>
        {
            writer.WriteStartArray("paths");
            foreach (var item in items)
                writer.WriteStringValue(item);
            writer.WriteEndArray();
            writer.WriteBoolean("openIfIdle", openIfIdle);
        });
    }

    public Task SelectPlaylistIndexAsync(int index) =>
        SendCommandAsync("select_playlist_index", writer => writer.WriteNumber("index", index));

    public Task RemovePlaylistIndicesAsync(IEnumerable<int> indices)
    {
        var values = indices.ToArray();
        return SendCommandAsync("remove_playlist_indices", writer =>
        {
            writer.WriteStartArray("indices");
            foreach (var value in values)
                writer.WriteNumberValue(value);
            writer.WriteEndArray();
        });
    }

    public Task ClearPlaylistAsync() =>
        SendCommandAsync("clear_playlist");

    public Task ShufflePlaylistAsync() =>
        SendCommandAsync("shuffle_playlist");

    public Task SetPausedAsync(bool paused) =>
        SendCommandAsync("set_paused", writer => writer.WriteBoolean("paused", paused));

    public Task TogglePauseAsync() =>
        SendCommandAsync("toggle_pause");

    public Task SeekAsync(string target) =>
        SendCommandAsync("seek", writer => writer.WriteString("target", target));

    public Task UndoSeekAsync() =>
        SendCommandAsync("undo_seek");

    public Task SetReadyAsync(bool ready) =>
        SendCommandAsync("set_ready", writer => writer.WriteBoolean("ready", ready));

    public Task SetAutoPlayAsync(bool enabled, int minUsers) =>
        SendCommandAsync("set_autoplay", writer =>
        {
            writer.WriteBoolean("enabled", enabled);
            writer.WriteNumber("minUsers", minUsers);
        });

    public Task SetRoomAsync(string room) =>
        SendCommandAsync("set_room", writer => writer.WriteString("room", room));

    public Task SendChatAsync(string message) =>
        SendCommandAsync("send_chat", writer => writer.WriteString("message", message));

    public Task SwitchToFileAsync(string filename) =>
        SendCommandAsync("switch_to_file", writer => writer.WriteString("filename", filename));

    public Task ReconnectAsync() =>
        SendCommandAsync("reconnect");

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _writeLock.Dispose();
    }

    private static string BuildCommandJson(string command, Action<Utf8JsonWriter>? writeFields = null)
    {
        using var buffer = new MemoryStream(128);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("cmd", command);
            writeFields?.Invoke(writer);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private async Task SendCommandAsync(string command, Action<Utf8JsonWriter>? writeFields = null)
    {
        var process = _process;
        if (process is null || process.HasExited)
            return;

        await SendCommandCoreAsync(process, BuildCommandJson(command, writeFields));
    }

    private async Task SendCommandCoreAsync(Process process, string json)
    {
        await _writeLock.WaitAsync();
        try
        {
            if (process.HasExited)
                return;
            await process.StandardInput.WriteLineAsync(json);
            await process.StandardInput.FlushAsync();
        }
        catch (IOException)
        {
            // Ignore broken pipe during shutdown.
        }
        catch (ObjectDisposedException)
        {
            // Ignore disposed stream during shutdown.
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs eventArgs)
    {
        var line = eventArgs.Data;
        if (string.IsNullOrWhiteSpace(line))
            return;

        if (!line.StartsWith(BridgePrefix, StringComparison.Ordinal))
        {
            if (!_connected)
            {
                if (line.Contains("Press enter to exit", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        _process?.StandardInput.WriteLine();
                        _process?.StandardInput.Flush();
                    }
                    catch
                    {
                        // Ignore.
                    }
                }
                else
                {
                    lock (_startupLock)
                        _startupErrors.Add(line.Trim());
                }
            }
            return;
        }

        var json = line[BridgePrefix.Length..];
        try
        {
            using var doc = JsonDocument.Parse(json);
            HandleBridgeEvent(doc.RootElement);
        }
        catch
        {
            // Ignore malformed telemetry line.
        }
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs eventArgs)
    {
        var line = eventArgs.Data;
        if (string.IsNullOrWhiteSpace(line))
            return;

        if (!_connected)
        {
            lock (_startupLock)
                _startupErrors.Add(line.Trim());
        }
    }

    private void OnProcessExited(object? sender, EventArgs eventArgs)
    {
        if (_stopping)
            return;

        _ = DisposeTunnelClientAsync();

        var tcs = _connectTcs;
        if (tcs is not null && !tcs.Task.IsCompleted)
        {
            string details;
            lock (_startupLock)
                details = string.Join(Environment.NewLine, _startupErrors.TakeLast(6));

            var message = string.IsNullOrWhiteSpace(details)
                ? "The watch-party client closed unexpectedly during startup."
                : details;
            tcs.TrySetException(new InvalidOperationException(message));
            return;
        }

        ClientExited?.Invoke(this, EventArgs.Empty);
    }

    private void HandleBridgeEvent(JsonElement root)
    {
        if (!root.TryGetProperty("type", out var typeElement))
            return;
        var eventType = typeElement.GetString();

        switch (eventType)
        {
            case "connected":
                _connected = true;
                _connectTcs?.TrySetResult(true);
                Connected?.Invoke(this, EventArgs.Empty);
                break;

            case "player_ready":
                PlayerReady?.Invoke(this, EventArgs.Empty);
                break;

            case "message":
            case "error":
            {
                var text = GetString(root, "text");
                var kind = eventType == "error" ? "error" : GetString(root, "kind", "info");
                var timestamp = GetString(root, "timestamp");
                var critical = root.TryGetProperty("critical", out var critProp) && critProp.ValueKind == JsonValueKind.True;

                if (critical && !_connected && _connectTcs is not null)
                    _connectTcs.TrySetException(new InvalidOperationException(text));

                MessageReceived?.Invoke(this, new WatchPartyMessage(kind, text, timestamp));
                break;
            }

            case "chat":
            {
                var username = GetString(root, "username");
                var message = GetString(root, "message");
                var timestamp = GetString(root, "timestamp");
                ChatReceived?.Invoke(this, new WatchPartyChat(username, message, timestamp));
                break;
            }

            case "userlist":
                UserListUpdated?.Invoke(this, ParseUserList(root));
                break;

            case "playlist":
                PlaylistUpdated?.Invoke(this, ParsePlaylist(root));
                break;

            case "player_state":
                PlayerStateUpdated?.Invoke(this, ParsePlayerState(root));
                break;

            case "autoplay":
                if (root.TryGetProperty("enabled", out var apProp) &&
                    (apProp.ValueKind == JsonValueKind.True || apProp.ValueKind == JsonValueKind.False))
                {
                    AutoPlayUpdated?.Invoke(this, apProp.GetBoolean());
                }
                break;

            case "room":
                RoomUpdated?.Invoke(this, GetString(root, "room"));
                break;
        }
    }

    private static WatchPartyUserListUpdate ParseUserList(JsonElement root)
    {
        var currentUser = GetString(root, "currentUser");
        var currentRoom = GetString(root, "currentRoom");
        var currentReady = GetNullableBool(root, "currentReady");
        var currentFile = GetNullableString(root, "currentFile");
        var currentFilePath = GetNullableString(root, "currentFilePath");

        var knownRooms = new List<string>();
        if (root.TryGetProperty("knownRooms", out var kr) && kr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in kr.EnumerateArray())
            {
                var roomName = item.GetString();
                if (!string.IsNullOrWhiteSpace(roomName))
                    knownRooms.Add(roomName);
            }
        }

        var rooms = new List<WatchPartyRoom>();
        if (root.TryGetProperty("rooms", out var roomsProp) && roomsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var roomEl in roomsProp.EnumerateArray())
            {
                var roomName = GetString(roomEl, "name");
                var isCurrent = GetBool(roomEl, "isCurrent");
                var users = new List<WatchPartyUser>();

                if (roomEl.TryGetProperty("users", out var usersProp) && usersProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var userEl in usersProp.EnumerateArray())
                    {
                        users.Add(new WatchPartyUser(
                            GetString(userEl, "username"),
                            GetBool(userEl, "isSelf"),
                            GetBool(userEl, "isController"),
                            GetNullableBool(userEl, "isReady"),
                            GetNullableString(userEl, "filename"),
                            GetDouble(userEl, "duration"),
                            GetString(userEl, "durationText"),
                            GetLong(userEl, "size"),
                            GetString(userEl, "sizeText"),
                            GetBool(userEl, "differs"),
                            GetString(userEl, "diffDetail")));
                    }
                }

                rooms.Add(new WatchPartyRoom(roomName, isCurrent, users));
            }
        }

        return new WatchPartyUserListUpdate(
            currentUser,
            currentRoom,
            currentReady,
            currentFile,
            currentFilePath,
            knownRooms,
            rooms);
    }

    private static WatchPartyPlaylistUpdate ParsePlaylist(JsonElement root)
    {
        var items = new List<string>();
        if (root.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in itemsProp.EnumerateArray())
            {
                var text = item.GetString();
                if (!string.IsNullOrEmpty(text))
                    items.Add(text);
            }
        }

        int? currentIndex = null;
        if (root.TryGetProperty("currentIndex", out var idxProp) && idxProp.ValueKind == JsonValueKind.Number)
            currentIndex = idxProp.GetInt32();

        var currentFilename = GetNullableString(root, "currentFilename");
        var enabled = !root.TryGetProperty("enabled", out var enProp) || enProp.ValueKind != JsonValueKind.False;
        return new WatchPartyPlaylistUpdate(items, currentIndex, currentFilename, enabled);
    }

    private static WatchPartyPlayerState ParsePlayerState(JsonElement root)
    {
        return new WatchPartyPlayerState(
            GetNullableBool(root, "paused"),
            GetDouble(root, "position"),
            GetDouble(root, "duration"),
            GetNullableString(root, "filename"),
            GetNullableString(root, "filepath"));
    }

    private static string GetString(JsonElement obj, string prop, string fallback = "") =>
        obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? fallback
            : fallback;

    private static string? GetNullableString(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static bool GetBool(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.True;

    private static bool? GetNullableBool(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var el)
            ? el.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;

    private static double GetDouble(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var value)
            ? value
            : 0.0;

    private static long GetLong(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var value)
            ? value
            : 0L;

    private static string ResolveWmpHostExecutable()
    {
        var bundledExe = Path.Combine(AppContext.BaseDirectory, "MostreamTV.Host.exe");
        if (File.Exists(bundledExe))
            return bundledExe;

        return File.Exists(Environment.ProcessPath) ? Environment.ProcessPath! : string.Empty;
    }
}
