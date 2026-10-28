using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using DXSuite.Sentinel.Entities;
using DXSuite.Sentinel.Services.Drivers;
using DXSuite.Shared.Services;
using DXSuite.Sentinel.Services.Routing;
using DXSuite.Sentinel.Services.Automation;

namespace DXSuite.Sentinel.Services.Engine;

/// <summary>
/// Implements the ASSESS -> CLASSIFY -> ACT -> LOG -> NOTIFY evaluation pipeline.
/// </summary>
public class DeviceEvaluator : IDeviceEvaluator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICommandRouter _commandRouter;
    private readonly DeviceDriverRegistry _driverRegistry;
    private readonly IGuardrailService _guardrailService;
    private readonly IAutomationExecutor _automationExecutor;
    private readonly ILlmGateway _llmGateway;
    private readonly ILogger<DeviceEvaluator> _logger;

    public DeviceEvaluator(
        IServiceScopeFactory scopeFactory,
        ICommandRouter commandRouter,
        DeviceDriverRegistry driverRegistry,
        IGuardrailService guardrailService,
        IAutomationExecutor automationExecutor,
        ILlmGateway llmGateway,
        ILogger<DeviceEvaluator> logger)
    {
        _scopeFactory = scopeFactory;
        _commandRouter = commandRouter;
        _driverRegistry = driverRegistry;
        _guardrailService = guardrailService;
        _automationExecutor = automationExecutor;
        _llmGateway = llmGateway;
        _logger = logger;
    }

    public async Task RunEvaluationCycleAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting evaluation cycle");

        // Reload guardrail policies each cycle so policy edits (enable/disable, limits) take effect live.
        _guardrailService.ResetCache();

        // 1. ASSESS - Get all enabled devices with their current states
        var devices = await GetEnabledDevicesWithStatesAsync(cancellationToken);
        _logger.LogDebug("Evaluating {Count} enabled devices", devices.Count);

        foreach (var device in devices)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                // Only evaluate devices that have active alerts or unhealthy state
                if (device.State.OverallHealth == OverallHealthStatus.Healthy)
                    continue;

                // 2. CLASSIFY - Determine which tier handles this
                var classification = await ClassifyIssueAsync(device, cancellationToken);

                // 3. ACT - Execute remediation based on tier
                var result = await ActOnClassificationAsync(device, classification, cancellationToken);

                // 4. LOG - Record the decision and result
                await LogRemediationAsync(device, classification, result, cancellationToken);

                // 5. NOTIFY - Signal to Reporter (async, don't block)
                await NotifyReporterAsync(device, classification, result, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating device {DeviceInstanceId}", device.DeviceInstanceId);
            }
        }

        _logger.LogDebug("Evaluation cycle complete");
    }

    private async Task<List<DeviceEvaluationContext>> GetEnabledDevicesWithStatesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

        var devices = await sentinelDb.DeviceInstances
            .AsNoTracking()
            .Where(d => d.IsEnabled && d.LastStateJson != null)
            .Include(d => d.DeviceDescriptor)
            .Include(d => d.DeviceDescriptorVersion)
            .ToListAsync(ct);

        // Per-source angles for the same devices (multi-angle identity), loaded in one query and
        // grouped by device so the Analyst engine can reason over conflicting source views.
        var deviceIds = devices.Select(d => d.DeviceInstanceId).ToList();
        var sourcesByDevice = (await sentinelDb.DeviceSourceStates
                .AsNoTracking()
                .Where(s => deviceIds.Contains(s.DeviceInstanceId))
                .ToListAsync(ct))
            .GroupBy(s => s.DeviceInstanceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var results = new List<DeviceEvaluationContext>();

        foreach (var device in devices)
        {
            try
            {
                var state = JsonSerializer.Deserialize<DeviceState>(device.LastStateJson!);
                if (state == null) continue;

                // Use the linked descriptor version, or fall back to the active version for the descriptor
                var descriptorJson = device.DeviceDescriptorVersion?.DescriptorJson;
                if (string.IsNullOrEmpty(descriptorJson))
                {
                    _logger.LogWarning("Device {DeviceId} ({Name}) has no descriptor version, skipping",
                        device.DeviceInstanceId, device.DisplayName);
                    continue;
                }

                var descriptor = JsonSerializer.Deserialize<DeviceDescriptorSchema>(descriptorJson);
                if (descriptor == null) continue;

                results.Add(new DeviceEvaluationContext
                {
                    DeviceInstanceId = device.DeviceInstanceId,
                    CustomerId = device.CustomerId,
                    State = state,
                    Descriptor = descriptor,
                    SourceViews = sourcesByDevice.TryGetValue(device.DeviceInstanceId, out var srcs)
                        ? BuildSourceViews(srcs)
                        : new List<DeviceSourceView>()
                });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize state/descriptor for device {DeviceId}, skipping",
                    device.DeviceInstanceId);
            }
        }

        return results;
    }

    /// <summary>
    /// Reduce a device's per-source rows to the Analyst-facing view: each source's type/key, its own
    /// health, last-seen, and a handful of readings for cross-source conflict reasoning.
    /// </summary>
    private static List<DeviceSourceView> BuildSourceViews(List<DeviceSourceState> sources)
    {
        var views = new List<DeviceSourceView>(sources.Count);
        foreach (var s in sources)
        {
            var view = new DeviceSourceView
            {
                SourceType = s.SourceType,
                SourceKey = s.SourceKey,
                Health = ((OverallHealthStatus)s.OverallHealth).ToString(),
                LastSeenUtc = s.LastSeenUtc
            };

            if (!string.IsNullOrEmpty(s.LastStateJson))
            {
                try
                {
                    var sourceState = JsonSerializer.Deserialize<DeviceState>(s.LastStateJson);
                    if (sourceState != null)
                    {
                        foreach (var kvp in sourceState.MonitorReadings.Take(8))
                            view.KeyReadings[kvp.Key] = kvp.Value.Value ?? (object)"null";
                    }
                }
                catch (JsonException)
                {
                    // A single unreadable source blob shouldn't drop the angle; type/key/health still inform the model.
                }
            }

            views.Add(view);
        }
        return views;
    }

    private async Task<IssueClassification> ClassifyIssueAsync(DeviceEvaluationContext device, CancellationToken ct)
    {
        // Check Tier 1: custom automations matching trigger conditions (highest priority)
        // Check Tier 1: device descriptor healthRules (built-in defaults)
        // If no Tier 1 match and multiple symptoms -> Tier 2 (AI)
        // If complex/unknown -> Tier 3 (advisory)

        // Check custom automations first
        var matchingAutomation = await FindMatchingAutomationAsync(device, ct);
        if (matchingAutomation != null)
        {
            return new IssueClassification
            {
                Tier = RemediationTier.Tier1_RuleBased,
                Source = "custom-automation",
                AutomationId = matchingAutomation.AutomationId,
                Description = $"Matched automation: {matchingAutomation.Name}"
            };
        }

        // Check descriptor health rules
        var matchingRule = FindMatchingHealthRule(device);
        if (matchingRule != null)
        {
            return new IssueClassification
            {
                Tier = RemediationTier.Tier1_RuleBased,
                Source = "descriptor-health-rule",
                HealthRule = matchingRule,
                Description = $"Matched health rule: {matchingRule.Name}"
            };
        }

        // No Tier-1 match: the tenant's escalation policy decides whether this reaches Tier-2 AI.
        var escalation = await _guardrailService.GetEscalationAsync(device.CustomerId, ct);
        if (escalation.AiAssistEnabled)
        {
            var alertCount = device.State.ActiveAlerts.Count;
            var hasCritical = device.State.OverallHealth == OverallHealthStatus.Critical
                || device.State.ActiveAlerts.Any(a => string.Equals(a.Severity, "critical", StringComparison.OrdinalIgnoreCase))
                || device.State.MonitorReadings.Values.Any(r => string.Equals(r.Status, "critical", StringComparison.OrdinalIgnoreCase));

            if (alertCount >= escalation.Tier2MinAlerts || (escalation.EscalateOnCritical && hasCritical))
            {
                var reason = alertCount >= escalation.Tier2MinAlerts
                    ? $"{alertCount} alerts (>= threshold {escalation.Tier2MinAlerts})"
                    : "critical reading";
                return new IssueClassification
                {
                    Tier = RemediationTier.Tier2_AiAssisted,
                    Source = "multi-symptom",
                    Description = $"AI diagnosis needed: {reason}"
                };
            }
        }

        // Default to advisory
        return new IssueClassification
        {
            Tier = RemediationTier.Tier3_Advisory,
            Source = "unclassified",
            Description = "Issue could not be automatically classified"
        };
    }

    private async Task<RemediationResult> ActOnClassificationAsync(DeviceEvaluationContext device, IssueClassification classification, CancellationToken ct)
    {
        var result = new RemediationResult();

        // Check guardrails first
        if (!await _guardrailService.CanRemediateAsync(device.DeviceInstanceId, device.CustomerId, ct))
        {
            result.Outcome = RemediationOutcome.Blocked;
            result.Details = "Remediation blocked by guardrail policy";
            return result;
        }

        switch (classification.Tier)
        {
            case RemediationTier.Tier1_RuleBased:
                if (classification.AutomationId.HasValue)
                {
                    result = await _automationExecutor.ExecuteAutomationAsync(
                        classification.AutomationId.Value, device.DeviceInstanceId, ct);
                }
                else if (classification.HealthRule != null)
                {
                    result = await ExecuteHealthRuleActionsAsync(device, classification.HealthRule, ct);
                }
                break;

            case RemediationTier.Tier2_AiAssisted:
                result = await ExecuteAiAssistedRemediationAsync(device, ct);
                break;

            case RemediationTier.Tier3_Advisory:
                result.Outcome = RemediationOutcome.Escalated;
                result.Details = classification.Description;
                break;
        }

        return result;
    }

    private async Task<RemediationResult> ExecuteHealthRuleActionsAsync(DeviceEvaluationContext device, HealthRuleDefinition rule, CancellationToken ct)
    {
        var result = new RemediationResult();

        foreach (var actionId in rule.SuggestedActions)
        {
            if (ct.IsCancellationRequested) break;

            // Validate action against guardrails
            if (!await _guardrailService.IsActionAllowedAsync(device.DeviceInstanceId, device.CustomerId, actionId, ct))
            {
                _logger.LogDebug("Action {ActionId} blocked by guardrails for device {DeviceId}", actionId, device.DeviceInstanceId);
                continue;
            }

            var cmdResult = await _commandRouter.RouteCommandAsync(device.DeviceInstanceId, actionId, null, ct);
            if (cmdResult.Success)
            {
                result.Outcome = RemediationOutcome.Success;
                result.Details = $"Executed {actionId} successfully via {cmdResult.RoutedVia}";
                result.ActionsExecuted.Add(actionId);
                return result;
            }
            else
            {
                _logger.LogWarning("Health rule action {ActionId} failed for device {DeviceId}: {Error}", actionId, device.DeviceInstanceId, cmdResult.ErrorMessage);
                result.ActionsExecuted.Add(actionId);
            }
        }

        if (result.ActionsExecuted.Count > 0 && result.Outcome != RemediationOutcome.Success)
        {
            result.Outcome = RemediationOutcome.Failed;
            result.Details = "All suggested actions failed";
        }
        else
        {
            result.Outcome = RemediationOutcome.Escalated;
            result.Details = "No actions were allowed by guardrails";
        }

        return result;
    }

    private async Task<RemediationResult> ExecuteAiAssistedRemediationAsync(DeviceEvaluationContext device, CancellationToken ct)
    {
        var result = new RemediationResult();

        var diagRequest = new LlmDiagnosticRequest
        {
            CustomerId = device.CustomerId,
            DeviceType = device.Descriptor.DeviceType,
            DeviceDisplayName = device.State.DisplayName,
            AvailableCommands = device.Descriptor.Commands.Select(c => c.Id).ToList(),
            CurrentState = device.State.MonitorReadings.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Value ?? (object)"null"),
            RecentAlertHistory = device.State.ActiveAlerts.Select(a => $"{a.TriggeredAt:u}: {a.Message}").ToList(),
            SourceViews = device.SourceViews
        };

        var diagnosis = await _llmGateway.GetDiagnosisAsync(diagRequest, ct);
        result.LlmInteractionId = diagnosis.InteractionId;

        if (!diagnosis.Success || diagnosis.ShouldEscalate)
        {
            result.Outcome = RemediationOutcome.Escalated;
            result.Details = diagnosis.Diagnosis ?? diagnosis.EscalationReason ?? "AI diagnosis failed";
            result.AiDiagnosis = diagnosis.Diagnosis;
            return result;
        }

        // Execute recommended actions (validated against guardrails)
        foreach (var action in diagnosis.RecommendedActions.OrderBy(a => a.Order))
        {
            if (!await _guardrailService.IsActionAllowedAsync(device.DeviceInstanceId, device.CustomerId, action.CommandId, ct))
            {
                _logger.LogDebug("AI-recommended action {ActionId} blocked by guardrails", action.CommandId);
                continue;
            }

            // Validate command exists in descriptor
            if (!device.Descriptor.Commands.Any(c => c.Id == action.CommandId))
            {
                _logger.LogWarning("AI recommended non-existent command {CommandId}, skipping", action.CommandId);
                continue;
            }

            var cmdResult = await _commandRouter.RouteCommandAsync(device.DeviceInstanceId, action.CommandId, action.Parameters, ct);
            result.ActionsExecuted.Add(action.CommandId);

            if (cmdResult.Success)
            {
                result.Outcome = RemediationOutcome.Success;
                result.Details = $"AI diagnosis: {diagnosis.Diagnosis}. Action {action.CommandId} succeeded.";
                result.AiDiagnosis = diagnosis.Diagnosis;
                return result;
            }
        }

        result.Outcome = result.ActionsExecuted.Count > 0 ? RemediationOutcome.Failed : RemediationOutcome.Escalated;
        result.Details = $"AI diagnosis: {diagnosis.Diagnosis}. Actions did not resolve the issue.";
        result.AiDiagnosis = diagnosis.Diagnosis;
        return result;
    }

    private async Task<AutomationMatch?> FindMatchingAutomationAsync(DeviceEvaluationContext device, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

        // Find the descriptor ID for this device's type
        var descriptorId = await sentinelDb.DeviceInstances
            .AsNoTracking()
            .Where(d => d.DeviceInstanceId == device.DeviceInstanceId)
            .Select(d => d.DescriptorId)
            .FirstOrDefaultAsync(ct);

        var candidates = await sentinelDb.AutomationDefinitions
            .AsNoTracking()
            .Include(a => a.AutomationVersions)
            .Where(a => a.Status == "published"
                && (a.DeviceDescriptorId == null || a.DeviceDescriptorId == descriptorId)
                && a.AutomationVersions.Any(v => v.IsActive))
            .OrderByDescending(a => a.DeviceDescriptorId.HasValue) // device-specific first
            .ThenByDescending(a => a.Priority)
            .ToListAsync(ct);

        foreach (var automation in candidates)
        {
            try
            {
                var trigger = JsonSerializer.Deserialize<TriggerDefinition>(automation.TriggerJson);
                if (trigger != null && MatchesTrigger(trigger, device.State))
                {
                    return new AutomationMatch
                    {
                        AutomationId = automation.AutomationId,
                        Name = automation.Name,
                        AuthorType = automation.AuthorType
                    };
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse trigger JSON for automation {AutomationId}", automation.AutomationId);
            }
        }

        return null;
    }

    private static bool MatchesTrigger(TriggerDefinition trigger, DeviceState state)
    {
        if (trigger.Conditions == null || trigger.Conditions.Count == 0)
            return false;

        var isMatchAll = string.Equals(trigger.MatchMode, "all", StringComparison.OrdinalIgnoreCase);

        foreach (var condition in trigger.Conditions)
        {
            var reading = state.GetMonitorPoint(condition.MonitorPoint);
            var actualValue = reading?.Value?.ToString() ?? "";
            var matched = EvaluateCondition(actualValue, condition.Operator, condition.Value);

            if (isMatchAll && !matched) return false;
            if (!isMatchAll && matched) return true;
        }

        // "all" mode: all passed; "any" mode: none matched
        return isMatchAll;
    }

    private static bool EvaluateCondition(string actual, string op, string expected)
    {
        return op switch
        {
            "eq" => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            "neq" => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            "contains" => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            "gt" => double.TryParse(actual, out var a) && double.TryParse(expected, out var b) && a > b,
            "lt" => double.TryParse(actual, out var c) && double.TryParse(expected, out var d) && c < d,
            _ => false
        };
    }

    private HealthRuleDefinition? FindMatchingHealthRule(DeviceEvaluationContext device)
    {
        foreach (var rule in device.Descriptor.HealthRules)
        {
            var monitorReading = device.State.GetMonitorPoint(rule.Condition.MonitorPoint);
            if (monitorReading != null && monitorReading.Status == rule.Condition.State)
            {
                return rule;
            }
        }
        return null;
    }

    private async Task LogRemediationAsync(DeviceEvaluationContext device, IssueClassification classification, RemediationResult result, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

            var log = new RemediationLog
            {
                DeviceInstanceId = device.DeviceInstanceId,
                Tier = (int)classification.Tier,
                TriggerCondition = Truncate($"[{classification.Source}] {classification.Description}", 200),
                ActionTaken = Truncate(string.Join(", ", result.ActionsExecuted), 200),
                Result = result.Outcome.ToString(),
                ResultDetails = result.Details,
                AutomationId = classification.AutomationId,
                LlmInteractionId = result.LlmInteractionId,
                DurationMs = result.DurationMs,
                CreateDate = DateTime.UtcNow
            };

            sentinelDb.RemediationLogs.Add(log);
            await sentinelDb.SaveChangesAsync(ct);

            _logger.LogDebug(
                "Remediation logged: Device={DeviceId}, Tier={Tier}, Outcome={Outcome}",
                device.DeviceInstanceId, classification.Tier, result.Outcome);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist remediation log for device {DeviceId}", device.DeviceInstanceId);
        }
    }

    private Task NotifyReporterAsync(DeviceEvaluationContext device, IssueClassification classification, RemediationResult result, CancellationToken ct)
    {
        // Reporter uses watermark-based polling on RemediationLog, no explicit signaling needed
        return Task.CompletedTask;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }

    // Parsed trigger definition from AutomationDefinition.TriggerJson
    private class TriggerDefinition
    {
        [System.Text.Json.Serialization.JsonPropertyName("conditions")]
        public List<TriggerCondition> Conditions { get; set; } = new();

        [System.Text.Json.Serialization.JsonPropertyName("matchMode")]
        public string MatchMode { get; set; } = "any";
    }

    private class TriggerCondition
    {
        [System.Text.Json.Serialization.JsonPropertyName("monitorPoint")]
        public string MonitorPoint { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("operator")]
        public string Operator { get; set; } = "eq";

        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;
    }
}
