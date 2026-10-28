using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DXSuite.Director.Entities;
using DXSuite.Shared.Services.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DXSuite.Director.Services.ContentPackage;

/// <summary>
/// Reads a content package bundle ZIP, stages it in a temp directory, and applies the
/// contained records to the local Director database as an upsert-by-ContentPackageId.
///
/// Apply order is fixed to respect FK dependencies:
///   LocationGroups → Locations → DisplayGroups → AssetGroups → Constraints →
///   Assets → Displays → AssetContentMetadata → ScheduleEvents → FloorPlanElements →
///   LocationAccessRules → AccessSchedules → ApprovalChains
///
/// Staging is a singleton <see cref="ConcurrentDictionary{TKey,TValue}"/> on this
/// service so the caller (controller) can Preview and then Apply across two HTTP
/// calls. The importer itself is scoped so DbContext lifetime matches the request.
/// </summary>
public class ContentPackageBundleImporter : IContentPackageBundleImporter
{
    // Static staging registry survives across scoped instances. Using the service
    // type as a static lock is safe because bundles are keyed by Guid and never
    // shared between concurrent operations on the same key.
    private static readonly ConcurrentDictionary<Guid, StagedBundle> Staging = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly IContentPackageUserResolver _userResolver;
    private readonly DXSuite.Shared.Multitenancy.ITenantMediaPathResolver _mediaPaths;
    private readonly ILogger<ContentPackageBundleImporter> _logger;
    private readonly IPublicationInvalidator _publicationInvalidator;
    private readonly ILocationMetadataRuleService _metadataRules;

    public ContentPackageBundleImporter(
        IDbContextFactory<DirectorDbContext> dbFactory,
        IContentPackageUserResolver userResolver,
        DXSuite.Shared.Multitenancy.ITenantMediaPathResolver mediaPaths,
        ILogger<ContentPackageBundleImporter> logger,
        IPublicationInvalidator publicationInvalidator,
        ILocationMetadataRuleService metadataRules)
    {
        _publicationInvalidator = publicationInvalidator;
        _metadataRules = metadataRules;
        _dbFactory = dbFactory;
        _userResolver = userResolver;
        _mediaPaths = mediaPaths;
        _logger = logger;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Preview
    // ────────────────────────────────────────────────────────────────────────

    public async Task<ContentPackageImportPreview> CreatePreviewAsync(
        Stream bundleStream,
        string? originalFileName,
        CancellationToken ct = default)
    {
        var stagingId = Guid.NewGuid();
        var stagingRoot = Path.Combine(Path.GetTempPath(), "DXSuite", "ContentPackageStaging", stagingId.ToString());
        Directory.CreateDirectory(stagingRoot);

        var bundlePath = Path.Combine(stagingRoot, "bundle.zip");
        await using (var fileStream = File.Create(bundlePath))
        {
            await bundleStream.CopyToAsync(fileStream, ct);
        }

        ContentPackageManifest manifest;
        List<string> upgradeWarnings;
        try
        {
            (manifest, upgradeWarnings) = ReadManifest(bundlePath);
        }
        catch (Exception ex)
        {
            TryDeleteStaging(stagingRoot);
            throw new InvalidOperationException("Bundle is not a valid content package ZIP: " + ex.Message, ex);
        }

        Staging[stagingId] = new StagedBundle
        {
            BundlePath = bundlePath,
            StagingRoot = stagingRoot,
            OriginalFileName = originalFileName,
            CreatedAt = DateTime.UtcNow
        };

        var preview = new ContentPackageImportPreview
        {
            StagingId = stagingId,
            BundleFileName = originalFileName,
            SchemaVersion = manifest.SchemaVersion,
            ExportedAt = manifest.ExportedAt,
            ExportedByUser = manifest.ExportedByUser,
            SourceEnvironment = manifest.SourceEnvironment,
            ScopeDescription = manifest.ScopeDescription
        };

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await ComputeDiffs(db, manifest, preview, ct);
        preview.Warnings.AddRange(upgradeWarnings);
        await AddPlaylistDisplayWarningsAsync(db, manifest, preview.Warnings, ct);
        await AddTargetingDeviceWarningsAsync(db, manifest, preview.Warnings, ct);

        return preview;
    }

    private async Task ComputeDiffs(DirectorDbContext db, ContentPackageManifest manifest, ContentPackageImportPreview preview, CancellationToken ct)
    {
        preview.EntityDiffs.Add(await Diff("LocationGroup", manifest.LocationGroups.Select(r => r.ContentPackageId), db.TblLocationGroups, ct));
        preview.EntityDiffs.Add(await Diff("Location", manifest.Locations.Select(r => r.ContentPackageId), db.TblLocations, ct));
        preview.EntityDiffs.Add(await DiffLocationMetadataRules(db, manifest, ct));
        preview.EntityDiffs.Add(await Diff("DisplayGroup", manifest.DisplayGroups.Select(r => r.ContentPackageId), db.TblDisplayGroups, ct));
        preview.EntityDiffs.Add(await Diff("AssetGroup", manifest.AssetGroups.Select(r => r.ContentPackageId), db.TblAssetGroups, ct));
        preview.EntityDiffs.Add(await Diff("Constraint", manifest.Constraints.Select(r => r.ContentPackageId), db.TblConstraints, ct));
        preview.EntityDiffs.Add(await Diff("Asset", manifest.Assets.Select(r => r.ContentPackageId), db.TblAssets, ct));
        preview.EntityDiffs.Add(await Diff("Display", manifest.Displays.Select(r => r.ContentPackageId), db.TblDisplays, ct));
        preview.EntityDiffs.Add(await Diff("AssetContentMetadata", manifest.AssetContentMetadata.Select(r => r.ContentPackageId), db.TblAssetContentMetadata, ct));
        preview.EntityDiffs.Add(await Diff("ScheduleEvent", manifest.ScheduleEvents.Select(r => r.ContentPackageId), db.TblScheduleEvents, ct));
        preview.EntityDiffs.Add(await Diff("FloorPlanElement", manifest.FloorPlanElements.Select(r => r.ContentPackageId), db.TblFloorPlanElements, ct));
        preview.EntityDiffs.Add(await Diff("LocationAccessRule", manifest.LocationAccessRules.Select(r => r.ContentPackageId), db.TblLocationAccessRules, ct));
        preview.EntityDiffs.Add(await Diff("AccessSchedule", manifest.AccessSchedules.Select(r => r.ContentPackageId), db.TblAccessSchedules, ct));
        preview.EntityDiffs.Add(await Diff("ApprovalChain", manifest.ApprovalChains.Select(r => r.ContentPackageId), db.TblApprovalChains, ct));
    }

    private static async Task<ContentPackageEntityDiff> Diff<T>(
        string entityType,
        IEnumerable<Guid> incomingIds,
        IQueryable<T> targetSet,
        CancellationToken ct) where T : class
    {
        var idList = incomingIds.ToList();
        var diff = new ContentPackageEntityDiff { EntityType = entityType };
        if (idList.Count == 0) return diff;

        // EF can't reflect "ContentPackageId" on T without either a shared interface or
        // expression trees per call. We use a raw parameterized query path via
        // DbContext.Set<T>() plus dynamic LINQ-style filtering through EF.Property.
        var existing = await targetSet
            .Where(e => idList.Contains(EF.Property<Guid>(e, "ContentPackageId")))
            .Select(e => EF.Property<Guid>(e, "ContentPackageId"))
            .ToListAsync(ct);

        var existingSet = existing.ToHashSet();
        diff.UpdateCount = existingSet.Count;
        diff.InsertCount = idList.Count - existingSet.Count;
        return diff;
    }

    // Rules are counted the way apply matches them, by location and key name, and a key the target registry
    // does not define is counted as the skip apply makes of it.
    private static async Task<ContentPackageEntityDiff> DiffLocationMetadataRules(
        DirectorDbContext db, ContentPackageManifest manifest, CancellationToken ct)
    {
        var diff = new ContentPackageEntityDiff { EntityType = "LocationMetadataRule" };
        if (manifest.LocationMetadataRules.Count == 0) return diff;

        var registryKeys = (await db.TblMetadataDefinitions.AsNoTracking().Select(d => d.MetadataKey).ToListAsync(ct))
            .Select(MetadataEvaluator.NormalizeKey)
            .ToHashSet(MetadataEvaluator.KeyComparer);

        var locationIds = manifest.LocationMetadataRules.Select(r => r.LocationContentPackageId).Distinct().ToList();
        var targetLocations = await db.TblLocations.AsNoTracking()
            .Where(l => locationIds.Contains(l.ContentPackageId))
            .Select(l => new { l.ContentPackageId, l.LocationId })
            .ToDictionaryAsync(l => l.ContentPackageId, l => l.LocationId, ct);
        var targetLocationIds = targetLocations.Values.ToList();
        var existingKeys = (await db.TblLocationMetadataRules.AsNoTracking()
                .Where(r => targetLocationIds.Contains(r.LocationId))
                .Select(r => new { r.LocationId, r.MetadataKey })
                .ToListAsync(ct))
            .ToLookup(r => r.LocationId, r => MetadataEvaluator.NormalizeKey(r.MetadataKey));

        foreach (var rule in manifest.LocationMetadataRules)
        {
            var key = MetadataEvaluator.NormalizeKey(rule.MetadataKey);
            if (!registryKeys.Contains(key))
                diff.SkipCount++;
            else if (targetLocations.TryGetValue(rule.LocationContentPackageId, out var locationId)
                     && existingKeys[locationId].Contains(key, MetadataEvaluator.KeyComparer))
                diff.UpdateCount++;
            else
                diff.InsertCount++;
        }
        return diff;
    }

    public bool CancelPreview(Guid stagingId)
    {
        if (!Staging.TryRemove(stagingId, out var staged)) return false;
        TryDeleteStaging(staged.StagingRoot);
        return true;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Apply
    // ────────────────────────────────────────────────────────────────────────

    public async Task<ContentPackageImportResult> ApplyAsync(Guid stagingId, string? initiatedByUser, bool replaceMode = false, CancellationToken ct = default)
    {
        if (!Staging.TryGetValue(stagingId, out var staged))
        {
            throw new InvalidOperationException($"No staged bundle found for stagingId {stagingId}. The preview may have expired.");
        }

        var (manifest, upgradeWarnings) = ReadManifest(staged.BundlePath);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Refused before the transaction opens, so a refused package writes nothing, not even its history.
        var playlistConflicts = await FindPlaylistDisplayConflictsAsync(db, manifest, replaceMode, ct);
        if (playlistConflicts.Count > 0)
            throw new InvalidOperationException(
                "The package was not imported. " + string.Join(" ", playlistConflicts.Select(c => c.Message)));

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Create the history header up-front so every per-record decision can be
        // linked. Marked InProgress until we know the final status.
        var history = new TblContentPackageHistory
        {
            Direction = "import",
            Status = "InProgress",
            BundleFileName = staged.OriginalFileName,
            InitiatedByUser = initiatedByUser,
            ExportedByUser = manifest.ExportedByUser,
            SourceEnvironment = manifest.SourceEnvironment,
            ScopeDescription = manifest.ScopeDescription,
            StartedAt = DateTime.UtcNow
        };
        db.TblContentPackageHistories.Add(history);
        await db.SaveChangesAsync(ct);

        var result = new ContentPackageImportResult { ContentPackageHistoryId = history.ContentPackageHistoryId };
        result.Warnings.AddRange(upgradeWarnings);

        // Maps from manifest ContentPackageId → target int PK, populated as we upsert.
        // Dependents (e.g. Display) look up FK targets here first, then fall back
        // to a DB lookup by ContentPackageId for records already in target but not in bundle.
        var locationGroupMap = new Dictionary<Guid, int>();
        var locationMap = new Dictionary<Guid, int>();
        var displayGroupMap = new Dictionary<Guid, int>();
        var assetGroupMap = new Dictionary<Guid, int>();
        var constraintMap = new Dictionary<Guid, int>();
        var assetMap = new Dictionary<Guid, int>();
        var displayMap = new Dictionary<Guid, int>();
        var accessRuleMap = new Dictionary<Guid, int>();

        // Target ids of the location records the package carries. locationMap also gains locations that were
        // only looked up as a foreign key, and rules are neither carried for nor pruned at those.
        var packageLocationIds = new HashSet<int>();

        try
        {
            // ─── LocationGroups ──────────────────────────────────────────────
            foreach (var rec in manifest.LocationGroups)
            {
                ct.ThrowIfCancellationRequested();
                var existing = await db.TblLocationGroups.FirstOrDefaultAsync(x => x.ContentPackageId == rec.ContentPackageId, ct);
                if (existing == null)
                {
                    existing = new TblLocationGroup
                    {
                        ContentPackageId = rec.ContentPackageId,
                        LocationGroupName = rec.LocationGroupName,
                        SortOrder = rec.SortOrder,
                        CreateDate = DateTime.UtcNow
                    };
                    db.TblLocationGroups.Add(existing);
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "LocationGroup", "insert", manifest.ExportedByUser);
                    result.Inserted++;
                }
                else
                {
                    existing.LocationGroupName = rec.LocationGroupName;
                    existing.SortOrder = rec.SortOrder;
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "LocationGroup", "update", manifest.ExportedByUser);
                    result.Updated++;
                }
                locationGroupMap[rec.ContentPackageId] = existing.LocationGroupId;
            }

            // ─── Locations ───────────────────────────────────────────────────
            foreach (var rec in manifest.Locations)
            {
                ct.ThrowIfCancellationRequested();
                var groupId = await ResolveFkAsync(locationGroupMap, rec.LocationGroupContentPackageId,
                    g => db.TblLocationGroups.Where(x => x.ContentPackageId == g).Select(x => (int?)x.LocationGroupId).FirstOrDefaultAsync(ct));
                if (groupId == null)
                {
                    result.Warnings.Add($"Location '{rec.LocationName}' skipped: parent LocationGroup {rec.LocationGroupContentPackageId} not found.");
                    LogItem(db, history, rec.ContentPackageId, "Location", "skip", manifest.ExportedByUser, "Missing LocationGroup FK");
                    result.Skipped++;
                    continue;
                }

                var matchMode = ContentPackageImportRules.NormalizeMatchMode(rec.LocationName, rec.MetadataMatchMode, result.Warnings);

                var existing = await db.TblLocations.FirstOrDefaultAsync(x => x.ContentPackageId == rec.ContentPackageId, ct);
                if (existing == null)
                {
                    existing = new TblLocation
                    {
                        ContentPackageId = rec.ContentPackageId,
                        LocationName = rec.LocationName,
                        LocationTypeId = rec.LocationTypeId,
                        LocationGroupId = groupId.Value,
                        LocationImagePath = rec.LocationImagePath,
                        SortOrder = rec.SortOrder,
                        BackgroundImagePath = rec.BackgroundImagePath,
                        DisplayAnchorTop = rec.DisplayAnchorTop,
                        DisplayAnchorLeft = rec.DisplayAnchorLeft,
                        DisplayOffset = rec.DisplayOffset,
                        BackgroundImageHeight = rec.BackgroundImageHeight,
                        BackgroundImageWidth = rec.BackgroundImageWidth,
                        EnableShuffle = rec.EnableShuffle,
                        EnableFade = rec.EnableFade,
                        EnableContentApproval = rec.EnableContentApproval,
                        TargetContentByMetadata = rec.TargetContentByMetadata,
                        MetadataMatchMode = matchMode,
                        ExtendedData = rec.ExtendedData,
                        CreateDate = DateTime.UtcNow
                    };
                    db.TblLocations.Add(existing);
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "Location", "insert", manifest.ExportedByUser);
                    result.Inserted++;
                }
                else
                {
                    existing.LocationName = rec.LocationName;
                    existing.LocationTypeId = rec.LocationTypeId;
                    existing.LocationGroupId = groupId.Value;
                    existing.LocationImagePath = rec.LocationImagePath;
                    existing.SortOrder = rec.SortOrder;
                    existing.BackgroundImagePath = rec.BackgroundImagePath;
                    existing.DisplayAnchorTop = rec.DisplayAnchorTop;
                    existing.DisplayAnchorLeft = rec.DisplayAnchorLeft;
                    existing.DisplayOffset = rec.DisplayOffset;
                    existing.BackgroundImageHeight = rec.BackgroundImageHeight;
                    existing.BackgroundImageWidth = rec.BackgroundImageWidth;
                    existing.EnableShuffle = rec.EnableShuffle;
                    existing.EnableFade = rec.EnableFade;
                    existing.EnableContentApproval = rec.EnableContentApproval;
                    existing.TargetContentByMetadata = rec.TargetContentByMetadata;
                    existing.MetadataMatchMode = matchMode;
                    existing.ExtendedData = rec.ExtendedData;
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "Location", "update", manifest.ExportedByUser);
                    result.Updated++;
                }
                locationMap[rec.ContentPackageId] = existing.LocationId;
                packageLocationIds.Add(existing.LocationId);
            }

