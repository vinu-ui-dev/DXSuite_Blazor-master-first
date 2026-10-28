using System.Text.Json;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Director.Services;

public interface IPixeraAllocationService
{
    Task<AllocationResult> GetCurrentAllocationAsync(int locationId, DateTime atTime);
}

public class PixeraAllocationService : IPixeraAllocationService
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IServiceScopeFactory    _scopeFactory;
    private readonly IWebHostEnvironment     _env;

    public PixeraAllocationService(IServiceScopeFactory scopeFactory, IWebHostEnvironment env)
    {
        _scopeFactory = scopeFactory;
        _env          = env;
    }

    public async Task<AllocationResult> GetCurrentAllocationAsync(int locationId, DateTime atTime)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var location = await db.TblLocations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.LocationId == locationId);
        if (location == null)
            return new AllocationResult(locationId, 0, []);

        VideoWallConfiguration? config = null;
        try { config = JsonSerializer.Deserialize<VideoWallConfiguration>(location.ExtendedData ?? "{}", _jsonOpts); }
        catch { }
        if (config?.Layout == null)
            return new AllocationResult(locationId, 0, []);

        var counterGroups = config.Layout.Groups
            .Where(g => g.AllowAirlineAllocation)
            .ToList();

        var rawDisplayIds = counterGroups
            .SelectMany(g => g.DisplayIds)
            .Distinct()
            .ToList();

        if (rawDisplayIds.Count == 0)
            return new AllocationResult(locationId, 0, []);

        var displays = await db.TblDisplays.AsNoTracking()
            .Where(d => rawDisplayIds.Contains(d.DisplayId))
            .ToListAsync();
        var displayById = displays.ToDictionary(d => d.DisplayId);

        // Sort by absolute canvas X position so multi-group layouts (e.g. two counter rows
        // whose displays interleave horizontally) produce the correct left-to-right order.
        var orderedDisplayIds = SortDisplaysByPosition(counterGroups, displayById);

        var events = await db.TblScheduleEvents.AsNoTracking()
            .Where(e => orderedDisplayIds.Contains(e.DisplayId) && e.UserGroupId != null)
            .ToListAsync();

        // Build a lookup from airline group → transient cluster location at this wall.
        // Transient locations are created when an airline is allocated a counter range;
        // their LocationId is what the counter content endpoint loads assets from.
        var transientLocations = await db.TblLocations.AsNoTracking()
            .Where(l => l.IsTransientCluster && l.ParentVideoWallLocationId == locationId && l.OwningUserGroupId != null)
            .ToListAsync();
        // An airline may have multiple transient locations at the same wall (one per allocated range).
        // Group by airline so the dictionary never gets a duplicate key; all transient locations for
        // the same airline share the same content, so taking the first is correct.
        var transientLocationIdByGroup = transientLocations
            .GroupBy(l => l.OwningUserGroupId!.Value)
            .ToDictionary(g => g.Key, g => g.First().LocationId);

        var groupIds = events.Select(e => e.UserGroupId!.Value).Distinct().ToList();
        var groups = await db.TblUserGroups.AsNoTracking()
            .Where(g => groupIds.Contains(g.UserGroupId))
            .ToListAsync();
        var groupById = groups.ToDictionary(g => g.UserGroupId);

        // Resolve the active airline group and default asset for each display at the given time
        var activeGroupByDisplay       = orderedDisplayIds.ToDictionary(id => id, _ => (int?)null);
        var activeDefaultAssetByDisplay = orderedDisplayIds.ToDictionary(id => id, _ => (int?)null);
        foreach (var ev in events)
        {
            if (!activeGroupByDisplay.ContainsKey(ev.DisplayId)) continue;
            if (IsTimeActive(ev, atTime) && IsDayActive(ev, atTime))
            {
                activeGroupByDisplay[ev.DisplayId] = ev.UserGroupId;
                if (ev.DefaultAssetId.HasValue)
                    activeDefaultAssetByDisplay[ev.DisplayId] = ev.DefaultAssetId;
            }
        }

        // Load group-location defaults for all active groups in one query
        var activeGroupIds = activeGroupByDisplay.Values
            .Where(v => v.HasValue).Select(v => v!.Value).Distinct().ToList();
        var defaults = await db.TblUserGroupLocationDefaults.AsNoTracking()
            .Include(x => x.LayerContents)
            .Where(x => x.LocationId == locationId && activeGroupIds.Contains(x.UserGroupId))
            .ToListAsync();
        // Multiple rows per group are dayparting states; resolve each to its active state
        var defaultByGroup = defaults
            .GroupBy(x => x.UserGroupId)
            .ToDictionary(g => g.Key, g => AirlineStateResolver.PickActive(g, atTime)!);

        // Cluster consecutive displays that share the same active group
        var clusters = new List<DisplayCluster>();
        int i = 0;
        while (i < orderedDisplayIds.Count)
        {
            var groupId = activeGroupByDisplay[orderedDisplayIds[i]];
            int j = i + 1;
            while (j < orderedDisplayIds.Count && activeGroupByDisplay[orderedDisplayIds[j]] == groupId)
                j++;

            var clusterIds = orderedDisplayIds.Skip(i).Take(j - i).ToList();
            var displayNames = clusterIds
                .Select(id => displayById.TryGetValue(id, out var d)
                    ? d.DisplayName ?? $"Counter {id}"
                    : $"Counter {id}")
                .ToList<string>();

            string? maskPath = null, maskMode = null, bgPath = null, bgMode = null;
            IReadOnlyList<LayerContentSegment>? maskSegs = null, bgSegs = null;
            int maskOpacity = 100, bgOpacity = 100;
            var groupName = "";
            if (groupId.HasValue)
            {
                groupName = groupById.TryGetValue(groupId.Value, out var grp) ? grp.GroupName : "";
                if (defaultByGroup.TryGetValue(groupId.Value, out var def))
                {
                    maskPath    = def.MaskPath;
                    maskMode    = def.MaskMode;
                    bgPath      = def.BackgroundPath;
                    bgMode      = def.BackgroundMode;
                    maskSegs    = ProjectSegments(def, "mask");
                    bgSegs      = ProjectSegments(def, "background");
                    maskOpacity = def.MaskOpacityPct;
                    bgOpacity   = def.BackgroundOpacityPct;
                }
            }

            int? transientLocationId = groupId.HasValue && transientLocationIdByGroup.TryGetValue(groupId.Value, out var tid)
                ? tid : null;

            // DefaultAssetId comes from the first display in this cluster whose active event carries one.
            activeDefaultAssetByDisplay.TryGetValue(orderedDisplayIds[i], out var clusterDefaultAssetId);

            clusters.Add(new DisplayCluster(groupId, groupName, i, j - i,
                maskPath, maskMode, bgPath, bgMode, displayNames, transientLocationId,
                DefaultAssetId: clusterDefaultAssetId,
                MaskSegments: maskSegs, BackgroundSegments: bgSegs,
                MaskOpacityPct: maskOpacity, BackgroundOpacityPct: bgOpacity));
            i = j;
        }

        // Build a per-display canvas position map using ComputeAccumulatedLeft, which correctly
        // incorporates each group's PositionLeft and the within-group display index. A naive
        // cursor loop starting at 0 with the global loop index for GetGapAfter has two bugs:
        //   (1) it ignores each group's PositionLeft, shifting all displays relative to the
        //       physical canvas origin; and (2) the cyclic gap pattern phases incorrectly for
        //       groups past the first because the global index carries over from previous groups.
        var leftByDisplayId  = new Dictionary<int, double>();
        var widthByDisplayId = new Dictionary<int, double>();
        foreach (var grp in counterGroups)
        {
            double scale = grp.DisplayScale > 0 ? grp.DisplayScale : 1.0;
            var scaledWidths = grp.DisplayIds
                .Select(id => displayById.TryGetValue(id, out var d) && d.DisplayWidth > 0
                              ? d.DisplayWidth * scale : 1920 * scale)
                .ToList<double>();
            for (int wi = 0; wi < grp.DisplayIds.Count; wi++)
            {
                var id = grp.DisplayIds[wi];
                leftByDisplayId[id]  = grp.ComputeAccumulatedLeft(wi, scaledWidths);
                widthByDisplayId[id] = scaledWidths[wi];
            }
        }

        // Cluster extents come from actual display positions, not accumulated width
        // sums — gap patterns are group-local, so width sums would undershoot across
        // row boundaries, leaving phantom uncovered strips. The boundary rules
        // themselves (row allocation bounds, 50/50 same-row splits, legacy cross-row
        // inheritance, wall-edge handling) live in ClusterBoundsResolver.
        var slots = new ClusterBoundsResolver.DisplaySlot[orderedDisplayIds.Count];
        for (int si = 0; si < orderedDisplayIds.Count; si++)
        {
            var id  = orderedDisplayIds[si];
            var grp = counterGroups.FirstOrDefault(g => g.DisplayIds.Contains(id));
            int withinGrpIdx = grp?.DisplayIds.IndexOf(id) ?? 0;
            slots[si] = new ClusterBoundsResolver.DisplaySlot(
                LeftPx:             leftByDisplayId.GetValueOrDefault(id),
                WidthPx:            widthByDisplayId.TryGetValue(id, out var w) ? w : 1920,
                RowKey:             grp != null ? counterGroups.IndexOf(grp) : -1,
                IsRowFirst:         grp != null && grp.DisplayIds[0] == id,
                IsRowLast:          grp != null && grp.DisplayIds[^1] == id,
                RowAllocationLeft:  grp?.AllocationLeftPx,
                RowAllocationRight: grp?.AllocationRightPx,
                TrailingGap:        grp?.GetGapAfter(withinGrpIdx) ?? 0);
        }

        var bounds = ClusterBoundsResolver.Resolve(
            clusters.Select(c => new ClusterBoundsResolver.ClusterRun(c.StartIndex, c.Count)).ToList(),
            slots);

        var enrichedClusters = new List<DisplayCluster>(clusters.Count);
        for (int ci = 0; ci < clusters.Count; ci++)
        {
            var c = clusters[ci];
            enrichedClusters.Add(c with
            {
                LeftPx            = bounds[ci].Left,
                WidthPx           = Math.Max(0, bounds[ci].Right - bounds[ci].Left),
                IsRightmost       = bounds[ci].IsRightmost,
                MaskVersion       = GetFileVersion(c.MaskPath),
                BackgroundVersion = GetFileVersion(c.BackgroundPath),
            });
        }

        var logoZones       = await BuildLogoZonesAsync(db, locationId, config, enrichedClusters, atTime);
        var backgroundZones = await BuildBackgroundZonesAsync(db, config, atTime);

        return new AllocationResult(locationId, orderedDisplayIds.Count, enrichedClusters, logoZones,
            BackgroundZones: backgroundZones);
    }

    /// <summary>
    /// Resolves logo zones from groups with Role="logo" in the wall layout.
    /// Each display in a logo group is one zone. The airline shown in a zone is determined
    /// by which enriched cluster's canvas X range contains the zone's midpoint — a single
    /// contiguous allocation can therefore light up multiple zones simultaneously.
    /// The logo asset path comes from TblUserGroupLocationDefault.LogoPath for that airline.
    /// </summary>
    private async Task<List<LogoZone>> BuildLogoZonesAsync(
        IDirectorDBContext db,
        int locationId,
        VideoWallConfiguration? config,
        IReadOnlyList<DisplayCluster> clusters,
        DateTime atTime)
    {
        var logoGroups = config?.Layout?.Groups
            .Where(g => string.Equals(g.Role, "logo", StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];
        if (logoGroups.Count == 0) return [];

        var logoDisplayIds = logoGroups.SelectMany(g => g.DisplayIds).Distinct().ToList();
        var logoDisplays = await db.TblDisplays.AsNoTracking()
            .Where(d => logoDisplayIds.Contains(d.DisplayId))
            .ToListAsync();
        var logoDisplayById = logoDisplays.ToDictionary(d => d.DisplayId);

        // Load logo content for all airlines that have an active allocation at this wall:
        // the legacy single LogoPath (role-less zones) plus any per-role uploads.
        var activeGroupIds = clusters
            .Where(c => c.GroupId.HasValue)
            .Select(c => c.GroupId!.Value)
            .Distinct().ToList();
        var logoPathByGroup    = new Dictionary<int, string?>();
        var contentByGroupRole = new Dictionary<(int GroupId, string RoleKey), (string? Path, string Mode)>();
        if (activeGroupIds.Count > 0)
        {
            var defaults = await db.TblUserGroupLocationDefaults.AsNoTracking()
                .Include(d => d.Logos)
                .Where(d => d.LocationId == locationId && activeGroupIds.Contains(d.UserGroupId))
                .ToListAsync();
            // Multiple rows per group are dayparting states; resolve each to its active state
            foreach (var d in defaults.GroupBy(x => x.UserGroupId)
                         .Select(g => AirlineStateResolver.PickActive(g, atTime)!))
            {
                logoPathByGroup[d.UserGroupId] = d.LogoPath;
                foreach (var logo in d.Logos)
                    contentByGroupRole[(d.UserGroupId, logo.LogoRoleKey)] = (logo.ContentPath, logo.ContentMode);
            }
        }

        var zones = new List<LogoZone>();
        foreach (var grp in logoGroups)
        {
            // Zones with a role key render only that role's upload — an airline with no
            // content for the role shows an empty zone rather than its default logo
            // (accent-style roles must not fall back to the primary logo). Role-less
            // zones keep the legacy single-logo behaviour.
            var roleKey = string.IsNullOrWhiteSpace(grp.LogoRoleKey) ? null : grp.LogoRoleKey;

            double scale = grp.DisplayScale > 0 ? grp.DisplayScale : 1.0;
            var scaledWidths = grp.DisplayIds
                .Select(id => logoDisplayById.TryGetValue(id, out var d) && d.DisplayWidth > 0
                              ? d.DisplayWidth * scale : 1920 * scale)
                .ToList<double>();

            for (int wi = 0; wi < grp.DisplayIds.Count; wi++)
            {
                var id = grp.DisplayIds[wi];
                if (!logoDisplayById.TryGetValue(id, out var disp)) continue;

                double zoneLeft  = grp.ComputeAccumulatedLeft(wi, scaledWidths);
                double zoneWidth = scaledWidths[wi];
                double zoneMid   = zoneLeft + zoneWidth / 2.0;
                double zoneTop   = grp.PositionTop;
                double zoneH     = disp.DisplayHeight > 0 ? disp.DisplayHeight * scale : 648 * scale;

                // The cluster whose canvas X range [LeftPx, LeftPx+WidthPx) contains the
                // zone's midpoint is the airline currently displayed in this logo slot.
                int? owningGroupId = null;
                string? logoPath   = null;
                string contentMode = "fit";
                foreach (var cluster in clusters)
                {
                    if (!cluster.GroupId.HasValue) continue;
                    if (zoneMid >= cluster.LeftPx && zoneMid < cluster.LeftPx + cluster.WidthPx)
                    {
                        owningGroupId = cluster.GroupId;
                        if (roleKey == null)
                        {
                            logoPathByGroup.TryGetValue(cluster.GroupId.Value, out logoPath);
                        }
                        else if (contentByGroupRole.TryGetValue((cluster.GroupId.Value, roleKey), out var content))
                        {
                            logoPath    = content.Path;
                            contentMode = content.Mode;
                        }
                        break;
                    }
                }

                zones.Add(new LogoZone(
                    LeftPx:      zoneLeft,
                    TopPx:       zoneTop,
                    WidthPx:     zoneWidth,
                    HeightPx:    zoneH,
                    ZIndex:      grp.ZIndex,
                    GroupId:     owningGroupId,
                    LogoPath:    logoPath,
                    LogoVersion: GetFileVersion(logoPath),
                    RoleKey:     roleKey,
                    ContentMode: contentMode));
            }
        }

        return zones;
    }

    /// <summary>
    /// Resolves background zones from groups with Role="background" in the wall layout.
    /// Each display in a background group is one zone positioned in raw canvas pixels
    /// (same space as DisplayCluster.LeftPx). Content is the asset assigned to the display —
    /// OverrideAssetId → active timed event → DefaultContent event — matching how the admin
    /// wall preview renders background-role displays via StandardDisplay.
    /// </summary>
    private async Task<List<BackgroundZone>> BuildBackgroundZonesAsync(
        IDirectorDBContext db,
        VideoWallConfiguration? config,
        DateTime atTime)
    {
        var bgGroups = config?.Layout?.Groups
            .Where(g => !g.AllowAirlineAllocation
                     && string.Equals(g.Role, "background", StringComparison.OrdinalIgnoreCase))
            .OrderBy(g => g.ZIndex)
            .ToList() ?? [];
        if (bgGroups.Count == 0) return [];

        var bgDisplayIds = bgGroups.SelectMany(g => g.DisplayIds).Distinct().ToList();
        var bgDisplays = await db.TblDisplays.AsNoTracking()
            .Where(d => bgDisplayIds.Contains(d.DisplayId))
            .ToListAsync();
        var bgDisplayById = bgDisplays.ToDictionary(d => d.DisplayId);

        var events = await db.TblScheduleEvents.AsNoTracking()
            .Where(e => bgDisplayIds.Contains(e.DisplayId))
            .ToListAsync();
        var eventsByDisplay = events
            .GroupBy(e => e.DisplayId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Resolve each display's assigned asset id, then load all assets in one query.
        var assetIdByDisplay = new Dictionary<int, int>();
        foreach (var disp in bgDisplays)
        {
            var id = disp.OverrideAssetId
                     ?? ResolveAssignedAssetId(eventsByDisplay.GetValueOrDefault(disp.DisplayId, []), atTime);
            if (id.HasValue) assetIdByDisplay[disp.DisplayId] = id.Value;
        }
        var assetIds = assetIdByDisplay.Values.Distinct().ToList();
        var assetById = assetIds.Count > 0
            ? (await db.TblAssets.AsNoTracking()
                .Where(a => assetIds.Contains(a.AssetId) && !a.IsDeleted)
                .ToListAsync()).ToDictionary(a => a.AssetId)
            : new Dictionary<int, TblAsset>();

        var zones = new List<BackgroundZone>();
        foreach (var grp in bgGroups)
        {
            double scale = grp.DisplayScale > 0 ? grp.DisplayScale : 1.0;
            var scaledWidths = grp.DisplayIds
                .Select(id => bgDisplayById.TryGetValue(id, out var d) && d.DisplayWidth > 0
                              ? d.DisplayWidth * scale : 1920 * scale)
                .ToList<double>();

            for (int wi = 0; wi < grp.DisplayIds.Count; wi++)
            {
                var id = grp.DisplayIds[wi];
                if (!bgDisplayById.ContainsKey(id)) continue;

                string? contentPath = null;
                if (assetIdByDisplay.TryGetValue(id, out var aid) && assetById.TryGetValue(aid, out var asset))
                    contentPath = NormalizePath(asset.AssetPath);

                zones.Add(new BackgroundZone(
                    LeftPx:         grp.ComputeAccumulatedLeft(wi, scaledWidths),
                    WidthPx:        scaledWidths[wi],
                    ZIndex:         grp.ZIndex,
                    LoopCount:      Math.Max(1, grp.HorizontalImageLoop),
                    ContentPath:    contentPath,
                    ContentVersion: GetAssetFileVersion(contentPath)));
            }
        }

        return zones;
    }

    /// <summary>
    /// Returns the AssetId assigned to a display by its schedule events: an active timed
    /// event carrying an explicit content reference wins over the standing DefaultContent
    /// event. Mirrors ResolveScheduledAssetId in PixeraController.
    /// </summary>
    private static int? ResolveAssignedAssetId(List<TblScheduleEvent> events, DateTime atTime)
    {
        foreach (var ev in events.Where(e => !e.DefaultContent))
        {
            if (!IsTimeActive(ev, atTime) || !IsDayActive(ev, atTime)) continue;
            var explicitId = ParseFirstAssetId(ev.ContentAssignment);
            if (explicitId.HasValue) return explicitId;
        }

        return events
            .Where(e => e.DefaultContent)
            .Select(e => ParseFirstAssetId(e.ContentAssignment))
            .FirstOrDefault(id => id.HasValue);
    }

    /// <summary>
    /// Returns the first AssetId from a ContentAssignment JSON blob, or null if the blob
    /// is empty or carries no content.
    /// </summary>
    private static int? ParseFirstAssetId(string? contentAssignment)
    {
        if (string.IsNullOrWhiteSpace(contentAssignment)) return null;
        try
        {
            using var doc = JsonDocument.Parse(contentAssignment);
            if (doc.RootElement.TryGetProperty("Content", out var content) &&
                content.ValueKind == JsonValueKind.Array &&
                content.GetArrayLength() > 0 &&
                content[0].TryGetProperty("AssetId", out var assetId))
                return assetId.GetInt32();
        }
        catch { }
        return null;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        path = path.Replace('\\', '/');
        return path.StartsWith('/') ? path : "/" + path;
    }

    /// <summary>
    /// File-version stamp for an arbitrary wwwroot-relative asset path (e.g.
    /// /MediaAssets/{locationId}/{constraintId}/file.mp4). GetFileVersion only handles
    /// files in the fixed AirlineContent folder; this maps the full relative path.
    /// </summary>
    private long GetAssetFileVersion(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return 0;
        try
        {
            var relative = relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var basePath = Path.GetFullPath(_env.WebRootPath);
            var fullPath = Path.GetFullPath(Path.Combine(basePath, relative));
            // Paths come from the assets table, but guard traversal anyway
            if (!fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) return 0;
            if (!File.Exists(fullPath)) return 0;
            return new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath)).ToUnixTimeSeconds();
        }
        catch { return 0; }
    }

    /// <summary>
    /// Returns counter display IDs sorted by their absolute canvas X position.
    /// Uses each group's PositionLeft plus accumulated display widths and gaps so that
    /// displays belonging to different groups are ordered correctly when groups interleave
    /// horizontally (e.g. two counter rows whose displays share the same X range).
    /// </summary>
    private static List<int> SortDisplaysByPosition(
        IReadOnlyList<PanoramaGroup> counterGroups,
        Dictionary<int, TblDisplay> displayById)
    {
        var positioned = new List<(int DisplayId, double AbsoluteX)>();
        foreach (var group in counterGroups)
        {
            double scale = group.DisplayScale > 0 ? group.DisplayScale : 1.0;
            var scaledWidths = group.DisplayIds
                .Select(id => displayById.TryGetValue(id, out var d) && d.DisplayWidth > 0
                    ? d.DisplayWidth * scale : 1920 * scale)
                .ToList<double>();
            for (int i = 0; i < group.DisplayIds.Count; i++)
                positioned.Add((group.DisplayIds[i], group.ComputeAccumulatedLeft(i, scaledWidths)));
        }
        return positioned.OrderBy(p => p.AbsoluteX).Select(p => p.DisplayId).Distinct().ToList();
    }

    /// <summary>
    /// Projects an airline's ordered side-by-side segments for one layer role, version-stamped
    /// for cache busting. Returns null when the airline has no segments for the role so the
    /// shell falls back to the single MaskPath/BackgroundPath.
    /// </summary>
    private IReadOnlyList<LayerContentSegment>? ProjectSegments(TblUserGroupLocationDefault def, string layerRole)
    {
        var segs = def.LayerContents
            .Where(s => string.Equals(s.LayerRole, layerRole, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(s.ContentPath))
            .OrderBy(s => s.SortOrder)
            .Select(s => new LayerContentSegment(s.ContentPath!, GetFileVersion(s.ContentPath), s.IntrinsicWidthPx))
            .ToList();
        return segs.Count > 0 ? segs : null;
    }

    /// <summary>
    /// Returns the Unix timestamp (seconds) of the file's last write time, used as a version
    /// token so the Pixera layer shell detects re-uploads of the same filename and forces a
    /// fresh iframe fetch. Returns 0 when the path is null or the file does not exist.
    /// </summary>
    private long GetFileVersion(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return 0;
        try
        {
            var fileName = Path.GetFileName(path);
            // Pixera pipeline reads the global web root: the Pixera-owning tenant must keep
            // FilePaths_MediaRoot='wwwroot' when tenant media isolation is enabled.
            var fullPath = Path.Combine(_env.WebRootPath, "MediaAssets", "AirlineContent", fileName);
            if (!File.Exists(fullPath)) return 0;
            return new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath)).ToUnixTimeSeconds();
        }
        catch { return 0; }
    }

    private static bool IsTimeActive(TblScheduleEvent e, DateTime atTime)
        => ScheduleTime.IsWithinWindow(TimeOnly.FromDateTime(atTime), e.StartTime, e.EndTime);

    private static bool IsDayActive(TblScheduleEvent e, DateTime atTime)
    {
        bool anySet = e.Sunday || e.Monday || e.Tuesday || e.Wednesday ||
                      e.Thursday || e.Friday || e.Saturday;
        if (!anySet) return true;
        return atTime.DayOfWeek switch
        {
            DayOfWeek.Sunday    => e.Sunday,
            DayOfWeek.Monday    => e.Monday,
            DayOfWeek.Tuesday   => e.Tuesday,
            DayOfWeek.Wednesday => e.Wednesday,
            DayOfWeek.Thursday  => e.Thursday,
            DayOfWeek.Friday    => e.Friday,
            DayOfWeek.Saturday  => e.Saturday,
            _                   => false
        };
    }
}
