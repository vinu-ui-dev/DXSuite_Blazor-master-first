using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DXSuite.Sentinel.Entities;
using DXSuite.Shared.Services;

namespace DXSuite.Sentinel.Services;

/// <summary>
/// Background service that enforces probe history retention policy.
/// Runs hourly: rolls up raw data into summaries, then purges expired rows.
/// Retention: 48h raw, 30d hourly summaries, 1y daily summaries.
/// </summary>
public class ProbeHistoryCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProbeHistoryCleanupService> _logger;
    private readonly IDistributedLockService _lockService;
    private readonly ISentinelAvailabilityService _sentinelAvailability;

    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan RawRetention = TimeSpan.FromHours(48);
    private static readonly TimeSpan HourlyRetention = TimeSpan.FromDays(30);
    private static readonly TimeSpan DailyRetention = TimeSpan.FromDays(365);

    public ProbeHistoryCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<ProbeHistoryCleanupService> logger,
        IDistributedLockService lockService,
        ISentinelAvailabilityService sentinelAvailability)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _lockService = lockService;
        _sentinelAvailability = sentinelAvailability;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProbeHistoryCleanupService started");

        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_sentinelAvailability.IsAvailable)
                {
                    await Task.Delay(CleanupInterval, stoppingToken);
                    continue;
                }
                using var distLock = await _lockService.TryAcquireLockAsync(
                    "probe-history-cleanup-lock",
                    TimeSpan.FromMinutes(30),
                    stoppingToken);

                if (distLock != null)
                {
                    await RunCleanupCycleAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProbeHistoryCleanupService cleanup cycle");
            }

            try
            {
                await Task.Delay(CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("ProbeHistoryCleanupService stopped");
    }

    private async Task RunCleanupCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        ISentinelDBContext sentinelDb;
        try
        {
            sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();
            // Verify DB is accessible
            await sentinelDb.ProbeHistories.AsNoTracking().Take(1).ToListAsync(ct);
        }
        catch
        {
            return; // Sentinel DB not available
        }

        var now = DateTime.UtcNow;

        // Step 1: Roll up raw records older than 1 hour into hourly summaries
        await RollUpRawToHourlyAsync(sentinelDb, now, ct);

        // Step 2: Delete raw records older than 48 hours
        await PurgeExpiredRawAsync(sentinelDb, now, ct);

        // Step 3: Roll up hourly summaries older than 30 days into daily summaries
        await RollUpHourlyToDailyAsync(sentinelDb, now, ct);

        // Step 4: Delete hourly summaries older than 30 days
        await PurgeExpiredHourlyAsync(sentinelDb, now, ct);

        // Step 5: Delete daily summaries older than 1 year
        await PurgeExpiredDailyAsync(sentinelDb, now, ct);
    }

    private async Task RollUpRawToHourlyAsync(ISentinelDBContext db, DateTime now, CancellationToken ct)
    {
        // Roll up records older than 1 hour (to ensure complete hour buckets)
        var cutoff = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc)
            .AddHours(-1);

        var rawRecords = await db.ProbeHistories
            .Where(h => h.ProbeTimestamp < cutoff)
            .ToListAsync(ct);

        if (rawRecords.Count == 0)
            return;

        // Group by device + hour bucket
        var groups = rawRecords.GroupBy(h => new
        {
            h.DeviceInstanceId,
            h.DirectorDeviceId,
            h.CustomerId,
            HourStart = new DateTime(h.ProbeTimestamp.Year, h.ProbeTimestamp.Month, h.ProbeTimestamp.Day,
                h.ProbeTimestamp.Hour, 0, 0, DateTimeKind.Utc)
        });

        foreach (var group in groups)
        {
            // Check if summary already exists for this bucket
            var existingSummary = await db.ProbeHistorySummaries
                .FirstOrDefaultAsync(s =>
                    s.DeviceInstanceId == group.Key.DeviceInstanceId
                    && s.DirectorDeviceId == group.Key.DirectorDeviceId
                    && s.Period == "hourly"
                    && s.PeriodStart == group.Key.HourStart, ct);

            if (existingSummary != null)
                continue; // Already rolled up

            var records = group.ToList();
            var passed = records.Count(r => r.StatusColor == "green");
            var degraded = records.Count(r => r.StatusColor == "yellow");
            var failed = records.Count(r => r.StatusColor == "red");
            var total = records.Count;

            db.ProbeHistorySummaries.Add(new ProbeHistorySummary
            {
                DeviceInstanceId = group.Key.DeviceInstanceId,
                DirectorDeviceId = group.Key.DirectorDeviceId,
                Period = "hourly",
                PeriodStart = group.Key.HourStart,
                TotalProbes = total,
                PassedCount = passed,
                DegradedCount = degraded,
                FailedCount = failed,
                UptimePercent = total > 0 ? (passed + degraded) * 100f / total : 0f,
                CustomerId = group.Key.CustomerId,
                CreateDate = now
            });
        }

        await db.SaveChangesAsync(ct);
        _logger.LogDebug("ProbeHistoryCleanup: rolled up {Count} raw records into hourly summaries", rawRecords.Count);
    }

    private async Task PurgeExpiredRawAsync(ISentinelDBContext db, DateTime now, CancellationToken ct)
    {
        var cutoff = now - RawRetention;
        var expired = await db.ProbeHistories
            .Where(h => h.ProbeTimestamp < cutoff)
            .ToListAsync(ct);

        if (expired.Count == 0)
            return;

        // Delete in batches to avoid large transactions
        const int batchSize = 1000;
        for (int i = 0; i < expired.Count; i += batchSize)
        {
            var batch = expired.Skip(i).Take(batchSize);
            db.ProbeHistories.RemoveRange(batch);
            await db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("ProbeHistoryCleanup: purged {Count} raw records older than {Hours}h", expired.Count, RawRetention.TotalHours);
    }

    private async Task RollUpHourlyToDailyAsync(ISentinelDBContext db, DateTime now, CancellationToken ct)
    {
        var cutoff = now - HourlyRetention;

        var hourlySummaries = await db.ProbeHistorySummaries
            .Where(s => s.Period == "hourly" && s.PeriodStart < cutoff)
            .ToListAsync(ct);

        if (hourlySummaries.Count == 0)
            return;

        var groups = hourlySummaries.GroupBy(s => new
        {
            s.DeviceInstanceId,
            s.DirectorDeviceId,
            s.CustomerId,
            DayStart = new DateTime(s.PeriodStart.Year, s.PeriodStart.Month, s.PeriodStart.Day, 0, 0, 0, DateTimeKind.Utc)
        });

        foreach (var group in groups)
        {
            var existingDaily = await db.ProbeHistorySummaries
                .FirstOrDefaultAsync(s =>
                    s.DeviceInstanceId == group.Key.DeviceInstanceId
                    && s.DirectorDeviceId == group.Key.DirectorDeviceId
                    && s.Period == "daily"
                    && s.PeriodStart == group.Key.DayStart, ct);

            if (existingDaily != null)
                continue;

            var records = group.ToList();
            var totalProbes = records.Sum(r => r.TotalProbes);
            var totalPassed = records.Sum(r => r.PassedCount);
            var totalDegraded = records.Sum(r => r.DegradedCount);
            var totalFailed = records.Sum(r => r.FailedCount);

            db.ProbeHistorySummaries.Add(new ProbeHistorySummary
            {
                DeviceInstanceId = group.Key.DeviceInstanceId,
                DirectorDeviceId = group.Key.DirectorDeviceId,
                Period = "daily",
                PeriodStart = group.Key.DayStart,
                TotalProbes = totalProbes,
                PassedCount = totalPassed,
                DegradedCount = totalDegraded,
                FailedCount = totalFailed,
                UptimePercent = totalProbes > 0 ? (totalPassed + totalDegraded) * 100f / totalProbes : 0f,
                CustomerId = group.Key.CustomerId,
                CreateDate = now
            });
        }

        await db.SaveChangesAsync(ct);
        _logger.LogDebug("ProbeHistoryCleanup: rolled up {Count} hourly summaries into daily", hourlySummaries.Count);
    }

    private async Task PurgeExpiredHourlyAsync(ISentinelDBContext db, DateTime now, CancellationToken ct)
    {
        var cutoff = now - HourlyRetention;
        var expired = await db.ProbeHistorySummaries
            .Where(s => s.Period == "hourly" && s.PeriodStart < cutoff)
            .ToListAsync(ct);

        if (expired.Count > 0)
        {
            db.ProbeHistorySummaries.RemoveRange(expired);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("ProbeHistoryCleanup: purged {Count} hourly summaries older than {Days}d", expired.Count, HourlyRetention.TotalDays);
        }
    }

    private async Task PurgeExpiredDailyAsync(ISentinelDBContext db, DateTime now, CancellationToken ct)
    {
        var cutoff = now - DailyRetention;
        var expired = await db.ProbeHistorySummaries
            .Where(s => s.Period == "daily" && s.PeriodStart < cutoff)
            .ToListAsync(ct);

        if (expired.Count > 0)
        {
            db.ProbeHistorySummaries.RemoveRange(expired);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("ProbeHistoryCleanup: purged {Count} daily summaries older than {Days}d", expired.Count, DailyRetention.TotalDays);
        }
    }
}
