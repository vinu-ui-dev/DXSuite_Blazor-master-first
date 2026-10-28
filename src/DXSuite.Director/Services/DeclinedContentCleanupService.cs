using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.DistributedLock;

namespace DXSuite.Director.Services;

/// <summary>
/// Hourly background service that purges declined content whose retention window has expired.
/// Each location's approval chain defines its own retention period (default 3 days).
/// </summary>
public class DeclinedContentCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedLockService _lockService;
    private readonly ILogger<DeclinedContentCleanupService> _logger;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private const int BatchSize = 100;

    public DeclinedContentCleanupService(
        IServiceScopeFactory scopeFactory,
        IDistributedLockService lockService,
        ILogger<DeclinedContentCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _lockService = lockService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) { break; }

            using var distLock = await _lockService.TryAcquireLockAsync("declined-content-cleanup", TimeSpan.FromMinutes(5), stoppingToken);
            if (distLock == null) continue;

            try
            {
                await RunCleanupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in declined content cleanup cycle");
            }
        }
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
        var approvalService = scope.ServiceProvider.GetRequiredService<IAssetApprovalService>();

        var now = DateTime.UtcNow;

        // Load all declined approvals with a DeclinedDate set, along with chain retention info
        var declinedApprovals = await db.TblAssetApprovals
            .AsNoTracking()
            .Include(a => a.Asset)
            .Where(a => a.ApprovalStatus == 2 && a.DeclinedDate != null)
            .Take(BatchSize)
            .Select(a => new
            {
                a.AssetApprovalId,
                a.DeclinedDate,
                a.Asset.LocationId
            })
            .ToListAsync(ct);

        if (declinedApprovals.Count == 0) return;

        // Cache chain retention days per location
        var locationIds = declinedApprovals.Select(a => a.LocationId).Distinct().ToList();
        var defaultChains = await db.TblApprovalChains
            .AsNoTracking()
            .Where(c => c.LocationId != null && locationIds.Contains(c.LocationId.Value) && c.IsDefault)
            .Select(c => new DefaultChainRetention(c.LocationId!.Value, c.ApprovalChainId, c.DeclinedContentRetentionDays))
            .ToListAsync(ct);
        var chains = RetentionDaysByLocation(defaultChains);

        int purged = 0;
        foreach (var approval in declinedApprovals)
        {
            if (ct.IsCancellationRequested) break;

            var retentionDays = chains.GetValueOrDefault(approval.LocationId, 3);
            var expiryDate = approval.DeclinedDate!.Value.AddDays(retentionDays);

            if (expiryDate < now)
            {
                try
                {
                    // Use a fresh scope for each purge to avoid stale tracking state
                    using var purgeScope = _scopeFactory.CreateScope();
                    var purgeService = purgeScope.ServiceProvider.GetRequiredService<IAssetApprovalService>();
                    await purgeService.PurgeDeclinedAssetAsync(approval.AssetApprovalId);
                    purged++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to purge declined approval {Id}", approval.AssetApprovalId);
                }
            }
        }

        if (purged > 0)
        {
            _logger.LogInformation("Declined content cleanup: purged {Count} expired items", purged);
        }
    }

    public readonly record struct DefaultChainRetention(int LocationId, int ApprovalChainId, int DeclinedContentRetentionDays);

    /// <summary>
    /// Retention is configured on each location's default chain; per-access-rule chains carry no
    /// retention of their own. Nothing in the schema stops a bundle import from adding a second
    /// default chain, so the oldest one wins rather than failing the purge for every location.
    /// </summary>
    public static Dictionary<int, int> RetentionDaysByLocation(IEnumerable<DefaultChainRetention> defaultChains)
        => defaultChains
            .GroupBy(c => c.LocationId)
            .ToDictionary(g => g.Key, g => g.MinBy(c => c.ApprovalChainId).DeclinedContentRetentionDays);
}
