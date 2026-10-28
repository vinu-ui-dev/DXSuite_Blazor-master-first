using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DXSuite.Shared.Networking;

/// <summary>
/// The internal-networks allow list (HOST-02/HOST-03): when <c>Network:InternalNetworks</c> (a list of CIDR
/// ranges) is set, local password sign-in and every API-key call are refused from outside it; when unset, every
/// network is allowed, unchanged from before this setting existed.
/// </summary>
public sealed class InternalNetworks
{
    public const string ApiKeyRefusedMessage = "API key calls are not accepted from this network.";

    private readonly IReadOnlyList<CidrRange> _ranges;

    public InternalNetworks(IConfiguration config)
    {
        _ranges = (config.GetSection("Network:InternalNetworks").Get<string[]>() ?? [])
            .Select(CidrRange.TryParse)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();
    }

    /// <summary>True when no internal-networks list is configured (unrestricted), or <paramref name="remoteIp"/> falls
    /// inside one of its ranges.</summary>
    public bool Allows(IPAddress? remoteIp)
    {
        if (_ranges.Count == 0) return true;
        if (remoteIp is null) return false;
        var candidate = remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp;
        return _ranges.Any(r => r.Contains(candidate));
    }

    public static void LogApiKeyCallRefused(ILogger logger, string clientIp, PathString path) =>
        logger.LogWarning("API key call to {Path} refused from {ClientIp}: outside the configured internal networks.", path, clientIp);

    private sealed class CidrRange(IPAddress network, int prefixLength)
    {
        public static CidrRange? TryParse(string cidr)
        {
            var parts = cidr.Split('/');
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0].Trim(), out var addr) || !int.TryParse(parts[1].Trim(), out var prefix))
                return null;
            return new CidrRange(addr, prefix);
        }

        public bool Contains(IPAddress address)
        {
            if (address.AddressFamily != network.AddressFamily) return false;
            var addrBytes = address.GetAddressBytes();
            var netBytes = network.GetAddressBytes();
            var fullBytes = prefixLength / 8;
            for (var i = 0; i < fullBytes && i < addrBytes.Length; i++)
                if (addrBytes[i] != netBytes[i]) return false;

            var remainingBits = prefixLength % 8;
            if (remainingBits == 0 || fullBytes >= addrBytes.Length) return true;
            var mask = (byte)~(0xFF >> remainingBits);
            return (addrBytes[fullBytes] & mask) == (netBytes[fullBytes] & mask);
        }
    }
}
