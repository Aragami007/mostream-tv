using System.Diagnostics;
using System.Security.Cryptography;

namespace MostreamTV.Host.Hosting;

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
        ChildProcessGuard.CleanupOrphanedRuntimeProcesses();

        if (!ChildProcessGuard.IsTcpPortAvailable(configuration.Port))
        {
            var suggestedPort = ChildProcessGuard.FindAvailableTcpPort(configuration.Port + 1);
            var hint = suggestedPort != configuration.Port ? $" (for example {suggestedPort})" : string.Empty;
            throw new InvalidOperationException(
                $"TCP port {configuration.Port} is already in use by another program. Choose a different TCP port{hint} and try again.");
        }

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
        startInfo.Environment["PYTHONWARNINGS"] = "ignore";

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += CaptureOutput;
        process.ErrorDataReceived += CaptureOutput;
        process.Exited += OnServerExited;

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Windows could not start the private server.");
            ChildProcessGuard.Attach(process);
            _serverProcess = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await Task.Delay(700, cancellationToken);

            if (process.HasExited)
            {
                string rawOutput;
                string details;
                lock (_outputLock)
                {
                    rawOutput = string.Join(Environment.NewLine, _serverOutput);
                    details = string.Join(
                        Environment.NewLine,
                        _serverOutput.Where(line => !IsPythonWarningNoise(line)).TakeLast(8));
                }

                if (rawOutput.Contains("10048", StringComparison.Ordinal)
                    || rawOutput.Contains("Couldn't listen", StringComparison.OrdinalIgnoreCase)
                    || rawOutput.Contains("Address already in use", StringComparison.OrdinalIgnoreCase))
                {
                    var suggestedPort = ChildProcessGuard.FindAvailableTcpPort(configuration.Port + 1);
                    var hint = suggestedPort != configuration.Port ? $" (for example {suggestedPort})" : string.Empty;
                    throw new InvalidOperationException(
                        $"TCP port {configuration.Port} is already in use by another program. Choose a different TCP port{hint} and try again.");
                }

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
        catch
        {
            // Ignore process-exit races or timeout if process is already tearing down.
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

    private static bool IsPythonWarningNoise(string line) =>
        line.StartsWith("<frozen zipimport>:", StringComparison.Ordinal)
        || line.Contains("CryptographyDeprecationWarning", StringComparison.Ordinal)
        || line.Contains("32-bit Python on a 64-bit Windows", StringComparison.OrdinalIgnoreCase)
        || line.TrimStart().StartsWith("is deprecated in cryptography", StringComparison.OrdinalIgnoreCase)
        || line.TrimStart().StartsWith("remove support for Python", StringComparison.OrdinalIgnoreCase)
        || line.TrimStart().StartsWith("Cryptography will be significantly faster", StringComparison.OrdinalIgnoreCase);

    private static string CreateServerSalt()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }
}
