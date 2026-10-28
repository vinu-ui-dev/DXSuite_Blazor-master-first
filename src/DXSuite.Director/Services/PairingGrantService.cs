using DXSuite.Director.Entities;
using DXSuite.Director.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services;

/// <summary>
/// Mints the short-lived, single-use, per-serial pairing grant a takeover bootstrap carries.
///
/// Enrollment is pairing-only, and pairing needs a human to read a six-digit code. A no-technician
/// takeover has no such human, and at a hundred players enrollment becomes the only step in the
/// operation that does not divide. A grant is not a baked tenant key: it is per-serial, single-use
/// and minutes long, and it fails safe in every direction a leaked key would not. The human gate
/// it replaces is the operator having armed that serial in the first place.
/// </summary>
public interface IPairingGrantService
{
    /// <summary>Mints a grant for an already-armed serial and returns the plaintext device token.</summary>
    Task<string> MintAsync(string serialNumber);
}

public sealed class PairingGrantService : IPairingGrantService
{
    /// <summary>
    /// Long enough for a download, a card write and a reboot; short enough that a bootstrap left
    /// on a shelf is worthless. Re-arming is the answer to "it needed longer".
    /// </summary>
    private static readonly TimeSpan GrantLifetime = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeviceTokenService _deviceTokenService;
    private readonly ILogger<PairingGrantService> _logger;

    public PairingGrantService(
        IServiceScopeFactory scopeFactory, IDeviceTokenService deviceTokenService,
        ILogger<PairingGrantService> logger)
    {
        _scopeFactory = scopeFactory;
        _deviceTokenService = deviceTokenService;
        _logger = logger;
    }

    public async Task<string> MintAsync(string serialNumber)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        // Recorded as an approved pairing request so the grant travels the same path, and shows up
        // on the same screens, as a token an operator approved by hand. Nothing about it is
        // special except who authorised it.
        var request = new TblDevicePairingRequest
        {
            PairingRequestId = Guid.NewGuid(),
            // No code is ever issued for a grant, so the hash is of a value nobody holds. The
            // approval gate here is the armed serial, not a six-digit code.
            PairingCodeHash = GatewayApiKeyHelper.ComputeApiKeyHash(Guid.NewGuid().ToString("N")),
            Status = "approved",
            SerialNumber = serialNumber,
            Platform = RecoveryHandlerService.EngageBrightSignPlatformName,
            CreateDate = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(GrantLifetime),
            ResolvedAt = DateTime.UtcNow
        };

        db.TblDevicePairingRequests.Add(request);
        await db.SaveChangesAsync();

        var token = await _deviceTokenService.CreateDeviceTokenAsync(request.PairingRequestId, null);

        _logger.LogInformation("Minted a pairing grant for armed serial {SerialNumber}, valid until {ExpiresAt}",
            serialNumber, request.ExpiresAt);

        return token.PlaintextToken;
    }
}
