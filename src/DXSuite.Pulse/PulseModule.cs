using System.Reflection;
using DXSuite.Pulse.Services;
using DXSuite.Shared.Module;
using DXSuite.Shared.Multitenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Pulse;

public class PulseModule : IBlazorModule
{
    public string Name => "Pulse";

    public string Version => "2026.05";

    public Assembly Assembly => typeof(PulseModule).Assembly;

    public bool IsAvailableForTenant(ITenantContext tenant) => tenant.Modules.Pulse;

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddPulseModule(config);
    }

    public IEnumerable<NavItem> GetNavItems()
    {
        yield return new NavItem("Pulse", "/pulse", "bi-heart-pulse", 20);
    }

    public IEnumerable<ConfigTab> GetConfigTabs()
    {
        // Cloud Connectors and Probe Profiles config tabs are now owned by the Sentinel module
        yield break;
    }
}
