using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DXSuite.Director.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DXSuite.Director.Services.ContentPackage;

/// <summary>
/// Resolves the transitive set of records that must be included in a content package bundle.
///
/// Resolution order is fixed: selection → location tree expansion → parent backfill
/// (groups, constraints) → optional access-control layer. Each step only adds ids; never
/// removes. This keeps the walker simple and deterministic even with overlapping selections.
/// </summary>
public class ContentPackageDependencyResolver : IContentPackageDependencyResolver
{
    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly ILogger<ContentPackageDependencyResolver> _logger;

    public ContentPackageDependencyResolver(
        IDbContextFactory<DirectorDbContext> dbFactory,
        ILogger<ContentPackageDependencyResolver> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<ContentPackageResolvedSet> ResolveAsync(ContentPackageSelection selection, CancellationToken ct = default)
    {
        var result = new ContentPackageResolvedSet();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Step 1: resolve selection GUIDs to int PKs. Skip anything missing from the source DB
        // — the caller's selection may contain stale ids if the UI raced a deletion.
        var selectedLocationIds = selection.LocationContentPackageIds.Count == 0
            ? new List<int>()
            : await db.TblLocations.AsNoTracking()
                .Where(l => selection.LocationContentPackageIds.Contains(l.ContentPackageId))
                .Select(l => l.LocationId)
                .ToListAsync(ct);

        var selectedAssetIds = selection.AssetContentPackageIds.Count == 0
            ? new List<int>()
            : await db.TblAssets.AsNoTracking()
                .Where(a => selection.AssetContentPackageIds.Contains(a.ContentPackageId))
                .Select(a => a.AssetId)
                .ToListAsync(ct);

        var selectedDisplayIds = selection.DisplayContentPackageIds.Count == 0
            ? new List<int>()
            : await db.TblDisplays.AsNoTracking()
                .Where(d => selection.DisplayContentPackageIds.Contains(d.ContentPackageId))
                .Select(d => d.DisplayId)
                .ToListAsync(ct);

        foreach (var id in selectedLocationIds) result.LocationIds.Add(id);
        foreach (var id in selectedAssetIds) result.AssetIds.Add(id);
        foreach (var id in selectedDisplayIds) result.DisplayIds.Add(id);

        // Step 2: expand full locations to all child records.
        if (result.LocationIds.Count > 0)
        {
            var locIds = result.LocationIds.ToList();

            var childDisplayIds = await db.TblDisplays.AsNoTracking()
                .Where(d => locIds.Contains(d.LocationId))
                .Select(d => d.DisplayId)
                .ToListAsync(ct);
            foreach (var id in childDisplayIds) result.DisplayIds.Add(id);

            var childAssetIds = await db.TblAssets.AsNoTracking()
                .Where(a => locIds.Contains(a.LocationId) && !a.IsDeleted)
                .Select(a => a.AssetId)
                .ToListAsync(ct);
            foreach (var id in childAssetIds) result.AssetIds.Add(id);

            var floorPlanIds = await db.TblFloorPlanElements.AsNoTracking()
                .Where(fp => locIds.Contains(fp.LocationId))
                .Select(fp => fp.FloorPlanElementId)
                .ToListAsync(ct);
            foreach (var id in floorPlanIds) result.FloorPlanElementIds.Add(id);

            // Location-scoped constraints (OwningLocationId matches). Global constraints
            // are intentionally excluded — they're environment-wide and managed separately.
            var locConstraintIds = await db.TblConstraints.AsNoTracking()
                .Where(c => c.OwningLocationId != null && locIds.Contains(c.OwningLocationId.Value))
                .Select(c => c.ConstraintId)
                .ToListAsync(ct);
            foreach (var id in locConstraintIds) result.ConstraintIds.Add(id);
        }

        // Step 3: selected displays pull in their schedule events.
        if (result.DisplayIds.Count > 0)
        {
            var dispIds = result.DisplayIds.ToList();
            var eventIds = await db.TblScheduleEvents.AsNoTracking()
                .Where(se => dispIds.Contains(se.DisplayId))
                .Select(se => se.ScheduleEventId)
                .ToListAsync(ct);
            foreach (var id in eventIds) result.ScheduleEventIds.Add(id);
        }

        // Step 4: selected assets pull in their content metadata.
        if (result.AssetIds.Count > 0)
        {
            var assetIds = result.AssetIds.ToList();
            var metaIds = await db.TblAssetContentMetadata.AsNoTracking()
                .Where(m => assetIds.Contains(m.AssetId))
                .Select(m => m.AssetContentMetadataId)
                .ToListAsync(ct);
            foreach (var id in metaIds) result.AssetContentMetadataIds.Add(id);
        }

        // Step 5: backfill parent references. For every display/asset/constraint in scope,
        // include the owning Location so the target can resolve the FK. Locations' parent
        // LocationGroup and child records' parent groups are also backfilled.
        if (result.DisplayIds.Count > 0 || result.AssetIds.Count > 0)
        {
            var dispIds = result.DisplayIds.ToList();
            var assetIds = result.AssetIds.ToList();

            var extraLocIds = await db.TblDisplays.AsNoTracking()
                .Where(d => dispIds.Contains(d.DisplayId))
                .Select(d => d.LocationId)
                .Union(db.TblAssets.AsNoTracking()
                    .Where(a => assetIds.Contains(a.AssetId))
                    .Select(a => a.LocationId))
                .Distinct()
                .ToListAsync(ct);
            foreach (var id in extraLocIds) result.LocationIds.Add(id);

            var dispConstraintIds = await db.TblDisplays.AsNoTracking()
                .Where(d => dispIds.Contains(d.DisplayId))
                .Select(d => d.ConstraintId)
                .ToListAsync(ct);
            foreach (var id in dispConstraintIds) result.ConstraintIds.Add(id);

            var assetConstraintIds = await db.TblAssets.AsNoTracking()
                .Where(a => assetIds.Contains(a.AssetId))
                .Select(a => a.ConstraintId)
                .ToListAsync(ct);
            foreach (var id in assetConstraintIds) result.ConstraintIds.Add(id);

            var dispGroupIds = await db.TblDisplays.AsNoTracking()
                .Where(d => dispIds.Contains(d.DisplayId))
                .Select(d => d.DisplayGroupId)
                .Distinct()
                .ToListAsync(ct);
            foreach (var id in dispGroupIds) result.DisplayGroupIds.Add(id);

            var assetGroupIds = await db.TblAssets.AsNoTracking()
                .Where(a => assetIds.Contains(a.AssetId))
                .Select(a => a.AssetGroupId)
                .Distinct()
                .ToListAsync(ct);
            foreach (var id in assetGroupIds) result.AssetGroupIds.Add(id);
        }

        if (result.LocationIds.Count > 0)
        {
            var locIds = result.LocationIds.ToList();
            var locGroupIds = await db.TblLocations.AsNoTracking()
                .Where(l => locIds.Contains(l.LocationId))
                .Select(l => l.LocationGroupId)
                .Distinct()
                .ToListAsync(ct);
            foreach (var id in locGroupIds) result.LocationGroupIds.Add(id);

            // Every location record carries its targeting switch and match mode, so its rule rows travel with
            // it, including a location backfilled only as a parent. The switch without its rules would arrive
            // meaning something different.
            var ruleIds = await db.TblLocationMetadataRules.AsNoTracking()
                .Where(r => locIds.Contains(r.LocationId))
                .Select(r => r.LocationMetadataRuleId)
                .ToListAsync(ct);
            foreach (var id in ruleIds) result.LocationMetadataRuleIds.Add(id);
        }

        // Step 6: optional access-control layer.
        if (selection.IncludeAccessControl && result.LocationIds.Count > 0)
        {
            var locIds = result.LocationIds.ToList();

            var ruleIds = await db.TblLocationAccessRules.AsNoTracking()
                .Where(r => locIds.Contains(r.LocationId))
                .Select(r => r.LocationAccessRuleId)
                .ToListAsync(ct);
            foreach (var id in ruleIds) result.LocationAccessRuleIds.Add(id);

            if (result.LocationAccessRuleIds.Count > 0)
            {
                var ruleIdList = result.LocationAccessRuleIds.ToList();
                var scheduleIds = await db.TblAccessSchedules.AsNoTracking()
                    .Where(s => ruleIdList.Contains(s.LocationAccessRuleId))
                    .Select(s => s.AccessScheduleId)
                    .ToListAsync(ct);
                foreach (var id in scheduleIds) result.AccessScheduleIds.Add(id);
            }
        }

        if ((selection.IncludeApprovalChains || selection.IncludeAccessControl) && result.LocationIds.Count > 0)
        {
            var locIds = result.LocationIds.ToList();
            var chainIds = await db.TblApprovalChains.AsNoTracking()
                .Where(c => c.LocationId != null && locIds.Contains(c.LocationId.Value))
                .Select(c => c.ApprovalChainId)
                .ToListAsync(ct);
            foreach (var id in chainIds) result.ApprovalChainIds.Add(id);
        }

        _logger.LogInformation(
            "Content package dependency resolution: {Total} records across {Locations} locations, {Displays} displays, {Assets} assets",
            result.TotalRecordCount, result.LocationIds.Count, result.DisplayIds.Count, result.AssetIds.Count);

        return result;
    }
}
