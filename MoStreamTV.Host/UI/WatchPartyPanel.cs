using MoStreamTV.Host.Hosting;

namespace MoStreamTV.Host.UI;

internal sealed class WatchPartyPanel : Panel
{
    private const int MaxLogLines = 400;

    private enum LogTone
    {
        Info,
        Notice,
        Chat,
    }

    private sealed record LogEntry(string Timestamp, string? Prefix, string Text, LogTone Tone);

    private readonly RichTextBox _chatLog = CreateReadOnlyLogBox();
    private readonly TextBox _chatInput = new()
    {
        Dock = DockStyle.Fill,
        PlaceholderText = "Type a chat message...",
    };
    private readonly Button _chatSendButton = new ThemedButton { Text = "Send", Dock = DockStyle.Fill, Height = 28 };
    private readonly RichTextBox _activityLog = CreateReadOnlyLogBox();

    private readonly ComboBox _roomCombo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly Button _joinRoomButton = new ThemedButton { Text = "Join room", Dock = DockStyle.Fill, Height = 28 };

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
        Width = 160,
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
        Width = 52,
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
    private readonly Button _addFilesButton = CreateToolbarButton("Add file(s)...");
    private readonly Button _addUrlButton = CreateToolbarButton("Add URL...");
    private readonly Button _playPlaylistButton = CreateToolbarButton("Play");
    private readonly Button _removePlaylistButton = CreateToolbarButton("Remove");
    private readonly Button _shufflePlaylistButton = CreateToolbarButton("Shuffle");

    private readonly ColumnHeader _viewerColumn = new() { Text = "Viewer", Width = 132 };
    private readonly ColumnHeader _statusColumn = new() { Text = "Status", Width = 96 };
    private readonly ColumnHeader _movieColumn = new() { Text = "Movie", Width = 160 };
    private readonly ColumnHeader _lengthColumn = new() { Text = "Length", Width = 78 };
    private readonly ColumnHeader _sizeColumn = new() { Text = "Size", Width = 74 };

    private readonly Font _boldListFont;
    private readonly Font _boldLogFont;
    private readonly List<LogEntry> _chatEntries = [];
    private readonly List<LogEntry> _activityEntries = [];

    private ThemePalette _palette = AppTheme.Light;
    private bool _suppressEvents;
    private bool _isActive;

    public WatchPartyPanel()
    {
        Dock = DockStyle.Fill;
        _boldListFont = new Font(_userList.Font, FontStyle.Bold);
        _boldLogFont = new Font(_chatLog.Font, FontStyle.Bold);

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

    public void ApplyTheme(ThemePalette palette)
    {
        _palette = palette;
        _userList.GridLines = !palette.IsDark;
        UpdateReadyButtonVisual();

        foreach (ListViewItem item in _userList.Items)
        {
            if (item.Tag is WatchPartyUser user && user.Differs)
                item.ForeColor = _palette.ErrorText;
            else
                item.ForeColor = _palette.TextPrimary;
        }

        RebuildLog(_chatLog, _chatEntries);
        RebuildLog(_activityLog, _activityEntries);
        _userList.Invalidate();
    }

    public void SetActive(bool active)
    {
        _isActive = active;

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
            _userList.Items.Clear();
            _userList.Groups.Clear();
            _playlistView.Items.Clear();
            _roomCombo.Items.Clear();
            _roomCombo.Text = string.Empty;
            SetReadyChecked(false);
            SetAutoPlayChecked(false);
        }
    }

    public void ResetSessionLog()
    {
        _chatEntries.Clear();
        _activityEntries.Clear();
        _chatLog.Clear();
        _activityLog.Clear();
    }

    public void AppendInfoMessage(string timestamp, string text)
    {
        AppendEntry(_activityLog, _activityEntries, new LogEntry(timestamp, null, text, LogTone.Info));
    }

    public void AppendErrorMessage(string timestamp, string text)
    {
        AppendEntry(_activityLog, _activityEntries, new LogEntry(timestamp, "Notice: ", text, LogTone.Notice));
    }

