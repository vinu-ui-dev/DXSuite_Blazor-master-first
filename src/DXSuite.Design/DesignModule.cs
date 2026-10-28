using System.Reflection;
using DXSuite.Design.Services;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Module;
using DXSuite.Shared.Multitenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Design;

public class DesignModule : IBlazorModule
{
    public string Name => "Design";

    public string Version => "2026.05";

    public Assembly Assembly => typeof(DesignModule).Assembly;

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddDesignModule(config);
    }

    /// <summary>
    /// Design requires both the tenant module flag and a Design connection string.
    /// The flag gates display; the connection string gates functionality.
    /// </summary>
    public bool IsAvailableForTenant(ITenantContext tenant) =>
        tenant.Modules.Design && !string.IsNullOrEmpty(tenant.DesignConnectionString);

    public IEnumerable<NavItem> GetNavItems()
    {
        yield return new NavItem("Designs", "/design", "bi-pencil-square", 50, "Design", Permission: PermissionKeys.PageDesign);
        yield return new NavItem("Catalog", "/design/catalog", "bi-grid", 51, "Design", Permission: PermissionKeys.PageDesign);
    }

    public IEnumerable<ConfigTab> GetConfigTabs()
    {
        yield return new ConfigTab("netsuite", "NetSuite", typeof(ConfigTabs.NetsuiteTab), 90);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IApplicationBuilder app)
    {
        // Read-only MCP endpoint (see docs/design-mcp-server.md). Not mapped at all unless
        // DesignMcp:Enabled — a disabled deployment 404s before auth can leak the endpoint's
        // existence. When mapped: PAT bearer auth (401), then tenant checks (404).
        var config = app.ApplicationServices.GetRequiredService<IConfiguration>();
        if (!config.GetValue<bool>("DesignMcp:Enabled"))
            return;

        var group = endpoints.MapGroup("/mcp");
        group.RequireAuthorization(Services.Mcp.DesignMcpTokenAuthHandler.SchemeName);
        group.AddEndpointFilter(async (ctx, next) =>
        {
            var tenant = ctx.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
            if (!tenant.IsPopulated || !IsAvailableForTenant(tenant))
                return Results.NotFound();

            // Defense in depth: the token was validated against this tenant's DB, but a shared
            // Design DB between tenants would otherwise let a token cross subdomains.
            var tokenCustomer = ctx.HttpContext.User.FindFirst("CustomerCode")?.Value;
            if (!string.Equals(tokenCustomer, tenant.CustomerCode, StringComparison.Ordinal))
                return Results.NotFound();

            return await next(ctx);
        });
        group.MapMcp("/design");
    }
}
