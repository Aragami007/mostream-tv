using MoStreamTV.Host.Hosting;

namespace MoStreamTV.Host.UI;

internal sealed class WatchPartyPanel : Panel
{
    private const int MaxLogLines = 400;

    private readonly Label _nowPlayingLabel = new()
    {
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI Semibold", 9.5F),
        Text = "Start the watch party above to open your media player and sync playback here.",
    };

    private readonly Button _playButton = new() { Text = "Play", Width = 64, Height = 28 };
    private readonly Button _pauseButton = new() { Text = "Pause", Width = 68, Height = 28 };
    private readonly TextBox _seekInput = new() { Text = "0:00", Width = 58, Margin = new Padding(6, 3, 3, 3) };
    private readonly Button _seekButton = new() { Text = "Seek", Width = 58, Height = 28 };
    private readonly Button _undoSeekButton = new() { Text = "Undo", Width = 60, Height = 28 };
    private readonly Button _openMovieButton = new() { Text = "Open movie...", Width = 108, Height = 28 };
    private readonly Button _openUrlButton = new() { Text = "Open URL...", Width = 98, Height = 28 };

    private readonly RichTextBox _activityLog = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BackColor = SystemColors.Window,
        BorderStyle = BorderStyle.FixedSingle,
        DetectUrls = false,
        ScrollBars = RichTextBoxScrollBars.Vertical,
        Font = new Font("Segoe UI", 9.5F),
    };
    private readonly TextBox _chatInput = new()
    {
        Dock = DockStyle.Fill,
        PlaceholderText = "Type a chat message...",
    };
    private readonly Button _chatSendButton = new() { Text = "Send", Dock = DockStyle.Fill, Height = 28 };

    private readonly ComboBox _roomCombo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly Button _joinRoomButton = new() { Text = "Join room", Dock = DockStyle.Fill, Height = 28 };

    private readonly ListView _userList = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
        BorderStyle = BorderStyle.FixedSingle,
        ShowItemToolTips = true,
    };

    private readonly CheckBox _readyCheckBox = new()
    {
        Appearance = Appearance.Button,
        Text = "I'm ready to watch!",
        TextAlign = ContentAlignment.MiddleCenter,
        Height = 30,
        Width = 152,
        Font = new Font("Segoe UI Semibold", 9.5F),
    };
    private readonly CheckBox _autoPlayCheckBox = new()
    {
        Text = "Play when all ready",
        AutoSize = true,
        Margin = new Padding(10, 6, 4, 0),
    };
    private readonly NumericUpDown _autoPlayMinUsers = new()
    {
        Minimum = 2,
        Maximum = 99,
        Value = 2,
        Width = 48,
        Margin = new Padding(2, 4, 0, 0),
    };

    private readonly ListView _playlistView = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        HeaderStyle = ColumnHeaderStyle.None,
        FullRowSelect = true,
        MultiSelect = true,
        AllowDrop = true,
        BorderStyle = BorderStyle.FixedSingle,
        HideSelection = false,
    };
    private readonly ColumnHeader _playlistColumn = new() { Text = "Playlist", Width = 300 };
    private readonly Button _addFilesButton = new() { Text = "Add file(s)...", Width = 96, Height = 26 };
    private readonly Button _addUrlButton = new() { Text = "Add URL...", Width = 84, Height = 26 };
    private readonly Button _playPlaylistButton = new() { Text = "Play", Width = 54, Height = 26 };
    private readonly Button _removePlaylistButton = new() { Text = "Remove", Width = 72, Height = 26 };
    private readonly Button _shufflePlaylistButton = new() { Text = "Shuffle", Width = 68, Height = 26 };

    private readonly ColumnHeader _viewerColumn = new() { Text = "Viewer", Width = 120 };
    private readonly ColumnHeader _statusColumn = new() { Text = "Status", Width = 84 };
    private readonly ColumnHeader _movieColumn = new() { Text = "Movie", Width = 165 };
    private readonly ColumnHeader _lengthColumn = new() { Text = "Length", Width = 68 };
    private readonly ColumnHeader _sizeColumn = new() { Text = "Size", Width = 64 };

    private readonly Font _boldListFont;
    private readonly Font _boldLogFont;

    private bool _suppressEvents;
    private bool _isActive;
    private string? _currentMovieName;

    public WatchPartyPanel()
    {
        Dock = DockStyle.Fill;
        _boldListFont = new Font(_userList.Font, FontStyle.Bold);
        _boldLogFont = new Font(_activityLog.Font, FontStyle.Bold);

        _userList.Columns.AddRange([_viewerColumn, _statusColumn, _movieColumn, _lengthColumn, _sizeColumn]);
        _playlistView.Columns.Add(_playlistColumn);

        Controls.Add(BuildLayout());
        WireEvents();
        SetActive(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _boldListFont.Dispose();
            _boldLogFont.Dispose();
        }
        base.Dispose(disposing);
    }

    public event EventHandler? PlayRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler<string>? SeekRequested;
    public event EventHandler? UndoSeekRequested;
    public event EventHandler? OpenMovieRequested;
    public event EventHandler<string>? OpenUrlRequested;
    public event EventHandler<string>? ChatSubmitted;
    public event EventHandler<string>? JoinRoomRequested;
    public event EventHandler<bool>? ReadyChanged;
    public event EventHandler<(bool Enabled, int MinUsers)>? AutoPlayChanged;
    public event EventHandler<string>? SwitchToUserFileRequested;
    public event EventHandler<string[]>? AddPlaylistFilesRequested;
    public event EventHandler<string[]>? AddPlaylistUrlsRequested;
    public event EventHandler<int>? PlayPlaylistIndexRequested;
    public event EventHandler<int[]>? RemovePlaylistIndicesRequested;
    public event EventHandler? ShufflePlaylistRequested;

    public void SetActive(bool active)
    {
        _isActive = active;

        _playButton.Enabled = active;
        _pauseButton.Enabled = active;
        _seekInput.Enabled = active;
        _seekButton.Enabled = active;
        _undoSeekButton.Enabled = active;
        _openMovieButton.Enabled = active;
        _openUrlButton.Enabled = active;
        _chatInput.Enabled = active;
        _chatSendButton.Enabled = active;
        _roomCombo.Enabled = active;
        _joinRoomButton.Enabled = active;
        _userList.Enabled = active;
        _readyCheckBox.Enabled = active;
        _autoPlayCheckBox.Enabled = active;
        _autoPlayMinUsers.Enabled = active;
        _playlistView.Enabled = active;
        _addFilesButton.Enabled = active;
        _addUrlButton.Enabled = active;
        _playPlaylistButton.Enabled = active;
        _removePlaylistButton.Enabled = active;
        _shufflePlaylistButton.Enabled = active;

        if (!active)
        {
            _currentMovieName = null;
            _nowPlayingLabel.Text = "Start the watch party above to open your media player and sync playback here.";
            _userList.Items.Clear();
            _userList.Groups.Clear();
            _playlistView.Items.Clear();
            _roomCombo.Items.Clear();
            _roomCombo.Text = string.Empty;
            SetReadyChecked(false);
            SetAutoPlayChecked(false);
        }
        else if (string.IsNullOrWhiteSpace(_currentMovieName))
        {
            _nowPlayingLabel.Text = "Connected — no movie loaded yet. Choose a movie or add files to the playlist.";
        }
    }

    public void ResetSessionLog()
    {
        _activityLog.Clear();
    }

    public void AppendInfoMessage(string timestamp, string text)
    {
        AppendLine(timestamp, null, text, SystemColors.ControlText, SystemColors.GrayText);
    }

    public void AppendErrorMessage(string timestamp, string text)
    {
        AppendLine(timestamp, "Notice: ", text, Color.Firebrick, Color.Firebrick);
    }

    public void AppendChatMessage(string timestamp, string username, string message)
    {
        AppendLine(timestamp, $"<{username}> ", message, SystemColors.ControlText, Color.FromArgb(24, 90, 157));
    }

    public void ApplyUserList(WatchPartyUserListUpdate update)
    {
        _suppressEvents = true;
        try
        {
            if (update.CurrentReady.HasValue)
                SetReadyChecked(update.CurrentReady.Value);

            var currentRoomText = _roomCombo.Text;
            _roomCombo.BeginUpdate();
            _roomCombo.Items.Clear();
            foreach (var room in update.KnownRooms)
                _roomCombo.Items.Add(room);
            if (!string.IsNullOrWhiteSpace(update.CurrentRoom))
                _roomCombo.Text = update.CurrentRoom;
            else
                _roomCombo.Text = currentRoomText;
            _roomCombo.EndUpdate();

            _userList.BeginUpdate();
            _userList.Items.Clear();
            _userList.Groups.Clear();

            foreach (var room in update.Rooms)
            {
                var groupHeader = room.IsCurrent ? $"{room.Name} (current)" : room.Name;
                var group = new ListViewGroup(groupHeader);
                _userList.Groups.Add(group);

                foreach (var user in room.Users)
                {
                    var status = user.IsReady switch
                    {
                        true => user.IsController ? "Operator • Ready" : "Ready",
                        false => user.IsController ? "Operator • Not ready" : "Not ready",
                        null => user.IsController ? "Operator" : "—",
                    };
                    var displayName = user.IsSelf ? $"{user.Username} (you)" : user.Username;
                    var item = new ListViewItem(displayName, group)
                    {
                        Tag = user,
                    };
                    item.SubItems.Add(status);
                    item.SubItems.Add(string.IsNullOrWhiteSpace(user.Filename) ? "(No file)" : user.Filename);
                    item.SubItems.Add(user.DurationText);
                    item.SubItems.Add(user.SizeText);

                    if (user.IsSelf)
                        item.Font = _boldListFont;

                    if (user.Differs)
                    {
                        item.ForeColor = Color.Firebrick;
                        item.ToolTipText = $"File differs ({user.DiffDetail}). Double-click to switch if available locally.";
                    }
                    else if (!string.IsNullOrWhiteSpace(user.Filename) && !user.IsSelf)
                    {
                        item.ToolTipText = $"Double-click to switch to {user.Filename}";
                    }

                    _userList.Items.Add(item);
                }
            }

            ResizeUserListColumns();
            _userList.EndUpdate();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    public void ApplyPlaylist(WatchPartyPlaylistUpdate update)
    {
        _playlistView.BeginUpdate();
        _playlistView.Items.Clear();

        for (var index = 0; index < update.Items.Count; index++)
        {
            var entry = update.Items[index];
            var isCurrent = update.CurrentIndex == index
                || (!update.CurrentIndex.HasValue
                    && !string.IsNullOrEmpty(update.CurrentFilename)
                    && string.Equals(entry, update.CurrentFilename, StringComparison.OrdinalIgnoreCase));

            var label = isCurrent ? $"▶ {entry}" : $"   {entry}";
            var item = new ListViewItem(label) { Tag = index };
            if (isCurrent)
                item.Font = _boldListFont;
            _playlistView.Items.Add(item);
        }

        ResizePlaylistColumn();
        _playlistView.EndUpdate();
    }

    public void ApplyPlayerState(WatchPartyPlayerState state)
    {
        _currentMovieName = state.Filename;
        if (!_isActive)
            return;

        if (string.IsNullOrWhiteSpace(state.Filename))
        {
            _nowPlayingLabel.Text = "Connected — no movie loaded yet. Choose a movie or add files to the playlist.";
            return;
        }

        var playStateText = state.Paused switch
        {
            true => "Paused",
            false => "Playing",
            null => "Ready",
        };
        var posText = FormatDuration(state.PositionSeconds);
        var durText = state.DurationSeconds > 0 ? $" / {FormatDuration(state.DurationSeconds)}" : string.Empty;
        _nowPlayingLabel.Text = $"Now playing: {state.Filename}  •  {playStateText} ({posText}{durText})";
    }

    public void ApplyRoomName(string room)
    {
        _suppressEvents = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(room))
                _roomCombo.Text = room;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    public void ApplyAutoPlay(bool enabled)
    {
        _suppressEvents = true;
        try
        {
            SetAutoPlayChecked(enabled);
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _nowPlayingLabel.Dock = DockStyle.Fill;
        _nowPlayingLabel.Height = 24;
        _nowPlayingLabel.Margin = new Padding(0, 0, 0, 4);
        root.Controls.Add(_nowPlayingLabel, 0, 0);
        root.Controls.Add(BuildMediaPlaybackBar(), 0, 1);

        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 6, 0, 0),
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        split.Controls.Add(BuildChatPane(), 0, 0);
        split.Controls.Add(BuildRoomAndPlaylistPane(), 1, 0);

        root.Controls.Add(split, 0, 2);
        return root;
    }

    private Control BuildMediaPlaybackBar()
    {
        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0),
        };
        controls.Controls.Add(_playButton);
        controls.Controls.Add(_pauseButton);
        controls.Controls.Add(_seekInput);
        controls.Controls.Add(_seekButton);
        controls.Controls.Add(_undoSeekButton);
        controls.Controls.Add(_openMovieButton);
        controls.Controls.Add(_openUrlButton);
        return controls;
    }

    private Control BuildChatPane()
    {
        var pane = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0, 0, 6, 0),
        };
        pane.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pane.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        pane.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        pane.Controls.Add(new Label
        {
            Text = "Activity & chat",
            UseMnemonic = false,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9.5F),
            Margin = new Padding(0, 4, 0, 4),
        }, 0, 0);

        pane.Controls.Add(_activityLog, 0, 1);

        var chatRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 6, 0, 0),
        };
        chatRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        chatRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        chatRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        chatRow.Controls.Add(_chatInput, 0, 0);
        chatRow.Controls.Add(_chatSendButton, 1, 0);

        pane.Controls.Add(chatRow, 0, 2);
        return pane;
    }

    private Control BuildRoomAndPlaylistPane()
    {
        var pane = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(6, 0, 0, 0),
        };
        pane.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pane.RowStyles.Add(new RowStyle(SizeType.Percent, 54));
        pane.RowStyles.Add(new RowStyle(SizeType.Percent, 46));

        pane.Controls.Add(BuildParticipantsSection(), 0, 0);
        pane.Controls.Add(BuildPlaylistSection(), 0, 1);
        return pane;
    }

    private Control BuildParticipantsSection()
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 0, 0, 6),
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        section.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var headerRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 4),
        };
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        headerRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        headerRow.Controls.Add(new Label
        {
            Text = "Room & participants",
            UseMnemonic = false,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font("Segoe UI Semibold", 9.5F),
            Margin = new Padding(0, 4, 8, 0),
        }, 0, 0);
        headerRow.Controls.Add(_roomCombo, 1, 0);
        headerRow.Controls.Add(_joinRoomButton, 2, 0);
        section.Controls.Add(headerRow, 0, 0);

        section.Controls.Add(_userList, 0, 1);

        var readinessRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 6, 0, 0),
        };
        readinessRow.Controls.Add(_readyCheckBox);
        readinessRow.Controls.Add(_autoPlayCheckBox);
        readinessRow.Controls.Add(new Label
        {
            Text = "Min:",
            AutoSize = true,
            Margin = new Padding(8, 7, 2, 0),
        });
        readinessRow.Controls.Add(_autoPlayMinUsers);
        section.Controls.Add(readinessRow, 0, 2);

        return section;
    }

    private Control BuildPlaylistSection()
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 4, 0, 0),
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        section.Controls.Add(new Label
        {
            Text = "Shared playlist (drag files here or add below)",
            UseMnemonic = false,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9.5F),
            Margin = new Padding(0, 0, 0, 4),
        }, 0, 0);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 4),
        };
        toolbar.Controls.Add(_addFilesButton);
        toolbar.Controls.Add(_addUrlButton);
        toolbar.Controls.Add(_playPlaylistButton);
        toolbar.Controls.Add(_removePlaylistButton);
        toolbar.Controls.Add(_shufflePlaylistButton);

        section.Controls.Add(toolbar, 0, 1);
        section.Controls.Add(_playlistView, 0, 2);
        return section;
    }

    private void WireEvents()
    {
        _playButton.Click += (_, _) => PlayRequested?.Invoke(this, EventArgs.Empty);
        _pauseButton.Click += (_, _) => PauseRequested?.Invoke(this, EventArgs.Empty);
        _seekButton.Click += (_, _) => SubmitSeek();
        _seekInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SubmitSeek();
            }
        };
        _undoSeekButton.Click += (_, _) => UndoSeekRequested?.Invoke(this, EventArgs.Empty);
        _openMovieButton.Click += (_, _) => OpenMovieRequested?.Invoke(this, EventArgs.Empty);
        _openUrlButton.Click += (_, _) => PromptOpenUrl();

        _chatSendButton.Click += (_, _) => SubmitChat();
        _chatInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SubmitChat();
            }
        };

        _joinRoomButton.Click += (_, _) => SubmitJoinRoom();
        _roomCombo.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SubmitJoinRoom();
            }
        };

        _readyCheckBox.CheckedChanged += (_, _) =>
        {
            UpdateReadyButtonVisual();
            if (!_suppressEvents)
                ReadyChanged?.Invoke(this, _readyCheckBox.Checked);
        };
        _autoPlayCheckBox.CheckedChanged += (_, _) =>
        {
            if (!_suppressEvents)
                AutoPlayChanged?.Invoke(this, (_autoPlayCheckBox.Checked, decimal.ToInt32(_autoPlayMinUsers.Value)));
        };
        _autoPlayMinUsers.ValueChanged += (_, _) =>
        {
            if (!_suppressEvents)
                AutoPlayChanged?.Invoke(this, (_autoPlayCheckBox.Checked, decimal.ToInt32(_autoPlayMinUsers.Value)));
        };

        _userList.DoubleClick += (_, _) =>
        {
            if (_userList.SelectedItems.Count == 1 && _userList.SelectedItems[0].Tag is WatchPartyUser user
                && !user.IsSelf && !string.IsNullOrWhiteSpace(user.Filename))
            {
                SwitchToUserFileRequested?.Invoke(this, user.Filename);
            }
        };
        _userList.Resize += (_, _) => ResizeUserListColumns();

        _addFilesButton.Click += (_, _) => PromptAddPlaylistFiles();
        _addUrlButton.Click += (_, _) => PromptAddPlaylistUrls();
        _playPlaylistButton.Click += (_, _) => PlaySelectedPlaylistItem();
        _removePlaylistButton.Click += (_, _) => RemoveSelectedPlaylistItems();
        _shufflePlaylistButton.Click += (_, _) => ShufflePlaylistRequested?.Invoke(this, EventArgs.Empty);

        _playlistView.DoubleClick += (_, _) => PlaySelectedPlaylistItem();
        _playlistView.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
            {
                e.SuppressKeyPress = true;
                RemoveSelectedPlaylistItems();
            }
            else if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                PlaySelectedPlaylistItem();
            }
        };
        _playlistView.Resize += (_, _) => ResizePlaylistColumn();
        _playlistView.DragEnter += (_, e) =>
        {
            if (_isActive && e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Copy;
        };
        _playlistView.DragDrop += (_, e) =>
        {
            if (_isActive && e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                var validFiles = files.Where(File.Exists).ToArray();
                if (validFiles.Length > 0)
                    AddPlaylistFilesRequested?.Invoke(this, validFiles);
            }
        };
    }

    private void SubmitSeek()
    {
        var target = _seekInput.Text.Trim();
        if (!string.IsNullOrEmpty(target))
            SeekRequested?.Invoke(this, target);
    }

    private void SubmitChat()
    {
        var text = _chatInput.Text.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        _chatInput.Clear();
        ChatSubmitted?.Invoke(this, text);
    }

    private void SubmitJoinRoom()
    {
        var room = _roomCombo.Text.Trim();
        if (!string.IsNullOrEmpty(room))
            JoinRoomRequested?.Invoke(this, room);
    }

    private void PromptOpenUrl()
    {
        var owner = FindForm();
        if (owner is null)
            return;

        var url = PromptDialog.ShowTextInput(owner, "Open media stream URL", "Enter a stream URL (HTTP/HTTPS):");
        if (!string.IsNullOrWhiteSpace(url))
            OpenUrlRequested?.Invoke(this, url);
    }

    private void PromptAddPlaylistFiles()
    {
        var owner = FindForm();
        if (owner is null)
            return;

        using var picker = new OpenFileDialog
        {
            Title = "Add movies or videos to the shared playlist",
            Filter = "Video files|*.mkv;*.mp4;*.avi;*.mov;*.webm;*.m4v;*.wmv;*.mpg;*.mpeg|All files|*.*",
            CheckFileExists = true,
            Multiselect = true,
        };
        if (picker.ShowDialog(owner) == DialogResult.OK && picker.FileNames.Length > 0)
            AddPlaylistFilesRequested?.Invoke(this, picker.FileNames);
    }

    private void PromptAddPlaylistUrls()
    {
        var owner = FindForm();
        if (owner is null)
            return;

        var raw = PromptDialog.ShowTextInput(
            owner,
            "Add stream URL(s) to playlist",
            "Enter one or more stream URLs (one per line):",
            multiline: true);

        if (string.IsNullOrWhiteSpace(raw))
            return;

        var urls = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (urls.Length > 0)
            AddPlaylistUrlsRequested?.Invoke(this, urls);
    }

    private void PlaySelectedPlaylistItem()
    {
        if (_playlistView.SelectedItems.Count > 0 && _playlistView.SelectedItems[0].Tag is int index)
            PlayPlaylistIndexRequested?.Invoke(this, index);
    }

    private void RemoveSelectedPlaylistItems()
    {
        var indices = _playlistView.SelectedItems
            .Cast<ListViewItem>()
            .Select(item => item.Tag is int idx ? idx : -1)
            .Where(idx => idx >= 0)
            .ToArray();

        if (indices.Length > 0)
            RemovePlaylistIndicesRequested?.Invoke(this, indices);
    }

    private void SetReadyChecked(bool ready)
    {
        _readyCheckBox.Checked = ready;
        UpdateReadyButtonVisual();
    }

    private void UpdateReadyButtonVisual()
    {
        if (_readyCheckBox.Checked)
        {
            _readyCheckBox.Text = "✓ Ready to watch";
            _readyCheckBox.BackColor = Color.FromArgb(223, 245, 228);
        }
        else
        {
            _readyCheckBox.Text = "I'm ready to watch!";
            _readyCheckBox.BackColor = SystemColors.Control;
        }
    }

    private void SetAutoPlayChecked(bool enabled)
    {
        _autoPlayCheckBox.Checked = enabled;
    }

    private void ResizeUserListColumns()
    {
        if (_userList.ClientSize.Width <= 0)
            return;

        var fixedWidth = _viewerColumn.Width + _statusColumn.Width + _lengthColumn.Width + _sizeColumn.Width;
        var remaining = _userList.ClientSize.Width - fixedWidth - 2;
        if (remaining > 90)
            _movieColumn.Width = remaining;
    }

    private void ResizePlaylistColumn()
    {
        if (_playlistView.ClientSize.Width > 20)
            _playlistColumn.Width = _playlistView.ClientSize.Width - 2;
    }

    private void AppendLine(string timestamp, string? prefix, string text, Color bodyColor, Color prefixColor)
    {
        if (_activityLog.Lines.Length > MaxLogLines)
        {
            var trimIndex = _activityLog.GetFirstCharIndexFromLine(_activityLog.Lines.Length - MaxLogLines);
            if (trimIndex > 0)
            {
                _activityLog.Select(0, trimIndex);
                _activityLog.SelectedText = string.Empty;
            }
        }

        _activityLog.SelectionStart = _activityLog.TextLength;
        _activityLog.SelectionLength = 0;

        if (!string.IsNullOrWhiteSpace(timestamp))
        {
            _activityLog.SelectionColor = SystemColors.GrayText;
            _activityLog.SelectionFont = _activityLog.Font;
            _activityLog.AppendText($"[{timestamp}] ");
        }

        if (!string.IsNullOrEmpty(prefix))
        {
            _activityLog.SelectionColor = prefixColor;
            _activityLog.SelectionFont = _boldLogFont;
            _activityLog.AppendText(prefix);
        }

        _activityLog.SelectionColor = bodyColor;
        _activityLog.SelectionFont = _activityLog.Font;
        _activityLog.AppendText(text + Environment.NewLine);
        _activityLog.SelectionStart = _activityLog.TextLength;
        _activityLog.ScrollToCaret();
    }

    private static string FormatDuration(double seconds)
    {
        if (seconds < 0)
            seconds = 0;
        var span = TimeSpan.FromSeconds(Math.Floor(seconds));
        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss")
            : span.ToString(@"m\:ss");
    }
}
