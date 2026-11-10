using System.Security.Cryptography;
using MoStreamTV.Host.Hosting;
using MoStreamTV.Host.Networking;
using MoStreamTV.Host.UI;

namespace MoStreamTV.Host;

internal sealed class MainForm : Form
{
    private const string MovieFileFilter = "Video files|*.mkv;*.mp4;*.avi;*.mov;*.webm;*.m4v;*.wmv;*.mpg;*.mpeg|All files|*.*";
    private const string IdleJoinPlaceholder = "Start the watch party to see the server address, room, and password to share with friends.";

    private readonly TextBox _roomName = new() { Text = "Movie Night", Dock = DockStyle.Fill };
    private readonly TextBox _hostName = new() { Text = Environment.UserName, Dock = DockStyle.Fill };
    private readonly TextBox _password = new() { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
    private readonly CheckBox _showPasswordCheckBox = new() { Text = "Show", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly NumericUpDown _port = new() { Minimum = 1024, Maximum = 65535, Value = 8999, Dock = DockStyle.Fill };
    private readonly TextBox _mediaPath = new()
    {
        ReadOnly = true,
        Dock = DockStyle.Fill,
        PlaceholderText = "Choose a movie file (or add one after starting)",
    };
    private readonly Button _browseButton = new() { Text = "Choose movie...", Height = 30, Dock = DockStyle.Fill };
    private readonly TextBox _playerPath = new()
    {
        ReadOnly = true,
        Dock = DockStyle.Fill,
        PlaceholderText = "Choose VLC, mpv, mpv.net, MPC-HC, or MPC-BE",
    };
    private readonly Button _browsePlayerButton = new() { Text = "Choose player...", Height = 30, Dock = DockStyle.Fill };

    private readonly Button _startButton = new() { Text = "Start watch party", Height = 38, Dock = DockStyle.Fill };
    private readonly Button _stopButton = new() { Text = "Stop watch party", Height = 38, Dock = DockStyle.Fill, Enabled = false };
    private readonly Button _copyButton = new() { Text = "Copy join details", Height = 38, Dock = DockStyle.Fill, Enabled = false };
    private readonly Label _status = new() { Text = "Watch party is off", AutoSize = true, Font = new Font("Segoe UI Semibold", 10F) };
    private readonly TextBox _joinDetails = new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.None,
        Text = IdleJoinPlaceholder,
    };

    private readonly WatchPartyPanel _watchPartyPanel = new();
    private readonly SyncplayRuntime _runtime;
    private readonly SyncplayHost _host;
    private readonly SyncplayClientSession _clientSession;
    private HostConfiguration? _activeConfiguration;
    private bool _closing;
    private bool _stopping;

    public MainForm()
    {
        _runtime = new SyncplayRuntime();
        _host = new SyncplayHost(_runtime);
        _clientSession = new SyncplayClientSession(_runtime);

        Text = "MoStream TV";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(920, 820);
        Size = new Size(1080, 920);
        Font = new Font("Segoe UI", 10F);
        DoubleBuffered = true;
        AllowDrop = true;

        _password.Text = CreateFriendlyPassword();
        _playerPath.Text = HostConfiguration.FindDefaultMediaPlayer();

        WireEvents();
        Controls.Add(BuildLayout());
    }

    private void WireEvents()
    {
        _showPasswordCheckBox.CheckedChanged += (_, _) => _password.UseSystemPasswordChar = !_showPasswordCheckBox.Checked;
        _browseButton.Click += BrowseButtonOnClick;
        _browsePlayerButton.Click += BrowsePlayerButtonOnClick;
        _startButton.Click += StartButtonOnClick;
        _stopButton.Click += StopButtonOnClick;
        _copyButton.Click += CopyButtonOnClick;
        FormClosing += MainFormOnFormClosing;

        DragEnter += MainFormOnDragEnter;
        DragDrop += MainFormOnDragDrop;

        _host.ServerExited += HostOnServerExited;

        _clientSession.MessageReceived += (_, msg) => SafeBeginInvoke(() =>
        {
            if (string.Equals(msg.Kind, "error", StringComparison.Ordinal))
                _watchPartyPanel.AppendErrorMessage(msg.Timestamp, msg.Text);
            else
                _watchPartyPanel.AppendInfoMessage(msg.Timestamp, msg.Text);
        });
        _clientSession.ChatReceived += (_, chat) => SafeBeginInvoke(() =>
            _watchPartyPanel.AppendChatMessage(chat.Timestamp, chat.Username, chat.Message));
        _clientSession.UserListUpdated += (_, update) => SafeBeginInvoke(() =>
        {
            _watchPartyPanel.ApplyUserList(update);
            if (!string.IsNullOrWhiteSpace(update.CurrentFilePath) && File.Exists(update.CurrentFilePath))
                _mediaPath.Text = update.CurrentFilePath;
        });
        _clientSession.PlaylistUpdated += (_, update) => SafeBeginInvoke(() =>
            _watchPartyPanel.ApplyPlaylist(update));
        _clientSession.PlayerStateUpdated += (_, state) => SafeBeginInvoke(() =>
        {
            _watchPartyPanel.ApplyPlayerState(state);
            if (!string.IsNullOrWhiteSpace(state.FilePath) && File.Exists(state.FilePath))
                _mediaPath.Text = state.FilePath;
        });
        _clientSession.AutoPlayUpdated += (_, enabled) => SafeBeginInvoke(() =>
            _watchPartyPanel.ApplyAutoPlay(enabled));
        _clientSession.RoomUpdated += (_, room) => SafeBeginInvoke(() =>
        {
            _watchPartyPanel.ApplyRoomName(room);
            if (_activeConfiguration is not null && !string.IsNullOrWhiteSpace(room))
            {
                _activeConfiguration = _activeConfiguration with { RoomName = room };
                _roomName.Text = room;
                UpdateJoinDetails(_activeConfiguration);
            }
        });
        _clientSession.ClientExited += ClientSessionOnClientExited;

        _watchPartyPanel.PlayRequested += async (_, _) => await RunSessionCommandAsync(() => _clientSession.SetPausedAsync(false));
        _watchPartyPanel.PauseRequested += async (_, _) => await RunSessionCommandAsync(() => _clientSession.SetPausedAsync(true));
        _watchPartyPanel.SeekRequested += async (_, target) => await RunSessionCommandAsync(() => _clientSession.SeekAsync(target));
        _watchPartyPanel.UndoSeekRequested += async (_, _) => await RunSessionCommandAsync(() => _clientSession.UndoSeekAsync());
        _watchPartyPanel.OpenMovieRequested += (_, _) => BrowseAndOpenMovie();
        _watchPartyPanel.OpenUrlRequested += async (_, url) => await RunSessionCommandAsync(() => _clientSession.OpenFileAsync(url));
        _watchPartyPanel.ChatSubmitted += async (_, text) => await RunSessionCommandAsync(() => _clientSession.SendChatAsync(text));
        _watchPartyPanel.JoinRoomRequested += async (_, room) => await RunSessionCommandAsync(() => _clientSession.SetRoomAsync(room));
        _watchPartyPanel.ReadyChanged += async (_, ready) => await RunSessionCommandAsync(() => _clientSession.SetReadyAsync(ready));
        _watchPartyPanel.AutoPlayChanged += async (_, args) => await RunSessionCommandAsync(() => _clientSession.SetAutoPlayAsync(args.Enabled, args.MinUsers));
        _watchPartyPanel.SwitchToUserFileRequested += async (_, filename) => await RunSessionCommandAsync(() => _clientSession.SwitchToFileAsync(filename));
        _watchPartyPanel.AddPlaylistFilesRequested += async (_, paths) => await RunSessionCommandAsync(() => _clientSession.AddPlaylistItemsAsync(paths));
        _watchPartyPanel.AddPlaylistUrlsRequested += async (_, urls) => await RunSessionCommandAsync(() => _clientSession.AddPlaylistItemsAsync(urls));
        _watchPartyPanel.PlayPlaylistIndexRequested += async (_, index) => await RunSessionCommandAsync(() => _clientSession.SelectPlaylistIndexAsync(index));
        _watchPartyPanel.RemovePlaylistIndicesRequested += async (_, indices) => await RunSessionCommandAsync(() => _clientSession.RemovePlaylistIndicesAsync(indices));
        _watchPartyPanel.ShufflePlaylistRequested += async (_, _) => await RunSessionCommandAsync(() => _clientSession.ShufflePlaylistAsync());
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 5,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildSetupGroup(), 0, 0);
        root.Controls.Add(BuildActionButtons(), 0, 1);

        var statusPanel = new Panel { Dock = DockStyle.Fill, Height = 30, Padding = new Padding(3, 6, 3, 2) };
        statusPanel.Controls.Add(_status);
        root.Controls.Add(statusPanel, 0, 2);

        root.Controls.Add(BuildInviteGroup(), 0, 3);
        root.Controls.Add(BuildWatchPartyGroup(), 0, 4);

        return root;
    }

