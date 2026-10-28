using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Director.Entities;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services.CoreApi;

/// <summary>
/// Keeps the tenant's CoreAPI connection in its Director database. Each call opens its own context from the
/// scope's tenant-aware factory, so the client can record the last good server while a page is using the
/// scope's shared context.
/// </summary>
public sealed class CoreApiSettingsStore : ICoreApiSettingsStore
{
    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly CoreApiKeyProtector _protector;
    private readonly TimeProvider _clock;

    public CoreApiSettingsStore(IDbContextFactory<DirectorDbContext> dbFactory, CoreApiKeyProtector protector, TimeProvider clock)
    {
        _dbFactory = dbFactory;
        _protector = protector;
        _clock = clock;
    }

    public async Task<CoreApiSettings?> ReadAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.TblCoreApiConnections.AsNoTracking()
            .Include(c => c.Servers)
            .OrderBy(c => c.CoreApiConnectionId)
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;

        _protector.TryUnprotect(row.DirectorKeyProtected, out var directorKey);
        var servers = row.Servers.OrderBy(s => s.SortOrder).Select(s => s.Url).ToList();
        return new CoreApiSettings(directorKey, servers, row.LastGoodServerUrl, row.HubUrl);
    }

    public async Task SaveAsync(string directorKey, IReadOnlyList<string> servers, string? hubUrl, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directorKey);
        var hub = CoreApiSettings.NormaliseHubUrl(hubUrl);
        var ordered = CoreApiSettings.NormaliseServers(servers);
        if (ordered.Count == 0) throw new ArgumentException("At least one CoreAPI server is needed.", nameof(servers));

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // A new list starts with no last good server, so the first call tries the servers in their new order.
        await db.TblCoreApiServers.ExecuteDeleteAsync(ct);
        await db.TblCoreApiConnections.ExecuteDeleteAsync(ct);

        db.TblCoreApiConnections.Add(new TblCoreApiConnection
        {
            DirectorKeyProtected = _protector.Protect(directorKey.Trim()),
            HubUrl = hub,
            UpdatedUtc = _clock.GetUtcNow().UtcDateTime,
            Servers = ordered.Select((url, i) => new TblCoreApiServer { Url = url, SortOrder = i }).ToList()
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task SaveHubUrlAsync(string? hubUrl, CancellationToken ct)
    {
        var hub = CoreApiSettings.NormaliseHubUrl(hubUrl);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        var changed = await db.TblCoreApiConnections.ExecuteUpdateAsync(s => s
            .SetProperty(c => c.HubUrl, hub)
            .SetProperty(c => c.UpdatedUtc, now), ct);
        if (changed == 0) throw new InvalidOperationException("No CoreAPI connection is saved, so there is no hub address to change.");
    }

    public async Task RecordLastGoodServerAsync(string server, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        await db.TblCoreApiConnections.ExecuteUpdateAsync(s => s
            .SetProperty(c => c.LastGoodServerUrl, server)
            .SetProperty(c => c.LastGoodUtc, now), ct);
    }
}
