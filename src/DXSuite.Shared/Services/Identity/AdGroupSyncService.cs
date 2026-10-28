using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Models;

namespace DXSuite.Shared.Services.Identity;

public class AdGroupSyncService : IAdGroupSyncService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<FeatureFlags> _featureFlags;
    private readonly ILogger<AdGroupSyncService> _logger;

    public AdGroupSyncService(
        IServiceScopeFactory scopeFactory,
        IOptions<FeatureFlags> featureFlags,
        ILogger<AdGroupSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _featureFlags = featureFlags;
        _logger = logger;
    }

    public async Task SyncMembershipAsync(int userId, IReadOnlyList<string> tokenGroupOids)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        // Load all active ad-sync groups that have an ExternalId
        var adSyncGroups = await db.TblUserGroups
            .Where(g => g.GroupType == "ad-sync" && g.IsActive && g.ExternalId != null)
            .Select(g => new { g.UserGroupId, g.ExternalId })
            .ToListAsync();

        if (adSyncGroups.Count == 0) return;

        var tokenOidSet = new HashSet<string>(tokenGroupOids, StringComparer.OrdinalIgnoreCase);

        foreach (var group in adSyncGroups)
        {
            var userIsInAdGroup = tokenOidSet.Contains(group.ExternalId!);

            var existingMembership = await db.TblUserGroupMemberships
                .FirstOrDefaultAsync(m => m.UserGroupId == group.UserGroupId && m.UserId == userId);

            if (userIsInAdGroup && existingMembership == null)
            {
                // User is in the AD group but not in Director — add membership
                try
                {
                    db.TblUserGroupMemberships.Add(new TblUserGroupMembership
                    {
                        UserGroupId = group.UserGroupId,
                        UserId = userId,
                        AddedDate = DateTime.UtcNow
                    });
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true
                                                    || ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // Concurrent login race — membership already exists
                    _logger.LogDebug("Duplicate membership detected for user {UserId} in group {GroupId}, skipping", userId, group.UserGroupId);
                }
            }
            else if (!userIsInAdGroup && existingMembership != null)
            {
                // User is NOT in the AD group but has Director membership — remove
                db.TblUserGroupMemberships.Remove(existingMembership);
                await db.SaveChangesAsync();
            }
        }
    }

    public async Task DiscoverGroupsAsync(IReadOnlyList<string> tokenGroupOids, GraphServiceClient? graphClient)
    {
        if (!_featureFlags.Value.GroupDiscovery) return;
        if (tokenGroupOids.Count == 0) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        // Load all known OIDs (discovered + adopted ad-sync groups)
        var knownDiscoveredOids = await db.TblDiscoveredAdGroups
            .Select(d => d.ObjectId)
            .ToListAsync();

        var adoptedOids = await db.TblUserGroups
            .Where(g => g.GroupType == "ad-sync" && g.ExternalId != null)
            .Select(g => g.ExternalId!)
            .ToListAsync();

        var knownOidSet = new HashSet<string>(knownDiscoveredOids.Concat(adoptedOids), StringComparer.OrdinalIgnoreCase);

        var newOids = tokenGroupOids.Where(oid => !knownOidSet.Contains(oid)).ToList();
        var existingOids = tokenGroupOids.Where(oid => knownDiscoveredOids.Contains(oid, StringComparer.OrdinalIgnoreCase)).ToList();

        // Update LastSeenDate for known discovered OIDs
        if (existingOids.Count > 0)
        {
            var existingRecords = await db.TblDiscoveredAdGroups
                .Where(d => existingOids.Contains(d.ObjectId))
                .ToListAsync();

            foreach (var record in existingRecords)
            {
                record.LastSeenDate = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
        }

        // Create new discovery records for unseen OIDs — only when display name resolves
        foreach (var oid in newOids)
        {
            string? displayName = null;

            if (graphClient != null)
            {
                displayName = await TryResolveGroupNameAsync(graphClient, oid);
            }

            if (string.IsNullOrEmpty(displayName))
            {
                _logger.LogDebug("Skipping discovery of AD group {Oid} — could not resolve display name", oid);
                continue;
            }

            db.TblDiscoveredAdGroups.Add(new TblDiscoveredAdGroup
            {
                ObjectId = oid,
                DisplayName = displayName,
                FirstSeenDate = DateTime.UtcNow,
                LastSeenDate = DateTime.UtcNow,
                IsDismissed = false,
                IsAdopted = false
            });

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true
                                                || ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Concurrent login discovered the same OID
                _logger.LogDebug("Discovered AD group OID {Oid} already exists, skipping", oid);
            }
        }
    }

    public async Task<List<TblDiscoveredAdGroup>> GetUndiscoveredGroupsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        return await db.TblDiscoveredAdGroups
            .AsNoTracking()
            .Where(d => !d.IsAdopted && !d.IsDismissed)
            .OrderBy(d => d.DisplayName)
            .ToListAsync();
    }

    public async Task AdoptGroupAsync(int discoveredGroupId, string editUser)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var discovered = await db.TblDiscoveredAdGroups.FindAsync(discoveredGroupId)
            ?? throw new InvalidOperationException($"Discovered AD group {discoveredGroupId} not found");

        if (discovered.IsAdopted)
            throw new InvalidOperationException("This group has already been adopted");

        // Create the Director user group
        var group = new TblUserGroup
        {
            GroupName = discovered.DisplayName ?? $"AD Group {discovered.ObjectId[..8]}",
            GroupType = "ad-sync",
            ExternalId = discovered.ObjectId,
            Description = $"Synced from Azure AD group {discovered.ObjectId}",
            IsActive = true,
            CreateDate = DateTime.UtcNow,
            LastEditUser = editUser
        };

        db.TblUserGroups.Add(group);
        await db.SaveChangesAsync();

        // Mark discovered record as adopted
        discovered.IsAdopted = true;
        discovered.AdoptedUserGroupId = group.UserGroupId;
        await db.SaveChangesAsync();

        _logger.LogInformation("Adopted AD group {ObjectId} ({DisplayName}) as UserGroup {GroupId}",
            discovered.ObjectId, discovered.DisplayName, group.UserGroupId);
    }

    public async Task DismissGroupAsync(int discoveredGroupId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var discovered = await db.TblDiscoveredAdGroups.FindAsync(discoveredGroupId);
        if (discovered == null) return;

        discovered.IsDismissed = true;
        await db.SaveChangesAsync();
    }

    public async Task UndismissGroupAsync(int discoveredGroupId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var discovered = await db.TblDiscoveredAdGroups.FindAsync(discoveredGroupId);
        if (discovered == null) return;

        discovered.IsDismissed = false;
        await db.SaveChangesAsync();
    }

    private async Task<string?> TryResolveGroupNameAsync(GraphServiceClient graphClient, string oid)
    {
        try
        {
            var group = await graphClient.Groups[oid].Request().Select("displayName").GetAsync();
            return group?.DisplayName;
        }
        catch (ServiceException ex)
        {
            _logger.LogDebug(ex, "Could not resolve display name for AD group {Oid}", oid);
            return null;
        }
    }
}
