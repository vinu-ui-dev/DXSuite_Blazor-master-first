namespace DXSuite.Shared.Services;

/// <summary>
/// Configuration for an LLM provider instance.
/// </summary>
public class LlmProviderConfig
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderType { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public string Model { get; set; } = string.Empty;
    public int MaxTokens { get; set; } = 2000;
    public float DefaultTemperature { get; set; } = 0.1f;
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// LLM configuration section (from appsettings.json).
/// </summary>
public class LlmOptions
{
    public const string SectionName = "Llm";

    public string DefaultProviderId { get; set; } = "default";
    public float DiagnosisConfidenceThreshold { get; set; } = 0.7f;
    public Dictionary<string, LlmProviderConfig> Providers { get; set; } = new();
}

/// <summary>
/// Interface for individual LLM provider implementations.
/// </summary>
public interface ILlmProvider
{
    string ProviderType { get; }
    bool SupportsVision => false;

    Task<LlmCompletionResponse> CompleteAsync(LlmProviderConfig config, string systemPrompt, string userPrompt, float temperature, int maxTokens, string? responseFormat, CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync(LlmProviderConfig config, CancellationToken cancellationToken = default);

    Task<LlmCompletionResponse> CompleteMultimodalAsync(LlmProviderConfig config, string systemPrompt, List<LlmMessageContent> content, float temperature, int maxTokens, string? responseFormat, CancellationToken cancellationToken = default)
    {
        var textParts = content.Where(c => c.Type == "text").Select(c => c.Content);
        var combinedText = string.Join("\n", textParts);
        return CompleteAsync(config, systemPrompt, combinedText, temperature, maxTokens, responseFormat, cancellationToken);
    }
}

/// <summary>
/// Registry of fixed LLM use cases with template variables and default prompts.
/// </summary>
public static class LlmUseCases
{
    public static readonly LlmUseCase ContentModeration = new()
    {
        Id = "content-moderation",
        DisplayName = "Content Moderation",
        Description = "Scans user-generated text and images for inappropriate content on digital signage.",
        Variables = new[]
        {
            new LlmVariable("{message_text}", "The combined text to evaluate for policy violations"),
            new LlmVariable("{image_base64}", "Base64-encoded image to evaluate (vision-capable models only)")
        },
        DefaultPrompt = @"You are a content moderation system for digital signage displays. Evaluate text for appropriateness in a professional, public-facing environment.

Flag content as inappropriate if it contains:
- Profanity, vulgar language, or obscenities
- Contact information (phone numbers, email addresses)
- URLs or website addresses
- Sexual content or innuendo
- Hate speech or discriminatory language
- Violence or threats
- Any unprofessional content inappropriate for a general audience

Use MODERATE evaluation - balance between safety and usability.",
        DefaultTemperature = 0.0f,
        DefaultMaxTokens = 200,
        DefaultResponseFormat = "json"
    };

    public static readonly LlmUseCase ServiceTechDiagnosis = new()
    {
        Id = "service-tech-diagnosis",
        DisplayName = "Service Tech Diagnosis",
        Description = "AI-assisted device diagnosis for the Tier 2 evaluation pipeline.",
        Variables = new[]
        {
            new LlmVariable("{device_state}", "Current device state as key-value pairs"),
            new LlmVariable("{alert_history}", "Recent alert history for the device"),
            new LlmVariable("{action_history}", "Recent remediation actions taken"),
            new LlmVariable("{available_commands}", "List of available command IDs the LLM may recommend"),
            new LlmVariable("{guardrail_constraints}", "Policy constraints the LLM must respect")
        },
        DefaultPrompt = @"You are a device diagnostic AI for the Sentinel monitoring platform.
You analyze device health data and recommend remediation actions.

Available commands you can recommend (ONLY use these exact command IDs):
{available_commands}

Guardrail constraints (you MUST respect these):
{guardrail_constraints}

Current device state:
{device_state}

Recent alert history:
{alert_history}

Recent actions taken:
{action_history}

Respond with ONLY valid JSON in this exact format:
{
  ""diagnosis"": ""brief explanation of the issue"",
  ""confidence"": 0.0 to 1.0,
  ""recommendedActions"": [
    {
      ""commandId"": ""exact command ID from the available list"",
      ""params"": {},
      ""rationale"": ""why this action should help""
    }
  ],
  ""escalate"": true/false,
  ""escalationReason"": ""reason if escalating""
}

IMPORTANT: Only recommend commands from the available list. If you cannot diagnose the issue or no appropriate command is available, set escalate to true.",
        DefaultTemperature = 0.1f,
        DefaultMaxTokens = 2000,
        DefaultResponseFormat = "json"
    };

    public static readonly LlmUseCase HealthAnalysis = new()
    {
        Id = "health-analysis",
        DisplayName = "Health Analysis",
        Description = "Summarizes device health state into a human-readable assessment.",
        Variables = new[]
        {
            new LlmVariable("{device_state}", "Current device state as key-value pairs"),
            new LlmVariable("{device_type}", "Device type slug (e.g. samsung-qm85r)"),
            new LlmVariable("{device_name}", "Human-readable device display name")
        },
        DefaultPrompt = @"Analyze the health of the following device and provide a brief summary.

Device: {device_name} ({device_type})

State:
{device_state}

Respond with a concise 1-3 sentence health summary suitable for a dashboard tooltip.",
        DefaultTemperature = 0.2f,
        DefaultMaxTokens = 300,
        DefaultResponseFormat = null
    };

    public static readonly LlmUseCase DeviceAgeAssessment = new()
    {
        Id = "device-age-assessment",
        DisplayName = "Device Age Assessment",
        Description = "Estimates device age and lifecycle status from state data.",
        Variables = new[]
        {
            new LlmVariable("{device_state}", "Current device state as key-value pairs"),
            new LlmVariable("{device_type}", "Device type slug"),
            new LlmVariable("{commissioned_date}", "Date the device was commissioned (may be empty)")
        },
        DefaultPrompt = @"Estimate the age and lifecycle status of this device.

Device type: {device_type}
Commissioned: {commissioned_date}

State:
{device_state}

Respond with ONLY valid JSON:
{
  ""estimated_age_years"": number or null,
  ""lifecycle_status"": ""new"" | ""active"" | ""aging"" | ""end-of-life"",
  ""confidence"": 0.0 to 1.0,
  ""reasoning"": ""brief explanation""
}",
        DefaultTemperature = 0.1f,
        DefaultMaxTokens = 300,
        DefaultResponseFormat = "json"
    };

    public static readonly LlmUseCase IncidentSummarization = new()
    {
        Id = "incident-summarization",
        DisplayName = "Incident Summarization",
        Description = "Produces a summary of a device incident for ITSM ticket creation.",
        Variables = new[]
        {
            new LlmVariable("{device_state}", "Current device state at time of incident"),
            new LlmVariable("{alert_history}", "Alert history leading up to the incident"),
            new LlmVariable("{action_history}", "Remediation actions attempted"),
            new LlmVariable("{ticket_context}", "Existing ticket info if this is an update")
        },
        DefaultPrompt = @"Summarize this device incident for an ITSM ticket.

Device state:
{device_state}

Alert history:
{alert_history}

Actions taken:
{action_history}

Existing ticket context:
{ticket_context}

Write a clear, professional incident summary suitable for a service desk ticket. Include:
1. What happened (symptoms)
2. What was tried (remediation)
3. Current status
4. Recommended next steps",
        DefaultTemperature = 0.3f,
        DefaultMaxTokens = 500,
        DefaultResponseFormat = null
    };

    public static readonly IReadOnlyList<LlmUseCase> All = new[]
    {
        ContentModeration,
        ServiceTechDiagnosis,
        HealthAnalysis,
        DeviceAgeAssessment,
        IncidentSummarization
    };
}

public class LlmUseCase
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public LlmVariable[] Variables { get; init; } = Array.Empty<LlmVariable>();
    public string DefaultPrompt { get; init; } = string.Empty;
    public float DefaultTemperature { get; init; } = 0.1f;
    public int DefaultMaxTokens { get; init; } = 1000;
    public string? DefaultResponseFormat { get; init; }
}

public class LlmVariable
{
    public string Token { get; }
    public string Description { get; }

    public LlmVariable(string token, string description)
    {
        Token = token;
        Description = description;
    }
}
