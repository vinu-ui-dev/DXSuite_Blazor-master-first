using DXSuite.Aviation.Entities;
using DXSuite.Shared.Multitenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Aviation.Services;

public static class Extensions
{
    public static IServiceCollection AddAviationModule(this IServiceCollection services, IConfiguration config)
    {
        // Scoped factory: options delegate runs once per DI scope so each request/circuit gets
        // its own tenant connection string. AddPooledDbContextFactory bakes DbContextOptions as
        // a singleton — the tenant routing in the delegate would be permanently bypassed.
        // Tenant connection string is authoritative when a tenant context is present; never
        // fall back to a different tenant's config DB when the tenant has no Aviation license.
        services.AddDbContextFactory<AviationDbContext>((sp, options) =>
        {
            var httpCtx = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
            var tenantCtx = httpCtx?.RequestServices.GetService<ITenantContext>();
            var connStr = tenantCtx != null
                ? tenantCtx.AviationConnectionString
                  ?? throw new InvalidOperationException(
                      $"Tenant '{tenantCtx.Subdomain}' does not license the Aviation module.")
                : config.GetConnectionString("Aviation")
                  ?? throw new InvalidOperationException(
                      "No Aviation connection string in app config (background service path).");
            options.UseSqlServer(connStr);
        }, ServiceLifetime.Scoped);

        return services;
    }
}
