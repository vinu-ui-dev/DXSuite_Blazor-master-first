namespace DXSuite.DeviceProxy;

/// <summary>
/// One device whose web interface is re-served through this application's origin.
///
/// Proxying rather than exposing the device directly means the browser only ever sees this
/// application's certificate, the device stays unreachable from client machines, and access can be
/// gated by the application's own authentication.
/// </summary>
public sealed class ProxiedDevice
{
    /// <summary>Stable identifier used in route and cluster names, and in log lines.</summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// Hostname this application serves the device on. Empty matches any host, which is what a
    /// single-device deployment wants: the device is reachable at the application's own address
    /// with no DNS work.
    ///
    /// A second device requires a hostname per device, because the paths cannot distinguish them
    /// (see <see cref="DeviceProxyConfigProvider"/>).
    /// </summary>
    public string ProxyHost { get; set; } = "";

    /// <summary>Where the device actually lives, e.g. https://192.168.124.5</summary>
    public string BackendAddress { get; set; } = "";

    /// <summary>
    /// SHA-256 fingerprint of the certificate the device presents. Colons and spaces are ignored.
    ///
    /// Appliances typically ship a self-signed certificate with no subjectAltName, which cannot be
    /// validated by name under any modern rule. Pinning the exact certificate is stronger than name
    /// validation and needs no change on the device.
    ///
    /// Replacing or factory-resetting the device changes this value, and the proxy will refuse to
    /// connect until it is updated. That is the intended behaviour, but it presents as "the panel
    /// stopped working after we swapped the hardware", so it belongs in the site's as-built notes.
    /// </summary>
    public string CertificateThumbprint { get; set; } = "";

    public bool Enabled { get; set; } = true;
}