    public void AppendChatMessage(string timestamp, string username, string message)
    {
        AppendEntry(_chatLog, _chatEntries, new LogEntry(timestamp, $"<{username}> ", message, LogTone.Chat));
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
            _roomCombo.Text = !string.IsNullOrWhiteSpace(update.CurrentRoom)
                ? update.CurrentRoom
                : currentRoomText;
            _roomCombo.EndUpdate();

            _userList.BeginUpdate();
            _userList.Items.Clear();
            _userList.Groups.Clear();
            _userList.ShowGroups = update.Rooms.Count > 1;

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
                        ForeColor = user.Differs ? _palette.ErrorText : _palette.TextPrimary,
                    };
                    item.SubItems.Add(status);
                    item.SubItems.Add(string.IsNullOrWhiteSpace(user.Filename) ? "(No file)" : user.Filename);
                    item.SubItems.Add(user.DurationText);
                    item.SubItems.Add(user.SizeText);

                    if (user.IsSelf)
                        item.Font = _boldListFont;

                    if (user.Differs)
                        item.ToolTipText = $"File differs ({user.DiffDetail}). Double-click to switch if available locally.";
                    else if (!string.IsNullOrWhiteSpace(user.Filename) && !user.IsSelf)
                        item.ToolTipText = $"Double-click to switch to {user.Filename}";

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

