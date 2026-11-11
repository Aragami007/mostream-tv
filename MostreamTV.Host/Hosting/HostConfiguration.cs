using System.Net;
using System.Net.Sockets;
using MostreamTV.Host.Networking;

namespace MostreamTV.Host.Hosting;

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
    string PlayerPath,
    bool ShareOverInternet = true)
{
    public bool IsHost => Mode == WatchPartyMode.Host;
    public bool IsTunnelJoin => !IsHost && WebSocketTunnelAddress.LooksLikeTunnelAddress(ServerHost);

    public bool TryGetTunnelUri(out Uri? uri, out string? error)
    {
        if (IsHost)
        {
            uri = null;
            error = null;
            return false;
        }

        return WebSocketTunnelAddress.TryNormalize(ServerHost, out uri, out error);
    }

    public string EffectiveHost
    {
        get
        {
            if (IsHost)
                return "127.0.0.1";
            if (TryGetTunnelUri(out var uri, out _) && uri is not null)
                return uri.Host;
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
            if (TryGetTunnelUri(out var uri, out _) && uri is not null)
                return uri.Port;
            var (_, parsedPort) = SplitHostAndPort(ServerHost, Port);
            return parsedPort;
        }
    }

    public string FormatTargetEndpoint()
    {
        if (!IsHost && TryGetTunnelUri(out var uri, out _) && uri is not null)
            return WebSocketTunnelAddress.FormatDisplayUrl(uri);

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
                throw new ArgumentException("Enter your friend's invite link (wss://...) or server IP/hostname.", nameof(ServerHost));

            if (IsTunnelJoin)
            {
                if (!TryGetTunnelUri(out var tunnelUri, out var tunnelError) || tunnelUri is null)
                    throw new ArgumentException(tunnelError ?? "Enter a valid invite link (for example wss://example.trycloudflare.com).", nameof(ServerHost));
            }
            else
            {
                var (host, port) = SplitHostAndPort(ServerHost, Port);
                if (string.IsNullOrWhiteSpace(host))
                    throw new ArgumentException("Enter a valid server address.", nameof(ServerHost));
                if (port is < 1024 or > 65535)
                    throw new ArgumentOutOfRangeException(nameof(Port), "Choose a port from 1024 to 65535.");
            }
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
            throw new ArgumentException("Choose a supported media player (VLC, Windows Media Player, mpv, mpv.net, MPC-HC, or MPC-BE).", nameof(PlayerPath));
        if (!File.Exists(PlayerPath))
            throw new FileNotFoundException("The selected media player could not be found.", PlayerPath);

        var playerExecutable = Path.GetFileName(PlayerPath);
        if (!MediaPlayerCatalog.IsSupportedExecutable(playerExecutable))
            throw new ArgumentException(
                "Choose a supported media player executable (vlc.exe, wmplayer.exe, mpv.exe, mpvnet.exe, mpc-hc64.exe, mpc-be64.exe, memento.exe, or mplayer.exe).",
                nameof(PlayerPath));

        if (playerExecutable.StartsWith("mplayer", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(MediaPath))
            throw new ArgumentException("MPlayer requires choosing a movie file before starting.", nameof(MediaPath));
    }

    public static (string Host, int Port) SplitHostAndPort(string rawHost, int fallbackPort)
    {
        var trimmed = rawHost.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return (string.Empty, fallbackPort);

        if (WebSocketTunnelAddress.LooksLikeTunnelAddress(trimmed))
        {
            if (WebSocketTunnelAddress.TryNormalize(trimmed, out var tunnelUri, out _) && tunnelUri is not null)
                return (WebSocketTunnelAddress.FormatDisplayUrl(tunnelUri), fallbackPort);
            return (trimmed, fallbackPort);
        }

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

    public static string FindDefaultMediaPlayer() => MediaPlayerCatalog.FindDefaultPlayerPath();
}
