using System.Net;
using System.Net.Sockets;

namespace KnowledgeHub.Api.Modules.Ingestion;

/// <summary>Решает, можно ли подключаться к IP-адресу при скачивании страниц пользователя (защита от SSRF).</summary>
public static class SsrfGuard
{
    public static bool IsAllowed(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // ::ffff:127.0.0.1 — это тот же loopback, проверяем как IPv4.
            return address.IsIPv4MappedToIPv6 ? IsAllowed(address.MapToIPv4()) : IsAllowedV6(address);
        }

        return address.AddressFamily == AddressFamily.InterNetwork && IsAllowedV4(address.GetAddressBytes());
    }

    private static bool IsAllowedV4(byte[] b) =>
        !(b[0] == 0                                    // 0.0.0.0/8
          || b[0] == 10                                // 10.0.0.0/8
          || b[0] == 100 && b[1] is >= 64 and <= 127   // CGNAT 100.64.0.0/10
          || b[0] == 127                               // loopback
          || b[0] == 169 && b[1] == 254                // link-local, в т.ч. 169.254.169.254
          || b[0] == 172 && b[1] is >= 16 and <= 31    // 172.16.0.0/12
          || b[0] == 192 && b[1] == 168                // 192.168.0.0/16
          || b[0] >= 224);                             // multicast 224/4, зарезервированные 240/4 и broadcast

    private static bool IsAllowedV6(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return !(address.Equals(IPAddress.IPv6Any)
                 || IPAddress.IsLoopback(address)
                 || address.IsIPv6LinkLocal                       // fe80::/10
                 || address.IsIPv6SiteLocal                       // fec0::/10
                 || address.IsIPv6Multicast
                 || (b[0] & 0xFE) == 0xFC                         // unique local fc00::/7
                 || IsNat64(b));
    }

    /// <summary>64:ff9b::/96 несёт в хвосте IPv4-адрес, поэтому через него можно достучаться до внутренней сети.</summary>
    private static bool IsNat64(byte[] b) =>
        b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b[4..12].All(x => x == 0);
}
