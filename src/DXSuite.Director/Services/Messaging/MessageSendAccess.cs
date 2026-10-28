using DXSuite.Director.Entities;
using DXSuite.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// The rule check behind every send dialog (FSD 1.1 ZONE-08): what a user may send a Message Type to, read from
/// that type's Message access rules (ZONE-07). Admin needs no Message access rule and reaches everything. A rule on
/// a retired Display Zone grants nothing (ZONE-12), and a rule for every Display Zone reaches only live ones.
/// </summary>
/// <remarks>
/// <para>
/// Every call takes the user and whether the Message is one-off. A one-off Message (typed at send time, unapproved)
/// is reached only through rules that allow one-off text; <c>CanFreeText</c> is the only gate on unapproved text.
/// The answer is read fresh on each call, so a send should check again when it saves.
/// </para>
/// <para>
/// Display Zones mode offers <see cref="ZonesAsync"/>. One-display mode offers <see cref="DisplaysAsync"/>: the
/// displays inside a covered Display Zone, or any display at all under a rule for every Display Zone, grouped by
/// location only to help find one. A send saves only targets its <see cref="ReachAsync"/> answer covers.
/// </para>
/// <para>
/// 1.2 (AM-01): Counter Messages are never governed by Message access rules; this check is for sends to Display
/// Zones or one display only.
/// </para>
/// </remarks>
public interface IMessageSendAccess
{
    /// <summary>The Display Zones and displays the user may send the type to.</summary>
    Task<MessageSendReach> ReachAsync(int userId, int messageTypeId, bool oneOff, CancellationToken ct = default);

    /// <summary>The live Display Zones the user may send the type to, in Display Zone list order.</summary>
    Task<IReadOnlyList<SendableZone>> ZonesAsync(int userId, int messageTypeId, bool oneOff, CancellationToken ct = default);

    /// <summary>
    /// The displays the user may send the type to one at a time, grouped by location (location order, then name)
    /// with each location's displays by name. Locations with no such display are left out.
    /// </summary>
    Task<IReadOnlyList<SendableLocation>> DisplaysAsync(int userId, int messageTypeId, bool oneOff, CancellationToken ct = default);
}

/// <summary>What one user may send one Message Type to.</summary>
/// <param name="ZoneIds">The live Display Zones covered.</param>
/// <param name="AnyDisplay">
/// A rule for every Display Zone (or Admin): in one-display mode any display may be picked, even one in no zone.
/// </param>
public sealed record MessageSendReach(IReadOnlySet<int> ZoneIds, bool AnyDisplay)
{
    public static MessageSendReach Nothing { get; } = new(new HashSet<int>(), false);

    public bool IsEmpty => ZoneIds.Count == 0 && !AnyDisplay;

    /// <summary>True when every listed Display Zone is covered; an empty list is covered.</summary>
    public bool CoversZones(IEnumerable<int> zoneIds) => zoneIds.All(ZoneIds.Contains);
}

public sealed record SendableZone(int ZoneId, string Name);

public sealed record SendableLocation(int LocationId, string Name, IReadOnlyList<SendableDisplay> Displays);

public sealed record SendableDisplay(int DisplayId, string Name);

internal sealed class MessageSendAccess(IDbContextFactory<DirectorDbContext> dbFactory) : IMessageSendAccess
{
    public async Task<MessageSendReach> ReachAsync(int userId, int messageTypeId, bool oneOff, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await ReachAsync(db, userId, messageTypeId, oneOff, ct);
    }

    public async Task<IReadOnlyList<SendableZone>> ZonesAsync(int userId, int messageTypeId, bool oneOff, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var reach = await ReachAsync(db, userId, messageTypeId, oneOff, ct);
        if (reach.ZoneIds.Count == 0) return [];

        var ids = reach.ZoneIds.ToList();
        return await db.TblZones
            .Where(z => ids.Contains(z.ZoneId))
            .OrderBy(z => z.SortOrder).ThenBy(z => z.ZoneId)
            .Select(z => new SendableZone(z.ZoneId, z.ZoneName))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SendableLocation>> DisplaysAsync(int userId, int messageTypeId, bool oneOff, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var reach = await ReachAsync(db, userId, messageTypeId, oneOff, ct);
        if (reach.IsEmpty) return [];

        var displays = db.TblDisplays.AsQueryable();
        if (!reach.AnyDisplay)
        {
            var zoneIds = reach.ZoneIds.ToList();
            displays = displays.Where(d => db.TblZoneDisplays.Any(zd => zd.DisplayId == d.DisplayId && zoneIds.Contains(zd.ZoneId)));
        }

        var rows = await displays
            .Select(d => new
            {
                d.DisplayId,
                d.DisplayName,
                d.LocationId,
                d.Location.LocationName,
                LocationOrder = d.Location.SortOrder,
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(d => (d.LocationId, d.LocationName, d.LocationOrder))
            .OrderBy(g => g.Key.LocationOrder).ThenBy(g => g.Key.LocationName)
            .Select(g => new SendableLocation(g.Key.LocationId, g.Key.LocationName ?? $"Location {g.Key.LocationId}",
                g.OrderBy(d => d.DisplayName).Select(d => new SendableDisplay(d.DisplayId, d.DisplayName)).ToList()))
            .ToList();
    }

    private static async Task<MessageSendReach> ReachAsync(DirectorDbContext db, int userId, int messageTypeId, bool oneOff,
        CancellationToken ct)
    {
        var liveZones = db.TblZones.Where(z => z.RetiredUtc == null).Select(z => z.ZoneId);

        if (await db.TblUsers.AnyAsync(u => u.UserId == userId && u.RoleId == (int)Roles.Role.Admin, ct))
            return new MessageSendReach((await liveZones.ToListAsync(ct)).ToHashSet(), AnyDisplay: true);

        var groupIds = db.TblUserGroupMemberships.Where(m => m.UserId == userId).Select(m => m.UserGroupId);
        var rules = await db.TblMessagePermissions
            .Where(p => p.MessageTypeId == messageTypeId)
            .Where(p => p.UserId == userId || (p.UserGroupId != null && groupIds.Contains(p.UserGroupId.Value)))
            .Where(p => !oneOff || p.CanFreeText)
            .Select(p => p.ZoneId)
            .ToListAsync(ct);
        if (rules.Count == 0) return MessageSendReach.Nothing;

        // A rule for every Display Zone (null ZoneId) reaches the live ones; a rule on a retired one reaches nothing.
        var every = rules.Contains(null);
        var named = rules.OfType<int>().ToList();
        var zoneIds = await liveZones.Where(id => every || named.Contains(id)).ToListAsync(ct);
        return new MessageSendReach(zoneIds.ToHashSet(), AnyDisplay: every);
    }
}
