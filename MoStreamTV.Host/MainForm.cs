using System.Security.Cryptography;
using MoStreamTV.Host.Hosting;
using MoStreamTV.Host.Networking;

namespace MoStreamTV.Host;

internal sealed class MainForm : Form
{
    private readonly TextBox _roomName = new() { Text = "Movie Night" };
    private readonly TextBox _hostName = new() { Text = Environment.UserName };
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly NumericUpDown _port = new() { Minimum = 1024, Maximum = 65535, Value = 8999 };
    private readonly Button _startButton = new() { Text = "Start watch party", Height = 38, Dock = DockStyle.Fill };
    private readonly Button _stopButton = new() { Text = "Stop server", Height = 38, Dock = DockStyle.Fill, Enabled = false };
    private readonly Button _openClientButton = new() { Text = "Open Syncplay", Height = 34, Dock = DockStyle.Fill, Enabled = false };
    private readonly Button _copyButton = new() { Text = "Copy join details", Height = 34, Dock = DockStyle.Fill, Enabled = false };
    private readonly Label _status = new() { Text = "Server is off", AutoSize = true };
    private readonly TextBox _joinDetails = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill };
    private readonly SyncplayHost _host = new(new SyncplayRuntime());
    private bool _closing;

    public MainForm()
    {
        Text = "MoStream TV Host";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(560, 520);
        Size = new Size(620, 560);
        Font = new Font("Segoe UI", 10F);

        _password.Text = CreateFriendlyPassword();
        _host.ServerExited += HostOnServerExited;
        _startButton.Click += StartButtonOnClick;
        _stopButton.Click += StopButtonOnClick;
        _openClientButton.Click += OpenClientButtonOnClick;
        _copyButton.Click += CopyButtonOnClick;
        FormClosing += MainFormOnFormClosing;

        Controls.Add(BuildLayout());
    }

    private Control BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 2,
            RowCount = 10,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 6; row++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddRow(layout, 0, "Room name", _roomName);
        AddRow(layout, 1, "Your name", _hostName);
        AddRow(layout, 2, "Password", _password);
        AddRow(layout, 3, "TCP port", _port);

        var primaryButtons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        primaryButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        primaryButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        primaryButtons.Controls.Add(_startButton, 0, 0);
        primaryButtons.Controls.Add(_stopButton, 1, 0);
        layout.Controls.Add(primaryButtons, 0, 4);
        layout.SetColumnSpan(primaryButtons, 2);

        layout.Controls.Add(_status, 0, 5);
        layout.SetColumnSpan(_status, 2);
        layout.Controls.Add(_joinDetails, 0, 6);
        layout.SetColumnSpan(_joinDetails, 2);

        var secondaryButtons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        secondaryButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        secondaryButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        secondaryButtons.Controls.Add(_openClientButton, 0, 0);
        secondaryButtons.Controls.Add(_copyButton, 1, 0);
        layout.Controls.Add(secondaryButtons, 0, 7);
        layout.SetColumnSpan(secondaryButtons, 2);

        var explanation = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(530, 0),
            Text = "Friends on the same Wi-Fi/LAN can use the address above. For friends over the Internet, forward this TCP port in your router to this computer. Closing MoStream TV stops the private server.",
        };
        layout.Controls.Add(explanation, 0, 8);
        layout.SetColumnSpan(explanation, 2);

        return layout;
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        control.Dock = DockStyle.Fill;
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private HostConfiguration ReadConfiguration()
    {
        return new HostConfiguration(
            _roomName.Text.Trim(),
            _hostName.Text.Trim(),
            _password.Text,
            decimal.ToInt32(_port.Value));
    }

    private async void StartButtonOnClick(object? sender, EventArgs eventArgs)
    {
        await RunUiActionAsync(async () =>
        {
            SetBusy(true, "Starting private server...");
            var configuration = ReadConfiguration();
            await _host.StartAsync(configuration);
            SetRunningState(true);
            UpdateJoinDetails(configuration);
            _status.Text = "Private server is running";
            await _host.LaunchClientAsync(configuration);
        });
    }

    private async void StopButtonOnClick(object? sender, EventArgs eventArgs)
    {
        await RunUiActionAsync(async () =>
        {
            SetBusy(true, "Stopping private server...");
            await _host.StopAsync();
            SetRunningState(false);
            _status.Text = "Server is off";
        });
    }

    private async void OpenClientButtonOnClick(object? sender, EventArgs eventArgs)
    {
        await RunUiActionAsync(() => _host.LaunchClientAsync(ReadConfiguration()));
    }

    private void CopyButtonOnClick(object? sender, EventArgs eventArgs)
    {
        if (!string.IsNullOrWhiteSpace(_joinDetails.Text))
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
        _status.Text = "Stopping private server...";
        await _host.DisposeAsync();
        FormClosing -= MainFormOnFormClosing;
        Close();
    }

    private void HostOnServerExited(object? sender, int exitCode)
    {
        if (_closing || IsDisposed)
            return;
        BeginInvoke(() =>
        {
            SetRunningState(false);
            _status.Text = $"Server stopped unexpectedly (exit code {exitCode})";
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
        _openClientButton.Enabled = running;
        _copyButton.Enabled = running;
        _roomName.Enabled = !running;
        _hostName.Enabled = !running;
        _password.Enabled = !running;
        _port.Enabled = !running;
    }

    private void SetBusy(bool busy, string status)
    {
        _startButton.Enabled = !busy && !_host.IsRunning;
        _stopButton.Enabled = !busy && _host.IsRunning;
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
            _status.Text = _host.IsRunning ? "Private server is running" : "Server is off";
            MessageBox.Show(this, exception.Message, "MoStream TV", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string CreateFriendlyPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(12);
        return new string(bytes.Select(value => alphabet[value % alphabet.Length]).ToArray());
    }
}
