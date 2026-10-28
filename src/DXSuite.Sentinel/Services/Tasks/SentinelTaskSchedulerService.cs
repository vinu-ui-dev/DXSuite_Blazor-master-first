using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DXSuite.Sentinel.Entities;
using DXSuite.Sentinel.Services.Gateway;
using DXSuite.Shared.Services;

namespace DXSuite.Sentinel.Services.Tasks;

/// <summary>
/// Time-based Task runner. Polls due <see cref="SentinelTaskSchedule"/> rows, resolves each task's
/// target agents, runs the task through <see cref="ITaskExecutionService"/>, and computes the next run.
/// Ported from the Graphite_Prototype_v2 ScriptSchedulerService and extended for the
/// interval / daily / weekly / once modes. All times are UTC.
/// </summary>
public class SentinelTaskSchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITaskExecutionService _taskExecutor;
    private readonly IGatewayConnectionManager _gateway;
    private readonly IDistributedLockService _lockService;
    private readonly ISentinelAvailabilityService _sentinelAvailability;
    private readonly ILogger<SentinelTaskSchedulerService> _logger;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    public SentinelTaskSchedulerService(
        IServiceScopeFactory scopeFactory,
        ITaskExecutionService taskExecutor,
        IGatewayConnectionManager gateway,
        IDistributedLockService lockService,
        ISentinelAvailabilityService sentinelAvailability,
        ILogger<SentinelTaskSchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _taskExecutor = taskExecutor;
        _gateway = gateway;
        _lockService = lockService;
        _sentinelAvailability = sentinelAvailability;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SentinelTaskSchedulerService started");

        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_sentinelAvailability.IsAvailable)
                {
                    using var distLock = await _lockService.TryAcquireLockAsync(
                        "task-scheduler-lock", TimeSpan.FromMinutes(10), stoppingToken);
                    if (distLock != null)
                        await RunCycleAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Error in task scheduler cycle"); }

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }

        _logger.LogInformation("SentinelTaskSchedulerService stopped");
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        ISentinelDBContext db;
        try
        {
            db = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();
            await db.SentinelTaskSchedules.AsNoTracking().Take(1).ToListAsync(ct);
        }
        catch { return; } // Sentinel DB not available

        var now = DateTime.UtcNow;
        var due = await db.SentinelTaskSchedules
            .Include(s => s.Task)
            .Where(s => s.IsEnabled
                && (s.NextRunUtc == null || s.NextRunUtc <= now)
                && (s.LastStatus == null || s.LastStatus != "running"))
            .ToListAsync(ct);

        if (due.Count == 0) return;
        _logger.LogInformation("Task scheduler: {Count} schedule(s) due", due.Count);

        foreach (var schedule in due)
        {
            if (ct.IsCancellationRequested) break;

            // Claim it so a slow run isn't re-picked next cycle.
            schedule.LastStatus = "running";
            schedule.LastRunUtc = now;
            await db.SaveChangesAsync(ct);

            try
            {
                var gatewayIds = await ResolveTargetGatewaysAsync(db, schedule.TaskId, ct);
                if (gatewayIds.Count == 0)
                {
                    _logger.LogWarning("Schedule {ScheduleId} (task {TaskId}) has no online target agents; skipping this run",
                        schedule.ScheduleId, schedule.TaskId);
                    schedule.LastStatus = "no-target";
                }
                else
                {
                    var allOk = true;
                    foreach (var gid in gatewayIds)
                    {
                        var result = await _taskExecutor.RunTaskAsync(schedule.TaskId, gid, $"schedule:{schedule.ScheduleId}", ct);
                        allOk &= result.Success;
                    }
                    schedule.LastStatus = allOk ? "success" : "failed";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Schedule {ScheduleId} execution failed", schedule.ScheduleId);
                schedule.LastStatus = "failed";
            }

            // "once" runs a single time; everything else reschedules.
            if (string.Equals(schedule.ExecutionMode, "once", StringComparison.OrdinalIgnoreCase))
            {
                schedule.IsEnabled = false;
                schedule.NextRunUtc = null;
            }
            else
            {
                schedule.NextRunUtc = ComputeNextRun(schedule, DateTime.UtcNow);
            }
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Resolve a task's enabled targets to currently-online gateway ids.</summary>
    private async Task<List<int>> ResolveTargetGatewaysAsync(ISentinelDBContext db, int taskId, CancellationToken ct)
    {
        var targets = await db.SentinelTaskTargets
            .AsNoTracking()
            .Where(t => t.TaskId == taskId && t.IsEnabled)
            .ToListAsync(ct);

        var online = _gateway.GetConnectedGateways();
        var ids = new HashSet<int>();

        foreach (var t in targets)
        {
            switch (t.TargetScope?.ToLowerInvariant())
            {
                case "gateway":
                    if (int.TryParse(t.TargetRef, out var gid) && online.Any(g => g.GatewayId == gid))
                        ids.Add(gid);
                    break;
                case "all":
                    foreach (var g in online) ids.Add(g.GatewayId);
                    break;
                case "device":
                    // A device runs its Task on the agent (site gateway) that serves it.
                    if (int.TryParse(t.TargetRef, out var deviceId))
                    {
                        var deviceGatewayId = await db.DeviceInstances
                            .AsNoTracking()
                            .Where(d => d.DeviceInstanceId == deviceId)
                            .Select(d => d.SiteGatewayId)
                            .FirstOrDefaultAsync(ct);
                        if (deviceGatewayId is int dg && online.Any(g => g.GatewayId == dg))
                            ids.Add(dg);
                        else
                            _logger.LogDebug("Task {TaskId}: device {DeviceId} has no online site gateway", taskId, deviceId);
                    }
                    break;
                default:
                    // descriptor / customer scopes resolve to gateways in a later phase.
                    _logger.LogDebug("Task {TaskId}: target scope '{Scope}' not yet supported for scheduled runs", taskId, t.TargetScope);
                    break;
            }
        }
        return ids.ToList();
    }

    /// <summary>Next run after a completed run (or for rescheduling). Returns null when it should not repeat.</summary>
    public static DateTime? ComputeNextRun(SentinelTaskSchedule s, DateTime fromUtc)
    {
        if (!s.IsEnabled) return null;
        switch (s.ExecutionMode?.ToLowerInvariant())
        {
            case "interval":
                return s.FrequencySeconds is > 0 ? fromUtc.AddSeconds(s.FrequencySeconds.Value) : null;
            case "daily":
                return NextTimeOfDay(fromUtc, ParseTimeOfDay(s.TimeOfDay), allowedDays: null);
            case "weekly":
                return NextTimeOfDay(fromUtc, ParseTimeOfDay(s.TimeOfDay), ParseDays(s.DaysOfWeek));
            default: // once / unknown
                return null;
        }
    }

    /// <summary>First run when a schedule is created/updated. Interval/once fire on the next cycle; daily/weekly wait for the time-of-day.</summary>
    public static DateTime? ComputeInitialRun(SentinelTaskSchedule s, DateTime fromUtc)
    {
        if (!s.IsEnabled) return null;
        return s.ExecutionMode?.ToLowerInvariant() switch
        {
            "interval" => fromUtc,
            "once" => string.IsNullOrWhiteSpace(s.TimeOfDay) ? fromUtc : NextTimeOfDay(fromUtc, ParseTimeOfDay(s.TimeOfDay), null),
            "daily" => NextTimeOfDay(fromUtc, ParseTimeOfDay(s.TimeOfDay), null),
            "weekly" => NextTimeOfDay(fromUtc, ParseTimeOfDay(s.TimeOfDay), ParseDays(s.DaysOfWeek)),
            _ => fromUtc
        };
    }

    private static TimeSpan ParseTimeOfDay(string? hhmmss) =>
        TimeSpan.TryParse(string.IsNullOrWhiteSpace(hhmmss) ? "00:00:00" : hhmmss, out var ts) ? ts : TimeSpan.Zero;

    private static HashSet<int>? ParseDays(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var set = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(d => int.TryParse(d, out _))
            .Select(int.Parse)
            .Where(d => d is >= 0 and <= 6)
            .ToHashSet();
        return set.Count > 0 ? set : null;
    }

    private static DateTime NextTimeOfDay(DateTime fromUtc, TimeSpan tod, HashSet<int>? allowedDays)
    {
        // Scan up to 8 days to find the next allowed day whose time-of-day is still ahead.
        for (var offset = 0; offset <= 8; offset++)
        {
            var candidate = fromUtc.Date.AddDays(offset) + tod;
            if (candidate <= fromUtc) continue;
            if (allowedDays == null || allowedDays.Contains((int)candidate.DayOfWeek))
                return candidate;
        }
        return fromUtc.AddDays(1) + tod; // fallback (shouldn't be reached)
    }
}
