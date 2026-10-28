using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using DXSuite.Director.Entities;
using DXSuite.Shared.Services;

namespace DXSuite.Director.Services;

/// <summary>
/// Implements content moderation by calling ILlmGateway with the "content-moderation"
/// prompt configuration. Registered as IContentModerationProvider so ContentModerationService
/// can delegate actual LLM calls here.
/// </summary>
public class LlmModerationProvider : IContentModerationProvider
{
    private readonly ILlmGateway _llmGateway;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LlmModerationProvider> _logger;

    public LlmModerationProvider(
        ILlmGateway llmGateway,
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        IConfiguration configuration,
        ILogger<LlmModerationProvider> logger)
    {
        _llmGateway = llmGateway;
        _scopeFactory = scopeFactory;
        _cache = cache;
        _configuration = configuration;
        _logger = logger;
    }

    public Task<ModerationResult?> ScanTextAsync(params string[] textInputs)
        => ScanTextAsync(null, textInputs);

    public async Task<ModerationResult?> ScanTextAsync(ModerationContext? context, params string[] textInputs)
    {
        var combinedText = string.Join(" | ", textInputs.Where(t => !string.IsNullOrWhiteSpace(t)));
        if (string.IsNullOrWhiteSpace(combinedText))
            return new ModerationResult { IsInappropriate = false };

        var promptConfig = await LoadPromptConfigAsync();
        if (promptConfig == null || !promptConfig.SupportsText)
        {
            _logger.LogInformation("Content moderation prompt not configured or does not support text, skipping");
            return null;
        }

        var cacheKey = $"moderation_txt_{promptConfig.PromptId}_{Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(combinedText)))}";
        if (_cache.TryGetValue(cacheKey, out ModerationResult? cached))
            return cached;

        var sw = Stopwatch.StartNew();
        try
        {
            var systemPrompt = BuildSystemPrompt(promptConfig.SystemPrompt
                .Replace("{message_text}", combinedText));

            var response = await _llmGateway.CompleteAsync(new LlmCompletionRequest
            {
                ProviderId = promptConfig.ProviderId,
                SystemPrompt = systemPrompt,
                UserPrompt = $"Evaluate this text: {combinedText}",
                Temperature = promptConfig.Temperature,
                MaxTokens = promptConfig.MaxTokens,
                ResponseFormat = promptConfig.ResponseFormat
            });
            sw.Stop();

            if (!response.Success || string.IsNullOrEmpty(response.Content))
            {
                _logger.LogWarning("LLM call failed for text moderation: {Error}", response.ErrorMessage);
                return null;
            }

            var result = ParseModerationResponse(response.Content);
            if (result != null)
                CacheResult(cacheKey, result);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Text moderation failed, allowing content to proceed");
            return null;
        }
    }

    public async Task<ModerationResult?> ScanImageAsync(byte[] imageData, string mimeType, ModerationContext? context = null)
    {
        if (imageData == null || imageData.Length == 0)
            return new ModerationResult { IsInappropriate = false };

        var promptConfig = await LoadPromptConfigAsync();
        if (promptConfig == null || !promptConfig.SupportsVision)
        {
            _logger.LogInformation("Content moderation prompt not configured or does not support vision, skipping image scan");
            return null;
        }

        var imageHash = Convert.ToBase64String(SHA256.HashData(imageData));
        var cacheKey = $"moderation_img_{promptConfig.PromptId}_{imageHash}";
        if (_cache.TryGetValue(cacheKey, out ModerationResult? cached))
        {
            _logger.LogInformation("Image moderation cache hit");
            return cached;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var systemPrompt = BuildSystemPrompt(promptConfig.SystemPrompt
                .Replace("{message_text}", "")
                .Replace("{image_base64}", ""));

            var base64 = Convert.ToBase64String(imageData);

            var response = await _llmGateway.CompleteAsync(new LlmCompletionRequest
            {
                ProviderId = promptConfig.ProviderId,
                SystemPrompt = systemPrompt,
                UserPrompt = "Evaluate this image for appropriateness in a professional digital signage environment.",
                Temperature = promptConfig.Temperature,
                MaxTokens = promptConfig.MaxTokens,
                ResponseFormat = promptConfig.ResponseFormat,
                MultimodalContent = new List<LlmMessageContent>
                {
                    new() { Type = "text", Content = "Evaluate this image for appropriateness in a professional digital signage environment." },
                    new() { Type = "image_url", Content = base64, MimeType = mimeType }
                }
            });
            sw.Stop();

            if (!response.Success || string.IsNullOrEmpty(response.Content))
            {
                _logger.LogWarning("LLM call failed for image moderation: {Error}", response.ErrorMessage);
                return null;
            }

            var result = ParseModerationResponse(response.Content);
            if (result != null)
                CacheResult(cacheKey, result);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Image moderation failed, allowing content to proceed");
            return null;
        }
    }

    private async Task<TblLlmPromptConfiguration?> LoadPromptConfigAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
            return await db.TblLlmPromptConfigurations
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UseCaseId == "content-moderation" && p.IsEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DB unavailable for content moderation prompt lookup");
            return null;
        }
    }

    private static string BuildSystemPrompt(string basePrompt) => basePrompt + @"

Respond with ONLY valid JSON in this exact format:
{
  ""is_inappropriate"": true or false,
  ""reason"": ""brief explanation of why content was flagged or approved"",
  ""violation_type"": ""category of violation or 'None'""
}";

    private ModerationResult? ParseModerationResponse(string content)
    {
        try
        {
            // Claude sometimes wraps JSON in markdown code fences despite explicit instructions.
            // Strip them before parsing so the response doesn't silently approve content.
            var json = content.Trim();
            if (json.StartsWith("```"))
            {
                var firstNewline = json.IndexOf('\n');
                if (firstNewline >= 0) json = json[(firstNewline + 1)..];
                if (json.EndsWith("```")) json = json[..^3].TrimEnd();
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var isInappropriate = root.GetProperty("is_inappropriate").GetBoolean();
            var reason = root.TryGetProperty("reason", out var reasonProp)
                ? reasonProp.GetString() ?? "Content flagged by moderation system"
                : "Content flagged by moderation system";
            var violationType = root.TryGetProperty("violation_type", out var violationProp)
                ? violationProp.GetString() ?? "Unknown"
                : "Unknown";

            return new ModerationResult
            {
                IsInappropriate = isInappropriate,
                Reason = reason,
                PolicyViolation = violationType
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse moderation response as JSON: {Content}", content);
            return null;
        }
    }

    private void CacheResult(string key, ModerationResult result)
    {
        var duration = TimeSpan.FromMinutes(
            _configuration.GetValue("ContentModeration:CacheDurationMinutes", 5));
        _cache.Set(key, result, duration);
    }
}
