using DXSuite.Director.Entities;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Tests;

/// <summary>Reads the tenant's delivery version (INK-09), which each bump-site test compares before and after.</summary>
internal static class DeliveryVersionProbe
{
    public static async Task<Guid> ReadAsync(Func<DirectorDbContext> director)
    {
        await using var db = director();
        return await db.TblMessagingDeliveryStates.AsNoTracking()
            .Where(s => s.MessagingDeliveryStateId == TblMessagingDeliveryState.SingleRowId)
            .Select(s => s.Version)
            .SingleAsync();
    }
}
