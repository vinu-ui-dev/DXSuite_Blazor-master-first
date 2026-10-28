namespace DXSuite.Sentinel.Services.Engine;

/// <summary>
/// Evaluates remediation guardrails - customer-configurable limits on what the Service Tech
/// can do, when, and to which devices.
/// </summary>
public interface IGuardrailService
{
    /// <summary>
    /// Check if any remediation is allowed for this device (max attempts, cooldown, business hours)
    /// </summary>
    Task<bool> CanRemediateAsync(int deviceInstanceId, string customerId, CancellationToken ct = default);

    /// <summary>
    /// Check if a specific action is allowed for a device
    /// </summary>
    Task<bool> IsActionAllowedAsync(int deviceInstanceId, string customerId, string actionId, CancellationToken ct = default);

    /// <summary>
    /// Resolve the per-tenant escalation thresholds (which issues route to Tier-2 AI). Returns sensible
    /// defaults when the tenant has no policy.
    /// </summary>
    Task<EscalationSettings> GetEscalationAsync(string customerId, CancellationToken ct = default);

    /// <summary>
    /// Drop the per-cycle policy cache so subsequent checks reload fresh policies. Call at the start of
    /// each evaluation cycle so guardrail-policy edits take effect without an app restart.
    /// </summary>
    void ResetCache();
}
