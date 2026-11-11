using System.Security.Cryptography;
using MostreamTV.Host.Hosting;
using MostreamTV.Host.Networking;
using MostreamTV.Host.UI;

namespace MostreamTV.Host;

internal sealed class MainForm : Form
{
    private const string MovieFileFilter = "Video files|*.mkv;*.mp4;*.avi;*.mov;*.webm;*.m4v;*.wmv;*.mpg;*.mpeg|All files|*.*";
    private const string IdleHostPlaceholder = "Start the watch party to get a shareable link, room, and password.";
    private const string IdleJoinPlaceholder = "Paste your friend's invite link (wss://...) or IP, room, and password, then click Join watch party.";
    private const string HostInternetServerDisplayText = "This computer (Internet tunnel + LAN)";
    private const string HostLanServerDisplayText = "This computer (LAN / Wi-Fi only)";

    private readonly ThemeToggleButton _themeToggleButton = new()
    {
        Dock = DockStyle.Right,
        Margin = new Padding(0, 1, 0, 1),
    };
    private readonly ToolTip _toolTip = new();

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
    private readonly CheckBox _shareOverInternetCheckBox = new()
    {
        Text = "Share over Internet",
        UseMnemonic = false,
        AutoSize = true,
        Checked = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 2, 3, 4),
    };

    private readonly TextBox _serverHost = new()
    {
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        ReadOnly = true,
        TabStop = false,
        Text = HostInternetServerDisplayText,
        PlaceholderText = "Friend's invite link (wss://...) or IP (e.g. 192.168.1.25:8999)",
    };
    private readonly ThemedButton _pasteInviteButton = new()
    {
        Text = "Paste invite",
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        Enabled = false,
    };

    private readonly TextBox _roomName = new() { Text = "Movie Night", Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _hostName = new() { Text = Environment.UserName, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _password = new() { UseSystemPasswordChar = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly CheckBox _showPasswordCheckBox = new() { Text = "Show", UseMnemonic = false, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 0, 0, 0) };
    private readonly ThemedNumericUpDown _port = new() { Minimum = 1024, Maximum = 65535, Value = 8999, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _mediaPath = new()
    {
        ReadOnly = true,
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        PlaceholderText = "Choose a movie file (or add one after starting)",
    };
    private readonly ThemedButton _browseButton = new() { Text = "Choose movie...", Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly ThemedComboBox _playerSelector = new()
    {
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        DropDownStyle = ComboBoxStyle.DropDownList,
        AccessibleName = "Media player",
        PlaceholderText = "No supported media player found — click Choose player...",
    };
    private readonly ThemedButton _browsePlayerButton = new() { Text = "Choose player...", Anchor = AnchorStyles.Left | AnchorStyles.Right };

    private readonly ThemedButton _startButton = new() { Text = "Start watch party", Dock = DockStyle.Fill };
    private readonly ThemedButton _stopButton = new() { Text = "Stop watch party", Dock = DockStyle.Fill, Enabled = false };
    private readonly ThemedButton _copyButton = new() { Text = "Copy join details", Dock = DockStyle.Fill, Enabled = false };
    private readonly Label _status = new() { Text = "Watch party is off", UseMnemonic = false, AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font("Segoe UI Semibold", 10F) };
    private readonly ThemedGroupBox _inviteGroup = new()
    {
        Text = "Invite friends",
        Dock = DockStyle.Fill,
        Margin = new Padding(10, 3, 3, 3),
        Padding = new Padding(10, 22, 10, 10),
    };
    private readonly TextBox _joinDetails = new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.None,
        Text = IdleHostPlaceholder,
    };
    private readonly Label _inviteHintLabel = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = true,
        UseMnemonic = false,
        Tag = "muted",
        ForeColor = SystemColors.GrayText,
        Padding = new Padding(0, 6, 0, 0),
    };

    private readonly WatchPartyPanel _watchPartyPanel = new();
    private readonly SyncplayRuntime _runtime;
    private readonly SyncplayHost _host;
    private readonly CloudflaredRuntime _cloudflaredRuntime;
    private readonly WebSocketTunnelHost _tunnelHost;
    private readonly SyncplayClientSession _clientSession;
    private HostConfiguration? _activeConfiguration;
    private CancellationTokenSource? _startupCts;
    private bool _isDarkTheme;
    private string _hostPassword;
    private string _joinHost = string.Empty;
    private string _joinPassword = string.Empty;
    private bool _closing;
    private bool _stopping;
    private bool _suppressPlayerSelectionEvents;

    private PictureBox? _logoBox;
    private ThemedGroupBox? _setupGroup;
    private ThemedGroupBox? _watchPartyGroup;

    public MainForm()
    {
        ChildProcessGuard.CleanupOrphanedRuntimeProcesses();

        _runtime = new SyncplayRuntime();
        _host = new SyncplayHost(_runtime);
        _cloudflaredRuntime = new CloudflaredRuntime();
        _tunnelHost = new WebSocketTunnelHost(_cloudflaredRuntime);
        _clientSession = new SyncplayClientSession(_runtime);
        _isDarkTheme = AppTheme.LoadDarkPreference();

        Text = "Mostream TV";
        Icon = BrandAssets.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10F);
        DoubleBuffered = true;
        AllowDrop = true;

        var workingArea = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(workingArea.Width - 40, AppTheme.Scale(this, 940)), Math.Min(workingArea.Height - 40, AppTheme.Scale(this, 760)));
        Size = new Size(Math.Min(workingArea.Width - 20, AppTheme.Scale(this, 1140)), Math.Min(workingArea.Height - 20, AppTheme.Scale(this, 950)));

        _hostPassword = CreateFriendlyPassword();
        _password.Text = _hostPassword;
        _port.Value = ChildProcessGuard.FindAvailableTcpPort(8999);

        WireEvents();
        Controls.Add(BuildLayout());
        RefreshMediaPlayers(initialLoad: true);
        ApplyResponsiveDimensions();
        ApplyModeUi(focusServerField: false);
        ApplyTheme();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyResponsiveDimensions();
        ApplyTheme();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _toolTip.Dispose();
        base.Dispose(disposing);
    }

    private bool IsHostMode => _hostModeRadio.Checked;
    private bool IsSessionRunning => _host.IsRunning || _tunnelHost.IsRunning || _clientSession.IsRunning;

    private void WireEvents()
    {
        _themeToggleButton.Click += (_, _) =>
        {
            _isDarkTheme = !_isDarkTheme;
            AppTheme.SaveDarkPreference(_isDarkTheme);
            ApplyTheme();
        };
        _hostModeRadio.CheckedChanged += ModeRadioOnCheckedChanged;
        _shareOverInternetCheckBox.CheckedChanged += (_, _) => ApplyModeUi(focusServerField: false);
        _serverHost.TextChanged += (_, _) => UpdatePortEnabledState();
        _pasteInviteButton.Click += PasteInviteButtonOnClick;
        _showPasswordCheckBox.CheckedChanged += (_, _) => _password.UseSystemPasswordChar = !_showPasswordCheckBox.Checked;
        _browseButton.Click += BrowseButtonOnClick;
        _playerSelector.DropDown += (_, _) => RefreshMediaPlayers(initialLoad: false);
        _playerSelector.SelectedIndexChanged += PlayerSelectorOnSelectedIndexChanged;
        _browsePlayerButton.Click += BrowsePlayerButtonOnClick;
        _startButton.Click += StartButtonOnClick;
        _stopButton.Click += StopButtonOnClick;
        _copyButton.Click += CopyButtonOnClick;
        FormClosing += MainFormOnFormClosing;

        DragEnter += MainFormOnDragEnter;
        DragDrop += MainFormOnDragDrop;

        _host.ServerExited += HostOnServerExited;
        _tunnelHost.TunnelExited += TunnelHostOnTunnelExited;

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
            Padding = new Padding(14, 10, 14, 14),
            ColumnCount = 1,
            RowCount = 5,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildHeaderBar(), 0, 0);

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

        root.Controls.Add(topRow, 0, 1);
        root.Controls.Add(BuildActionButtons(), 0, 2);

        var statusPanel = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(3, 5, 3, 4) };
        statusPanel.Controls.Add(_status);
        root.Controls.Add(statusPanel, 0, 3);

        root.Controls.Add(BuildWatchPartyGroup(), 0, 4);

        return root;
    }

    private Control BuildHeaderBar()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 4),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _logoBox = new PictureBox
        {
            Image = BrandAssets.Logo32,
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(26, 26),
            Anchor = AnchorStyles.Left,
            Margin = new Padding(2, 2, 8, 2),
        };
        var titleLabel = new Label
        {
            Text = "Mostream TV",
            UseMnemonic = false,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font("Segoe UI Semibold", 11.5F),
            Margin = new Padding(0, 2, 0, 2),
        };

        _themeToggleButton.Dock = DockStyle.None;
        _themeToggleButton.Anchor = AnchorStyles.Right;
        _themeToggleButton.Margin = new Padding(0, 1, 0, 1);

        header.Controls.Add(_logoBox, 0, 0);
        header.Controls.Add(titleLabel, 1, 0);
        header.Controls.Add(_themeToggleButton, 2, 0);
        return header;
    }

    private Control BuildSetupGroup()
    {
        var group = new ThemedGroupBox
        {
            Text = "Watch party setup",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 22, 12, 8),
        };
        _setupGroup = group;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            RowCount = 7,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        for (var row = 0; row < 7; row++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var modePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 2),
        };
        modePanel.Controls.Add(_hostModeRadio);
        modePanel.Controls.Add(_joinModeRadio);
        layout.Controls.Add(CreateFieldLabel("Mode"), 0, 0);
        layout.Controls.Add(modePanel, 1, 0);
        layout.SetColumnSpan(modePanel, 3);

        layout.Controls.Add(CreateFieldLabel("Internet"), 0, 1);
        layout.Controls.Add(_shareOverInternetCheckBox, 1, 1);
        layout.SetColumnSpan(_shareOverInternetCheckBox, 3);

        var serverPicker = BuildFilePickerRow(_serverHost, _pasteInviteButton);
        layout.Controls.Add(CreateFieldLabel("Server address"), 0, 2);
        layout.Controls.Add(serverPicker, 1, 2);
        layout.SetColumnSpan(serverPicker, 3);

        AddField(layout, 3, 0, "Room name", _roomName);
        AddField(layout, 3, 2, "Your name", _hostName);

        var passwordPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(3, 2, 12, 4),
        };
        passwordPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        passwordPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        passwordPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        passwordPanel.Controls.Add(_password, 0, 0);
        passwordPanel.Controls.Add(_showPasswordCheckBox, 1, 0);
        layout.Controls.Add(CreateFieldLabel("Password"), 0, 4);
        layout.Controls.Add(passwordPanel, 1, 4);

        AddField(layout, 4, 2, "TCP port", _port);

        var filePicker = BuildFilePickerRow(_mediaPath, _browseButton);
        layout.Controls.Add(CreateFieldLabel("Movie file"), 0, 5);
        layout.Controls.Add(filePicker, 1, 5);
        layout.SetColumnSpan(filePicker, 3);

        var playerPicker = BuildFilePickerRow(_playerSelector, _browsePlayerButton);
        layout.Controls.Add(CreateFieldLabel("Media player"), 0, 6);
        layout.Controls.Add(playerPicker, 1, 6);
        layout.SetColumnSpan(playerPicker, 3);

        group.Controls.Add(layout);
        return group;
    }

    private TableLayoutPanel BuildFilePickerRow(Control primaryControl, ThemedButton browseButton)
    {
        var picker = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(3, 2, 3, 4),
        };
        picker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        picker.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        picker.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        primaryControl.Margin = new Padding(0, 2, 6, 2);
        browseButton.Margin = new Padding(0, 2, 0, 2);
        picker.Controls.Add(primaryControl, 0, 0);
        picker.Controls.Add(browseButton, 1, 0);
        return picker;
    }

    private static Label CreateFieldLabel(string label, int leftMargin = 0) => new()
    {
        Text = label,
        UseMnemonic = false,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(leftMargin, 4, 10, 4),
    };

    private static void AddField(TableLayoutPanel layout, int row, int labelColumn, string label, Control control)
    {
        control.Margin = new Padding(3, 3, labelColumn == 0 ? 12 : 3, 4);
        layout.Controls.Add(CreateFieldLabel(label, labelColumn == 0 ? 0 : 10), labelColumn, row);
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
        inviteLayout.Controls.Add(_inviteHintLabel, 0, 1);
        _inviteGroup.Controls.Add(inviteLayout);
        return _inviteGroup;
    }

    private Control BuildWatchPartyGroup()
    {
        var group = new ThemedGroupBox
        {
            Text = "Watch party",
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 22, 10, 10),
        };
        _watchPartyGroup = group;
        group.Controls.Add(_watchPartyPanel);
        return group;
    }

    private void ApplyResponsiveDimensions()
    {
        var inputButtonHeight = AppTheme.RecommendedInputHeight(this, 32);
        var captionPad = Math.Max(AppTheme.Scale(this, 22), Font.Height + AppTheme.Scale(this, 8));

        var pickerColumnWidth = Math.Max(
            AppTheme.Scale(this, 168),
            AppTheme.MeasureButtonWidth(_browsePlayerButton, "Paste invite", "Choose movie...", "Choose player..."));

        _pasteInviteButton.Size = new Size(pickerColumnWidth, inputButtonHeight);
        _browseButton.Size = new Size(pickerColumnWidth, inputButtonHeight);
        _browsePlayerButton.Size = new Size(pickerColumnWidth, inputButtonHeight);
        UpdatePlayerDropDownWidth();

        var actionHeight = Math.Max(AppTheme.Scale(this, 38), Font.Height + AppTheme.Scale(this, 16));
        _startButton.Height = actionHeight;
        _stopButton.Height = actionHeight;
        _copyButton.Height = actionHeight;

        if (_logoBox is not null)
        {
            var logoSize = AppTheme.Scale(this, 26);
            _logoBox.Size = new Size(logoSize, logoSize);
        }

        var toggleSize = AppTheme.Scale(this, 30);
        _themeToggleButton.Size = new Size(toggleSize, toggleSize);

        if (_setupGroup is not null)
            _setupGroup.Padding = new Padding(12, captionPad, 12, 8);
        _inviteGroup.Padding = new Padding(10, captionPad, 10, 10);
        if (_watchPartyGroup is not null)
            _watchPartyGroup.Padding = new Padding(10, captionPad, 10, 10);
    }

    private void ApplyTheme()
    {
        var palette = AppTheme.GetPalette(_isDarkTheme);
        AppTheme.ApplyToForm(this, palette);
        _watchPartyPanel.ApplyTheme(palette);
        _toolTip.SetToolTip(_themeToggleButton, _isDarkTheme ? "Light theme" : "Dark theme");
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
        var palette = AppTheme.GetPalette(_isDarkTheme);

        if (IsHostMode)
        {
            _shareOverInternetCheckBox.Enabled = !IsSessionRunning;
            _serverHost.ReadOnly = true;
            _serverHost.TabStop = false;
            _serverHost.Text = _shareOverInternetCheckBox.Checked
                ? HostInternetServerDisplayText
                : HostLanServerDisplayText;
            _pasteInviteButton.Enabled = false;
            _startButton.Text = "Start watch party";
            _stopButton.Text = "Stop watch party";
            _inviteGroup.Text = "Invite friends";
            _inviteHintLabel.Text = _shareOverInternetCheckBox.Checked
                ? "Share the wss:// link over the Internet, or LAN on the same Wi-Fi."
                : "LAN friends use this IP and port. Forward TCP port for direct Internet.";
            if (!IsSessionRunning)
                _joinDetails.Text = IdleHostPlaceholder;
        }
        else
        {
            _shareOverInternetCheckBox.Enabled = false;
            _serverHost.ReadOnly = IsSessionRunning;
            _serverHost.TabStop = !IsSessionRunning;
            _serverHost.Text = _joinHost;
            _pasteInviteButton.Enabled = !IsSessionRunning;
            _startButton.Text = "Join watch party";
            _stopButton.Text = "Leave watch party";
            _inviteGroup.Text = "Friend's server details";
            _inviteHintLabel.Text = "Paste the invite (wss://... or IP:port), room, and password, then click Join.";
            if (!IsSessionRunning)
                _joinDetails.Text = IdleJoinPlaceholder;
            if (focusServerField && !_serverHost.ReadOnly)
                _serverHost.Focus();
        }

        AppTheme.StyleTextBox(_serverHost, palette);
        UpdatePortEnabledState();
    }

    private void PasteInviteButtonOnClick(object? sender, EventArgs eventArgs)
    {
        if (!Clipboard.ContainsText())
        {
            MessageBox.Show(
                this,
                "Copy your friend's invite details (Server, Room, Password) to your clipboard, then click Paste invite.",
                "Mostream TV",
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
                "Mostream TV",
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
            SelectedPlayerPath,
            mode == WatchPartyMode.Host && _shareOverInternetCheckBox.Checked);
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

    private string SelectedPlayerPath => (_playerSelector.SelectedItem as DetectedMediaPlayer)?.ExecutablePath ?? string.Empty;

    private void BrowsePlayerButtonOnClick(object? sender, EventArgs eventArgs)
    {
        using var picker = new OpenFileDialog
        {
            Title = "Choose a supported media player",
            Filter = "Media players|vlc.exe;vlcportable.exe;wmplayer.exe;mpv.exe;mpvnet.exe;mpc-hc.exe;mpc-hc64.exe;mpc-hcportable.exe;mpc-hc_nvo.exe;mpc-hc64_nvo.exe;mpc-be.exe;mpc-be64.exe;shoukaku.exe;memento.exe;mplayer.exe;mplayer2.exe|Applications|*.exe|All files|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (picker.ShowDialog(this) != DialogResult.OK)
            return;

        if (!MediaPlayerCatalog.IsSupportedExecutable(picker.FileName))
        {
            MessageBox.Show(
                this,
                "Choose a supported media player executable (vlc.exe, wmplayer.exe, mpv.exe, mpvnet.exe, mpc-hc64.exe, mpc-be64.exe, memento.exe, or mplayer.exe).",
                "Mostream TV",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var fullPath = Path.GetFullPath(picker.FileName);
        MediaPlayerCatalog.SavePreferredPlayerPath(fullPath);
        RefreshMediaPlayers(initialLoad: false, preferredPathOverride: fullPath);
    }

    private void PlayerSelectorOnSelectedIndexChanged(object? sender, EventArgs eventArgs)
    {
        if (_suppressPlayerSelectionEvents)
            return;

        if (_playerSelector.SelectedItem is DetectedMediaPlayer selected)
            MediaPlayerCatalog.SavePreferredPlayerPath(selected.ExecutablePath);

        UpdatePlayerTooltip();
    }

    private void RefreshMediaPlayers(bool initialLoad, string? preferredPathOverride = null)
    {
        var preferredPath = preferredPathOverride
            ?? (initialLoad ? MediaPlayerCatalog.LoadPreferredPlayerPath() : SelectedPlayerPath);
        var players = MediaPlayerCatalog.DetectInstalledPlayers(preferredPath);
        var desiredSelection = MediaPlayerCatalog.ResolveInitialSelection(players, preferredPath);

        var itemsChanged = _playerSelector.Items.Count != players.Count;
        if (!itemsChanged)
        {
            for (var i = 0; i < players.Count; i++)
            {
                if (_playerSelector.Items[i] is not DetectedMediaPlayer existing
                    || !string.Equals(existing.ExecutablePath, players[i].ExecutablePath, StringComparison.OrdinalIgnoreCase)
                    || existing.IsSystemDefault != players[i].IsSystemDefault)
                {
                    itemsChanged = true;
                    break;
                }
            }
        }

        if (itemsChanged || initialLoad)
        {
            _suppressPlayerSelectionEvents = true;
            try
            {
                _playerSelector.BeginUpdate();
                _playerSelector.Items.Clear();
                foreach (var player in players)
                    _playerSelector.Items.Add(player);
                _playerSelector.SelectedItem = desiredSelection;
                _playerSelector.EndUpdate();
            }
            finally
            {
                _suppressPlayerSelectionEvents = false;
            }
        }
        else if (desiredSelection is not null
            && !string.Equals(SelectedPlayerPath, desiredSelection.ExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            _suppressPlayerSelectionEvents = true;
            try
            {
                _playerSelector.SelectedItem = desiredSelection;
            }
            finally
            {
                _suppressPlayerSelectionEvents = false;
            }
        }

        UpdatePlayerTooltip();
        UpdatePlayerDropDownWidth();
    }

    private void UpdatePlayerTooltip()
    {
        var selected = _playerSelector.SelectedItem as DetectedMediaPlayer;
        _toolTip.SetToolTip(
            _playerSelector,
            selected is null
                ? "No supported media player detected. Click Choose player... to locate one."
                : selected.ExecutablePath);
    }

    private void UpdatePlayerDropDownWidth()
    {
        var maxWidth = Math.Max(180, _playerSelector.Width);
        foreach (var item in _playerSelector.Items)
        {
            var label = item?.ToString();
            if (string.IsNullOrEmpty(label))
                continue;
            var measured = TextRenderer.MeasureText(label, _playerSelector.Font).Width
                + SystemInformation.VerticalScrollBarWidth
                + AppTheme.Scale(this, 16);
            if (measured > maxWidth)
                maxWidth = measured;
        }

        var workingWidth = Screen.FromControl(this).WorkingArea.Width - AppTheme.Scale(this, 48);
        _playerSelector.DropDownWidth = Math.Min(maxWidth, Math.Max(220, workingWidth));
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

            _startupCts?.Cancel();
            _startupCts?.Dispose();
            var startupCts = new CancellationTokenSource();
            _startupCts = startupCts;

            try
            {
                if (configuration.IsHost)
                {
                    SetBusy(true, "Starting watch party server...");
                    await _host.StartAsync(configuration, startupCts.Token);

                    if (configuration.ShareOverInternet)
                    {
                        await _tunnelHost.StartAsync(
                            configuration.Port,
                            status => SafeBeginInvoke(() => _status.Text = status),
                            startupCts.Token);
                    }
                }

                _activeConfiguration = configuration;
                UpdateJoinDetails(configuration);
                SetBusy(true, configuration.IsHost
                    ? "Starting Mostream TV watch party and opening media player..."
                    : $"Joining {configuration.FormatTargetEndpoint()} and opening media player...");

                await _clientSession.StartAsync(
                    configuration,
                    status => SafeBeginInvoke(() => _status.Text = status),
                    startupCts.Token);

                SetRunningState(true);
                _watchPartyPanel.SetActive(true);
                if (configuration.IsHost && _tunnelHost.IsRunning)
                {
                    _watchPartyPanel.AppendInfoMessage(
                        DateTime.Now.ToString("HH:mm:ss"),
                        $"Internet tunnel ready: {_tunnelHost.PublicWssUrl} (click Copy join details to share with friends).");
                    _status.Text = "Watch party is running (Internet tunnel + LAN ready)";
                }
                else
                {
                    _status.Text = configuration.IsHost
                        ? "Watch party is running (LAN)"
                        : $"Joined watch party on {configuration.FormatTargetEndpoint()}";
                }
            }
            catch
            {
                _activeConfiguration = null;
                _joinDetails.Text = IsHostMode ? IdleHostPlaceholder : IdleJoinPlaceholder;
                await StopAllRuntimeProcessesAsync(stopHost: configuration.IsHost);
                throw;
            }
            finally
            {
                if (ReferenceEquals(_startupCts, startupCts))
                    _startupCts = null;
                startupCts.Dispose();
            }
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
            _startupCts?.Cancel();
            var wasHost = _host.IsRunning || _tunnelHost.IsRunning;
            SetBusy(true, wasHost ? "Stopping watch party..." : "Leaving watch party...");
            _watchPartyPanel.SetActive(false);
            await StopAllRuntimeProcessesAsync();
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
        _startupCts?.Cancel();
        Enabled = false;
        _status.Text = "Stopping watch party...";
        try
        {
            await StopAllRuntimeProcessesAsync(dispose: true);
        }
        finally
        {
            FormClosing -= MainFormOnFormClosing;
            Close();
        }
    }

    private void HostOnServerExited(object? sender, int exitCode)
    {
        if (_closing || _stopping || IsDisposed || !IsHandleCreated)
            return;

        BeginInvoke(async () =>
        {
            try
            {
                _watchPartyPanel.SetActive(false);
                await StopAllRuntimeProcessesAsync(stopHost: false);
                _activeConfiguration = null;
                _joinDetails.Text = IsHostMode ? IdleHostPlaceholder : IdleJoinPlaceholder;
                SetRunningState(false);
                _status.Text = $"Server stopped unexpectedly (exit code {exitCode})";
            }
            catch
            {
            }
        });
    }

    private void TunnelHostOnTunnelExited(object? sender, string reason)
    {
        if (_closing || _stopping || IsDisposed || !IsHandleCreated)
            return;

        BeginInvoke(() =>
        {
            if (_closing || _stopping || !IsSessionRunning)
                return;

            if (_activeConfiguration is not null)
                UpdateJoinDetails(_activeConfiguration);

            _watchPartyPanel.AppendErrorMessage(
                DateTime.Now.ToString("HH:mm:ss"),
                $"{reason} Local Wi-Fi (LAN) friends can still connect.");
            _status.Text = "Watch party is running (LAN only — Internet tunnel disconnected)";
        });
    }

    private void ClientSessionOnClientExited(object? sender, EventArgs eventArgs)
    {
        if (_closing || _stopping || IsDisposed || !IsHandleCreated)
            return;

        BeginInvoke(async () =>
        {
            try
            {
                var wasHost = _host.IsRunning || _tunnelHost.IsRunning;
                _watchPartyPanel.SetActive(false);
                await StopAllRuntimeProcessesAsync();
                _activeConfiguration = null;
                _joinDetails.Text = IsHostMode ? IdleHostPlaceholder : IdleJoinPlaceholder;
                SetRunningState(false);
                _status.Text = wasHost
                    ? "Media player closed; watch party stopped"
                    : "Disconnected from watch party";
            }
            catch
            {
            }
        });
    }

    private async Task StopAllRuntimeProcessesAsync(bool stopHost = true, bool dispose = false)
    {
        try
        {
            if (dispose)
                await _clientSession.DisposeAsync();
            else
                await _clientSession.StopAsync();
        }
        catch
        {
        }

        try
        {
            if (dispose)
                await _tunnelHost.DisposeAsync();
            else
                await _tunnelHost.StopAsync();
        }
        catch
        {
        }

        if (stopHost)
        {
            try
            {
                if (dispose)
                    await _host.DisposeAsync();
                else
                    await _host.StopAsync();
            }
            catch
            {
            }
        }
    }

    private void UpdateJoinDetails(HostConfiguration configuration)
    {
        var passwordLine = string.IsNullOrEmpty(configuration.Password) ? "(none)" : configuration.Password;
        if (configuration.IsHost)
        {
            var lanEndpoint = $"{LocalAddressProvider.GetPreferredIpv4Address()}:{configuration.Port}";
            var tunnelUrl = _tunnelHost.PublicWssUrl;
            if (!string.IsNullOrWhiteSpace(tunnelUrl))
            {
                _joinDetails.Text =
                    $"Server: {tunnelUrl}{Environment.NewLine}" +
                    $"Room: {configuration.RoomName}{Environment.NewLine}" +
                    $"Password: {passwordLine}{Environment.NewLine}" +
                    $"LAN: {lanEndpoint}";
            }
            else
            {
                _joinDetails.Text =
                    $"Server: {lanEndpoint}{Environment.NewLine}" +
                    $"Room: {configuration.RoomName}{Environment.NewLine}" +
                    $"Password: {passwordLine}";
            }
        }
        else
        {
            _joinDetails.Text =
                $"Server: {configuration.FormatTargetEndpoint()}{Environment.NewLine}" +
                $"Room: {configuration.RoomName}{Environment.NewLine}" +
                $"Password: {passwordLine}";
        }
    }

    private void SetRunningState(bool running)
    {
        _startButton.Enabled = !running;
        _stopButton.Enabled = running;
        _copyButton.Enabled = running;
        _hostModeRadio.Enabled = !running;
        _joinModeRadio.Enabled = !running;
        _shareOverInternetCheckBox.Enabled = !running && IsHostMode;
        _serverHost.ReadOnly = running || IsHostMode;
        _serverHost.TabStop = !running && !IsHostMode;
        var palette = AppTheme.GetPalette(_isDarkTheme);
        _serverHost.BackColor = _serverHost.ReadOnly ? palette.SurfaceBack : palette.InputBack;
        _serverHost.ForeColor = _serverHost.ReadOnly && !_serverHost.TabStop ? palette.TextMuted : palette.TextPrimary;
        _pasteInviteButton.Enabled = !running && !IsHostMode;
        SetFieldLocked(_roomName, running, palette);
        SetFieldLocked(_hostName, running, palette);
        SetFieldLocked(_password, running, palette);
        UpdatePortEnabledState(running);
        _playerSelector.Enabled = !running;
        _browsePlayerButton.Enabled = !running;
        AppTheme.StyleTextBox(_mediaPath, palette);
        _browseButton.Enabled = true;
    }

    private static void SetFieldLocked(TextBox field, bool locked, ThemePalette palette)
    {
        field.ReadOnly = locked;
        field.TabStop = !locked;
        AppTheme.StyleTextBox(field, palette);
    }

    private void UpdatePortEnabledState(bool? runningOverride = null)
    {
        var running = runningOverride ?? IsSessionRunning;
        _port.Enabled = !running && (IsHostMode || !WebSocketTunnelAddress.LooksLikeTunnelAddress(_serverHost.Text));
    }

    private void SetBusy(bool busy, string status)
    {
        _startButton.Enabled = !busy && !IsSessionRunning;
        _stopButton.Enabled = !busy && IsSessionRunning;
        _copyButton.Enabled = !busy && IsSessionRunning;
        _hostModeRadio.Enabled = !busy && !IsSessionRunning;
        _joinModeRadio.Enabled = !busy && !IsSessionRunning;
        _shareOverInternetCheckBox.Enabled = !busy && !IsSessionRunning && IsHostMode;
        _status.Text = status;
    }

    private async Task RunUiActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (_closing || _stopping)
        {
            // Startup canceled by closing the window or clicking Stop.
        }
        catch (Exception exception)
        {
            if (_closing || IsDisposed)
                return;
            SetRunningState(IsSessionRunning);
            _watchPartyPanel.SetActive(_clientSession.IsRunning);
            _status.Text = IsSessionRunning ? "Watch party is running" : "Watch party is off";
            MessageBox.Show(this, exception.Message, "Mostream TV", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
