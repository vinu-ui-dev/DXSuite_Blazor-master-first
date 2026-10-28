using System.Reflection;
using DXSuite.Shared.Multitenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Shared.Module;

public interface IBlazorModule
{
    string Name { get; }
    Assembly Assembly { get; }
    void RegisterServices(IServiceCollection services, IConfiguration config);
    IEnumerable<NavItem> GetNavItems();
    IEnumerable<ConfigTab> GetConfigTabs();

    /// <summary>
    /// Returns false when this module should be hidden for the given tenant — for example,
    /// when the tenant has no connection string for the module's backing database.
    /// Nav items, config tabs, and settings tabs are all suppressed for unavailable modules.
    /// Default: always available (modules without per-tenant licensing constraints).
    /// </summary>
    bool IsAvailableForTenant(ITenantContext tenant) => true;

    /// <summary>
    /// Registers controllers, middleware, and endpoints in the HTTP pipeline.
    /// Default implementation is a no-op for modules without endpoints.
    /// </summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints, IApplicationBuilder app) { }

    /// <summary>
    /// Returns settings tabs for this module, rendered on the per-module Settings page.
    /// Default implementation returns no tabs.
    /// </summary>
    IEnumerable<SettingsTab> GetSettingsTabs() => Enumerable.Empty<SettingsTab>();

    /// <summary>
    /// Extra destinations this module offers as a user's landing page, beyond its nav items —
    /// Director contributes its locations, for example. Queried on demand rather than exposed as
    /// a property because the list is usually a database read.
    /// Default implementation returns none.
    /// </summary>
    /// <param name="services">A scoped provider the module resolves its own dependencies from,
    /// mirroring how MapEndpoints receives the pipeline rather than capturing one.</param>
    Task<IEnumerable<LandingPageOption>> GetLandingPageOptionsAsync(IServiceProvider services) =>
        Task.FromResult(Enumerable.Empty<LandingPageOption>());

    /// <summary>
    /// CalVer version string (e.g. "2026.05") displayed on the Home dashboard.
    /// Defaults to empty string for modules that do not publish a version.
    /// </summary>
    string Version => string.Empty;

    /// <summary>
    /// Returns the Razor component type to render as a stats card on the Home dashboard,
    /// or null if this module has no home card. The component receives no parameters and
    /// resolves its own data via IServiceScopeFactory injection.
    /// </summary>
    Type? GetHomeDashboardComponent() => null;
}
