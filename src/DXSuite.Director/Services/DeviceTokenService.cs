using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.Security;
using System.Security.Cryptography;

namespace DXSuite.Director.Services;

public class DeviceTokenService : IDeviceTokenService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DeviceTokenCacheInvalidator _cacheInvalidator;
    private readonly ILogger<DeviceTokenService> _logger;

    /// <summary>
    /// Paired devices receive heartbeat + upload scopes only.
    /// </summary>
    private const string DeviceTokenPermissions = "signage:heartbeat signage:upload";

    public DeviceTokenService(IServiceScopeFactory scopeFactory,
        DeviceTokenCacheInvalidator cacheInvalidator, ILogger<DeviceTokenService> logger)
    {
        _scopeFactory = scopeFactory;
        _cacheInvalidator = cacheInvalidator;
        _logger = logger;
    }

    public async Task<DeviceTokenValidationResult> ValidateDeviceTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new DeviceTokenValidationResult
            {
                IsValid = false,
                InvalidReason = "Device token is empty"
            };
        }

        // Strip the dt_ prefix before hashing
        var rawToken = token.StartsWith("dt_") ? token[3..] : token;
        var hash = GatewayApiKeyHelper.ComputeApiKeyHash(rawToken);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var deviceToken = await db.TblDeviceTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (deviceToken == null)
        {
            return new DeviceTokenValidationResult
            {
                IsValid = false,
                InvalidReason = "Device token not found"
            };
        }

        if (!deviceToken.IsActive)
        {
            return new DeviceTokenValidationResult
            {
                IsValid = false,
                InvalidReason = "Device token has been revoked"
            };
        }

        // Fire-and-forget usage tracking
        _ = Task.Run(() => RecordUsageAsync(deviceToken.DeviceTokenId));

        return new DeviceTokenValidationResult
        {
            IsValid = true,
            DeviceTokenId = deviceToken.DeviceTokenId,
            DeviceId = deviceToken.DeviceId,
            Permissions = DeviceTokenPermissions
        };
    }

    public async Task<DeviceTokenCreationResult> CreateDeviceTokenAsync(Guid? pairingRequestId, int? approvedByUserId)
    {
        var plaintextRaw = GenerateSecureToken();
        var plaintextToken = $"dt_{plaintextRaw}";

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var token = new TblDeviceToken
        {
            TokenHash = GatewayApiKeyHelper.ComputeApiKeyHash(plaintextRaw),
            TokenPrefix = plaintextToken[..Math.Min(11, plaintextToken.Length)],
            IsActive = true,
            CreateDate = DateTime.UtcNow,
            PairingRequestId = pairingRequestId,
            ApprovedByUserId = approvedByUserId
        };

        db.TblDeviceTokens.Add(token);
        await db.SaveChangesAsync();

        _logger.LogInformation("Created device token (ID: {TokenId}) via pairing request {PairingRequestId}",
            token.DeviceTokenId, pairingRequestId);

        return new DeviceTokenCreationResult(token, plaintextToken);
    }

    public async Task<List<TblDeviceToken>> GetAllDeviceTokensAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await db.TblDeviceTokens
            .Include(t => t.Device)
            .Include(t => t.ApprovedByUser)
            .OrderByDescending(t => t.CreateDate)
            .ToListAsync();
    }

    public async Task RevokeDeviceTokenAsync(int deviceTokenId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var token = await db.TblDeviceTokens.FindAsync(deviceTokenId);
        if (token == null)
            throw new ArgumentException($"Device token {deviceTokenId} not found");

        token.IsActive = false;
        await db.SaveChangesAsync();

        // Revocation takes effect now rather than at the end of the auth cache's five minutes.
        _cacheInvalidator.Invalidate(deviceTokenId);

        _logger.LogInformation("Revoked device token (ID: {TokenId})", deviceTokenId);
    }

    /// <summary>
    /// Revokes every other active token bound to a device, so a device holds exactly one.
    ///
    /// Nothing did this before: the only RevokeDeviceTokenAsync call site in the solution is an
    /// operator button, so every re-pairing on every platform left the previous token live.
    /// </summary>
    public async Task<int> RevokeOtherTokensForDeviceAsync(string deviceId, int keepDeviceTokenId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return 0;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var stale = await db.TblDeviceTokens
            .Where(t => t.DeviceId == deviceId && t.IsActive && t.DeviceTokenId != keepDeviceTokenId)
            .ToListAsync();

        if (stale.Count == 0)
            return 0;

        foreach (var token in stale)
            token.IsActive = false;

        await db.SaveChangesAsync();

        foreach (var token in stale)
            _cacheInvalidator.Invalidate(token.DeviceTokenId);

        _logger.LogInformation("Revoked {Count} superseded token(s) for device {DeviceId}", stale.Count, deviceId);
        return stale.Count;
    }

    public async Task ReactivateDeviceTokenAsync(int deviceTokenId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var token = await db.TblDeviceTokens.FindAsync(deviceTokenId);
        if (token == null)
            throw new ArgumentException($"Device token {deviceTokenId} not found");

        token.IsActive = true;
        await db.SaveChangesAsync();

        _logger.LogInformation("Reactivated device token (ID: {TokenId})", deviceTokenId);
    }

    public async Task DeleteDeviceTokenAsync(int deviceTokenId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var token = await db.TblDeviceTokens.FindAsync(deviceTokenId);
        if (token == null)
            throw new ArgumentException($"Device token {deviceTokenId} not found");

        db.TblDeviceTokens.Remove(token);
        await db.SaveChangesAsync();

        _cacheInvalidator.Invalidate(deviceTokenId);

        _logger.LogInformation("Deleted device token (ID: {TokenId})", deviceTokenId);
    }

    public async Task RecordUsageAsync(int deviceTokenId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var token = await db.TblDeviceTokens.FindAsync(deviceTokenId);
            if (token != null)
            {
                token.LastUsedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record device token usage for ID {TokenId}", deviceTokenId);
        }
    }

    public async Task BindToDeviceAsync(int deviceTokenId, string deviceId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var token = await db.TblDeviceTokens.FindAsync(deviceTokenId);
        if (token == null)
            throw new ArgumentException($"Device token {deviceTokenId} not found");

        // Already bound — nothing to do (called speculatively from every heartbeat until cache refreshes)
        if (token.DeviceId == deviceId)
            return;

        // TblDevice is created asynchronously by HeartbeatService. Skip binding on the very
        // first heartbeat before the device record exists; the next heartbeat will succeed.
        var deviceExists = await db.TblDevices.AnyAsync(d => d.DeviceId == deviceId);
        if (!deviceExists)
            return;

        token.DeviceId = deviceId;
        await db.SaveChangesAsync();

        _logger.LogInformation("Bound device token {TokenId} to device {DeviceId}", deviceTokenId, deviceId);
    }

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}
