using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DXSuite.Director.Entities;
using DXSuite.Shared.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DXSuite.Director.Services.ContentPackage;

/// <summary>
/// Default implementation that looks up users by UserPrincipalName and caches the
/// result for the lifetime of one import operation. Expects callers to pass their
/// own <see cref="DirectorDbContext"/> so user lookups share the import's transaction.
/// </summary>
public class ContentPackageUserResolver : IContentPackageUserResolver
{
    public const string SystemUserUpn = "contentpackage@dxsuite.system";

    private readonly ILogger<ContentPackageUserResolver> _logger;

    // Per-instance caches. Resolver is registered scoped so each import gets a clean one.
    private readonly ConcurrentDictionary<string, int?> _userCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int?> _groupCache = new(StringComparer.OrdinalIgnoreCase);
    private int? _systemUserId;

    public ContentPackageUserResolver(ILogger<ContentPackageUserResolver> logger)
    {
        _logger = logger;
    }

    public async Task<int?> ResolveAsync(DirectorDbContext db, string? sourceUpn, bool fallbackToSystemUser, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceUpn))
        {
            return fallbackToSystemUser ? await GetSystemUserIdAsync(db, ct) : (int?)null;
        }

        if (_userCache.TryGetValue(sourceUpn, out var cached))
        {
            if (cached.HasValue) return cached;
            return fallbackToSystemUser ? await GetSystemUserIdAsync(db, ct) : (int?)null;
        }

        var id = await db.Set<TblUser>().AsNoTracking()
            .Where(u => u.UserPrincipalName == sourceUpn)
            .Select(u => (int?)u.UserId)
            .FirstOrDefaultAsync(ct);

        _userCache[sourceUpn] = id;

        if (id.HasValue) return id;

        _logger.LogInformation("Content package user resolver: no match for {Upn}, falling back to system user", sourceUpn);
        return fallbackToSystemUser ? await GetSystemUserIdAsync(db, ct) : (int?)null;
    }

    public async Task<int?> ResolveGroupAsync(DirectorDbContext db, string? groupName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return null;

        if (_groupCache.TryGetValue(groupName, out var cached)) return cached;

        var id = await db.Set<TblUserGroup>().AsNoTracking()
            .Where(g => g.GroupName == groupName)
            .Select(g => (int?)g.UserGroupId)
            .FirstOrDefaultAsync(ct);

        _groupCache[groupName] = id;
        return id;
    }

    public async Task<int> GetSystemUserIdAsync(DirectorDbContext db, CancellationToken ct = default)
    {
        if (_systemUserId.HasValue) return _systemUserId.Value;

        var existing = await db.Set<TblUser>()
            .Where(u => u.UserPrincipalName == SystemUserUpn)
            .Select(u => (int?)u.UserId)
            .FirstOrDefaultAsync(ct);

        if (existing.HasValue)
        {
            _systemUserId = existing.Value;
            return existing.Value;
        }

        // Migration not applied yet — seed on demand so imports against an
        // un-migrated dev DB don't die on FK constraints. Uses the lowest RoleId
        // to keep the account non-privileged.
        var roleId = await db.Set<TblUserRole>()
            .OrderBy(r => r.RoleId)
            .Select(r => (int?)r.RoleId)
            .FirstOrDefaultAsync(ct);

        if (!roleId.HasValue)
        {
            throw new InvalidOperationException(
                "Cannot seed Content Package system user: tblUserRole is empty. Run the Phase 1 SQL migration first.");
        }

        var user = new TblUser
        {
            FriendlyName = "Content Package System",
            GivenName = "Content Package",
            Surname = "System",
            UserPrincipalName = SystemUserUpn,
            Title = "System Account",
            RoleId = roleId.Value,
            PulseAccess = false,
            IsDisabled = true,
            CreateDate = DateTime.UtcNow
        };
        db.Set<TblUser>().Add(user);
        await db.SaveChangesAsync(ct);

        _systemUserId = user.UserId;
        _logger.LogInformation("Seeded Content Package system user with UserId {UserId}", user.UserId);
        return user.UserId;
    }
}
