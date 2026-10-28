using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;

namespace DXSuite.Director.Services;

/// <summary>A registry field as the rule builder offers it.</summary>
public sealed record RuleBuilderField(string Key, string DisplayName, string FieldType, int SortOrder);

/// <summary>A Layout location that shows this location as one of its zones.</summary>
public sealed record LayoutZoneUsage(string LayoutName, int ZoneNumber);

/// <summary>Everything the content metadata rule builder shows for one location, read once when it opens.</summary>
/// <param name="IsPlaylist">Selects the full builder; every other location type gets the reduced, scope-only shape.</param>
/// <param name="Rules">The stored rule rows, including any whose key the registry no longer defines.</param>
/// <param name="Registry">Every registry field, ordered as the builder lists them.</param>
/// <param name="Assets">The location's content: every live asset record, plus a null-valued entry per file with no record.</param>
/// <param name="FilesWithoutAssetRecord">How many of <paramref name="Assets"/> are files with no record.</param>
public sealed record RuleBuilderSnapshot(
    int LocationId,
    string LocationName,
    bool IsPlaylist,
    bool TargetContentByMetadata,
    string MatchMode,
    IReadOnlyList<LocationMetadataRuleInput> Rules,
    IReadOnlyList<RuleBuilderField> Registry,
    IReadOnlyList<RuleBuilderAsset> Assets,
    int FilesWithoutAssetRecord,
    IReadOnlyList<RuleBuilderPlayer> Players,
    IReadOnlyList<LayoutZoneUsage> LayoutUsages);

/// <summary>
/// Reads and writes a location's content metadata rules for the admin surfaces. Each call uses its own
/// short-lived context, because the Locations tab and the uploaders query while other components on the
/// circuit are still loading.
/// </summary>
public interface IContentMetadataRuleEditor
{
    Task<RuleBuilderSnapshot?> LoadAsync(int locationId, CancellationToken ct = default);

    /// <summary>The location's participating keys: rule rows whose key the registry defines, ordered by key.</summary>
    Task<IReadOnlyList<string>> GetParticipatingKeysAsync(int locationId, CancellationToken ct = default);

    Task SaveAsync(int locationId, bool targetContentByMetadata, string matchMode,
        IReadOnlyCollection<LocationMetadataRuleInput> rules, string editUser, CancellationToken ct = default);
}

public class ContentMetadataRuleEditor : IContentMetadataRuleEditor
{
    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly IMemoryCache _cache;
    private readonly DXSuite.Shared.Multitenancy.ITenantContext _tenantContext;
    private readonly IMediaFolderCacheService _mediaFolders;
    private readonly ILoggerFactory _loggerFactory;

    public ContentMetadataRuleEditor(
        IDbContextFactory<DirectorDbContext> dbFactory,
        IMemoryCache cache,
        DXSuite.Shared.Multitenancy.ITenantContext tenantContext,
        IMediaFolderCacheService mediaFolders,
        ILoggerFactory loggerFactory)
    {
        _dbFactory = dbFactory;
        _cache = cache;
        _tenantContext = tenantContext;
        _mediaFolders = mediaFolders;
        _loggerFactory = loggerFactory;
    }

    private LocationMetadataRuleService RulesFor(DirectorDbContext db) =>
        new(db, _cache, _tenantContext, _loggerFactory.CreateLogger<LocationMetadataRuleService>());

