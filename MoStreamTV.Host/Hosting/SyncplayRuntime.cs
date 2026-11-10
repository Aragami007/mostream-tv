using System.IO.Compression;

namespace MoStreamTV.Host.Hosting;

internal sealed class SyncplayRuntime
{
    private const string Version = "1.7.6";
    private readonly string _archivePath;
    private readonly string _installDirectory;

    public SyncplayRuntime()
    {
        _archivePath = FindArchivePath();
        _installDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MoStreamTV",
            "Runtime",
            $"Syncplay-{Version}");
    }

    public string ServerExecutable => Path.Combine(_installDirectory, "syncplayServer.exe");
    public string ClientExecutable => Path.Combine(_installDirectory, "Syncplay.exe");
    public string WorkingDirectory => _installDirectory;

    private static string FindArchivePath()
    {
        var archiveName = $"Syncplay_{Version}_Portable.zip";
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "runtime", archiveName);
        if (File.Exists(bundledPath))
            return bundledPath;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 6 && directory is not null; depth++, directory = directory.Parent)
        {
            var repositoryPath = Path.Combine(directory.FullName, "vendor", archiveName);
            if (File.Exists(repositoryPath))
                return repositoryPath;
        }

        return bundledPath;
    }

    public async Task EnsureAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(ServerExecutable) && File.Exists(ClientExecutable))
            return;

        if (!File.Exists(_archivePath))
            throw new FileNotFoundException(
                "The bundled Syncplay runtime is missing. Rebuild or reinstall MoStream TV.",
                _archivePath);

        Directory.CreateDirectory(_installDirectory);
        await Task.Run(() => ZipFile.ExtractToDirectory(_archivePath, _installDirectory, true), cancellationToken);

        if (!File.Exists(ServerExecutable) || !File.Exists(ClientExecutable))
            throw new InvalidDataException("The Syncplay package did not contain the expected executables.");
    }
}
