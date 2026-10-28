using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using DXSuite.Director.Services;

namespace DXSuite.Director.Controllers;

/// <summary>
/// API endpoints for Control Client apps.
/// Pairing endpoints are unauthenticated; all others require a cc_ token via X-API-KEY.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[EnableCors("SignageDevices")]
public class ControlClientController : ControllerBase
{
    private readonly IControlClientService _clientService;
    private readonly IControlClientPairingService _pairingService;
    private readonly ILogger<ControlClientController> _logger;

    public ControlClientController(
        IControlClientService clientService,
        IControlClientPairingService pairingService,
        ILogger<ControlClientController> logger)
    {
        _clientService = clientService;
        _pairingService = pairingService;
        _logger = logger;
    }

    #region Pairing (unauthenticated)

    [HttpPost("pairing/request")]
    public async Task<IActionResult> RequestPairing([FromBody] ControlClientPairingRequestBody body)
    {
        try
        {
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            var result = await _pairingService.CreatePairingRequestAsync(body.Hostname, clientIp);

            return Ok(new
            {
                pairingRequestId = result.PairingRequestId,
                pairingCode = result.PairingCode,
                expiresInSeconds = result.ExpiresInSeconds
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create control client pairing request");
            return StatusCode(500, "Failed to create pairing request");
        }
    }

    [HttpPost("pairing/status")]
    public async Task<IActionResult> CheckPairingStatus([FromBody] ControlClientPairingStatusBody body)
    {
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
            _logger.LogError(ex, "Failed to check control client pairing status");
            return StatusCode(500, "Failed to check pairing status");
        }
    }

    #endregion

    #region Session Auth (requires cc_ token)

    [HttpPost("auth/session")]
    public async Task<IActionResult> AuthenticateSession([FromBody] ControlClientSessionBody body)
    {
        var controlClientId = GetControlClientId();
        if (controlClientId == null)
            return Unauthorized("Invalid control client token");

        try
        {
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            var valid = await _clientService.ValidateAccessCodeAsync(controlClientId.Value, body.AccessCode, clientIp);

            if (!valid)
                return Unauthorized(new { error = "Invalid access code" });

            var client = await _clientService.GetClientByIdAsync(controlClientId.Value);
            if (client == null)
                return Unauthorized(new { error = "Client not found" });

            return Ok(new
            {
                clientId = client.ControlClientId,
                clientName = client.ClientName,
                locationId = client.LocationId,
                locationName = client.Location?.LocationName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Control client session auth failed for client {ClientId}", controlClientId);
            return StatusCode(500, "Authentication failed");
        }
    }

    [HttpPost("auth/validate")]
    public async Task<IActionResult> ValidateToken()
    {
        var controlClientId = GetControlClientId();
        if (controlClientId == null)
            return Unauthorized("Invalid control client token");

        var client = await _clientService.GetClientByIdAsync(controlClientId.Value);
        if (client == null || !client.IsActive)
            return Unauthorized(new { error = "Client not found or disabled" });

        return Ok(new
        {
            clientId = client.ControlClientId,
            clientName = client.ClientName,
            locationId = client.LocationId,
            locationName = client.Location?.LocationName,
            authMode = client.AuthMode
        });
    }

    #endregion

    #region Display & Content (requires cc_ token)

    [HttpGet("displays")]
    public async Task<IActionResult> GetDisplays()
    {
        var controlClientId = GetControlClientId();
        if (controlClientId == null)
            return Unauthorized("Invalid control client token");

        try
        {
            var displays = await _clientService.GetEffectiveDisplaysAsync(controlClientId.Value);

            return Ok(displays.Select(d => new
            {
                displayId = d.DisplayId,
                displayName = d.DisplayName,
                displayMode = d.DisplayMode,
                overrideAssetId = d.OverrideAssetId,
                locationId = d.LocationId
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get displays for control client {ClientId}", controlClientId);
            return StatusCode(500, "Failed to get displays");
        }
    }

    [HttpGet("assets")]
    public async Task<IActionResult> GetAssets()
    {
        var controlClientId = GetControlClientId();
        if (controlClientId == null)
            return Unauthorized("Invalid control client token");

        try
        {
            var assets = await _clientService.GetAvailableAssetsAsync(controlClientId.Value);

            return Ok(assets.Select(a => new
            {
                assetId = a.AssetId,
                assetName = a.AssetName,
                assetTypeId = a.AssetTypeId,
                assetPath = a.AssetPath
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get assets for control client {ClientId}", controlClientId);
            return StatusCode(500, "Failed to get assets");
        }
    }

    [HttpPut("displays/{displayId}/content")]
    public async Task<IActionResult> SetDisplayContent(int displayId, [FromBody] SetContentBody body)
    {
        var controlClientId = GetControlClientId();
        if (controlClientId == null)
            return Unauthorized("Invalid control client token");

        try
        {
            await _clientService.SetDisplayOverrideAsync(controlClientId.Value, displayId, body.AssetId);
            return Ok(new { success = true });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set display content for client {ClientId}, display {DisplayId}",
                controlClientId, displayId);
            return StatusCode(500, "Failed to set display content");
        }
    }

    [HttpDelete("displays/{displayId}/override")]
    public async Task<IActionResult> ClearDisplayOverride(int displayId)
    {
        var controlClientId = GetControlClientId();
        if (controlClientId == null)
            return Unauthorized("Invalid control client token");

        try
        {
            await _clientService.ClearDisplayOverrideAsync(controlClientId.Value, displayId);
            return Ok(new { success = true });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear display override for client {ClientId}, display {DisplayId}",
                controlClientId, displayId);
            return StatusCode(500, "Failed to clear override");
        }
    }

    #endregion

    private int? GetControlClientId()
    {
        if (HttpContext.Items.TryGetValue("ControlClientId", out var value) && value is int id)
            return id;
        return null;
    }
}

#region Request/Response DTOs

public class ControlClientPairingRequestBody
{
    public string? Hostname { get; set; }
}

public class ControlClientPairingStatusBody
{
    public Guid PairingRequestId { get; set; }
}

public class ControlClientSessionBody
{
    public string AccessCode { get; set; } = null!;
}

public class SetContentBody
{
    public int AssetId { get; set; }
}

#endregion
