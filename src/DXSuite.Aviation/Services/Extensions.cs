using DXSuite.Aviation.Entities;
using DXSuite.Aviation.Services.CounterAllocation;
using DXSuite.Aviation.Services.FlightDataEditor;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
            options.UseConfiguredProvider(config, "Aviation", connStr);
        }, ServiceLifetime.Scoped);

        // ──────────────────────────────────────────────────────────────────
        // Flight Data Editor — GTAA FSD 1.1
        // ──────────────────────────────────────────────────────────────────
        services.Configure<FlightDataEditorOptions>(config.GetSection(FlightDataEditorOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IFlightListClock, FlightListClock>();
        services.AddScoped<FlightEditAudit>();

        // ──────────────────────────────────────────────────────────────────
        // Counter Allocations — GTAA FSD 1.2 (simplified; no CoreAPI feed yet)
        // ──────────────────────────────────────────────────────────────────
        services.AddSingleton<CounterAllocationOutageState>();
        services.AddScoped<CounterAllocationService>();

        return services;
    }
}
