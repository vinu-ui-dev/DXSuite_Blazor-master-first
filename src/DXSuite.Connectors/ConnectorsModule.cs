using System.Reflection;
using DXSuite.Connectors.Services;
using DXSuite.Shared.Module;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Connectors;

/// <summary>
/// Infrastructure module providing LLM, ITSM, and cloud connector implementations.
/// Service-only — no nav items, config tabs, or pages.
/// </summary>
public class ConnectorsModule : IBlazorModule
{
    public string Name => "Connectors";

    public Assembly Assembly => typeof(ConnectorsModule).Assembly;

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddConnectorsModule(config);
    }

    public IEnumerable<NavItem> GetNavItems() => Enumerable.Empty<NavItem>();

    public IEnumerable<ConfigTab> GetConfigTabs() => Enumerable.Empty<ConfigTab>();
}
