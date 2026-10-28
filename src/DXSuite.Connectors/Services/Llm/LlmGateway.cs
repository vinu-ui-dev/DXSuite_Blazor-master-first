using System.Text;
using System.Text.Json;
using DXSuite.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DXSuite.Connectors.Services.Llm;

/// <summary>
/// Routes LLM requests to configured providers. Handles provider selection,
/// prompt building for diagnostics, response parsing, and interaction logging.
/// </summary>
public class LlmGateway : ILlmGateway
{
    private readonly Dictionary<string, ILlmProvider> _providers;
    private readonly LlmOptions _options;
    private readonly LlmApiKeyProtector _apiKeyProtector;
    private readonly ILogger<LlmGateway> _logger;
    private readonly ILlmProviderConfigSource? _configSource;
    private readonly ILlmInteractionSink? _interactionSink;

    public LlmGateway(
        IEnumerable<ILlmProvider> providers,
        IOptions<LlmOptions> options,
        LlmApiKeyProtector apiKeyProtector,
        ILogger<LlmGateway> logger,
        ILlmProviderConfigSource? configSource = null,
        ILlmInteractionSink? interactionSink = null)
    {
        _providers = providers.ToDictionary(p => p.ProviderType, StringComparer.OrdinalIgnoreCase);
        _options = options.Value;
        _apiKeyProtector = apiKeyProtector;
        _logger = logger;
        _configSource = configSource;
        _interactionSink = interactionSink;
    }

