using DXSuite.Director.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// Moves the messaging delivery version (INK-09) in any Director save that adds or deletes a Director Display: the
/// delivery body lists every display, and displays are made and deleted in many places (the location editor, layouts,
/// the content package importer, every hard delete), so the bump lives here rather than at each of them.
/// <see cref="MessagingServiceExtensions.AddMessaging"/> registers it for the Director context factory.
/// </summary>
/// <remarks>
/// The bump is staged on the saving context, so it commits with the display change or not at all. It reads nothing:
/// the state row is seeded by its migration, so an update of its <c>Version</c> alone is enough.
/// </remarks>
public sealed class DeliveryVersionInterceptor : SaveChangesInterceptor, IDirectorSaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        BumpIfDisplaysChanged(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        BumpIfDisplaysChanged(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void BumpIfDisplaysChanged(DbContext? context)
    {
        if (context is DirectorDbContext db && DeliveryVersion.DisplaysAddedOrDeleted(db.ChangeTracker))
            DeliveryVersion.BumpWithoutReading(db);
    }
}
