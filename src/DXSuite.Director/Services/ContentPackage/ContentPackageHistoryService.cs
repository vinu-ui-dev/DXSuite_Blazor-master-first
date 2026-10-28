using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DXSuite.Director.Entities;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services.ContentPackage;

public class ContentPackageHistoryService : IContentPackageHistoryService
{
    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;

    public ContentPackageHistoryService(IDbContextFactory<DirectorDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<ContentPackageHistoryListRow>> ListAsync(
        string? directionFilter,
        int take,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var q = db.TblContentPackageHistories.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(directionFilter))
        {
            q = q.Where(h => h.Direction == directionFilter);
        }

        return await q.OrderByDescending(h => h.StartedAt)
            .Take(take <= 0 ? 100 : take)
            .Select(h => new ContentPackageHistoryListRow
            {
                ContentPackageHistoryId = h.ContentPackageHistoryId,
                Direction = h.Direction,
                Status = h.Status,
                BundleFileName = h.BundleFileName,
                InitiatedByUser = h.InitiatedByUser,
                SourceEnvironment = h.SourceEnvironment,
                ScopeDescription = h.ScopeDescription,
                TotalRecords = h.TotalRecords,
                InsertedCount = h.InsertedCount,
                UpdatedCount = h.UpdatedCount,
                SkippedCount = h.SkippedCount,
                StartedAt = h.StartedAt,
                CompletedAt = h.CompletedAt,
                ErrorMessage = h.ErrorMessage
            })
            .ToListAsync(ct);
    }

    public async Task<ContentPackageHistoryDetail?> GetDetailAsync(int historyId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var header = await db.TblContentPackageHistories.AsNoTracking()
            .Where(h => h.ContentPackageHistoryId == historyId)
            .Select(h => new ContentPackageHistoryListRow
            {
                ContentPackageHistoryId = h.ContentPackageHistoryId,
                Direction = h.Direction,
                Status = h.Status,
                BundleFileName = h.BundleFileName,
                InitiatedByUser = h.InitiatedByUser,
                SourceEnvironment = h.SourceEnvironment,
                ScopeDescription = h.ScopeDescription,
                TotalRecords = h.TotalRecords,
                InsertedCount = h.InsertedCount,
                UpdatedCount = h.UpdatedCount,
                SkippedCount = h.SkippedCount,
                StartedAt = h.StartedAt,
                CompletedAt = h.CompletedAt,
                ErrorMessage = h.ErrorMessage
            })
            .FirstOrDefaultAsync(ct);

        if (header == null) return null;

        var items = await db.TblContentPackageHistoryItems.AsNoTracking()
            .Where(i => i.ContentPackageHistoryId == historyId)
            .OrderBy(i => i.EntityType).ThenBy(i => i.ContentPackageId)
            .Select(i => new ContentPackageHistoryDetailItem
            {
                ContentPackageId = i.ContentPackageId,
                EntityType = i.EntityType,
                Action = i.Action,
                SourceUserPrincipalName = i.SourceUserPrincipalName,
                Notes = i.Notes
            })
            .ToListAsync(ct);

        return new ContentPackageHistoryDetail { Header = header, Items = items };
    }
}
