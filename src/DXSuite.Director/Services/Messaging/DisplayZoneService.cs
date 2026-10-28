using DXSuite.Director.Entities;
using DXSuite.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// Configuration > Display Zones (FSD 1.1 ZONE-11, ZONE-12): the Display Zone list, its order, each zone's displays,
/// and the delete or retire rules. Every write needs <c>config-tab:zone-builder</c>, checked here as well as by the tab.
/// </summary>
public interface IDisplayZoneService
{
    /// <summary>Display Zones in list order; retired ones only when asked for.</summary>
    Task<IReadOnlyList<DisplayZoneSummary>> ListAsync(bool includeRetired, CancellationToken ct = default);

    /// <summary>Every location with its displays, the tree a Display Zone's displays are ticked in.</summary>
    Task<IReadOnlyList<DisplayZoneLocation>> LocationTreeAsync(CancellationToken ct = default);

    /// <summary>The displays a Display Zone holds now.</summary>
    Task<IReadOnlySet<int>> DisplayIdsAsync(int zoneId, CancellationToken ct = default);

    /// <summary>
    /// Creates a Display Zone (<see cref="DisplayZoneEdit.ZoneId"/> null) or changes one. Membership is saved by
    /// difference, so saving an unchanged Display Zone writes nothing.
    /// </summary>
    Task<DisplayZoneSaveResult> SaveAsync(DisplayZoneEdit edit, CancellationToken ct = default);

    /// <summary>
    /// Puts the listed Display Zones in this order. Zones not listed (retired ones while hidden) keep their places.
    /// </summary>
    Task ReorderAsync(IReadOnlyList<int> zoneIds, CancellationToken ct = default);

    /// <summary>Deletes a Display Zone no Activation has ever targeted, with its Message access rules.</summary>
    Task<DisplayZoneRemoval> DeleteAsync(int zoneId, CancellationToken ct = default);

    /// <summary>Retires a Display Zone: it leaves the list and the pickers, and past Activations keep its name.</summary>
    Task<DisplayZoneRemoval> RetireAsync(int zoneId, CancellationToken ct = default);
}

/// <param name="EverTargeted">An Activation has targeted it, so it can only be retired (ZONE-12).</param>
/// <param name="InUse">An active or scheduled Activation targets it, so it can be neither deleted nor retired.</param>
public sealed record DisplayZoneSummary(
    int ZoneId, string Name, string? Description, int DisplayCount, DateTime? RetiredUtc, bool EverTargeted, bool InUse)
{
    public bool IsRetired => RetiredUtc is not null;
}

public sealed record DisplayZoneLocation(int LocationId, string Name, IReadOnlyList<DisplayZoneDisplay> Displays);

public sealed record DisplayZoneDisplay(int DisplayId, string Name);

public sealed record DisplayZoneEdit(int? ZoneId, string Name, string? Description, IReadOnlySet<int> DisplayIds);

public enum DisplayZoneSaveOutcome
{
    Saved,
    NameRequired,
    NameTaken,
    NotFound,
}

/// <param name="RowsWritten">Rows inserted, updated or deleted; zero when nothing changed.</param>
public sealed record DisplayZoneSaveResult(DisplayZoneSaveOutcome Outcome, int ZoneId = 0, int RowsWritten = 0);

public enum DisplayZoneRemoval
{
    Deleted,
    Retired,

    /// <summary>An Activation has targeted it, so it can only be retired.</summary>
    Targeted,

    /// <summary>An active or scheduled Activation targets it (A-33).</summary>
    InUse,
    NotFound,
}

