using System.Reflection;
using DXSuite.CoreData.Services;
using DXSuite.Shared.Module;
using DXSuite.Shared.Multitenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.CoreData;

public class CoreDataModule : IBlazorModule
{
    public string Name => "Core Data";
    public string Version => "2026.05";
    public Assembly Assembly => typeof(CoreDataModule).Assembly;

    public bool IsAvailableForTenant(ITenantContext tenant) => tenant.Modules.CoreData;

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddCoreDataModule(config);
    }

    // Service-only module — no navigation or config tabs
    public IEnumerable<NavItem> GetNavItems() => [];
    public IEnumerable<ConfigTab> GetConfigTabs() => [];
}
