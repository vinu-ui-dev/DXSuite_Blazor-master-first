using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DXSuite.Shared.Services;
using Microsoft.Extensions.Logging;

namespace DXSuite.Connectors.Services.Llm;

/// <summary>
/// LLM provider for the Anthropic Messages API (Claude models).
/// Uses x-api-key header auth and the /v1/messages endpoint.
/// </summary>
public class AnthropicProvider : ILlmProvider
{
    public string ProviderType => "anthropic";
    public bool SupportsVision => true;

    private const string DefaultBaseUrl = "https://api.anthropic.com";
    private const string ApiVersion = "2023-06-01";

    private readonly HttpClient _httpClient;
    private readonly ILogger<AnthropicProvider> _logger;

    public AnthropicProvider(HttpClient httpClient, ILogger<AnthropicProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<LlmCompletionResponse> CompleteAsync(LlmProviderConfig config, string systemPrompt, string userPrompt, float temperature, int maxTokens, string? responseFormat, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var response = new LlmCompletionResponse
        {
            Usage = new LlmUsageInfo { Provider = config.ProviderId, Model = config.Model }
        };

        try
        {
            var baseUrl = string.IsNullOrWhiteSpace(config.BaseUrl) ? DefaultBaseUrl : config.BaseUrl.TrimEnd('/');

            // Anthropic has no native JSON mode — instruct via system prompt
            var effectiveSystemPrompt = systemPrompt;
            if (responseFormat == "json")
            {
                effectiveSystemPrompt += "\n\nIMPORTANT: You must respond with ONLY valid JSON. No markdown, no explanation, just the JSON object.";
            }

            var requestBody = new
            {
                model = config.Model,
                max_tokens = maxTokens,
                temperature = temperature,
                system = effectiveSystemPrompt,
                messages = new[]
                {
                    new { role = "user", content = userPrompt }
                }
            };

            var requestJson = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/messages");
            request.Content = content;
            request.Headers.Add("x-api-key", config.ApiKey);
            request.Headers.Add("anthropic-version", ApiVersion);

            using var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
            var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                response.ErrorMessage = $"API returned {(int)httpResponse.StatusCode}: {responseJson}";
                _logger.LogWarning("Anthropic API error: {StatusCode} {Body}", (int)httpResponse.StatusCode, responseJson);
                return response;
            }

            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            // Response content is in content[0].text
            if (root.TryGetProperty("content", out var contentArray) && contentArray.GetArrayLength() > 0)
            {
                response.Content = contentArray[0].GetProperty("text").GetString();
                response.Success = true;
            }

            // Usage is in usage.input_tokens / usage.output_tokens
            if (root.TryGetProperty("usage", out var usage))
            {
                response.Usage.PromptTokens = usage.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
                response.Usage.CompletionTokens = usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;
            }
        }
        catch (Exception ex)
        {
            response.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error calling Anthropic provider {ProviderId}", config.ProviderId);
        }
        finally
        {
            sw.Stop();
            response.Usage.LatencyMs = sw.ElapsedMilliseconds;
        }

        return response;
    }

    public async Task<LlmCompletionResponse> CompleteMultimodalAsync(LlmProviderConfig config, string systemPrompt, List<LlmMessageContent> content, float temperature, int maxTokens, string? responseFormat, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var response = new LlmCompletionResponse
        {
            Usage = new LlmUsageInfo { Provider = config.ProviderId, Model = config.Model }
        };

        try
        {
            var baseUrl = string.IsNullOrWhiteSpace(config.BaseUrl) ? DefaultBaseUrl : config.BaseUrl.TrimEnd('/');

            var effectiveSystemPrompt = systemPrompt;
            if (responseFormat == "json")
            {
                effectiveSystemPrompt += "\n\nIMPORTANT: You must respond with ONLY valid JSON. No markdown, no explanation, just the JSON object.";
            }

            // Build Anthropic content blocks: text → { type: "text", text: ... }, image → { type: "image", source: ... }
            var contentBlocks = new List<object>();
            foreach (var part in content)
            {
                if (part.Type == "image_url")
                {
                    contentBlocks.Add(new
                    {
                        type = "image",
                        source = new
                        {
                            type = "base64",
                            media_type = part.MimeType ?? "image/png",
                            data = part.Content
                        }
                    });
                }
                else
                {
                    contentBlocks.Add(new { type = "text", text = part.Content });
                }
            }

            var requestBody = new
            {
                model = config.Model,
                max_tokens = maxTokens,
                temperature = temperature,
                system = effectiveSystemPrompt,
                messages = new[]
                {
                    new { role = "user", content = (object)contentBlocks }
                }
            };

            var requestJson = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/messages");
            request.Content = httpContent;
            request.Headers.Add("x-api-key", config.ApiKey);
            request.Headers.Add("anthropic-version", ApiVersion);

            using var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
            var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                response.ErrorMessage = $"API returned {(int)httpResponse.StatusCode}: {responseJson}";
                _logger.LogWarning("Anthropic multimodal API error: {StatusCode} {Body}", (int)httpResponse.StatusCode, responseJson);
                return response;
            }

            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("content", out var contentArray) && contentArray.GetArrayLength() > 0)
            {
                response.Content = contentArray[0].GetProperty("text").GetString();
                response.Success = true;
            }

            if (root.TryGetProperty("usage", out var usage))
            {
                response.Usage.PromptTokens = usage.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
                response.Usage.CompletionTokens = usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;
            }
        }
        catch (Exception ex)
        {
            response.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error calling Anthropic multimodal provider {ProviderId}", config.ProviderId);
        }
        finally
        {
            sw.Stop();
            response.Usage.LatencyMs = sw.ElapsedMilliseconds;
        }

        return response;
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
