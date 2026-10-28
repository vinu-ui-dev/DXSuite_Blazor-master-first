using DXSuite.Sentinel.Services.Engine;

namespace DXSuite.Sentinel.Services.Automation;

/// <summary>
/// Sandboxed API surface for automation scripts (both low-code and Roslyn-scripted).
/// Provides device state access, command execution (validated against guardrails),
/// flow control, and multi-device coordination.
/// No direct DB, file, or network access is exposed.
/// </summary>
public interface IAutomationContext
{
    // Device state
    DeviceState GetDeviceState(int deviceInstanceId);
    IReadOnlyList<ActiveAlert> GetActiveAlerts(int deviceInstanceId);
    IReadOnlyList<MonitorReading> GetHistory(int deviceInstanceId, string monitorPointId, TimeSpan lookback);

    // Commands (validated against guardrails before execution)
    Task<CommandResult> ExecuteCommandAsync(int deviceInstanceId, string commandId, Dictionary<string, object>? parameters = null);

    // Tasks (scripting engine) — runs a stored Task on the agent (site gateway) serving the current device.
    Task<CommandResult> ExecuteScriptAsync(int taskId);

    // Flow control
    Task WaitAsync(TimeSpan duration);
    void Log(string message);
    void Resolve(string summary);
    void Escalate(string reason);

    // Multi-device
    IReadOnlyList<int> GetDevicesAtLocation(int locationId);
    IReadOnlyList<int> GetDevicesByType(string descriptorDeviceType);

    // Context info
    int CurrentDeviceInstanceId { get; }
    string CurrentDeviceType { get; }
    bool IsDryRun { get; }
}
