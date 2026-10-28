namespace DXSuite.Shared.Multitenancy;

/// <summary>
/// Per-request tenant identity. Populated by TenantResolutionMiddleware before
/// auth and routing run. Inject as a scoped service.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// False in Blazor Server circuit scopes before TenantCircuitInitializer has run.
    /// Check before accessing any other member to avoid InvalidOperationException.
    /// </summary>
    bool IsPopulated { get; }

    string TenantId { get; }
    string Subdomain { get; }
    string CustomerCode { get; }
    string DisplayName { get; }

    string? DirectorConnectionString { get; }
    string? SentinelConnectionString { get; }
    string? CoreDataConnectionString { get; }
    string? AviationConnectionString { get; }
    string? DesignConnectionString { get; }

    TenantAuthConfig AuthConfig { get; }
    TenantModuleConfig Modules { get; }
    TenantFeatureFlags Features { get; }

    /// <summary>
    /// Absolute or relative path to this tenant's media root directory.
    /// </summary>
    string MediaRootPath { get; }

    /// <summary>
    /// Absolute base URL for this tenant, for URL generation outside an active request.
    /// Null = fall back to the global FilePaths:SiteUrl config.
    /// </summary>
    string? SiteUrl { get; }
}
