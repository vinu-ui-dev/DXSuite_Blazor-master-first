using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace DXSuite.DeviceProxy;

public static class DeviceProxyServiceCollectionExtensions
{
    /// <summary>
    /// Registers device proxying. Throws when a device entry is malformed, which fails startup
    /// rather than leaving a route that forwards somewhere unintended.
    ///
    /// Kept separate from the module so the wiring has one definition: a harness exercising the
    /// forwarding path composes the same services the application does, rather than a copy of them
    /// that can drift.
    /// </summary>
    public static IServiceCollection AddDeviceProxy(this IServiceCollection services, DeviceProxyOptions options)
    {
        services.AddSingleton(new DeviceProxyRegistry(options));
        services.AddSingleton<IProxyConfigProvider, DeviceProxyConfigProvider>();
        services.AddSingleton<IForwarderHttpClientFactory, PinnedCertificateClientFactory>();
        services.AddReverseProxy();

        return services;
    }
}
