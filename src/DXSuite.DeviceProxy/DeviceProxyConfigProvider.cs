using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace DXSuite.DeviceProxy;

/// <summary>
/// Builds YARP's routes and clusters from the device registry.
///
/// Paths are mirrored rather than prefixed. A device's own web interface generally assumes it owns
/// the root of its origin: the Q-SYS UCI viewer, for instance, builds its control-socket URL as
/// location.hostname + '/qrc', discarding the path entirely. Serving it under a prefix would send
/// that socket to the wrong place, and its relative asset references would need rewriting.
///
/// The consequence is that one hostname can serve exactly one device. A single-device deployment
/// leaves ProxyHost empty and the device answers on the application's own address. A second device
/// needs its own hostname, at which point every device should get one and the unqualified routes
/// must go, or requests to the shared paths become ambiguous.
/// </summary>
public sealed class DeviceProxyConfigProvider : IProxyConfigProvider
{
    // Paths a proxied device claims on its hostname.
    //
    // /designs is the Q-SYS viewer's design data, fetched from the serving hostname rather than
    // relative to the page. It is needed for the viewer to render anything at all: without it the
    // shell loads and shows an empty canvas, which looks like a working embed until someone tries
    // to use it.
    //
    // Deliberately excludes /manifest.json, which the shell references and which is only PWA
    // metadata: proxying it would let a device claim a conventional application path for nothing.
    //
    // These are root paths, so each one is a name a device takes over on its hostname. /designs
    // misses this application's own /design routes by a single character, which is the clearest
    // argument for giving devices their own hostname before the list grows.
    private static readonly string[] ProxiedPaths =
    [
        "/api-uci/{**catch-all}",
        "/designs/{**catch-all}",
        "/qrc"
    ];

    private readonly DeviceProxyConfig _config;

    public DeviceProxyConfigProvider(DeviceProxyRegistry registry)
    {
        var routes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();

        foreach (var device in registry.Devices)
        {
            var clusterId = DeviceProxyRegistry.ClusterIdFor(device);

            clusters.Add(new ClusterConfig
            {
                ClusterId = clusterId,
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    [device.Key] = new() { Address = device.BackendAddress }
                }
            });

            for (var i = 0; i < ProxiedPaths.Length; i++)
            {
                routes.Add(new RouteConfig
                {
                    RouteId = $"{clusterId}-{i}",
                    ClusterId = clusterId,
                    Match = new RouteMatch
                    {
                        Path = ProxiedPaths[i],
                        Hosts = string.IsNullOrWhiteSpace(device.ProxyHost) ? null : [device.ProxyHost]
                    },
                    Transforms = BuildTransforms(registry.FrameAncestorOrigin)
                });
            }
        }

        _config = new DeviceProxyConfig(routes, clusters);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> BuildTransforms(string frameAncestorOrigin)
    {
        var transforms = new List<IReadOnlyDictionary<string, string>>
        {
            // A device's HSTS policy applies to the device's origin. Inherited by this application's
            // origin it would pin the whole site to HTTPS and make any certificate error here
            // unbypassable, including on hostnames that have nothing to do with the device.
            new Dictionary<string, string> { ["ResponseHeaderRemove"] = "Strict-Transport-Security" },

            // Replaced below by frame-ancestors, which is defined over the whole ancestor chain.
            // X-Frame-Options is not, and browsers disagree about how it applies to nested frames --
            // which is exactly the shape here, since a location bundle is itself framed.
            new Dictionary<string, string> { ["ResponseHeaderRemove"] = "X-Frame-Options" }
        };

        if (!string.IsNullOrWhiteSpace(frameAncestorOrigin))
        {
            transforms.Add(new Dictionary<string, string>
            {
                ["ResponseHeader"] = "Content-Security-Policy",
                ["Set"] = $"frame-ancestors {frameAncestorOrigin}"
            });
        }

        return transforms;
    }

    public IProxyConfig GetConfig() => _config;

    private sealed class DeviceProxyConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
        : IProxyConfig
    {
        public IReadOnlyList<RouteConfig> Routes { get; } = routes;
        public IReadOnlyList<ClusterConfig> Clusters { get; } = clusters;

        // Configuration is read once at startup, so there is nothing to signal.
        public IChangeToken ChangeToken { get; } = new CancellationChangeToken(CancellationToken.None);
    }
}
