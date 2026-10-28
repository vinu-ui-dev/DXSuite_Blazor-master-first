using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using DXSuite.Director.Models;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Director.Services;

/// <summary>
/// Blocks server-side web-preview fetches from reaching loopback, link-local (cloud metadata),
/// and private-range hosts (SSRF). Admin-entered URLs are still untrusted input in a shared
/// deployment: an admin of one tenant must not be able to point the server's network identity
/// at internal endpoints. Enforcement is at socket-connect time in <see cref="DialAsync"/>, so
/// every hop of a redirect chain is validated against the address actually dialed — closing the
/// DNS-rebinding window a validate-then-fetch sequence would leave open. Both outbound paths
/// funnel through it: our own HttpClient via <see cref="ConnectAsync"/>, and the headless capture
/// browser via <see cref="EgressFilteringProxy"/>.
/// WebPreview:AllowPrivateNetworks disables the guard for intranet-facing on-prem installs;
/// WebPreview:OutboundAllowList grants narrower per-host or per-range exemptions.
/// </summary>
public class OutboundUrlGuard
{
    private readonly bool _allowPrivateNetworks;
    private readonly string[] _allowedHosts;
    private readonly IPNetwork[] _allowedNetworks;
    private readonly ITenantStore _tenantStore;
    private readonly ILogger<OutboundUrlGuard> _logger;
    private bool? _captureAllowed;

