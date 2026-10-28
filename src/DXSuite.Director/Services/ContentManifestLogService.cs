using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Director.Services;

/// <summary>
/// Last logged manifest version per bucket, so the hot path only touches the
/// database when a rebuild actually produced new content.
/// </summary>
public class ContentManifestVersionTracker
{
    public ConcurrentDictionary<(string TenantId, string BucketType, int BucketId), string> LastVersions { get; } = new();
    public DateTime LastTrimUtc { get; set; } = DateTime.MinValue;
    public object TrimLock { get; } = new();
}

public class ManifestLogItem
{
    public string? Name { get; set; }
    public string? Url { get; set; }
    public int Plays { get; set; }
    public int DurationSeconds { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public string? Days { get; set; }
}

public interface IContentManifestLogService
{
    /// <summary>
    /// Appends a content-log row when the publication's version differs from the bucket's last
    /// logged version. Failures are swallowed; logging must never break
    /// the player content path.
    /// </summary>
    Task RecordIfChangedAsync(string bucketType, int bucketId, CachedManifest manifest);
}

public class ContentManifestLogService : IContentManifestLogService
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(400);
    private static readonly TimeSpan TrimInterval = TimeSpan.FromHours(24);

    // A publication is shared by every device in its bucket, so every row is the unfiltered variant.
    // Rows written when filtered locations published one manifest per value keep that value.
    private const string UnfilteredVariant = "all";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IDirectorDBContext _db;
    private readonly ContentManifestVersionTracker _tracker;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ContentManifestLogService> _logger;

    public ContentManifestLogService(
        IDirectorDBContext db,
        ContentManifestVersionTracker tracker,
        ITenantContext tenantContext,
        ILogger<ContentManifestLogService> logger)
    {
        _db = db;
        _tracker = tracker;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task RecordIfChangedAsync(string bucketType, int bucketId, CachedManifest manifest)
    {
        if (string.IsNullOrEmpty(manifest.Version)) return;

        var key = (_tenantContext.TenantId, bucketType, bucketId);

        try
        {
            if (_tracker.LastVersions.TryGetValue(key, out var lastVersion) && lastVersion == manifest.Version)
                return;

            // First encounter since startup: the last row may already carry this version
            var lastLogged = await _db.TblContentManifestLogs
                .Where(l => l.BucketType == bucketType && l.BucketId == bucketId && l.FilterValue == UnfilteredVariant)
                .OrderByDescending(l => l.EffectiveFromUtc)
                .Select(l => l.Version)
                .FirstOrDefaultAsync();

            if (lastLogged == manifest.Version)
            {
                _tracker.LastVersions[key] = manifest.Version;
                return;
            }

            var items = BuildSnapshot(manifest);
            _db.TblContentManifestLogs.Add(new TblContentManifestLog
            {
                BucketType = bucketType,
                BucketId = bucketId,
                FilterValue = UnfilteredVariant,
                Version = manifest.Version,
                ItemsJson = JsonSerializer.Serialize(items, JsonOptions),
                ItemCount = items.Count,
                PlayCount = items.Sum(i => i.Plays),
                EffectiveFromUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            _tracker.LastVersions[key] = manifest.Version;

            await TrimIfDueAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Content manifest log write failed for {BucketType} {BucketId}", bucketType, bucketId);
        }
    }

    private static List<ManifestLogItem> BuildSnapshot(CachedManifest manifest)
    {
        var items = new List<ManifestLogItem>();
        if (manifest.Assets == null) return items;

        // The cached asset list is the expanded weighted loop; collapse repeats back
        // into per-item play counts for a compact snapshot
        foreach (var group in manifest.Assets.GroupBy(a => a.Url ?? a.FileName ?? ""))
        {
            var first = group.First();
            items.Add(new ManifestLogItem
            {
                Name = first.FileName ?? first.AssetName ?? first.Url,
                Url = first.Url,
                Plays = group.Count(),
                DurationSeconds = first.Duration ?? 7,
                StartDate = first.StartDate,
                EndDate = first.EndDate,
                StartTime = first.StartTime,
                EndTime = first.EndTime,
                Days = first.Days
            });
        }
        return items;
    }

    private async Task TrimIfDueAsync()
    {
        lock (_tracker.TrimLock)
        {
            if (DateTime.UtcNow - _tracker.LastTrimUtc < TrimInterval) return;
            _tracker.LastTrimUtc = DateTime.UtcNow;
        }

        try
        {
            var cutoff = DateTime.UtcNow - RetentionPeriod;
            var removed = await _db.TblContentManifestLogs
                .Where(l => l.EffectiveFromUtc < cutoff)
                .ExecuteDeleteAsync();
            if (removed > 0)
                _logger.LogInformation("Content manifest log retention removed {Count} rows", removed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Content manifest log retention trim failed");
        }
    }
}