    public async Task<LlmDiagnosticResponse> GetDiagnosisAsync(LlmDiagnosticRequest request, CancellationToken cancellationToken = default)
    {
        var response = new LlmDiagnosticResponse();

        var providerConfig = await GetProviderConfigAsync(request.CustomerId);
        if (providerConfig == null)
        {
            response.ErrorMessage = "No LLM provider configured";
            response.ShouldEscalate = true;
            response.EscalationReason = "No LLM provider available for diagnosis";
            return response;
        }

        var systemPrompt = BuildDiagnosticSystemPrompt(request);
        var userPrompt = BuildDiagnosticUserPrompt(request);

        var completionResult = await CompleteWithProvider(providerConfig, systemPrompt, userPrompt, 0.1f, providerConfig.MaxTokens, "json", cancellationToken);

        // Persist the raw interaction (best-effort) and link its id onto the response.
        response.InteractionId = await LogInteractionAsync(
            providerConfig, systemPrompt, userPrompt, completionResult, "device-diagnosis", request.CustomerId, cancellationToken);

        if (!completionResult.Success || string.IsNullOrEmpty(completionResult.Content))
        {
            response.ErrorMessage = completionResult.ErrorMessage ?? "No response from LLM";
            response.ShouldEscalate = true;
            response.EscalationReason = "LLM diagnosis failed";
            response.Usage = completionResult.Usage;
            return response;
        }

        try
        {
            using var doc = JsonDocument.Parse(completionResult.Content);
            var root = doc.RootElement;

            response.Diagnosis = root.TryGetProperty("diagnosis", out var diag) ? diag.GetString() : null;
            response.Confidence = root.TryGetProperty("confidence", out var conf) ? conf.GetSingle() : 0f;
            response.ShouldEscalate = root.TryGetProperty("escalate", out var esc) && esc.GetBoolean();
            response.EscalationReason = root.TryGetProperty("escalationReason", out var escR) ? escR.GetString() : null;

            if (root.TryGetProperty("recommendedActions", out var actions) && actions.ValueKind == JsonValueKind.Array)
            {
                int order = 0;
                foreach (var action in actions.EnumerateArray())
                {
                    var recommended = new RecommendedAction
                    {
                        CommandId = action.TryGetProperty("commandId", out var cmdId) ? cmdId.GetString() ?? "" : "",
                        Rationale = action.TryGetProperty("rationale", out var rat) ? rat.GetString() ?? "" : "",
                        Order = order++
                    };

                    if (action.TryGetProperty("params", out var parms) && parms.ValueKind == JsonValueKind.Object)
                    {
                        recommended.Parameters = new Dictionary<string, object>();
                        foreach (var prop in parms.EnumerateObject())
                        {
                            recommended.Parameters[prop.Name] = prop.Value.ValueKind switch
                            {
                                JsonValueKind.String => prop.Value.GetString()!,
                                JsonValueKind.Number => prop.Value.GetDouble(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                _ => prop.Value.GetRawText()
                            };
                        }
                    }

                    response.RecommendedActions.Add(recommended);
                }
            }

            response.Success = true;
            response.Usage = completionResult.Usage;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM diagnostic response as JSON");
            response.Diagnosis = completionResult.Content;
            response.ShouldEscalate = true;
            response.EscalationReason = "LLM response was not structured - manual review needed";
            response.Usage = completionResult.Usage;
        }

        return response;
    }

    public async Task<LlmCompletionResponse> CompleteAsync(LlmCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var providerConfig = await GetProviderConfigAsync(request.CustomerId, request.ProviderId);
        if (providerConfig == null)
            return new LlmCompletionResponse { ErrorMessage = "No LLM provider configured" };

        if (request.MultimodalContent != null && request.MultimodalContent.Count > 0)
        {
            var provider = GetProvider(providerConfig.ProviderType);
            if (provider == null)
                return new LlmCompletionResponse { ErrorMessage = $"Provider type '{providerConfig.ProviderType}' not registered" };
            return await provider.CompleteMultimodalAsync(providerConfig, request.SystemPrompt, request.MultimodalContent, request.Temperature, request.MaxTokens, request.ResponseFormat, cancellationToken);
        }

        return await CompleteWithProvider(providerConfig, request.SystemPrompt, request.UserPrompt, request.Temperature, request.MaxTokens, request.ResponseFormat, cancellationToken);
    }

    public async Task<bool> TestConnectionAsync(string? providerId = null, CancellationToken cancellationToken = default)
    {
        var providerConfig = await GetProviderConfigAsync(null, providerId);
        if (providerConfig == null) return false;

        var provider = GetProvider(providerConfig.ProviderType);
        if (provider == null) return false;

        return await provider.TestConnectionAsync(providerConfig, cancellationToken);
    }

    private async Task<LlmCompletionResponse> CompleteWithProvider(LlmProviderConfig config, string systemPrompt, string userPrompt, float temperature, int maxTokens, string? responseFormat, CancellationToken cancellationToken)
    {
        var provider = GetProvider(config.ProviderType);
        if (provider == null)
            return new LlmCompletionResponse { ErrorMessage = $"Provider type '{config.ProviderType}' not registered" };

        return await provider.CompleteAsync(config, systemPrompt, userPrompt, temperature, maxTokens, responseFormat, cancellationToken);
    }

    private ILlmProvider? GetProvider(string providerType)
        => _providers.TryGetValue(providerType, out var provider) ? provider : null;

    private async Task<LlmProviderConfig?> GetProviderConfigAsync(string? customerId, string? providerId = null)
    {
        // Try DB-backed config source first
        if (_configSource != null)
        {
            var config = await _configSource.GetProviderConfigAsync(providerId);
            if (config != null)
            {
                config.ApiKey = _apiKeyProtector.Decrypt(config.ApiKey);
                return config;
            }
        }

        // Fall back to appsettings configuration
        var id = providerId ?? _options.DefaultProviderId;
        return _options.Providers.TryGetValue(id, out var appConfig) ? appConfig : null;
    }

    private static string BuildDiagnosticSystemPrompt(LlmDiagnosticRequest request)
    {
        return $@"You are a device diagnostic AI for the Sentinel monitoring platform.
You analyze device health data and recommend remediation actions.

Device type: {request.DeviceType}
Device name: {request.DeviceDisplayName}

Available commands you can recommend (ONLY use these exact command IDs):
{string.Join("\n", request.AvailableCommands.Select(c => $"- {c}"))}

Guardrail constraints (you MUST respect these):
{string.Join("\n", request.GuardrailConstraints.Select(c => $"- {c}"))}

Respond with ONLY valid JSON in this exact format:
{{
  ""diagnosis"": ""brief explanation of the issue"",
  ""confidence"": 0.0 to 1.0,
  ""recommendedActions"": [
    {{
      ""commandId"": ""exact command ID from the available list"",
      ""params"": {{}},
      ""rationale"": ""why this action should help""
    }}
  ],
  ""escalate"": true/false,
  ""escalationReason"": ""reason if escalating""
}}

When the device is watched from multiple sources, weigh their agreement: convergent signals raise confidence, while a single source disagreeing with the rest (e.g. one probe Offline while others are Healthy) may indicate a path/source-specific fault rather than a device-wide failure — diagnose accordingly.

IMPORTANT: Only recommend commands from the available list. If you cannot diagnose the issue or no appropriate command is available, set escalate to true.";
    }

    private static string BuildDiagnosticUserPrompt(LlmDiagnosticRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Current device state (rolled up across all sources):");
        foreach (var kvp in request.CurrentState)
            sb.AppendLine($"  {kvp.Key}: {kvp.Value}");

        // Multi-angle: surface each monitoring source separately so the model can spot conflicts
        // (e.g. a gateway probe says Offline while the cloud connector still reports Healthy).
        if (request.SourceViews.Count > 1)
        {
            sb.AppendLine($"\nThis device is watched from {request.SourceViews.Count} independent sources; their views may disagree:");
            foreach (var src in request.SourceViews.OrderBy(s => s.SourceType))
            {
                sb.AppendLine($"  - {src.SourceType} ({src.SourceKey}): {src.Health}, last seen {FormatAge(DateTime.UtcNow - src.LastSeenUtc)}");
                foreach (var reading in src.KeyReadings)
                    sb.AppendLine($"      {reading.Key}: {reading.Value}");
            }
        }

        if (request.RecentAlertHistory.Count > 0)
        {
            sb.AppendLine("\nRecent alert history:");
            foreach (var alert in request.RecentAlertHistory)
                sb.AppendLine($"  - {alert}");
        }

        if (request.RecentActionHistory.Count > 0)
        {
            sb.AppendLine("\nRecent actions taken:");
            foreach (var action in request.RecentActionHistory)
                sb.AppendLine($"  - {action}");
        }

        if (!string.IsNullOrEmpty(request.AdditionalContext))
            sb.AppendLine($"\nAdditional context: {request.AdditionalContext}");

        return sb.ToString();
    }

    private async Task<long?> LogInteractionAsync(
        LlmProviderConfig config, string systemPrompt, string userPrompt, LlmCompletionResponse result,
        string purpose, string? customerId, CancellationToken cancellationToken)
    {
        if (_interactionSink is null) return null;
        try
        {
            return await _interactionSink.LogAsync(new LlmInteractionRecord
            {
                Provider = string.IsNullOrEmpty(result.Usage.Provider) ? config.ProviderType : result.Usage.Provider,
                Model = string.IsNullOrEmpty(result.Usage.Model) ? config.Model : result.Usage.Model,
                Purpose = purpose,
                RequestText = $"{systemPrompt}\n\n--- USER ---\n\n{userPrompt}",
                ResponseText = result.Content,
                PromptTokens = result.Usage.PromptTokens,
                CompletionTokens = result.Usage.CompletionTokens,
                LatencyMs = result.Usage.LatencyMs,
                Success = result.Success,
                ErrorMessage = result.ErrorMessage,
                CustomerId = customerId
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist LLM interaction (non-fatal)");
            return null;
        }
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalSeconds < 1) return "just now";
        if (age.TotalMinutes < 1) return $"{(int)age.TotalSeconds}s ago";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalDays < 1) return $"{(int)age.TotalHours}h ago";
        return $"{(int)age.TotalDays}d ago";
    }
}

/// <summary>
/// Provides LLM provider configuration from a database or external source.
/// Implemented by the module that owns LLM config storage (typically Director).
/// </summary>
public interface ILlmProviderConfigSource
{
    Task<LlmProviderConfig?> GetProviderConfigAsync(string? providerId, CancellationToken cancellationToken = default);
}
