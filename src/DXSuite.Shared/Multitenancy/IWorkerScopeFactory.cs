using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Shared.Multitenancy;

/// <summary>
/// Creates DI scopes with ITenantContext pre-populated for background service iteration.
/// Background services inject this instead of IServiceScopeFactory for per-tenant work,
/// so the DbContext factory inside each scope resolves the correct tenant connection string.
/// </summary>
public interface IWorkerScopeFactory
{
    /// <summary>
    /// Tenant subdomains assigned to this process instance.
    /// Standalone mode: all enabled tenants. Worker mode: DXSuite:WorkerTenants from config.
    /// </summary>
    IReadOnlyList<string> WorkerTenants { get; }

    /// <summary>
    /// Creates a new DI scope with ITenantContext populated for the given tenant subdomain.
    /// The caller must dispose the returned scope when done.
    /// </summary>
    Task<IServiceScope> CreateTenantScopeAsync(string subdomain, CancellationToken ct = default);
}
