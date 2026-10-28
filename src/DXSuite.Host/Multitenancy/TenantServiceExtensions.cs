using Microsoft.AspNetCore.Components.Server.Circuits;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Host.Multitenancy;

internal static class TenantServiceExtensions
{
    internal static IServiceCollection AddTenantResolution(this IServiceCollection services,
        IConfiguration config)
    {
        services.Configure<TenantResolutionOptions>(config.GetSection("DXSuite:TenantResolution"));

        services.AddSingleton<ITenantStore, ConfigTenantStore>();

        // TenantContext implements both read (ITenantContext) and write (IPopulatableTenantContext) sides.
        // Worker-tier code resolves IPopulatableTenantContext to prime a new scope;
        // all other code resolves ITenantContext and never calls Populate.
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<IPopulatableTenantContext>(sp => sp.GetRequiredService<TenantContext>());

        // Carries the resolved subdomain from the HTTP (SSR) scope into the Blazor circuit scope.
        // TenantResolutionMiddleware writes it; TenantCircuitInitializer reads it.
        services.AddScoped<TenantSubdomainState>();

        // Populates TenantContext in Blazor Server circuit scopes, which are separate DI scopes
        // that never flow through TenantResolutionMiddleware.
        services.AddScoped<CircuitHandler, TenantCircuitInitializer>();

        // Singleton: reads DXSuite:WorkerTenants at startup; falls back to all enabled tenants.
        services.AddSingleton<IWorkerScopeFactory, WorkerScopeFactory>();

        // Per-tenant disk root for web-servable media; consumed wherever tenant content
        // paths were previously built from IWebHostEnvironment.WebRootPath.
        services.AddScoped<ITenantMediaPathResolver, TenantMediaPathResolver>();

        return services;
    }

    internal static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
        => app.UseMiddleware<TenantResolutionMiddleware>();

    internal static IApplicationBuilder UseTenantMedia(this IApplicationBuilder app)
        => app.UseMiddleware<TenantMediaMiddleware>();
}
