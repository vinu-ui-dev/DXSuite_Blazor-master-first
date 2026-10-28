using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using DXSuite.Director.Entities;
using DXSuite.Shared.Services;

namespace DXSuite.Director.Services
{
    public class ContentModerationService : IContentModerationService
    {
        private readonly IContentModerationProvider? _moderationProvider;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ContentModerationService> _logger;

        // Event type IDs (pre-seeded in tblEventType)
        private const int EventTypeApproved = 17;
        private const int EventTypeFlagged = 18;
        private const int EventTypeSkipped = 19;
        private const int EventTypeError = 20;

        // Severity IDs (pre-seeded in tblEventSeverity)
        private const int SeverityInfo = 1;
        private const int SeverityWarning = 2;

        public ContentModerationService(
            IServiceScopeFactory scopeFactory,
            IMemoryCache cache,
            IConfiguration configuration,
            ILogger<ContentModerationService> logger,
            IContentModerationProvider? moderationProvider = null)
        {
            _scopeFactory = scopeFactory;
            _cache = cache;
            _configuration = configuration;
            _logger = logger;
            _moderationProvider = moderationProvider;
        }

        public Task<ModerationResult?> ScanTextAsync(params string[] textInputs)
        {
            return ScanTextAsync(null, textInputs);
        }

        public async Task<ModerationResult?> ScanTextAsync(ModerationContext? context, params string[] textInputs)
        {
            if (textInputs == null || textInputs.Length == 0)
            {
                return new ModerationResult { IsInappropriate = false };
            }

            var combinedText = string.Join(" | ", textInputs.Where(t => !string.IsNullOrWhiteSpace(t)));
            if (string.IsNullOrWhiteSpace(combinedText))
            {
                return new ModerationResult { IsInappropriate = false };
            }

            if (_moderationProvider == null)
            {
                _logger.LogInformation("No content moderation provider configured, skipping");
                _ = LogModerationEventAsync(EventTypeSkipped, SeverityInfo, null, combinedText, null, 0, context);
                return null;
            }

            // Check cache
            var cacheKey = GetCacheKey(combinedText);
            if (_cache.TryGetValue(cacheKey, out ModerationResult? cachedResult))
            {
                _logger.LogInformation("Content moderation cache hit");
                return cachedResult;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                // Delegate to the shared moderation provider
                var sharedContext = context != null
                    ? new Shared.Services.ModerationContext
                    {
                        UserId = context.UserId,
                        DisplayId = context.DisplayId,
                        LocationId = context.LocationId
                    }
                    : null;

                var providerResult = await _moderationProvider.ScanTextAsync(sharedContext!, textInputs);
                sw.Stop();

                if (providerResult == null)
                {
                    _logger.LogWarning("Moderation provider returned null for text scan");
                    _ = LogModerationEventAsync(EventTypeError, SeverityWarning, null, combinedText, "Provider returned null", sw.ElapsedMilliseconds, context);
                    return null;
                }

                // Map shared result to local ModerationResult
                var result = new ModerationResult
                {
                    IsInappropriate = providerResult.IsInappropriate,
                    Reason = providerResult.Reason,
                    PolicyViolation = providerResult.PolicyViolation
                };

                // Cache the result
                var cacheDuration = TimeSpan.FromMinutes(
                    _configuration.GetValue("ContentModeration:CacheDurationMinutes", 5));
                _cache.Set(cacheKey, result, cacheDuration);

                var eventType = result.IsInappropriate ? EventTypeFlagged : EventTypeApproved;
                var severity = result.IsInappropriate ? SeverityWarning : SeverityInfo;
                _ = LogModerationEventAsync(eventType, severity, null, combinedText, result.Reason, sw.ElapsedMilliseconds, context);

                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogWarning(ex, "Content moderation failed, allowing generation to proceed");
                _ = LogModerationEventAsync(EventTypeError, SeverityWarning, null, combinedText, ex.Message, sw.ElapsedMilliseconds, context);
                return null;
            }
        }

        public async Task<ModerationResult?> ScanImageAsync(byte[] imageData, string mimeType, ModerationContext? context = null)
        {
            if (imageData == null || imageData.Length == 0)
            {
                return new ModerationResult { IsInappropriate = false };
            }

            if (_moderationProvider == null)
            {
                _logger.LogInformation("No content moderation provider configured, skipping image scan");
                _ = LogModerationEventAsync(EventTypeSkipped, SeverityInfo, null, "[image]", null, 0, context);
                return null;
            }

            // Cache by image content hash
            var imageHash = Convert.ToBase64String(SHA256.HashData(imageData));
            var cacheKey = $"moderation_img_{imageHash}";
            if (_cache.TryGetValue(cacheKey, out ModerationResult? cachedResult))
            {
                _logger.LogInformation("Image moderation cache hit");
                return cachedResult;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                var sharedContext = context != null
                    ? new Shared.Services.ModerationContext
                    {
                        UserId = context.UserId,
                        DisplayId = context.DisplayId,
                        LocationId = context.LocationId
                    }
                    : null;

                var providerResult = await _moderationProvider.ScanImageAsync(imageData, mimeType, sharedContext);
                sw.Stop();

                if (providerResult == null)
                {
                    _logger.LogWarning("Moderation provider returned null for image scan");
                    _ = LogModerationEventAsync(EventTypeError, SeverityWarning, null, "[image]", "Provider returned null", sw.ElapsedMilliseconds, context);
                    return null;
                }

                var result = new ModerationResult
                {
                    IsInappropriate = providerResult.IsInappropriate,
                    Reason = providerResult.Reason,
                    PolicyViolation = providerResult.PolicyViolation
                };

                var cacheDuration = TimeSpan.FromMinutes(
                    _configuration.GetValue("ContentModeration:CacheDurationMinutes", 5));
                _cache.Set(cacheKey, result, cacheDuration);

                var eventType = result.IsInappropriate ? EventTypeFlagged : EventTypeApproved;
                var severity = result.IsInappropriate ? SeverityWarning : SeverityInfo;
                _ = LogModerationEventAsync(eventType, severity, null, "[image]", result.Reason, sw.ElapsedMilliseconds, context);

                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogWarning(ex, "Image moderation failed, allowing content to proceed");
                _ = LogModerationEventAsync(EventTypeError, SeverityWarning, null, "[image]", ex.Message, sw.ElapsedMilliseconds, context);
                return null;
            }
        }

        private async Task LogModerationEventAsync(int eventTypeId, int severityId, string? providerId, string inputText, string? resultDetail, long latencyMs, ModerationContext? context)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var directorDb = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

                var truncatedInput = inputText.Length > 500 ? inputText[..500] + "..." : inputText;
                var details = JsonSerializer.Serialize(new
                {
                    providerId,
                    input = truncatedInput,
                    result = resultDetail,
                    latencyMs
                });

                var eventLog = new TblEventLog
                {
                    EventTypeId = eventTypeId,
                    EventSeverityId = severityId,
                    EventSource = "ContentModeration",
                    EventTitle = "Content Moderation",
                    EventMessage = resultDetail,
                    Details = details,
                    UserId = context?.UserId,
                    DisplayId = context?.DisplayId,
                    LocationId = context?.LocationId,
                    CreateDate = DateTime.UtcNow
                };

                directorDb.TblEventLogs.Add(eventLog);
                await directorDb.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to log content moderation event");
            }
        }

        private static string GetCacheKey(string text)
        {
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            return $"moderation_{Convert.ToBase64String(hashBytes)}";
        }
    }
}
