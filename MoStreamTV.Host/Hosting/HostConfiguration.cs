using System.Net;
using System.Net.Sockets;

namespace MoStreamTV.Host.Hosting;

internal enum WatchPartyMode
{
    Host = 0,
    Join = 1,
}

internal sealed record HostConfiguration(
    WatchPartyMode Mode,
    string ServerHost,
    string RoomName,
    string HostName,
    string Password,
    int Port,
    string MediaPath,
    string PlayerPath)
{
    private static readonly HashSet<string> SupportedPlayerExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "vlc.exe",
        "vlcportable.exe",
        "mpv.exe",
        "mpvnet.exe",
        "mpc-hc.exe",
        "mpc-hc64.exe",
        "mpc-hcportable.exe",
        "mpc-hc_nvo.exe",
        "mpc-hc64_nvo.exe",
        "shoukaku.exe",
        "mpc-be.exe",
        "mpc-be64.exe",
        "mplayer.exe",
        "mplayer2.exe",
        "memento.exe",
    };

    public bool IsHost => Mode == WatchPartyMode.Host;

    public string EffectiveHost
    {
        get
        {
            if (IsHost)
                return "127.0.0.1";
            var (host, _) = SplitHostAndPort(ServerHost, Port);
            return host;
        }
    }

    public int EffectivePort
    {
        get
        {
            if (IsHost)
                return Port;
            var (_, parsedPort) = SplitHostAndPort(ServerHost, Port);
            return parsedPort;
        }
    }

    public string FormatTargetEndpoint()
    {
        var host = EffectiveHost;
        var port = EffectivePort;
        if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6 && !host.StartsWith('['))
            return $"[{host}]:{port}";
        return $"{host}:{port}";
    }

    public void Validate()
    {
        if (!IsHost)
        {
            if (string.IsNullOrWhiteSpace(ServerHost))
                throw new ArgumentException("Enter your friend's server IP address or hostname.", nameof(ServerHost));

            var (host, port) = SplitHostAndPort(ServerHost, Port);
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("Enter a valid server address.", nameof(ServerHost));
            if (port is < 1024 or > 65535)
                throw new ArgumentOutOfRangeException(nameof(Port), "Choose a port from 1024 to 65535.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Password))
                throw new ArgumentException("Enter a server password.", nameof(Password));
            if (Port is < 1024 or > 65535)
                throw new ArgumentOutOfRangeException(nameof(Port), "Choose a port from 1024 to 65535.");
        }

        if (string.IsNullOrWhiteSpace(RoomName))
            throw new ArgumentException("Enter a room name.", nameof(RoomName));
        if (string.IsNullOrWhiteSpace(HostName))
            throw new ArgumentException("Enter your display name.", nameof(HostName));

        if (!string.IsNullOrWhiteSpace(MediaPath) && !File.Exists(MediaPath))
            throw new FileNotFoundException("The selected movie could not be found.", MediaPath);

        if (string.IsNullOrWhiteSpace(PlayerPath))
            throw new ArgumentException("Choose a supported media player (VLC, mpv, mpv.net, MPC-HC, or MPC-BE).", nameof(PlayerPath));
        if (!File.Exists(PlayerPath))
            throw new FileNotFoundException("The selected media player could not be found.", PlayerPath);

        var playerExecutable = Path.GetFileName(PlayerPath);
        if (!SupportedPlayerExecutables.Contains(playerExecutable))
            throw new ArgumentException(
                "Choose a supported media player executable (vlc.exe, mpv.exe, mpvnet.exe, mpc-hc64.exe, mpc-be64.exe, or mplayer.exe).",
                nameof(PlayerPath));

        if (playerExecutable.StartsWith("mplayer", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(MediaPath))
            throw new ArgumentException("MPlayer requires choosing a movie file before starting.", nameof(MediaPath));
    }

    public static (string Host, int Port) SplitHostAndPort(string rawHost, int fallbackPort)
    {
        var trimmed = rawHost.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return (string.Empty, fallbackPort);

        if (trimmed.StartsWith('['))
        {
            var closeIndex = trimmed.IndexOf(']');
            if (closeIndex > 1)
            {
                var ipv6 = trimmed[1..closeIndex];
                if (closeIndex + 1 < trimmed.Length && trimmed[closeIndex + 1] == ':'
                    && int.TryParse(trimmed[(closeIndex + 2)..], out var parsedV6Port))
                {
                    return (ipv6, parsedV6Port);
                }
                return (ipv6, fallbackPort);
            }
        }

        var firstColon = trimmed.IndexOf(':');
        var lastColon = trimmed.LastIndexOf(':');
        if (firstColon > 0 && firstColon == lastColon)
        {
            var hostPart = trimmed[..lastColon].Trim();
            var portPart = trimmed[(lastColon + 1)..].Trim();
            if (int.TryParse(portPart, out var parsedPort))
                return (hostPart, parsedPort);
        }

        return (trimmed, fallbackPort);
    }

    public static string FindDefaultMediaPlayer()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidates = new[]
        {
            Path.Combine(programFiles, "VideoLAN", "VLC", "vlc.exe"),
            Path.Combine(programFilesX86, "VideoLAN", "VLC", "vlc.exe"),
            Path.Combine(programFiles, "mpv", "mpv.exe"),
            Path.Combine(programFiles, "mpv-player", "mpv.exe"),
            Path.Combine(localAppData, "Microsoft", "WindowsApps", "mpv.exe"),
            Path.Combine(programFiles, "mpv.net", "mpvnet.exe"),
            Path.Combine(localAppData, "Programs", "mpv.net", "mpvnet.exe"),
            Path.Combine(localAppData, "Microsoft", "WindowsApps", "mpvnet.exe"),
            Path.Combine(programFiles, "MPC-HC", "mpc-hc64.exe"),
            Path.Combine(programFilesX86, "MPC-HC", "mpc-hc.exe"),
            Path.Combine(programFiles, "MPC-BE x64", "mpc-be64.exe"),
            Path.Combine(programFiles, "MPC-BE", "mpc-be64.exe"),
            Path.Combine(programFilesX86, "MPC-BE", "mpc-be.exe"),
        };

        var directMatch = candidates.FirstOrDefault(File.Exists);
        if (directMatch is not null)
            return directMatch;

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var executable in new[] { "vlc.exe", "mpv.exe", "mpvnet.exe", "mpc-hc64.exe", "mpc-be64.exe" })
        {
            foreach (var directory in pathEntries)
            {
                var candidate = Path.Combine(directory, executable);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return string.Empty;
    }
}
