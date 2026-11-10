using System.Net.Http;

namespace MostreamTV.Host.Networking;

internal sealed class CloudflaredRuntime
{
    private const string ExecutableName = "cloudflared.exe";

    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _downloadLock = new(1, 1);

    public CloudflaredRuntime()
    {
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MoStreamTV",
            "Runtime");
    }

    public async Task<string> EnsureAvailableAsync(Action<string>? statusCallback, CancellationToken cancellationToken)
    {
        var existing = FindExistingExecutable();
        if (existing is not null)
            return existing;

        await _downloadLock.WaitAsync(cancellationToken);
        try
        {
            existing = FindExistingExecutable();
            if (existing is not null)
                return existing;

            Directory.CreateDirectory(_cacheDirectory);
            var targetPath = Path.Combine(_cacheDirectory, ExecutableName);
            var tempPath = $"{targetPath}.downloading.{Guid.NewGuid():N}";
            var downloadUrl = Environment.Is64BitOperatingSystem
                ? "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe"
                : "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-386.exe";

            statusCallback?.Invoke("Downloading Internet tunnel helper (one-time setup)...");

            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MoStreamTV/0.4");

                using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength;
                await using (var remoteStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var fileStream = File.Create(tempPath))
                {
                    var buffer = new byte[65536];
                    long downloaded = 0;
                    int lastPercent = -1;
                    int read;
                    while ((read = await remoteStream.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        downloaded += read;
                        if (totalBytes is > 0)
                        {
                            var percent = (int)Math.Clamp(downloaded * 100 / totalBytes.Value, 0, 100);
                            if (percent != lastPercent && (percent % 10 == 0 || percent == 100))
                            {
                                lastPercent = percent;
                                statusCallback?.Invoke($"Downloading Internet tunnel helper ({percent}%)...");
                            }
                        }
                    }
                }

                if (new FileInfo(tempPath).Length < 1024 * 1024)
                    throw new InvalidOperationException("Downloaded cloudflared executable was incomplete.");

                File.Move(tempPath, targetPath, overwrite: true);
                return targetPath;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Could not download the built-in Internet tunnel helper (cloudflared): {ex.Message} " +
                    "Check your internet connection or uncheck 'Share over Internet' to host on your local network.",
                    ex);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    private string? FindExistingExecutable()
    {
        var appDirCandidate = Path.Combine(AppContext.BaseDirectory, ExecutableName);
        if (IsValidExecutable(appDirCandidate))
            return appDirCandidate;

        var cachedCandidate = Path.Combine(_cacheDirectory, ExecutableName);
        if (IsValidExecutable(cachedCandidate))
            return cachedCandidate;

        var programFilesCandidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "cloudflared",
            ExecutableName);
        if (IsValidExecutable(programFilesCandidate))
            return programFilesCandidate;

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in pathEntries)
        {
            var candidate = Path.Combine(entry, ExecutableName);
            if (IsValidExecutable(candidate))
                return candidate;
        }

        return null;
    }

    private static bool IsValidExecutable(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 1024 * 1024;
        }
        catch
        {
            return false;
        }
    }
}
