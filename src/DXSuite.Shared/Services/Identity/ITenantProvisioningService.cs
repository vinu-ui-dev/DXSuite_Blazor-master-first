using DXSuite.Shared.Entities.Registry;

namespace DXSuite.Shared.Services.Identity;

public interface ITenantProvisioningService
{
    /// <summary>
    /// False when the tenant registry DB is not configured (dev / single-tenant deployments).
    /// When false, mutating methods throw NotSupportedException.
    /// </summary>
    bool IsAvailable { get; }

    Task<IReadOnlyList<TblTenant>> GetAllTenantsAsync();

    Task<TblTenant?> GetTenantAsync(string tenantId);

    /// <summary>
    /// Provisions a new tenant row. Audits TenantProvisioned.
    /// <paramref name="reAuthToken"/> must be a valid, unconsumed token from ReAuthConfirmationService.
    /// </summary>
    Task<TblTenant> CreateTenantAsync(TblTenant tenant, Guid actingPlatformUserId, Guid reAuthToken);

    /// <summary>
    /// Updates an existing tenant row. Audits TenantSettingsUpdated.
    /// <paramref name="reAuthToken"/> must be a valid, unconsumed token from ReAuthConfirmationService.
    /// </summary>
    Task<TblTenant> UpdateTenantAsync(TblTenant tenant, Guid actingPlatformUserId, Guid reAuthToken);

    /// <summary>
    /// Marks a tenant disabled. Audits TenantDeprovisioned.
    /// <paramref name="reAuthToken"/> must be a valid, unconsumed token from ReAuthConfirmationService.
    /// </summary>
    Task DisableTenantAsync(string tenantId, Guid actingPlatformUserId, Guid reAuthToken);
}
