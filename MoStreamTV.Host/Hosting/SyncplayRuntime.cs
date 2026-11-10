using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace MoStreamTV.Host.Hosting;

internal sealed class SyncplayRuntime
{
    private const string Version = "1.7.6";
    private const string BridgeResourceName = "MoStreamTV.Host.Bridge.consoleUI.py";
    private const string BridgeEntryPy = "syncplay/ui/consoleUI.py";
    private const string BridgeEntryPyc = "syncplay/ui/consoleUI.pyc";

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
    public string ConsoleClientExecutable => Path.Combine(_installDirectory, "SyncplayConsole.exe");
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
        if (File.Exists(ServerExecutable) && File.Exists(ConsoleClientExecutable))
            return;

        if (!File.Exists(_archivePath))
            throw new FileNotFoundException(
                "The bundled watch-party runtime is missing. Rebuild or reinstall MoStream TV.",
                _archivePath);

        Directory.CreateDirectory(_installDirectory);
        await Task.Run(() => ZipFile.ExtractToDirectory(_archivePath, _installDirectory, true), cancellationToken);

        if (!File.Exists(ServerExecutable) || !File.Exists(ConsoleClientExecutable))
            throw new InvalidDataException("The watch-party runtime package did not contain the expected executables.");
    }

    public async Task PrepareBridgeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAvailableAsync(cancellationToken);

        var configPath = Path.Combine(_installDirectory, "syncplay.ini");
        var contents = File.Exists(configPath)
            ? await File.ReadAllTextAsync(configPath, cancellationToken)
            : string.Empty;
        var updated = UpsertIniValue(contents, "client_settings", "forceguiprompt", "False");
        updated = UpsertIniValue(updated, "general", "checkforupdatesautomatically", "False");
        if (!string.Equals(contents, updated, StringComparison.Ordinal))
            await File.WriteAllTextAsync(configPath, updated, cancellationToken);

        var bridgeScript = LoadEmbeddedBridgeScript();
        var bridgeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bridgeScript)));
        var markerPath = Path.Combine(_installDirectory, ".mostream-bridge-version");
        var libZipPath = Path.Combine(_installDirectory, "lib", "libsync.zip");

        if (!File.Exists(libZipPath))
            throw new FileNotFoundException("The watch-party runtime library archive is missing.", libZipPath);

        var installedMarker = File.Exists(markerPath)
            ? (await File.ReadAllTextAsync(markerPath, cancellationToken)).Trim()
            : string.Empty;

        if (string.Equals(installedMarker, bridgeHash, StringComparison.Ordinal))
            return;

        await Task.Run(() =>
        {
            using var archive = ZipFile.Open(libZipPath, ZipArchiveMode.Update);
            archive.GetEntry(BridgeEntryPyc)?.Delete();
            archive.GetEntry(BridgeEntryPy)?.Delete();

            var entry = archive.CreateEntry(BridgeEntryPy, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(bridgeScript);
        }, cancellationToken);

        await File.WriteAllTextAsync(markerPath, bridgeHash, cancellationToken);
    }

    private static string LoadEmbeddedBridgeScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(BridgeResourceName)
            ?? throw new InvalidOperationException($"Missing embedded bridge resource '{BridgeResourceName}'.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string UpsertIniValue(string contents, string section, string key, string value)
    {
        var newline = contents.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = contents.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var sectionHeader = $"[{section}]";
        var sectionIndex = lines.FindIndex(line => string.Equals(line.Trim(), sectionHeader, StringComparison.OrdinalIgnoreCase));

        if (sectionIndex < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add(string.Empty);
            lines.Add(sectionHeader);
            lines.Add($"{key} = {value}");
        }
        else
        {
            var nextSection = lines.FindIndex(sectionIndex + 1, line =>
                line.TrimStart().StartsWith("[", StringComparison.Ordinal) && line.TrimEnd().EndsWith("]", StringComparison.Ordinal));
            if (nextSection < 0)
                nextSection = lines.Count;

            var keyIndex = lines.FindIndex(sectionIndex + 1, nextSection - sectionIndex - 1, line =>
            {
                var separator = line.IndexOf('=');
                return separator >= 0 && string.Equals(line[..separator].Trim(), key, StringComparison.OrdinalIgnoreCase);
            });

            if (keyIndex >= 0)
                lines[keyIndex] = $"{key} = {value}";
            else
                lines.Insert(nextSection, $"{key} = {value}");
        }

        return string.Join(newline, lines);
    }
}
