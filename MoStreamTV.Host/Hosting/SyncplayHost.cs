using System.Diagnostics;
using System.Security.Cryptography;

namespace MoStreamTV.Host.Hosting;

internal sealed class SyncplayHost : IAsyncDisposable
{
    private readonly SyncplayRuntime _runtime;
    private readonly List<string> _serverOutput = [];
    private readonly object _outputLock = new();
    private Process? _serverProcess;

    public SyncplayHost(SyncplayRuntime runtime)
    {
        _runtime = runtime;
    }

    public bool IsRunning => _serverProcess is { HasExited: false };

    public event EventHandler<int>? ServerExited;

    public async Task StartAsync(HostConfiguration configuration, CancellationToken cancellationToken = default)
    {
        configuration.Validate();
        if (IsRunning)
            throw new InvalidOperationException("The private server is already running.");

        await _runtime.EnsureAvailableAsync(cancellationToken);
        lock (_outputLock)
            _serverOutput.Clear();

        var startInfo = new ProcessStartInfo
        {
            FileName = _runtime.ServerExecutable,
            WorkingDirectory = _runtime.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(configuration.Port.ToString());
        startInfo.ArgumentList.Add("--ipv4-only");
        startInfo.ArgumentList.Add("--interface-ipv4");
        startInfo.ArgumentList.Add("0.0.0.0");
        startInfo.Environment["SYNCPLAY_PASSWORD"] = configuration.Password;
        startInfo.Environment["SYNCPLAY_SALT"] = CreateServerSalt();

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += CaptureOutput;
        process.ErrorDataReceived += CaptureOutput;
        process.Exited += OnServerExited;

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Windows could not start the private server.");
            _serverProcess = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await Task.Delay(700, cancellationToken);

            if (process.HasExited)
            {
                string details;
                lock (_outputLock)
                    details = string.Join(Environment.NewLine, _serverOutput.TakeLast(8));
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(details)
                        ? "The private server closed during startup."
                        : $"The private server closed during startup:{Environment.NewLine}{details}");
            }
        }
        catch
        {
            process.Dispose();
            _serverProcess = null;
            throw;
        }
    }

    public async Task StopAsync()
    {
        var process = _serverProcess;
        _serverProcess = null;
        if (process is null)
            return;

        process.Exited -= OnServerExited;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and shutdown.
        }
        finally
        {
            process.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private void CaptureOutput(object sender, DataReceivedEventArgs eventArgs)
    {
        if (!string.IsNullOrWhiteSpace(eventArgs.Data))
        {
            lock (_outputLock)
                _serverOutput.Add(eventArgs.Data);
        }
    }

    private void OnServerExited(object? sender, EventArgs eventArgs)
    {
        if (sender is Process process)
            ServerExited?.Invoke(this, process.ExitCode);
    }

    private static string CreateServerSalt()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }
}