    public async Task<RuleBuilderSnapshot?> LoadAsync(int locationId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var location = await db.TblLocations.AsNoTracking()
            .Include(l => l.LocationType)
            .FirstOrDefaultAsync(l => l.LocationId == locationId, ct);
        if (location == null)
            return null;

        var isPlaylist = LocationTypeConstants.IsPlaylist(location.LocationType);

        var rules = (await RulesFor(db).GetRulesAsync(locationId, ct))
            .Select(r => new LocationMetadataRuleInput(r.MetadataKey, r.UntaggedContentPolicy, r.UntaggedPlayerPolicy))
            .ToList();

        var registry = await db.TblMetadataDefinitions.AsNoTracking()
            .OrderBy(d => d.SortOrder).ThenBy(d => d.DisplayName)
            .Select(d => new RuleBuilderField(d.MetadataKey, d.DisplayName, d.FieldType, d.SortOrder))
            .ToListAsync(ct);

        var resolver = new MetadataSubjectResolver(db);
        var assetIds = await db.TblAssets.AsNoTracking()
            .Where(a => a.LocationId == locationId && !a.IsDeleted)
            .Select(a => a.AssetId)
            .ToListAsync(ct);
        var assetValues = await resolver.GetAssetValuesAsync(assetIds, ct);
        var assets = assetIds.Select(id => new RuleBuilderAsset(assetValues[id])).ToList();

        var filesWithoutRecord = 0;
        var players = new List<RuleBuilderPlayer>();
        var layoutUsages = new List<LayoutZoneUsage>();

        if (isPlaylist)
        {
            // A file on disk with no asset record plays untargeted, so it counts as content every player receives.
            var records = await resolver.GetLocationAssetRecordsAsync(locationId, ct);
            filesWithoutRecord = PlaylistManifestRules
                .SelectPublished(_mediaFolders.GetMediaFiles(_tenantContext.TenantId, locationId), locationId, records, enableContentApproval: false)
                .Count(f => f.Record == null);
            assets.AddRange(Enumerable.Range(0, filesWithoutRecord).Select(_ => new RuleBuilderAsset(null)));

            var devices = await db.TblDevices.AsNoTracking()
                .Where(d => d.LocationId == locationId)
                .Select(d => new { d.DeviceId, d.Name })
                .ToListAsync(ct);
            var deviceValues = await resolver.GetDeviceValuesAsync(devices.Select(d => d.DeviceId).ToList(), ct);
            players = devices
                .Select(d => new RuleBuilderPlayer(string.IsNullOrWhiteSpace(d.Name) ? d.DeviceId : d.Name!, deviceValues[d.DeviceId]))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            layoutUsages = await GetLayoutUsagesAsync(db, locationId, ct);
        }

        return new RuleBuilderSnapshot(
            locationId,
            location.LocationName ?? "",
            isPlaylist,
            location.TargetContentByMetadata,
            location.MetadataMatchMode,
            rules,
            registry,
            assets,
            filesWithoutRecord,
            players,
            layoutUsages);
    }

    public async Task<IReadOnlyList<string>> GetParticipatingKeysAsync(int locationId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return (await RulesFor(db).GetTargetingAsync(locationId, ct)).Rules.Participating.Select(r => r.Key).ToList();
    }

    public async Task SaveAsync(int locationId, bool targetContentByMetadata, string matchMode,
        IReadOnlyCollection<LocationMetadataRuleInput> rules, string editUser, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await RulesFor(db).SaveAsync(locationId, targetContentByMetadata, matchMode, rules, editUser, ct);
    }

    // Layout zones live in each Layout location's ExtendedData, so every candidate blob is parsed.
    private static async Task<List<LayoutZoneUsage>> GetLayoutUsagesAsync(DirectorDbContext db, int locationId, CancellationToken ct)
    {
        var layouts = await db.TblLocations.AsNoTracking()
            .Where(l => l.ExtendedData != null && l.ExtendedData.Contains("SourceLocationId"))
            .Select(l => new { l.LocationName, l.ExtendedData })
            .ToListAsync(ct);

        return layouts
            .SelectMany(l => (ParseLayout(l.ExtendedData)?.Zones ?? new List<LayoutZone>())
                .Where(z => z.SourceLocationId == locationId)
                .Select(z => new LayoutZoneUsage(l.LocationName ?? "", z.ZoneNumber)))
            .OrderBy(u => u.LayoutName, StringComparer.OrdinalIgnoreCase).ThenBy(u => u.ZoneNumber)
            .ToList();
    }

    // Layout configuration is written with PascalCase names, unlike the camelCase ExtendedDataHelper blobs.
    private static LayoutConfiguration? ParseLayout(string? json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<LayoutConfiguration>(json ?? "",
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