    public OutboundUrlGuard(
        IOptions<WebPreviewOptions> options,
        ILogger<OutboundUrlGuard> logger,
        ITenantStore tenantStore)
    {
        _logger = logger;
        _tenantStore = tenantStore;
        _allowPrivateNetworks = options.Value.AllowPrivateNetworks;

        var hosts = new List<string>();
        var networks = new List<IPNetwork>();
        foreach (var entry in options.Value.OutboundAllowList)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            if (entry.Contains('/'))
            {
                if (IPNetwork.TryParse(entry, out var network)) networks.Add(network);
                else logger.LogWarning("Ignoring invalid WebPreview:OutboundAllowList CIDR entry {Entry}", entry);
            }
            else
            {
                hosts.Add(entry.Trim());
            }
        }
        _allowedHosts = hosts.ToArray();
        _allowedNetworks = networks.ToArray();
    }

    /// <summary>
    /// Whether headless-browser capture may run at all. Capture routes every browser connection
    /// through <see cref="EgressFilteringProxy"/>, so subresources and redirect hops are held to
    /// the same address policy as our own fetches and shared deployments are safe.
    ///
    /// The one configuration that stays refused is a shared deployment with
    /// WebPreview:AllowPrivateNetworks set: that setting disarms the filter, and a tenant admin
    /// could then have the browser render an internal endpoint into a thumbnail only they can
    /// see. It is documented as single-tenant-only, so honouring it here would mean trusting a
    /// misconfiguration with a full read-SSRF primitive.
    /// </summary>
    public async ValueTask<bool> IsBrowserCaptureAllowedAsync()
    {
        if (_captureAllowed is { } cached) return cached;

        var tenantCount = (await _tenantStore.GetAllEnabledAsync()).Count;
        var allowed = !_allowPrivateNetworks || tenantCount <= 1;

        if (!allowed)
            _logger.LogError(
                "WebPreview:CaptureEnabled is ignored: WebPreview:AllowPrivateNetworks disables the " +
                "outbound address filter, and {TenantCount} tenants are configured. Clear " +
                "AllowPrivateNetworks and grant narrower WebPreview:OutboundAllowList entries to " +
                "re-enable capture. Thumbnails fall back to page metadata (og:image/icon).", tenantCount);

        _captureAllowed = allowed;
        return allowed;
    }

    public bool IsHostAllowListed(string host)
    {
        foreach (var entry in _allowedHosts)
        {
            if (entry.StartsWith("*.", StringComparison.Ordinal))
            {
                if (host.EndsWith(entry[1..], StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (string.Equals(host, entry, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public bool IsAllowedAddress(IPAddress address)
    {
        if (_allowPrivateNetworks) return true;

        // Normalize so "::ffff:10.0.0.1" can't smuggle a private IPv4 past the checks
        var ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        foreach (var network in _allowedNetworks)
        {
            if (network.BaseAddress.AddressFamily == ip.AddressFamily && network.Contains(ip))
                return true;
        }

        return !IsBlockedAddress(ip);
    }

    private static bool IsBlockedAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;

        var b = ip.GetAddressBytes();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return b[0] == 0                                // "this network"
                || b[0] == 10                               // RFC 1918
                || (b[0] == 100 && (b[1] & 0xC0) == 64)     // CGNAT 100.64/10
                || (b[0] == 169 && b[1] == 254)             // link-local incl. cloud metadata
                || (b[0] == 172 && (b[1] & 0xF0) == 16)     // RFC 1918
                || (b[0] == 192 && b[1] == 168)             // RFC 1918
                || b[0] >= 224                              // multicast, reserved, broadcast
                // Azure Wire Server: a globally-routable-looking address that every Azure VM
                // routes to a host-internal agent (DNS, health, IMDS proxy)
                || (b[0] == 168 && b[1] == 63 && b[2] == 129 && b[3] == 16);
        }

        // Default-deny for IPv6: only global unicast (2000::/3) is permitted. This blocks
        // link-local, ULA, multicast, and the transition ranges whose embedded IPv4 address
        // would otherwise reach a private host without the classifier ever seeing it
        // (NAT64 64:ff9b::/96, IPv4-compatible ::a.b.c.d — both outside 2000::/3).
        if ((b[0] & 0xE0) != 0x20) return true;

        return (b[0] == 0x20 && b[1] == 0x02)                               // 6to4 2002::/16
            || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00); // Teredo 2001::/32
    }

    /// <summary>
    /// Resolves <paramref name="host"/> and connects to the first resolved address the policy
    /// permits. Because the socket is dialled at a specific validated address rather than by name,
    /// no second DNS answer can re-point the connection after the check.
    /// </summary>
    /// <exception cref="OutboundBlockedException">Every resolved address is disallowed.</exception>
    public async Task<Socket> DialAsync(string host, int port, CancellationToken ct)
    {
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (SocketException ex)
        {
            throw new OutboundBlockedException($"Could not resolve '{host}'.", ex);
        }

        var allowed = IsHostAllowListed(host)
            ? addresses
            : addresses.Where(IsAllowedAddress).ToArray();

        if (allowed.Length == 0)
        {
            _logger.LogWarning("Blocked outbound web-preview connection to {Host}: all resolved addresses are disallowed", host);
            throw new OutboundBlockedException($"Outbound connections to '{host}' are not permitted from this server.");
        }

        Exception? lastError = null;
        foreach (var address in allowed)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), ct);
                return socket;
            }
            catch (Exception ex)
            {
                lastError = ex;
                socket.Dispose();
            }
        }
        throw new IOException($"Could not connect to '{host}'.", lastError);
    }

    /// <summary>
    /// SocketsHttpHandler.ConnectCallback that resolves the target itself and refuses to dial
    /// any disallowed address. Wire this into every HttpClient that fetches admin-entered URLs.
    /// </summary>
    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        try
        {
            return new NetworkStream(
                await DialAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, ct), ownsSocket: true);
        }
        catch (Exception ex) when (ex is OutboundBlockedException or IOException)
        {
            throw new HttpRequestException(ex.Message, ex);
        }
    }

    /// <summary>
    /// Preflight for the headless-browser capture path. Returns whether the top-level URL may be
    /// captured, plus a Chromium --host-resolver-rules value pinning the hostname to the validated
    /// address.
    ///
    /// This validates only the top-level URL, so it is a fail-fast check, not the security
    /// boundary: it avoids launching a browser for a URL that is already known to be disallowed
    /// and produces a specific log line for it. The page's own subresource loads and redirect hops
    /// — which would otherwise be unvalidated read-SSRF, since whatever they return is rendered
    /// into a thumbnail the tenant admin can read — are enforced by
    /// <see cref="EgressFilteringProxy"/>, which the capture browser is required to route every
    /// connection through. The resolver pin is likewise belt-and-braces: a proxied Chromium
    /// forwards hostnames to the proxy instead of resolving them, and the proxy dials only
    /// addresses it validated itself.
    /// </summary>
    public async Task<(bool Allowed, string? HostResolverRule)> EvaluateForBrowserCaptureAsync(Uri uri)
    {
        var host = uri.IdnHost;
        if (_allowPrivateNetworks || IsHostAllowListed(host)) return (true, null);

        if (IPAddress.TryParse(host, out var literal))
            return (IsAllowedAddress(literal), null);

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host);
        }
        catch (SocketException)
        {
            // Unresolvable now — the capture would fail anyway
            return (false, null);
        }

        var pinned = addresses.FirstOrDefault(IsAllowedAddress);
        return pinned == null ? (false, null) : (true, $"MAP {host} {pinned}");
    }
}

/// <summary>
/// A connection was refused by policy rather than by the network. Callers distinguish this from a
/// transport failure so they can report it as a block (HTTP 403 from the egress proxy) instead of
/// retrying or reporting an upstream outage.
/// </summary>
public class OutboundBlockedException : Exception
{
    public OutboundBlockedException(string message, Exception? inner = null) : base(message, inner) { }
}
