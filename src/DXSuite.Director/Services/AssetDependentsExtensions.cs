using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

/// <summary>
/// Removes every row that names an asset before the asset row itself is deleted, so a hard delete never hits a
/// foreign-key violation on a dependent the caller didn't know to clear by hand.
/// </summary>
/// <remarks>
/// Covers the Messaging dependents introduced by GTAA FSD 1.1 (an asset used as a Message's slot content, or a
/// pending revision's proposed replacement for one). Other asset-dependent tables this product already had before
/// Messaging existed are unaffected by this change — this extension only adds coverage, it does not replace
/// whatever cleanup already ran before a delete.
/// </remarks>
public static class AssetDependentsExtensions
{
    public static async Task RemoveAssetDependentsAsync(this IDirectorDBContext db, int assetId, CancellationToken ct = default)
    {
        await db.TblMessageAssets.Where(a => a.AssetId == assetId).ExecuteDeleteAsync(ct);
        await db.TblMessageRevisionAssets.Where(a => a.AssetId == assetId).ExecuteDeleteAsync(ct);
    }
}
