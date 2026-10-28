using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DXSuite.Shared.Services;
using Microsoft.Extensions.Logging;

namespace DXSuite.Connectors.Services.Llm;

/// <summary>
/// LLM provider for Google Gemini's generateContent API.
/// Auth via API key in query string, different request/response structure from OpenAI.
/// </summary>
public class GeminiProvider : ILlmProvider
{
    public string ProviderType => "gemini";
    public bool SupportsVision => true;

    private const string DefaultBaseUrl = "https://generativelanguage.googleapis.com";

    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiProvider> _logger;

    public GeminiProvider(HttpClient httpClient, ILogger<GeminiProvider> logger)
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
            var url = $"{baseUrl}/v1beta/models/{config.Model}:generateContent?key={config.ApiKey}";

            var generationConfig = new Dictionary<string, object>
            {
                ["temperature"] = temperature,
                ["maxOutputTokens"] = maxTokens
            };

            if (responseFormat == "json")
            {
                generationConfig["responseMimeType"] = "application/json";
            }

            var requestBody = new Dictionary<string, object>
            {
                ["systemInstruction"] = new
                {
                    parts = new[] { new { text = systemPrompt } }
                },
                ["contents"] = new[]
                {
                    new
                    {
                        parts = new[] { new { text = userPrompt } }
                    }
                },
                ["generationConfig"] = generationConfig
            };

            var requestJson = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var httpResponse = await _httpClient.PostAsync(url, content, cancellationToken);
            var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                response.ErrorMessage = $"API returned {(int)httpResponse.StatusCode}: {responseJson}";
                _logger.LogWarning("Gemini API error: {StatusCode} {Body}", (int)httpResponse.StatusCode, responseJson);
                return response;
            }

            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            // Response in candidates[0].content.parts[0].text
            if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                if (firstCandidate.TryGetProperty("content", out var candidateContent) &&
                    candidateContent.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0)
                {
                    response.Content = parts[0].GetProperty("text").GetString();
                    response.Success = true;
                }
            }

            // Usage in usageMetadata
            if (root.TryGetProperty("usageMetadata", out var usage))
            {
                response.Usage.PromptTokens = usage.TryGetProperty("promptTokenCount", out var pt) ? pt.GetInt32() : 0;
                response.Usage.CompletionTokens = usage.TryGetProperty("candidatesTokenCount", out var ct) ? ct.GetInt32() : 0;
            }
        }
        catch (Exception ex)
        {
            response.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error calling Gemini provider {ProviderId}", config.ProviderId);
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
            var url = $"{baseUrl}/v1beta/models/{config.Model}:generateContent?key={config.ApiKey}";

            var generationConfig = new Dictionary<string, object>
            {
                ["temperature"] = temperature,
                ["maxOutputTokens"] = maxTokens
            };

            if (responseFormat == "json")
            {
                generationConfig["responseMimeType"] = "application/json";
            }

            // Build Gemini parts: text → { text: ... }, image → { inlineData: { mimeType, data } }
            var parts = new List<object>();
            foreach (var part in content)
            {
                if (part.Type == "image_url")
                {
                    parts.Add(new
                    {
                        inlineData = new
                        {
                            mimeType = part.MimeType ?? "image/png",
                            data = part.Content
                        }
                    });
                }
                else
                {
                    parts.Add(new { text = part.Content });
                }
            }

            var requestBody = new Dictionary<string, object>
            {
                ["systemInstruction"] = new
                {
                    parts = new[] { new { text = systemPrompt } }
                },
                ["contents"] = new[]
                {
                    new { parts = (object)parts }
                },
                ["generationConfig"] = generationConfig
            };

            var requestJson = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var httpResponse = await _httpClient.PostAsync(url, httpContent, cancellationToken);
            var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                response.ErrorMessage = $"API returned {(int)httpResponse.StatusCode}: {responseJson}";
                _logger.LogWarning("Gemini multimodal API error: {StatusCode} {Body}", (int)httpResponse.StatusCode, responseJson);
                return response;
            }

            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                if (firstCandidate.TryGetProperty("content", out var candidateContent) &&
                    candidateContent.TryGetProperty("parts", out var responseParts) &&
                    responseParts.GetArrayLength() > 0)
                {
                    response.Content = responseParts[0].GetProperty("text").GetString();
                    response.Success = true;
                }
            }

            if (root.TryGetProperty("usageMetadata", out var usage))
            {
                response.Usage.PromptTokens = usage.TryGetProperty("promptTokenCount", out var pt) ? pt.GetInt32() : 0;
                response.Usage.CompletionTokens = usage.TryGetProperty("candidatesTokenCount", out var ct) ? ct.GetInt32() : 0;
            }
        }
        catch (Exception ex)
        {
            response.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error calling Gemini multimodal provider {ProviderId}", config.ProviderId);
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
