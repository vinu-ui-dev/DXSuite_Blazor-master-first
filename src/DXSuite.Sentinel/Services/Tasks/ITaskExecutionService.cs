namespace DXSuite.Sentinel.Services.Tasks;

/// <summary>Outcome of running a Task on an agent, plus the id of the execution-log row written.</summary>
public record TaskExecutionResult(bool Success, int? ExitCode, string? Stdout, string? Stderr, string? Error, long ExecutionLogId);

/// <summary>
/// Runs a stored <see cref="Entities.SentinelTask"/> on a target agent (gateway) via the MQTT
/// gateway transport and records a <see cref="Entities.SentinelTaskExecutionLog"/>. Called by the
/// scheduler, the reactive engine, and run-now from the UI.
/// </summary>
public interface ITaskExecutionService
{
    Task<TaskExecutionResult> RunTaskAsync(int taskId, int gatewayId, string triggeredBy, CancellationToken cancellationToken = default);
}
