using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DXSuite.Director.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DXSuite.Director.Services.ContentPackage;

/// <summary>
/// Loads the resolved record set from the database, serializes it to ContentPackageRecord DTOs,
/// and packages media files into a ZIP. Media paths in AssetPath are copied as-is to the
/// manifest — rewriting to target LocationId happens on import, not export.
/// </summary>
public class ContentPackageBundleBuilder : IContentPackageBundleBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IContentPackageDependencyResolver _resolver;
    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly DXSuite.Shared.Multitenancy.ITenantMediaPathResolver _mediaPaths;
    private readonly ILogger<ContentPackageBundleBuilder> _logger;

    public ContentPackageBundleBuilder(
        IContentPackageDependencyResolver resolver,
        IDbContextFactory<DirectorDbContext> dbFactory,
        DXSuite.Shared.Multitenancy.ITenantMediaPathResolver mediaPaths,
        ILogger<ContentPackageBundleBuilder> logger)
    {
        _resolver = resolver;
        _dbFactory = dbFactory;
        _mediaPaths = mediaPaths;
        _logger = logger;
    }

    public async Task<ContentPackageBundleResult> BuildAsync(
        ContentPackageSelection selection,
        string? exportedByUser,
        Stream output,
        CancellationToken ct = default)
    {
        var resolved = await _resolver.ResolveAsync(selection, ct);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var manifest = new ContentPackageManifest
        {
            ExportedAt = DateTime.UtcNow,
            ExportedByUser = exportedByUser,
            SourceEnvironment = selection.SourceEnvironment,
            ScopeDescription = selection.ScopeDescription
        };

        // Build ContentPackageId lookup maps for cross-references. Every FK from an in-scope
        // record to another in-scope record is rewritten from int → Guid using these maps.
        var locationIds = resolved.LocationIds.ToList();
        var locationMap = await db.TblLocations.AsNoTracking()
            .Where(l => locationIds.Contains(l.LocationId))
            .ToDictionaryAsync(l => l.LocationId, l => l.ContentPackageId, ct);

        var locationGroupIds = resolved.LocationGroupIds.ToList();
        var locationGroupMap = await db.TblLocationGroups.AsNoTracking()
            .Where(g => locationGroupIds.Contains(g.LocationGroupId))
            .ToDictionaryAsync(g => g.LocationGroupId, g => g.ContentPackageId, ct);

        var displayIds = resolved.DisplayIds.ToList();
        var displayMap = await db.TblDisplays.AsNoTracking()
            .Where(d => displayIds.Contains(d.DisplayId))
            .ToDictionaryAsync(d => d.DisplayId, d => d.ContentPackageId, ct);

        var displayGroupIds = resolved.DisplayGroupIds.ToList();
        var displayGroupMap = await db.TblDisplayGroups.AsNoTracking()
            .Where(g => displayGroupIds.Contains(g.DisplayGroupId))
            .ToDictionaryAsync(g => g.DisplayGroupId, g => g.ContentPackageId, ct);

        var assetIds = resolved.AssetIds.ToList();
        var assetMap = await db.TblAssets.AsNoTracking()
            .Where(a => assetIds.Contains(a.AssetId))
            .ToDictionaryAsync(a => a.AssetId, a => a.ContentPackageId, ct);

        var assetGroupIds = resolved.AssetGroupIds.ToList();
        var assetGroupMap = await db.TblAssetGroups.AsNoTracking()
            .Where(g => assetGroupIds.Contains(g.AssetGroupId))
            .ToDictionaryAsync(g => g.AssetGroupId, g => g.ContentPackageId, ct);

        var constraintIds = resolved.ConstraintIds.ToList();
        var constraintMap = await db.TblConstraints.AsNoTracking()
            .Where(c => constraintIds.Contains(c.ConstraintId))
            .ToDictionaryAsync(c => c.ConstraintId, c => c.ContentPackageId, ct);

        var accessRuleIds = resolved.LocationAccessRuleIds.ToList();
        var accessRuleMap = await db.TblLocationAccessRules.AsNoTracking()
            .Where(r => accessRuleIds.Contains(r.LocationAccessRuleId))
            .ToDictionaryAsync(r => r.LocationAccessRuleId, r => r.ContentPackageId, ct);

        // Load + project each entity type into its manifest record.

        manifest.LocationGroups = await db.TblLocationGroups.AsNoTracking()
            .Where(g => locationGroupIds.Contains(g.LocationGroupId))
            .Select(g => new LocationGroupRecord
            {
                ContentPackageId = g.ContentPackageId,
                LocationGroupName = g.LocationGroupName,
                SortOrder = g.SortOrder
            })
            .ToListAsync(ct);

        manifest.Locations = (await db.TblLocations.AsNoTracking()
            .Where(l => locationIds.Contains(l.LocationId))
            .ToListAsync(ct))
            .Select(l => new LocationRecord
            {
                ContentPackageId = l.ContentPackageId,
                LocationName = l.LocationName,
                LocationTypeId = l.LocationTypeId,
                LocationGroupContentPackageId = LookupOrEmpty(locationGroupMap, l.LocationGroupId),
                LocationImagePath = l.LocationImagePath,
                SortOrder = l.SortOrder,
                BackgroundImagePath = l.BackgroundImagePath,
                DisplayAnchorTop = l.DisplayAnchorTop,
                DisplayAnchorLeft = l.DisplayAnchorLeft,
                DisplayOffset = l.DisplayOffset,
                BackgroundImageHeight = l.BackgroundImageHeight,
                BackgroundImageWidth = l.BackgroundImageWidth,
                EnableShuffle = l.EnableShuffle,
                EnableFade = l.EnableFade,
                EnableContentApproval = l.EnableContentApproval,
                TargetContentByMetadata = l.TargetContentByMetadata,
                MetadataMatchMode = l.MetadataMatchMode,
                // VideoWall layouts embed source display PKs in ExtendedData. Tokenize them to
                // display ContentPackageId GUIDs so the importer can translate them to the target
                // environment's display IDs (a verbatim copy would point at stale/foreign displays).
                // Participating keys travel as rule records, so a retired copy left in the blob is dropped.
                ExtendedData = WallLayoutIdRemapper.TokenizeDisplayIds(
                    ContentPackageManifestUpgrade.StripCustomMetadataFields(l.ExtendedData).ExtendedData, displayMap)
            })
            .ToList();

        var ruleIds = resolved.LocationMetadataRuleIds.ToList();
        manifest.LocationMetadataRules = (await db.TblLocationMetadataRules.AsNoTracking()
            .Where(r => ruleIds.Contains(r.LocationMetadataRuleId))
            .ToListAsync(ct))
            .Select(r => new LocationMetadataRuleRecord
            {
                ContentPackageId = r.ContentPackageId,
                LocationContentPackageId = LookupOrEmpty(locationMap, r.LocationId),
                MetadataKey = r.MetadataKey,
                UntaggedContentPolicy = r.UntaggedContentPolicy,
                UntaggedPlayerPolicy = r.UntaggedPlayerPolicy
            })
            .ToList();

        manifest.DisplayGroups = await db.TblDisplayGroups.AsNoTracking()
            .Where(g => displayGroupIds.Contains(g.DisplayGroupId))
            .Select(g => new DisplayGroupRecord
            {
                ContentPackageId = g.ContentPackageId,
                DisplayGroupName = g.DisplayGroupName,
                SortOrder = g.SortOrder
            })
            .ToListAsync(ct);

        manifest.AssetGroups = await db.TblAssetGroups.AsNoTracking()
            .Where(g => assetGroupIds.Contains(g.AssetGroupId))
            .Select(g => new AssetGroupRecord
            {
                ContentPackageId = g.ContentPackageId,
                AssetGroupName = g.AssetGroupName
            })
            .ToListAsync(ct);

        manifest.Constraints = (await db.TblConstraints.AsNoTracking()
            .Where(c => constraintIds.Contains(c.ConstraintId))
            .ToListAsync(ct))
            .Select(c => new ConstraintRecord
            {
                ContentPackageId = c.ContentPackageId,
                ConstraintName = c.ConstraintName,
                OwningLocationContentPackageId = c.OwningLocationId.HasValue && locationMap.TryGetValue(c.OwningLocationId.Value, out var lg)
                    ? lg
                    : (Guid?)null,
                IsGlobal = c.IsGlobal,
                ResolutionWidth = c.ResolutionWidth,
                ResolutionHeight = c.ResolutionHeight,
                ResolutionMode = c.ResolutionMode,
                ExtendedData = c.ExtendedData
            })
            .ToList();

        manifest.Displays = (await db.TblDisplays.AsNoTracking()
            .Where(d => displayIds.Contains(d.DisplayId))
            .ToListAsync(ct))
            .Select(d => new DisplayRecord
            {
                ContentPackageId = d.ContentPackageId,
                LocationContentPackageId = LookupOrEmpty(locationMap, d.LocationId),
                DisplayName = d.DisplayName,
                DisplayMode = d.DisplayMode,
                DisplayTypeId = d.DisplayTypeId,
                DisplayGroupContentPackageId = LookupOrEmpty(displayGroupMap, d.DisplayGroupId),
                DisplayHeight = d.DisplayHeight,
                DisplayWidth = d.DisplayWidth,
                ConstraintContentPackageId = LookupOrEmpty(constraintMap, d.ConstraintId),
                OverrideAssetContentPackageId = d.OverrideAssetId.HasValue && assetMap.TryGetValue(d.OverrideAssetId.Value, out var oa) ? oa : (Guid?)null,
                UseClock = d.UseClock,
                ClockHeight = d.ClockHeight,
                ClockWidth = d.ClockWidth,
                UseVideoMask = d.UseVideoMask,
                EnableFade = d.EnableFade,
                EnableContentApproval = d.EnableContentApproval,
                AssetLibraryLocationContentPackageId = d.AssetLibraryLocationId.HasValue && locationMap.TryGetValue(d.AssetLibraryLocationId.Value, out var alm) ? alm : (Guid?)null,
                StatusSelectorMetadataKey = d.StatusSelectorMetadataKey,
                EndpointId = d.EndpointId,
                SortOrder = d.SortOrder,
                ExtendedData = d.ExtendedData
            })
            .ToList();

        var assetsForBundle = await db.TblAssets.AsNoTracking()
            .Where(a => assetIds.Contains(a.AssetId))
            .Include(a => a.UploadedByUser)
            .Include(a => a.UploadedByUserGroup)
            .ToListAsync(ct);

        manifest.Assets = assetsForBundle
            .Select(a => new AssetRecord
            {
                ContentPackageId = a.ContentPackageId,
                LocationContentPackageId = LookupOrEmpty(locationMap, a.LocationId),
                AssetName = a.AssetName,
                AssetPath = a.AssetPath,
                AssetInkPath = a.AssetInkPath,
                AssetGroupContentPackageId = LookupOrEmpty(assetGroupMap, a.AssetGroupId),
                AssetTypeId = a.AssetTypeId,
                AssetHeight = a.AssetHeight,
                AssetWidth = a.AssetWidth,
                ConstraintContentPackageId = LookupOrEmpty(constraintMap, a.ConstraintId),
                Duration = a.Duration,
                VideoMaskPath = a.VideoMaskPath,
                EndpointId = a.EndpointId,
                EndpointPath = a.EndpointPath,
                WebRenderMode = a.WebRenderMode,
                EnableHealthMonitoring = a.EnableHealthMonitoring,
                ContentSourceUrl = a.ContentSourceUrl,
                ExtendedData = a.ExtendedData,
                SortOrder = a.SortOrder,
                UploadedByUserPrincipalName = a.UploadedByUser?.UserPrincipalName,
                UploadedByUserGroupName = a.UploadedByUserGroup?.GroupName,
                MediaFileName = ExtractFileName(a.AssetPath)
            })
            .ToList();

        var metadataIds = resolved.AssetContentMetadataIds.ToList();
        manifest.AssetContentMetadata = (await db.TblAssetContentMetadata.AsNoTracking()
            .Where(m => metadataIds.Contains(m.AssetContentMetadataId))
            .Include(m => m.SetByUser)
            .ToListAsync(ct))
            .Select(m => new AssetContentMetadataRecord
            {
                ContentPackageId = m.ContentPackageId,
                AssetContentPackageId = LookupOrEmpty(assetMap, m.AssetId),
                MetadataKey = m.MetadataKey,
                MetadataValue = m.MetadataValue,
                SetByUserPrincipalName = m.SetByUser?.UserPrincipalName
            })
            .ToList();

        var eventIds = resolved.ScheduleEventIds.ToList();
        var scheduleEventEntities = await db.TblScheduleEvents.AsNoTracking()
            .Where(e => eventIds.Contains(e.ScheduleEventId))
            .ToListAsync(ct);

        // Video-wall allocation events reference assets, constraints and user groups that are often
        // global libraries outside the in-scope sets, so resolve their ContentPackageId / name directly.
        var refAssetIds = scheduleEventEntities
            .Where(e => e.DefaultAssetId.HasValue).Select(e => e.DefaultAssetId!.Value)
            .Concat(scheduleEventEntities.SelectMany(e => ContentAssignmentRemapper.ExtractAssetIds(e.ContentAssignment)))
            .Distinct().ToList();
        var refAssetGuid = refAssetIds.Count > 0
            ? await db.TblAssets.AsNoTracking().Where(a => refAssetIds.Contains(a.AssetId))
                .ToDictionaryAsync(a => a.AssetId, a => a.ContentPackageId, ct)
            : new System.Collections.Generic.Dictionary<int, Guid>();

        var refConstraintIds = scheduleEventEntities
            .Where(e => e.OverrideConstraintId.HasValue).Select(e => e.OverrideConstraintId!.Value)
            .Distinct().ToList();
        var refConstraintGuid = refConstraintIds.Count > 0
            ? await db.TblConstraints.AsNoTracking().Where(c => refConstraintIds.Contains(c.ConstraintId))
                .ToDictionaryAsync(c => c.ConstraintId, c => c.ContentPackageId, ct)
            : new System.Collections.Generic.Dictionary<int, Guid>();

        var refGroupIds = scheduleEventEntities
            .Where(e => e.UserGroupId.HasValue).Select(e => e.UserGroupId!.Value)
            .Distinct().ToList();
        var refGroupName = refGroupIds.Count > 0
            ? await db.TblUserGroups.AsNoTracking().Where(g => refGroupIds.Contains(g.UserGroupId))
                .ToDictionaryAsync(g => g.UserGroupId, g => (string?)g.GroupName, ct)
            : new System.Collections.Generic.Dictionary<int, string?>();

        manifest.ScheduleEvents = scheduleEventEntities
            .Select(e => new ScheduleEventRecord
            {
                ContentPackageId = e.ContentPackageId,
                ScheduleEventTypeId = e.ScheduleEventTypeId,
                DisplayContentPackageId = LookupOrEmpty(displayMap, e.DisplayId),
                LocationContentPackageId = e.LocationId.HasValue && locationMap.TryGetValue(e.LocationId.Value, out var lp) ? lp : (Guid?)null,
                AirlineCode = e.AirlineCode,
                ContentAssignment = ContentAssignmentRemapper.TokenizeAssetIds(e.ContentAssignment, refAssetGuid) ?? e.ContentAssignment,
                UserGroupName = e.UserGroupId.HasValue && refGroupName.TryGetValue(e.UserGroupId.Value, out var gn) ? gn : null,
                DefaultAssetContentPackageId = e.DefaultAssetId.HasValue && refAssetGuid.TryGetValue(e.DefaultAssetId.Value, out var dag) ? dag : (Guid?)null,
                OverrideConstraintContentPackageId = e.OverrideConstraintId.HasValue && refConstraintGuid.TryGetValue(e.OverrideConstraintId.Value, out var ocg) ? ocg : (Guid?)null,
                StartTime = e.StartTime,
                EndTime = e.EndTime,
                StartDate = e.StartDate,
                EndDate = e.EndDate,
                DefaultContent = e.DefaultContent,
                Sunday = e.Sunday,
                Monday = e.Monday,
                Tuesday = e.Tuesday,
                Wednesday = e.Wednesday,
                Thursday = e.Thursday,
                Friday = e.Friday,
                Saturday = e.Saturday
            })
            .ToList();

        var floorPlanIds = resolved.FloorPlanElementIds.ToList();
        manifest.FloorPlanElements = (await db.TblFloorPlanElements.AsNoTracking()
            .Where(fp => floorPlanIds.Contains(fp.FloorPlanElementId))
            .ToListAsync(ct))
            .Select(fp => new FloorPlanElementRecord
            {
                ContentPackageId = fp.ContentPackageId,
                LocationContentPackageId = LookupOrEmpty(locationMap, fp.LocationId),
                ChildLocationContentPackageId = fp.ChildLocationId.HasValue && locationMap.TryGetValue(fp.ChildLocationId.Value, out var cl) ? cl : (Guid?)null,
                ChildDisplayContentPackageId = fp.ChildDisplayId.HasValue && displayMap.TryGetValue(fp.ChildDisplayId.Value, out var cd) ? cd : (Guid?)null,
                PositionTop = fp.PositionTop,
                PositionLeft = fp.PositionLeft,
                ElementWidth = fp.ElementWidth,
                ElementHeight = fp.ElementHeight,
                IconPath = fp.IconPath,
                IconLabel = fp.IconLabel,
                ShowLivePreview = fp.ShowLivePreview,
                Latitude = fp.Latitude,
                Longitude = fp.Longitude,
                Position3DX = fp.Position3DX,
                Position3DY = fp.Position3DY,
                Position3DZ = fp.Position3DZ,
                SortOrder = fp.SortOrder
            })
            .ToList();

        if (accessRuleIds.Count > 0)
        {
            manifest.LocationAccessRules = (await db.TblLocationAccessRules.AsNoTracking()
                .Where(r => accessRuleIds.Contains(r.LocationAccessRuleId))
                .Include(r => r.User)
                .Include(r => r.UserGroup)
                .ToListAsync(ct))
                .Select(r => new LocationAccessRuleRecord
                {
                    ContentPackageId = r.ContentPackageId,
                    LocationContentPackageId = LookupOrEmpty(locationMap, r.LocationId),
                    UserPrincipalName = r.User?.UserPrincipalName,
                    UserGroupName = r.UserGroup?.GroupName,
                    AccessLevel = r.AccessLevel,
                    ViewScope = r.ViewScope
                })
                .ToList();
        }

        var accessScheduleIds = resolved.AccessScheduleIds.ToList();
        if (accessScheduleIds.Count > 0)
        {
            manifest.AccessSchedules = (await db.TblAccessSchedules.AsNoTracking()
                .Where(s => accessScheduleIds.Contains(s.AccessScheduleId))
                .ToListAsync(ct))
                .Select(s => new AccessScheduleRecord
                {
                    ContentPackageId = s.ContentPackageId,
                    LocationAccessRuleContentPackageId = LookupOrEmpty(accessRuleMap, s.LocationAccessRuleId),
                    DisplayContentPackageId = s.DisplayId.HasValue && displayMap.TryGetValue(s.DisplayId.Value, out var dp) ? dp : (Guid?)null,
                    ConstraintContentPackageId = s.ConstraintId.HasValue && constraintMap.TryGetValue(s.ConstraintId.Value, out var cp) ? cp : (Guid?)null,
                    StartDate = s.StartDate,
                    EndDate = s.EndDate,
                    Sunday = s.Sunday,
                    Monday = s.Monday,
                    Tuesday = s.Tuesday,
                    Wednesday = s.Wednesday,
                    Thursday = s.Thursday,
                    Friday = s.Friday,
                    Saturday = s.Saturday,
                    MetadataKey = s.MetadataKey
                })
                .ToList();
        }

        var chainIds = resolved.ApprovalChainIds.ToList();
        if (chainIds.Count > 0)
        {
            manifest.ApprovalChains = (await db.TblApprovalChains.AsNoTracking()
                .Where(c => chainIds.Contains(c.ApprovalChainId))
                .ToListAsync(ct))
                .Select(c => new ApprovalChainRecord
                {
                    ContentPackageId = c.ContentPackageId,
                    LocationContentPackageId = LookupOrEmpty(locationMap, c.LocationId),
                    IsEnabled = c.IsEnabled,
                    IsDefault = c.IsDefault,
                    LocationAccessRuleContentPackageId = c.LocationAccessRuleId.HasValue && accessRuleMap.TryGetValue(c.LocationAccessRuleId.Value, out var arp) ? arp : (Guid?)null,
                    AutoApproveMembers = c.AutoApproveMembers,
                    EnableLlmAutoApproval = c.EnableLlmAutoApproval,
                    LlmAutoApprovalUpToStageOrder = c.LlmAutoApprovalUpToStageOrder,
                    DeclinedContentRetentionDays = c.DeclinedContentRetentionDays
                })
                .ToList();
        }

        // Per-airline, per-wall default content (background/mask/logo + counter-content constraint).
        // Keyed to the packaged wall locations. The owning UserGroup and a global DefaultConstraint
        // are carried by name because both are environment-wide and resolved by name on import.
        // Only the default dayparting state is exported — the bundle schema carries one record
        // per (group, location); additional states are environment-specific tuning.
        manifest.UserGroupLocationDefaults = (await db.TblUserGroupLocationDefaults.AsNoTracking()
            .Where(x => locationIds.Contains(x.LocationId) && x.IsDefaultState)
            .Include(x => x.UserGroup)
            .Include(x => x.DefaultConstraint)
            .Include(x => x.Logos)
            .Include(x => x.LayerContents)
            .ToListAsync(ct))
            .Select(x => new UserGroupLocationDefaultRecord
            {
                ContentPackageId = Guid.NewGuid(), // synthetic — row is matched by (UserGroup, Location) on import
                LocationContentPackageId = LookupOrEmpty(locationMap, x.LocationId),
                UserGroupName = x.UserGroup != null ? x.UserGroup.GroupName : null,
                DefaultConstraintContentPackageId = x.DefaultConstraintId.HasValue
                    && constraintMap.TryGetValue(x.DefaultConstraintId.Value, out var dcg) ? dcg : (Guid?)null,
                DefaultConstraintName = x.DefaultConstraint != null ? x.DefaultConstraint.ConstraintName : null,
                DefaultConstraintIsGlobal = x.DefaultConstraint != null && x.DefaultConstraint.IsGlobal,
                BackgroundPath = x.BackgroundPath,
                BackgroundMode = x.BackgroundMode,
                BackgroundOpacityPct = x.BackgroundOpacityPct,
                MaskPath = x.MaskPath,
                MaskMode = x.MaskMode,
                MaskOpacityPct = x.MaskOpacityPct,
                LogoPath = x.LogoPath,
                Logos = x.Logos.Select(l => new LogoRoleContentRecord
                {
                    LogoRoleKey = l.LogoRoleKey,
                    ContentPath = l.ContentPath,
                    ContentMode = l.ContentMode
                }).ToList(),
                Segments = x.LayerContents.OrderBy(s => s.SortOrder).Select(s => new LayerSegmentRecord
                {
                    LayerRole = s.LayerRole,
                    SortOrder = s.SortOrder,
                    ContentPath = s.ContentPath,
                    IntrinsicWidthPx = s.IntrinsicWidthPx,
                    ContentMode = s.ContentMode
                }).ToList()
            })
            .ToList();

        // Package the ZIP: manifest.json + media/{ContentPackageId}/{fileName} per asset.
        var result = new ContentPackageBundleResult
        {
            TotalRecords = resolved.TotalRecordCount
        };

        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using (var entryStream = manifestEntry.Open())
            await using (var writer = new StreamWriter(entryStream, new UTF8Encoding(false)))
            {
                var json = JsonSerializer.Serialize(manifest, JsonOptions);
                await writer.WriteAsync(json);
            }

            foreach (var asset in assetsForBundle)
            {
                ct.ThrowIfCancellationRequested();
                var diskPath = ResolveAssetDiskPath(asset.AssetPath);
                if (diskPath == null || !File.Exists(diskPath))
                {
                    _logger.LogDebug("Skipping media for asset {AssetId} ({ContentPackageId}): no file at {Path}",
                        asset.AssetId, asset.ContentPackageId, diskPath ?? "(null)");
                    continue;
                }

                var fileName = Path.GetFileName(diskPath);
                var entryName = $"media/{asset.ContentPackageId}/{fileName}";
                var fileEntry = archive.CreateEntry(entryName, CompressionLevel.Fastest);

                // Block-scoped using so the entry is fully closed before
                // opening the next one (ZipArchive requires it).
                await using (var fileStream = File.OpenRead(diskPath))
                await using (var entryWrite = fileEntry.Open())
                {
                    await fileStream.CopyToAsync(entryWrite, ct);
                    result.MediaFileCount++;
                    result.TotalMediaBytes += fileStream.Length;
                }

                // Include the .metadata sidecar when present (used by playlist-style locations)
                var metadataPath = diskPath + ".metadata";
                if (File.Exists(metadataPath))
                {
                    var metaEntryName = $"media/{asset.ContentPackageId}/{fileName}.metadata";
                    var metaEntry = archive.CreateEntry(metaEntryName, CompressionLevel.Optimal);
                    await using (var metaStream = File.OpenRead(metadataPath))
                    await using (var metaWrite = metaEntry.Open())
                    {
                        await metaStream.CopyToAsync(metaWrite, ct);
                    }
                }
            }
        }

        var scopeSlug = string.IsNullOrWhiteSpace(selection.ScopeDescription)
            ? "selection"
            : new string(selection.ScopeDescription.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
        if (string.IsNullOrEmpty(scopeSlug)) scopeSlug = "selection";
        result.SuggestedFileName = $"content-package-{scopeSlug}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";

        _logger.LogInformation(
            "Content package bundle built: {Records} records, {Files} media files ({Bytes} bytes) — {FileName}",
            result.TotalRecords, result.MediaFileCount, result.TotalMediaBytes, result.SuggestedFileName);

        return result;
    }

    private static Guid LookupOrEmpty(System.Collections.Generic.Dictionary<int, Guid> map, int id)
        => map.TryGetValue(id, out var guid) ? guid : Guid.Empty;

    private static string? ExtractFileName(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return null;
        var trimmed = assetPath.Replace('\\', '/').TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash >= 0 && slash < trimmed.Length - 1 ? trimmed[(slash + 1)..] : trimmed;
    }

    /// <summary>
    /// Resolves a web-style AssetPath (e.g. /MediaAssets/12/foo.png) to its on-disk location
    /// under the web root. Returns null when the path is empty or an external URL.
    /// </summary>
    private string? ResolveAssetDiskPath(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return null;
        if (assetPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            assetPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var webRoot = _mediaPaths.GetRoot();
        if (string.IsNullOrEmpty(webRoot)) return null;

        var relative = assetPath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(webRoot, relative);
    }
}
