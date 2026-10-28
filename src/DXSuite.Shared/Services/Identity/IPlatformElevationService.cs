using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Shared.Services.Identity;

public interface IPlatformElevationService
{
    /// <summary>
    /// Creates a pending elevation request.
    /// requestedByPlatformUserId and platformUserId may differ if an operator requests on behalf of another.
    /// </summary>
    Task<TblPlatformElevation> RequestElevationAsync(
        Guid platformUserId,
        string tenantId,
        string roleName,
        string reason,
        int durationMinutes,
        Guid requestedByPlatformUserId,
        string? ticketRef = null);

    /// <summary>
    /// Approves a pending elevation. Enforces separation of duties:
    /// approvedByPlatformUserId must differ from RequestedByPlatformUserId.
    /// </summary>
    Task<TblPlatformElevation> ApproveElevationAsync(
        Guid elevationId,
        Guid approvedByPlatformUserId);

    /// <summary>
    /// Revokes an active elevation before it expires.
    /// </summary>
    Task RevokeElevationAsync(Guid elevationId, Guid revokedByPlatformUserId);

    /// <summary>
    /// Returns the live (Active + not expired) elevation for the given platform user and tenant, or null.
    /// </summary>
    Task<TblPlatformElevation?> GetLiveElevationAsync(Guid platformUserId, string tenantId);

    /// <summary>Returns all elevations in Pending status (for the approval queue).</summary>
    Task<List<TblPlatformElevation>> GetPendingElevationsAsync();

    /// <summary>Returns all elevation records (any status) targeting a specific tenant.</summary>
    Task<List<TblPlatformElevation>> GetElevationsForTenantAsync(string tenantId, int take = 50);

    /// <summary>
    /// Returns the effective tenant settings for a tenant, or defaults when no row exists.
    /// When <paramref name="callerPlatformUserId"/> is provided, enforces IsTenantAdmin membership
    /// at the service layer (tenant-admin contexts). Omit for internal operator calls.
    /// </summary>
    Task<TblTenantSettings> GetTenantSettingsAsync(string tenantId, Guid? callerPlatformUserId = null);

    /// <summary>
    /// Returns pending elevations for the given tenant. Enforces that callerPlatformUserId
    /// holds an active IsTenantAdmin membership for tenantId — this is the authoritative
    /// security boundary, not any page-level check.
    /// </summary>
    Task<List<TblPlatformElevation>> GetPendingElevationsForTenantAsync(
        string tenantId, Guid callerPlatformUserId);

    /// <summary>
    /// Records tenant-admin approval (sets TenantApprovedAt). Verifies that
    /// tenantAdminPlatformUserId holds IsTenantAdmin for elevation.TenantId.
    /// Does not activate the elevation — operator approval via ApproveElevationAsync
    /// is still required after this call.
    /// </summary>
    Task<TblPlatformElevation> ApproveTenantElevationAsync(
        Guid elevationId, Guid tenantAdminPlatformUserId);

    /// <summary>
    /// Moves the elevation to Revoked status on behalf of a tenant admin. The reason is
    /// written to the audit log only, not stored on the elevation row.
    /// Verifies that tenantAdminPlatformUserId holds IsTenantAdmin for elevation.TenantId.
    /// </summary>
    Task RejectTenantElevationAsync(
        Guid elevationId, Guid tenantAdminPlatformUserId, string reason);

    /// <summary>
    /// Upserts TblTenantSettings for the given tenant. Creates the row if it does not exist.
    /// </summary>
    Task UpdateTenantSettingsAsync(TblTenantSettings settings, Guid actingPlatformUserId);
}
