using DXSuite.Design.Entities;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Multitenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Design.Services;

public static class Extensions
{
    public static IServiceCollection AddDesignModule(this IServiceCollection services, IConfiguration config)
    {
        // Scoped factory (not pooled): options delegate runs once per DI scope so each request
        // and Blazor circuit gets its own tenant connection string from ITenantContext.
        // AddPooledDbContextFactory would bake options as a process-lifetime singleton, breaking
        // per-tenant routing.
        services.AddDbContextFactory<DesignDbContext>((sp, options) =>
        {
            var tenantCtx = sp.GetService<ITenantContext>();

            if (tenantCtx?.IsPopulated != true)
            {
                var httpCtx = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
                var httpTenantCtx = httpCtx?.RequestServices.GetService<ITenantContext>();
                if (httpTenantCtx?.IsPopulated == true)
                    tenantCtx = httpTenantCtx;
                else
                    tenantCtx = null;
            }

            var connStr = tenantCtx != null
                ? tenantCtx.DesignConnectionString
                  ?? throw new InvalidOperationException(
                      $"Tenant '{tenantCtx.Subdomain}' does not have a Design module connection string configured.")
                : config.GetConnectionString("Design")
                  ?? throw new InvalidOperationException(
                      "No Design connection string in app config (background service path).");

            options.UseSqlServer(connStr);
        }, ServiceLifetime.Scoped);

        services.AddScoped<DesignDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<DesignDbContext>>().CreateDbContext());

        // IdentityDbContext connects to the Director database (same physical DB, identity tables only).
        // RequirementsPanel and RequirementModal use it to resolve user display names for the
        // Assign To dropdown without requiring a project reference to DXSuite.Director.
        services.AddDbContextFactory<IdentityDbContext>((sp, options) =>
        {
            var tenantCtx = sp.GetService<ITenantContext>();

            if (tenantCtx?.IsPopulated != true)
            {
                var httpCtx = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
                var httpTenantCtx = httpCtx?.RequestServices.GetService<ITenantContext>();
                if (httpTenantCtx?.IsPopulated == true)
                    tenantCtx = httpTenantCtx;
                else
                    tenantCtx = null;
            }

            var connStr = tenantCtx != null
                ? tenantCtx.DirectorConnectionString
                  ?? throw new InvalidOperationException(
                      $"Tenant '{tenantCtx.Subdomain}' does not have a Director connection string configured.")
                : config.GetConnectionString("Director")
                  ?? throw new InvalidOperationException(
                      "No Director connection string in app config.");

            options.UseSqlServer(connStr);
        }, ServiceLifetime.Scoped);

        services.AddSingleton<NetsuiteCredentialProtector>();
        services.AddScoped<INetsuiteConnectionProvider, NetsuiteConnectionProvider>();
        services.AddHttpClient<NetsuiteClient>();
        services.AddScoped<INetsuiteService>(sp => sp.GetRequiredService<NetsuiteClient>());
        services.AddScoped<NetsuiteEstimatePusher>();

        // Read-only MCP server (see docs/design-mcp-server.md). Stateless transport: every HTTP
        // request is independent, so tools are activated in the request's DI scope and see the
        // tenant-routed DbContext factory. The endpoint itself is mapped in DesignModule.MapEndpoints.
        services.AddMcpServer()
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<Mcp.DesignMcpTools>();

        // MCP endpoint auth: dxd_ personal access tokens, validated per tenant. The policy pins
        // the scheme so the interactive cookie session is never accepted on the MCP endpoint.
        services.AddScoped<Mcp.DesignMcpTokenService>();
        services.AddAuthentication()
            .AddScheme<Mcp.DesignMcpTokenAuthOptions, Mcp.DesignMcpTokenAuthHandler>(
                Mcp.DesignMcpTokenAuthHandler.SchemeName, _ => { });
        services.AddAuthorization(o => o.AddPolicy(Mcp.DesignMcpTokenAuthHandler.SchemeName, p => p
            .AddAuthenticationSchemes(Mcp.DesignMcpTokenAuthHandler.SchemeName)
            .RequireAuthenticatedUser()));

        return services;
    }
}
