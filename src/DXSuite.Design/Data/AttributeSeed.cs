using DXSuite.Design.Entities;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Design.Data;

public static class AttributeSeed
{
    /// <summary>
    /// Defines the four system attributes required by the worksheet engine.
    /// These are global, typed, undeletable, and created once per tenant.
    /// </summary>
    private static readonly (string Name, AttributeType Type)[] SystemAttributes =
    [
        ("LAN Drops",      AttributeType.Number),
        ("PoE Power",      AttributeType.Number),
        ("Power Outlets",  AttributeType.Number),
        ("Shipping",       AttributeType.Dollars),
    ];

    /// <summary>
    /// Idempotent seed: ensures the four system attributes exist for a tenant.
    /// Matches by name — safe to call on every startup.
    /// </summary>
    public static async Task SeedAsync(DesignDbContext db, string customerId, CancellationToken ct = default)
    {
        var existingNames = await db.TblDesignAttributes
            .Where(a => a.CustomerId == customerId && a.IsSystem)
            .Select(a => a.Name)
            .ToListAsync(ct);

        var existingSet = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, type) in SystemAttributes)
        {
            if (existingSet.Contains(name)) continue;

            db.TblDesignAttributes.Add(new TblDesignAttribute
            {
                CustomerId = customerId,
                Name = name,
                AttributeType = type,
                IsGlobal = true,
                IsSystem = true,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
