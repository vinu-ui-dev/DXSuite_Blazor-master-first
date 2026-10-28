namespace DXSuite.Shared.Services;

/// <summary>
/// Abstraction layer for routing LLM requests to configured providers.
/// Supports multiple providers (Claude, OpenAI, Groq, Ollama, Gemini) with per-customer configuration.
/// </summary>
public interface ILlmGateway
{
    Task<LlmDiagnosticResponse> GetDiagnosisAsync(LlmDiagnosticRequest request, CancellationToken cancellationToken = default);
    Task<LlmCompletionResponse> CompleteAsync(LlmCompletionRequest request, CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync(string? providerId = null, CancellationToken cancellationToken = default);
}

public class LlmDiagnosticRequest
{
    public string CustomerId { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string DeviceDisplayName { get; set; } = string.Empty;
    public List<string> AvailableCommands { get; set; } = new();
    public Dictionary<string, object> CurrentState { get; set; } = new();
    public List<string> RecentAlertHistory { get; set; } = new();
    public List<string> RecentActionHistory { get; set; } = new();
    public List<string> GuardrailConstraints { get; set; } = new();
    public string? AdditionalContext { get; set; }

    /// <summary>
    /// Per-source monitoring angles for this physical device (multi-angle identity). Lets the model
    /// reason over conflicting views — e.g. a gateway probe reporting Offline while the cloud connector
    /// still reports Healthy. Empty for single-source devices.
    /// </summary>
    public List<DeviceSourceView> SourceViews { get; set; } = new();
}

/// <summary>
/// One monitoring source's view of a device, as seen by the Analyst engine. Mirrors a
/// DeviceSourceState row reduced to what is useful for diagnosis.
/// </summary>
public class DeviceSourceView
{
    /// <summary>cloud-connector | heartbeat | gateway | agent-host.</summary>
    public string SourceType { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    /// <summary>This source's own health (Healthy/Warning/Critical/Offline/Unknown).</summary>
    public string Health { get; set; } = string.Empty;
    public DateTime LastSeenUtc { get; set; }
    /// <summary>A few notable readings from this source, for cross-source conflict reasoning.</summary>
    public Dictionary<string, object> KeyReadings { get; set; } = new();
}

public class LlmDiagnosticResponse
{
    public bool Success { get; set; }
    public string? Diagnosis { get; set; }
    public float Confidence { get; set; }
    public List<RecommendedAction> RecommendedActions { get; set; } = new();
    public bool ShouldEscalate { get; set; }
    public string? EscalationReason { get; set; }
    public string? ErrorMessage { get; set; }
    public LlmUsageInfo Usage { get; set; } = new();

    /// <summary>Id of the persisted LLM interaction record, when an interaction sink is configured.</summary>
    public long? InteractionId { get; set; }
}

public class RecommendedAction
{
    public string CommandId { get; set; } = string.Empty;
    public Dictionary<string, object>? Parameters { get; set; }
    public string Rationale { get; set; } = string.Empty;
    public int Order { get; set; }
}

public class LlmCompletionRequest
{
    public string? CustomerId { get; set; }
    public string? ProviderId { get; set; }
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
    public float Temperature { get; set; } = 0.1f;
    public int MaxTokens { get; set; } = 1000;
    public string? ResponseFormat { get; set; }
    public List<LlmMessageContent>? MultimodalContent { get; set; }
}

public class LlmMessageContent
{
    /// <summary>"text" or "image_url"</summary>
    public string Type { get; set; } = "text";
    public string Content { get; set; } = string.Empty;
    public string? MimeType { get; set; }
}

public class LlmCompletionResponse
{
    public bool Success { get; set; }
    public string? Content { get; set; }
    public string? ErrorMessage { get; set; }
    public LlmUsageInfo Usage { get; set; } = new();
}

public class LlmUsageInfo
{
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public long LatencyMs { get; set; }
}
