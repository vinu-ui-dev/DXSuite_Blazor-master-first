using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DXSuite.Director.Controllers;

/// <summary>
/// Writes for CUPPS counter publishing. [AllowAnonymous] for the same loopback reason as
/// <see cref="CuppsAirlinesController"/> and <see cref="CounterTemplatesController"/> — but unlike
/// those read endpoints, a write cannot fall back to "unrestricted when the caller can't be identified".
/// Every request here must carry a valid X-Internal-Caller-Token (minted by
/// <see cref="IInternalCallerTokenService"/> from the Blazor circuit's own authenticated
/// ISessionState) identifying the acting user; requests without one are rejected outright.
/// </summary>
[Route("api/counter-publish")]
[ApiController]
[AllowAnonymous]
public sealed class CounterPublishController : ControllerBase
{
    private readonly ICounterPublishService _publishService;
    private readonly IInternalCallerTokenService _tokenService;

    public CounterPublishController(ICounterPublishService publishService, IInternalCallerTokenService tokenService)
    {
        _publishService = publishService;
        _tokenService = tokenService;
    }

    /// <summary>Read-only snapshot of every counter's persisted screen state. Anonymous and best-effort, like the
    /// other CUPPS GETs (counter-templates/page-options, cupps/airlines), because CuppsLiveDataProvider calls it
    /// over an internal loopback HttpClient that carries no auth cookie.</summary>
    [HttpGet("states")]
    public async Task<IActionResult> GetStates(CancellationToken ct) =>
        Ok(await _publishService.GetStatesAsync(ct));

    [HttpPost("publish")]
    public async Task<IActionResult> Publish([FromBody] PublishRequestDto body, CancellationToken ct)
    {
        var username = _tokenService.ValidateToken(Request.Headers["X-Internal-Caller-Token"]);
        if (username is null)
            return Unauthorized(new { message = "Missing or invalid caller token." });

        if (string.IsNullOrWhiteSpace(body.CounterId) || body.AirlineId <= 0)
            return BadRequest(new { message = "counterId and a positive airlineId are required." });

        var result = await _publishService.PublishAsync(new CounterPublishRequest(
            body.CounterId,
            body.AirlineId,
            username,
            body.CounterTemplateId,
            body.CounterTemplateKind,
            body.CounterTemplateName,
            body.Destination,
            body.Sunflower,
            body.PageShowSeconds,
            body.AslOn), ct);

        if (!result.Success)
            return Forbid();

        return Ok(new { publishId = result.PublishId });
    }

    [HttpPost("close")]
    public async Task<IActionResult> Close([FromBody] CloseRequestDto body, CancellationToken ct)
    {
        var username = _tokenService.ValidateToken(Request.Headers["X-Internal-Caller-Token"]);
        if (username is null)
            return Unauthorized(new { message = "Missing or invalid caller token." });

        if (string.IsNullOrWhiteSpace(body.CounterId))
            return BadRequest(new { message = "counterId is required." });

        var result = await _publishService.CloseAsync(body.CounterId, username, ct);
        if (!result.Success)
            return Forbid();

        return Ok();
    }
}

public sealed record PublishRequestDto(
    string CounterId,
    int AirlineId,
    int? CounterTemplateId,
    string? CounterTemplateKind,
    string? CounterTemplateName,
    string? Destination,
    bool? Sunflower,
    int? PageShowSeconds,
    bool? AslOn = null);

public sealed record CloseRequestDto(string CounterId);
