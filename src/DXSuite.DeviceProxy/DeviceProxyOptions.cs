namespace DXSuite.DeviceProxy;

public sealed class DeviceProxyOptions
{
    public const string SectionName = "DeviceProxy";

    /// <summary>
    /// Origin permitted to frame proxied device pages, e.g. https://dxsuite.example.com. Emitted as
    /// a Content-Security-Policy frame-ancestors directive.
    ///
    /// Set this to the application's own origin rather than 'self'. The two are equivalent while a
    /// device is served from the application's own hostname, but the explicit form stays correct if
    /// a device later moves to its own hostname, so the policy never needs revisiting.
    ///
    /// Empty disables the header, which allows framing from anywhere and should only be used while
    /// developing locally.
    /// </summary>
    public string FrameAncestorOrigin { get; set; } = "";

    public List<ProxiedDevice> Devices { get; set; } = new();
}

/// <summary>
/// Devices with usable configuration, resolved once at startup. Anything malformed is rejected
/// there rather than failing later as an opaque routing miss.
/// </summary>
public sealed class DeviceProxyRegistry
{
    private readonly Dictionary<string, ProxiedDevice> _byClusterId;

    public DeviceProxyRegistry(DeviceProxyOptions options)
    {
        Devices = Validate(options.Devices);
        FrameAncestorOrigin = options.FrameAncestorOrigin;
        _byClusterId = Devices.ToDictionary(ClusterIdFor, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ProxiedDevice> Devices { get; }

    public string FrameAncestorOrigin { get; }

    public static string ClusterIdFor(ProxiedDevice device) => $"device-{device.Key}";

    public ProxiedDevice? ByClusterId(string clusterId) =>
        _byClusterId.TryGetValue(clusterId, out var device) ? device : null;

    /// <summary>
    /// A reverse proxy forwards wherever its configuration points, so a malformed or half-written
    /// entry is the difference between a device panel and a route into the customer's network.
    /// Rejecting it at startup keeps the set of reachable addresses exactly the set an
    /// administrator wrote down.
    /// </summary>
    private static List<ProxiedDevice> Validate(List<ProxiedDevice> configured)
    {
        var accepted = new List<ProxiedDevice>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in configured.Where(d => d.Enabled))
        {
            if (string.IsNullOrWhiteSpace(device.Key))
                throw new InvalidOperationException("A proxied device is missing its Key.");

            if (!seenKeys.Add(device.Key))
                throw new InvalidOperationException($"Duplicate proxied device key '{device.Key}'.");

            if (!Uri.TryCreate(device.BackendAddress, UriKind.Absolute, out var backend) ||
                (backend.Scheme != Uri.UriSchemeHttp && backend.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    $"Proxied device '{device.Key}' has an invalid BackendAddress: '{device.BackendAddress}'.");
            }

            if (NormalizeThumbprint(device.CertificateThumbprint).Length != 64)
            {
                throw new InvalidOperationException(
                    $"Proxied device '{device.Key}' needs a SHA-256 CertificateThumbprint (64 hex characters).");
            }

            accepted.Add(device);
        }

        return accepted;
    }

    public static string NormalizeThumbprint(string? thumbprint) =>
        new((thumbprint ?? "").Where(char.IsAsciiHexDigit).Select(char.ToUpperInvariant).ToArray());
}
