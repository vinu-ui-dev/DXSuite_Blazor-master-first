using DXSuite.Director.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// The tenant's messaging delivery version (INK-09): every change that can alter the delivery body calls
/// <see cref="BumpAsync"/> before the save that commits it, so the new version commits with the change or not at all.
/// The doorbell job nudges the Moxie script when it sees the version move.
/// </summary>
/// <remarks>
/// A bump only stages the row on the caller's context, so it rides in whatever transaction the caller's next
/// SaveChanges uses. Director Displays added or deleted are bumped by <see cref="DeliveryVersionInterceptor"/> on
/// every save, because the body lists every display and displays are made and deleted in many places.
/// </remarks>
public static class DeliveryVersion
{
    public static async Task BumpAsync(IDirectorDBContext db, CancellationToken ct)
    {
        var row = await db.TblMessagingDeliveryStates.FindAsync([TblMessagingDeliveryState.SingleRowId], ct);
        Bump(db, row);
    }

    /// <summary>
    /// Bumps without a read, for a save already under way: the tracked row when there is one, else the seeded row by key
    /// with only <c>Version</c> marked modified.
    /// </summary>
    internal static void BumpWithoutReading(DirectorDbContext db)
    {
        var row = db.TblMessagingDeliveryStates.Local.FirstOrDefault(r => r.MessagingDeliveryStateId == TblMessagingDeliveryState.SingleRowId);
        if (row is null)
        {
            row = new TblMessagingDeliveryState { MessagingDeliveryStateId = TblMessagingDeliveryState.SingleRowId };
            db.TblMessagingDeliveryStates.Attach(row);
        }
        row.Version = Guid.NewGuid();
        db.Entry(row).Property(r => r.Version).IsModified = true;
    }

    // Only Version is marked modified, so the save never overwrites the doorbell's own columns.
    private static void Bump(IDirectorDBContext db, TblMessagingDeliveryState? row)
    {
        if (row is null)
        {
            row = new TblMessagingDeliveryState { MessagingDeliveryStateId = TblMessagingDeliveryState.SingleRowId };
            db.TblMessagingDeliveryStates.Add(row);
        }
        row.Version = Guid.NewGuid();
    }

    /// <summary>Whether a save adds or deletes a Director Display, which adds or drops an entry of the body's <c>displays</c>.</summary>
    internal static bool DisplaysAddedOrDeleted(ChangeTracker changes) =>
        changes.Entries<TblDisplay>().Any(e => e.State is EntityState.Added or EntityState.Deleted);
}
