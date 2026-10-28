namespace DXSuite.Connectors.Services.Llm;

/// <summary>
/// Optional sink for persisting each LLM call (prompt, response, usage) for audit and tuning.
/// Implemented by the module that owns interaction storage (Director, into tblLlmInteraction),
/// mirroring <see cref="ILlmProviderConfigSource"/>. The gateway logs best-effort: a sink failure
/// never breaks the call. Returns the stored interaction id so callers can link records to it.
/// </summary>
public interface ILlmInteractionSink
{
    Task<long?> LogAsync(LlmInteractionRecord record, CancellationToken cancellationToken = default);
}

/// <summary>One LLM call to persist. Prompt/response are stored raw; the sink computes the prompt hash.</summary>
public class LlmInteractionRecord
{
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    /// <summary>What the call was for, e.g. "device-diagnosis".</summary>
    public string Purpose { get; set; } = string.Empty;
    public string? RequestText { get; set; }
    public string? ResponseText { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public long LatencyMs { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CustomerId { get; set; }
}
