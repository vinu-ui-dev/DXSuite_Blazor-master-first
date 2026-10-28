using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Shared.Services.Identity;

namespace DXSuite.Director.Services;

/// <summary>An access rule reduced to what identity decisions read.</summary>
public sealed record AccessRuleFact(int RuleId, int? UserId, int? UserGroupId, string AccessLevel);

/// <summary>
/// One identity a scoped user may upload as: a partial rule granted to the user directly ("Myself", no
/// group) or to one of the user's groups.
/// </summary>
public sealed record UploadCandidate(int RuleId, int? UserGroupId, string Label)
{
    public bool IsMyself => UserGroupId is null;
}

public enum UploadIdentityMode
{
    /// <summary>Nothing to pick: no rules apply, or full access. The upload is owned by the user alone.</summary>
    Unrestricted,

    /// <summary>Partial access through one or more rules; the upload must name one of them.</summary>
    Scoped,

    /// <summary>View access only. No identity can upload.</summary>
    NotPermitted
}

public sealed record UploadCandidates(UploadIdentityMode Mode, IReadOnlyList<UploadCandidate> Candidates)
{
    public static UploadCandidates Unrestricted { get; } = new(UploadIdentityMode.Unrestricted, []);
}

/// <param name="Identity">The matched identity, or null for an unrestricted upload.</param>
public sealed record UploadIdentityValidation(
    bool IsValid, UploadCandidate? Identity, UploadCandidates Candidates, string? Error);

/// <summary>
/// Everything a creation site needs to write an upload, decided before the asset row exists.
/// </summary>
/// <param name="OwnerUserId">Null only when no user is behind the upload.</param>
/// <param name="PickedRuleId">The rule behind the pick, for approval routing. Null for an unrestricted upload.</param>
/// <param name="Tags">The tags to write, one entry per key.</param>
public sealed record PreparedUpload(
    bool IsValid,
    string? Error,
    UploadCandidates Candidates,
    int? OwnerUserId,
    int? OwnerGroupId,
    int? PickedRuleId,
    Dictionary<string, List<string>> Tags);

/// <summary>What the upload form renders from, loaded once when it opens.</summary>
/// <param name="Rules">The location's participating keys, whether or not targeting is switched on.</param>
/// <param name="IsLibrary">An Asset Library, where a missing value affects visibility rather than players.</param>
public sealed record UploadFormContext(
    UploadCandidates Candidates,
    LocationTargeting Targeting,
    IReadOnlyList<MetadataKeyRule> Rules,
    IReadOnlyDictionary<string, MetadataDefinitionDto> Definitions,
    bool IsLibrary);

/// <summary>
/// The decisions behind "Uploading as", pure over rule facts and values so the picker and the write-time
/// guard can never disagree about what is allowed.
/// </summary>
public static class UploadIdentityRules
{
    public const string MyselfLabel = "Myself";

    /// <summary>
    /// Who the user may upload as at a location. Access resolves highest level wins, so any full rule
    /// makes the upload unrestricted and is never prompted. A user no rule matches keeps the legacy open
    /// behaviour and is unrestricted too.
    /// </summary>
    public static UploadCandidates Derive(
        int userId,
        bool isAdmin,
        IReadOnlyCollection<AccessRuleFact> locationRules,
        IReadOnlySet<int> memberGroupIds,
        IReadOnlyDictionary<int, string> groupNames)
    {
        if (isAdmin) return UploadCandidates.Unrestricted;

        var matching = locationRules
            .Where(r => r.UserId == userId || (r.UserGroupId is int g && memberGroupIds.Contains(g)))
            .ToList();

        if (matching.Count == 0 || matching.Any(r => r.AccessLevel == "full"))
            return UploadCandidates.Unrestricted;

        var partial = matching.Where(r => r.AccessLevel == "partial").ToList();
        if (partial.Count == 0)
            return new UploadCandidates(UploadIdentityMode.NotPermitted, []);

        var candidates = partial
            .Select(r => r.UserGroupId is int g
                ? new UploadCandidate(r.RuleId, g, groupNames.TryGetValue(g, out var name) ? name : $"Group {g}")
                : new UploadCandidate(r.RuleId, null, MyselfLabel))
            .OrderBy(c => c.IsMyself ? 0 : 1)
            .ThenBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new UploadCandidates(UploadIdentityMode.Scoped, candidates);
    }

