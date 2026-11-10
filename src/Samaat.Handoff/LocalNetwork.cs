using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Samaat.Core.Diagnostics;

namespace Samaat.Handoff;

/// <summary>Whether an address is on the same subnet as one of this computer's network interfaces.</summary>
public static class LocalNetwork
{
    private static int _warned;

    public static bool IsOnLink(IPAddress remote, IReadOnlyList<(IPAddress Address, IPAddress Mask)> interfaces)
    {
        if (IPAddress.IsLoopback(remote))
        {
            return true;
        }

        if (remote.IsIPv4MappedToIPv6)
        {
            remote = remote.MapToIPv4();
        }

        if (remote.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var wanted = remote.GetAddressBytes();
        foreach (var (address, mask) in interfaces)
        {
            if (address.AddressFamily != AddressFamily.InterNetwork || mask.AddressFamily != AddressFamily.InterNetwork)
            {
                continue;
            }

            var ours = address.GetAddressBytes();
            var maskBytes = mask.GetAddressBytes();
            if (maskBytes.All(b => b == 0))
            {
                continue;
            }

            var same = true;
            for (var i = 0; i < 4; i++)
            {
                same &= (wanted[i] & maskBytes[i]) == (ours[i] & maskBytes[i]);
            }

            if (same)
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<(IPAddress Address, IPAddress Mask)> Interfaces()
    {
        var result = new List<(IPAddress, IPAddress)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && unicast.IPv4Mask is { } mask)
                {
                    result.Add((unicast.Address, mask));
                }
            }
        }

        return result;
    }

    /// <summary>Fails open if the interfaces can't be read: this is defence in depth, and refusing would silently break a paired phone.</summary>
    public static bool IsLocal(IPAddress remote)
    {
        try
        {
            return IsOnLink(remote, Interfaces());
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _warned, 1) == 0)
            {
                SamaatLog.Warn("Phone link", "Couldn't read the network interfaces; accepting phone connections from any address", ex);
            }

            return true;
        }
    }
}
