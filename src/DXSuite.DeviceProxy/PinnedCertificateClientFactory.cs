using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Forwarder;

namespace DXSuite.DeviceProxy;

/// <summary>
/// Validates a device's certificate by pinned SHA-256 fingerprint rather than by name.
///
/// Appliances commonly present a self-signed certificate with no subjectAltName, which no modern
/// client will accept by name. The usual workaround is to accept any certificate, which turns the
/// backend hop into an unauthenticated channel that anything on the network segment can impersonate.
/// Pinning avoids both: exactly one certificate is accepted, and it is the one an administrator
/// recorded.
/// </summary>
internal sealed class PinnedCertificateClientFactory : ForwarderHttpClientFactory
{
    private readonly DeviceProxyRegistry _registry;
    private readonly ILogger<PinnedCertificateClientFactory> _logger;

    public PinnedCertificateClientFactory(
        DeviceProxyRegistry registry,
        ILogger<PinnedCertificateClientFactory> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    protected override void ConfigureHandler(ForwarderHttpClientContext context, SocketsHttpHandler handler)
    {
        base.ConfigureHandler(context, handler);

        var device = _registry.ByClusterId(context.ClusterId);
        if (device is null) return;

        var expected = DeviceProxyRegistry.NormalizeThumbprint(device.CertificateThumbprint);
        var key = device.Key;

        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, sslErrors) =>
        {
            if (certificate is null)
            {
                _logger.LogError("Device '{DeviceKey}' presented no certificate; refusing to connect.", key);
                return false;
            }

            // The certificate is expected to fail the usual checks -- that is why it is pinned --
            // so sslErrors is recorded for diagnosis rather than used as the decision. A fingerprint
            // is public information, so an ordinary comparison is sufficient.
            var actual = Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));

            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                return true;

            // Worth being loud. The likeliest cause is that the device was replaced or reset, and
            // without the expected and actual values side by side the failure is indistinguishable
            // from the device simply being unreachable.
            _logger.LogError(
                "Device '{DeviceKey}' presented a certificate that does not match its pinned thumbprint, so the " +
                "connection was refused. Expected {Expected}, got {Actual} (TLS policy errors: {SslErrors}). " +
                "If the device was replaced or reset, update its CertificateThumbprint in DeviceProxy:Devices.",
                key, expected, actual, sslErrors);

            return false;
        };
    }
}
