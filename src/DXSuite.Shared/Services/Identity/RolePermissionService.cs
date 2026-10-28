using DXSuite.Shared.Authorization;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Module;
using DXSuite.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DXSuite.Shared.Services.Identity;

public class RolePermissionService : IRolePermissionService
{
    private const int AdminRoleId = (int)Roles.Role.Admin;
    private const int MaxBuiltInRoleId = (int)Roles.Role.NewUser;

    // DB access goes through a fresh scope — the circuit-lifetime scoped context can
    // have a closed SQL connection if the circuit idles past the pool timeout.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyList<IBlazorModule> _modules;
    private readonly RolePermissionCache _cache;
    private readonly ILogger<RolePermissionService> _logger;
    private readonly ITenantContext? _tenantContext;

    public RolePermissionService(
        IServiceScopeFactory scopeFactory,
        IReadOnlyList<IBlazorModule> modules,
        RolePermissionCache cache,
        ILogger<RolePermissionService> logger,
        ITenantContext? tenantContext = null)
    {
        _scopeFactory = scopeFactory;
        _modules = modules;
        _cache = cache;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    private string TenantKey => _tenantContext?.IsPopulated == true ? _tenantContext.TenantId : "";

    public async Task<List<TblUserRole>> GetAllRolesAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();
        return await db.TblUserRoles
            .Include(r => r.Permissions)
            .AsNoTracking()
            .OrderBy(r => r.RoleId)
            .ToListAsync();
    }

