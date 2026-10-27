using System.Net;
using System.Net.Sockets;

namespace LupiraCalApi.Core.Application.Subscriptions;

/// <summary>Keeps feed fetches off the LAN: checked at connect time, so a redirect or a DNS answer can't steer a fetch
/// to a private, loopback, link-local or reserved address.</summary>
public static class PrivateAddressGuard
{
    public static bool IsAllowed(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(b[0] is 0 or 10 or 127 or >= 224
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && (b[1] & 0xF0) == 16)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 100 && (b[1] & 0xC0) == 64));
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6) return false;
        // NAT64 (64:ff9b::/96) carries an IPv4 address the gateway would reach.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b.AsSpan(4, 8).IndexOfAnyExcept((byte) 0) < 0)
            return IsAllowed(new IPAddress(b.AsSpan(12, 4)));
        return !(address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6Loopback)
            || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
            || (b[0] & 0xFE) == 0xFC);
    }

    /// <summary>A <see cref="SocketsHttpHandler.ConnectCallback"/>: connects to the first allowed address the host resolves to.</summary>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
        var allowed = addresses.Where(IsAllowed).ToArray();
        if (allowed.Length == 0) throw new FeedBlockedException(host);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
