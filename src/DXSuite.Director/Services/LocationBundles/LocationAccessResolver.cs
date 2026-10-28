using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DXSuite.Director.Entities;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Services.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DXSuite.Director.Services.LocationBundles;

public class LocationAccessResolver : ILocationAccessResolver
{
    private readonly IDirectorDBContext _db;
    private readonly ILocationAccessService _accessService;
    private readonly ILogger<LocationAccessResolver> _logger;

    public LocationAccessResolver(
        IDirectorDBContext db,
        ILocationAccessService accessService,
        ILogger<LocationAccessResolver> logger)
    {
        _db = db;
        _accessService = accessService;
        _logger = logger;
    }

    public async Task<LocationCaller?> ResolveAsync(ClaimsPrincipal principal, int locationId, CancellationToken ct = default)
    {
        var upn = principal?.Identity?.Name;
        if (string.IsNullOrEmpty(upn))
            return null;

        var user = await _db.TblUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserPrincipalName == upn, ct);

        if (user == null || user.IsDisabled)
            return null;

        var caller = new LocationCaller
        {
            User = user,
            Role = (Roles.Role)user.RoleId
        };

        var userGroupIds = await _db.TblUserGroupMemberships
            .AsNoTracking()
            .Where(m => m.UserId == user.UserId)
            .Select(m => m.UserGroupId)
            .ToListAsync(ct);

        var ruleLocationIds = await _accessService.GetAccessibleLocationIdsAsync(user.UserId, userGroupIds);

        // Admins with zero explicit rules get implicit full access to every location —
        // an admin WITH rules is limited to them. Mirrors SessionState exactly.
        if (ruleLocationIds.Count == 0 && caller.Role == Roles.Role.Admin)
        {
            var allLocationIds = await _accessService.GetAllLocationIdsAsync();
            caller.IsAssigned = allLocationIds.Contains(locationId);
            caller.Access = caller.IsAssigned ? new ResolvedAccess { AccessLevel = "full" } : null;
            return caller;
        }

        caller.IsAssigned = ruleLocationIds.Contains(locationId);
        if (caller.IsAssigned)
        {
            caller.Access = await _accessService.ResolveAccessAsync(user.UserId, locationId);
            if (caller.Access == null)
            {
                // Rules granted visibility but no level resolved — treat as full to match
                // SessionState's no-access-service fallback rather than locking the user out.
                _logger.LogWarning("Access level did not resolve for user {UserId} at location {LocationId}; defaulting to full",
                    user.UserId, locationId);
                caller.Access = new ResolvedAccess { AccessLevel = "full" };
            }
        }

        return caller;
    }
}
