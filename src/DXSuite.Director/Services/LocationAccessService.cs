using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Services.Identity;

namespace DXSuite.Director.Services;

public class LocationAccessService : IDirectorLocationAccessService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LocationAccessService> _logger;
    private readonly ScheduleEvaluator _scheduleEvaluator;
    private readonly AccessCacheRefreshService? _cacheRefreshService;

    // Admin role ID (matches Roles.Role.Admin)
    private const int AdminRoleId = 1;

    public LocationAccessService(
        IServiceScopeFactory scopeFactory,
        ILogger<LocationAccessService> logger,
        ScheduleEvaluator scheduleEvaluator,
        AccessCacheRefreshService? cacheRefreshService = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _scheduleEvaluator = scheduleEvaluator;
        _cacheRefreshService = cacheRefreshService;
    }

    public async Task<List<TblLocationAccessRule>> GetRulesForLocationAsync(int locationId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await db.TblLocationAccessRules
            .Include(r => r.User)
            .Include(r => r.UserGroup)
            .Where(r => r.LocationId == locationId)
            .OrderBy(r => r.CreateDate)
            .ToListAsync();
    }

    public async Task<TblLocationAccessRule> CreateRuleAsync(int locationId, int? userId, int? userGroupId,
        string accessLevel, string? viewScope, string editUser)
    {
        if (userId == null && userGroupId == null)
            throw new ArgumentException("Either userId or userGroupId must be provided.");
        if (userId != null && userGroupId != null)
            throw new ArgumentException("Only one of userId or userGroupId can be provided.");
        ValidateLevel(accessLevel, viewScope);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var rule = new TblLocationAccessRule
        {
            LocationId = locationId,
            UserId = userId,
            UserGroupId = userGroupId,
            AccessLevel = accessLevel,
            ViewScope = accessLevel == "view" ? viewScope : null,
            CreateDate = DateTime.UtcNow,
            LastEditUser = editUser
        };

        db.TblLocationAccessRules.Add(rule);
        await db.SaveChangesAsync();

        _logger.LogInformation("Created access rule {RuleId} for location {LocationId}: {Target} = {Level}",
            rule.LocationAccessRuleId, locationId,
            userId.HasValue ? $"User:{userId}" : $"Group:{userGroupId}",
            accessLevel);

        return rule;
    }

    public async Task UpdateRuleAsync(int ruleId, string accessLevel, string? viewScope, string editUser)
    {
        ValidateLevel(accessLevel, viewScope);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var rule = await db.TblLocationAccessRules.FindAsync(ruleId);
        if (rule == null) throw new InvalidOperationException($"Access rule {ruleId} not found.");

        rule.AccessLevel = accessLevel;
        rule.ViewScope = accessLevel == "view" ? viewScope : null;
        rule.LastEditDate = DateTime.UtcNow;
        rule.LastEditUser = editUser;

        await db.SaveChangesAsync();
    }

    private static void ValidateLevel(string accessLevel, string? viewScope)
    {
        if (accessLevel is not ("full" or "partial" or "view"))
            throw new ArgumentException($"Unknown access level '{accessLevel}'.");
        if (accessLevel == "view" && !ContentScope.IsValidViewScope(viewScope))
            throw new ArgumentException($"View access requires scope '{ContentScope.ViewScopeAll}' or '{ContentScope.ViewScopeNone}'.");
    }

    public async Task DeleteRuleAsync(int ruleId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var rule = await db.TblLocationAccessRules.FindAsync(ruleId);
        if (rule != null)
        {
            db.TblLocationAccessRules.Remove(rule);
            await db.SaveChangesAsync();
            _logger.LogInformation("Deleted access rule {RuleId}", ruleId);
        }
    }

    public async Task<ResolvedAccess?> ResolveAccessAsync(int userId, int locationId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        // Admin always has full access regardless of rules
        var user = await db.TblUsers.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
        if (user?.RoleId == AdminRoleId)
        {
            return new ResolvedAccess { AccessLevel = "full" };
        }

        // Get all rules for this location
        var rules = await db.TblLocationAccessRules
            .Where(r => r.LocationId == locationId)
            .ToListAsync();

        if (rules.Count == 0)
            return null; // No rules = legacy open behavior

        // Get user's group memberships
        var userGroupIds = await db.TblUserGroupMemberships
            .Where(m => m.UserId == userId)
            .Select(m => m.UserGroupId)
            .ToListAsync();

        // Find matching rules: direct user rules + group rules
        var matchingRules = rules
            .Where(r => r.UserId == userId || (r.UserGroupId.HasValue && userGroupIds.Contains(r.UserGroupId.Value)))
            .ToList();

        if (matchingRules.Count == 0)
            return null; // User has no rules for this location

        // Scope values are not resolved here: they are read live, with current group membership, wherever
        // scope is evaluated.
        var resolved = ContentScope.Resolve(matchingRules.Select(r => new AccessRuleGrant(r.AccessLevel, r.ViewScope)))!;
        return new ResolvedAccess
        {
            AccessLevel = resolved.AccessLevel,
            ViewScope = resolved.ViewScope
        };
    }

    public async Task<ResolvedAccess?> ResolveAccessAsync(int userId, int locationId, DateTime asOf)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        // Admin always has full access regardless of rules or schedules
        var user = await db.TblUsers.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
        if (user?.RoleId == AdminRoleId)
            return new ResolvedAccess { AccessLevel = "full" };

        var rules = await db.TblLocationAccessRules
            .Include(r => r.AccessSchedules)
            .Where(r => r.LocationId == locationId)
            .ToListAsync();

        if (rules.Count == 0)
            return null;

        var userGroupIds = await db.TblUserGroupMemberships
            .Where(m => m.UserId == userId)
            .Select(m => m.UserGroupId)
            .ToListAsync();

        var matchingRules = rules
            .Where(r => r.UserId == userId || (r.UserGroupId.HasValue && userGroupIds.Contains(r.UserGroupId.Value)))
            .ToList();

        if (matchingRules.Count == 0)
            return null;

        // Filter to rules that are currently active based on their schedules.
        // Rules with no schedules are always active (backward-compatible).
        var activeRules = matchingRules
            .Where(r => !r.AccessSchedules.Any() || r.AccessSchedules.Any(s => _scheduleEvaluator.IsActive(s, asOf)))
            .ToList();

        if (activeRules.Count == 0)
            return null;

        // Determine active constraint + display from the first active schedule found
        int? activeConstraintId = null;
        int? activeDisplayId = null;
        string? activeMetadataKey = null;
        foreach (var rule in activeRules)
        {
            var activeSchedule = rule.AccessSchedules.FirstOrDefault(s => _scheduleEvaluator.IsActive(s, asOf));
            if (activeSchedule != null)
            {
                activeConstraintId ??= activeSchedule.ConstraintId;
                activeDisplayId ??= activeSchedule.DisplayId;
                activeMetadataKey ??= activeSchedule.MetadataKey;
            }
        }

        var resolved = ContentScope.Resolve(activeRules.Select(r => new AccessRuleGrant(r.AccessLevel, r.ViewScope)))!;
        return new ResolvedAccess
        {
            AccessLevel = resolved.AccessLevel,
            ViewScope = resolved.ViewScope,
            ActiveConstraintId = activeConstraintId,
            ActiveDisplayId = activeDisplayId,
            ActiveMetadataKey = activeMetadataKey
        };
    }

    public async Task<bool> HasAccessRulesAsync(int locationId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await db.TblLocationAccessRules.AnyAsync(r => r.LocationId == locationId);
    }

    public async Task<List<int>> GetAccessibleLocationIdsAsync(int userId, List<int> userGroupIds)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var directIds = await db.TblLocationAccessRules
            .Where(r => r.UserId == userId)
            .Select(r => r.LocationId)
            .Distinct()
            .ToListAsync();

        if (userGroupIds.Count > 0)
        {
            var groupIds = await db.TblLocationAccessRules
                .Where(r => r.UserGroupId.HasValue && userGroupIds.Contains(r.UserGroupId.Value))
                .Select(r => r.LocationId)
                .Distinct()
                .ToListAsync();

            directIds = directIds.Union(groupIds).Distinct().ToList();
        }

        return directIds;
    }

    public async Task<List<int>> GetAllLocationIdsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await db.TblLocations
            .Select(l => l.LocationId)
            .ToListAsync();
    }

    public async Task<List<TblAccessSchedule>> GetSchedulesForRuleAsync(int ruleId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await db.TblAccessSchedules
            .Where(s => s.LocationAccessRuleId == ruleId)
            .OrderBy(s => s.StartDate).ThenBy(s => s.StartTime)
            .ToListAsync();
    }

    public async Task<Dictionary<int, int>> GetScheduleCountsAsync(IEnumerable<int> ruleIds)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var ids = ruleIds.ToList();
        if (ids.Count == 0) return new Dictionary<int, int>();

        return await db.TblAccessSchedules
            .Where(s => ids.Contains(s.LocationAccessRuleId))
            .GroupBy(s => s.LocationAccessRuleId)
            .Select(g => new { RuleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RuleId, x => x.Count);
    }

    public async Task<TblAccessSchedule> AddScheduleAsync(TblAccessSchedule schedule, string editUser)
    {
        schedule.CreateDate = DateTime.UtcNow;
        schedule.LastEditUser = editUser;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        db.TblAccessSchedules.Add(schedule);
        await db.SaveChangesAsync();

        _logger.LogInformation("Added access schedule {ScheduleId} to rule {RuleId}",
            schedule.AccessScheduleId, schedule.LocationAccessRuleId);

        _cacheRefreshService?.ForceRefresh();
        return schedule;
    }

    public async Task DeleteScheduleAsync(int scheduleId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var schedule = await db.TblAccessSchedules.FindAsync(scheduleId);
        if (schedule != null)
        {
            db.TblAccessSchedules.Remove(schedule);
            await db.SaveChangesAsync();
            _logger.LogInformation("Deleted access schedule {ScheduleId}", scheduleId);
            _cacheRefreshService?.ForceRefresh();
        }
    }
}
