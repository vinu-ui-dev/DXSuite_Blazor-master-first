using DXSuite.Sentinel.Services.Engine;

namespace DXSuite.Sentinel.Services.Automation;

/// <summary>
/// Executes automation definitions (both low-code JSON and code-based scripts) against devices.
/// </summary>
public interface IAutomationExecutor
{
    /// <summary>
    /// Execute an automation against a device instance
    /// </summary>
    Task<RemediationResult> ExecuteAutomationAsync(int automationId, int deviceInstanceId, CancellationToken ct = default);

    /// <summary>
    /// Execute an automation in dry-run mode (reads state but doesn't execute commands)
    /// </summary>
    Task<RemediationResult> DryRunAutomationAsync(int automationId, int deviceInstanceId, CancellationToken ct = default);
}