/// <remarks>
/// Saves, retires and deletes log their <c>display-zone.*</c> event once committed (AUD-08); a refused or failed one
/// logs with that outcome. A reorder has no action key of its own in AUD-08 and is not logged.
/// </remarks>
internal sealed class DisplayZoneService(
    IDbContextFactory<DirectorDbContext> dbFactory,
    ISessionState session,
    TimeProvider clock,
    IUserEventLog events,
    ILogger<DisplayZoneService> logger) : IDisplayZoneService
{
    public async Task<IReadOnlyList<DisplayZoneSummary>> ListAsync(bool includeRetired, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var liveScreens = db.TblMessageActivationTargets.Where(ActivationState.TargetActiveOrScheduled(now));
        var liveChosen = db.TblMessageActivationZones.Where(ActivationState.ChosenZoneActiveOrScheduled(now));
        return await db.TblZones
            .Where(z => includeRetired || z.RetiredUtc == null)
            .OrderBy(z => z.SortOrder).ThenBy(z => z.ZoneId)
            .Select(z => new DisplayZoneSummary(
                z.ZoneId, z.ZoneName, z.Description, z.ZoneDisplays.Count, z.RetiredUtc,
                db.TblMessageActivationTargets.Any(t => t.ZoneId == z.ZoneId) || db.TblMessageActivationZones.Any(c => c.ZoneId == z.ZoneId),
                liveScreens.Any(t => t.ZoneId == z.ZoneId) || liveChosen.Any(c => c.ZoneId == z.ZoneId)))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DisplayZoneLocation>> LocationTreeAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var locations = await db.TblLocations
            .Where(l => l.TblDisplays.Any())
            .OrderBy(l => l.SortOrder).ThenBy(l => l.LocationName)
            .Select(l => new
            {
                l.LocationId,
                l.LocationName,
                Displays = l.TblDisplays.OrderBy(d => d.DisplayName).Select(d => new DisplayZoneDisplay(d.DisplayId, d.DisplayName)).ToList(),
            })
            .ToListAsync(ct);
        return locations.Select(l => new DisplayZoneLocation(l.LocationId, l.LocationName ?? $"Location {l.LocationId}", l.Displays)).ToList();
    }

    public async Task<IReadOnlySet<int>> DisplayIdsAsync(int zoneId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = await db.TblZoneDisplays.Where(z => z.ZoneId == zoneId).Select(z => z.DisplayId).ToListAsync(ct);
        return ids.ToHashSet();
    }

    public async Task<DisplayZoneSaveResult> SaveAsync(DisplayZoneEdit edit, CancellationToken ct = default)
    {
        var attemptKey = edit.ZoneId is null ? UserEventActionKeys.DisplayZoneCreated : UserEventActionKeys.DisplayZoneRenamed;
        await DemandAsync(new UserEvent(attemptKey, MessagingEvents.DisplayZone(edit.ZoneId, edit.Name?.Trim())));

        var name = edit.Name?.Trim() ?? "";
        if (name.Length == 0) return new(DisplayZoneSaveOutcome.NameRequired);
        var description = string.IsNullOrWhiteSpace(edit.Description) ? null : edit.Description.Trim();

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Names are unique across retired Display Zones too (UX_tblZone_ZoneName).
        if (await db.TblZones.AnyAsync(z => z.ZoneName == name && z.ZoneId != edit.ZoneId, ct))
            return new(DisplayZoneSaveOutcome.NameTaken);

        TblZone zone;
        if (edit.ZoneId is { } zoneId)
        {
            var found = await db.TblZones.Include(z => z.ZoneDisplays).SingleOrDefaultAsync(z => z.ZoneId == zoneId, ct);
            if (found is null) return new(DisplayZoneSaveOutcome.NotFound);
            zone = found;
        }
        else
        {
            // A new Display Zone goes to the end of the list.
            var last = await db.TblZones.MaxAsync(z => (int?)z.SortOrder, ct) ?? 0;
            zone = new TblZone { SortOrder = last + 1, CreatedUtc = clock.GetUtcNow().UtcDateTime };
            db.TblZones.Add(zone);
        }

        var (oldName, oldDescription) = (zone.ZoneName, zone.Description);

        // A rename changes only the name; the ZoneId stays (ZONE-04).
        if (zone.ZoneName != name) zone.ZoneName = name;
        if (zone.Description != description) zone.Description = description;

        // Only the difference is written (ZONE-11), so unchanged membership touches no rows.
        var removed = zone.ZoneDisplays.Where(z => !edit.DisplayIds.Contains(z.DisplayId)).ToList();
        foreach (var row in removed)
            zone.ZoneDisplays.Remove(row);
        var stored = zone.ZoneDisplays.Select(z => z.DisplayId).ToHashSet();
        var added = edit.DisplayIds.Where(id => !stored.Contains(id)).Order().ToList();
        foreach (var displayId in added)
            zone.ZoneDisplays.Add(new TblZoneDisplay { DisplayId = displayId });

        var written = await events.FailureLoggedAsync(logger,
            new UserEvent(attemptKey, MessagingEvents.DisplayZone(edit.ZoneId, name)), () => db.SaveChangesAsync(ct));

        var target = MessagingEvents.DisplayZone(zone.ZoneId, zone.ZoneName);
        if (edit.ZoneId is null)
            await events.TryWriteAsync(logger, new UserEvent(UserEventActionKeys.DisplayZoneCreated, target, Details: Described(zone)));
        else if (oldName != zone.ZoneName || oldDescription != zone.Description)
            await events.TryWriteAsync(logger, new UserEvent(UserEventActionKeys.DisplayZoneRenamed, target, Details: new Dictionary<string, object?>
            {
                ["name"] = new UserEventChanges.Change(oldName, zone.ZoneName),
                ["description"] = new UserEventChanges.Change(oldDescription, zone.Description),
                ["order"] = zone.SortOrder,
            }));
        if (added.Count > 0 || removed.Count > 0)
            await events.TryWriteAsync(logger, new UserEvent(UserEventActionKeys.DisplayZoneMembershipChanged, target,
                Details: new { added, removed = removed.Select(r => r.DisplayId).Order().ToList() }));
        return new(DisplayZoneSaveOutcome.Saved, zone.ZoneId, written);
    }

    /// <summary>The details of <c>display-zone.created</c>, <c>.retired</c> and <c>.deleted</c> (AUD-08).</summary>
    private static object Described(TblZone zone) => new { name = zone.ZoneName, description = zone.Description, order = zone.SortOrder };

    public async Task ReorderAsync(IReadOnlyList<int> zoneIds, CancellationToken ct = default)
    {
        await DemandAsync(refusal: null);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var zones = await db.TblZones.OrderBy(z => z.SortOrder).ThenBy(z => z.ZoneId).ToListAsync(ct);
        var byId = zones.ToDictionary(z => z.ZoneId);

        // SortOrder is list order only and gives no rank (ZONE-11), so it is renumbered from 1 in the new order.
        var order = DisplayZoneOrder.Apply(zones.Select(z => z.ZoneId).ToList(), zoneIds);
        for (var i = 0; i < order.Count; i++)
        {
            var zone = byId[order[i]];
            if (zone.SortOrder != i + 1) zone.SortOrder = i + 1;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<DisplayZoneRemoval> DeleteAsync(int zoneId, CancellationToken ct = default)
    {
        await DemandAsync(new UserEvent(UserEventActionKeys.DisplayZoneDeleted, MessagingEvents.DisplayZone(zoneId, null)));
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var zone = await db.TblZones.SingleOrDefaultAsync(z => z.ZoneId == zoneId, ct);
        if (zone is null) return DisplayZoneRemoval.NotFound;

        var deleted = new UserEvent(UserEventActionKeys.DisplayZoneDeleted, MessagingEvents.DisplayZone(zoneId, zone.ZoneName),
            Details: Described(zone));
        // A targeted Display Zone is named by its Activations' history, so it can only be retired (ZONE-12).
        var refusal = await InUseAsync(db, zoneId, ct) ? DisplayZoneRemoval.InUse
            : await EverTargetedAsync(db, zoneId, ct) ? DisplayZoneRemoval.Targeted
            : (DisplayZoneRemoval?)null;
        if (refusal is { } refused)
        {
            await events.TryWriteAsync(logger, deleted with { Outcome = UserEventOutcome.Refused });
            return refused;
        }

        // Removed here rather than left to the cascades, which a database carried over from legacy Director may lack.
        db.TblMessagePermissions.RemoveRange(await db.TblMessagePermissions.Where(p => p.ZoneId == zoneId).ToListAsync(ct));
        db.TblZoneDisplays.RemoveRange(await db.TblZoneDisplays.Where(z => z.ZoneId == zoneId).ToListAsync(ct));
        db.TblZones.Remove(zone);
        await events.FailureLoggedAsync(logger, deleted, () => db.SaveChangesAsync(ct));
        await events.TryWriteAsync(logger, deleted);
        return DisplayZoneRemoval.Deleted;
    }

    public async Task<DisplayZoneRemoval> RetireAsync(int zoneId, CancellationToken ct = default)
    {
        await DemandAsync(new UserEvent(UserEventActionKeys.DisplayZoneRetired, MessagingEvents.DisplayZone(zoneId, null)));
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var zone = await db.TblZones.SingleOrDefaultAsync(z => z.ZoneId == zoneId, ct);
        if (zone is null) return DisplayZoneRemoval.NotFound;
        if (zone.RetiredUtc is not null) return DisplayZoneRemoval.Retired;

        var retired = new UserEvent(UserEventActionKeys.DisplayZoneRetired, MessagingEvents.DisplayZone(zoneId, zone.ZoneName),
            Details: Described(zone));
        if (await InUseAsync(db, zoneId, ct))
        {
            await events.TryWriteAsync(logger, retired with { Outcome = UserEventOutcome.Refused });
            return DisplayZoneRemoval.InUse;
        }

        zone.RetiredUtc = clock.GetUtcNow().UtcDateTime;
        await events.FailureLoggedAsync(logger, retired, () => db.SaveChangesAsync(ct));
        await events.TryWriteAsync(logger, retired);
        return DisplayZoneRemoval.Retired;
    }

    // A Display Zone picked at send counts whether or not a screen row came through it (ZONE-06): the rows name the
    // zones that reached each display, the picked zones the ones the sender chose.
    private static async Task<bool> EverTargetedAsync(DirectorDbContext db, int zoneId, CancellationToken ct) =>
        await db.TblMessageActivationTargets.AnyAsync(t => t.ZoneId == zoneId, ct)
        || await db.TblMessageActivationZones.AnyAsync(c => c.ZoneId == zoneId, ct);

    private async Task<bool> InUseAsync(DirectorDbContext db, int zoneId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.TblMessageActivationTargets.Where(ActivationState.TargetActiveOrScheduled(now)).AnyAsync(t => t.ZoneId == zoneId, ct)
            || await db.TblMessageActivationZones.Where(ActivationState.ChosenZoneActiveOrScheduled(now)).AnyAsync(c => c.ZoneId == zoneId, ct);
    }

    /// <param name="refusal">The attempt to log as refused when the key is missing; null for a change with no action key.</param>
    private Task DemandAsync(UserEvent? refusal) => session.DemandAsync(PermissionKeys.ConfigTabZoneBuilder, refusal, events, logger);
}

/// <summary>The Display Zone list order after the user reorders the zones they can see.</summary>
public static class DisplayZoneOrder
{
    /// <summary>
    /// Puts <paramref name="reordered"/> into the places those zones hold in <paramref name="current"/>, so zones the
    /// user could not see (retired ones while hidden) keep their places. Ids not in <paramref name="current"/> are ignored.
    /// </summary>
    public static IReadOnlyList<int> Apply(IReadOnlyList<int> current, IReadOnlyList<int> reordered)
    {
        var known = current.ToHashSet();
        var listed = reordered.Where(known.Contains).Distinct().ToList();
        var moved = listed.ToHashSet();
        var next = new Queue<int>(listed);
        return current.Select(id => moved.Contains(id) ? next.Dequeue() : id).ToList();
    }
}
