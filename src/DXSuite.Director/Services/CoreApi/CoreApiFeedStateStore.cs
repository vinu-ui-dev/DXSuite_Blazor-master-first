using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Director.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services.CoreApi;

/// <summary>
/// Keeps the tenant's <see cref="CoreApiFeedState"/> in its Director database (<c>tblCoreApiFeedState</c>).
/// Anyone may read it; only the CoreAPI watcher replaces it. Each call opens its own context from the scope's
/// tenant-aware factory.
/// </summary>
public sealed class CoreApiFeedStateStore(IDbContextFactory<DirectorDbContext> dbFactory) : ICoreApiFeedStateReader
{
    public async Task<CoreApiFeedState?> ReadAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.TblCoreApiFeedStates.AsNoTracking()
            .SingleOrDefaultAsync(s => s.CoreApiFeedStateId == TblCoreApiFeedState.SingleRowId, ct);
        return row is null ? null : new CoreApiFeedState(row.Status, row.Reachable, row.SwitchOn, row.ChangedUtc);
    }

    /// <summary>
    /// Replaces the state only if it is still <paramref name="expected"/> (null: no row yet), so that when two
    /// writers see the same change only one records it, and only that one logs it.
    /// </summary>
    /// <returns>True when this call made the change.</returns>
    internal async Task<bool> TryReplaceAsync(CoreApiFeedState? expected, CoreApiFeedState next, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (expected is null)
        {
            db.TblCoreApiFeedStates.Add(new TblCoreApiFeedState
            {
                CoreApiFeedStateId = TblCoreApiFeedState.SingleRowId,
                Status = next.Status,
                Reachable = next.Reachable,
                SwitchOn = next.SwitchOn,
                ChangedUtc = next.ChangedUtc,
            });
            try
            {
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
            {
                return false;
            }
        }

        var changed = await db.TblCoreApiFeedStates
            .Where(s => s.CoreApiFeedStateId == TblCoreApiFeedState.SingleRowId
                && s.Status == expected.Status
                && s.Reachable == expected.Reachable
                && s.SwitchOn == expected.SwitchOn)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, next.Status)
                .SetProperty(r => r.Reachable, next.Reachable)
                .SetProperty(r => r.SwitchOn, next.SwitchOn)
                .SetProperty(r => r.ChangedUtc, next.ChangedUtc), ct);
        return changed == 1;
    }
}
