using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DXSuite.Sentinel.Entities;
using DXSuite.Sentinel.Services.Gateway;

namespace DXSuite.Sentinel.Services.Tasks;

/// <inheritdoc />
public class TaskExecutionService : ITaskExecutionService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGatewayConnectionManager _gateway;
    private readonly ILogger<TaskExecutionService> _logger;

    public TaskExecutionService(
        IServiceScopeFactory scopeFactory,
        IGatewayConnectionManager gateway,
        ILogger<TaskExecutionService> logger)
    {
        _scopeFactory = scopeFactory;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<TaskExecutionResult> RunTaskAsync(int taskId, int gatewayId, string triggeredBy, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

        var task = await db.SentinelTasks.FirstOrDefaultAsync(t => t.TaskId == taskId, cancellationToken);
        if (task is null)
            return new TaskExecutionResult(false, null, null, null, "Task not found", 0);
        if (!task.IsActive)
            return new TaskExecutionResult(false, null, null, null, "Task is not active", 0);

        // Record a "running" log up front so an in-flight or crashed run is still visible.
        var log = new SentinelTaskExecutionLog
        {
            TaskId = taskId,
            GatewayId = gatewayId,
            Status = "running",
            TriggeredBy = triggeredBy,
            StartUtc = DateTime.UtcNow,
            CreateDate = DateTime.UtcNow
        };
        db.SentinelTaskExecutionLogs.Add(log);
        await db.SaveChangesAsync(cancellationToken);

        var result = await _gateway.SendScriptViaGatewayAsync(
            gatewayId, task.ScriptType, task.ScriptContent, task.ContentHash, task.TimeoutMs, cancellationToken);

        var timedOut = result.ErrorMessage?.Contains("timed out", StringComparison.OrdinalIgnoreCase) == true;
        log.Status = result.Success ? "success" : (timedOut ? "timeout" : "failed");
        log.ExitCode = result.ExitCode;
        log.OutputLog = result.Stdout;
        log.ErrorMessage = result.Success ? null : (result.Stderr is { Length: > 0 } ? result.Stderr : result.ErrorMessage);
        log.DurationMs = result.ElapsedMs;
        log.EndUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Task {TaskId} on gateway {GatewayId}: {Status} (exit {Exit})",
            taskId, gatewayId, log.Status, result.ExitCode);

        return new TaskExecutionResult(
            result.Success, result.ExitCode, result.Stdout, result.Stderr,
            result.Success ? null : (result.ErrorMessage ?? result.Stderr), log.ExecutionId);
    }
}