            // ─── LocationMetadataRules ───────────────────────────────────────
            // Matched on (LocationId, MetadataKey), not ContentPackageId: two environments that each declared a
            // key at a location hold different ids for one fact, and a ContentPackageId insert would then break
            // the unique index and fail the whole import. A matched row keeps its own id, since adopting the
            // incoming one could collide with a different row. A key the target registry does not define is
            // skipped, because its rule would participate in targeting while the rule builder shows it nowhere.
            var registryKeys = (await db.TblMetadataDefinitions.AsNoTracking().Select(d => d.MetadataKey).ToListAsync(ct))
                .GroupBy(MetadataEvaluator.NormalizeKey, MetadataEvaluator.KeyComparer)
                .ToDictionary(g => g.Key, g => g.First(), MetadataEvaluator.KeyComparer);
            foreach (var rec in manifest.LocationMetadataRules)
            {
                ct.ThrowIfCancellationRequested();
                var locationName = manifest.Locations.FirstOrDefault(l => l.ContentPackageId == rec.LocationContentPackageId)?.LocationName;
                var locId = locationMap.TryGetValue(rec.LocationContentPackageId, out var mappedLocationId) ? mappedLocationId : (int?)null;
                if (locId == null || !packageLocationIds.Contains(locId.Value))
                {
                    LogItem(db, history, rec.ContentPackageId, "LocationMetadataRule", "skip", manifest.ExportedByUser, "Location not imported");
                    result.Skipped++;
                    continue;
                }

                var key = MetadataEvaluator.NormalizeKey(rec.MetadataKey);
                if (!registryKeys.TryGetValue(key, out var registryKey))
                {
                    result.Warnings.Add($"Location '{locationName}': metadata rule for '{key}' skipped because this environment's metadata registry does not define the key.");
                    LogItem(db, history, rec.ContentPackageId, "LocationMetadataRule", "skip", manifest.ExportedByUser, "Key not in registry");
                    result.Skipped++;
                    continue;
                }

                var (contentPolicy, playerPolicy) = ContentPackageImportRules.NormalizePolicies(
                    locationName, registryKey, rec.UntaggedContentPolicy, rec.UntaggedPlayerPolicy, result.Warnings);

                var existing = await db.TblLocationMetadataRules
                    .FirstOrDefaultAsync(x => x.LocationId == locId.Value && x.MetadataKey == registryKey, ct);
                if (existing == null)
                {
                    var idTaken = await db.TblLocationMetadataRules.AnyAsync(x => x.ContentPackageId == rec.ContentPackageId, ct);
                    existing = new TblLocationMetadataRule
                    {
                        ContentPackageId = idTaken || rec.ContentPackageId == Guid.Empty ? Guid.NewGuid() : rec.ContentPackageId,
                        LocationId = locId.Value,
                        MetadataKey = registryKey,
                        UntaggedContentPolicy = contentPolicy,
                        UntaggedPlayerPolicy = playerPolicy,
                        CreateDate = DateTime.UtcNow
                    };
                    db.TblLocationMetadataRules.Add(existing);
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "LocationMetadataRule", "insert", manifest.ExportedByUser);
                    result.Inserted++;
                }
                else
                {
                    existing.UntaggedContentPolicy = contentPolicy;
                    existing.UntaggedPlayerPolicy = playerPolicy;
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "LocationMetadataRule", "update", manifest.ExportedByUser);
                    result.Updated++;
                }
            }

            // ─── DisplayGroups ───────────────────────────────────────────────
            foreach (var rec in manifest.DisplayGroups)
            {
                var existing = await db.TblDisplayGroups.FirstOrDefaultAsync(x => x.ContentPackageId == rec.ContentPackageId, ct);
                if (existing == null)
                {
                    existing = new TblDisplayGroup
                    {
                        ContentPackageId = rec.ContentPackageId,
                        DisplayGroupName = rec.DisplayGroupName,
                        SortOrder = rec.SortOrder,
                        CreateDate = DateTime.UtcNow
                    };
                    db.TblDisplayGroups.Add(existing);
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "DisplayGroup", "insert", manifest.ExportedByUser);
                    result.Inserted++;
                }
                else
                {
                    existing.DisplayGroupName = rec.DisplayGroupName;
                    existing.SortOrder = rec.SortOrder;
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "DisplayGroup", "update", manifest.ExportedByUser);
                    result.Updated++;
                }
                displayGroupMap[rec.ContentPackageId] = existing.DisplayGroupId;
            }

            // ─── AssetGroups ─────────────────────────────────────────────────
            foreach (var rec in manifest.AssetGroups)
            {
                var existing = await db.TblAssetGroups.FirstOrDefaultAsync(x => x.ContentPackageId == rec.ContentPackageId, ct);
                if (existing == null)
                {
                    existing = new TblAssetGroup
                    {
                        ContentPackageId = rec.ContentPackageId,
                        AssetGroupName = rec.AssetGroupName,
                        CreateDate = DateTime.UtcNow
                    };
                    db.TblAssetGroups.Add(existing);
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "AssetGroup", "insert", manifest.ExportedByUser);
                    result.Inserted++;
                }
                else
                {
                    existing.AssetGroupName = rec.AssetGroupName;
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "AssetGroup", "update", manifest.ExportedByUser);
                    result.Updated++;
                }
                assetGroupMap[rec.ContentPackageId] = existing.AssetGroupId;
            }

            // ─── Constraints ─────────────────────────────────────────────────
            foreach (var rec in manifest.Constraints)
            {
                int? owningLocationId = null;
                if (rec.OwningLocationContentPackageId.HasValue)
                {
                    owningLocationId = await ResolveFkAsync(locationMap, rec.OwningLocationContentPackageId.Value,
                        g => db.TblLocations.Where(x => x.ContentPackageId == g).Select(x => (int?)x.LocationId).FirstOrDefaultAsync(ct));
                }

                var existing = await db.TblConstraints.FirstOrDefaultAsync(x => x.ContentPackageId == rec.ContentPackageId, ct);
                if (existing == null)
                {
                    existing = new TblConstraint
                    {
                        ContentPackageId = rec.ContentPackageId,
                        ConstraintName = rec.ConstraintName,
                        OwningLocationId = owningLocationId,
                        IsGlobal = rec.IsGlobal,
                        ResolutionWidth = rec.ResolutionWidth,
                        ResolutionHeight = rec.ResolutionHeight,
                        ResolutionMode = rec.ResolutionMode,
                        ExtendedData = rec.ExtendedData,
                        CreateDate = DateTime.UtcNow
                    };
                    db.TblConstraints.Add(existing);
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "Constraint", "insert", manifest.ExportedByUser);
                    result.Inserted++;
                }
                else
                {
                    existing.ConstraintName = rec.ConstraintName;
                    existing.OwningLocationId = owningLocationId;
                    existing.IsGlobal = rec.IsGlobal;
                    existing.ResolutionWidth = rec.ResolutionWidth;
                    existing.ResolutionHeight = rec.ResolutionHeight;
                    existing.ResolutionMode = rec.ResolutionMode;
                    existing.ExtendedData = rec.ExtendedData;
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "Constraint", "update", manifest.ExportedByUser);
                    result.Updated++;
                }
                constraintMap[rec.ContentPackageId] = existing.ConstraintId;
            }

            // ─── Assets ──────────────────────────────────────────────────────
            // Assets reference Location + AssetGroup + Constraint. Media files are
            // copied after the DB commit so a failed insert doesn't leave orphaned files.
            var mediaCopyActions = new List<Func<Task>>();
            var unresolvedOwners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var unresolvedOwnerGroups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var rec in manifest.Assets)
            {
                var locId = await ResolveFkAsync(locationMap, rec.LocationContentPackageId,
                    g => db.TblLocations.Where(x => x.ContentPackageId == g).Select(x => (int?)x.LocationId).FirstOrDefaultAsync(ct));
                var groupId = await ResolveFkAsync(assetGroupMap, rec.AssetGroupContentPackageId,
                    g => db.TblAssetGroups.Where(x => x.ContentPackageId == g).Select(x => (int?)x.AssetGroupId).FirstOrDefaultAsync(ct));
                var constraintId = await ResolveFkAsync(constraintMap, rec.ConstraintContentPackageId,
                    g => db.TblConstraints.Where(x => x.ContentPackageId == g).Select(x => (int?)x.ConstraintId).FirstOrDefaultAsync(ct));

                if (locId == null || groupId == null || constraintId == null)
                {
                    result.Warnings.Add($"Asset '{rec.AssetName}' skipped: missing parent Location/AssetGroup/Constraint.");
                    LogItem(db, history, rec.ContentPackageId, "Asset", "skip", manifest.ExportedByUser, "Missing parent FK");
                    result.Skipped++;
                    continue;
                }

                // Rewrite AssetPath to target LocationId. Preserves the filename so
                // the media file lookup in the ZIP still works off the ContentPackageId key.
                var newAssetPath = RewriteAssetPath(rec.AssetPath, locId.Value);

                var ownership = ContentPackageImportRules.ResolveOwnership(
                    rec.UploadedByUserPrincipalName,
                    await _userResolver.ResolveAsync(db, rec.UploadedByUserPrincipalName, fallbackToSystemUser: false, ct),
                    rec.UploadedByUserGroupName,
                    await _userResolver.ResolveGroupAsync(db, rec.UploadedByUserGroupName, ct));
                if (ownership.UserUnresolved)
                    unresolvedOwners[rec.UploadedByUserPrincipalName!] = unresolvedOwners.GetValueOrDefault(rec.UploadedByUserPrincipalName!) + 1;
                if (ownership.GroupUnresolved)
                    unresolvedOwnerGroups[rec.UploadedByUserGroupName!] = unresolvedOwnerGroups.GetValueOrDefault(rec.UploadedByUserGroupName!) + 1;

                var existing = await db.TblAssets.FirstOrDefaultAsync(x => x.ContentPackageId == rec.ContentPackageId, ct);
                if (existing == null)
                {
                    existing = new TblAsset
                    {
                        ContentPackageId = rec.ContentPackageId,
                        LocationId = locId.Value,
                        AssetName = rec.AssetName,
                        AssetPath = newAssetPath,
                        AssetInkPath = rec.AssetInkPath,
                        AssetGroupId = groupId.Value,
                        AssetTypeId = rec.AssetTypeId,
                        AssetHeight = rec.AssetHeight,
                        AssetWidth = rec.AssetWidth,
                        ConstraintId = constraintId.Value,
                        Duration = rec.Duration,
                        VideoMaskPath = rec.VideoMaskPath,
                        EndpointId = rec.EndpointId,
                        EndpointPath = rec.EndpointPath,
                        WebRenderMode = rec.WebRenderMode,
                        EnableHealthMonitoring = rec.EnableHealthMonitoring,
                        ContentSourceUrl = rec.ContentSourceUrl,
                        ExtendedData = rec.ExtendedData,
                        SortOrder = rec.SortOrder,
                        UploadedByUserId = ownership.UserId,
                        UploadedByUserGroupId = ownership.GroupId,
                        CreateDate = DateTime.UtcNow,
                        IsDeleted = false
                    };
                    db.TblAssets.Add(existing);
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "Asset", "insert", manifest.ExportedByUser);
                    result.Inserted++;
                }
                else
                {
                    existing.LocationId = locId.Value;
                    existing.AssetName = rec.AssetName;
                    existing.AssetPath = newAssetPath;
                    existing.AssetInkPath = rec.AssetInkPath;
                    existing.AssetGroupId = groupId.Value;
                    existing.AssetTypeId = rec.AssetTypeId;
                    existing.AssetHeight = rec.AssetHeight;
                    existing.AssetWidth = rec.AssetWidth;
                    existing.ConstraintId = constraintId.Value;
                    existing.Duration = rec.Duration;
                    existing.VideoMaskPath = rec.VideoMaskPath;
                    existing.EndpointId = rec.EndpointId;
                    existing.EndpointPath = rec.EndpointPath;
                    existing.WebRenderMode = rec.WebRenderMode;
                    existing.EnableHealthMonitoring = rec.EnableHealthMonitoring;
                    existing.ContentSourceUrl = rec.ContentSourceUrl;
                    existing.ExtendedData = rec.ExtendedData;
                    existing.SortOrder = rec.SortOrder;
                    existing.IsDeleted = false;
                    // A pre-v4 bundle carries no ownership, so it cannot say the target's owner is wrong.
                    if (manifest.SchemaVersion >= 4)
                    {
                        existing.UploadedByUserId = ownership.UserId;
                        existing.UploadedByUserGroupId = ownership.GroupId;
                    }
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "Asset", "update", manifest.ExportedByUser);
                    result.Updated++;
                }
                assetMap[rec.ContentPackageId] = existing.AssetId;

                if (!string.IsNullOrEmpty(rec.MediaFileName))
                {
                    var contentPackageId = rec.ContentPackageId;
                    var fileName = rec.MediaFileName!;
                    var destPath = ResolveDestinationDiskPath(newAssetPath);
                    mediaCopyActions.Add(() => CopyMediaFromBundleAsync(staged.BundlePath, contentPackageId, fileName, destPath, ct));

                    // Copy the .metadata sidecar when present (playlist-style locations).
                    // If the bundle predates sidecar support, generate a minimal one so
                    // Playlist.razor can discover the file via its Location field.
                    if (destPath != null)
                    {
                        var metaDestPath = destPath + ".metadata";
                        var targetLocId = locId.Value;
                        var assetName = rec.AssetName;
                        mediaCopyActions.Add(async () =>
                        {
                            await CopyMediaFromBundleAsync(staged.BundlePath, contentPackageId, fileName + ".metadata", metaDestPath, ct);
                            if (File.Exists(metaDestPath))
                            {
                                PatchMetadataLocationField(metaDestPath, targetLocId);
                            }
                            else
                            {
                                GenerateMinimalMetadata(metaDestPath, targetLocId, assetName);
                            }
                        });
                    }
                }
            }

            foreach (var (name, count) in unresolvedOwners.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                result.Warnings.Add($"Asset owner '{name}' not found in target: {count} asset(s) imported with no owning user.");
            foreach (var (name, count) in unresolvedOwnerGroups.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                result.Warnings.Add($"Asset owning group '{name}' not found in target: {count} asset(s) imported with no owning group.");

            // ─── Displays ────────────────────────────────────────────────────
            foreach (var rec in manifest.Displays)
            {
                var locId = await ResolveFkAsync(locationMap, rec.LocationContentPackageId,
                    g => db.TblLocations.Where(x => x.ContentPackageId == g).Select(x => (int?)x.LocationId).FirstOrDefaultAsync(ct));
                var groupId = await ResolveFkAsync(displayGroupMap, rec.DisplayGroupContentPackageId,
                    g => db.TblDisplayGroups.Where(x => x.ContentPackageId == g).Select(x => (int?)x.DisplayGroupId).FirstOrDefaultAsync(ct));
                var constraintId = await ResolveFkAsync(constraintMap, rec.ConstraintContentPackageId,
                    g => db.TblConstraints.Where(x => x.ContentPackageId == g).Select(x => (int?)x.ConstraintId).FirstOrDefaultAsync(ct));

                if (locId == null || groupId == null || constraintId == null)
                {
                    result.Warnings.Add($"Display '{rec.DisplayName}' skipped: missing parent Location/DisplayGroup/Constraint.");
                    LogItem(db, history, rec.ContentPackageId, "Display", "skip", manifest.ExportedByUser, "Missing parent FK");
                    result.Skipped++;
                    continue;
                }

                int? overrideAssetId = null;
                if (rec.OverrideAssetContentPackageId.HasValue)
                {
                    overrideAssetId = await ResolveFkAsync(assetMap, rec.OverrideAssetContentPackageId.Value,
                        g => db.TblAssets.Where(x => x.ContentPackageId == g).Select(x => (int?)x.AssetId).FirstOrDefaultAsync(ct));
                }

                int? libraryLocId = null;
                if (rec.AssetLibraryLocationContentPackageId.HasValue)
                {
                    libraryLocId = await ResolveFkAsync(locationMap, rec.AssetLibraryLocationContentPackageId.Value,
                        g => db.TblLocations.Where(x => x.ContentPackageId == g).Select(x => (int?)x.LocationId).FirstOrDefaultAsync(ct));
                }

                var existing = await db.TblDisplays.FirstOrDefaultAsync(x => x.ContentPackageId == rec.ContentPackageId, ct);
                if (existing == null)
                {
                    existing = new TblDisplay
                    {
                        ContentPackageId = rec.ContentPackageId,
                        LocationId = locId.Value,
                        DisplayName = rec.DisplayName,
                        DisplayMode = rec.DisplayMode,
                        DisplayTypeId = rec.DisplayTypeId,
                        DisplayGroupId = groupId.Value,
                        DisplayHeight = rec.DisplayHeight,
                        DisplayWidth = rec.DisplayWidth,
                        ConstraintId = constraintId.Value,
                        OverrideAssetId = overrideAssetId,
                        UseClock = rec.UseClock,
                        ClockHeight = rec.ClockHeight,
                        ClockWidth = rec.ClockWidth,
                        UseVideoMask = rec.UseVideoMask,
                        EnableFade = rec.EnableFade,
                        EnableContentApproval = rec.EnableContentApproval,
                        AssetLibraryLocationId = libraryLocId,
                        StatusSelectorMetadataKey = rec.StatusSelectorMetadataKey,
                        EndpointId = rec.EndpointId,
                        SortOrder = rec.SortOrder,
                        ExtendedData = rec.ExtendedData,
                        CreateDate = DateTime.UtcNow
                    };
                    db.TblDisplays.Add(existing);
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "Display", "insert", manifest.ExportedByUser);
                    result.Inserted++;
                }
                else
                {
                    existing.LocationId = locId.Value;
                    existing.DisplayName = rec.DisplayName;
                    existing.DisplayMode = rec.DisplayMode;
                    existing.DisplayTypeId = rec.DisplayTypeId;
                    existing.DisplayGroupId = groupId.Value;
                    existing.DisplayHeight = rec.DisplayHeight;
                    existing.DisplayWidth = rec.DisplayWidth;
                    existing.ConstraintId = constraintId.Value;
                    existing.OverrideAssetId = overrideAssetId;
                    existing.UseClock = rec.UseClock;
                    existing.ClockHeight = rec.ClockHeight;
                    existing.ClockWidth = rec.ClockWidth;
                    existing.UseVideoMask = rec.UseVideoMask;
                    existing.EnableFade = rec.EnableFade;
                    existing.EnableContentApproval = rec.EnableContentApproval;
                    existing.AssetLibraryLocationId = libraryLocId;
                    existing.StatusSelectorMetadataKey = rec.StatusSelectorMetadataKey;
                    existing.EndpointId = rec.EndpointId;
                    existing.SortOrder = rec.SortOrder;
                    existing.ExtendedData = rec.ExtendedData;
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "Display", "update", manifest.ExportedByUser);
                    result.Updated++;
                }
                displayMap[rec.ContentPackageId] = existing.DisplayId;
            }

            // ─── AssetContentMetadata ────────────────────────────────────────
            // Reconciled per asset and key rather than upserted per row, since an asset holds several rows for a
            // multi-value key. For every key the bundle carries on an asset, the target ends up holding exactly
            // the bundle's values; keys the bundle does not carry on that asset are left alone. Rows match on
            // value, so a matched row keeps its own ContentPackageId, and a list key cannot gain a second value.
            var tagGroups = manifest.AssetContentMetadata
                .GroupBy(r => r.AssetContentPackageId)
                .SelectMany(byAsset => byAsset
                    .GroupBy(r => MetadataEvaluator.NormalizeKey(r.MetadataKey), MetadataEvaluator.KeyComparer)
                    .Select(byKey => (AssetContentPackageId: byAsset.Key, Key: byKey.Key, Records: byKey.ToList())))
                .ToList();
            var usedTagIds = new HashSet<Guid>();
            foreach (var (assetContentPackageId, key, records) in tagGroups)
            {
                ct.ThrowIfCancellationRequested();
                var assetId = await ResolveFkAsync(assetMap, assetContentPackageId,
                    g => db.TblAssets.Where(x => x.ContentPackageId == g).Select(x => (int?)x.AssetId).FirstOrDefaultAsync(ct));
                if (assetId == null || key.Length == 0)
                {
                    foreach (var rec in records)
                        LogItem(db, history, rec.ContentPackageId, "AssetContentMetadata", "skip", manifest.ExportedByUser,
                            assetId == null ? "Missing Asset FK" : "Blank key");
                    result.Skipped += records.Count;
                    continue;
                }

                var desired = MetadataValueSet.Normalize(records.Select(r => r.MetadataValue));
                AssetContentMetadataRecord RecordFor(string value) =>
                    records.First(r => MetadataEvaluator.ValueComparer.Equals(r.MetadataValue?.Trim(), value));

                var stored = await db.TblAssetContentMetadata
                    .Where(t => t.AssetId == assetId.Value && t.MetadataKey == key)
                    .OrderBy(t => t.AssetContentMetadataId)
                    .ToListAsync(ct);
                var plan = MetadataValueSet.Reconcile(stored.Select(t => t.MetadataValue).ToList(), desired);

                foreach (var (index, value) in plan.Updates)
                {
                    var rec = RecordFor(value);
                    stored[index].MetadataValue = value;
                    stored[index].SetByUserId = await _userResolver.ResolveAsync(db, rec.SetByUserPrincipalName, fallbackToSystemUser: true, ct);
                }
                foreach (var index in plan.Removes)
                {
                    LogItem(db, history, stored[index].ContentPackageId, "AssetContentMetadata", "delete", manifest.ExportedByUser, "Value not in bundle for this key");
                    db.TblAssetContentMetadata.Remove(stored[index]);
                }
                // Removals and rewrites land before any insert, so an insert can reuse an id a removal freed.
                await db.SaveChangesAsync(ct);

                var added = new HashSet<string>(plan.Adds, MetadataEvaluator.ValueComparer);
                foreach (var value in plan.Adds)
                {
                    var rec = RecordFor(value);
                    var idAvailable = rec.ContentPackageId != Guid.Empty
                        && !usedTagIds.Contains(rec.ContentPackageId)
                        && !await db.TblAssetContentMetadata.AnyAsync(t => t.ContentPackageId == rec.ContentPackageId, ct);
                    var tagId = idAvailable ? rec.ContentPackageId : Guid.NewGuid();
                    usedTagIds.Add(tagId);
                    db.TblAssetContentMetadata.Add(new TblAssetContentMetadata
                    {
                        ContentPackageId = tagId,
                        AssetId = assetId.Value,
                        MetadataKey = key,
                        MetadataValue = value,
                        SetByUserId = await _userResolver.ResolveAsync(db, rec.SetByUserPrincipalName, fallbackToSystemUser: true, ct),
                        CreateDate = DateTime.UtcNow
                    });
                    LogItem(db, history, rec.ContentPackageId, "AssetContentMetadata", "insert", rec.SetByUserPrincipalName);
                    result.Inserted++;
                }
                await db.SaveChangesAsync(ct);

                foreach (var value in desired.Where(v => !added.Contains(v)))
                {
                    var rec = RecordFor(value);
                    LogItem(db, history, rec.ContentPackageId, "AssetContentMetadata", "update", rec.SetByUserPrincipalName);
                    result.Updated++;
                }
            }

            // ─── ScheduleEvents ──────────────────────────────────────────────
            foreach (var rec in manifest.ScheduleEvents)
            {
                var displayId = await ResolveFkAsync(displayMap, rec.DisplayContentPackageId,
                    g => db.TblDisplays.Where(x => x.ContentPackageId == g).Select(x => (int?)x.DisplayId).FirstOrDefaultAsync(ct));
                if (displayId == null)
                {
                    LogItem(db, history, rec.ContentPackageId, "ScheduleEvent", "skip", manifest.ExportedByUser, "Missing Display FK");
                    result.Skipped++;
                    continue;
                }

                int? locationId = null;
                if (rec.LocationContentPackageId.HasValue)
                {
                    locationId = await ResolveFkAsync(locationMap, rec.LocationContentPackageId.Value,
                        g => db.TblLocations.Where(x => x.ContentPackageId == g).Select(x => (int?)x.LocationId).FirstOrDefaultAsync(ct));
                }

                // Video-wall allocation fields. UserGroup matches by name; the default asset and override
                // constraint resolve by ContentPackageId (target row with the same GUID, in-package or not).
                var userGroupId = await _userResolver.ResolveGroupAsync(db, rec.UserGroupName, ct);

                int? defaultAssetId = null;
                if (rec.DefaultAssetContentPackageId.HasValue)
                {
                    defaultAssetId = await ResolveFkAsync(assetMap, rec.DefaultAssetContentPackageId.Value,
                        g => db.TblAssets.Where(a => a.ContentPackageId == g).Select(a => (int?)a.AssetId).FirstOrDefaultAsync(ct));
                    if (defaultAssetId == null)
                        result.Warnings.Add($"Schedule event default asset not found in target (ContentPackageId {rec.DefaultAssetContentPackageId}); counter default left unset.");
                }

                int? overrideConstraintId = null;
                if (rec.OverrideConstraintContentPackageId.HasValue)
                {
                    overrideConstraintId = await ResolveFkAsync(constraintMap, rec.OverrideConstraintContentPackageId.Value,
                        g => db.TblConstraints.Where(c => c.ContentPackageId == g).Select(c => (int?)c.ConstraintId).FirstOrDefaultAsync(ct));
                }

                // Resolve the asset tokens inside ContentAssignment, then translate the JSON back.
                var assetGuidToId = new Dictionary<Guid, int>();
                foreach (var g in ContentAssignmentRemapper.ExtractAssetGuids(rec.ContentAssignment))
                {
                    if (assetGuidToId.ContainsKey(g)) continue;
                    var aid = await ResolveFkAsync(assetMap, g,
                        gg => db.TblAssets.Where(a => a.ContentPackageId == gg).Select(a => (int?)a.AssetId).FirstOrDefaultAsync(ct));
                    if (aid.HasValue) assetGuidToId[g] = aid.Value;
                }
                var contentAssignment = ContentAssignmentRemapper.DetokenizeAssetIds(
                    rec.ContentAssignment, assetGuidToId, out var unresolvedAssets) ?? rec.ContentAssignment;
                if (unresolvedAssets.Count > 0)
                    result.Warnings.Add($"Schedule event content: {unresolvedAssets.Count} asset reference(s) not found in target — those content items were dropped.");

                var existing = await db.TblScheduleEvents.FirstOrDefaultAsync(x => x.ContentPackageId == rec.ContentPackageId, ct);
                if (existing == null)
                {
                    existing = new TblScheduleEvent
                    {
                        ContentPackageId = rec.ContentPackageId,
                        ScheduleEventTypeId = rec.ScheduleEventTypeId,
                        DisplayId = displayId.Value,
                        LocationId = locationId,
                        UserGroupId = userGroupId,
                        DefaultAssetId = defaultAssetId,
                        OverrideConstraintId = overrideConstraintId,
                        AirlineCode = rec.AirlineCode,
                        ContentAssignment = contentAssignment,
                        StartTime = rec.StartTime,
                        EndTime = rec.EndTime,
                        StartDate = rec.StartDate,
                        EndDate = rec.EndDate,
                        DefaultContent = rec.DefaultContent,
                        Sunday = rec.Sunday,
                        Monday = rec.Monday,
                        Tuesday = rec.Tuesday,
                        Wednesday = rec.Wednesday,
                        Thursday = rec.Thursday,
                        Friday = rec.Friday,
                        Saturday = rec.Saturday,
                        CreateDate = DateTime.UtcNow
                    };
                    db.TblScheduleEvents.Add(existing);
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "ScheduleEvent", "insert", manifest.ExportedByUser);
                    result.Inserted++;
                }
                else
                {
                    existing.ScheduleEventTypeId = rec.ScheduleEventTypeId;
                    existing.DisplayId = displayId.Value;
                    existing.LocationId = locationId;
                    existing.UserGroupId = userGroupId;
                    existing.DefaultAssetId = defaultAssetId;
                    existing.OverrideConstraintId = overrideConstraintId;
                    existing.AirlineCode = rec.AirlineCode;
                    existing.ContentAssignment = contentAssignment;
                    existing.StartTime = rec.StartTime;
                    existing.EndTime = rec.EndTime;
                    existing.StartDate = rec.StartDate;
                    existing.EndDate = rec.EndDate;
                    existing.DefaultContent = rec.DefaultContent;
                    existing.Sunday = rec.Sunday;
                    existing.Monday = rec.Monday;
                    existing.Tuesday = rec.Tuesday;
                    existing.Wednesday = rec.Wednesday;
                    existing.Thursday = rec.Thursday;
                    existing.Friday = rec.Friday;
                    existing.Saturday = rec.Saturday;
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "ScheduleEvent", "update", manifest.ExportedByUser);
                    result.Updated++;
                }
            }

            // ─── FloorPlanElements ───────────────────────────────────────────
            foreach (var rec in manifest.FloorPlanElements)
            {
                var locId = await ResolveFkAsync(locationMap, rec.LocationContentPackageId,
                    g => db.TblLocations.Where(x => x.ContentPackageId == g).Select(x => (int?)x.LocationId).FirstOrDefaultAsync(ct));
                if (locId == null)
                {
                    LogItem(db, history, rec.ContentPackageId, "FloorPlanElement", "skip", manifest.ExportedByUser, "Missing Location FK");
                    result.Skipped++;
                    continue;
                }

                int? childLocId = null;
                if (rec.ChildLocationContentPackageId.HasValue)
                {
                    childLocId = await ResolveFkAsync(locationMap, rec.ChildLocationContentPackageId.Value,
                        g => db.TblLocations.Where(x => x.ContentPackageId == g).Select(x => (int?)x.LocationId).FirstOrDefaultAsync(ct));
                }
                int? childDispId = null;
                if (rec.ChildDisplayContentPackageId.HasValue)
                {
                    childDispId = await ResolveFkAsync(displayMap, rec.ChildDisplayContentPackageId.Value,
                        g => db.TblDisplays.Where(x => x.ContentPackageId == g).Select(x => (int?)x.DisplayId).FirstOrDefaultAsync(ct));
                }

                var existing = await db.TblFloorPlanElements.FirstOrDefaultAsync(x => x.ContentPackageId == rec.ContentPackageId, ct);
                if (existing == null)
                {
                    existing = new TblFloorPlanElement
                    {
                        ContentPackageId = rec.ContentPackageId,
                        LocationId = locId.Value,
                        ChildLocationId = childLocId,
                        ChildDisplayId = childDispId,
                        PositionTop = rec.PositionTop,
                        PositionLeft = rec.PositionLeft,
                        ElementWidth = rec.ElementWidth,
                        ElementHeight = rec.ElementHeight,
                        IconPath = rec.IconPath,
                        IconLabel = rec.IconLabel,
                        ShowLivePreview = rec.ShowLivePreview,
                        Latitude = rec.Latitude,
                        Longitude = rec.Longitude,
                        Position3DX = rec.Position3DX,
                        Position3DY = rec.Position3DY,
                        Position3DZ = rec.Position3DZ,
                        SortOrder = rec.SortOrder,
                        CreateDate = DateTime.UtcNow
                    };
                    db.TblFloorPlanElements.Add(existing);
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "FloorPlanElement", "insert", manifest.ExportedByUser);
                    result.Inserted++;
                }
                else
                {
                    existing.LocationId = locId.Value;
                    existing.ChildLocationId = childLocId;
                    existing.ChildDisplayId = childDispId;
                    existing.PositionTop = rec.PositionTop;
                    existing.PositionLeft = rec.PositionLeft;
                    existing.ElementWidth = rec.ElementWidth;
                    existing.ElementHeight = rec.ElementHeight;
                    existing.IconPath = rec.IconPath;
                    existing.IconLabel = rec.IconLabel;
                    existing.ShowLivePreview = rec.ShowLivePreview;
                    existing.Latitude = rec.Latitude;
                    existing.Longitude = rec.Longitude;
                    existing.Position3DX = rec.Position3DX;
                    existing.Position3DY = rec.Position3DY;
                    existing.Position3DZ = rec.Position3DZ;
                    existing.SortOrder = rec.SortOrder;
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "FloorPlanElement", "update", manifest.ExportedByUser);
                    result.Updated++;
                }
            }

            // ─── LocationAccessRules ─────────────────────────────────────────
            // User-scoped or group-scoped rules are skipped when their principal can't be
            // resolved in the target. A view rule carries its scope as all or none, and every other
            // access level carries none; a pre-v4 bundle's MetadataFilterKey was translated on read.
            foreach (var rec in manifest.LocationAccessRules)
            {
                var locId = await ResolveFkAsync(locationMap, rec.LocationContentPackageId,
                    g => db.TblLocations.Where(x => x.ContentPackageId == g).Select(x => (int?)x.LocationId).FirstOrDefaultAsync(ct));
                if (locId == null)
                {
                    LogItem(db, history, rec.ContentPackageId, "LocationAccessRule", "skip", manifest.ExportedByUser, "Missing Location FK");
                    result.Skipped++;
                    continue;
                }

                var userId = await _userResolver.ResolveAsync(db, rec.UserPrincipalName, fallbackToSystemUser: false, ct);
                var groupId = await _userResolver.ResolveGroupAsync(db, rec.UserGroupName, ct);

                var hasPrincipal = !string.IsNullOrEmpty(rec.UserPrincipalName) || !string.IsNullOrEmpty(rec.UserGroupName);
                if (hasPrincipal && !userId.HasValue && !groupId.HasValue)
                {
                    result.Warnings.Add($"LocationAccessRule skipped: principal '{rec.UserPrincipalName ?? rec.UserGroupName}' not found in target.");
                    LogItem(db, history, rec.ContentPackageId, "LocationAccessRule", "skip", manifest.ExportedByUser, "Principal not found");
                    result.Skipped++;
                    continue;
                }

                var viewScope = ContentPackageImportRules.NormalizeViewScope(rec, result.Warnings);

                var existing = await db.TblLocationAccessRules.FirstOrDefaultAsync(x => x.ContentPackageId == rec.ContentPackageId, ct);
                if (existing == null)
                {
                    existing = new TblLocationAccessRule
                    {
                        ContentPackageId = rec.ContentPackageId,
                        LocationId = locId.Value,
                        UserId = userId,
                        UserGroupId = groupId,
                        AccessLevel = rec.AccessLevel,
                        ViewScope = viewScope,
                        CreateDate = DateTime.UtcNow
                    };
                    db.TblLocationAccessRules.Add(existing);
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "LocationAccessRule", "insert", manifest.ExportedByUser);
                    result.Inserted++;
                }
                else
                {
                    existing.LocationId = locId.Value;
                    existing.UserId = userId;
                    existing.UserGroupId = groupId;
                    existing.AccessLevel = rec.AccessLevel;
                    existing.ViewScope = viewScope;
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "LocationAccessRule", "update", manifest.ExportedByUser);
                    result.Updated++;
                }
                accessRuleMap[rec.ContentPackageId] = existing.LocationAccessRuleId;
            }

            // ─── AccessSchedules ─────────────────────────────────────────────
            foreach (var rec in manifest.AccessSchedules)
            {
                var ruleId = await ResolveFkAsync(accessRuleMap, rec.LocationAccessRuleContentPackageId,
                    g => db.TblLocationAccessRules.Where(x => x.ContentPackageId == g).Select(x => (int?)x.LocationAccessRuleId).FirstOrDefaultAsync(ct));
                if (ruleId == null)
                {
                    LogItem(db, history, rec.ContentPackageId, "AccessSchedule", "skip", manifest.ExportedByUser, "Missing AccessRule FK");
                    result.Skipped++;
                    continue;
                }

                int? dispId = null;
                if (rec.DisplayContentPackageId.HasValue)
                {
                    dispId = await ResolveFkAsync(displayMap, rec.DisplayContentPackageId.Value,
                        g => db.TblDisplays.Where(x => x.ContentPackageId == g).Select(x => (int?)x.DisplayId).FirstOrDefaultAsync(ct));
                }
                int? constrId = null;
                if (rec.ConstraintContentPackageId.HasValue)
                {
                    constrId = await ResolveFkAsync(constraintMap, rec.ConstraintContentPackageId.Value,
                        g => db.TblConstraints.Where(x => x.ContentPackageId == g).Select(x => (int?)x.ConstraintId).FirstOrDefaultAsync(ct));
                }

                var existing = await db.TblAccessSchedules.FirstOrDefaultAsync(x => x.ContentPackageId == rec.ContentPackageId, ct);
                if (existing == null)
                {
                    existing = new TblAccessSchedule
                    {
                        ContentPackageId = rec.ContentPackageId,
                        LocationAccessRuleId = ruleId.Value,
                        DisplayId = dispId,
                        ConstraintId = constrId,
                        StartDate = rec.StartDate,
                        EndDate = rec.EndDate,
                        Sunday = rec.Sunday,
                        Monday = rec.Monday,
                        Tuesday = rec.Tuesday,
                        Wednesday = rec.Wednesday,
                        Thursday = rec.Thursday,
                        Friday = rec.Friday,
                        Saturday = rec.Saturday,
                        MetadataKey = rec.MetadataKey,
                        CreateDate = DateTime.UtcNow
                    };
                    db.TblAccessSchedules.Add(existing);
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "AccessSchedule", "insert", manifest.ExportedByUser);
                    result.Inserted++;
                }
                else
                {
                    existing.LocationAccessRuleId = ruleId.Value;
                    existing.DisplayId = dispId;
                    existing.ConstraintId = constrId;
                    existing.StartDate = rec.StartDate;
                    existing.EndDate = rec.EndDate;
                    existing.Sunday = rec.Sunday;
                    existing.Monday = rec.Monday;
                    existing.Tuesday = rec.Tuesday;
                    existing.Wednesday = rec.Wednesday;
                    existing.Thursday = rec.Thursday;
                    existing.Friday = rec.Friday;
                    existing.Saturday = rec.Saturday;
                    existing.MetadataKey = rec.MetadataKey;
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "AccessSchedule", "update", manifest.ExportedByUser);
                    result.Updated++;
                }
            }

            // ─── ApprovalChains ──────────────────────────────────────────────
            foreach (var rec in manifest.ApprovalChains)
            {
                var locId = await ResolveFkAsync(locationMap, rec.LocationContentPackageId,
                    g => db.TblLocations.Where(x => x.ContentPackageId == g).Select(x => (int?)x.LocationId).FirstOrDefaultAsync(ct));
                if (locId == null)
                {
                    LogItem(db, history, rec.ContentPackageId, "ApprovalChain", "skip", manifest.ExportedByUser, "Missing Location FK");
                    result.Skipped++;
                    continue;
                }

                int? ruleId = null;
                if (rec.LocationAccessRuleContentPackageId.HasValue)
                {
                    ruleId = await ResolveFkAsync(accessRuleMap, rec.LocationAccessRuleContentPackageId.Value,
                        g => db.TblLocationAccessRules.Where(x => x.ContentPackageId == g).Select(x => (int?)x.LocationAccessRuleId).FirstOrDefaultAsync(ct));
                }

                var existing = await db.TblApprovalChains.FirstOrDefaultAsync(x => x.ContentPackageId == rec.ContentPackageId, ct);
                if (existing == null)
                {
                    existing = new TblApprovalChain
                    {
                        ContentPackageId = rec.ContentPackageId,
                        LocationId = locId.Value,
                        IsEnabled = rec.IsEnabled,
                        IsDefault = rec.IsDefault,
                        LocationAccessRuleId = ruleId,
                        AutoApproveMembers = rec.AutoApproveMembers,
                        EnableLlmAutoApproval = rec.EnableLlmAutoApproval,
                        LlmAutoApprovalUpToStageOrder = rec.LlmAutoApprovalUpToStageOrder,
                        DeclinedContentRetentionDays = rec.DeclinedContentRetentionDays,
                        CreateDate = DateTime.UtcNow
                    };
                    db.TblApprovalChains.Add(existing);
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "ApprovalChain", "insert", manifest.ExportedByUser);
                    result.Inserted++;
                }
                else
                {
                    existing.LocationId = locId.Value;
                    existing.IsEnabled = rec.IsEnabled;
                    existing.IsDefault = rec.IsDefault;
                    existing.LocationAccessRuleId = ruleId;
                    existing.AutoApproveMembers = rec.AutoApproveMembers;
                    existing.EnableLlmAutoApproval = rec.EnableLlmAutoApproval;
                    existing.LlmAutoApprovalUpToStageOrder = rec.LlmAutoApprovalUpToStageOrder;
                    existing.DeclinedContentRetentionDays = rec.DeclinedContentRetentionDays;
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "ApprovalChain", "update", manifest.ExportedByUser);
                    result.Updated++;
                }
            }

            // ─── UserGroupLocationDefaults ───────────────────────────────────
            // Per-airline, per-wall default content. No ContentPackageId on the entity, so matched
            // by natural key (target UserGroupId + target LocationId). UserGroup and a global
            // DefaultConstraint are resolved by name (both are environment-wide, not in the package).
            foreach (var rec in manifest.UserGroupLocationDefaults)
            {
                var locId = await ResolveFkAsync(locationMap, rec.LocationContentPackageId,
                    g => db.TblLocations.Where(x => x.ContentPackageId == g).Select(x => (int?)x.LocationId).FirstOrDefaultAsync(ct));
                if (locId == null)
                {
                    LogItem(db, history, rec.ContentPackageId, "UserGroupLocationDefault", "skip", manifest.ExportedByUser, "Missing Location FK");
                    result.Skipped++;
                    continue;
                }

                var groupId = await _userResolver.ResolveGroupAsync(db, rec.UserGroupName, ct);
                if (groupId == null)
                {
                    result.Warnings.Add($"Airline default skipped: user group '{rec.UserGroupName}' not found in target.");
                    LogItem(db, history, rec.ContentPackageId, "UserGroupLocationDefault", "skip", manifest.ExportedByUser, "User group not found");
                    result.Skipped++;
                    continue;
                }

                // Counter-content constraint: prefer an in-package (location-scoped) constraint,
                // otherwise resolve a global constraint by name. A missing constraint is non-fatal —
                // the rest of the airline's default layers still import.
                int? constraintId = null;
                if (rec.DefaultConstraintContentPackageId.HasValue)
                {
                    constraintId = await ResolveFkAsync(constraintMap, rec.DefaultConstraintContentPackageId.Value,
                        g => db.TblConstraints.Where(c => c.ContentPackageId == g).Select(c => (int?)c.ConstraintId).FirstOrDefaultAsync(ct));
                }
                if (constraintId == null && !string.IsNullOrEmpty(rec.DefaultConstraintName))
                {
                    constraintId = await db.TblConstraints
                        .Where(c => c.ConstraintName == rec.DefaultConstraintName)
                        .Select(c => (int?)c.ConstraintId)
                        .FirstOrDefaultAsync(ct);
                    if (constraintId == null)
                        result.Warnings.Add($"Airline default for group '{rec.UserGroupName}': counter-content constraint '{rec.DefaultConstraintName}' not found in target — library not set.");
                }

                // Bundle records target the default dayparting state; added states are untouched.
                var existing = await db.TblUserGroupLocationDefaults
                    .Include(x => x.Logos)
                    .Include(x => x.LayerContents)
                    .Where(x => x.UserGroupId == groupId.Value && x.LocationId == locId.Value)
                    .OrderByDescending(x => x.IsDefaultState)
                    .FirstOrDefaultAsync(ct);
                if (existing == null)
                {
                    existing = new TblUserGroupLocationDefault
                    {
                        UserGroupId = groupId.Value,
                        LocationId = locId.Value,
                        DefaultConstraintId = constraintId,
                        BackgroundPath = rec.BackgroundPath,
                        BackgroundMode = string.IsNullOrEmpty(rec.BackgroundMode) ? "fill-width" : rec.BackgroundMode,
                        BackgroundOpacityPct = Math.Clamp(rec.BackgroundOpacityPct, 0, 100),
                        MaskPath = rec.MaskPath,
                        MaskMode = string.IsNullOrEmpty(rec.MaskMode) ? "fill-width" : rec.MaskMode,
                        MaskOpacityPct = Math.Clamp(rec.MaskOpacityPct, 0, 100),
                        LogoPath = rec.LogoPath,
                        CreateDate = DateTime.UtcNow,
                        LastEditDate = DateTime.UtcNow
                    };
                    db.TblUserGroupLocationDefaults.Add(existing);
                    ReconcileRoleLogos(existing, rec, manifest.SchemaVersion);
                    ReconcileLayerSegments(existing, rec, manifest.SchemaVersion);
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "UserGroupLocationDefault", "insert", manifest.ExportedByUser);
                    result.Inserted++;
                }
                else
                {
                    existing.DefaultConstraintId = constraintId;
                    existing.BackgroundPath = rec.BackgroundPath;
                    existing.BackgroundMode = string.IsNullOrEmpty(rec.BackgroundMode) ? "fill-width" : rec.BackgroundMode;
                    existing.BackgroundOpacityPct = Math.Clamp(rec.BackgroundOpacityPct, 0, 100);
                    existing.MaskPath = rec.MaskPath;
                    existing.MaskMode = string.IsNullOrEmpty(rec.MaskMode) ? "fill-width" : rec.MaskMode;
                    existing.MaskOpacityPct = Math.Clamp(rec.MaskOpacityPct, 0, 100);
                    existing.LogoPath = rec.LogoPath;
                    existing.LastEditDate = DateTime.UtcNow;
                    ReconcileRoleLogos(existing, rec, manifest.SchemaVersion);
                    ReconcileLayerSegments(existing, rec, manifest.SchemaVersion);
                    await db.SaveChangesAsync(ct);
                    LogItem(db, history, rec.ContentPackageId, "UserGroupLocationDefault", "update", manifest.ExportedByUser);
                    result.Updated++;
                }
            }

            // ─── VideoWall ExtendedData display-id remap ─────────────────────
            // Wall layouts carry source display PKs (tokenized to ContentPackageId GUIDs at export).
            // Now that every display has a target PK, translate the tokens back so the wall's groups
            // and placements reference the displays that were just imported. Runs last so displayMap
            // is fully populated. Reads rec.ExtendedData (the tokenized form) so it is idempotent on
            // re-import even though the Locations pass already wrote the tokenized blob to the row.
            foreach (var rec in manifest.Locations)
            {
                if (string.IsNullOrEmpty(rec.ExtendedData)) continue;
                if (!locationMap.TryGetValue(rec.ContentPackageId, out var locId)) continue;

                var remapped = WallLayoutIdRemapper.DetokenizeDisplayIds(rec.ExtendedData, displayMap, out var unresolved);
                if (remapped == null) continue; // not a tokenized wall layout

                var loc = await db.TblLocations.FirstOrDefaultAsync(x => x.LocationId == locId, ct);
                if (loc == null) continue;
                loc.ExtendedData = remapped;
                await db.SaveChangesAsync(ct);

                if (unresolved.Count > 0)
                    result.Warnings.Add($"Location '{rec.LocationName}': {unresolved.Count} display reference(s) in its video-wall layout could not be remapped (those displays were not in the package).");
            }

            // Replace mode: for each location in the bundle, delete any direct-child
            // records whose ContentPackageId is not in the bundle. Scoped strictly to child
            // entities of the promoted locations so unrelated data is never touched.
            // Runs inside the same transaction so replace is all-or-nothing with upserts.
            if (replaceMode && manifest.Locations.Count > 0)
            {
                var targetLocationIds = locationMap.Values.ToList();
                if (targetLocationIds.Count > 0)
                {
                    var keepDisplays = new HashSet<Guid>(manifest.Displays.Select(d => d.ContentPackageId));
                    var keepAssets = new HashSet<Guid>(manifest.Assets.Select(a => a.ContentPackageId));
                    var keepScheduleEvents = new HashSet<Guid>(manifest.ScheduleEvents.Select(s => s.ContentPackageId));
                    var keepConstraints = new HashSet<Guid>(manifest.Constraints.Select(c => c.ContentPackageId));
                    var keepFloorPlanElements = new HashSet<Guid>(manifest.FloorPlanElements.Select(f => f.ContentPackageId));

                    var deletedCount = 0;

                    // Identify orphan parents first — their IDs drive child cleanup.
                    var orphanAssets = await db.TblAssets
                        .Where(a => targetLocationIds.Contains(a.LocationId) && !keepAssets.Contains(a.ContentPackageId))
                        .ToListAsync(ct);
                    var orphanAssetIds = orphanAssets.Select(a => a.AssetId).ToHashSet();

                    var orphanDisplays = await db.TblDisplays
                        .Where(d => targetLocationIds.Contains(d.LocationId) && !keepDisplays.Contains(d.ContentPackageId))
                        .ToListAsync(ct);
                    var orphanDisplayIds = orphanDisplays.Select(d => d.DisplayId).ToHashSet();

                    // ── Phase 1: Delete/nullify FK children of orphan assets ──

                    // Content metadata rows (FK_ContentMetadata_Asset, NO_ACTION)
                    var orphanContentMeta = await db.TblAssetContentMetadata
                        .Where(m => orphanAssetIds.Contains(m.AssetId))
                        .ToListAsync(ct);
                    db.TblAssetContentMetadata.RemoveRange(orphanContentMeta);

                    // Floor plan icon schedules (FK_FloorPlanIconSchedule_Asset, NO_ACTION)
                    var orphanIconSchedules = await db.TblFloorPlanIconSchedules
                        .Where(s => s.IconAssetId.HasValue && orphanAssetIds.Contains(s.IconAssetId.Value))
                        .ToListAsync(ct);
                    db.TblFloorPlanIconSchedules.RemoveRange(orphanIconSchedules);

                    // Displays referencing orphan assets via OverrideAssetId (FK_tblDisplay_tblAsset, NO_ACTION)
                    var displaysWithOrphanOverride = await db.TblDisplays
                        .Where(d => d.OverrideAssetId.HasValue && orphanAssetIds.Contains(d.OverrideAssetId.Value))
                        .ToListAsync(ct);
                    foreach (var d in displaysWithOrphanOverride) d.OverrideAssetId = null;

                    // Event log asset references (FK_tblEventLog_tblAsset, NO_ACTION)
                    var logsWithOrphanAsset = await db.TblEventLogs
                        .Where(e => e.AssetId.HasValue && orphanAssetIds.Contains(e.AssetId.Value))
                        .ToListAsync(ct);
                    foreach (var e in logsWithOrphanAsset) e.AssetId = null;

                    // ── Phase 2: Delete/nullify FK children of orphan displays ──

                    // Schedule events (FK_tblScheduleEvent_tblDisplay, NO_ACTION)
                    var orphanScheduleEvents = await db.TblScheduleEvents
                        .Where(s => orphanDisplayIds.Contains(s.DisplayId))
                        .ToListAsync(ct);
                    foreach (var s in orphanScheduleEvents)
                    {
                        LogItem(db, history, s.ContentPackageId, "ScheduleEvent", "delete", manifest.ExportedByUser, "Replace mode");
                        deletedCount++;
                    }
                    db.TblScheduleEvents.RemoveRange(orphanScheduleEvents);

                    // Also remove orphan schedule events scoped by location but not tied to an orphan display
                    var orphanScheduleEventsByLocation = await db.TblScheduleEvents
                        .Where(s => s.Display != null && targetLocationIds.Contains(s.Display.LocationId)
                                    && !keepScheduleEvents.Contains(s.ContentPackageId)
                                    && !orphanDisplayIds.Contains(s.DisplayId))
                        .ToListAsync(ct);
                    foreach (var s in orphanScheduleEventsByLocation)
                    {
                        LogItem(db, history, s.ContentPackageId, "ScheduleEvent", "delete", manifest.ExportedByUser, "Replace mode");
                        deletedCount++;
                    }
                    db.TblScheduleEvents.RemoveRange(orphanScheduleEventsByLocation);

                    // Access schedules (FK_AccessSchedule_Display, NO_ACTION)
                    var orphanAccessSchedules = await db.TblAccessSchedules
                        .Where(a => a.DisplayId.HasValue && orphanDisplayIds.Contains(a.DisplayId.Value))
                        .ToListAsync(ct);
                    db.TblAccessSchedules.RemoveRange(orphanAccessSchedules);

                    // Devices assigned to orphan displays (FK_tblDevice_tblDisplay, NO_ACTION)
                    var devicesOnOrphanDisplays = await db.TblDevices
                        .Where(d => d.DisplayId.HasValue && orphanDisplayIds.Contains(d.DisplayId.Value))
                        .ToListAsync(ct);
                    foreach (var d in devicesOnOrphanDisplays) d.DisplayId = null;

                    // Floor plan elements referencing orphan displays (FK_FloorPlanElement_ChildDisplay, NO_ACTION)
                    var fpWithOrphanChild = await db.TblFloorPlanElements
                        .Where(f => f.ChildDisplayId.HasValue && orphanDisplayIds.Contains(f.ChildDisplayId.Value))
                        .ToListAsync(ct);
                    foreach (var f in fpWithOrphanChild) f.ChildDisplayId = null;

                    // Event log display references (FK_tblEventLog_tblDisplay, NO_ACTION)
                    var logsWithOrphanDisplay = await db.TblEventLogs
                        .Where(e => e.DisplayId.HasValue && orphanDisplayIds.Contains(e.DisplayId.Value))
                        .ToListAsync(ct);
                    foreach (var e in logsWithOrphanDisplay) e.DisplayId = null;

                    // ── Phase 3: Delete orphan parents and remaining location-scoped entities ──

                    // Collect disk paths before removing from the DB — we clean up the
                    // physical media files (and .metadata sidecars) after SaveChanges.
                    var orphanMediaPaths = new List<string>();
                    foreach (var a in orphanAssets)
                    {
                        LogItem(db, history, a.ContentPackageId, "Asset", "delete", manifest.ExportedByUser, "Replace mode");
                        deletedCount++;

                        var diskPath = ResolveDestinationDiskPath(a.AssetPath);
                        if (!string.IsNullOrEmpty(diskPath))
                        {
                            orphanMediaPaths.Add(diskPath);
                            // .metadata sidecar used by playlist-style locations
                            var metaPath = diskPath + ".metadata";
                            if (File.Exists(metaPath))
                                orphanMediaPaths.Add(metaPath);
                        }
                    }
                    db.TblAssets.RemoveRange(orphanAssets);

                    foreach (var d in orphanDisplays)
                    {
                        LogItem(db, history, d.ContentPackageId, "Display", "delete", manifest.ExportedByUser, "Replace mode");
                        deletedCount++;
                    }
                    db.TblDisplays.RemoveRange(orphanDisplays);

                    // Location-scoped constraints only — global constraints are never touched
                    var orphanConstraints = await db.TblConstraints
                        .Where(c => c.OwningLocationId.HasValue && targetLocationIds.Contains(c.OwningLocationId.Value)
                                    && !keepConstraints.Contains(c.ContentPackageId))
                        .ToListAsync(ct);
                    foreach (var c in orphanConstraints)
                    {
                        LogItem(db, history, c.ContentPackageId, "Constraint", "delete", manifest.ExportedByUser, "Replace mode");
                        deletedCount++;
                    }
                    db.TblConstraints.RemoveRange(orphanConstraints);

                    // Floor plan elements
                    var orphanFloorPlanElements = await db.TblFloorPlanElements
                        .Where(f => targetLocationIds.Contains(f.LocationId) && !keepFloorPlanElements.Contains(f.ContentPackageId))
                        .ToListAsync(ct);
                    foreach (var f in orphanFloorPlanElements)
                    {
                        LogItem(db, history, f.ContentPackageId, "FloorPlanElement", "delete", manifest.ExportedByUser, "Replace mode");
                        deletedCount++;
                    }
                    db.TblFloorPlanElements.RemoveRange(orphanFloorPlanElements);

                    // Rule rows at the carried locations that the bundle does not name. A stale row is a key
                    // still targeting content, so it cannot be left behind. The keep-set is the natural key the
                    // upsert matches on, and a key the bundle names but skipped as unregistered is still kept.
                    // Removing a rule leaves every asset tag in place, so adding the key back restores it.
                    var keepMetadataRules = new Dictionary<int, HashSet<string>>();
                    foreach (var r in manifest.LocationMetadataRules)
                    {
                        if (!locationMap.TryGetValue(r.LocationContentPackageId, out var ruleLocationId)) continue;
                        if (!keepMetadataRules.TryGetValue(ruleLocationId, out var keys))
                            keepMetadataRules[ruleLocationId] = keys = new HashSet<string>(MetadataEvaluator.KeyComparer);
                        keys.Add(MetadataEvaluator.NormalizeKey(r.MetadataKey));
                    }
                    var carriedLocationIds = packageLocationIds.ToList();
                    var orphanMetadataRules = (await db.TblLocationMetadataRules
                            .Where(r => carriedLocationIds.Contains(r.LocationId))
                            .ToListAsync(ct))
                        .Where(r => !(keepMetadataRules.TryGetValue(r.LocationId, out var keys)
                                      && keys.Contains(MetadataEvaluator.NormalizeKey(r.MetadataKey))))
                        .ToList();
                    foreach (var r in orphanMetadataRules)
                    {
                        LogItem(db, history, r.ContentPackageId, "LocationMetadataRule", "delete", manifest.ExportedByUser, "Replace mode");
                        deletedCount++;
                    }
                    db.TblLocationMetadataRules.RemoveRange(orphanMetadataRules);

                    // Asset groups are global (no LocationId) — not pruned by replace mode.

                    if (deletedCount > 0)
                    {
                        await db.SaveChangesAsync(ct);
                        result.Warnings.Add($"Replace mode: deleted {deletedCount} orphan child record(s) not present in bundle.");

                        // Clean up physical media files for deleted assets
                        foreach (var path in orphanMediaPaths)
                        {
                            try { if (File.Exists(path)) File.Delete(path); }
                            catch (Exception ex) { _logger.LogWarning(ex, "Replace mode: failed to delete orphan media file {Path}", path); }
                        }
                    }
                }
            }

            // The pre-check assumed every carried location and display is written, but a record whose parent does
            // not resolve is skipped, which can leave a display where the package moved it from. So the
            // invariant is checked again against what was actually written, and a breach rolls everything back.
            var touchedDisplayIds = displayMap.Values.Distinct().ToList();
            var touchedLocationIds = packageLocationIds.ToList();
            var displaysUnderPlaylists = await (
                    from d in db.TblDisplays.AsNoTracking()
                    join l in db.TblLocations.AsNoTracking() on d.LocationId equals l.LocationId
                    where l.LocationTypeId == ContentPackageImportRules.PlaylistLocationTypeId
                          && (touchedLocationIds.Contains(l.LocationId) || touchedDisplayIds.Contains(d.DisplayId))
                    select new { l.LocationName, d.DisplayName })
                .ToListAsync(ct);
            if (displaysUnderPlaylists.Count > 0)
                throw new InvalidOperationException("The package was not imported. " + string.Join(" ", displaysUnderPlaylists
                    .GroupBy(d => d.LocationName)
                    .Select(g => new PlaylistDisplayConflict(g.Key, g.Select(d => d.DisplayName).Order(StringComparer.OrdinalIgnoreCase).ToList()).Message)));

            // Finalize history header and commit DB work.
            history.TotalRecords = result.Inserted + result.Updated + result.Skipped;
            history.InsertedCount = result.Inserted;
            history.UpdatedCount = result.Updated;
            history.SkippedCount = result.Skipped;
            history.CompletedAt = DateTime.UtcNow;
            history.Status = result.Skipped > 0 ? "PartialSuccess" : "Success";
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            result.Status = history.Status;

            // Media copy AFTER DB commit. A copy failure here logs but doesn't roll
            // back the DB — media files can be re-copied by re-importing the bundle.
            foreach (var copyAction in mediaCopyActions)
            {
                try
                {
                    await copyAction();
                    result.MediaFilesCopied++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Content package media copy failed");
                    result.Warnings.Add($"Media copy failed: {ex.Message}");
                }
            }

            // An import writes locations, assets, tags, displays, schedule events and layout remaps in one
            // transaction, so every location and display it touched is invalidated once, after the media lands.
            var assetLocationIds = new List<int>();
            foreach (var chunk in assetMap.Values.Distinct().Chunk(1000))
                assetLocationIds.AddRange(await db.TblAssets.AsNoTracking()
                    .Where(a => chunk.Contains(a.AssetId))
                    .Select(a => a.LocationId)
                    .Distinct()
                    .ToListAsync(ct));
            _publicationInvalidator.AssetsChanged(locationMap.Values.Concat(assetLocationIds));
            foreach (var displayId in displayMap.Values.Distinct())
                _publicationInvalidator.DisplayChanged(displayId);
            foreach (var locationId in packageLocationIds)
                _metadataRules.InvalidateLocation(locationId);

            // Clean up staging only on success so failures can be re-inspected.
            Staging.TryRemove(stagingId, out _);
            TryDeleteStaging(staged.StagingRoot);

            return result;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            history.Status = "Failed";
            history.CompletedAt = DateTime.UtcNow;
            history.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            try
            {
                // Reopen and write the failure row. The original transaction rolled it back.
                await using var db2 = await _dbFactory.CreateDbContextAsync(ct);
                db2.TblContentPackageHistories.Add(new TblContentPackageHistory
                {
                    Direction = "import",
                    Status = "Failed",
                    BundleFileName = staged.OriginalFileName,
                    InitiatedByUser = initiatedByUser,
                    ExportedByUser = manifest.ExportedByUser,
                    SourceEnvironment = manifest.SourceEnvironment,
                    ScopeDescription = manifest.ScopeDescription,
                    StartedAt = history.StartedAt,
                    CompletedAt = DateTime.UtcNow,
                    ErrorMessage = history.ErrorMessage
                });
                await db2.SaveChangesAsync(ct);
            }
            catch (Exception historyEx)
            {
                _logger.LogError(historyEx, "Failed to write content package history failure row");
            }
            throw;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Syncs the airline's per-role logo rows to the bundle record. Only v3+ bundles carry
    /// per-role logos, so older bundles leave the target's rows untouched rather than
    /// wiping them with an always-empty list.
    /// </summary>
    private static void ReconcileRoleLogos(TblUserGroupLocationDefault target,
        UserGroupLocationDefaultRecord rec, int schemaVersion)
    {
        if (schemaVersion < 3) return;

        var desired = rec.Logos
            .Where(l => !string.IsNullOrWhiteSpace(l.LogoRoleKey) && !string.IsNullOrWhiteSpace(l.ContentPath))
            .ToDictionary(l => l.LogoRoleKey);

        foreach (var row in target.Logos.Where(r => !desired.ContainsKey(r.LogoRoleKey)).ToList())
            target.Logos.Remove(row);

        foreach (var (roleKey, content) in desired)
        {
            var row = target.Logos.FirstOrDefault(r => r.LogoRoleKey == roleKey);
            if (row == null)
            {
                target.Logos.Add(new TblUserGroupLocationLogo
                {
                    LogoRoleKey = roleKey,
                    ContentPath = content.ContentPath,
                    ContentMode = string.IsNullOrEmpty(content.ContentMode) ? "fit" : content.ContentMode,
                    CreateDate = DateTime.UtcNow,
                    LastEditDate = DateTime.UtcNow,
                });
            }
            else
            {
                row.ContentPath = content.ContentPath;
                row.ContentMode = string.IsNullOrEmpty(content.ContentMode) ? "fit" : content.ContentMode;
                row.LastEditDate = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// Syncs the airline's side-by-side layer segments to the bundle record. v3+ only,
    /// for the same reason as ReconcileRoleLogos.
    /// </summary>
    private static void ReconcileLayerSegments(TblUserGroupLocationDefault target,
        UserGroupLocationDefaultRecord rec, int schemaVersion)
    {
        if (schemaVersion < 3) return;

        foreach (var row in target.LayerContents.ToList())
            target.LayerContents.Remove(row);

        foreach (var seg in rec.Segments
                     .Where(s => !string.IsNullOrWhiteSpace(s.ContentPath))
                     .OrderBy(s => s.SortOrder))
        {
            target.LayerContents.Add(new TblUserGroupLocationLayerContent
            {
                LayerRole = string.Equals(seg.LayerRole, "mask", StringComparison.OrdinalIgnoreCase) ? "mask" : "background",
                SortOrder = seg.SortOrder,
                ContentPath = seg.ContentPath,
                IntrinsicWidthPx = Math.Max(0, seg.IntrinsicWidthPx),
                ContentMode = string.IsNullOrEmpty(seg.ContentMode) ? "native" : seg.ContentMode,
                CreateDate = DateTime.UtcNow,
                LastEditDate = DateTime.UtcNow,
            });
        }
    }

    /// <summary>Reads the bundle's manifest, upgraded to the current schema, and the warnings the upgrade raised.</summary>
    private static (ContentPackageManifest Manifest, List<string> Warnings) ReadManifest(string bundlePath)
    {
        using var fs = File.OpenRead(bundlePath);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
        var entry = archive.GetEntry("manifest.json")
            ?? throw new InvalidOperationException("Bundle does not contain manifest.json");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var manifest = JsonSerializer.Deserialize<ContentPackageManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException("Bundle manifest.json could not be parsed");
        return (manifest, ContentPackageManifestUpgrade.Apply(manifest));
    }

    /// <summary>
    /// Every Playlist location the package would leave with a display. The package is the only path that can
    /// put a display under a Playlist location or retype a location that has displays, so it is checked here.
    /// </summary>
    private static async Task<IReadOnlyList<PlaylistDisplayConflict>> FindPlaylistDisplayConflictsAsync(
        DirectorDbContext db, ContentPackageManifest manifest, bool replaceMode, CancellationToken ct)
    {
        var packageLocations = manifest.Locations
            .GroupBy(l => l.ContentPackageId)
            .ToDictionary(g => g.Key, g => g.Last());
        var locationIds = packageLocations.Keys
            .Concat(manifest.Displays.Select(d => d.LocationContentPackageId))
            .Where(g => g != Guid.Empty)
            .Distinct()
            .ToList();
        if (locationIds.Count == 0) return [];

        var targetLocations = new Dictionary<Guid, (int LocationId, string? Name, int TypeId)>();
        foreach (var chunk in locationIds.Chunk(1000))
        {
            foreach (var l in await db.TblLocations.AsNoTracking()
                         .Where(l => chunk.Contains(l.ContentPackageId))
                         .Select(l => new { l.ContentPackageId, l.LocationId, l.LocationName, l.LocationTypeId })
                         .ToListAsync(ct))
                targetLocations[l.ContentPackageId] = (l.LocationId, l.LocationName, l.LocationTypeId);
        }

        var states = locationIds
            .Select(id => packageLocations.TryGetValue(id, out var rec)
                ? new PackageLocationState(id, rec.LocationName, rec.LocationTypeId, InPackage: true)
                : targetLocations.TryGetValue(id, out var t)
                    ? new PackageLocationState(id, t.Name, t.TypeId, InPackage: false)
                    : null)
            .OfType<PackageLocationState>()
            .ToList();

        var playlistTargets = states
            .Where(st => st.FinalTypeId == ContentPackageImportRules.PlaylistLocationTypeId && targetLocations.ContainsKey(st.ContentPackageId))
            .ToDictionary(st => targetLocations[st.ContentPackageId].LocationId, st => st.ContentPackageId);
        var playlistTargetIds = playlistTargets.Keys.ToList();
        var targetDisplays = playlistTargetIds.Count == 0
            ? new List<PackageDisplayState>()
            : (await db.TblDisplays.AsNoTracking()
                    .Where(d => playlistTargetIds.Contains(d.LocationId))
                    .Select(d => new { d.ContentPackageId, d.DisplayName, d.LocationId })
                    .ToListAsync(ct))
                .Select(d => new PackageDisplayState(d.ContentPackageId, d.DisplayName, playlistTargets[d.LocationId]))
                .ToList();

        return ContentPackageImportRules.PlaylistDisplayConflicts(
            states,
            targetDisplays,
            manifest.Displays.Select(d => new PackageDisplayState(d.ContentPackageId, d.DisplayName, d.LocationContentPackageId)),
            replaceMode);
    }

    private static async Task AddPlaylistDisplayWarningsAsync(
        DirectorDbContext db, ContentPackageManifest manifest, List<string> warnings, CancellationToken ct)
    {
        var inMerge = await FindPlaylistDisplayConflictsAsync(db, manifest, replaceMode: false, ct);
        if (inMerge.Count == 0) return;

        var inReplace = await FindPlaylistDisplayConflictsAsync(db, manifest, replaceMode: true, ct);
        if (inReplace.Count > 0)
            warnings.AddRange(inReplace.Select(c => $"{c.Message} This package cannot be imported."));
        else
            warnings.AddRange(inMerge.Select(c => $"{c.Message} Only replace mode can import this package, because it removes displays the package does not list."));
    }

    /// <summary>
    /// Names the keys no player at a targeting location holds a value for. Devices and their metadata never
    /// travel in a package, so a location promoted with targeting on arrives with its players untagged, and a
    /// key whose untagged player policy is none then leaves every player there with nothing to play.
    /// </summary>
    private static async Task AddTargetingDeviceWarningsAsync(
        DirectorDbContext db, ContentPackageManifest manifest, List<string> warnings, CancellationToken ct)
    {
        var targeting = manifest.Locations.Where(l => l.TargetContentByMetadata).ToList();
        if (targeting.Count == 0) return;

        // A key the target registry does not define is skipped on import, so it never needs a player value.
        var registryKeys = (await db.TblMetadataDefinitions.AsNoTracking().Select(d => d.MetadataKey).ToListAsync(ct))
            .Select(MetadataEvaluator.NormalizeKey)
            .ToHashSet(MetadataEvaluator.KeyComparer);

        foreach (var location in targeting)
        {
            var keys = manifest.LocationMetadataRules
                .Where(r => r.LocationContentPackageId == location.ContentPackageId)
                .Select(r => MetadataEvaluator.NormalizeKey(r.MetadataKey))
                .Where(registryKeys.Contains)
                .ToList();
            if (keys.Count == 0) continue;

            var rows = await (
                    from m in db.TblDeviceMetadata.AsNoTracking()
                    join d in db.TblDevices.AsNoTracking() on m.DeviceId equals d.DeviceId
                    join l in db.TblLocations.AsNoTracking() on d.LocationId equals l.LocationId
                    where l.ContentPackageId == location.ContentPackageId
                    select new { m.DeviceId, m.MetadataKey, m.MetadataValue })
                .ToListAsync(ct);
            var deviceValues = rows
                .GroupBy(r => r.DeviceId, StringComparer.OrdinalIgnoreCase)
                .Select(g => MetadataSubjectResolver.GroupValues(g.Select(r => (r.MetadataKey, r.MetadataValue))));

            var missing = ContentPackageImportRules.KeysNoPlayerHolds(keys, deviceValues);
            if (missing.Count > 0)
                warnings.Add($"Location '{location.LocationName}' targets content by metadata, but no player at it in this environment has a value for {string.Join(", ", missing)}. Players are not part of a package, so tag them here before relying on targeting.");
        }
    }

    /// <summary>
    /// Looks up the target int PK for a given manifest ContentPackageId, first checking
    /// the in-bundle map (populated as records upsert) and falling back to a DB
    /// query against pre-existing rows. Returns null when the referenced record
    /// is not present in either location.
    /// </summary>
    private static async Task<int?> ResolveFkAsync(
        Dictionary<Guid, int> map,
        Guid contentPackageId,
        Func<Guid, Task<int?>> dbLookup)
    {
        if (contentPackageId == Guid.Empty) return null;
        if (map.TryGetValue(contentPackageId, out var id)) return id;
        var dbId = await dbLookup(contentPackageId);
        if (dbId.HasValue) map[contentPackageId] = dbId.Value;
        return dbId;
    }

    private static string RewriteAssetPath(string sourcePath, int targetLocationId)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return sourcePath;
        if (sourcePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            sourcePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return sourcePath;
        }

        // Source path is of the form /MediaAssets/{srcLocId}/{filename} or
        // /MediaAssets/{srcLocId}/{srcConstraintId}/{filename}. We preserve the
        // subpath after the source location segment so any per-constraint
        // subdirectories remain intact.
        var normalized = sourcePath.Replace('\\', '/').TrimStart('/');
        var parts = normalized.Split('/');
        if (parts.Length < 3 || !parts[0].Equals("MediaAssets", StringComparison.OrdinalIgnoreCase))
        {
            // Unknown layout — copy file into /MediaAssets/{targetLoc}/{basename}
            return $"/MediaAssets/{targetLocationId}/{Path.GetFileName(normalized)}";
        }

        // Replace the location segment; keep the rest.
        parts[1] = targetLocationId.ToString();
        return "/" + string.Join("/", parts);
    }

    private string? ResolveDestinationDiskPath(string webPath)
    {
        if (string.IsNullOrWhiteSpace(webPath)) return null;
        var webRoot = _mediaPaths.GetRoot();
        if (string.IsNullOrEmpty(webRoot)) return null;
        var relative = webPath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(webRoot, relative);
    }

    private static async Task CopyMediaFromBundleAsync(string bundlePath, Guid contentPackageId, string fileName, string? destDiskPath, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(destDiskPath)) return;

        using var fs = File.OpenRead(bundlePath);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
        var entryName = $"media/{contentPackageId}/{fileName}";
        var entry = archive.GetEntry(entryName);
        if (entry == null) return;

        var destDir = Path.GetDirectoryName(destDiskPath);
        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

        // Write to a temp file first, then move into place. This avoids
        // locking the destination while writing (the static file middleware
        // may be serving it to a device concurrently) and prevents devices
        // from receiving a partially-written file.
        var tempPath = destDiskPath + ".promo-tmp";
        try
        {
            await using (var src = entry.Open())
            await using (var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920))
            {
                await src.CopyToAsync(dst, ct);
            }

            // File.Move can transiently fail on Windows when the MediaFolderCacheService's
            // FileSystemWatcher reacts to a previous move in the same directory — the
            // watcher event handler briefly touches the file. Retry a few times.
            const int maxRetries = 5;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(tempPath, destDiskPath, overwrite: true);
                    break;
                }
                catch (IOException) when (attempt < maxRetries)
                {
                    await Task.Delay(200 * attempt, ct);
                }
            }
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* best-effort */ }
            throw;
        }
    }

    /// <summary>
    /// Updates the Location field inside a .metadata XML sidecar to point to the
    /// target locationId. The sidecar format is:
    /// <![CDATA[<usermetadata><field name="Location" ... value="22" />...</usermetadata>]]>
    /// </summary>
    private static void PatchMetadataLocationField(string metadataPath, int targetLocationId)
    {
        if (!File.Exists(metadataPath)) return;
        try
        {
            var doc = new System.Xml.XmlDocument();
            doc.Load(metadataPath);
            var nodes = doc.SelectNodes("//field[@name='Location']");
            if (nodes != null)
            {
                foreach (System.Xml.XmlNode node in nodes)
                    node.Attributes!["value"]!.Value = targetLocationId.ToString();
            }
            doc.Save(metadataPath);
        }
        catch
        {
            // Non-critical — the file plays fine without a correct Location field
        }
    }

    /// <summary>
    /// Creates a minimal .metadata sidecar for bundles that predate sidecar support.
    /// Playlist.razor requires the Location field to discover files; FileName is
    /// included so the original name displays correctly in the UI.
    /// </summary>
    private static void GenerateMinimalMetadata(string metadataPath, int locationId, string? assetName)
    {
        try
        {
            var dir = Path.GetDirectoryName(metadataPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using var writer = new StreamWriter(metadataPath, false);
            writer.WriteLine("<usermetadata>");
            writer.WriteLine($"  <field name=\"Location\" datatype=\"ovx_String\" value=\"{locationId}\" />");
            if (!string.IsNullOrEmpty(assetName))
                writer.WriteLine($"  <field name=\"FileName\" datatype=\"ovx_String\" value=\"{System.Security.SecurityElement.Escape(assetName)}\" />");
            writer.WriteLine("</usermetadata>");
        }
        catch
        {
            // Non-critical — file will still play, just won't appear in Playlist.razor
        }
    }

    private static void LogItem(
        DirectorDbContext db,
        TblContentPackageHistory history,
        Guid contentPackageId,
        string entityType,
        string action,
        string? sourceUser,
        string? notes = null)
    {
        db.TblContentPackageHistoryItems.Add(new TblContentPackageHistoryItem
        {
            ContentPackageHistoryId = history.ContentPackageHistoryId,
            ContentPackageId = contentPackageId,
            EntityType = entityType,
            Action = action,
            SourceUserPrincipalName = sourceUser,
            Notes = notes
        });
    }

    private static void TryDeleteStaging(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch { /* best-effort cleanup */ }
    }

    private class StagedBundle
    {
        public string BundlePath { get; set; } = null!;
        public string StagingRoot { get; set; } = null!;
        public string? OriginalFileName { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
