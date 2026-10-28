using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

/// <summary>
/// Saves and deletes a display's schedule events from the schedule editors, invalidating the display's
/// publication after each write.
/// </summary>
public interface IScheduleEventService
{
    /// <summary>
    /// Upserts an event. A default-content event replaces the display's existing default content; any other
    /// event updates the row with its id, or is added when it has none.
    /// </summary>
    Task SaveEventAsync(TblScheduleEvent scheduleEvent, string contentAssignmentJson, CancellationToken ct = default);

    /// <returns>False when the event no longer exists.</returns>
    Task<bool> DeleteEventAsync(int scheduleEventId, CancellationToken ct = default);
}

public class ScheduleEventService : IScheduleEventService
{
    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly IPublicationInvalidator _invalidator;

    public ScheduleEventService(IDbContextFactory<DirectorDbContext> dbFactory, IPublicationInvalidator invalidator)
    {
        _dbFactory = dbFactory;
        _invalidator = invalidator;
    }

    public async Task SaveEventAsync(TblScheduleEvent scheduleEvent, string contentAssignmentJson, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        if (scheduleEvent.DefaultContent)
        {
            var existingDefault = await db.TblScheduleEvents
                .FirstOrDefaultAsync(e => e.DisplayId == scheduleEvent.DisplayId && e.DefaultContent, ct);
            if (existingDefault != null)
                existingDefault.ContentAssignment = contentAssignmentJson;
            else
                Add(db, scheduleEvent, contentAssignmentJson);
        }
        else if (scheduleEvent.ScheduleEventId == 0)
        {
            Add(db, scheduleEvent, contentAssignmentJson);
        }
        else
        {
            var existing = await db.TblScheduleEvents.FirstOrDefaultAsync(e => e.ScheduleEventId == scheduleEvent.ScheduleEventId, ct);
            if (existing != null)
            {
                existing.StartDate = scheduleEvent.StartDate;
                existing.StartTime = scheduleEvent.StartTime;
                existing.EndDate = scheduleEvent.EndDate;
                existing.EndTime = scheduleEvent.EndTime;
                existing.Sunday = scheduleEvent.Sunday;
                existing.Monday = scheduleEvent.Monday;
                existing.Tuesday = scheduleEvent.Tuesday;
                existing.Wednesday = scheduleEvent.Wednesday;
                existing.Thursday = scheduleEvent.Thursday;
                existing.Friday = scheduleEvent.Friday;
                existing.Saturday = scheduleEvent.Saturday;
                existing.ContentAssignment = contentAssignmentJson;
            }
            else
            {
                Add(db, scheduleEvent, contentAssignmentJson);
            }
        }

        await db.SaveChangesAsync(ct);
        _invalidator.DisplayScheduleChanged(scheduleEvent.DisplayId);
    }

    public async Task<bool> DeleteEventAsync(int scheduleEventId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var scheduleEvent = await db.TblScheduleEvents.FirstOrDefaultAsync(e => e.ScheduleEventId == scheduleEventId, ct);
        if (scheduleEvent == null)
            return false;

        db.TblScheduleEvents.Remove(scheduleEvent);
        await db.SaveChangesAsync(ct);
        _invalidator.DisplayScheduleChanged(scheduleEvent.DisplayId);
        return true;
    }

    private static void Add(DirectorDbContext db, TblScheduleEvent scheduleEvent, string contentAssignmentJson)
    {
        scheduleEvent.ContentAssignment = contentAssignmentJson;
        db.TblScheduleEvents.Add(scheduleEvent);
    }
}
