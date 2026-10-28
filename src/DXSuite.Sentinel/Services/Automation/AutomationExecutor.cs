using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DXSuite.Sentinel.Entities;
using DXSuite.Sentinel.Services.Engine;
using DXSuite.Sentinel.Services.Routing;

namespace DXSuite.Sentinel.Services.Automation;

/// <summary>
/// Executes automation definitions by parsing the JSON step definitions
/// and running them in sequence. Supports low-code (JSON steps) and code-based (C# script) automations.
/// </summary>
public class AutomationExecutor : IAutomationExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICommandRouter _commandRouter;
    private readonly IGuardrailService _guardrailService;
    private readonly ILogger<AutomationExecutor> _logger;

    public AutomationExecutor(
        IServiceScopeFactory scopeFactory,
        ICommandRouter commandRouter,
        IGuardrailService guardrailService,
        ILogger<AutomationExecutor> logger)
    {
        _scopeFactory = scopeFactory;
        _commandRouter = commandRouter;
        _guardrailService = guardrailService;
        _logger = logger;
    }

    public Task<RemediationResult> ExecuteAutomationAsync(int automationId, int deviceInstanceId, CancellationToken ct = default)
    {
        return ExecuteInternalAsync(automationId, deviceInstanceId, isDryRun: false, ct);
    }

    public Task<RemediationResult> DryRunAutomationAsync(int automationId, int deviceInstanceId, CancellationToken ct = default)
    {
        return ExecuteInternalAsync(automationId, deviceInstanceId, isDryRun: true, ct);
    }

    private async Task<RemediationResult> ExecuteInternalAsync(int automationId, int deviceInstanceId, bool isDryRun, CancellationToken ct)
    {
        var result = new RemediationResult();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int stepsExecuted = 0;
        AutomationDefinition? automation = null;
        AutomationVersion? activeVersion = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

            automation = await sentinelDb.AutomationDefinitions
                .AsNoTracking()
                .Include(a => a.AutomationVersions)
                .FirstOrDefaultAsync(a => a.AutomationId == automationId, ct);

            if (automation == null)
            {
                result.Outcome = RemediationOutcome.Failed;
                result.Details = $"Automation {automationId} not found";
                return result;
            }

            activeVersion = automation.AutomationVersions.FirstOrDefault(v => v.IsActive)
                ?? automation.AutomationVersions.FirstOrDefault(v => v.Version == automation.CurrentVersion);

            if (activeVersion == null)
            {
                result.Outcome = RemediationOutcome.Failed;
                result.Details = $"No active version found for automation '{automation.Name}'";
                return result;
            }

            // Load device context for customerId and deviceType
            var device = await sentinelDb.DeviceInstances
                .AsNoTracking()
                .Include(d => d.DeviceDescriptor)
                .FirstOrDefaultAsync(d => d.DeviceInstanceId == deviceInstanceId, ct);

            var customerId = device?.CustomerId ?? "unknown";
            var deviceType = device?.DeviceDescriptor?.DeviceType ?? "unknown";

            _logger.LogInformation("Executing automation '{Name}' v{Version} for device {DeviceId} (DryRun={DryRun})",
                automation.Name, activeVersion.Version, deviceInstanceId, isDryRun);

            var context = new AutomationContext(
                deviceInstanceId,
                deviceType,
                customerId,
                isDryRun,
                _commandRouter,
                _guardrailService,
                _scopeFactory);

            // Parse and execute the low-code steps
            var stepsDoc = JsonDocument.Parse(activeVersion.ContentJson);
            if (stepsDoc.RootElement.TryGetProperty("steps", out var stepsElement))
            {
                await ExecuteLowCodeStepsAsync(stepsElement, context, ct);
            }
            else
            {
                // ContentJson may be the steps array directly
                await ExecuteLowCodeStepsAsync(stepsDoc.RootElement, context, ct);
            }

            stepsExecuted = context.GetLogEntries().Count;

            var flowResult = context.GetFlowResult();
            if (flowResult != null)
            {
                result.Outcome = flowResult.Type == FlowResultType.Resolved
                    ? RemediationOutcome.Success
                    : RemediationOutcome.Escalated;
                result.Details = flowResult.Message;
            }
            else
            {
                result.Outcome = RemediationOutcome.Failed;
                result.Details = "Automation completed without explicit resolve or escalate";
            }
        }
        catch (Exception ex)
        {
            result.Outcome = RemediationOutcome.Failed;
            result.Details = $"Automation execution error: {ex.Message}";
            _logger.LogError(ex, "Error executing automation {AutomationId}", automationId);
        }
        finally
        {
            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;

            // Persist execution log
            if (automation != null && activeVersion != null)
            {
                await SaveExecutionLogAsync(automation.AutomationId, activeVersion.VersionId,
                    deviceInstanceId, result, stepsExecuted);
            }
        }

        return result;
    }

    private async Task SaveExecutionLogAsync(int automationId, int versionId,
        int deviceInstanceId, RemediationResult result, int stepsExecuted)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

            var log = new AutomationExecutionLog
            {
                AutomationId = automationId,
                AutomationVersionId = versionId,
                DeviceInstanceId = deviceInstanceId,
                Status = result.Outcome switch
                {
                    RemediationOutcome.Success => "success",
                    RemediationOutcome.Escalated => "escalated",
                    _ => "failed"
                },
                StepsExecuted = stepsExecuted,
                DurationMs = result.DurationMs,
                ErrorMessage = result.Outcome == RemediationOutcome.Failed
                    ? result.Details?.Length > 500 ? result.Details[..497] + "..." : result.Details
                    : null,
                CreateDate = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };

            sentinelDb.AutomationExecutionLogs.Add(log);
            await sentinelDb.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save execution log for automation {AutomationId}", automationId);
        }
    }

    /// <summary>
    /// Execute low-code JSON steps sequentially
    /// </summary>
    private async Task ExecuteLowCodeStepsAsync(JsonElement steps, AutomationContext context, CancellationToken ct)
    {
        if (steps.ValueKind != JsonValueKind.Array) return;

        foreach (var step in steps.EnumerateArray())
        {
            if (ct.IsCancellationRequested) break;
            if (context.GetFlowResult() != null) break; // resolved or escalated

            var stepType = step.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

            switch (stepType)
            {
                case "execute":
                    var commandId = step.GetProperty("commandId").GetString()!;
                    Dictionary<string, object>? parameters = null;
                    if (step.TryGetProperty("parameters", out var paramsProp))
                    {
                        parameters = new Dictionary<string, object>();
                        foreach (var p in paramsProp.EnumerateObject())
                        {
                            parameters[p.Name] = p.Value.ValueKind switch
                            {
                                JsonValueKind.String => p.Value.GetString()!,
                                JsonValueKind.Number => p.Value.GetDouble(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                _ => p.Value.GetRawText()
                            };
                        }
                    }
                    await context.ExecuteCommandAsync(context.CurrentDeviceInstanceId, commandId, parameters);
                    break;

                case "run-script":
                    var taskId = step.GetProperty("taskId").GetInt32();
                    await context.ExecuteScriptAsync(taskId);
                    break;

                case "wait":
                    var seconds = step.GetProperty("seconds").GetInt32();
                    await context.WaitAsync(TimeSpan.FromSeconds(seconds));
                    break;

                case "check":
                    var monitorPoint = step.GetProperty("monitorPoint").GetString()!;
                    var state = context.GetDeviceState(context.CurrentDeviceInstanceId);
                    var reading = state.GetMonitorPoint(monitorPoint);
                    var value = reading?.Value?.ToString() ?? "unknown";

                    if (step.TryGetProperty("branches", out var branches))
                    {
                        JsonElement? matchedBranch = null;
                        if (branches.TryGetProperty(value, out var exactMatch))
                        {
                            matchedBranch = exactMatch;
                        }
                        else if (branches.TryGetProperty("default", out var defaultBranch))
                        {
                            matchedBranch = defaultBranch;
                        }

                        if (matchedBranch.HasValue)
                        {
                            await ExecuteLowCodeStepsAsync(matchedBranch.Value, context, ct);
                        }
                    }
                    break;

                case "resolve":
                    var summary = step.TryGetProperty("summary", out var sumProp) ? sumProp.GetString() : "Resolved";
                    context.Resolve(summary ?? "Resolved");
                    break;

                case "escalate":
                    var reason = step.TryGetProperty("reason", out var reasonProp) ? reasonProp.GetString() : "Escalated";
                    var tier = step.TryGetProperty("tier", out var tierProp) ? tierProp.GetInt32() : 3;
                    context.Escalate($"Tier {tier}: {reason}");
                    break;
            }
        }
    }
}
