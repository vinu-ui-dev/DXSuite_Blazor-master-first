using System.Reflection;
using DXSuite.Shared.Module;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Host.Components.Platform;

public class PlatformModule : IBlazorModule
{
    private readonly IConfiguration _config;

    public PlatformModule(IConfiguration config)
    {
        _config = config;
    }

    public string Name => "Platform";

    // Components live in DXSuite.Host — already the primary assembly for MapRazorComponents<App>().
    public Assembly Assembly => typeof(PlatformModule).Assembly;

    public void RegisterServices(IServiceCollection services, IConfiguration config) { }

    public IEnumerable<NavItem> GetNavItems()
    {
        var role = _config["DXSuite:Role"] ?? "standalone";
        if (role == "worker") yield break;

        // Platform nav is only meaningful when the Platform DB is configured.
        // Standalone/on-prem deployments omit ConnectionStrings:Platform entirely.
        if (string.IsNullOrEmpty(_config.GetConnectionString("Platform"))) yield break;

        // Visibility mirrors each destination page's own platform-identity guard so the
        // nav never shows a tab the user would be denied on.
        yield return new NavItem("Tenants",        "/platform/tenants",              "bi-building",           100, "Platform", IsVisible: s => s.IsPlatformOperator);
        yield return new NavItem("Users",          "/platform/users",                "bi-people",             110, "Platform", IsVisible: s => s.IsPlatformOperator);
        yield return new NavItem("Audit Log",      "/platform/audit",                "bi-journal-text",       120, "Platform", IsVisible: s => s.IsPlatformOperator || s.IsAuditor);
        yield return new NavItem("Elevation",      "/platform/elevation/request",    "bi-shield-lock",        130, "Platform", IsVisible: s => s.IsPlatformOperator);
        yield return new NavItem("My Tenant",      "/platform/tenant-admin",         "bi-gear",               140, "Platform", IsVisible: s => s.IsTenantAdmin);
    }

    public IEnumerable<ConfigTab> GetConfigTabs() => Enumerable.Empty<ConfigTab>();
}
