using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DXSuite.Sentinel.Entities;

namespace DXSuite.Sentinel.Services.Engine;

/// <summary>
/// Evaluates remediation permissions against customer-configured guardrail policies.
/// Checks max retry limits, cooldown periods, business hours, and per-device/location/type action allowlists.
/// </summary>
public class GuardrailService : IGuardrailService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GuardrailService> _logger;

    // Per-cycle policy cache: cleared when a new evaluation cycle starts
    private Dictionary<string, GuardrailPolicy?> _policyCache = new();

    // Tolerate either casing in hand-authored PolicyJson.
    private static readonly JsonSerializerOptions PolicyJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public GuardrailService(IServiceScopeFactory scopeFactory, ILogger<GuardrailService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Clear the policy cache at the start of each evaluation cycle so fresh policies are loaded.
    /// </summary>
    public void ResetCache() => _policyCache = new();

    public async Task<bool> CanRemediateAsync(int deviceInstanceId, string customerId, CancellationToken ct = default)
    {
        var policy = await LoadPolicyAsync(customerId, ct);
        if (policy == null) return true; // No policy = no restrictions

        // Check max remediation attempts
        var recentAttempts = await GetRecentRemediationCountAsync(deviceInstanceId, policy.CooldownMinutes, ct);
        if (recentAttempts >= policy.MaxRemediationAttemptsPerDevice)
        {
            _logger.LogInformation("Device {DeviceId} has reached max remediation attempts ({Count}/{Max})",
                deviceInstanceId, recentAttempts, policy.MaxRemediationAttemptsPerDevice);
            return false;
        }

        // Check business hours
        if (policy.BusinessHoursOnly)
        {
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById(policy.Timezone ?? "Eastern Standard Time"));
            if (now.TimeOfDay < policy.BusinessHoursStart || now.TimeOfDay > policy.BusinessHoursEnd)
            {
                _logger.LogInformation("Remediation blocked for device {DeviceId}: outside business hours", deviceInstanceId);
                return false;
            }
        }

        return true;
    }

    public async Task<EscalationSettings> GetEscalationAsync(string customerId, CancellationToken ct = default)
    {
        var policy = await LoadPolicyAsync(customerId, ct);
        return policy?.Escalation ?? new EscalationSettings();
    }

    public async Task<bool> IsActionAllowedAsync(int deviceInstanceId, string customerId, string actionId, CancellationToken ct = default)
    {
        var policy = await LoadPolicyAsync(customerId, ct);
        if (policy == null) return true;

        // Check blocked actions
        if (policy.BlockedActions.Contains(actionId, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Action {ActionId} is blocked by policy for customer {CustomerId}", actionId, customerId);
            return false;
        }

        // Check allowed actions (if allowlist is defined, action must be in it)
        if (policy.AllowedActions.Count > 0 && !policy.AllowedActions.Contains(actionId, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Action {ActionId} is not in the allowlist for customer {CustomerId}", actionId, customerId);
            return false;
        }

        return true;
    }

    private async Task<GuardrailPolicy?> LoadPolicyAsync(string customerId, CancellationToken ct)
    {
        if (_policyCache.TryGetValue(customerId, out var cached))
            return cached;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

            // Load active policies for this customer, ordered by specificity (most specific scope first)
            var policies = await sentinelDb.RemediationPolicies
                .AsNoTracking()
                .Where(p => p.IsActive && (p.CustomerId == customerId || p.Scope == "global"))
                .OrderByDescending(p => p.Scope == "device-level" ? 3 : p.Scope == "global" ? 1 : 2)
                .ToListAsync(ct);

            // Most specific match wins: device-level > customer > global
            var bestMatch = policies.FirstOrDefault(p => p.CustomerId == customerId)
                ?? policies.FirstOrDefault(p => p.Scope == "global");

            GuardrailPolicy? policy = null;
            if (bestMatch != null)
            {
                policy = JsonSerializer.Deserialize<GuardrailPolicy>(bestMatch.PolicyJson, PolicyJsonOptions);
            }

            _policyCache[customerId] = policy;
            return policy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load guardrail policy for customer {CustomerId}", customerId);
            _policyCache[customerId] = null;
            return null;
        }
    }

    private async Task<int> GetRecentRemediationCountAsync(int deviceInstanceId, int cooldownMinutes, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

            var cutoff = DateTime.UtcNow.AddMinutes(-cooldownMinutes);
            return await sentinelDb.RemediationLogs
                .AsNoTracking()
                .CountAsync(r => r.DeviceInstanceId == deviceInstanceId && r.CreateDate > cutoff, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to count recent remediations for device {DeviceId}", deviceInstanceId);
            return 0;
        }
    }
}

/// <summary>
/// Parsed guardrail policy from RemediationPolicy.PolicyJson
/// </summary>
public class GuardrailPolicy
{
    public int MaxRemediationAttemptsPerDevice { get; set; } = 3;
    public int CooldownMinutes { get; set; } = 30;
    public bool BusinessHoursOnly { get; set; }
    public TimeSpan BusinessHoursStart { get; set; } = TimeSpan.FromHours(6);
    public TimeSpan BusinessHoursEnd { get; set; } = TimeSpan.FromHours(22);
    public string? Timezone { get; set; }
    public List<string> AllowedActions { get; set; } = new();
    public List<string> BlockedActions { get; set; } = new();
    public bool RequiresApproval { get; set; }

    /// <summary>Per-tenant escalation thresholds: which issues are routed to the Tier-2 AI engine.</summary>
    public EscalationSettings Escalation { get; set; } = new();
}

/// <summary>
/// Controls when an unhandled issue (no Tier-1 automation or health-rule match) escalates to Tier-2
/// AI diagnosis vs. Tier-3 advisory. Defaults preserve the historical behavior (AI on 2+ alerts).
/// </summary>
public class EscalationSettings
{
    /// <summary>Allow Tier-2 AI diagnosis at all. When false, unhandled issues go straight to advisory.</summary>
    public bool AiAssistEnabled { get; set; } = true;

    /// <summary>Active-alert count at or above which an issue escalates to AI diagnosis.</summary>
    public int Tier2MinAlerts { get; set; } = 2;

    /// <summary>Escalate to AI on any single Critical reading/alert, even below the alert-count threshold.</summary>
    public bool EscalateOnCritical { get; set; } = true;
}
