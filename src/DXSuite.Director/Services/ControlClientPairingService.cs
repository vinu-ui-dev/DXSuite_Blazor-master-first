using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.Security;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace DXSuite.Director.Services;

public class ControlClientPairingService : IControlClientPairingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IControlClientTokenService _tokenService;
    private readonly ILogger<ControlClientPairingService> _logger;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// In-memory cache for token delivery after pairing approval.
    /// Plaintext tokens cannot be stored in the DB (only the hash is persisted),
    /// so they live here until the client app retrieves them via status poll.
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, (string Token, DateTime CreatedAt)> _pendingTokenDelivery = new();

    private const int MaxFailedAttempts = 5;

    public ControlClientPairingService(
        IServiceScopeFactory scopeFactory,
        IControlClientTokenService tokenService,
        ILogger<ControlClientPairingService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _tokenService = tokenService;
        _logger = logger;
        _configuration = configuration;
    }

    private int ExpiryMinutes => _configuration.GetValue("Security:ControlClientPairingExpiryMinutes", 10);

    public async Task<ControlClientPairingRequestResult> CreatePairingRequestAsync(string? hostname, string? ipAddress)
    {
        var code = GeneratePairingCode();
        var codeHash = GatewayApiKeyHelper.ComputeApiKeyHash(code);
        var now = DateTime.UtcNow;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var request = new TblControlClientPairingRequest
        {
            PairingRequestId = Guid.NewGuid(),
            PairingCodeHash = codeHash,
            Status = "pending",
            ClientHostname = hostname,
            ClientIp = ipAddress,
            ExpiresAt = now.AddMinutes(ExpiryMinutes),
            CreateDate = now,
            FailedAttempts = 0
        };

        db.TblControlClientPairingRequests.Add(request);
        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Created control client pairing request {RequestId} from {Hostname} ({IP})",
            request.PairingRequestId, hostname, ipAddress);

        return new ControlClientPairingRequestResult
        {
            PairingRequestId = request.PairingRequestId,
            PairingCode = code,
            ExpiresInSeconds = ExpiryMinutes * 60
        };
    }

    public async Task<ControlClientPairingStatusResult> GetPairingStatusAsync(Guid pairingRequestId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var request = await db.TblControlClientPairingRequests
            .FirstOrDefaultAsync(r => r.PairingRequestId == pairingRequestId);

        if (request == null)
        {
            return new ControlClientPairingStatusResult { Status = "not_found" };
        }

        if (request.Status == "pending" && request.ExpiresAt < DateTime.UtcNow)
        {
            request.Status = "expired";
            request.ResolvedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        if (request.Status != "approved")
        {
            return new ControlClientPairingStatusResult { Status = request.Status };
        }

        string? plaintextToken = null;
        if (_pendingTokenDelivery.TryGetValue(request.PairingRequestId, out var entry))
        {
            plaintextToken = entry.Token;
        }

        return new ControlClientPairingStatusResult
        {
            Status = "approved",
            DeviceToken = plaintextToken
        };
    }

    public async Task<TblControlClientPairingRequest> ApprovePairingAsync(string pairingCode, int controlClientId, int approvedByUserId)
    {
        var codeHash = GatewayApiKeyHelper.ComputeApiKeyHash(pairingCode);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var request = await db.TblControlClientPairingRequests
            .FirstOrDefaultAsync(r =>
                r.PairingCodeHash == codeHash &&
                r.Status == "pending" &&
                r.ExpiresAt > DateTime.UtcNow);

        if (request == null)
        {
            var candidates = await db.TblControlClientPairingRequests
                .Where(r => r.Status == "pending" && r.ExpiresAt > DateTime.UtcNow)
                .ToListAsync();

            foreach (var c in candidates)
            {
                c.FailedAttempts++;
                if (c.FailedAttempts >= MaxFailedAttempts)
                {
                    c.Status = "denied";
                    c.ResolvedAt = DateTime.UtcNow;
                    _logger.LogWarning("Control client pairing request {RequestId} auto-denied after {Attempts} failed attempts",
                        c.PairingRequestId, c.FailedAttempts);
                }
            }
            await db.SaveChangesAsync();

            throw new ArgumentException("Invalid or expired pairing code.");
        }

        var tokenResult = await _tokenService.CreateTokenAsync(controlClientId);

        _pendingTokenDelivery[request.PairingRequestId] = (tokenResult.PlaintextToken, DateTime.UtcNow);
        CleanExpiredDeliveryEntries();

        request.Status = "approved";
        request.ControlClientId = controlClientId;
        request.ResolvedAt = DateTime.UtcNow;
        request.ResolvedByUserId = approvedByUserId;
        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Approved control client pairing request {RequestId} → client {ClientId} by user {UserId}",
            request.PairingRequestId, controlClientId, approvedByUserId);

        return request;
    }

    public async Task DenyPairingAsync(Guid pairingRequestId, int deniedByUserId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var request = await db.TblControlClientPairingRequests.FindAsync(pairingRequestId);
        if (request == null)
            throw new ArgumentException($"Pairing request {pairingRequestId} not found");

        request.Status = "denied";
        request.ResolvedAt = DateTime.UtcNow;
        request.ResolvedByUserId = deniedByUserId;
        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Denied control client pairing request {RequestId} by user {UserId}",
            pairingRequestId, deniedByUserId);
    }

    public async Task<List<TblControlClientPairingRequest>> GetPendingRequestsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var now = DateTime.UtcNow;

        return await db.TblControlClientPairingRequests
            .Where(r => r.Status == "pending" && r.ExpiresAt > now)
            .OrderByDescending(r => r.CreateDate)
            .ToListAsync();
    }

    public async Task<int> ExpireStaleRequestsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var now = DateTime.UtcNow;
        var stale = await db.TblControlClientPairingRequests
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
        var old = await db.TblControlClientPairingRequests
            .Where(r => r.Status != "pending" && r.ResolvedAt < cutoff)
            .ToListAsync();

        if (old.Count > 0)
        {
            db.TblControlClientPairingRequests.RemoveRange(old);
            await db.SaveChangesAsync();
        }

        return old.Count;
    }

    private static string GeneratePairingCode()
    {
        var code = RandomNumberGenerator.GetInt32(100000, 1000000);
        return code.ToString();
    }

    private static void CleanExpiredDeliveryEntries()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-15);
        foreach (var kvp in _pendingTokenDelivery)
        {
            if (kvp.Value.CreatedAt < cutoff)
                _pendingTokenDelivery.TryRemove(kvp.Key, out _);
        }
    }
}
