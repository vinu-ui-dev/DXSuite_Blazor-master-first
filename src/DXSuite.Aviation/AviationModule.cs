using System.Reflection;
using DXSuite.Aviation.Services;
using DXSuite.Shared.Module;
using DXSuite.Shared.Multitenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Aviation;

public class AviationModule : IBlazorModule
{
    public string Name => "Aviation";

    public string Version => "2026.05";

    public Assembly Assembly => typeof(AviationModule).Assembly;

    public bool IsAvailableForTenant(ITenantContext tenant) => tenant.Modules.Aviation;

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddAviationModule(config);
    }

    public IEnumerable<NavItem> GetNavItems()
    {
        // Aviation provides data services only — airline/city management is accessed via Configuration tabs
        yield break;
    }

    public IEnumerable<ConfigTab> GetConfigTabs()
    {
        yield return new ConfigTab("airlines", "Airlines", typeof(ConfigTabs.AirlinesTab), 50);
        yield return new ConfigTab("cities", "Cities", typeof(ConfigTabs.CitiesTab), 60);
    }
}
