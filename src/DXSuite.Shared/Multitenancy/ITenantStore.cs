namespace DXSuite.Shared.Multitenancy;

/// <summary>
/// Registry of configured tenants. Singleton — implementations may cache at startup.
/// </summary>
public interface ITenantStore
{
    /// <summary>Returns the tenant matching the subdomain, or null if not found.</summary>
    Task<TenantRecord?> FindBySubdomainAsync(string subdomain, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the configured default tenant (DXSuite:DefaultTenant key), falling
    /// back to the first enabled tenant. Used for localhost / no-subdomain requests.
    /// </summary>
    Task<TenantRecord?> GetDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all enabled tenants. Used by worker tier iteration.</summary>
    Task<IReadOnlyList<TenantRecord>> GetAllEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans enabled tenant Director databases for an API key matching the given SHA-256 hash.
    /// Used by TenantResolutionMiddleware for X-API-KEY requests without a subdomain.
    /// O(N tenants) scan — acceptable for small N with the config-file store (Option B).
    /// Phase 8 replaces this with a central mapping table for O(1) lookup.
    /// </summary>
    Task<TenantRecord?> FindTenantByApiKeyHashAsync(string keyHash, CancellationToken ct = default);
}
