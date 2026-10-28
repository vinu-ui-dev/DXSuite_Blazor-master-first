using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using DXSuite.Director.Entities;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Director.Services;

/// <summary>
/// What content targeting needs to know about one location, read once per location rather than once
/// per poll.
/// </summary>
/// <param name="Enabled">The location's "Target content by metadata" switch. It gates targeting only.</param>
public sealed record LocationTargeting(bool Enabled, LocationMetadataRules Rules)
{
    public static LocationTargeting Off { get; } =
        new(false, new LocationMetadataRules(MetadataCombineMode.All, [], []));
}

/// <summary>One participating key as the rule builder saves it. Policies use their stored spellings.</summary>
public sealed record LocationMetadataRuleInput(
    string MetadataKey,
    string UntaggedContentPolicy = "all",
    string UntaggedPlayerPolicy = "all");

public interface ILocationMetadataRuleService
{
    /// <summary>
    /// The location's switch, match mode and participating keys, cached per location. A rule edit through
    /// <see cref="SaveAsync"/> is visible on the next read; a registry edit within the cache lifetime.
    /// </summary>
    Task<LocationTargeting> GetTargetingAsync(int locationId, CancellationToken ct = default);

    /// <summary>The stored rule rows, uncached, for editing.</summary>
    Task<IReadOnlyList<TblLocationMetadataRule>> GetRulesAsync(int locationId, CancellationToken ct = default);

    /// <summary>
    /// Replaces the location's rule set with <paramref name="rules"/> and sets its switch and match mode.
    /// Removing a key deletes only its rule row: asset tags for that key are kept, so adding the key back
    /// restores the targeting it had.
    /// </summary>
    Task SaveAsync(int locationId, bool targetContentByMetadata, string matchMode,
        IReadOnlyCollection<LocationMetadataRuleInput> rules, string editUser, CancellationToken ct = default);

    void InvalidateLocation(int locationId);
}

public class LocationMetadataRuleService : ILocationMetadataRuleService
{
    // Rule edits invalidate explicitly. The lifetime bounds how long a registry edit takes to change
    // participation, and matches how stale a player's own metadata may already be on the poll path.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private static readonly HashSet<string> MatchModes = new(StringComparer.Ordinal) { "all", "any" };
    private static readonly HashSet<string> ContentPolicies = new(StringComparer.Ordinal) { "all", "none" };
    private static readonly HashSet<string> PlayerPolicies = new(StringComparer.Ordinal) { "all", "untaggedOnly", "none" };

    private readonly IDirectorDBContext _db;
    private readonly IMemoryCache _cache;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<LocationMetadataRuleService> _logger;

