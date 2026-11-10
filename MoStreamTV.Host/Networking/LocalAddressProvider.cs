using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MoStreamTV.Host.Networking;

internal static class LocalAddressProvider
{
    public static string GetPreferredIpv4Address()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .Where(network => network.NetworkInterfaceType is not NetworkInterfaceType.Loopback)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
            .Where(address => !IPAddress.IsLoopback(address))
            .Where(address => !address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .ToList();

        return candidates.FirstOrDefault()?.ToString() ?? "your-computer-ip";
    }
}
