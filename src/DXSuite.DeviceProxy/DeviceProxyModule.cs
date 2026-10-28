using System.Reflection;
using DXSuite.Shared.Module;
using DXSuite.Shared.Multitenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.DeviceProxy;

/// <summary>
/// Re-serves configured device web interfaces through this application's origin.
///
/// The module exists separately from the feature that consumes it because a reverse proxy is a
/// security-sensitive surface: it forwards requests to internal addresses and is the one component
/// that could become a route into a customer's network if misconfigured. Keeping it small and
/// separately gated keeps that surface auditable, and lets a deployment that does not need it turn
/// the routes off entirely rather than merely hiding them.
/// </summary>
public class DeviceProxyModule : IBlazorModule
{
    public string Name => "Device Proxy";

    public Assembly Assembly => typeof(DeviceProxyModule).Assembly;

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection(DeviceProxyOptions.SectionName).Get<DeviceProxyOptions>()
                      ?? new DeviceProxyOptions();

        services.AddDeviceProxy(options);
    }

    /// <summary>
    /// The proxy has no navigation of its own; it is consumed by whatever embeds a device page.
    /// </summary>
    public IEnumerable<NavItem> GetNavItems() => [];

    public IEnumerable<ConfigTab> GetConfigTabs() => [];

    public bool IsAvailableForTenant(ITenantContext tenant) => true;

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IApplicationBuilder app)
    {
        // Authorization is the point of proxying rather than exposing the device directly: the
        // device itself has no notion of this application's users, so without this the proxy would
        // simply be a more convenient way to reach an unauthenticated control surface.
        endpoints.MapReverseProxy().RequireAuthorization();
    }
}
