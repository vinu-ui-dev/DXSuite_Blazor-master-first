using System.Reflection;
using DXSuite.Shared.Module;
using DXSuite.Shared.Multitenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.StyleGuide;

public class StyleGuideModule : IBlazorModule
{
    public string Name => "Style Guide";

    public string Version => "2026.05";

    public Assembly Assembly => typeof(StyleGuideModule).Assembly;

    public void RegisterServices(IServiceCollection services, IConfiguration config) { }

    /// <summary>
    /// Style Guide is an internal developer tool gated by its own tenant module flag.
    /// </summary>
    public bool IsAvailableForTenant(ITenantContext tenant) => tenant.Modules.StyleGuide;

    public IEnumerable<NavItem> GetNavItems()
    {
        yield return new NavItem("Style Guide", "/style-guide", "bi-palette", 90);
    }

    public IEnumerable<ConfigTab> GetConfigTabs() => [];
}
