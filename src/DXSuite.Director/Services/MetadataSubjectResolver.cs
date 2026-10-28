using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

/// <param name="ApprovalStatus">Null when the asset has no approval row.</param>
public sealed record LocationAssetRecord(int AssetId, int? ApprovalStatus);

/// <summary>
/// Materializes the value sets <see cref="MetadataEvaluator"/> compares, from the database, in batches.
/// The evaluator is pure and takes values; this is the one place that reads them, so no caller issues
/// a query per asset.
/// </summary>
public interface IMetadataSubjectResolver
{
    /// <summary>
    /// The live asset rows behind a location's media files, keyed by AssetPath, with each row's approval
    /// status: everything a publication needs besides the tags, in two queries. A file whose path is
    /// absent has no asset row and is legacy content.
    /// </summary>
    Task<IReadOnlyDictionary<string, LocationAssetRecord>> GetLocationAssetRecordsAsync(int locationId, CancellationToken ct = default);

    /// <summary>
    /// Every tag each asset carries, keyed by asset. Every id passed is present; one with no tags maps to
    /// no keys. Pass only ids that have asset rows: a file with none is represented by null, not by an entry.
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlyDictionary<string, IReadOnlySet<string>>>> GetAssetValuesAsync(
        IReadOnlyCollection<int> assetIds, CancellationToken ct = default);

    /// <summary>A player's values, keyed by device. A device holds at most one value per key.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlySet<string>>>> GetDeviceValuesAsync(
        IReadOnlyCollection<string> deviceIds, CancellationToken ct = default);

    /// <summary>
    /// A user's scope subject: her own values unioned with those of every group she currently belongs to.
    /// Membership is read on every call, never snapshotted.
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> GetUserValuesAsync(int userId, CancellationToken ct = default);
}

public class MetadataSubjectResolver : IMetadataSubjectResolver
{
    // Well under SQL Server's 2,100 parameter limit for an IN list.
    private const int BatchSize = 1000;

    private readonly IDirectorDBContext _db;

    public MetadataSubjectResolver(IDirectorDBContext db)
    {
        _db = db;
    }

    /// <summary>
    /// A player's subject from the single-valued metadata the poll path already holds.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> DeviceSubject(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
            return new Dictionary<string, IReadOnlySet<string>>(MetadataEvaluator.KeyComparer);

        return GroupValues(metadata.Select(kv => (kv.Key, kv.Value)));
    }

    public async Task<IReadOnlyDictionary<string, LocationAssetRecord>> GetLocationAssetRecordsAsync(int locationId, CancellationToken ct = default)
    {
        var assets = await _db.TblAssets.AsNoTracking()
            .Where(a => a.LocationId == locationId && !a.IsDeleted)
            .Select(a => new { a.AssetId, a.AssetPath })
            .ToListAsync(ct);

        var ids = assets.Select(a => a.AssetId).ToList();
        var approvals = new Dictionary<int, int>();
        foreach (var chunk in ids.Chunk(BatchSize))
        {
            // An asset can carry more than one approval row over its life; the newest is its status.
            var batch = await _db.TblAssetApprovals.AsNoTracking()
                .Where(a => chunk.Contains(a.AssetId))
                .Select(a => new { a.AssetId, a.AssetApprovalId, a.ApprovalStatus })
                .ToListAsync(ct);
            foreach (var row in batch.GroupBy(r => r.AssetId))
                approvals[row.Key] = row.MaxBy(r => r.AssetApprovalId)!.ApprovalStatus;
        }

        // AssetPath compares case-insensitively in the database; a path held by two live rows resolves
        // to the newer one.
        return assets
            .Where(a => !string.IsNullOrEmpty(a.AssetPath))
            .GroupBy(a => a.AssetPath!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var assetId = g.Max(a => a.AssetId);
                    return new LocationAssetRecord(assetId, approvals.TryGetValue(assetId, out var status) ? status : null);
                },
                StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyDictionary<string, IReadOnlySet<string>>>> GetAssetValuesAsync(
        IReadOnlyCollection<int> assetIds, CancellationToken ct = default)
    {
        var ids = assetIds.Distinct().ToList();
        var rows = new List<(int AssetId, string Key, string Value)>();

        foreach (var chunk in ids.Chunk(BatchSize))
        {
            var batch = await _db.TblAssetContentMetadata.AsNoTracking()
                .Where(t => chunk.Contains(t.AssetId))
                .Select(t => new { t.AssetId, t.MetadataKey, t.MetadataValue })
                .ToListAsync(ct);
            rows.AddRange(batch.Select(t => (t.AssetId, t.MetadataKey, t.MetadataValue)));
        }

        var byAsset = rows.ToLookup(r => r.AssetId);
        return ids.ToDictionary(id => id, id => GroupValues(byAsset[id].Select(r => (r.Key, r.Value))));
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlySet<string>>>> GetDeviceValuesAsync(
        IReadOnlyCollection<string> deviceIds, CancellationToken ct = default)
    {
        var ids = deviceIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var rows = new List<(string DeviceId, string Key, string Value)>();

        foreach (var chunk in ids.Chunk(BatchSize))
        {
            var batch = await _db.TblDeviceMetadata.AsNoTracking()
                .Where(m => chunk.Contains(m.DeviceId))
                .Select(m => new { m.DeviceId, m.MetadataKey, m.MetadataValue })
                .ToListAsync(ct);
            rows.AddRange(batch.Select(m => (m.DeviceId, m.MetadataKey, m.MetadataValue)));
        }

        var byDevice = rows.ToLookup(r => r.DeviceId, StringComparer.OrdinalIgnoreCase);
        return ids.ToDictionary(
            id => id,
            id => GroupValues(byDevice[id].Select(r => (r.Key, r.Value))),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> GetUserValuesAsync(int userId, CancellationToken ct = default)
    {
        var own = await _db.TblUserMetadata.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new { m.MetadataKey, m.MetadataValue })
            .ToListAsync(ct);

        var fromGroups = await (
                from membership in _db.TblUserGroupMemberships.AsNoTracking()
                join value in _db.TblUserGroupMetadata.AsNoTracking() on membership.UserGroupId equals value.UserGroupId
                where membership.UserId == userId
                select new { value.MetadataKey, value.MetadataValue })
            .ToListAsync(ct);

        return GroupValues(own.Concat(fromGroups).Select(m => (m.MetadataKey, m.MetadataValue)));
    }

    /// <summary>Key and value rows as a value set per key, normalized the way the evaluator compares them.</summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> GroupValues(IEnumerable<(string Key, string Value)> rows) =>
        rows
            .Select(r => (Key: MetadataEvaluator.NormalizeKey(r.Key), r.Value))
            .Where(r => r.Key.Length > 0)
            .GroupBy(r => r.Key, MetadataEvaluator.KeyComparer)
            .ToDictionary(
                g => g.Key,
                g => MetadataEvaluator.NormalizeValues(g.Select(r => r.Value)),
                MetadataEvaluator.KeyComparer);
}
