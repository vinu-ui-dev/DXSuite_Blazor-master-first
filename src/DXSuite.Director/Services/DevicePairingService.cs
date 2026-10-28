using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.Security;
using System.Security.Cryptography;

namespace DXSuite.Director.Services;

public class DevicePairingService : IDevicePairingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeviceTokenService _deviceTokenService;
    private readonly ProvisioningSecretProtector _secretProtector;
    private readonly ILogger<DevicePairingService> _logger;
    private readonly IConfiguration _configuration;

    private const int MaxFailedAttempts = 5;

    /// <summary>
    /// Ceiling on unapproved requests a tenant can hold at once. A rate limiter bounds how fast
    /// rows arrive; this bounds how many can exist, which is the half that matters when the rows
    /// are what an attacker is after. Sized well above a technician enrolling a rack.
    /// </summary>
    private const int MaxOutstandingRequests = 200;

    /// <summary>How long an approved token stays collectable before it has to be re-approved.</summary>
    private static readonly TimeSpan DeliveryWindow = TimeSpan.FromMinutes(15);

    public DevicePairingService(
        IServiceScopeFactory scopeFactory,
        IDeviceTokenService deviceTokenService,
        ProvisioningSecretProtector secretProtector,
        ILogger<DevicePairingService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _deviceTokenService = deviceTokenService;
        _secretProtector = secretProtector;
        _logger = logger;
        _configuration = configuration;
    }

    private int ExpiryMinutes => _configuration.GetValue("Security:PairingCodeExpiryMinutes", 10);

    public async Task<PairingRequestResult> CreatePairingRequestAsync(
        string? deviceId, string? manufacturer, string? model,
        string? serialNumber, string? ipAddress, string? macAddress, string? platform = null)
    {
        if (await IsDeviceBlockedAsync(deviceId, macAddress))
        {
            _logger.LogWarning("Blocked device attempted pairing: DeviceId={DeviceId}, MAC={Mac}", deviceId, macAddress);
            throw new DeviceBlockedException("This device has been denied and cannot create new pairing requests.");
        }

        var code = GeneratePairingCode();
        var codeHash = GatewayApiKeyHelper.ComputeApiKeyHash(code);
        var now = DateTime.UtcNow;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var outstanding = await db.TblDevicePairingRequests
            .CountAsync(r => r.Status == "pending" && r.ExpiresAt > now);

        if (outstanding >= MaxOutstandingRequests)
        {
            _logger.LogWarning(
                "Refused a pairing request: {Outstanding} unapproved requests already outstanding for this tenant",
                outstanding);
            throw new DeviceBlockedException(
                "Too many pairing requests are already waiting for approval. Approve or deny some first.");
        }

        var request = new TblDevicePairingRequest
        {
            PairingRequestId = Guid.NewGuid(),
            PairingCodeHash = codeHash,
            Status = "pending",
            ExpiresAt = now.AddMinutes(ExpiryMinutes),
            CreateDate = now,
            DeviceId = deviceId,
            Manufacturer = manufacturer,
            Model = model,
            SerialNumber = serialNumber,
            IpAddress = ipAddress,
            MacAddress = macAddress,
            Platform = platform,
            FailedAttempts = 0
        };

        db.TblDevicePairingRequests.Add(request);
        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Created pairing request {RequestId} for device {DeviceId} from {IP}",
            request.PairingRequestId, deviceId, ipAddress);

        return new PairingRequestResult
        {
            PairingRequestId = request.PairingRequestId,
            PairingCode = code,
            ExpiresInSeconds = ExpiryMinutes * 60
        };
    }

    public async Task<PairingStatusResult> GetPairingStatusAsync(Guid pairingRequestId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var request = await db.TblDevicePairingRequests
            .FirstOrDefaultAsync(r => r.PairingRequestId == pairingRequestId);

        if (request == null)
        {
            return new PairingStatusResult { Status = "not_found" };
        }

        // Auto-expire if past TTL but still marked pending
        if (request.Status == "pending" && request.ExpiresAt < DateTime.UtcNow)
        {
            request.Status = "expired";
            request.ResolvedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        if (request.Status != "approved")
        {
            return new PairingStatusResult { Status = request.Status };
        }

        // Delivery is once. The poll used to hand back the plaintext token on every call for
        // fifteen minutes, keyed only on the request GUID, without requiring the six-digit code,
        // so anyone holding the GUID could collect a token the operator minted for a player.
        string? plaintextToken = null;

        if (request.DeliveryTokenCollectedAt == null
            && request.DeliveryTokenCreatedAt is { } mintedAt
            && DateTime.UtcNow - mintedAt <= DeliveryWindow)
        {
            plaintextToken = _secretProtector.Decrypt(request.DeliveryTokenProtected);

            if (plaintextToken != null)
            {
                request.DeliveryTokenCollectedAt = DateTime.UtcNow;
                request.DeliveryTokenProtected = null;
                await db.SaveChangesAsync();
            }
        }
        else if (request.DeliveryTokenProtected != null)
        {
            // Past the window or already collected: drop the ciphertext rather than keep a
            // collectable secret on a row that is retained for thirty days.
            request.DeliveryTokenProtected = null;
            await db.SaveChangesAsync();
        }

        return new PairingStatusResult
        {
            Status = "approved",
            DeviceToken = plaintextToken
        };
    }

    public async Task<TblDevicePairingRequest> ApprovePairingAsync(string pairingCode, int approvedByUserId)
    {
        var codeHash = GatewayApiKeyHelper.ComputeApiKeyHash(pairingCode);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var request = await db.TblDevicePairingRequests
            .FirstOrDefaultAsync(r =>
                r.PairingCodeHash == codeHash &&
                r.Status == "pending" &&
                r.ExpiresAt > DateTime.UtcNow);

        if (request == null)
        {
            // Record failed attempt on all pending requests with unknown codes
            // to prevent enumeration (the caller sees a generic error)
            var candidates = await db.TblDevicePairingRequests
                .Where(r => r.Status == "pending" && r.ExpiresAt > DateTime.UtcNow)
                .ToListAsync();

            foreach (var c in candidates)
            {
                c.FailedAttempts++;
                if (c.FailedAttempts >= MaxFailedAttempts)
                {
                    c.Status = "denied";
                    c.ResolvedAt = DateTime.UtcNow;
                    _logger.LogWarning("Pairing request {RequestId} auto-denied after {Attempts} failed attempts",
                        c.PairingRequestId, c.FailedAttempts);
                }
            }
            await db.SaveChangesAsync();

            throw new ArgumentException("Invalid or expired pairing code.");
        }

        // Create device token
        var tokenResult = await _deviceTokenService.CreateDeviceTokenAsync(
            request.PairingRequestId, approvedByUserId);

        // The plaintext exists only here, so it is persisted on the request row rather than in a
        // process-local dictionary: that dictionary was not tenant-keyed, did not survive a
        // restart, and did not exist on a second replica, so an app-pool recycle mid-takeover
        // stranded every uncollected token with re-arming by hand as the only recovery.
        request.DeliveryTokenProtected = _secretProtector.Encrypt(tokenResult.PlaintextToken);
        request.DeliveryTokenCreatedAt = DateTime.UtcNow;
        request.DeliveryTokenCollectedAt = null;

        request.Status = "approved";
        request.ResolvedAt = DateTime.UtcNow;
        request.ResolvedByUserId = approvedByUserId;

        await ApplyFlavourAsync(db, request, tokenResult.Token.DeviceTokenId, approvedByUserId);

        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Approved pairing request {RequestId} for device {DeviceId} by user {UserId}",
            request.PairingRequestId, request.DeviceId, approvedByUserId);

        return request;
    }

    /// <summary>
    /// Writes the approved flavour onto the device row, and cleans up what the old flavour left.
    ///
    /// Flavour comes from the approved request and never from a heartbeat, so a device cannot
    /// choose its own update stream by declaring one. The device keeps its row either way: both
    /// flavours identify a BrightSign player by serial, so the row is reused by construction and
    /// keeps its location, name, display, event log, metadata and Sentinel link.
    /// </summary>
    private async Task ApplyFlavourAsync(
        IDirectorDBContext db, TblDevicePairingRequest request, int newTokenId, int approvedByUserId)
    {
        if (string.IsNullOrEmpty(request.DeviceId))
            return;

        // A device holds exactly one live token. Nothing did this before, so every re-pairing on
        // every platform left the previous token working.
        var revoked = await _deviceTokenService.RevokeOtherTokensForDeviceAsync(request.DeviceId, newTokenId);

        if (string.IsNullOrWhiteSpace(request.Platform))
            return;

        var device = await db.TblDevices.FirstOrDefaultAsync(d => d.DeviceId == request.DeviceId);
        if (device == null)
        {
            // The row is created by the heartbeat, which may not have arrived yet. The request
            // keeps the flavour, and registration reads it from here.
            _logger.LogInformation(
                "Pairing request {RequestId} approved for {DeviceId} before its device row exists; flavour {Platform} stays on the request",
                request.PairingRequestId, request.DeviceId, request.Platform);
            return;
        }

        var platform = await db.TblAppPlatforms
            .FirstOrDefaultAsync(p => p.PlatformName == request.Platform && p.IsActive);

        if (platform == null)
        {
            _logger.LogWarning("Pairing request {RequestId} declares unknown or inactive platform {Platform}",
                request.PairingRequestId, request.Platform);
            return;
        }

        var previousPlatformId = device.PlatformId;
        if (previousPlatformId == platform.PlatformId)
            return;

        var previousPlatformName = previousPlatformId.HasValue
            ? (await db.TblAppPlatforms.FirstOrDefaultAsync(p => p.PlatformId == previousPlatformId))?.PlatformName
            : null;

        device.PlatformId = platform.PlatformId;

        // The pin is the only field that actively corrupts across a reflavour, because it is
        // returned ahead of any platform check. ClientAppVersion is left alone because the next
        // heartbeat overwrites it, and deployment rows are left alone because they are
        // platform-guarded and go inert on their own.
        var clearedPin = device.TargetAppVersionId.HasValue || device.TargetBootloaderVersionId.HasValue;
        device.TargetAppVersionId = null;
        device.TargetBootloaderVersionId = null;

        var clearedExtendedData = !string.IsNullOrEmpty(device.ExtendedData);
        device.ExtendedData = null;

        // Approved requests are purged after 30 days, so this row is the only durable record that
        // the device ever ran the other flavour.
        db.TblEventLogs.Add(new TblEventLog
        {
            EventSource = "DevicePairingService",
            EventTypeId = 1,
            EventSeverityId = 1,
            EventTitle = previousPlatformId.HasValue
                ? $"Reflavoured {request.DeviceId}: {previousPlatformName ?? previousPlatformId.ToString()} to {platform.PlatformName}"
                : $"Enrolled {request.DeviceId} as {platform.PlatformName}",
            EventMessage = previousPlatformId.HasValue
                ? "Pairing approval overwrote an existing flavour on this device."
                : null,
            DeviceId = request.DeviceId,
            CreateDate = DateTime.UtcNow,
            Details = System.Text.Json.JsonSerializer.Serialize(new
            {
                previousPlatformId,
                previousPlatformName,
                newPlatformId = platform.PlatformId,
                newPlatformName = platform.PlatformName,
                approvedByUserId,
                clearedTargetVersionPins = clearedPin,
                clearedExtendedData,
                revokedTokenCount = revoked,
                sentinelNote = "Sentinel's device descriptor keeps the protocol chosen for the old flavour, and the DWS password is per-device after enrollment; both may need revisiting."
            })
        });
    }

    public async Task DenyPairingAsync(Guid pairingRequestId, int deniedByUserId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var request = await db.TblDevicePairingRequests.FindAsync(pairingRequestId);
        if (request == null)
            throw new ArgumentException($"Pairing request {pairingRequestId} not found");

        request.Status = "denied";
        request.ResolvedAt = DateTime.UtcNow;
        request.ResolvedByUserId = deniedByUserId;
        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Denied pairing request {RequestId} by user {UserId}",
            pairingRequestId, deniedByUserId);
    }

    public async Task<List<TblDevicePairingRequest>> GetPendingRequestsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var now = DateTime.UtcNow;

        return await db.TblDevicePairingRequests
            .Where(r => r.Status == "pending" && r.ExpiresAt > now)
            .OrderByDescending(r => r.CreateDate)
            .ToListAsync();
    }

    public async Task<int> ExpireStaleRequestsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var now = DateTime.UtcNow;
        var stale = await db.TblDevicePairingRequests
            .Where(r => r.Status == "pending" && r.ExpiresAt < now)
            .ToListAsync();

        foreach (var r in stale)
        {
            r.Status = "expired";
            r.ResolvedAt = now;
        }

        if (stale.Count > 0)
            await db.SaveChangesAsync();

        return stale.Count;
    }

    public async Task<int> PurgeOldRequestsAsync(TimeSpan retention)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var cutoff = DateTime.UtcNow - retention;
        var old = await db.TblDevicePairingRequests
            .Where(r => r.Status != "pending" && r.Status != "denied" && r.ResolvedAt < cutoff)
            .ToListAsync();

        if (old.Count > 0)
        {
            db.TblDevicePairingRequests.RemoveRange(old);
            await db.SaveChangesAsync();
        }

        return old.Count;
    }

    public async Task<bool> IsDeviceBlockedAsync(string? deviceId, string? macAddress)
    {
        if (string.IsNullOrEmpty(deviceId) && string.IsNullOrEmpty(macAddress))
            return false;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await db.TblDevicePairingRequests.AnyAsync(r =>
            r.Status == "denied" &&
            ((!string.IsNullOrEmpty(deviceId) && r.DeviceId == deviceId) ||
             (!string.IsNullOrEmpty(macAddress) && r.MacAddress == macAddress)));
    }

    public async Task<List<TblDevicePairingRequest>> GetDeniedRequestsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await db.TblDevicePairingRequests
            .Where(r => r.Status == "denied")
            .OrderByDescending(r => r.ResolvedAt)
            .ToListAsync();
    }

    public async Task UnblockDeviceAsync(Guid pairingRequestId, int unblockedByUserId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var request = await db.TblDevicePairingRequests.FindAsync(pairingRequestId);
        if (request == null || request.Status != "denied")
            throw new ArgumentException($"Denied pairing request {pairingRequestId} not found");

        db.TblDevicePairingRequests.Remove(request);
        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Unblocked device {DeviceId} (MAC: {Mac}) by user {UserId}",
            request.DeviceId, request.MacAddress, unblockedByUserId);
    }

    /// <summary>
    /// Generates a 6-digit numeric code (100000-999999).
    /// </summary>
    private static string GeneratePairingCode()
    {
        var code = RandomNumberGenerator.GetInt32(100000, 1000000);
        return code.ToString();
    }

}
