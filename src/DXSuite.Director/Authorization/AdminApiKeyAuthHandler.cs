using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using DXSuite.Director.Services;

namespace DXSuite.Director.Authorization;

public class AdminApiKeyAuthOptions : AuthenticationSchemeOptions { }

/// <summary>
/// Validates admin API keys (TblApiKey) for integration and management endpoints.
/// </summary>
public class AdminApiKeyAuthHandler : AuthenticationHandler<AdminApiKeyAuthOptions>
{
    private readonly IApiKeyService _apiKeyService;
    private readonly ApiKeyRateLimiter _rateLimiter;

    public AdminApiKeyAuthHandler(
        IOptionsMonitor<AdminApiKeyAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyService apiKeyService,
        ApiKeyRateLimiter rateLimiter)
        : base(options, logger, encoder)
    {
        _apiKeyService = apiKeyService;
        _rateLimiter = rateLimiter;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var ipAddress = Context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var lockoutExpiry = _rateLimiter.GetLockoutExpiry(ipAddress);
        if (lockoutExpiry.HasValue)
        {
            var retryAfter = (int)Math.Ceiling((lockoutExpiry.Value - DateTime.UtcNow).TotalSeconds);
            Response.Headers["Retry-After"] = retryAfter.ToString();
            return AuthenticateResult.Fail("IP is locked out");
        }

        if (!Request.Headers.TryGetValue("X-API-KEY", out var headerValues))
            return AuthenticateResult.NoResult();

        var apiKey = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(apiKey))
            return AuthenticateResult.NoResult();

        // Device tokens are handled by DeviceTokenAuthHandler — don't validate them here
        if (apiKey.StartsWith("dt_"))
            return AuthenticateResult.NoResult();

        var result = await _apiKeyService.ValidateApiKeyAsync(apiKey);

        if (!result.IsValid)
        {
            _rateLimiter.RecordFailure(ipAddress);
            Logger.LogWarning("Admin API key auth failed: {Reason}. Path: {Path}, IP: {IP}",
                result.InvalidReason, Request.Path, ipAddress);
            return AuthenticateResult.Fail($"Invalid API key: {result.InvalidReason}");
        }

        _rateLimiter.ClearFailures(ipAddress);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.ApiKeyId?.ToString() ?? ""),
            new("ApiKeyName", result.KeyName ?? ""),
        };

        if (result.LocationId.HasValue)
            claims.Add(new Claim("LocationId", result.LocationId.Value.ToString()));

        if (!string.IsNullOrEmpty(result.Permissions))
        {
            foreach (var scope in result.Permissions.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                claims.Add(new Claim("scope", scope));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSchemes.AdminApiKey));
        var ticket = new AuthenticationTicket(principal, AuthSchemes.AdminApiKey);

        // Mirror into HttpContext.Items for controllers that read them directly
        Context.Items["ApiKeyId"] = result.ApiKeyId;
        Context.Items["ApiKeyName"] = result.KeyName;
        Context.Items["ApiKeyLocationId"] = result.LocationId;
        Context.Items["ApiKeyPermissions"] = result.Permissions;

        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        return Response.WriteAsync("Admin API key required");
    }
}
