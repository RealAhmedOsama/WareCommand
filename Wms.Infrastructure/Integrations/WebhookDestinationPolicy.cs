using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Wms.Infrastructure.Integrations;

public interface IWebhookDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class SystemWebhookDnsResolver : IWebhookDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(
        string host,
        CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

public sealed class WebhookDestinationPolicy(
    IOptions<WebhookDeliveryTransportOptions> options,
    IHostEnvironment environment,
    IWebhookDnsResolver dnsResolver)
{
    private readonly WebhookDeliveryTransportOptions _options = options.Value;
    private readonly HashSet<string> _allowedHosts = options.Value.AllowedHosts
        .Select(host => WebhookHostName.TryNormalize(host, out var normalized) ? normalized : string.Empty)
        .Where(host => host.Length > 0)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _allowedSchemes = options.Value.AllowedSchemes
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _allowedPorts = options.Value.AllowedPorts.ToHashSet();

    public bool TryValidateEndpoint(string endpointUrl, out Uri? endpoint, out string errorCode)
    {
        endpoint = null;
        errorCode = "webhook_destination_not_allowed";
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var parsed) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Fragment))
        {
            errorCode = "webhook_endpoint_invalid";
            return false;
        }

        if (!WebhookHostName.TryNormalize(parsed.DnsSafeHost, out var normalizedHost) ||
            !_allowedHosts.Contains(normalizedHost) ||
            !_allowedSchemes.Contains(parsed.Scheme) ||
            !_allowedPorts.Contains(parsed.Port))
        {
            return false;
        }

        if (parsed.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !IsDevelopmentLoopbackAllowed(normalizedHost))
        {
            return false;
        }

        endpoint = parsed;
        errorCode = string.Empty;
        return true;
    }

    public async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host.Trim('[', ']');
        if (!WebhookHostName.TryNormalize(host, out var normalizedHost) ||
            !_allowedHosts.Contains(normalizedHost) ||
            !_allowedPorts.Contains(context.DnsEndPoint.Port))
        {
            throw new SocketException((int)SocketError.AccessDenied);
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literalAddress))
        {
            addresses = [literalAddress];
        }
        else
        {
            addresses = await dnsResolver.ResolveAsync(host, cancellationToken);
        }

        var allowLoopback = IsDevelopmentLoopbackAllowed(normalizedHost);
        if (addresses.Length == 0 || addresses.Any(address =>
                !IsAllowedAddress(address, allowLoopback)))
        {
            throw new SocketException((int)SocketError.AccessDenied);
        }

        SocketException? lastConnectionError = null;
        foreach (var address in addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port),
                    cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException exception)
            {
                lastConnectionError = exception;
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw lastConnectionError ?? new SocketException((int)SocketError.HostNotFound);
    }

    private bool IsDevelopmentLoopbackAllowed(string normalizedHost) =>
        _options.AllowLocalHttpForDevelopment &&
        environment.IsDevelopment() &&
        (normalizedHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
         IPAddress.TryParse(normalizedHost, out var address) && IPAddress.IsLoopback(address));

    private static bool IsAllowedAddress(IPAddress address, bool allowLoopback)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return IsAllowedAddress(address.MapToIPv4(), allowLoopback);
        }

        if (allowLoopback)
        {
            return IPAddress.IsLoopback(address);
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            var first = bytes[0];
            var second = bytes[1];
            var third = bytes[2];
            if (IPAddress.IsLoopback(address))
            {
                return false;
            }

            return first is not (0 or 10 or 127 or >= 224) &&
                   !(first == 100 && second is >= 64 and <= 127) &&
                   !(first == 169 && second == 254) &&
                   !(first == 168 && second == 63 && third == 129 && bytes[3] == 16) &&
                   !(first == 172 && second is >= 16 and <= 31) &&
                   !(first == 192 && second == 0 && third == 0) &&
                   !(first == 192 && second == 0 && third == 2) &&
                   !(first == 192 && second == 88 && third == 99) &&
                   !(first == 192 && second == 168) &&
                   !(first == 198 && second is 18 or 19) &&
                   !(first == 198 && second == 51 && third == 100) &&
                   !(first == 203 && second == 0 && third == 113) &&
                   first < 240;
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6 ||
            address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.IPv6Loopback) ||
            address.IsIPv6LinkLocal ||
            address.IsIPv6SiteLocal ||
            address.IsIPv6Multicast)
        {
            return false;
        }

        var ipv6 = address.GetAddressBytes();
        var isGlobalUnicast = (ipv6[0] & 0xE0) == 0x20;
        var isDocumentation = ipv6[0] == 0x20 && ipv6[1] == 0x01 &&
                              ipv6[2] == 0x0D && ipv6[3] == 0xB8 ||
                              ipv6[0] == 0x3F && ipv6[1] == 0xFF &&
                              (ipv6[2] & 0xF0) == 0x00;
        var isSpecialPurpose = ipv6[0] == 0x20 && ipv6[1] == 0x01 &&
                               (ipv6[2] <= 0x01 || ipv6[2] == 0x00 && ipv6[3] == 0x02);
        var isTransitionAddress = ipv6[0] == 0x20 && ipv6[1] == 0x02;
        var isNat64Address = ipv6[0] == 0x00 && ipv6[1] == 0x64 &&
                             ipv6[2] == 0xFF && ipv6[3] == 0x9B;
        return isGlobalUnicast && !isDocumentation && !isSpecialPurpose &&
               !isTransitionAddress && !isNat64Address;
    }
}