    private Control BuildSetupGroup()
    {
        var group = new GroupBox
        {
            Text = "Watch party setup",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(12, 22, 12, 10),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 4,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var row = 0; row < 4; row++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        AddField(layout, 0, 0, "Room name", _roomName);
        AddField(layout, 0, 2, "Your name", _hostName);

        var passwordPanel = new TableLayoutPanel { Dock = DockStyle.Fill, Height = 30, ColumnCount = 2, RowCount = 1, Margin = new Padding(3, 2, 12, 5) };
        passwordPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        passwordPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        passwordPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        passwordPanel.Controls.Add(_password, 0, 0);
        passwordPanel.Controls.Add(_showPasswordCheckBox, 1, 0);
        layout.Controls.Add(new Label { Text = "Password", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(passwordPanel, 1, 1);

        AddField(layout, 1, 2, "TCP port", _port);

        var filePicker = BuildFilePickerRow(_mediaPath, _browseButton);
        layout.Controls.Add(new Label { Text = "Movie file", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        layout.Controls.Add(filePicker, 1, 2);
        layout.SetColumnSpan(filePicker, 3);

        var playerPicker = BuildFilePickerRow(_playerPath, _browsePlayerButton);
        layout.Controls.Add(new Label { Text = "Media player", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        layout.Controls.Add(playerPicker, 1, 3);
        layout.SetColumnSpan(playerPicker, 3);

        group.Controls.Add(layout);
        return group;
    }

    private static TableLayoutPanel BuildFilePickerRow(TextBox pathBox, Button browseButton)
    {
        var picker = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = 32,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(3, 2, 3, 4),
        };
        picker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        picker.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        picker.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        picker.Controls.Add(pathBox, 0, 0);
        picker.Controls.Add(browseButton, 1, 0);
        return picker;
    }

    private static void AddField(TableLayoutPanel layout, int row, int labelColumn, string label, Control control)
    {
        control.Margin = new Padding(3, 2, labelColumn == 0 ? 12 : 3, 5);
        layout.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(labelColumn == 0 ? 0 : 8, 0, 3, 0),
        }, labelColumn, row);
        layout.Controls.Add(control, labelColumn + 1, row);
    }

    private Control BuildActionButtons()
    {
        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Padding = new Padding(0, 8, 0, 0),
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
        buttons.Controls.Add(_startButton, 0, 0);
        buttons.Controls.Add(_stopButton, 1, 0);
        buttons.Controls.Add(_copyButton, 2, 0);
        return buttons;
    }

    private Control BuildInviteGroup()
    {
        var inviteGroup = new GroupBox
        {
            Text = "Invite friends",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 22, 10, 10),
        };
        var inviteLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
        };
        inviteLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inviteLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        inviteLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        inviteLayout.Controls.Add(_joinDetails, 0, 0);
        inviteLayout.Controls.Add(new Label
        {
            Text = "Friends on the same Wi-Fi/LAN can connect directly. Over the Internet, forward this TCP port in your router.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(0, 4, 0, 0),
        }, 0, 1);
        inviteGroup.Controls.Add(inviteLayout);
        return inviteGroup;
    }

    private Control BuildWatchPartyGroup()
    {
        var group = new GroupBox
        {
            Text = "Watch party",
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 22, 10, 10),
        };
        group.Controls.Add(_watchPartyPanel);
        return group;
    }

    private HostConfiguration ReadConfiguration() => new(
        _roomName.Text.Trim(),
        _hostName.Text.Trim(),
        _password.Text,
        decimal.ToInt32(_port.Value),
        _mediaPath.Text.Trim(),
        _playerPath.Text.Trim());

    private void BrowseButtonOnClick(object? sender, EventArgs eventArgs)
    {
        BrowseAndOpenMovie();
    }

    private void BrowseAndOpenMovie()
    {
        using var picker = new OpenFileDialog
        {
            Title = "Choose a movie for the watch party",
            Filter = MovieFileFilter,
            CheckFileExists = true,
            Multiselect = false,
        };

        if (picker.ShowDialog(this) != DialogResult.OK)
            return;

        _mediaPath.Text = picker.FileName;
        if (_clientSession.IsRunning)
            _ = RunSessionCommandAsync(() => _clientSession.OpenFileAsync(picker.FileName));
    }

    private void BrowsePlayerButtonOnClick(object? sender, EventArgs eventArgs)
    {
        using var picker = new OpenFileDialog
        {
            Title = "Choose a supported media player",
            Filter = "Media players|vlc.exe;mpv.exe;mpvnet.exe;mpc-hc.exe;mpc-hc64.exe;mpc-be.exe;mpc-be64.exe;mplayer.exe|Applications|*.exe|All files|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (picker.ShowDialog(this) == DialogResult.OK)
            _playerPath.Text = picker.FileName;
    }

    private void MainFormOnDragEnter(object? sender, DragEventArgs eventArgs)
    {
        if (eventArgs.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            eventArgs.Effect = DragDropEffects.Copy;
    }

    private void MainFormOnDragDrop(object? sender, DragEventArgs eventArgs)
    {
        if (eventArgs.Data?.GetData(DataFormats.FileDrop) is not string[] rawFiles)
            return;

        var files = rawFiles.Where(File.Exists).ToArray();
        if (files.Length == 0)
            return;

        if (!_clientSession.IsRunning)
        {
            _mediaPath.Text = files[0];
            return;
        }

        if (files.Length == 1)
        {
            _mediaPath.Text = files[0];
            _ = RunSessionCommandAsync(() => _clientSession.OpenFileAsync(files[0]));
        }
        else
        {
            _ = RunSessionCommandAsync(() => _clientSession.AddPlaylistItemsAsync(files));
        }
    }

    private async void StartButtonOnClick(object? sender, EventArgs eventArgs)
    {
        await RunUiActionAsync(async () =>
        {
            var configuration = ReadConfiguration();
            configuration.Validate();

            _watchPartyPanel.ResetSessionLog();
            SetBusy(true, "Starting private server...");
            await _host.StartAsync(configuration);

            try
            {
                _activeConfiguration = configuration;
                UpdateJoinDetails(configuration);
                SetBusy(true, "Starting MoStream TV watch party and opening media player...");
                await _clientSession.StartAsync(configuration);
            }
            catch
            {
                _activeConfiguration = null;
                _joinDetails.Text = IdleJoinPlaceholder;
                await _host.StopAsync();
                throw;
            }

            SetRunningState(true);
            _watchPartyPanel.SetActive(true);
            _status.Text = "Watch party is running";
        });
    }

    private async void StopButtonOnClick(object? sender, EventArgs eventArgs)
    {
        await RunUiActionAsync(StopWatchPartyAsync);
    }

    private async Task StopWatchPartyAsync()
    {
        if (_stopping)
            return;

        _stopping = true;
        try
        {
            SetBusy(true, "Stopping watch party...");
            _watchPartyPanel.SetActive(false);
            await _clientSession.StopAsync();
            await _host.StopAsync();
            _activeConfiguration = null;
            _joinDetails.Text = IdleJoinPlaceholder;
            SetRunningState(false);
            _status.Text = "Watch party is off";
        }
        finally
        {
            _stopping = false;
        }
    }

    private void CopyButtonOnClick(object? sender, EventArgs eventArgs)
    {
        if (!string.IsNullOrWhiteSpace(_joinDetails.Text) && _host.IsRunning)
        {
            Clipboard.SetText(_joinDetails.Text);
            _status.Text = "Join details copied";
        }
    }

    private async void MainFormOnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_closing)
            return;

        eventArgs.Cancel = true;
        _closing = true;
        Enabled = false;
        _status.Text = "Stopping watch party...";
        await _clientSession.DisposeAsync();
        await _host.DisposeAsync();
        FormClosing -= MainFormOnFormClosing;
        Close();
    }

    private void HostOnServerExited(object? sender, int exitCode)
    {
        if (_closing || _stopping || IsDisposed || !IsHandleCreated)
            return;

        BeginInvoke(async () =>
        {
            _watchPartyPanel.SetActive(false);
            await _clientSession.StopAsync();
            _activeConfiguration = null;
            _joinDetails.Text = IdleJoinPlaceholder;
            SetRunningState(false);
            _status.Text = $"Server stopped unexpectedly (exit code {exitCode})";
        });
    }

    private void ClientSessionOnClientExited(object? sender, EventArgs eventArgs)
    {
        if (_closing || _stopping || IsDisposed || !IsHandleCreated)
            return;

        BeginInvoke(async () =>
        {
            _watchPartyPanel.SetActive(false);
            await _host.StopAsync();
            _activeConfiguration = null;
            _joinDetails.Text = IdleJoinPlaceholder;
            SetRunningState(false);
            _status.Text = "Media player closed; watch party stopped";
        });
    }

    private void UpdateJoinDetails(HostConfiguration configuration)
    {
        var address = $"{LocalAddressProvider.GetPreferredIpv4Address()}:{configuration.Port}";
        _joinDetails.Text = $"Server: {address}{Environment.NewLine}Room: {configuration.RoomName}{Environment.NewLine}Password: {configuration.Password}";
    }

    private void SetRunningState(bool running)
    {
        _startButton.Enabled = !running;
        _stopButton.Enabled = running;
        _copyButton.Enabled = running;
        _roomName.Enabled = !running;
        _hostName.Enabled = !running;
        _password.Enabled = !running;
        _port.Enabled = !running;
        _playerPath.Enabled = !running;
        _browsePlayerButton.Enabled = !running;
        _mediaPath.Enabled = true;
        _browseButton.Enabled = true;
    }

    private void SetBusy(bool busy, string status)
    {
        _startButton.Enabled = !busy && !_host.IsRunning;
        _stopButton.Enabled = !busy && _host.IsRunning;
        _copyButton.Enabled = !busy && _host.IsRunning;
        _status.Text = status;
    }

    private async Task RunUiActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            SetRunningState(_host.IsRunning);
            _watchPartyPanel.SetActive(_clientSession.IsRunning);
            _status.Text = _host.IsRunning ? "Private server is running" : "Watch party is off";
            MessageBox.Show(this, exception.Message, "MoStream TV", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RunSessionCommandAsync(Func<Task> command)
    {
        try
        {
            await command();
        }
        catch (Exception exception)
        {
            _watchPartyPanel.AppendErrorMessage(DateTime.Now.ToString("HH:mm:ss"), exception.Message);
        }
    }

    private void SafeBeginInvoke(Action action)
    {
        if (_closing || IsDisposed || !IsHandleCreated)
            return;
        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // Form handle was disposed concurrently.
        }
    }

    private static string CreateFriendlyPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(12);
        return new string(bytes.Select(value => alphabet[value % alphabet.Length]).ToArray());
    }
}
