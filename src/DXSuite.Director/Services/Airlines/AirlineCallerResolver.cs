using DXSuite.Director.Entities;
using DXSuite.Shared.Services.Identity;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services.Airlines;

/// <summary>Who is calling an airline write: the user the signed loopback token names, and the role they act in.</summary>
public sealed record AirlineCaller(string Username, string? RoleName)
{
    public bool IsAdmin => string.Equals(RoleName, "Admin", StringComparison.OrdinalIgnoreCase);
    public bool IsArchitect => string.Equals(RoleName, "Architect", StringComparison.OrdinalIgnoreCase);

    /// <summary>Admin and Architect set airlines up. Only an Admin approves, declines, retires or reactivates one.</summary>
    public bool CanManage => IsAdmin || IsArchitect;
}

/// <summary>
/// Reads the X-Internal-Caller-Token of a loopback call. The role on file counts, and so does the signed token's: an
/// Editor bridge user has no TblUser row, and a custom role's own name says nothing about what it may do.
/// </summary>
public sealed class AirlineCallerResolver(IInternalCallerTokenService tokens, IDirectorDBContext db)
{
    public async Task<AirlineCaller?> ResolveAsync(HttpRequest request, CancellationToken ct = default)
    {
        var username = tokens.ValidateToken(request.Headers["X-Internal-Caller-Token"], out var tokenRole);
        if (username is null) return null;

        var onFile = await db.TblUsers.AsNoTracking()
            .Where(u => u.UserPrincipalName == username)
            .Select(u => u.Role.RoleName)
            .FirstOrDefaultAsync(ct);

        static bool Manages(string? role) =>
            string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "Architect", StringComparison.OrdinalIgnoreCase);

        // The higher of the two: an Admin on either side is an Admin.
        string? role = onFile;
        if (string.Equals(tokenRole, "Admin", StringComparison.OrdinalIgnoreCase)) role = tokenRole;
        else if (!Manages(onFile) && Manages(tokenRole)) role = tokenRole;
        else role ??= tokenRole;
        return new AirlineCaller(username, role);
    }
}