    public async Task<TblUserRole?> GetRoleAsync(int roleId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();
        return await db.TblUserRoles
            .Include(r => r.Permissions)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RoleId == roleId);
    }

    public async Task<Dictionary<int, int>> GetAssignedUserCountsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();
        return await db.TblUsers
            .AsNoTracking()
            .GroupBy(u => u.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count);
    }

    public async Task<int> GetAssignedUserCountAsync(int roleId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();
        return await db.TblUsers.CountAsync(u => u.RoleId == roleId);
    }

    public async Task<TblUserRole> CreateRoleAsync(string roleName, string? description, int baseRoleId, IEnumerable<string> permissionKeys)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            throw new ArgumentException("Role name is required.", nameof(roleName));
        if (baseRoleId is < AdminRoleId or > MaxBuiltInRoleId)
            throw new ArgumentException("Base role must be one of the built-in roles.", nameof(baseRoleId));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        if (await db.TblUserRoles.AnyAsync(r => r.RoleName == roleName))
            throw new InvalidOperationException($"A role named '{roleName}' already exists.");

        var role = new TblUserRole
        {
            RoleName = roleName.Trim(),
            RoleDescription = description,
            IsGlobal = true,
            IsSystem = false,
            BaseRoleId = baseRoleId
        };

        foreach (var key in SanitizeKeys(permissionKeys))
            role.Permissions.Add(new TblRolePermission { PermissionKey = key });

        db.TblUserRoles.Add(role);
        await db.SaveChangesAsync();
        _cache.Invalidate(TenantKey);
        return role;
    }

    public async Task UpdateRoleAsync(int roleId, string roleName, string? description, int? baseRoleId, IEnumerable<string> permissionKeys)
    {
        if (roleId == AdminRoleId)
            throw new InvalidOperationException("The Admin role cannot be modified.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var role = await db.TblUserRoles
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.RoleId == roleId)
            ?? throw new InvalidOperationException($"Role {roleId} not found.");

        if (role.IsSystem)
        {
            if (!string.Equals(role.RoleName, roleName, StringComparison.Ordinal))
                throw new InvalidOperationException("Built-in roles cannot be renamed.");
            if (baseRoleId != null)
                throw new InvalidOperationException("Built-in roles cannot have a base role.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(roleName))
                throw new ArgumentException("Role name is required.", nameof(roleName));
            if (baseRoleId is null or < AdminRoleId or > MaxBuiltInRoleId)
                throw new ArgumentException("Base role must be one of the built-in roles.", nameof(baseRoleId));
            if (await db.TblUserRoles.AnyAsync(r => r.RoleName == roleName && r.RoleId != roleId))
                throw new InvalidOperationException($"A role named '{roleName}' already exists.");

            role.RoleName = roleName.Trim();
            role.BaseRoleId = baseRoleId;
        }

        role.RoleDescription = description;

        var desired = SanitizeKeys(permissionKeys);
        foreach (var existing in role.Permissions.Where(p => !desired.Contains(p.PermissionKey)).ToList())
            db.TblRolePermissions.Remove(existing);
        foreach (var key in desired.Where(k => !role.Permissions.Any(p => p.PermissionKey == k)))
            role.Permissions.Add(new TblRolePermission { RoleId = roleId, PermissionKey = key });

        await db.SaveChangesAsync();
        _cache.Invalidate(TenantKey);
    }

    public async Task<RoleDeleteResult> DeleteRoleAsync(int roleId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var role = await db.TblUserRoles.FirstOrDefaultAsync(r => r.RoleId == roleId)
            ?? throw new InvalidOperationException($"Role {roleId} not found.");

        if (role.IsSystem)
            throw new InvalidOperationException("Built-in roles cannot be deleted.");

        var userCount = await db.TblUsers.CountAsync(u => u.RoleId == roleId);
        if (userCount > 0)
            return new RoleDeleteResult(false, userCount);

        db.TblUserRoles.Remove(role);
        await db.SaveChangesAsync();
        _cache.Invalidate(TenantKey);
        return new RoleDeleteResult(true, 0);
    }

    public async Task<IReadOnlySet<string>> GetPermissionsForRoleAsync(int roleId)
    {
        var tenant = TenantKey;
        if (_cache.TryGet(tenant, roleId, out var cached))
            return cached;

        await EnsureDefaultsSyncedAsync(tenant);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        // Admin holds every known permission; other roles hold their explicit grants.
        HashSet<string> permissions = roleId == AdminRoleId
            ? (await db.TblKnownPermissions.AsNoTracking().Select(k => k.PermissionKey).ToListAsync()).ToHashSet()
            : (await db.TblRolePermissions.AsNoTracking()
                .Where(p => p.RoleId == roleId)
                .Select(p => p.PermissionKey)
                .ToListAsync()).ToHashSet();

        _cache.Set(tenant, roleId, permissions);
        return permissions;
    }

    public async Task<bool> HasPermissionAsync(int roleId, string permissionKey)
    {
        if (roleId == AdminRoleId)
            return true;

        var permissions = await GetPermissionsForRoleAsync(roleId);
        return permissions.Contains(permissionKey);
    }

    public async Task<Roles.Role> GetEffectiveRoleAsync(int roleId)
    {
        if (roleId is >= AdminRoleId and <= MaxBuiltInRoleId)
            return (Roles.Role)roleId;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var baseRoleId = await db.TblUserRoles.AsNoTracking()
            .Where(r => r.RoleId == roleId)
            .Select(r => r.BaseRoleId)
            .FirstOrDefaultAsync();

        if (baseRoleId is >= AdminRoleId and <= MaxBuiltInRoleId)
            return (Roles.Role)baseRoleId.Value;

        _logger.LogWarning("Role {RoleId} has missing or invalid BaseRoleId {BaseRoleId}; treating as NewUser", roleId, baseRoleId);
        return Roles.Role.NewUser;
    }

    /// <summary>
    /// One-time (per tenant per app lifetime) discovery sync: any permission key not yet
    /// in tblKnownPermission gets its default grants inserted for the built-in roles and
    /// is recorded as known. Keys already known are never re-granted, so deliberate
    /// revocations survive restarts and module re-registration.
    /// </summary>
    private async Task EnsureDefaultsSyncedAsync(string tenant)
    {
        if (_cache.IsSynced(tenant))
            return;

        await _cache.SyncLock.WaitAsync();
        try
        {
            if (_cache.IsSynced(tenant))
                return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

            var known = (await db.TblKnownPermissions.AsNoTracking()
                .Select(k => k.PermissionKey)
                .ToListAsync()).ToHashSet();

            var discovered = 0;
            foreach (var (key, defaultRoles) in EnumerateDefinedPermissions())
            {
                if (known.Contains(key))
                    continue;

                db.TblKnownPermissions.Add(new TblKnownPermission { PermissionKey = key, DiscoveredDate = DateTime.UtcNow });
                foreach (var role in defaultRoles)
                    db.TblRolePermissions.Add(new TblRolePermission { RoleId = (int)role, PermissionKey = key });
                discovered++;
            }

            if (discovered > 0)
            {
                await db.SaveChangesAsync();
                _logger.LogInformation("Registered {Count} new permission keys with default grants", discovered);
            }

            _cache.MarkSynced(tenant);
        }
        finally
        {
            _cache.SyncLock.Release();
        }
    }

    /// <summary>
    /// All permission keys defined by the current codebase, with the built-in roles that
    /// receive them by default. Settings-tab defaults derive from SettingsTab.MinimumRole
    /// (the pre-permission-system gate); config tabs default to Architect, matching the
    /// old Admin/Architect page-level gate on Configuration.
    /// </summary>
    private IEnumerable<(string Key, IEnumerable<Roles.Role> DefaultRoles)> EnumerateDefinedPermissions()
    {
        var nonAdminRoles = new[] { Roles.Role.Architect, Roles.Role.Contributor, Roles.Role.NewUser };

        foreach (var tab in _modules.SelectMany(m => m.GetSettingsTabs()))
        {
            if (tab.Key == PermissionKeys.RolesSettingsTabKey)
                continue;
            yield return (PermissionKeys.ForSettingsTab(tab.Key), nonAdminRoles.Where(r => r <= tab.MinimumRole));
        }

        foreach (var tab in _modules.SelectMany(m => m.GetConfigTabs()))
            yield return (PermissionKeys.ForConfigTab(tab.Key), new[] { Roles.Role.Architect });

        foreach (var def in PermissionKeys.FeaturePermissions)
            yield return (def.Key, def.DefaultRoles);
    }

    private static HashSet<string> SanitizeKeys(IEnumerable<string> permissionKeys)
        => permissionKeys
            .Where(k => !string.IsNullOrWhiteSpace(k) && k != PermissionKeys.ForSettingsTab(PermissionKeys.RolesSettingsTabKey))
            .Select(k => k.Trim())
            .ToHashSet();
}
