using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DXSuite.Shared.Services;
using Microsoft.Extensions.Logging;

namespace DXSuite.Connectors.Services.Llm;

/// <summary>
/// LLM provider for OpenAI-compatible APIs (Claude, OpenAI, Groq).
/// </summary>
public class OpenAiCompatibleProvider : ILlmProvider
{
    public string ProviderType => "openai-compatible";
    public bool SupportsVision => true;

    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAiCompatibleProvider> _logger;

    public OpenAiCompatibleProvider(HttpClient httpClient, ILogger<OpenAiCompatibleProvider> logger)
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
            var requestBody = new
            {
                model = config.Model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = temperature,
                max_tokens = maxTokens,
                response_format = responseFormat == "json" ? new { type = "json_object" } : null
            };

            var requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{config.BaseUrl.TrimEnd('/')}/chat/completions");
            request.Content = content;
            if (!string.IsNullOrEmpty(config.ApiKey))
                request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");

            using var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
            var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                response.ErrorMessage = $"API returned {(int)httpResponse.StatusCode}: {responseJson}";
                _logger.LogWarning("LLM API error: {StatusCode} {Body}", (int)httpResponse.StatusCode, responseJson);
                return response;
            }

            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                response.Content = choices[0].GetProperty("message").GetProperty("content").GetString();
                response.Success = true;
            }

            if (root.TryGetProperty("usage", out var usage))
            {
                response.Usage.PromptTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
                response.Usage.CompletionTokens = usage.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0;
            }
        }
        catch (Exception ex)
        {
            response.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error calling LLM provider {ProviderId}", config.ProviderId);
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
            var contentParts = new List<object>();
            foreach (var part in content)
            {
                if (part.Type == "image_url")
                {
                    var mimeType = part.MimeType ?? "image/png";
                    contentParts.Add(new
                    {
                        type = "image_url",
                        image_url = new { url = $"data:{mimeType};base64,{part.Content}" }
                    });
                }
                else
                {
                    contentParts.Add(new { type = "text", text = part.Content });
                }
            }

            var requestBody = new
            {
                model = config.Model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = (object)contentParts }
                },
                temperature = temperature,
                max_tokens = maxTokens,
                response_format = responseFormat == "json" ? new { type = "json_object" } : null
            };

            var requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            var httpContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{config.BaseUrl.TrimEnd('/')}/chat/completions");
            request.Content = httpContent;
            if (!string.IsNullOrEmpty(config.ApiKey))
                request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");

            using var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
            var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                response.ErrorMessage = $"API returned {(int)httpResponse.StatusCode}: {responseJson}";
                _logger.LogWarning("LLM multimodal API error: {StatusCode} {Body}", (int)httpResponse.StatusCode, responseJson);
                return response;
            }

            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                response.Content = choices[0].GetProperty("message").GetProperty("content").GetString();
                response.Success = true;
            }

            if (root.TryGetProperty("usage", out var usage))
            {
                response.Usage.PromptTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
                response.Usage.CompletionTokens = usage.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0;
            }
        }
        catch (Exception ex)
        {
            response.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error calling LLM multimodal provider {ProviderId}", config.ProviderId);
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
