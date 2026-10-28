using DXSuite.Director.Entities;
using DXSuite.Shared.Services.Identity;

namespace DXSuite.Director.Services;

/// <summary>
/// Extended location access service with CRUD operations that return Director entities.
/// </summary>
public interface IDirectorLocationAccessService : ILocationAccessService
{
    Task<List<TblLocationAccessRule>> GetRulesForLocationAsync(int locationId);
    Task<TblLocationAccessRule> CreateRuleAsync(int locationId, int? userId, int? userGroupId,
        string accessLevel, string? viewScope, string editUser);
    Task UpdateRuleAsync(int ruleId, string accessLevel, string? viewScope, string editUser);
    Task DeleteRuleAsync(int ruleId);

    /// <summary>Returns all schedules for a specific access rule, ordered by start date/time.</summary>
    Task<List<TblAccessSchedule>> GetSchedulesForRuleAsync(int ruleId);

    /// <summary>Returns schedule counts keyed by rule ID for a set of rules (used for badge display).</summary>
    Task<Dictionary<int, int>> GetScheduleCountsAsync(IEnumerable<int> ruleIds);

    /// <summary>Persists a new schedule after validation; triggers cache refresh.</summary>
    Task<TblAccessSchedule> AddScheduleAsync(TblAccessSchedule schedule, string editUser);

    /// <summary>Permanently removes a schedule; triggers cache refresh.</summary>
    Task DeleteScheduleAsync(int scheduleId);
}
