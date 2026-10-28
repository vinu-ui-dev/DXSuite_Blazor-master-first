using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Shared.Services.Identity;

public class UserGroupMetadataService : IUserGroupMetadataService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UserGroupMetadataService> _logger;

    private static readonly List<string> CommonKeys = new()
    {
        "BuildingNo",
        "Department",
        "EmailDomain",
        "Region"
    };

    public UserGroupMetadataService(IServiceScopeFactory scopeFactory, ILogger<UserGroupMetadataService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<Dictionary<string, List<string>>> GetMetadataAsync(int userGroupId)
    {
        if (userGroupId <= 0) return new Dictionary<string, List<string>>();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var rows = await db.TblUserGroupMetadata
            .Where(m => m.UserGroupId == userGroupId)
            .OrderBy(m => m.UserGroupMetadataId)
            .Select(m => new { m.MetadataKey, m.MetadataValue })
            .ToListAsync();

        return rows
            .GroupBy(m => m.MetadataKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(m => m.MetadataValue).ToList(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<List<string>> GetValuesAsync(int userGroupId, string key)
    {
        if (userGroupId <= 0 || string.IsNullOrWhiteSpace(key)) return new List<string>();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        return await db.TblUserGroupMetadata
            .Where(m => m.UserGroupId == userGroupId && m.MetadataKey == key)
            .OrderBy(m => m.UserGroupMetadataId)
            .Select(m => m.MetadataValue)
            .ToListAsync();
    }

    public async Task<List<TblUserGroupMetadata>> GetMetadataRecordsAsync(int userGroupId)
    {
        if (userGroupId <= 0) return new List<TblUserGroupMetadata>();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        return await db.TblUserGroupMetadata
            .Where(m => m.UserGroupId == userGroupId)
            .OrderBy(m => m.MetadataKey)
            .ToListAsync();
    }

    public async Task SetMetadataAsync(int userGroupId, string key, IEnumerable<string> values)
    {
        if (userGroupId <= 0)
            throw new ArgumentException("User Group ID is required", nameof(userGroupId));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Metadata key is required", nameof(key));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var existing = await db.TblUserGroupMetadata
            .Where(m => m.UserGroupId == userGroupId && m.MetadataKey == key)
            .OrderBy(m => m.UserGroupMetadataId)
            .ToListAsync();

        var plan = MetadataValueSet.Reconcile(existing.Select(m => m.MetadataValue).ToList(), values);
        if (plan.IsEmpty) return;

        foreach (var (index, value) in plan.Updates)
            existing[index].MetadataValue = value;
        foreach (var index in plan.Removes)
            db.TblUserGroupMetadata.Remove(existing[index]);
        foreach (var value in plan.Adds)
        {
            db.TblUserGroupMetadata.Add(new TblUserGroupMetadata
            {
                UserGroupId = userGroupId,
                MetadataKey = key,
                MetadataValue = value,
                CreateDate = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        _logger.LogDebug("Set metadata for group {GroupId}: {Key} ({Added} added, {Updated} updated, {Removed} removed)",
            userGroupId, key, plan.Adds.Count, plan.Updates.Count, plan.Removes.Count);
    }

    public async Task DeleteMetadataAsync(int userGroupId, string key)
    {
        if (userGroupId <= 0 || string.IsNullOrWhiteSpace(key)) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var existing = await db.TblUserGroupMetadata
            .Where(m => m.UserGroupId == userGroupId && m.MetadataKey == key)
            .ToListAsync();

        if (existing.Count > 0)
        {
            db.TblUserGroupMetadata.RemoveRange(existing);
            await db.SaveChangesAsync();
            _logger.LogDebug("Deleted metadata for group {GroupId}: {Key}", userGroupId, key);
        }
    }

    public async Task DeleteMetadataByIdAsync(int userGroupMetadataId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var existing = await db.TblUserGroupMetadata.FindAsync(userGroupMetadataId);

        if (existing != null)
        {
            db.TblUserGroupMetadata.Remove(existing);
            await db.SaveChangesAsync();
            _logger.LogDebug("Deleted metadata ID {MetadataId} for group {GroupId}", userGroupMetadataId, existing.UserGroupId);
        }
    }

    public async Task<List<string>> GetKnownKeysAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var dbKeys = await db.TblUserGroupMetadata
            .Select(m => m.MetadataKey)
            .Distinct()
            .ToListAsync();

        return CommonKeys
            .Union(dbKeys)
            .Distinct()
            .OrderBy(k => k)
            .ToList();
    }
}
