using System.Buffers;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace MostreamTV.Host.UI;

internal sealed class WmpPlayerForm : Form
{
    private const string WindowsMediaPlayerClsid = "6bf52a52-394a-11d3-b153-00c04f79faa6";

    private sealed class WmpAxHost : AxHost
    {
        public WmpAxHost() : base(WindowsMediaPlayerClsid)
        {
        }

        public object? ActiveXInstance => GetOcx();
    }

    private readonly WmpAxHost _axHost = new() { Dock = DockStyle.Fill };
    private readonly Label _osdBanner = new()
    {
        AutoSize = true,
        Visible = false,
        BackColor = Color.FromArgb(20, 22, 26),
        ForeColor = Color.White,
        Font = new Font("Segoe UI Semibold", 10F),
        Padding = new Padding(12, 6, 12, 6),
        Margin = new Padding(12),
        Location = new Point(14, 14),
        MaximumSize = new Size(760, 0),
    };
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 150 };
    private readonly System.Windows.Forms.Timer _osdTimer = new() { Interval = 3500 };
    private readonly StreamReader _stdinReader = new(Console.OpenStandardInput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    private readonly StreamWriter _stdoutWriter = new(Console.OpenStandardOutput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
    private readonly object _stdoutLock = new();
    private readonly string? _initialFilePath;

    private CancellationTokenSource? _stdinCts;
    private string? _targetFilePath;
    private string? _announcedFilePath;
    private double _announcedDuration;
    private double _lastPosition;
    private double _lastEmittedPosition = -1;
    private bool? _lastEmittedPaused;
    private string? _lastEmittedFilePath;
    private bool _lastSteadyPaused = true;
    private double? _pendingSeekPosition;
    private bool? _pendingPaused;
    private bool _readySent;
    private bool _closingEmitted;

    public WmpPlayerForm(string? initialFilePath)
    {
        _initialFilePath = string.IsNullOrWhiteSpace(initialFilePath) ? null : initialFilePath.Trim();

        Text = "Mostream TV — Windows Media Player";
        Icon = BrandAssets.AppIcon;
        BackColor = Color.Black;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(AppTheme.Scale(this, 1024), AppTheme.Scale(this, 640));
        MinimumSize = new Size(AppTheme.Scale(this, 560), AppTheme.Scale(this, 360));
        ApplyWindowTheme();

        Controls.Add(_axHost);
        Controls.Add(_osdBanner);
        _osdBanner.BringToFront();

        _pollTimer.Tick += (_, _) => PollPlayerState();
        _osdTimer.Tick += (_, _) =>
        {
            _osdTimer.Stop();
            _osdBanner.Visible = false;
        };

        Shown += OnFormShown;
        FormClosing += OnFormClosing;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWindowTheme();
    }

    private void ApplyWindowTheme()
    {
        AppTheme.ApplyToForm(this, AppTheme.GetPalette(AppTheme.LoadDarkPreference()));
        BackColor = Color.Black;
        _osdBanner.BackColor = Color.FromArgb(20, 22, 26);
        _osdBanner.ForeColor = Color.White;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollTimer.Stop();
            _pollTimer.Dispose();
            _osdTimer.Stop();
            _osdTimer.Dispose();
            _stdinCts?.Cancel();
            _stdinCts?.Dispose();
            _stdinReader.Dispose();
            lock (_stdoutLock)
                _stdoutWriter.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnFormShown(object? sender, EventArgs e)
    {
        InitializeOcx();
        StartStdinCommandLoop();
        _pollTimer.Start();

        if (!_readySent)
        {
            _readySent = true;
            EmitReady();
        }

        if (!string.IsNullOrEmpty(_initialFilePath))
            LoadMediaFile(_initialFilePath, resetPosition: false);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        _pollTimer.Stop();
        _osdTimer.Stop();
        _stdinCts?.Cancel();

        try
        {
            if (_axHost.ActiveXInstance is { } ocx && GetComProperty(ocx, "controls") is { } controls)
                InvokeComMethod(controls, "stop");
        }
        catch
        {
        }

        EmitClosing();
    }

    private void InitializeOcx()
    {
        try
        {
            var ocx = _axHost.ActiveXInstance;
            if (ocx is null)
                return;

            SetComProperty(ocx, "uiMode", "full");
            SetComProperty(ocx, "enableContextMenu", true);
            SetComProperty(ocx, "stretchToFit", true);
            if (GetComProperty(ocx, "settings") is { } settings)
            {
                SetComProperty(settings, "autoStart", true);
                SetComProperty(settings, "volume", 100);
            }
        }
        catch (Exception ex)
        {
            EmitError($"Windows Media Player initialization failed: {ex.Message}");
        }
    }

    private void StartStdinCommandLoop()
    {
        _stdinCts = new CancellationTokenSource();
        var token = _stdinCts.Token;

        _ = Task.Run(() =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var line = _stdinReader.ReadLine();
                    if (line is null)
                    {
                        SafeInvokeClose();
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    HandleCommandLine(line);
                }
            }
            catch
            {
                SafeInvokeClose();
            }
        }, token);
    }

    private void HandleCommandLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("cmd", out var cmdProp))
                return;

            var cmd = cmdProp.GetString();
            switch (cmd)
            {
                case "open_file":
                {
                    var path = root.TryGetProperty("path", out var pathProp) ? pathProp.GetString() : null;
                    var resetPosition = root.TryGetProperty("resetPosition", out var resetProp) && resetProp.GetBoolean();
                    SafeBeginInvoke(() => LoadMediaFile(path, resetPosition));
                    break;
                }

                case "set_paused":
                {
                    var paused = root.TryGetProperty("paused", out var pausedProp) && pausedProp.GetBoolean();
                    SafeBeginInvoke(() => ApplyPaused(paused));
                    break;
                }

                case "set_position":
                {
                    if (root.TryGetProperty("position", out var posProp) && posProp.TryGetDouble(out var position))
                        SafeBeginInvoke(() => ApplyPosition(position));
                    break;
                }

                case "set_speed":
                {
                    if (root.TryGetProperty("speed", out var speedProp) && speedProp.TryGetDouble(out var speed))
                        SafeBeginInvoke(() => ApplySpeed(speed));
                    break;
                }

                case "osd":
                {
                    var text = root.TryGetProperty("text", out var textProp) ? textProp.GetString() : null;
                    var durationMs = root.TryGetProperty("durationMs", out var durProp) && durProp.TryGetInt32(out var ms) ? ms : 3500;
                    SafeBeginInvoke(() => ShowOsdBanner(text, durationMs));
                    break;
                }

                case "ask_status":
                    SafeBeginInvoke(() => EmitStatusSnapshot());
                    break;

                case "quit":
                    SafeInvokeClose();
                    break;
            }
        }
        catch
        {
        }
    }

    private void LoadMediaFile(string? path, bool resetPosition)
    {
        try
        {
            var ocx = _axHost.ActiveXInstance;
            if (ocx is null)
                return;

            var cleaned = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
            _targetFilePath = cleaned;
            _announcedFilePath = null;
            _announcedDuration = 0;
            _lastPosition = 0;
            _pendingSeekPosition = resetPosition ? 0 : null;

            if (string.IsNullOrEmpty(cleaned))
            {
                if (GetComProperty(ocx, "controls") is { } controls)
                    InvokeComMethod(controls, "stop");
                SetComProperty(ocx, "URL", string.Empty);
                Text = "Mostream TV — Windows Media Player";
                EmitStatusSnapshot();
                return;
            }

            SetComProperty(ocx, "URL", cleaned);
            Text = $"{Path.GetFileName(cleaned)} — Windows Media Player";
            PollPlayerState();
        }
        catch (Exception ex)
        {
            EmitError($"Windows Media Player could not open media: {ex.Message}");
        }
    }

    private void ApplyPaused(bool paused)
    {
        try
        {
            _pendingPaused = paused;
            _lastSteadyPaused = paused;

            var ocx = _axHost.ActiveXInstance;
            if (ocx is null || string.IsNullOrEmpty(_targetFilePath))
                return;

            if (GetComProperty(ocx, "controls") is not { } controls)
                return;

            var state = Convert.ToInt32(GetComProperty(ocx, "playState") ?? 0);
            if (state != 9 && state != 0)
            {
                InvokeComMethod(controls, paused ? "pause" : "play");
                _pendingPaused = null;
            }

            EmitStatusSnapshot();
        }
        catch
        {
        }
    }

    private void ApplyPosition(double position)
    {
        try
        {
            var clamped = Math.Max(0, position);
            _pendingSeekPosition = clamped;
            _lastPosition = clamped;

            var ocx = _axHost.ActiveXInstance;
            if (ocx is null || string.IsNullOrEmpty(_targetFilePath))
                return;

            var duration = ReadCurrentDuration(ocx);
            var state = Convert.ToInt32(GetComProperty(ocx, "playState") ?? 0);
            if (duration > 0 && state != 9 && GetComProperty(ocx, "controls") is { } controls)
            {
                var safeTarget = Math.Min(clamped, Math.Max(0, duration - 0.1));
                SetComProperty(controls, "currentPosition", safeTarget);
                _lastPosition = safeTarget;
                _pendingSeekPosition = null;
            }

            EmitStatusSnapshot();
        }
        catch
        {
        }
    }

    private void ApplySpeed(double speed)
    {
        try
        {
            var ocx = _axHost.ActiveXInstance;
            if (ocx is null || speed <= 0)
                return;

            if (GetComProperty(ocx, "settings") is { } settings)
                SetComProperty(settings, "rate", speed);
        }
        catch
        {
        }
    }

    private void ShowOsdBanner(string? text, int durationMs)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        _osdBanner.MaximumSize = new Size(Math.Max(220, ClientSize.Width - 28), 0);
        _osdBanner.Text = text.Trim();
        _osdBanner.Visible = true;
        _osdBanner.BringToFront();

        _osdTimer.Stop();
        _osdTimer.Interval = Math.Clamp(durationMs, 1000, 10000);
        _osdTimer.Start();
    }

    private void PollPlayerState()
    {
        try
        {
            var ocx = _axHost.ActiveXInstance;
            if (ocx is null)
                return;

            var state = Convert.ToInt32(GetComProperty(ocx, "playState") ?? 0);
            var duration = ReadCurrentDuration(ocx);
            var isReadyPlaybackState = state is 1 or 2 or 3 or 8 or 10;

            if (!string.IsNullOrEmpty(_targetFilePath) && duration > 0 && isReadyPlaybackState)
            {
                var controls = GetComProperty(ocx, "controls");
                if (controls is not null)
                {
                    if (_pendingSeekPosition.HasValue)
                    {
                        var safeTarget = Math.Min(_pendingSeekPosition.Value, Math.Max(0, duration - 0.1));
                        SetComProperty(controls, "currentPosition", safeTarget);
                        _lastPosition = safeTarget;
                        _pendingSeekPosition = null;
                    }

                    if (_pendingPaused.HasValue)
                    {
                        var wantPaused = _pendingPaused.Value;
                        _pendingPaused = null;
                        if (wantPaused && state == 3)
                            InvokeComMethod(controls, "pause");
                        else if (!wantPaused && state != 3)
                            InvokeComMethod(controls, "play");
                    }
                }
            }

            if (!string.IsNullOrEmpty(_targetFilePath)
                && duration > 0
                && (!string.Equals(_announcedFilePath, _targetFilePath, StringComparison.OrdinalIgnoreCase)
                    || Math.Abs(duration - _announcedDuration) > 2.0))
            {
                _announcedFilePath = _targetFilePath;
                _announcedDuration = duration;
                EmitFileLoaded(_targetFilePath, duration);
                EmitStatusSnapshot(force: true);
            }
            else
            {
                EmitStatusSnapshot(force: false);
            }
        }
        catch
        {
        }
    }

    private void EmitStatusSnapshot(bool force = true)
    {
        var ocx = _axHost.ActiveXInstance;
        if (ocx is null)
            return;

        var state = Convert.ToInt32(GetComProperty(ocx, "playState") ?? 0);
        if (state == 3)
            _lastSteadyPaused = false;
        else if (state is 1 or 2 or 8 or 10)
            _lastSteadyPaused = true;

        var duration = ReadCurrentDuration(ocx);
        var controls = GetComProperty(ocx, "controls");
        if (!_pendingSeekPosition.HasValue && controls is not null && state is 1 or 2 or 3 or 4 or 5)
            _lastPosition = Math.Max(0, Convert.ToDouble(GetComProperty(controls, "currentPosition") ?? _lastPosition));

        var filePath = _targetFilePath;
        if (!force
            && _lastEmittedPaused == _lastSteadyPaused
            && string.Equals(_lastEmittedFilePath, filePath, StringComparison.OrdinalIgnoreCase)
            && Math.Abs(_lastPosition - _lastEmittedPosition) < 0.05)
        {
            return;
        }

        _lastEmittedPaused = _lastSteadyPaused;
        _lastEmittedFilePath = filePath;
        _lastEmittedPosition = _lastPosition;
        var fileName = string.IsNullOrEmpty(filePath) ? null : Path.GetFileName(filePath);

        WriteJsonLine(writer =>
        {
            writer.WriteString("type", "status");
            writer.WriteBoolean("paused", _lastSteadyPaused);
            writer.WriteNumber("position", _lastPosition);
            writer.WriteNumber("duration", duration);
            writer.WriteString("filepath", filePath);
            writer.WriteString("filename", fileName);
        });
    }

    private void EmitFileLoaded(string filePath, double duration)
    {
        WriteJsonLine(writer =>
        {
            writer.WriteString("type", "file");
            writer.WriteString("filepath", filePath);
            writer.WriteString("filename", Path.GetFileName(filePath));
            writer.WriteNumber("duration", duration);
        });
    }

    private void EmitReady() => WriteJsonLine(writer => writer.WriteString("type", "ready"));

    private void EmitClosing()
    {
        if (_closingEmitted)
            return;
        _closingEmitted = true;
        WriteJsonLine(writer => writer.WriteString("type", "closing"));
    }

    private void EmitError(string message) => WriteJsonLine(writer =>
    {
        writer.WriteString("type", "error");
        writer.WriteString("message", message);
    });

    private void WriteJsonLine(Action<Utf8JsonWriter> writeProperties)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writeProperties(writer);
            writer.WriteEndObject();
        }

        var line = Encoding.UTF8.GetString(buffer.WrittenSpan);
        lock (_stdoutLock)
        {
            try
            {
                _stdoutWriter.WriteLine(line);
                _stdoutWriter.Flush();
            }
            catch
            {
            }
        }
    }

    private void SafeBeginInvoke(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void SafeInvokeClose() => SafeBeginInvoke(Close);

    private static double ReadCurrentDuration(object ocx)
    {
        var currentMedia = GetComProperty(ocx, "currentMedia");
        if (currentMedia is null)
            return 0;

        var raw = GetComProperty(currentMedia, "duration");
        return raw is null ? 0 : Math.Max(0, Convert.ToDouble(raw));
    }

    private static object? GetComProperty(object target, string propertyName) =>
        target.GetType().InvokeMember(propertyName, BindingFlags.GetProperty, null, target, null);

    private static void SetComProperty(object target, string propertyName, object? value) =>
        target.GetType().InvokeMember(propertyName, BindingFlags.SetProperty, null, target, [value]);

    private static object? InvokeComMethod(object target, string methodName, params object[] args) =>
        target.GetType().InvokeMember(methodName, BindingFlags.InvokeMethod, null, target, args);
}
