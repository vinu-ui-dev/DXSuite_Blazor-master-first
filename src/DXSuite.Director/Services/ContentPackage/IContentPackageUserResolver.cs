using System.Threading;
using System.Threading.Tasks;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Services.ContentPackage;

/// <summary>
/// Resolves UserPrincipalName values from source manifests to local int user IDs
/// in the target environment. When the source UPN has no match, falls back to
/// the seeded "contentpackage@dxsuite.system" account so the FK remains valid and
/// audit attribution still points at something meaningful.
/// </summary>
public interface IContentPackageUserResolver
{
    /// <summary>
    /// Returns the target int UserId for the given source UPN, or the ContentPackage
    /// system user ID if no match exists. Returns null if <paramref name="sourceUpn"/>
    /// is null/empty AND no fallback is desired (the caller's FK column is nullable).
    /// </summary>
    Task<int?> ResolveAsync(DirectorDbContext db, string? sourceUpn, bool fallbackToSystemUser, CancellationToken ct = default);

    /// <summary>
    /// Resolves a user group name to a TblUserGroup.UserGroupId in the target.
    /// Returns null when the group doesn't exist — callers typically skip the
    /// dependent record and log it, since a "fallback group" isn't meaningful.
    /// </summary>
    Task<int?> ResolveGroupAsync(DirectorDbContext db, string? groupName, CancellationToken ct = default);

    /// <summary>
    /// Returns the int UserId of the Content Package system user, seeding it if the
    /// migration wasn't applied yet. Thrown if the tblUserRole table is empty.
    /// </summary>
    Task<int> GetSystemUserIdAsync(DirectorDbContext db, CancellationToken ct = default);
}
