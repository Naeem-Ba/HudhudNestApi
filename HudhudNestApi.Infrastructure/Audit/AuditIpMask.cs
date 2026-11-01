using System.Net;
using System.Net.Sockets;

namespace HudhudNestApi.Infrastructure.Audit;

/// <summary>
/// Phase 14 (D07): the audit log keeps a masked address, enough to tell networks apart and spot a pattern
/// without storing a full personal address indefinitely. IPv4 loses its last octet (203.0.113.0), IPv6 keeps
/// its first 48 bits (2001:db8:1::). Anything that is not an IP address is dropped rather than stored raw.
/// </summary>
public static class AuditIpMask
{
    public static string? Mask(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return null;

        var text = ipAddress.Trim();

        // IPAddress.TryParse also accepts shorthand such as "1.2.3" or "16909060"; only real dotted quads count.
        if (!text.Contains(':') && text.Count(c => c == '.') != 3)
            return null;

        if (!IPAddress.TryParse(text, out var ip))
            return null;

        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        var bytes = ip.GetAddressBytes();
        switch (ip.AddressFamily)
        {
            case AddressFamily.InterNetwork:
                bytes[3] = 0;
                break;
            case AddressFamily.InterNetworkV6:
                for (var i = 6; i < bytes.Length; i++)
                    bytes[i] = 0;
                break;
            default:
                return null;
        }

        return new IPAddress(bytes).ToString();
    }
}
