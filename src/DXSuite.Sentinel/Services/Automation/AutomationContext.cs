using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DXSuite.Sentinel.Entities;
using DXSuite.Sentinel.Services.Engine;
using DXSuite.Sentinel.Services.Routing;
using DXSuite.Sentinel.Services.Tasks;

namespace DXSuite.Sentinel.Services.Automation;

/// <summary>
/// Concrete implementation of IAutomationContext. Created per-execution with specific device context.
/// All commands are routed through ICommandRouter and validated against guardrails.
/// In dry-run mode, commands are logged but not executed.
/// </summary>
public class AutomationContext : IAutomationContext
{
    private readonly ICommandRouter _commandRouter;
    private readonly IGuardrailService _guardrailService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _customerId;
    private readonly List<string> _logEntries = new();
    private AutomationFlowResult? _flowResult;

    public int CurrentDeviceInstanceId { get; }
    public string CurrentDeviceType { get; }
    public bool IsDryRun { get; }

    public AutomationContext(
        int deviceInstanceId,
        string deviceType,
        string customerId,
        bool isDryRun,
        ICommandRouter commandRouter,
        IGuardrailService guardrailService,
        IServiceScopeFactory scopeFactory)
    {
        CurrentDeviceInstanceId = deviceInstanceId;
        CurrentDeviceType = deviceType;
        _customerId = customerId;
        IsDryRun = isDryRun;
        _commandRouter = commandRouter;
        _guardrailService = guardrailService;
        _scopeFactory = scopeFactory;
    }

    public DeviceState GetDeviceState(int deviceInstanceId)
    {
        using var scope = _scopeFactory.CreateScope();
        var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

        var json = sentinelDb.DeviceInstances
            .AsNoTracking()
            .Where(d => d.DeviceInstanceId == deviceInstanceId)
            .Select(d => d.LastStateJson)
            .FirstOrDefault();

        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var state = JsonSerializer.Deserialize<DeviceState>(json);
                if (state != null) return state;
            }
            catch (JsonException) { }
        }

        return new DeviceState { DeviceInstanceId = deviceInstanceId };
    }

    public IReadOnlyList<ActiveAlert> GetActiveAlerts(int deviceInstanceId)
    {
        var state = GetDeviceState(deviceInstanceId);
        return state.ActiveAlerts.AsReadOnly();
    }

    public IReadOnlyList<MonitorReading> GetHistory(int deviceInstanceId, string monitorPointId, TimeSpan lookback)
    {
        // Load historical monitor readings from Sentinel device state snapshots
        return new List<MonitorReading>().AsReadOnly();
    }

    public async Task<CommandResult> ExecuteCommandAsync(int deviceInstanceId, string commandId, Dictionary<string, object>? parameters = null)
    {
        Log($"Executing command: {commandId} on device {deviceInstanceId}");

        if (IsDryRun)
        {
            Log($"[DRY RUN] Would execute {commandId} on device {deviceInstanceId}");
            return new CommandResult { Success = true, CommandId = commandId, RoutedVia = "dry-run" };
        }

        // Validate against guardrails
        if (!await _guardrailService.IsActionAllowedAsync(deviceInstanceId, _customerId, commandId))
        {
            Log($"Command {commandId} blocked by guardrails");
            return new CommandResult { Success = false, CommandId = commandId, ErrorMessage = "Blocked by guardrails" };
        }

        return await _commandRouter.RouteCommandAsync(deviceInstanceId, commandId, parameters);
    }

    public async Task<CommandResult> ExecuteScriptAsync(int taskId)
    {
        Log($"Running task {taskId} on the agent for device {CurrentDeviceInstanceId}");

        if (IsDryRun)
        {
            Log($"[DRY RUN] Would run task {taskId}");
            return new CommandResult { Success = true, CommandId = $"run-script:{taskId}", RoutedVia = "dry-run" };
        }

        // Tasks are remote code execution; gate them through the same guardrails as commands.
        if (!await _guardrailService.IsActionAllowedAsync(CurrentDeviceInstanceId, _customerId, "run-script"))
        {
            Log("Task blocked by guardrails");
            return new CommandResult { Success = false, CommandId = $"run-script:{taskId}", ErrorMessage = "Blocked by guardrails" };
        }

        using var scope = _scopeFactory.CreateScope();
        var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

        // A Task runs on the agent (site gateway) that serves the triggering device.
        var gatewayId = await sentinelDb.DeviceInstances
            .AsNoTracking()
            .Where(d => d.DeviceInstanceId == CurrentDeviceInstanceId)
            .Select(d => d.SiteGatewayId)
            .FirstOrDefaultAsync();

        if (gatewayId is null or 0)
        {
            Log($"No site gateway assigned to device {CurrentDeviceInstanceId}; cannot run task {taskId}");
            return new CommandResult { Success = false, CommandId = $"run-script:{taskId}", ErrorMessage = "No agent (site gateway) for this device" };
        }

        var taskExecutor = scope.ServiceProvider.GetRequiredService<ITaskExecutionService>();
        var r = await taskExecutor.RunTaskAsync(taskId, gatewayId.Value, $"reactive:device:{CurrentDeviceInstanceId}");
        Log($"Task {taskId} on gateway {gatewayId}: success={r.Success} exit={r.ExitCode}");

        return new CommandResult
        {
            Success = r.Success,
            CommandId = $"run-script:{taskId}",
            ErrorMessage = r.Error,
            ExitCode = r.ExitCode,
            Stdout = r.Stdout,
            Stderr = r.Stderr,
            RoutedVia = "task"
        };
    }

    public async Task WaitAsync(TimeSpan duration)
    {
        Log($"Waiting {duration.TotalSeconds}s");
        if (!IsDryRun)
        {
            await Task.Delay(duration);
        }
    }

    public void Log(string message)
    {
        _logEntries.Add($"[{DateTime.UtcNow:HH:mm:ss}] {message}");
    }

    public void Resolve(string summary)
    {
        Log($"RESOLVED: {summary}");
        _flowResult = new AutomationFlowResult { Type = FlowResultType.Resolved, Message = summary };
    }

    public void Escalate(string reason)
    {
        Log($"ESCALATED: {reason}");
        _flowResult = new AutomationFlowResult { Type = FlowResultType.Escalated, Message = reason };
    }

    public IReadOnlyList<int> GetDevicesAtLocation(int locationId)
    {
        using var scope = _scopeFactory.CreateScope();
        var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

        return sentinelDb.DeviceInstances
            .AsNoTracking()
            .Where(d => d.IsEnabled && d.LocationId == locationId)
            .Select(d => d.DeviceInstanceId)
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<int> GetDevicesByType(string descriptorDeviceType)
    {
        using var scope = _scopeFactory.CreateScope();
        var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

        return sentinelDb.DeviceInstances
            .AsNoTracking()
            .Where(d => d.IsEnabled && d.DeviceDescriptor.DeviceType == descriptorDeviceType)
            .Select(d => d.DeviceInstanceId)
            .ToList()
            .AsReadOnly();
    }

    // Internal accessors for the executor
    internal IReadOnlyList<string> GetLogEntries() => _logEntries.AsReadOnly();
    internal AutomationFlowResult? GetFlowResult() => _flowResult;
}

internal enum FlowResultType
{
    Resolved,
    Escalated
}

internal class AutomationFlowResult
{
    public FlowResultType Type { get; set; }
    public string Message { get; set; } = string.Empty;
}
