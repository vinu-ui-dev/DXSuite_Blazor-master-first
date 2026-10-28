using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Shared.Services.Identity;

public class UserMetadataService : IUserMetadataService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UserMetadataService> _logger;

    private static readonly List<string> CommonKeys = new()
    {
        "BuildingNo",
        "Region",
        "Department"
    };

    public UserMetadataService(IServiceScopeFactory scopeFactory, ILogger<UserMetadataService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<Dictionary<string, List<string>>> GetMetadataAsync(int userId)
    {
        if (userId <= 0) return new Dictionary<string, List<string>>();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var rows = await db.TblUserMetadata
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.UserMetadataId)
            .Select(m => new { m.MetadataKey, m.MetadataValue })
            .ToListAsync();

        return rows
            .GroupBy(m => m.MetadataKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(m => m.MetadataValue).ToList(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<List<string>> GetValuesAsync(int userId, string key)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(key)) return new List<string>();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        return await db.TblUserMetadata
            .Where(m => m.UserId == userId && m.MetadataKey == key)
            .OrderBy(m => m.UserMetadataId)
            .Select(m => m.MetadataValue)
            .ToListAsync();
    }

    public async Task<List<TblUserMetadata>> GetMetadataRecordsAsync(int userId)
    {
        if (userId <= 0) return new List<TblUserMetadata>();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        return await db.TblUserMetadata
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.MetadataKey)
            .ToListAsync();
    }

    public async Task SetMetadataAsync(int userId, string key, IEnumerable<string> values)
    {
        if (userId <= 0)
            throw new ArgumentException("User ID is required", nameof(userId));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Metadata key is required", nameof(key));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var existing = await db.TblUserMetadata
            .Where(m => m.UserId == userId && m.MetadataKey == key)
            .OrderBy(m => m.UserMetadataId)
            .ToListAsync();

        var plan = MetadataValueSet.Reconcile(existing.Select(m => m.MetadataValue).ToList(), values);
        if (plan.IsEmpty) return;

        foreach (var (index, value) in plan.Updates)
            existing[index].MetadataValue = value;
        foreach (var index in plan.Removes)
            db.TblUserMetadata.Remove(existing[index]);
        foreach (var value in plan.Adds)
        {
            db.TblUserMetadata.Add(new TblUserMetadata
            {
                UserId = userId,
                MetadataKey = key,
                MetadataValue = value,
                CreateDate = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        _logger.LogDebug("Set metadata for user {UserId}: {Key} ({Added} added, {Updated} updated, {Removed} removed)",
            userId, key, plan.Adds.Count, plan.Updates.Count, plan.Removes.Count);
    }

    public async Task DeleteMetadataAsync(int userId, string key)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(key)) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var existing = await db.TblUserMetadata
            .Where(m => m.UserId == userId && m.MetadataKey == key)
            .ToListAsync();

        if (existing.Count > 0)
        {
            db.TblUserMetadata.RemoveRange(existing);
            await db.SaveChangesAsync();
            _logger.LogDebug("Deleted metadata for user {UserId}: {Key}", userId, key);
        }
    }

    public async Task DeleteMetadataByIdAsync(int userMetadataId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var existing = await db.TblUserMetadata.FindAsync(userMetadataId);

        if (existing != null)
        {
            db.TblUserMetadata.Remove(existing);
            await db.SaveChangesAsync();
            _logger.LogDebug("Deleted metadata ID {MetadataId} for user {UserId}", userMetadataId, existing.UserId);
        }
    }

    public async Task<List<string>> GetKnownKeysAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var dbKeys = await db.TblUserMetadata
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
