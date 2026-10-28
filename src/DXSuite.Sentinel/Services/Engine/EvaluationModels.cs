using DXSuite.Shared.Services;

namespace DXSuite.Sentinel.Services.Engine;

/// <summary>
/// Context for evaluating a single device during the Service Tech cycle
/// </summary>
public class DeviceEvaluationContext
{
    public int DeviceInstanceId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public DeviceState State { get; set; } = new();
    public DeviceDescriptorSchema Descriptor { get; set; } = new();

    /// <summary>Per-source angles backing this device (empty for single-source devices). Fed to the Analyst engine.</summary>
    public List<DeviceSourceView> SourceViews { get; set; } = new();
}

public class IssueClassification
{
    public RemediationTier Tier { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? AutomationId { get; set; }
    public HealthRuleDefinition? HealthRule { get; set; }
}

public enum RemediationTier
{
    Tier1_RuleBased = 1,
    Tier2_AiAssisted = 2,
    Tier3_Advisory = 3
}

public class RemediationResult
{
    public RemediationOutcome Outcome { get; set; } = RemediationOutcome.None;
    public string Details { get; set; } = string.Empty;
    public List<string> ActionsExecuted { get; set; } = new();
    public string? AiDiagnosis { get; set; }
    public long DurationMs { get; set; }

    /// <summary>Persisted LLM interaction id for the diagnosis behind this result, when available.</summary>
    public long? LlmInteractionId { get; set; }
}

public enum RemediationOutcome
{
    None,
    Success,
    Failed,
    Escalated,
    Blocked
}

public class AutomationMatch
{
    public int AutomationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AuthorType { get; set; } = string.Empty;
}