    public LocationMetadataRuleService(
        IDirectorDBContext db,
        IMemoryCache cache,
        ITenantContext tenantContext,
        ILogger<LocationMetadataRuleService> logger)
    {
        _db = db;
        _cache = cache;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<LocationTargeting> GetTargetingAsync(int locationId, CancellationToken ct = default)
    {
        var cacheKey = CacheKey(locationId);
        if (_cache.TryGetValue(cacheKey, out LocationTargeting? cached) && cached != null)
            return cached;

        var location = await _db.TblLocations.AsNoTracking()
            .Where(l => l.LocationId == locationId)
            .Select(l => new { l.TargetContentByMetadata, l.MetadataMatchMode })
            .FirstOrDefaultAsync(ct);

        var targeting = LocationTargeting.Off;
        if (location != null)
        {
            var rows = await _db.TblLocationMetadataRules.AsNoTracking()
                .Where(r => r.LocationId == locationId)
                .Select(r => new { r.MetadataKey, r.UntaggedContentPolicy, r.UntaggedPlayerPolicy })
                .ToListAsync(ct);

            var registryKeys = rows.Count == 0
                ? new List<string>()
                : await _db.TblMetadataDefinitions.AsNoTracking().Select(d => d.MetadataKey).ToListAsync(ct);

            var rules = new LocationMetadataRules(
                ParseMatchMode(location.MetadataMatchMode, locationId),
                rows.Select(r => new MetadataKeyRule(
                    r.MetadataKey,
                    ParseContentPolicy(r.UntaggedContentPolicy, locationId, r.MetadataKey),
                    ParsePlayerPolicy(r.UntaggedPlayerPolicy, locationId, r.MetadataKey))),
                registryKeys);

            targeting = new LocationTargeting(location.TargetContentByMetadata, rules);
        }

        _cache.Set(cacheKey, targeting, CacheTtl);
        return targeting;
    }

    public async Task<IReadOnlyList<TblLocationMetadataRule>> GetRulesAsync(int locationId, CancellationToken ct = default) =>
        await _db.TblLocationMetadataRules.AsNoTracking()
            .Where(r => r.LocationId == locationId)
            .OrderBy(r => r.MetadataKey)
            .ToListAsync(ct);

    public async Task SaveAsync(int locationId, bool targetContentByMetadata, string matchMode,
        IReadOnlyCollection<LocationMetadataRuleInput> rules, string editUser, CancellationToken ct = default)
    {
        if (!MatchModes.Contains(matchMode))
            throw new ArgumentException($"Unknown metadata match mode '{matchMode}'.", nameof(matchMode));

        var incoming = rules
            .Select(r => r with { MetadataKey = MetadataEvaluator.NormalizeKey(r.MetadataKey) })
            .Where(r => r.MetadataKey.Length > 0)
            .ToList();

        foreach (var rule in incoming)
        {
            if (!ContentPolicies.Contains(rule.UntaggedContentPolicy))
                throw new ArgumentException($"Unknown untagged content policy '{rule.UntaggedContentPolicy}' for key '{rule.MetadataKey}'.", nameof(rules));
            if (!PlayerPolicies.Contains(rule.UntaggedPlayerPolicy))
                throw new ArgumentException($"Unknown untagged player policy '{rule.UntaggedPlayerPolicy}' for key '{rule.MetadataKey}'.", nameof(rules));
        }

        var duplicate = incoming.GroupBy(r => r.MetadataKey, MetadataEvaluator.KeyComparer).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            throw new ArgumentException($"Metadata key '{duplicate.Key}' appears more than once.", nameof(rules));

        var location = await _db.TblLocations.FirstOrDefaultAsync(l => l.LocationId == locationId, ct)
            ?? throw new InvalidOperationException($"Location {locationId} not found.");

        location.TargetContentByMetadata = targetContentByMetadata;
        location.MetadataMatchMode = matchMode;

        var existing = await _db.TblLocationMetadataRules
            .Where(r => r.LocationId == locationId)
            .ToListAsync(ct);
        var byKey = existing.ToDictionary(r => MetadataEvaluator.NormalizeKey(r.MetadataKey), MetadataEvaluator.KeyComparer);
        var now = DateTime.UtcNow;

        foreach (var removed in existing.Where(r => !incoming.Any(i => MetadataEvaluator.KeyComparer.Equals(i.MetadataKey, MetadataEvaluator.NormalizeKey(r.MetadataKey)))))
            _db.TblLocationMetadataRules.Remove(removed);

        foreach (var rule in incoming)
        {
            if (byKey.TryGetValue(rule.MetadataKey, out var row))
            {
                if (row.UntaggedContentPolicy == rule.UntaggedContentPolicy && row.UntaggedPlayerPolicy == rule.UntaggedPlayerPolicy)
                    continue;

                row.UntaggedContentPolicy = rule.UntaggedContentPolicy;
                row.UntaggedPlayerPolicy = rule.UntaggedPlayerPolicy;
                row.LastEditDate = now;
                row.LastEditUser = editUser;
            }
            else
            {
                _db.TblLocationMetadataRules.Add(new TblLocationMetadataRule
                {
                    LocationId = locationId,
                    MetadataKey = rule.MetadataKey,
                    UntaggedContentPolicy = rule.UntaggedContentPolicy,
                    UntaggedPlayerPolicy = rule.UntaggedPlayerPolicy,
                    CreateDate = now,
                    LastEditUser = editUser
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        InvalidateLocation(locationId);
    }

    public void InvalidateLocation(int locationId) => _cache.Remove(CacheKey(locationId));

    private string CacheKey(int locationId) => $"metadatarules:{_tenantContext.TenantId}:{locationId}";

    // Stored values are validated on write, so anything else was written around this service. Each
    // falls back to the value that preserves pre-targeting behaviour, loudly.
    private MetadataCombineMode ParseMatchMode(string? stored, int locationId)
    {
        if (stored == "any") return MetadataCombineMode.Any;
        if (stored != "all")
            _logger.LogWarning("Location {LocationId} has unknown metadata match mode {MatchMode}; using all", locationId, stored);
        return MetadataCombineMode.All;
    }

    private UntaggedContentPolicy ParseContentPolicy(string? stored, int locationId, string key)
    {
        if (stored == "none") return UntaggedContentPolicy.None;
        if (stored != "all")
            _logger.LogWarning("Location {LocationId} key {MetadataKey} has unknown untagged content policy {Policy}; using all", locationId, key, stored);
        return UntaggedContentPolicy.All;
    }

    private UntaggedPlayerPolicy ParsePlayerPolicy(string? stored, int locationId, string key)
    {
        if (stored == "none") return UntaggedPlayerPolicy.None;
        if (stored == "untaggedOnly") return UntaggedPlayerPolicy.UntaggedOnly;
        if (stored != "all")
            _logger.LogWarning("Location {LocationId} key {MetadataKey} has unknown untagged player policy {Policy}; using all", locationId, key, stored);
        return UntaggedPlayerPolicy.All;
    }
}
