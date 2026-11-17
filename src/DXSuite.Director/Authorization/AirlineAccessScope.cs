using DXSuite.Director.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Authorization;

/// <summary>
/// Resolves which TblAirlineAccount.AirlineId values the current HTTP caller may access through the
/// CUPPS/counter-template endpoints. Only the "Airline Supervisor" role is restricted; every other
/// authenticated role (Admin, Architect, Director RMU, Airline Agent, etc.) is unrestricted, matching
/// today's behavior. An unauthenticated caller — including this app's own internal loopback calls from
/// CuppsLiveDataProvider, which don't carry the browser's auth cookie — is also treated as unrestricted,
/// so this does not change behavior for the server's own CUPPS page load. It only stops an authenticated
/// Airline Supervisor from reading another airline's data by calling the endpoint directly (e.g. via
/// browser devtools) with a different airlineId than their own.
/// </summary>
public interface IAirlineAccessScope
{
    /// <summary>Null means unrestricted. Non-null is the exact set of AirlineId values the caller may
    /// access (possibly empty, if the supervisor's group doesn't match any onboarded airline). Resolves
    /// the caller from the current HTTP request's authenticated identity (its auth cookie) — use this
    /// for ordinary browser-originated requests. It returns unrestricted (null) for a request with no
    /// authenticated identity at all, which includes this app's own internal loopback calls that don't
    /// carry a cookie — see <see cref="GetAllowedAirlineIdsForUserAsync"/> for those.</summary>
    Task<IReadOnlySet<int>?> GetAllowedAirlineIdsAsync(CancellationToken ct = default);

    /// <summary>Same resolution as <see cref="GetAllowedAirlineIdsAsync"/>, but for an explicitly named
    /// user rather than the current HTTP request's identity — for internal loopback calls that prove the
    /// caller via a signed <see cref="DXSuite.Shared.Services.Identity.IInternalCallerTokenService"/>
    /// token instead of a cookie. The caller is responsible for having already validated that token;
    /// this method does no further identity verification of its own.</summary>
    Task<IReadOnlySet<int>?> GetAllowedAirlineIdsForUserAsync(string username, CancellationToken ct = default);
}

internal sealed class AirlineAccessScope(
    IHttpContextAccessor httpContextAccessor,
    IDirectorDBContext db) : IAirlineAccessScope
{
    private const string AirlineSupervisorRoleName = "Airline Supervisor";

    public Task<IReadOnlySet<int>?> GetAllowedAirlineIdsAsync(CancellationToken ct = default)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
            return Task.FromResult<IReadOnlySet<int>?>(null);

        var username = httpContext.User.Identity.Name;
        return string.IsNullOrEmpty(username)
            ? Task.FromResult<IReadOnlySet<int>?>(null)
            : GetAllowedAirlineIdsForUserAsync(username, ct);
    }

    public async Task<IReadOnlySet<int>?> GetAllowedAirlineIdsForUserAsync(string username, CancellationToken ct = default)
    {
        var user = await db.TblUsers.AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.UserPrincipalName == username, ct);

        if (user is null || !string.Equals(user.Role?.RoleName, AirlineSupervisorRoleName, StringComparison.OrdinalIgnoreCase))
            return null;

        var groupIds = await db.TblUserGroupMemberships.AsNoTracking()
            .Where(m => m.UserId == user.UserId)
            .Select(m => m.UserGroupId)
            .ToListAsync(ct);

        if (groupIds.Count == 0)
            return new HashSet<int>();

        var groupNames = await db.TblUserGroups.AsNoTracking()
            .Where(g => groupIds.Contains(g.UserGroupId))
            .Select(g => g.GroupName)
            .ToListAsync(ct);

        // Prefer the real UserGroupId FK when it's populated; fall back to the same
        // group-name-equals-airline-name match AirlinesTab.razor already uses, so a supervisor's
        // API access always lines up with what the Configuration > Airlines UI already shows them.
        var allowed = await db.TblAirlineAccounts.AsNoTracking()
            .Where(a => a.RetiredUtc == null)
            .Where(a => (a.UserGroupId != null && groupIds.Contains(a.UserGroupId.Value))
                     || (a.NameEnglish != null && groupNames.Contains(a.NameEnglish))
                     || (a.CuppsName != null && groupNames.Contains(a.CuppsName)))
            .Select(a => a.AirlineId)
            .ToListAsync(ct);

        return allowed.ToHashSet();
    }
}
