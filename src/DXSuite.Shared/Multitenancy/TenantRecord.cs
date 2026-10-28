namespace DXSuite.Shared.Multitenancy;

/// <summary>
/// Raw data for a tenant as returned by ITenantStore. All connection strings are
/// nullable because not every tenant licenses every module.
/// </summary>
public class TenantRecord
{
    public string TenantId { get; init; } = null!;
    public string Subdomain { get; init; } = null!;
    public string CustomerCode { get; init; } = null!;
    public string DisplayName { get; init; } = null!;
    public bool IsEnabled { get; init; } = true;

    public string? DirectorConnectionString { get; init; }
    public string? SentinelConnectionString { get; init; }
    public string? CoreDataConnectionString { get; init; }
    public string? AviationConnectionString { get; init; }
    public string? DesignConnectionString { get; init; }

    public TenantAuthConfig AuthConfig { get; init; } = new();
    public TenantModuleConfig Modules { get; init; } = new();
    public TenantFeatureFlags Features { get; init; } = new();

    /// <summary>
    /// Override for the tenant's media root path. Null = derive from subdomain.
    /// </summary>
    public string? MediaRootPath { get; init; }

    /// <summary>
    /// Absolute base URL for this tenant, used when generating URLs outside an active
    /// request (background services). Null = fall back to the global FilePaths:SiteUrl config.
    /// </summary>
    public string? SiteUrl { get; init; }
}
