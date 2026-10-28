using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.DistributedLock;

namespace DXSuite.Director.Services;

/// <summary>
/// Daily background service that enforces event log retention policy (IS-LMAS2026).
/// Phase 1: Archives online records older than configured retention period (default 365 days).
/// Phase 2: Purges archive records older than archive retention period (default 7 years).
/// </summary>
public class EventLogRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventLogRetentionService> _logger;
    private readonly IDistributedLockService _lockService;
    private readonly IConfiguration _configuration;

    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CycleInterval = TimeSpan.FromHours(24);
    private const int BatchSize = 1000;

    public EventLogRetentionService(
        IServiceScopeFactory scopeFactory,
        ILogger<EventLogRetentionService> logger,
        IDistributedLockService lockService,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _lockService = lockService;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EventLogRetentionService started");
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var distLock = await _lockService.TryAcquireLockAsync(
                    "event-log-retention-lock",
                    TimeSpan.FromMinutes(30),
                    stoppingToken);

                if (distLock != null)
                {
                    await RunRetentionCycleAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in EventLogRetentionService cycle");
            }

            try { await Task.Delay(CycleInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("EventLogRetentionService stopped");
    }

    private async Task RunRetentionCycleAsync(CancellationToken ct)
    {
        var onlineRetentionDays = _configuration.GetValue<int>("Security:EventLogOnlineRetentionDays", 365);
        var archiveRetentionYears = _configuration.GetValue<int>("Security:EventLogArchiveRetentionYears", 7);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
        var now = DateTime.UtcNow;

        // Phase 1: Archive records beyond online retention
        var archiveCutoff = now.AddDays(-onlineRetentionDays);
        var totalArchived = await ArchiveOldRecordsAsync(db, archiveCutoff, ct);
        if (totalArchived > 0)
            _logger.LogInformation("Archived {Count} event log records older than {Days} days", totalArchived, onlineRetentionDays);

        // Phase 2: Purge archive records beyond archive retention
        var purgeCutoff = now.AddYears(-archiveRetentionYears);
        var totalPurged = await PurgeOldArchivesAsync(db, purgeCutoff, ct);
        if (totalPurged > 0)
            _logger.LogInformation("Purged {Count} archived event log records older than {Years} years", totalPurged, archiveRetentionYears);
    }

    private async Task<int> ArchiveOldRecordsAsync(IDirectorDBContext db, DateTime cutoff, CancellationToken ct)
    {
        int totalArchived = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await db.TblEventLogs
                .Where(e => e.CreateDate < cutoff)
                .OrderBy(e => e.RecordId)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            var archives = batch.Select(e => new TblEventLogArchive
            {
                OriginalRecordId = e.RecordId,
                EventSource = e.EventSource,
                EventTypeId = e.EventTypeId,
                EventTitle = e.EventTitle,
                EventMessage = e.EventMessage,
                EventSeverityId = e.EventSeverityId,
                UserId = e.UserId,
                AssetId = e.AssetId,
                DisplayId = e.DisplayId,
                ScheduleEventId = e.ScheduleEventId,
                LocationId = e.LocationId,
                CreateDate = e.CreateDate,
                DeviceId = e.DeviceId,
                Details = e.Details,
                Success = e.Success,
                TicketCreated = e.TicketCreated,
                TicketId = e.TicketId,
                ArchivedDate = DateTime.UtcNow
            }).ToList();

            db.TblEventLogArchives.AddRange(archives);
            db.TblEventLogs.RemoveRange(batch);
            await db.SaveChangesAsync(ct);

            totalArchived += batch.Count;

            if (batch.Count < BatchSize) break;
        }

        return totalArchived;
    }

    private async Task<int> PurgeOldArchivesAsync(IDirectorDBContext db, DateTime cutoff, CancellationToken ct)
    {
        int totalPurged = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await db.TblEventLogArchives
                .Where(a => a.ArchivedDate < cutoff)
                .OrderBy(a => a.ArchiveId)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            db.TblEventLogArchives.RemoveRange(batch);
            await db.SaveChangesAsync(ct);

            totalPurged += batch.Count;

            if (batch.Count < BatchSize) break;
        }

        return totalPurged;
    }
}
