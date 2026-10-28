using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using DXSuite.Director.Models;
using DXSuite.Director.Services;
using DXSuite.Shared.Models;

namespace DXSuite.Director.Controllers;

/// <summary>
/// Unauthenticated endpoints for the device pairing enrollment flow.
/// Devices call these before they have any credentials.
/// </summary>
[Route("api/signage/[controller]")]
[ApiController]
[EnableCors("SignageDevices")]
[EnableRateLimiting(RecoveryRateLimiting.PairingPolicyName)]
public class PairingController : ControllerBase
{
    private readonly IDevicePairingService _pairingService;
    private readonly IOptions<FeatureFlags> _featureFlags;
    private readonly ILogger<PairingController> _logger;

    public PairingController(IDevicePairingService pairingService, IOptions<FeatureFlags> featureFlags, ILogger<PairingController> logger)
    {
        _pairingService = pairingService;
        _featureFlags = featureFlags;
        _logger = logger;
    }

    /// <summary>
    /// Device requests a pairing code. Returns a 6-digit code and a GUID request ID.
    /// </summary>
    [HttpPost("request")]
    public async Task<IActionResult> RequestPairing([FromBody] PairingRequestBody body)
    {
        if (!_featureFlags.Value.Engage) return NotFound();

        try
        {
            var result = await _pairingService.CreatePairingRequestAsync(
                body.DeviceId, body.Manufacturer, body.Model,
                body.SerialNumber, body.IpAddress, body.MacAddress, body.Platform);

            return Ok(new
            {
                pairingRequestId = result.PairingRequestId,
                pairingCode = result.PairingCode,
                expiresInSeconds = result.ExpiresInSeconds
            });
        }
        catch (DeviceBlockedException ex)
        {
            return StatusCode(403, new { status = "blocked", reason = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create pairing request");
            return StatusCode(500, "Failed to create pairing request");
        }
    }

    /// <summary>
    /// Device polls for pairing status. Returns "pending", "approved" (with token), "denied", or "expired".
    /// </summary>
    [HttpPost("status")]
    public async Task<IActionResult> CheckStatus([FromBody] PairingStatusBody body)
    {
        if (!_featureFlags.Value.Engage) return NotFound();

        try
        {
            var result = await _pairingService.GetPairingStatusAsync(body.PairingRequestId);

            return Ok(new
            {
                status = result.Status,
                deviceToken = result.DeviceToken
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check pairing status");
            return StatusCode(500, "Failed to check pairing status");
        }
    }
}

public class PairingRequestBody
{
    public string? DeviceId { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? IpAddress { get; set; }
    public string? MacAddress { get; set; }

    // The flavour the player declares. Approval copies it to the device row, so a device never
    // sets its own update stream from a heartbeat.
    public string? Platform { get; set; }
}

public class PairingStatusBody
{
    public Guid PairingRequestId { get; set; }
}
