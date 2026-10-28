using Microsoft.Extensions.Primitives;
using DXSuite.Director.Services;

namespace DXSuite.Director.Middleware
{
    public class ApiKeyAuthorization
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ApiKeyAuthorization> _logger;
        private readonly ApiKeyRateLimiter _rateLimiter;

        public ApiKeyAuthorization(RequestDelegate next, ILogger<ApiKeyAuthorization> logger, ApiKeyRateLimiter rateLimiter)
        {
            _next = next;
            _logger = logger;
            _rateLimiter = rateLimiter;
        }

        public async Task InvokeAsync(HttpContext context, IApiKeyService apiKeyService)
        {
            // Allow CORS preflight requests through without API key validation
            if (context.Request.Method == "OPTIONS")
            {
                await _next(context);
                return;
            }

            // Rate limiting: reject locked-out IPs before any validation
            var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var lockoutExpiry = _rateLimiter.GetLockoutExpiry(ipAddress);
            if (lockoutExpiry.HasValue)
            {
                var retryAfterSeconds = (int)Math.Ceiling((lockoutExpiry.Value - DateTime.UtcNow).TotalSeconds);
                _logger.LogWarning("API request rejected: IP {IP} is locked out until {LockoutExpiry}. Path: {Path}",
                    ipAddress, lockoutExpiry.Value, context.Request.Path);

                context.Response.StatusCode = 429;
                context.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
                await context.Response.WriteAsync("Too many failed attempts. Try again later.");
                return;
            }

            if (!context.Request.Headers.TryGetValue("X-API-KEY", out StringValues apiKeyHeader))
            {
                _logger.LogWarning("API request rejected: X-API-KEY header not provided. Path: {Path}, IP: {IP}",
                    context.Request.Path,
                    context.Connection.RemoteIpAddress);

                _rateLimiter.RecordFailure(ipAddress);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("X-API-KEY Header Not Provided!");
                return;
            }

            var apiKey = apiKeyHeader.ToString();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _rateLimiter.RecordFailure(ipAddress);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("X-API-KEY Header Is Empty!");
                return;
            }

            var validationResult = await apiKeyService.ValidateApiKeyAsync(apiKey);

            if (!validationResult.IsValid)
            {
                _logger.LogWarning("API request rejected: {Reason}. Path: {Path}, IP: {IP}",
                    validationResult.InvalidReason,
                    context.Request.Path,
                    context.Connection.RemoteIpAddress);

                _rateLimiter.RecordFailure(ipAddress);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync($"Invalid API Key: {validationResult.InvalidReason}");
                return;
            }

            _rateLimiter.ClearFailures(ipAddress);

            // Store API key info in HttpContext for downstream use
            context.Items["ApiKeyId"] = validationResult.ApiKeyId;
            context.Items["ApiKeyName"] = validationResult.KeyName;
            context.Items["ApiKeyLocationId"] = validationResult.LocationId;
            context.Items["ApiKeyPermissions"] = validationResult.Permissions;

            await _next(context);
        }
    }
}
