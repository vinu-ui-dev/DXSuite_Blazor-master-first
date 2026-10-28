using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using DXSuite.Director.Entities;
using DXSuite.Shared.Authorization;

namespace DXSuite.Director.Services;

/// <summary>
/// Loads what <see cref="ContentScope"/> decides over: a location's rule set, a user's subject values and
/// asset tags. No cache dependency on access rules, which no longer decide what a device receives.
/// </summary>
public interface IContentScopeService
{
    /// <summary>
    /// The scope for one user at one location. <paramref name="access"/> is what the caller already resolved
    /// (the session's access map, or the API caller); the subject is read only when scope is evaluated, and
    /// group membership is read live.
    /// </summary>
    Task<ContentScopeContext> GetAsync(int userId, int locationId, ResolvedAccess? access, CancellationToken ct = default);

    /// <summary>Every tag each asset carries, keyed by asset. Every id passed is present.</summary>
    Task<IReadOnlyDictionary<int, IReadOnlyDictionary<string, IReadOnlySet<string>>>> GetAssetValuesAsync(
        IReadOnlyCollection<int> assetIds, CancellationToken ct = default);

    /// <summary>
    /// Whether a rule about to be saved would scope its holder to nothing. A user rule's subject is the user
    /// with her current groups; a group rule's subject is the group's own values.
    /// </summary>
    Task<bool> ResolvesToNothingAsync(int locationId, int? userId, int? userGroupId, string accessLevel, string? viewScope,
        CancellationToken ct = default);
}

public class ContentScopeService : IContentScopeService
{
    // Each call reads through its own short-lived context: pages load scope while other components on the
    // circuit are still querying, and the circuit's one DbContext throws on overlap.
    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly IMemoryCache _cache;
    private readonly DXSuite.Shared.Multitenancy.ITenantContext _tenantContext;
    private readonly ILoggerFactory _loggerFactory;

    public ContentScopeService(
        IDbContextFactory<DirectorDbContext> dbFactory,
        IMemoryCache cache,
        DXSuite.Shared.Multitenancy.ITenantContext tenantContext,
        ILoggerFactory loggerFactory)
    {
        _dbFactory = dbFactory;
        _cache = cache;
        _tenantContext = tenantContext;
        _loggerFactory = loggerFactory;
    }

    private LocationMetadataRuleService RulesFor(DirectorDbContext db) =>
        new(db, _cache, _tenantContext, _loggerFactory.CreateLogger<LocationMetadataRuleService>());

    public async Task<ContentScopeContext> GetAsync(int userId, int locationId, ResolvedAccess? access, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rules = (await RulesFor(db).GetTargetingAsync(locationId, ct)).Rules;

        if (!ContentScope.IsEvaluated(access))
            return new ContentScopeContext(access, rules, null);

        var subject = await new MetadataSubjectResolver(db).GetUserValuesAsync(userId, ct);
        return new ContentScopeContext(access, rules, subject);
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyDictionary<string, IReadOnlySet<string>>>> GetAssetValuesAsync(
        IReadOnlyCollection<int> assetIds, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await new MetadataSubjectResolver(db).GetAssetValuesAsync(assetIds, ct);
    }

    public async Task<bool> ResolvesToNothingAsync(int locationId, int? userId, int? userGroupId, string accessLevel, string? viewScope,
        CancellationToken ct = default)
    {
        if (!ContentScope.IsEvaluated(accessLevel, viewScope))
            return false;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rules = (await RulesFor(db).GetTargetingAsync(locationId, ct)).Rules;

        IReadOnlyDictionary<string, IReadOnlySet<string>> subject;
        if (userId is int user)
        {
            subject = await new MetadataSubjectResolver(db).GetUserValuesAsync(user, ct);
        }
        else
        {
            var rows = await db.TblUserGroupMetadata.AsNoTracking()
                .Where(m => m.UserGroupId == userGroupId)
                .Select(m => new { m.MetadataKey, m.MetadataValue })
                .ToListAsync(ct);
            subject = MetadataSubjectResolver.GroupValues(rows.Select(r => (r.MetadataKey, r.MetadataValue)));
        }

        return ContentScope.ResolvesToNothing(accessLevel, viewScope, rules, subject);
    }
}
