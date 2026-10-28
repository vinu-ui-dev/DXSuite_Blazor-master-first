using DXSuite.Shared.Services;
using Microsoft.Extensions.Logging;

namespace DXSuite.Connectors.Services.Llm;

public class OllamaProvider : ILlmProvider
{
    public string ProviderType => "ollama";

    private readonly OpenAiCompatibleProvider _innerProvider;
    private readonly ILogger<OllamaProvider> _logger;

    public OllamaProvider(HttpClient httpClient, ILogger<OllamaProvider> logger)
    {
        _innerProvider = new OpenAiCompatibleProvider(httpClient, new LoggerFactory().CreateLogger<OpenAiCompatibleProvider>());
        _logger = logger;
    }

    public async Task<LlmCompletionResponse> CompleteAsync(LlmProviderConfig config, string systemPrompt, string userPrompt, float temperature, int maxTokens, string? responseFormat, CancellationToken cancellationToken = default)
    {
        var ollamaConfig = new LlmProviderConfig
        {
            ProviderId = config.ProviderId,
            ProviderType = "ollama",
            BaseUrl = config.BaseUrl.TrimEnd('/') + "/v1",
            ApiKey = null,
            Model = config.Model,
            MaxTokens = config.MaxTokens,
            DefaultTemperature = config.DefaultTemperature,
            TimeoutSeconds = config.TimeoutSeconds
        };

        return await _innerProvider.CompleteAsync(ollamaConfig, systemPrompt, userPrompt, temperature, maxTokens, responseFormat, cancellationToken);
    }

    public async Task<bool> TestConnectionAsync(LlmProviderConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await CompleteAsync(config, "You are a test.", "Say 'ok'.", 0, 5, null, cancellationToken);
            return result.Success;
        }
        catch
        {
            return false;
        }
    }
}
