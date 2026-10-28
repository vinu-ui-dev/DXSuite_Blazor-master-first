using DXSuite.Shared.Authorization;

namespace DXSuite.Shared.Services.Identity;

public interface IPlatformIdentityService
{
    /// <summary>
    /// Finds or creates the platform-level identity for <paramref name="email"/>,
    /// then finds or creates a tenant membership linking it to the current tenant.
    /// Called once per login — safe to call on every session load.
    /// </summary>
    Task<PlatformBootstrapResult?> BootstrapAsync(string email, string tenantId, int tenantUserId);

    /// <summary>
    /// Returns all active tenant memberships for the platform user, enriched with
    /// display metadata from the tenant registry.
    /// </summary>
    Task<IReadOnlyList<TenantMembershipInfo>> GetTenantMembershipsAsync(Guid platformUserId);

    /// <summary>
    /// Returns all platform users, optionally scoped to users who hold a membership for
    /// the given tenantId. Each result includes enriched membership summaries.
    /// </summary>
    Task<IReadOnlyList<PlatformUserSummary>> GetPlatformUsersAsync(string? tenantId = null);

    /// <summary>
    /// Grants or revokes the IsTenantAdmin flag on a specific membership row.
    /// Audits TenantAdminGranted or TenantAdminRevoked.
    /// <paramref name="reAuthToken"/> must be a valid, unconsumed token from ReAuthConfirmationService.
    /// </summary>
    Task SetTenantAdminAsync(Guid membershipId, bool isTenantAdmin, Guid actingPlatformUserId, Guid reAuthToken);

    /// <summary>
    /// Activates or deactivates a tenant membership.
    /// Audits MembershipGranted or MembershipRevoked.
    /// <paramref name="reAuthToken"/> must be a valid, unconsumed token from ReAuthConfirmationService.
    /// </summary>
    Task SetMembershipActiveAsync(Guid membershipId, bool isActive, Guid actingPlatformUserId, Guid reAuthToken);

    /// <summary>
    /// Returns true when the given platform user holds an active IsTenantAdmin membership
    /// for the specified tenant. Used by SessionState to populate IsTenantAdmin.
    /// </summary>
    Task<bool> IsTenantAdminAsync(Guid platformUserId, string tenantId);

    /// <summary>
    /// Returns true when the platform user has been granted the given platform role.
    /// Used by SessionState to populate role flags such as IsAuditor.
    /// </summary>
    Task<bool> HasPlatformRoleAsync(Guid platformUserId, string roleName);
}

public record PlatformBootstrapResult(Guid PlatformUserId, int MembershipCount, bool IsPlatformOperator);

public record PlatformUserSummary(
    Guid PlatformUserId,
    string PrimaryEmail,
    string? DisplayName,
    bool IsPlatformOperator,
    IReadOnlyList<MembershipSummary> Memberships);

public record MembershipSummary(
    Guid MembershipId,
    string TenantId,
    string TenantDisplayName,
    bool IsActive,
    bool IsTenantAdmin,
    DateTime GrantedDate);
