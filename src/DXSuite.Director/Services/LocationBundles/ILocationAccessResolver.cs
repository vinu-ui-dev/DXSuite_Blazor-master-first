using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Services.LocationBundles;

/// <summary>
/// Resolves the calling user's identity and per-location access for HTTP controllers.
/// ISessionState only exists inside a Blazor circuit, so cookie-authenticated API
/// requests (bundle serving, Location Content API) re-derive the same answers per
/// request from the principal. The resolution rules intentionally mirror
/// SessionState.LoadLocationAccessMapAsync so a user sees identical access in the
/// Blazor UI and through the API.
/// </summary>
public interface ILocationAccessResolver
{
    Task<LocationCaller?> ResolveAsync(ClaimsPrincipal principal, int locationId, CancellationToken ct = default);
}

public class LocationCaller
{
    public TblUser User { get; set; } = null!;
    public Roles.Role Role { get; set; }

    /// <summary>True when the user may open this location at all (any access level).</summary>
    public bool IsAssigned { get; set; }

    /// <summary>
    /// Resolved access for the location. Null when access comes from the Admin
    /// implicit-all fallback — callers treat null-with-IsAssigned as full access,
    /// matching how SessionState populates its map for that case.
    /// </summary>
    public ResolvedAccess? Access { get; set; }

    public bool IsAdmin => Role == Roles.Role.Admin;

    public string EffectiveAccessLevel => Access?.AccessLevel ?? (IsAssigned ? "full" : "none");

    public bool CanWrite => IsAssigned && EffectiveAccessLevel != "view";
}
