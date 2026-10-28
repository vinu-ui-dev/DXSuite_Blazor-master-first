namespace DXSuite.Director.Entities;

/// <summary>
/// Shared interface for entities with time-based scheduling (access schedules, floor plan icon schedules).
/// Enables reuse of ScheduleEvaluator across different schedule types.
/// </summary>
public interface IScheduleWindow
{
    DateTime? StartDate { get; }
    DateTime? EndDate { get; }
    TimeOnly? StartTime { get; }
    TimeOnly? EndTime { get; }
    bool Sunday { get; }
    bool Monday { get; }
    bool Tuesday { get; }
    bool Wednesday { get; }
    bool Thursday { get; }
    bool Friday { get; }
    bool Saturday { get; }
}
