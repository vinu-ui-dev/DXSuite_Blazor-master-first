using DXSuite.CoreData.Entities;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.CoreData.Services;

public static class Extensions
{
    public static IServiceCollection AddCoreDataModule(this IServiceCollection services, IConfiguration config)
    {
        // Scoped factory: options delegate runs once per DI scope so each request/circuit gets
        // its own tenant connection string. AddPooledDbContextFactory bakes DbContextOptions as
        // a singleton — the tenant routing in the delegate would be permanently bypassed.
        // Tenant connection string is authoritative when a tenant context is present; never
        // fall back to a different tenant's config DB when the tenant has no CoreData license.
        services.AddDbContextFactory<CoreDataDbContext>((sp, options) =>
        {
            var httpCtx = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
            var tenantCtx = httpCtx?.RequestServices.GetService<ITenantContext>();
            var connStr = tenantCtx != null
                ? tenantCtx.CoreDataConnectionString
                  ?? throw new InvalidOperationException(
                      $"Tenant '{tenantCtx.Subdomain}' does not license the CoreData module.")
                : config.GetConnectionString("CoreData")
                  ?? throw new InvalidOperationException(
                      "No CoreData connection string in app config (background service path).");
            options.UseConfiguredProvider(config, "CoreData", connStr);
        }, ServiceLifetime.Scoped);

        // Scoped because it queries per-tenant CoreData DB via the scoped factory above.
        services.AddScoped<ILanguageProvider, LanguageProvider>();

        return services;
    }
}
