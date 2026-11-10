using System.Security.Cryptography;
using MoStreamTV.Host.Hosting;
using MoStreamTV.Host.Networking;
using MoStreamTV.Host.UI;

namespace MoStreamTV.Host;

internal sealed class MainForm : Form
{
    private const string MovieFileFilter = "Video files|*.mkv;*.mp4;*.avi;*.mov;*.webm;*.m4v;*.wmv;*.mpg;*.mpeg|All files|*.*";
    private const string IdleHostPlaceholder = "Start the watch party to see the server address, room, and password to share with friends.";
    private const string IdleJoinPlaceholder = "Enter your friend's server address, room name, and password (or click Paste invite), then click Join watch party.";
    private const string HostServerDisplayText = "This computer (private server)";

    private readonly RadioButton _hostModeRadio = new()
    {
        Text = "Host watch party",
        UseMnemonic = false,
        AutoSize = true,
        Checked = true,
        Font = new Font("Segoe UI Semibold", 9.5F),
    };
    private readonly RadioButton _joinModeRadio = new()
    {
        Text = "Join watch party",
        UseMnemonic = false,
        AutoSize = true,
        Font = new Font("Segoe UI Semibold", 9.5F),
        Margin = new Padding(16, 3, 3, 3),
    };

    private readonly TextBox _serverHost = new()
    {
        Dock = DockStyle.Fill,
        Enabled = false,
        Text = HostServerDisplayText,
        PlaceholderText = "Friend's server IP or host (e.g. 192.168.1.25 or 192.168.1.25:8999)",
    };
    private readonly Button _pasteInviteButton = new()
    {
        Text = "Paste invite",
        Height = 30,
        Dock = DockStyle.Fill,
        Enabled = false,
    };

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
    private readonly GroupBox _inviteGroup = new()
    {
        Text = "Invite friends",
        Dock = DockStyle.Fill,
        Margin = new Padding(10, 3, 3, 3),
        Padding = new Padding(10, 20, 10, 10),
    };
    private readonly TextBox _joinDetails = new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.None,
        Text = IdleHostPlaceholder,
    };

    private readonly WatchPartyPanel _watchPartyPanel = new();
    private readonly SyncplayRuntime _runtime;
    private readonly SyncplayHost _host;
    private readonly SyncplayClientSession _clientSession;
    private HostConfiguration? _activeConfiguration;
    private string _hostPassword;
    private string _joinHost = string.Empty;
    private string _joinPassword = string.Empty;
    private bool _closing;
    private bool _stopping;

    public MainForm()
    {
        _runtime = new SyncplayRuntime();
        _host = new SyncplayHost(_runtime);
        _clientSession = new SyncplayClientSession(_runtime);

        Text = "MoStream TV";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 760);
        Size = new Size(1120, 880);
        Font = new Font("Segoe UI", 10F);
        DoubleBuffered = true;
        AllowDrop = true;

        _hostPassword = CreateFriendlyPassword();
        _password.Text = _hostPassword;
        _playerPath.Text = HostConfiguration.FindDefaultMediaPlayer();

        WireEvents();
        Controls.Add(BuildLayout());
        ApplyModeUi(focusServerField: false);
    }

    private bool IsHostMode => _hostModeRadio.Checked;
    private bool IsSessionRunning => _host.IsRunning || _clientSession.IsRunning;

    private void WireEvents()
    {
        _hostModeRadio.CheckedChanged += ModeRadioOnCheckedChanged;
        _pasteInviteButton.Click += PasteInviteButtonOnClick;
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
            RowCount = 4,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var topRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
        };
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 67));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        topRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        topRow.Controls.Add(BuildSetupGroup(), 0, 0);
        topRow.Controls.Add(BuildInviteGroup(), 1, 0);

        root.Controls.Add(topRow, 0, 0);
        root.Controls.Add(BuildActionButtons(), 0, 1);

        var statusPanel = new Panel { Dock = DockStyle.Fill, Height = 26, Padding = new Padding(3, 4, 3, 2) };
        statusPanel.Controls.Add(_status);
        root.Controls.Add(statusPanel, 0, 2);

        root.Controls.Add(BuildWatchPartyGroup(), 0, 3);

        return root;
    }

    private Control BuildSetupGroup()
    {
        var group = new GroupBox
        {
            Text = "Watch party setup",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 20, 12, 8),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            RowCount = 6,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var row = 0; row < 6; row++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var modePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 4),
        };
        modePanel.Controls.Add(_hostModeRadio);
        modePanel.Controls.Add(_joinModeRadio);
        layout.Controls.Add(new Label { Text = "Mode", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(modePanel, 1, 0);
        layout.SetColumnSpan(modePanel, 3);

        var serverPicker = BuildFilePickerRow(_serverHost, _pasteInviteButton);
        layout.Controls.Add(new Label { Text = "Server address", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(serverPicker, 1, 1);
        layout.SetColumnSpan(serverPicker, 3);

        AddField(layout, 2, 0, "Room name", _roomName);
        AddField(layout, 2, 2, "Your name", _hostName);

        var passwordPanel = new TableLayoutPanel { Dock = DockStyle.Fill, Height = 30, ColumnCount = 2, RowCount = 1, Margin = new Padding(3, 2, 12, 4) };
        passwordPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        passwordPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        passwordPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        passwordPanel.Controls.Add(_password, 0, 0);
        passwordPanel.Controls.Add(_showPasswordCheckBox, 1, 0);
        layout.Controls.Add(new Label { Text = "Password", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        layout.Controls.Add(passwordPanel, 1, 3);

        AddField(layout, 3, 2, "TCP port", _port);

        var filePicker = BuildFilePickerRow(_mediaPath, _browseButton);
        layout.Controls.Add(new Label { Text = "Movie file", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        layout.Controls.Add(filePicker, 1, 4);
        layout.SetColumnSpan(filePicker, 3);

        var playerPicker = BuildFilePickerRow(_playerPath, _browsePlayerButton);
        layout.Controls.Add(new Label { Text = "Media player", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 5);
        layout.Controls.Add(playerPicker, 1, 5);
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
        control.Margin = new Padding(3, 2, labelColumn == 0 ? 12 : 3, 4);
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
        var inviteLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        inviteLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inviteLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        inviteLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        inviteLayout.Controls.Add(_joinDetails, 0, 0);
        inviteLayout.Controls.Add(new Label
        {
            Text = "LAN friends connect directly. Over the Internet, forward this TCP port in your router.",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 44,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(0, 6, 0, 0),
        }, 0, 1);
        _inviteGroup.Controls.Add(inviteLayout);
        return _inviteGroup;
    }

    private Control BuildWatchPartyGroup()
    {
        var group = new GroupBox
        {
            Text = "Watch party",
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 20, 10, 10),
        };
        group.Controls.Add(_watchPartyPanel);
        return group;
    }

    private void ModeRadioOnCheckedChanged(object? sender, EventArgs eventArgs)
    {
        if (IsHostMode)
        {
            _joinHost = _serverHost.Text.Trim();
            _joinPassword = _password.Text;
            _password.Text = _hostPassword;
        }
        else
        {
            _hostPassword = _password.Text;
            _password.Text = _joinPassword;
        }

        ApplyModeUi(focusServerField: !IsHostMode);
    }

    private void ApplyModeUi(bool focusServerField)
    {
        if (IsHostMode)
        {
            _serverHost.Enabled = false;
            _serverHost.Text = HostServerDisplayText;
            _pasteInviteButton.Enabled = false;
            _startButton.Text = "Start watch party";
            _stopButton.Text = "Stop watch party";
            _inviteGroup.Text = "Invite friends";
            if (!IsSessionRunning)
                _joinDetails.Text = IdleHostPlaceholder;
        }
        else
        {
            _serverHost.Enabled = !IsSessionRunning;
            _serverHost.Text = _joinHost;
            _pasteInviteButton.Enabled = !IsSessionRunning;
            _startButton.Text = "Join watch party";
            _stopButton.Text = "Leave watch party";
            _inviteGroup.Text = "Friend's server details";
            if (!IsSessionRunning)
                _joinDetails.Text = IdleJoinPlaceholder;
            if (focusServerField && _serverHost.Enabled)
                _serverHost.Focus();
        }
    }

    private void PasteInviteButtonOnClick(object? sender, EventArgs eventArgs)
    {
        if (!Clipboard.ContainsText())
        {
            MessageBox.Show(
                this,
                "Copy your friend's invite details (Server, Room, Password) to your clipboard, then click Paste invite.",
                "MoStream TV",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var raw = Clipboard.GetText();
        var parsedAny = false;
        var foundPassword = false;

        foreach (var rawLine in raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = rawLine.IndexOf(':');
            if (colon <= 0)
                continue;

            var key = rawLine[..colon].Trim().ToLowerInvariant();
            var value = rawLine[(colon + 1)..].Trim();

            switch (key)
            {
                case "server":
                case "host":
                case "address":
                case "ip":
                {
                    var (host, port) = HostConfiguration.SplitHostAndPort(value, decimal.ToInt32(_port.Value));
                    if (!string.IsNullOrWhiteSpace(host))
                    {
                        _serverHost.Text = host;
                        _joinHost = host;
                        if (port is >= 1024 and <= 65535)
                            _port.Value = port;
                        parsedAny = true;
                    }
                    break;
                }
                case "port" when int.TryParse(value, out var portValue) && portValue is >= 1024 and <= 65535:
                    _port.Value = portValue;
                    parsedAny = true;
                    break;
                case "room":
                case "room name":
                    _roomName.Text = value;
                    parsedAny = true;
                    break;
                case "password":
                case "pass":
                    _password.Text = value;
                    _joinPassword = value;
                    foundPassword = true;
                    parsedAny = true;
                    break;
            }
        }

        if (!parsedAny)
        {
            var single = raw.Trim();
            if (!string.IsNullOrWhiteSpace(single) && !single.Contains(' ') && !single.Contains('\n'))
            {
                var (host, port) = HostConfiguration.SplitHostAndPort(single, decimal.ToInt32(_port.Value));
                if (!string.IsNullOrWhiteSpace(host))
                {
                    _serverHost.Text = host;
                    _joinHost = host;
                    if (port is >= 1024 and <= 65535)
                        _port.Value = port;
                    parsedAny = true;
                }
            }
        }

        if (parsedAny)
        {
            if (!foundPassword)
                _joinPassword = _password.Text;
            _status.Text = "Friend's server details pasted";
        }
        else
        {
            MessageBox.Show(
                this,
                "Could not find Server, Room, or Password in the clipboard text.",
                "MoStream TV",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private HostConfiguration ReadConfiguration()
    {
        var mode = IsHostMode ? WatchPartyMode.Host : WatchPartyMode.Join;
        var rawServerHost = mode == WatchPartyMode.Host ? "127.0.0.1" : _serverHost.Text.Trim();
        var (parsedHost, parsedPort) = HostConfiguration.SplitHostAndPort(rawServerHost, decimal.ToInt32(_port.Value));

        if (mode == WatchPartyMode.Join && parsedPort is >= 1024 and <= 65535 && parsedPort != decimal.ToInt32(_port.Value))
            _port.Value = parsedPort;

        return new HostConfiguration(
            mode,
            mode == WatchPartyMode.Host ? "127.0.0.1" : parsedHost,
            _roomName.Text.Trim(),
            _hostName.Text.Trim(),
            _password.Text,
            mode == WatchPartyMode.Host ? decimal.ToInt32(_port.Value) : parsedPort,
            _mediaPath.Text.Trim(),
            _playerPath.Text.Trim());
    }

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

            if (configuration.IsHost)
            {
                SetBusy(true, "Starting private server...");
                await _host.StartAsync(configuration);
            }

            try
            {
                _activeConfiguration = configuration;
                UpdateJoinDetails(configuration);
                SetBusy(true, configuration.IsHost
                    ? "Starting MoStream TV watch party and opening media player..."
                    : $"Joining {configuration.FormatTargetEndpoint()} and opening media player...");
                await _clientSession.StartAsync(configuration);
            }
            catch
            {
                _activeConfiguration = null;
                _joinDetails.Text = IsHostMode ? IdleHostPlaceholder : IdleJoinPlaceholder;
                if (configuration.IsHost)
                    await _host.StopAsync();
                throw;
            }

            SetRunningState(true);
            _watchPartyPanel.SetActive(true);
            _status.Text = configuration.IsHost
                ? "Watch party is running"
                : $"Joined watch party on {configuration.FormatTargetEndpoint()}";
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
            var wasHost = _host.IsRunning;
            SetBusy(true, wasHost ? "Stopping watch party..." : "Leaving watch party...");
            _watchPartyPanel.SetActive(false);
            await _clientSession.StopAsync();
            await _host.StopAsync();
            _activeConfiguration = null;
            _joinDetails.Text = IsHostMode ? IdleHostPlaceholder : IdleJoinPlaceholder;
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
        if (!string.IsNullOrWhiteSpace(_joinDetails.Text) && IsSessionRunning)
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
            _joinDetails.Text = IsHostMode ? IdleHostPlaceholder : IdleJoinPlaceholder;
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
            var wasHost = _host.IsRunning;
            _watchPartyPanel.SetActive(false);
            await _host.StopAsync();
            _activeConfiguration = null;
            _joinDetails.Text = IsHostMode ? IdleHostPlaceholder : IdleJoinPlaceholder;
            SetRunningState(false);
            _status.Text = wasHost
                ? "Media player closed; watch party stopped"
                : "Disconnected from watch party";
        });
    }

    private void UpdateJoinDetails(HostConfiguration configuration)
    {
        var address = configuration.IsHost
            ? $"{LocalAddressProvider.GetPreferredIpv4Address()}:{configuration.Port}"
            : configuration.FormatTargetEndpoint();
        var passwordLine = string.IsNullOrEmpty(configuration.Password) ? "(none)" : configuration.Password;
        _joinDetails.Text = $"Server: {address}{Environment.NewLine}Room: {configuration.RoomName}{Environment.NewLine}Password: {passwordLine}";
    }

    private void SetRunningState(bool running)
    {
        _startButton.Enabled = !running;
        _stopButton.Enabled = running;
        _copyButton.Enabled = running;
        _hostModeRadio.Enabled = !running;
        _joinModeRadio.Enabled = !running;
        _serverHost.Enabled = !running && !IsHostMode;
        _pasteInviteButton.Enabled = !running && !IsHostMode;
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
        _startButton.Enabled = !busy && !IsSessionRunning;
        _stopButton.Enabled = !busy && IsSessionRunning;
        _copyButton.Enabled = !busy && IsSessionRunning;
        _hostModeRadio.Enabled = !busy && !IsSessionRunning;
        _joinModeRadio.Enabled = !busy && !IsSessionRunning;
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
            SetRunningState(IsSessionRunning);
            _watchPartyPanel.SetActive(_clientSession.IsRunning);
            _status.Text = IsSessionRunning ? "Watch party is running" : "Watch party is off";
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