    /// <summary>
    /// Whether <paramref name="pickedGroupId"/> (null meaning Myself) is an identity the user holds now.
    /// Matching is by identity, not rule id, so an administrator re-creating an equivalent rule does not
    /// break an upload already in flight.
    /// </summary>
    public static UploadIdentityValidation Validate(UploadCandidates candidates, int? pickedGroupId)
    {
        switch (candidates.Mode)
        {
            case UploadIdentityMode.Unrestricted:
                return pickedGroupId is null
                    ? new(true, null, candidates, null)
                    : new(false, null, candidates, "Uploading as a group applies only to partial access at this location.");

            case UploadIdentityMode.NotPermitted:
                return new(false, null, candidates, "View access does not permit uploading at this location.");

            default:
                var identity = candidates.Candidates.FirstOrDefault(c => c.UserGroupId == pickedGroupId);
                if (identity != null)
                    return new(true, identity, candidates, null);

                return new(false, null, candidates, pickedGroupId is null
                    ? "Choose which identity to upload as."
                    : "You can no longer upload as that group at this location.");
        }
    }

    /// <summary>
    /// The tags an identity supplies: its values for the location's participating keys only, since a tag on
    /// a key the location does not declare would activate silently the day someone adds the key. A
    /// multi-select key takes every value; any other type takes one, the first in a stable sort.
    /// </summary>
    /// <param name="fieldTypes">Registry field type by key; a key missing here is treated as single-valued.</param>
    public static Dictionary<string, List<string>> ValuesFor(
        IEnumerable<string> participatingKeys,
        IReadOnlyDictionary<string, string> fieldTypes,
        IReadOnlyDictionary<string, IReadOnlySet<string>> identityValues)
    {
        var result = new Dictionary<string, List<string>>(MetadataEvaluator.KeyComparer);
        foreach (var key in participatingKeys)
        {
            if (!identityValues.TryGetValue(key, out var values) || values.Count == 0)
                continue;

            var sorted = values.Order(StringComparer.OrdinalIgnoreCase).ThenBy(v => v, StringComparer.Ordinal).ToList();
            var isMulti = fieldTypes.TryGetValue(key, out var type) && type == MetadataFieldTypes.MultiSelect;
            result[key] = isMulti ? sorted : [sorted[0]];
        }
        return result;
    }
}

/// <summary>
/// Owns both directions of "Uploading as": the candidates a picker offers, the guard that re-checks a pick
/// at write time, and the tags the picked identity supplies. One service, so a validator can never reject
/// what the picker offered.
/// </summary>
public interface IUploadIdentityService
{
    /// <summary>Computed on demand, never at login: most accessible locations are never uploaded to.</summary>
    Task<UploadCandidates> GetCandidatesAsync(int userId, int locationId, CancellationToken ct = default);

    /// <summary>
    /// Re-resolves the pick against current rules and membership. A failure means the write must not
    /// happen; falling back to no group would silently un-scope the upload.
    /// </summary>
    /// <param name="pickedGroupId">The group picked, or null for Myself (or for an unrestricted upload).</param>
    Task<UploadIdentityValidation> ValidateAsync(int userId, int locationId, int? pickedGroupId, CancellationToken ct = default);

    /// <summary>The identity's values for the location's participating keys.</summary>
    Task<Dictionary<string, List<string>>> GetValuesAsync(int userId, int locationId, UploadCandidate identity, CancellationToken ct = default);

    /// <summary>Every value the identity holds, by key.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> GetHeldValuesAsync(int userId, UploadCandidate identity, CancellationToken ct = default);

    Task<UploadFormContext> GetFormContextAsync(int userId, int locationId, CancellationToken ct = default);

    /// <summary>
    /// The write-time guard every creation site calls before adding the asset row: validates the pick and
    /// settles owner, approval rule and tags. For a scoped upload, <paramref name="picks"/> is constrained to
    /// what the identity holds now; for an unrestricted one it is written as picked.
    /// </summary>
    /// <param name="userId">0 when no user is behind the upload, which leaves it unowned and untagged by identity.</param>
    Task<PreparedUpload> PrepareAsync(int userId, int locationId, int? pickedGroupId,
        IReadOnlyDictionary<string, List<string>>? picks, CancellationToken ct = default);

    /// <summary>
    /// How many of a location's players would receive an asset carrying <paramref name="tags"/>, or null when
    /// the location has no players or does not target by metadata.
    /// </summary>
    Task<MetadataReach?> GetReachAsync(int locationId, IReadOnlyDictionary<string, List<string>> tags, CancellationToken ct = default);
}

