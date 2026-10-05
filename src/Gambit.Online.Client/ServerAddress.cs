using System.Net;
using System.Net.Sockets;

namespace Gambit.Online.Client;

/// <summary>What a server address says about how safely a password reaches it.</summary>
public static class ServerAddress
{
    /// <summary>
    /// True for an http:// address on the open internet: a password sent there can be read on the
    /// way. HTTPS, this PC, home and office networks (private addresses, .local names, plain computer
    /// names) and Tailscale (100.64.0.0/10 and *.ts.net, which encrypts by itself) don't count.
    /// </summary>
    public static bool IsUnencryptedOverInternet(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttp) return false;
        if (uri.IsLoopback) return false;
        string host = uri.IdnHost.Trim('[', ']');
        if (IPAddress.TryParse(host, out IPAddress? ip)) return !IsPrivate(ip);
        if (!host.Contains('.')) return false; // a computer name on the local network
        string[] localSuffixes = [".local", ".lan", ".home", ".home.arpa", ".internal", ".ts.net"];
        return !localSuffixes.Any(s => host.EndsWith(s, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPrivate(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal) return true;
            if (!ip.IsIPv4MappedToIPv6) return false;
            ip = ip.MapToIPv4();
        }
        byte[] b = ip.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] is >= 64 and <= 127); // Tailscale (and carrier-grade NAT)
    }
}