    private static RichTextBox CreateReadOnlyLogBox() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BackColor = SystemColors.Window,
        BorderStyle = BorderStyle.FixedSingle,
        DetectUrls = false,
        ScrollBars = RichTextBoxScrollBars.Vertical,
        Font = new Font("Segoe UI", 9.5F),
    };

    private static Button CreateToolbarButton(string text) => new ThemedButton
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Height = 28,
        MinimumSize = new Size(56, 28),
        Padding = new Padding(7, 2, 7, 2),
        Margin = new Padding(0, 0, 5, 3),
    };

    private Control BuildLayout()
    {
        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        split.Controls.Add(BuildChatAndActivityPane(), 0, 0);
        split.Controls.Add(BuildRoomAndPlaylistPane(), 1, 0);
        return split;
    }

    private Control BuildChatAndActivityPane()
    {
        var pane = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 0, 8, 0),
            Margin = new Padding(0),
        };
        pane.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pane.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        pane.RowStyles.Add(new RowStyle(SizeType.Percent, 44));

        var chatSection = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 0, 0, 6),
        };
        chatSection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        chatSection.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        chatSection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        chatSection.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        chatSection.Controls.Add(new Label
        {
            Text = "Chat",
            UseMnemonic = false,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9.5F),
            Margin = new Padding(0, 2, 0, 4),
        }, 0, 0);
        chatSection.Controls.Add(_chatLog, 0, 1);

        var chatRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 6, 0, 0),
        };
        chatRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        chatRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        chatRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        chatRow.Controls.Add(_chatInput, 0, 0);
        chatRow.Controls.Add(_chatSendButton, 1, 0);
        chatSection.Controls.Add(chatRow, 0, 2);

        var activitySection = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 4, 0, 0),
        };
        activitySection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        activitySection.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        activitySection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        activitySection.Controls.Add(new Label
        {
            Text = "Activity log",
            UseMnemonic = false,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9.5F),
            Margin = new Padding(0, 0, 0, 4),
        }, 0, 0);
        activitySection.Controls.Add(_activityLog, 0, 1);

        pane.Controls.Add(chatSection, 0, 0);
        pane.Controls.Add(activitySection, 0, 1);
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
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
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

        _userList.DrawColumnHeader += UserListOnDrawColumnHeader;
        _userList.DrawItem += (_, _) => { };
        _userList.DrawSubItem += UserListOnDrawSubItem;
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

    private void UserListOnDrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var backBrush = new SolidBrush(_palette.SurfaceBack);
        using var borderPen = new Pen(_palette.Border);
        e.Graphics.FillRectangle(backBrush, e.Bounds);
        e.Graphics.DrawLine(borderPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        e.Graphics.DrawLine(borderPen, e.Bounds.Right - 1, e.Bounds.Top + 2, e.Bounds.Right - 1, e.Bounds.Bottom - 3);

        var textBounds = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 10), e.Bounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            e.Header?.Text ?? string.Empty,
            _userList.Font,
            textBounds,
            _palette.TextMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private void UserListOnDrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (e.Item is null)
            return;

        var backColor = e.Item.Selected ? _palette.SelectionBack : _userList.BackColor;
        var foreColor = e.Item.Selected ? Color.White : e.Item.ForeColor;
        using var backBrush = new SolidBrush(backColor);
        e.Graphics.FillRectangle(backBrush, e.Bounds);

        using var linePen = new Pen(_palette.SurfaceBack);
        e.Graphics.DrawLine(linePen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        var textBounds = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 10), e.Bounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            e.SubItem?.Text ?? string.Empty,
            e.Item.Font,
            textBounds,
            foreColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
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
            multiline: true,
            palette: _palette);

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
            _readyCheckBox.FlatStyle = _palette.IsDark ? FlatStyle.Flat : FlatStyle.Standard;
            _readyCheckBox.UseVisualStyleBackColor = false;
            _readyCheckBox.BackColor = _palette.ReadyBack;
            _readyCheckBox.ForeColor = _palette.ReadyFore;
            if (_palette.IsDark)
            {
                _readyCheckBox.FlatAppearance.BorderSize = 1;
                _readyCheckBox.FlatAppearance.BorderColor = Color.FromArgb(62, 148, 96);
                _readyCheckBox.FlatAppearance.CheckedBackColor = _palette.ReadyBack;
                _readyCheckBox.FlatAppearance.MouseOverBackColor = Color.FromArgb(44, 104, 66);
            }
        }
        else
        {
            _readyCheckBox.Text = "I'm ready to watch!";
            _readyCheckBox.FlatAppearance.CheckedBackColor = _palette.ButtonBack;
            AppTheme.StyleButton(_readyCheckBox, _palette);
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
        var remaining = _userList.ClientSize.Width - fixedWidth - 4;
        if (remaining > 90)
            _movieColumn.Width = remaining;
    }

    private void ResizePlaylistColumn()
    {
        if (_playlistView.ClientSize.Width > 30)
            _playlistColumn.Width = Math.Max(80, _playlistView.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
    }

    private void AppendEntry(RichTextBox box, List<LogEntry> entries, LogEntry entry)
    {
        entries.Add(entry);
        if (entries.Count > MaxLogLines)
        {
            entries.RemoveAt(0);
            RebuildLog(box, entries);
            return;
        }

        RenderEntry(box, entry);
        box.SelectionStart = box.TextLength;
        box.ScrollToCaret();
    }

    private void RebuildLog(RichTextBox box, IReadOnlyList<LogEntry> entries)
    {
        box.SuspendLayout();
        box.Clear();
        foreach (var entry in entries)
            RenderEntry(box, entry);
        box.SelectionStart = box.TextLength;
        box.ScrollToCaret();
        box.ResumeLayout();
    }

    private void RenderEntry(RichTextBox box, LogEntry entry)
    {
        var (bodyColor, prefixColor) = entry.Tone switch
        {
            LogTone.Notice => (_palette.ErrorText, _palette.ErrorText),
            LogTone.Chat => (_palette.TextPrimary, _palette.AccentUser),
            _ => (_palette.TextPrimary, _palette.TextMuted),
        };

        box.SelectionStart = box.TextLength;
        box.SelectionLength = 0;

        if (!string.IsNullOrWhiteSpace(entry.Timestamp))
        {
            box.SelectionColor = _palette.TextMuted;
            box.SelectionFont = box.Font;
            box.AppendText($"[{entry.Timestamp}] ");
        }

        if (!string.IsNullOrEmpty(entry.Prefix))
        {
            box.SelectionColor = prefixColor;
            box.SelectionFont = _boldLogFont;
            box.AppendText(entry.Prefix);
        }

        box.SelectionColor = bodyColor;
        box.SelectionFont = box.Font;
        box.AppendText(entry.Text + Environment.NewLine);
    }
}