public class UploadIdentityService : IUploadIdentityService
{
    // Matches Roles.Role.Admin, which LocationAccessService also resolves to full access.
    private const int AdminRoleId = 1;

    // Each call reads through its own short-lived context. The upload form loads while its host modal is
    // still querying, and both would otherwise share the circuit's one DbContext, which throws on overlap.
    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly IMemoryCache _cache;
    private readonly DXSuite.Shared.Multitenancy.ITenantContext _tenantContext;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IMetadataDefinitionService _definitions;

    public UploadIdentityService(
        IDbContextFactory<DirectorDbContext> dbFactory,
        IMemoryCache cache,
        DXSuite.Shared.Multitenancy.ITenantContext tenantContext,
        ILoggerFactory loggerFactory,
        IMetadataDefinitionService definitions)
    {
        _dbFactory = dbFactory;
        _cache = cache;
        _tenantContext = tenantContext;
        _loggerFactory = loggerFactory;
        _definitions = definitions;
    }

    private LocationMetadataRuleService RulesFor(DirectorDbContext db) =>
        new(db, _cache, _tenantContext, _loggerFactory.CreateLogger<LocationMetadataRuleService>());

    public async Task<UploadCandidates> GetCandidatesAsync(int userId, int locationId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await GetCandidatesAsync(db, userId, locationId, ct);
    }

    private static async Task<UploadCandidates> GetCandidatesAsync(DirectorDbContext db, int userId, int locationId, CancellationToken ct)
    {
        var roleId = await db.TblUsers.AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => (int?)u.RoleId)
            .FirstOrDefaultAsync(ct);

        var rules = await db.TblLocationAccessRules.AsNoTracking()
            .Where(r => r.LocationId == locationId)
            .Select(r => new AccessRuleFact(r.LocationAccessRuleId, r.UserId, r.UserGroupId, r.AccessLevel))
            .ToListAsync(ct);

        var memberGroupIds = rules.Count == 0
            ? new HashSet<int>()
            : (await db.TblUserGroupMemberships.AsNoTracking()
                .Where(m => m.UserId == userId)
                .Select(m => m.UserGroupId)
                .ToListAsync(ct)).ToHashSet();

        var ruleGroupIds = rules.Where(r => r.UserGroupId.HasValue).Select(r => r.UserGroupId!.Value).Distinct().ToList();
        var groupNames = ruleGroupIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.TblUserGroups.AsNoTracking()
                .Where(g => ruleGroupIds.Contains(g.UserGroupId))
                .ToDictionaryAsync(g => g.UserGroupId, g => g.GroupName, ct);

