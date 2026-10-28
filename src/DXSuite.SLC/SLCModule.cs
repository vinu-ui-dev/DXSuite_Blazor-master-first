using System.Reflection;
using DXSuite.SLC.Data;
using DXSuite.Shared.Module;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.SLC;

public class SLCModule : IBlazorModule
{
    public string Name => "SLC";

    public string Version => "2026.05";

    public Assembly Assembly => typeof(SLCModule).Assembly;

    public bool IsAvailableForTenant(ITenantContext tenant) => tenant.Modules.SLC;

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        // Scoped factory: options delegate runs once per DI scope so each request/circuit
        // gets its own tenant connection string. The Pulse connection string is resolved
        // from the tenant context when available, falling back to top-level config for
        // background service paths.
        services.AddDbContextFactory<SlcPulseDbContext>((sp, options) =>
        {
            var httpCtx = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
            var tenantCtx = httpCtx?.RequestServices.GetService<ITenantContext>();

            // ITenantContext has no PulseConnectionString property — the Pulse DB is a
            // shared infrastructure DB so we always use the top-level connection string.
            var connStr = config.GetConnectionString("Pulse")
                ?? throw new InvalidOperationException(
                    "No 'Pulse' connection string configured. Add ConnectionStrings:Pulse to appsettings.json.");

            options.UseConfiguredProvider(config, "SlcPulse", connStr);
        }, ServiceLifetime.Scoped);
    }

    public IEnumerable<NavItem> GetNavItems()
    {
        yield return new NavItem("Screenshots", "/slc/screenshots", "bi-camera", Order: 30, Group: "SLC");
    }

    public IEnumerable<ConfigTab> GetConfigTabs()
    {
        yield break;
    }
}
