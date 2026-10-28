using System.Reflection;
using DXSuite.Shared.Module;
using DXSuite.Shared.Multitenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.NetJets;

public class NetJetsModule : IBlazorModule
{
    public string Name => "NetJets";

    public string Version => "2026.05";

    public Assembly Assembly => typeof(NetJetsModule).Assembly;

    public bool IsAvailableForTenant(ITenantContext tenant) => tenant.Modules.NetJets;

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        // No module-level services to register. The Razor component and the controller
        // are auto-discovered by the host via AddAdditionalAssemblies + MapControllers.
        // Storage uses Director's IDirectorDBContext (referenced via project reference).
    }

    public IEnumerable<NavItem> GetNavItems()
    {
        yield break;
    }

    public IEnumerable<ConfigTab> GetConfigTabs()
    {
        yield break;
    }
}