        return UploadIdentityRules.Derive(userId, roleId == AdminRoleId, rules, memberGroupIds, groupNames);
    }

    public async Task<UploadIdentityValidation> ValidateAsync(int userId, int locationId, int? pickedGroupId, CancellationToken ct = default) =>
        UploadIdentityRules.Validate(await GetCandidatesAsync(userId, locationId, ct), pickedGroupId);

    public async Task<Dictionary<string, List<string>>> GetValuesAsync(int userId, int locationId, UploadCandidate identity, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var targeting = await RulesFor(db).GetTargetingAsync(locationId, ct);
        var keys = targeting.Rules.Participating.Select(r => r.Key).ToList();
        if (keys.Count == 0)
            return new Dictionary<string, List<string>>(MetadataEvaluator.KeyComparer);

        return UploadIdentityRules.ValuesFor(keys, await GetFieldTypesAsync(), await GetHeldValuesAsync(db, userId, identity, ct));
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> GetHeldValuesAsync(int userId, UploadCandidate identity, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await GetHeldValuesAsync(db, userId, identity, ct);
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> GetHeldValuesAsync(
        DirectorDbContext db, int userId, UploadCandidate identity, CancellationToken ct)
    {
        var rows = identity.UserGroupId is int groupId
            ? await db.TblUserGroupMetadata.AsNoTracking()
                .Where(m => m.UserGroupId == groupId)
                .Select(m => new { m.MetadataKey, m.MetadataValue })
                .ToListAsync(ct)
            : await db.TblUserMetadata.AsNoTracking()
                .Where(m => m.UserId == userId)
                .Select(m => new { m.MetadataKey, m.MetadataValue })
                .ToListAsync(ct);

        return rows
            .GroupBy(r => MetadataEvaluator.NormalizeKey(r.MetadataKey), MetadataEvaluator.KeyComparer)
            .ToDictionary(
                g => g.Key,
                g => MetadataEvaluator.NormalizeValues(g.Select(r => r.MetadataValue)),
                MetadataEvaluator.KeyComparer);
    }

    public async Task<UploadFormContext> GetFormContextAsync(int userId, int locationId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var candidates = await GetCandidatesAsync(db, userId, locationId, ct);
        var targeting = await RulesFor(db).GetTargetingAsync(locationId, ct);
        var definitions = (await _definitions.GetAllDefinitionsAsync())
            .GroupBy(d => d.MetadataKey, MetadataEvaluator.KeyComparer)
            .ToDictionary(g => g.Key, g => g.First(), MetadataEvaluator.KeyComparer);
        var locationType = await db.TblLocations.AsNoTracking()
            .Where(l => l.LocationId == locationId)
            .Select(l => l.LocationType)
            .FirstOrDefaultAsync(ct);
        return new UploadFormContext(candidates, targeting, targeting.Rules.Participating, definitions,
            LocationTypeConstants.IsAssetLibrary(locationType));
    }

    public async Task<PreparedUpload> PrepareAsync(int userId, int locationId, int? pickedGroupId,
        IReadOnlyDictionary<string, List<string>>? picks, CancellationToken ct = default)
    {
        var noTags = new Dictionary<string, List<string>>(MetadataEvaluator.KeyComparer);

        if (userId <= 0)
        {
            return pickedGroupId is null
                ? new PreparedUpload(true, null, UploadCandidates.Unrestricted, null, null, null, noTags)
                : new PreparedUpload(false, "An upload with no uploading user cannot act for a group.", UploadCandidates.Unrestricted, null, null, null, noTags);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var validation = UploadIdentityRules.Validate(await GetCandidatesAsync(db, userId, locationId, ct), pickedGroupId);
        if (!validation.IsValid)
            return new PreparedUpload(false, validation.Error, validation.Candidates, null, null, null, noTags);

        if (validation.Identity is not UploadCandidate identity)
        {
            var asPicked = new Dictionary<string, List<string>>(MetadataEvaluator.KeyComparer);
            foreach (var (key, values) in picks ?? new Dictionary<string, List<string>>())
            {
                var normalized = MetadataValueSet.Normalize(values);
                if (normalized.Count > 0) asPicked[MetadataEvaluator.NormalizeKey(key)] = normalized;
            }
            return new PreparedUpload(true, null, validation.Candidates, userId, null, null, asPicked);
        }

        var targeting = await RulesFor(db).GetTargetingAsync(locationId, ct);
        var keys = targeting.Rules.Participating.Select(r => r.Key).ToList();
        var tags = keys.Count == 0
            ? noTags
            : UploadFormRules.ConstrainPicks(keys, await GetFieldTypesAsync(), await GetHeldValuesAsync(db, userId, identity, ct), picks);

        return new PreparedUpload(true, null, validation.Candidates, userId, identity.UserGroupId, identity.RuleId, tags);
    }

    public async Task<MetadataReach?> GetReachAsync(int locationId, IReadOnlyDictionary<string, List<string>> tags, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var targeting = await RulesFor(db).GetTargetingAsync(locationId, ct);
        if (!targeting.Enabled)
            return null;

        var deviceIds = await db.TblDevices.AsNoTracking()
            .Where(d => d.LocationId == locationId)
            .Select(d => d.DeviceId)
            .ToListAsync(ct);
        if (deviceIds.Count == 0)
            return null;

        var players = await new MetadataSubjectResolver(db).GetDeviceValuesAsync(deviceIds, ct);
        IReadOnlyDictionary<string, IReadOnlySet<string>> assetValues = tags.ToDictionary(
            kv => kv.Key, kv => MetadataEvaluator.NormalizeValues(kv.Value), MetadataEvaluator.KeyComparer);

        return MetadataEvaluator.Reach(targeting.Rules, assetValues, deviceIds.Select(id => players[id]));
    }

    private async Task<Dictionary<string, string>> GetFieldTypesAsync() =>
        (await _definitions.GetAllDefinitionsAsync())
            .GroupBy(d => d.MetadataKey, MetadataEvaluator.KeyComparer)
            .ToDictionary(g => g.Key, g => g.First().FieldType, MetadataEvaluator.KeyComparer);
}
