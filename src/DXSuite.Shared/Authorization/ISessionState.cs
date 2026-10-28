using System.Security.Claims;

namespace DXSuite.Shared.Authorization;

/// <summary>
/// A tenant the current user can access, resolved from TblPlatformUserTenantMembership.
/// </summary>
public record TenantMembershipInfo(string TenantId, string Subdomain, string DisplayName);

/// <summary>
/// Cross-module user identity contract implemented by each module's auth layer.
/// </summary>
public interface ICurrentUser
{
    int UserId { get; }
    string UserPrincipalName { get; }
    string? FriendlyName { get; }
    string? GivenName { get; }
    string? Surname { get; }
    bool IsDisabled { get; }
    int RoleId { get; }
    string? ExtendedData { get; }
}

/// <summary>
/// Session-scoped state for the authenticated user.
/// </summary>
public interface ISessionState
{
    ICurrentUser? User { get; set; }
    string? UserName { get; }
    string? Role { get; }

    /// <summary>
    /// The effective built-in role used by legacy role checks. For users assigned a
    /// custom role this is the custom role's base role, not the role itself — use
    /// ActualRoleId/ActualRoleName for the assigned role and HasPermissionAsync for
    /// tab and feature access.
    /// </summary>
    Roles.Role RoleEnum { get; set; }

    /// <summary>
    /// The user's assigned RoleId from tblUser — may be a custom role (> 4).
    /// </summary>
    int ActualRoleId { get; }

    /// <summary>
    /// Display name of the assigned role (custom role name, or the built-in name).
    /// </summary>
    string? ActualRoleName { get; }

    /// <summary>
    /// The effective permission set for the user's assigned role (cache-backed).
    /// </summary>
    Task<IReadOnlySet<string>> GetPermissionsAsync();

    /// <summary>
    /// True when the user's role holds the permission. Admin always returns true.
    /// </summary>
    Task<bool> HasPermissionAsync(string permissionKey);
    List<int> AssignedLocationIds { get; set; }
    bool IsDisabled { get; }
    Dictionary<int, ResolvedAccess?> LocationAccessMap { get; set; }
    string? PreferredLanguage { get; set; }
    string? ProfilePhotoDataUri { get; set; }
    string UserInitials { get; set; }
    Task LoadUserAsync(string username, ClaimsPrincipal? claimsPrincipal = null);
    TaskCompletionSource<bool> SessionLoaded { get; }

    /// <summary>
    /// Raised after RefreshLocationAccessAsync recomputes AssignedLocationIds and
    /// LocationAccessMap, so location-scoped components can re-apply their filters.
    /// </summary>
    event Action? LocationAccessChanged;

    /// <summary>
    /// Re-resolves AssignedLocationIds and LocationAccessMap from current access rules
    /// and group membership, then raises LocationAccessChanged. No-op before the
    /// session has loaded a user.
    /// </summary>
    Task RefreshLocationAccessAsync();

    /// <summary>
    /// The platform-level identity GUID from TblPlatformUser.
    /// Null when the Platform DB is not configured or bootstrap has not run.
    /// </summary>
    Guid? PlatformUserId { get; }

    /// <summary>
    /// All active tenant memberships for this user. Empty when single-tenant or Platform DB absent.
    /// Drives the tenant switcher UI — shown only when count > 1 or IsPlatformOperator.
    /// </summary>
    IReadOnlyList<TenantMembershipInfo> TenantMemberships { get; }

    /// <summary>
    /// True when the user holds the platform operator role (TblPlatformUser.IsPlatformOperator).
    /// Platform operators always see the tenant switcher regardless of membership count.
    /// </summary>
    bool IsPlatformOperator { get; }

    /// <summary>
    /// True when the user holds IsTenantAdmin for the CURRENT tenant (TenantContext.TenantId).
    /// This is a per-tenant-scoped flag — a user may be admin for one tenant but not another.
    /// </summary>
    bool IsTenantAdmin { get; }

    /// <summary>
    /// True when the user has been granted the platform Auditor role.
    /// Auditors can read audit logs across all tenants but have no write access.
    /// </summary>
    bool IsAuditor { get; }
}

public static class Roles
{
    public enum Role
    {
        Admin = 1,
        Architect = 2,
        Contributor = 3,
        NewUser = 4
    }
}

public class ResolvedAccess
{
    /// <summary>
    /// "full", "partial", or "view"
    /// </summary>
    public string AccessLevel { get; set; } = null!;

    /// <summary>
    /// From "view" rules: "all" sees every asset, "none" sees what the user's metadata scopes her to. Null when
    /// no view rule applies. Which assets a scoped user sees is decided where scope is evaluated, not here.
    /// </summary>
    public string? ViewScope { get; set; }

    /// <summary>
    /// Active constraint ID from a schedule-specific content scope.
    /// Null when no constraint-scoped schedule is active.
    /// </summary>
    public int? ActiveConstraintId { get; set; }

    /// <summary>
    /// Display-level scope from an active schedule.
    /// Null = whole location; non-null = specific display.
    /// </summary>
    public int? ActiveDisplayId { get; set; }

    /// <summary>
    /// Metadata key override from the active schedule, for content grouping in StatusSelector.
    /// </summary>
    public string? ActiveMetadataKey { get; set; }
}
