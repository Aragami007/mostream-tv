using System.Security;
using Microsoft.Win32;

namespace MostreamTV.Host.Hosting;

internal sealed record DetectedMediaPlayer(
    string DisplayName,
    string ExecutablePath,
    bool IsSystemDefault = false)
{
    public string DropdownLabel => IsSystemDefault
        ? $"{DisplayName} (Windows default) — {ExecutablePath}"
        : $"{DisplayName} — {ExecutablePath}";

    public override string ToString() => DropdownLabel;
}

internal static class MediaPlayerCatalog
{
    private static readonly Dictionary<string, string> ExecutableDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vlc.exe"] = "VLC media player",
        ["vlcportable.exe"] = "VLC Portable",
        ["mpv.exe"] = "mpv",
        ["mpvnet.exe"] = "mpv.net",
        ["mpc-hc64.exe"] = "MPC-HC (64-bit)",
        ["mpc-hc.exe"] = "MPC-HC (32-bit)",
        ["mpc-hc64_nvo.exe"] = "MPC-HC (64-bit)",
        ["mpc-hc_nvo.exe"] = "MPC-HC (32-bit)",
        ["mpc-hcportable.exe"] = "MPC-HC Portable",
        ["mpc-be64.exe"] = "MPC-BE (64-bit)",
        ["mpc-be.exe"] = "MPC-BE (32-bit)",
        ["shoukaku.exe"] = "Shoukaku (MPC-HC)",
        ["memento.exe"] = "Memento",
        ["mplayer.exe"] = "MPlayer",
        ["mplayer2.exe"] = "MPlayer2",
        ["wmplayer.exe"] = "Windows Media Player",
    };

    private static readonly (string RelativeFolder, string[] Executables)[] KnownRelativePlayerLocations =
    [
        (Path.Combine("VideoLAN", "VLC"), ["vlc.exe", "vlcportable.exe"]),
        ("VLC", ["vlc.exe", "vlcportable.exe"]),
        ("Windows Media Player", ["wmplayer.exe"]),
        ("mpv", ["mpv.exe"]),
        ("mpv-player", ["mpv.exe"]),
        ("mpv-x86_64", ["mpv.exe"]),
        ("mpv.net", ["mpvnet.exe"]),
        ("MPC-HC", ["mpc-hc64.exe", "mpc-hc.exe", "mpc-hc64_nvo.exe", "mpc-hc_nvo.exe", "mpc-hcportable.exe"]),
        ("MPC-HC Portable", ["mpc-hcportable.exe", "mpc-hc64.exe", "mpc-hc.exe"]),
        ("Media Player Classic - Home Cinema", ["mpc-hc64.exe", "mpc-hc.exe"]),
        ("MPC HomeCinema", ["mpc-hc64.exe", "mpc-hc.exe"]),
        (Path.Combine("Combined Community Codec Pack", "MPC"), ["mpc-hc.exe", "mpc-hc64.exe"]),
        (Path.Combine("K-Lite Codec Pack", "MPC-HC64"), ["mpc-hc64.exe"]),
        (Path.Combine("K-Lite Codec Pack", "MPC-HC"), ["mpc-hc.exe"]),
        (Path.Combine("K-Lite Codec Pack", "Media Player Classic"), ["mpc-hc.exe", "mpc-hc64.exe"]),
        (Path.Combine("LAV Filters", "x64", "mpc-hc"), ["shoukaku.exe"]),
        (Path.Combine("LAV Filters", "x86", "mpc-hc"), ["shoukaku.exe"]),
        ("MPC-BE x64", ["mpc-be64.exe", "mpc-be.exe"]),
        ("MPC-BE", ["mpc-be64.exe", "mpc-be.exe"]),
        ("Memento", ["memento.exe"]),
        ("MPlayer", ["mplayer.exe", "mplayer2.exe"]),
        ("MPlayer for Windows", ["mplayer.exe", "mplayer2.exe"]),
        ("Shoukaku", ["shoukaku.exe"]),
    ];

    private static readonly string[] PlayerKeywordFilters =
    [
        "vlc",
        "windows media player",
        "wmplayer",
        "mpv",
        "mpc",
        "media player classic",
        "k-lite",
        "shoukaku",
        "memento",
        "mplayer",
    ];

    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".avi", ".webm", ".mov"];

    private static string PreferredPlayerFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MoStreamTV",
        "player.txt");

    public static bool IsSupportedExecutable(string? executableOrPath)
    {
        if (string.IsNullOrWhiteSpace(executableOrPath))
            return false;

        var fileName = Path.GetFileName(executableOrPath.Trim());
        if (string.IsNullOrEmpty(fileName) || !ExecutableDisplayNames.ContainsKey(fileName))
            return false;

        return !string.Equals(fileName, "wmplayer.exe", StringComparison.OrdinalIgnoreCase)
            || IsWindowsMediaPlayerEngineAvailable();
    }

    public static bool IsWindowsMediaPlayerExecutable(string? executableOrPath) =>
        !string.IsNullOrWhiteSpace(executableOrPath)
        && string.Equals(Path.GetFileName(executableOrPath.Trim()), "wmplayer.exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsWindowsMediaPlayerEngineAvailable()
    {
        try
        {
            using var clsidKey = Registry.ClassesRoot.OpenSubKey(@"CLSID\{6BF52A52-394A-11d3-B153-00C04F79FAA6}\InprocServer32");
            var dllPath = clsidKey?.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(dllPath))
            {
                var expanded = Environment.ExpandEnvironmentVariables(dllPath.Trim().Trim('"'));
                return File.Exists(expanded);
            }
        }
        catch
        {
        }

        return File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wmp.dll"));
    }

    public static string GetDisplayName(string executableOrPath)
    {
        var fileName = Path.GetFileName(executableOrPath.Trim());
        return ExecutableDisplayNames.TryGetValue(fileName, out var name)
            ? name
            : Path.GetFileNameWithoutExtension(fileName);
    }

    public static string? LoadPreferredPlayerPath()
    {
        try
        {
            if (File.Exists(PreferredPlayerFilePath))
            {
                var saved = File.ReadAllText(PreferredPlayerFilePath).Trim();
                return string.IsNullOrWhiteSpace(saved) ? null : saved;
            }
        }
        catch
        {
        }

        return null;
    }

    public static void SavePreferredPlayerPath(string? executablePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(executablePath))
                return;

            var directory = Path.GetDirectoryName(PreferredPlayerFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(PreferredPlayerFilePath, Path.GetFullPath(executablePath.Trim()));
        }
        catch
        {
        }
    }

    public static string FindDefaultPlayerPath()
    {
        var preferred = LoadPreferredPlayerPath();
        var detected = DetectInstalledPlayers(preferred);
        return ResolveInitialSelection(detected, preferred)?.ExecutablePath ?? string.Empty;
    }

    public static DetectedMediaPlayer? ResolveInitialSelection(
        IReadOnlyList<DetectedMediaPlayer> players,
        string? preferredPath)
    {
        if (players.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            var normalizedPreferred = TryNormalizePath(preferredPath);
            var match = players.FirstOrDefault(p =>
                string.Equals(p.ExecutablePath, normalizedPreferred ?? preferredPath.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        return players.FirstOrDefault(p => p.IsSystemDefault) ?? players[0];
    }

    public static IReadOnlyList<DetectedMediaPlayer> DetectInstalledPlayers(string? additionalPlayerPath = null)
    {
        var windowsDefaultPath = DetectWindowsDefaultSupportedPlayerPath();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var discovered = new List<DetectedMediaPlayer>();

        void AddCandidate(string? rawPath)
        {
            var extracted = TryExtractExecutablePath(rawPath) ?? rawPath;
            if (!TryNormalizeSupportedExistingPath(extracted, out var fullPath))
                return;

            if (!seen.Add(fullPath))
                return;

            var isDefault = !string.IsNullOrEmpty(windowsDefaultPath)
                && string.Equals(fullPath, windowsDefaultPath, StringComparison.OrdinalIgnoreCase);
            discovered.Add(new DetectedMediaPlayer(GetDisplayName(fullPath), fullPath, isDefault));
        }

        AddCandidate(windowsDefaultPath);
        ScanKnownDirectories(AddCandidate);
        ScanAppPathsRegistry(AddCandidate);
        ScanFileAssociationsAndApplications(AddCandidate);
        ScanUninstallRegistry(AddCandidate);
        ScanPathEnvironment(AddCandidate);
        AddCandidate(additionalPlayerPath);

        return CollapseDuplicateWindowsMediaPlayer(discovered, additionalPlayerPath)
            .OrderByDescending(p => p.IsSystemDefault)
            .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<DetectedMediaPlayer> CollapseDuplicateWindowsMediaPlayer(
        List<DetectedMediaPlayer> discovered,
        string? explicitPlayerPath)
    {
        var wmpEntries = discovered.Where(p => IsWindowsMediaPlayerExecutable(p.ExecutablePath)).ToList();
        if (wmpEntries.Count <= 1)
            return discovered;

        var normalizedExplicit = TryNormalizePath(explicitPlayerPath);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var preferredWmp = wmpEntries.FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(normalizedExplicit)
                && string.Equals(p.ExecutablePath, normalizedExplicit, StringComparison.OrdinalIgnoreCase))
            ?? wmpEntries.FirstOrDefault(p =>
                string.IsNullOrWhiteSpace(programFilesX86)
                || !p.ExecutablePath.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase))
            ?? wmpEntries[0];

        var anyDefault = wmpEntries.Any(p => p.IsSystemDefault);
        var mergedWmp = preferredWmp with { IsSystemDefault = anyDefault };

        return discovered
            .Where(p => !IsWindowsMediaPlayerExecutable(p.ExecutablePath))
            .Append(mergedWmp);
    }

    private static string? DetectWindowsDefaultSupportedPlayerPath()
    {
        foreach (var extension in VideoExtensions)
        {
            var progId = ReadRegistryString(
                RegistryHive.CurrentUser,
                RegistryView.Default,
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\UserChoice",
                "ProgId");

            if (string.IsNullOrWhiteSpace(progId))
                continue;

            var command = ReadClassesRootCommand(progId);
            var exePath = TryExtractExecutablePath(command);
            if (TryNormalizeSupportedExistingPath(exePath, out var normalized))
                return normalized;
        }

        return null;
    }

    private static void ScanKnownDirectories(Action<string?> addCandidate)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var roots = new[]
        {
            programFiles,
            programFilesX86,
            Path.Combine(localAppData, "Programs"),
            localAppData,
        }
        .Where(dir => !string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        foreach (var root in roots)
        {
            foreach (var (relativeFolder, executables) in KnownRelativePlayerLocations)
            {
                var folder = Path.Combine(root, relativeFolder);
                if (!Directory.Exists(folder))
                    continue;

                foreach (var executable in executables)
                    addCandidate(Path.Combine(folder, executable));
            }
        }

        var directBinDirs = new[]
        {
            AppContext.BaseDirectory,
            Path.Combine(localAppData, "Microsoft", "WindowsApps"),
            Path.Combine(programData, "chocolatey", "bin"),
            Path.Combine(userProfile, "scoop", "shims"),
        };

        foreach (var dir in directBinDirs)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                continue;

            foreach (var executable in ExecutableDisplayNames.Keys)
                addCandidate(Path.Combine(dir, executable));
        }

        ScanPackageManagerDirectory(Path.Combine(localAppData, "Microsoft", "WinGet", "Packages"), addCandidate);
        ScanPackageManagerDirectory(Path.Combine(userProfile, "scoop", "apps"), addCandidate);
    }

    private static void ScanPackageManagerDirectory(string rootDir, Action<string?> addCandidate)
    {
        if (string.IsNullOrWhiteSpace(rootDir) || !Directory.Exists(rootDir))
            return;

        try
        {
            foreach (var packageDir in Directory.EnumerateDirectories(rootDir))
            {
                var dirName = Path.GetFileName(packageDir);
                if (!ContainsPlayerKeyword(dirName))
                    continue;

                SearchDirectoryShallow(packageDir, maxDepth: 2, addCandidate);
            }
        }
        catch
        {
        }
    }

    private static void SearchDirectoryShallow(string directory, int maxDepth, Action<string?> addCandidate)
    {
        foreach (var executable in ExecutableDisplayNames.Keys)
            addCandidate(Path.Combine(directory, executable));

        if (maxDepth <= 0)
            return;

        try
        {
            foreach (var subDir in Directory.EnumerateDirectories(directory))
                SearchDirectoryShallow(subDir, maxDepth - 1, addCandidate);
        }
        catch
        {
        }
    }

    private static void ScanAppPathsRegistry(Action<string?> addCandidate)
    {
        foreach (var (hive, view) in GetRegistryScopes())
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var appPathsKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
                if (appPathsKey is null)
                    continue;

                foreach (var executable in ExecutableDisplayNames.Keys)
                {
                    using var subKey = appPathsKey.OpenSubKey(executable);
                    addCandidate(subKey?.GetValue(null) as string);
                }
            }
            catch
            {
            }
        }
    }

    private static void ScanFileAssociationsAndApplications(Action<string?> addCandidate)
    {
        foreach (var extension in VideoExtensions)
        {
            try
            {
                using var openWithKey = Registry.CurrentUser.OpenSubKey(
                    $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\OpenWithProgids");
                if (openWithKey is null)
                    continue;

                foreach (var progId in openWithKey.GetValueNames())
                {
                    if (!string.IsNullOrWhiteSpace(progId))
                        addCandidate(ReadClassesRootCommand(progId));
                }
            }
            catch
            {
            }
        }

        foreach (var executable in ExecutableDisplayNames.Keys)
            addCandidate(ReadClassesRootCommand($@"Applications\{executable}"));
    }

    private static void ScanUninstallRegistry(Action<string?> addCandidate)
    {
        foreach (var (hive, view) in GetRegistryScopes())
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstallKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstallKey is null)
                    continue;

                foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                {
                    using var entryKey = uninstallKey.OpenSubKey(subKeyName);
                    if (entryKey is null)
                        continue;

                    var displayName = entryKey.GetValue("DisplayName") as string;
                    if (!ContainsPlayerKeyword(displayName) && !ContainsPlayerKeyword(subKeyName))
                        continue;

                    addCandidate(entryKey.GetValue("DisplayIcon") as string);

                    var installLocation = TryNormalizePath(entryKey.GetValue("InstallLocation") as string);
                    if (!string.IsNullOrWhiteSpace(installLocation)
                        && !string.Equals(Path.GetPathRoot(installLocation), installLocation, StringComparison.OrdinalIgnoreCase)
                        && Directory.Exists(installLocation))
                        SearchDirectoryShallow(installLocation, maxDepth: 1, addCandidate);
                }
            }
            catch
            {
            }
        }
    }

    private static void ScanPathEnvironment(Action<string?> addCandidate)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
            return;

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var executable in ExecutableDisplayNames.Keys)
                addCandidate(Path.Combine(directory, executable));
        }
    }

    private static IEnumerable<(RegistryHive Hive, RegistryView View)> GetRegistryScopes() =>
    [
        (RegistryHive.CurrentUser, RegistryView.Default),
        (RegistryHive.LocalMachine, RegistryView.Registry64),
        (RegistryHive.LocalMachine, RegistryView.Registry32),
    ];

    private static string? ReadClassesRootCommand(string progId)
    {
        try
        {
            using var commandKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
            return commandKey?.GetValue(null) as string;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadRegistryString(RegistryHive hive, RegistryView view, string subKeyPath, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var subKey = baseKey.OpenSubKey(subKeyPath);
            return subKey?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    private static bool ContainsPlayerKeyword(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (var keyword in PlayerKeywordFilters)
        {
            if (value.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? TryExtractExecutablePath(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        var expanded = Environment.ExpandEnvironmentVariables(rawValue.Trim());
        if (string.IsNullOrEmpty(expanded))
            return null;

        if (expanded.StartsWith('"'))
        {
            var closeQuote = expanded.IndexOf('"', 1);
            if (closeQuote > 1)
                return expanded[1..closeQuote].Trim();
        }

        var searchIndex = 0;
        while (searchIndex < expanded.Length)
        {
            var exeIndex = expanded.IndexOf(".exe", searchIndex, StringComparison.OrdinalIgnoreCase);
            if (exeIndex < 0)
                break;

            var endIndex = exeIndex + 4;
            if (endIndex == expanded.Length || expanded[endIndex] is ' ' or '\t' or ',' or '/' or '-')
                return expanded[..endIndex].Trim();

            searchIndex = endIndex;
        }

        return expanded.Trim('"', ' ');
    }

    private static bool TryNormalizeSupportedExistingPath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        var fullPath = TryNormalizePath(path);
        if (string.IsNullOrEmpty(fullPath))
            return false;

        if (!IsSupportedExecutable(fullPath))
            return false;

        if (!File.Exists(fullPath))
            return false;

        normalizedPath = fullPath;
        return true;
    }

    private static string? TryNormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(path.Trim().Trim('"'));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
        {
            return null;
        }
    }
}
