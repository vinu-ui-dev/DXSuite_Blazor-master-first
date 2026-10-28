using DXSuite.Design.Entities;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Design.Data;

public static class ColumnSeed
{
    private static readonly (string Name, int SortOrder)[] _defaults =
    [
        ("System Build",          10),
        ("Cabling & Termination", 20),
        ("Programming",           30),
        ("Commissioning",         40),
        ("Installation",          50),
        ("Training",              60),
    ];

    /// <summary>
    /// Idempotent — seeds the default AV column set for the given customer if none exist yet.
    /// Called on CatalogPage load so columns are available before the user visits the Columns subtab.
    /// </summary>
    public static async Task SeedAsync(IDbContextFactory<DesignDbContext> factory, string customerId)
    {
        await using var db = await factory.CreateDbContextAsync();

        // Skip entirely if this customer already has at least one column defined.
        if (await db.TblDesignLaborColumns.AnyAsync(c => c.CustomerId == customerId))
            return;

        foreach (var (name, order) in _defaults)
        {
            db.TblDesignLaborColumns.Add(new TblDesignLaborColumn
            {
                CustomerId = customerId,
                Name       = name,
                SortOrder  = order,
            });
        }

        await db.SaveChangesAsync();
    }
}
