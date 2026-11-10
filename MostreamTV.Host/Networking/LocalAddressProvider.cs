using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MostreamTV.Host.Networking;

internal static class LocalAddressProvider
{
    private static readonly string[] VirtualAdapterHints =
    [
        "hamachi",
        "zerotier",
        "tailscale",
        "wireguard",
        "wintun",
        "tap-",
        "tun",
        "vpn",
        "vethernet",
        "hyper-v",
        "wsl",
        "virtualbox",
        "vmware",
        "loopback",
    ];

    public static string GetPreferredIpv4Address()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .Where(network => network.NetworkInterfaceType is not NetworkInterfaceType.Loopback)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses
                .Select(unicast => unicast.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Where(address => !IPAddress.IsLoopback(address))
                .Where(address => !address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                .Select(address => (Score: ScoreCandidate(network, address), Address: address)))
            .OrderByDescending(candidate => candidate.Score)
            .ToList();

        return candidates.FirstOrDefault().Address?.ToString() ?? "your-computer-ip";
    }

    private static int ScoreCandidate(NetworkInterface network, IPAddress address)
    {
        var score = 0;
        var descriptor = $"{network.Name} {network.Description}";
        var isVirtual = network.NetworkInterfaceType is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp
            || VirtualAdapterHints.Any(hint => descriptor.Contains(hint, StringComparison.OrdinalIgnoreCase));

        if (!isVirtual)
            score += 100;

        if (network.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet)
            score += 30;

        var bytes = address.GetAddressBytes();
        var isPrivateLan = bytes[0] == 192 && bytes[1] == 168
            || bytes[0] == 10
            || bytes[0] == 172 && bytes[1] is >= 16 and <= 31;

        if (isPrivateLan)
            score += 20;

        // De-prioritize Hamachi 25.0.0.0/8 addresses when a real LAN adapter exists.
        if (bytes[0] == 25)
            score -= 80;

        return score;
    }
}
