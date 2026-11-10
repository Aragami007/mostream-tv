using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MoStreamTV.Host.Hosting;

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

    public async Task StartAsync(HostConfiguration configuration, CancellationToken cancellationToken = default)
    {
        configuration.Validate();
        if (IsRunning)
            throw new InvalidOperationException("The watch-party client is already running.");

        await _runtime.PrepareBridgeAsync(cancellationToken);

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

        startInfo.ArgumentList.Add("--no-gui");
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add($"127.0.0.1:{configuration.Port}");
        startInfo.ArgumentList.Add("--name");
        startInfo.ArgumentList.Add(configuration.HostName);
        startInfo.ArgumentList.Add("--room");
        startInfo.ArgumentList.Add(configuration.RoomName);
        startInfo.ArgumentList.Add("--password");
        startInfo.ArgumentList.Add(configuration.Password);
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
            throw new InvalidOperationException("Windows could not start the MoStream TV watch-party engine.");
        }

        _process = process;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
            using var registration = timeoutCts.Token.Register(() =>
                connectTcs.TrySetException(new TimeoutException("Timed out connecting to the watch party.")));

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
                    await SendCommandCoreAsync(process, new { cmd = "stop" });
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
        catch (InvalidOperationException)
        {
            // Process exited concurrently.
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

            _stopping = false;
        }
    }

    public Task OpenFileAsync(string path, bool resetPosition = false, bool addToPlaylist = false) =>
        SendCommandAsync(new { cmd = "open_file", path, resetPosition, addToPlaylist });

    public Task AddPlaylistItemsAsync(IEnumerable<string> paths, bool openIfIdle = true) =>
        SendCommandAsync(new { cmd = "add_playlist_items", paths = paths.ToArray(), openIfIdle });

    public Task SelectPlaylistIndexAsync(int index) =>
        SendCommandAsync(new { cmd = "select_playlist_index", index });

    public Task RemovePlaylistIndicesAsync(IEnumerable<int> indices) =>
        SendCommandAsync(new { cmd = "remove_playlist_indices", indices = indices.ToArray() });

    public Task ClearPlaylistAsync() =>
        SendCommandAsync(new { cmd = "clear_playlist" });

    public Task ShufflePlaylistAsync() =>
        SendCommandAsync(new { cmd = "shuffle_playlist" });

    public Task SetPausedAsync(bool paused) =>
        SendCommandAsync(new { cmd = "set_paused", paused });

    public Task TogglePauseAsync() =>
        SendCommandAsync(new { cmd = "toggle_pause" });

    public Task SeekAsync(string target) =>
        SendCommandAsync(new { cmd = "seek", target });

    public Task UndoSeekAsync() =>
        SendCommandAsync(new { cmd = "undo_seek" });

    public Task SetReadyAsync(bool ready) =>
        SendCommandAsync(new { cmd = "set_ready", ready });

    public Task SetAutoPlayAsync(bool enabled, int minUsers) =>
        SendCommandAsync(new { cmd = "set_autoplay", enabled, minUsers });

    public Task SetRoomAsync(string room) =>
        SendCommandAsync(new { cmd = "set_room", room });

    public Task SendChatAsync(string message) =>
        SendCommandAsync(new { cmd = "send_chat", message });

    public Task SwitchToFileAsync(string filename) =>
        SendCommandAsync(new { cmd = "switch_to_file", filename });

    public Task ReconnectAsync() =>
        SendCommandAsync(new { cmd = "reconnect" });

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _writeLock.Dispose();
    }

    private async Task SendCommandAsync(object payload)
    {
        var process = _process;
        if (process is null || process.HasExited)
            return;

        await SendCommandCoreAsync(process, payload);
    }

    private async Task SendCommandCoreAsync(Process process, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
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
}
