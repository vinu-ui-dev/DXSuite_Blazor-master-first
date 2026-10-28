using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using DXSuite.Director.Services;

namespace DXSuite.Director.Authorization;

public class DeviceTokenAuthOptions : AuthenticationSchemeOptions { }

/// <summary>
/// Validates dt_-prefixed device tokens issued to paired signage players.
/// Results are cached for 5 minutes (sliding) to reduce DB load at scale —
/// a fleet of 800 devices on 30s heartbeats would otherwise generate ~1,600 auth queries/min.
/// </summary>
public class DeviceTokenAuthHandler : AuthenticationHandler<DeviceTokenAuthOptions>
{
    private readonly IDeviceTokenService _deviceTokenService;
    private readonly IMemoryCache _cache;
    private readonly ApiKeyRateLimiter _rateLimiter;
    private readonly DeviceTokenCacheInvalidator _cacheInvalidator;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public DeviceTokenAuthHandler(
        IOptionsMonitor<DeviceTokenAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IDeviceTokenService deviceTokenService,
        IMemoryCache cache,
        ApiKeyRateLimiter rateLimiter,
        DeviceTokenCacheInvalidator cacheInvalidator)
        : base(options, logger, encoder)
    {
        _deviceTokenService = deviceTokenService;
        _cache = cache;
        _rateLimiter = rateLimiter;
        _cacheInvalidator = cacheInvalidator;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var ipAddress = Context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Reject locked-out IPs before spending any validation work
        var lockoutExpiry = _rateLimiter.GetLockoutExpiry(ipAddress);
        if (lockoutExpiry.HasValue)
        {
            var retryAfter = (int)Math.Ceiling((lockoutExpiry.Value - DateTime.UtcNow).TotalSeconds);
            Response.Headers["Retry-After"] = retryAfter.ToString();
            return AuthenticateResult.Fail("IP is locked out");
        }

        // SignalR connections cannot carry custom headers on the WebSocket upgrade, so the
        // wake channel presents the device token via the standard access_token query param.
        // The SignalR client's accessTokenFactory sends that token as an Authorization:
        // Bearer header on the negotiate request and only uses the query param for the
        // socket itself, so hub paths honor both. Query/Bearer tokens are only honored on
        // hub paths to keep the REST surface header-only.
        var token = Request.Headers.TryGetValue("X-API-KEY", out var headerValues)
            ? headerValues.ToString()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(token) && Request.Path.StartsWithSegments("/hubs"))
        {
            token = Request.Query["access_token"].ToString();
            if (string.IsNullOrWhiteSpace(token))
            {
                var authorization = Request.Headers.Authorization.ToString();
                if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    token = authorization["Bearer ".Length..].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith("dt_"))
            return AuthenticateResult.NoResult();

        // Use a hash of the token as the cache key — never store plaintext
        var cacheKey = "dtauth:" + ComputeCacheKey(token);

        if (!_cache.TryGetValue(cacheKey, out DeviceTokenCacheEntry? entry))
        {
            var result = await _deviceTokenService.ValidateDeviceTokenAsync(token);

            entry = result.IsValid
                ? new DeviceTokenCacheEntry(true, result.DeviceTokenId?.ToString(), result.DeviceId, result.Permissions)
                : new DeviceTokenCacheEntry(false, null, null, null);

            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl
            };

            // A valid entry is also dropped the moment its token is revoked. Without this the
            // cache key, a hash of the plaintext token, is unreachable from revocation, so a
            // revoked token kept working until the five minutes ran out.
            if (entry.IsValid && int.TryParse(entry.DeviceTokenId, out var tokenId))
                cacheOptions.AddExpirationToken(_cacheInvalidator.GetChangeToken(tokenId));

            _cache.Set(cacheKey, entry, cacheOptions);
        }

        if (!entry.IsValid)
        {
            _rateLimiter.RecordFailure(ipAddress);
            Logger.LogWarning("Device token auth failed. Path: {Path}, IP: {IP}", Request.Path, ipAddress);
            return AuthenticateResult.Fail("Invalid device token");
        }

        _rateLimiter.ClearFailures(ipAddress);

        // Expose token identity and device info downstream via claims
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, entry.DeviceId ?? ""),
            new("DeviceTokenId", entry.DeviceTokenId ?? ""),
        };

        if (!string.IsNullOrEmpty(entry.Permissions))
        {
            foreach (var scope in entry.Permissions.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                claims.Add(new Claim("scope", scope));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSchemes.DeviceToken));
        var ticket = new AuthenticationTicket(principal, AuthSchemes.DeviceToken);

        // Mirror device info into HttpContext.Items for controllers that read them directly
        Context.Items["DeviceTokenId"] = entry.DeviceTokenId;
        Context.Items["DeviceId"] = entry.DeviceId;
        Context.Items["ApiKeyPermissions"] = entry.Permissions;

        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        return Response.WriteAsync("Device token required");
    }

    private static string ComputeCacheKey(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes)[..16];
    }

    private sealed record DeviceTokenCacheEntry(bool IsValid, string? DeviceTokenId, string? DeviceId, string? Permissions);
}
