using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DXSuite.Director.Authorization;
using DXSuite.Director.Services;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Director.Controllers;

/// <summary>
/// The API trigger (MSG-19): an external system sends an Approved canned Message by API key. Free text is never
/// accepted. The AdminApiKey scheme refuses keys from outside <c>Network:InternalNetworks</c> when it is set (HOST-03),
/// and the route answers 404 while the tenant's Messaging flag is off (DEV-06).
/// </summary>
[Route("api/messaging")]
[ApiController]
[Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
[RequiresTenantFeature(nameof(TenantFeatureFlags.Messaging))]
public sealed class MessagingApiController : ControllerBase
{
    private readonly MessageSending _sending;

    public MessagingApiController(MessageSending sending) => _sending = sending;

    /// <summary>
    /// Sends one canned Message to Display Zones or to one display, now or over a start and an end. The Activation and its
    /// frozen screen rows are saved exactly as the send dialog saves them, with source <c>Api</c>.
    /// </summary>
    /// <response code="201">Sent: the Activation's id and the displays it reaches.</response>
    /// <response code="400">The request is malformed, names free text, or a field is out of range (<c>errors</c>).</response>
    /// <response code="403">The key lacks <c>messaging:send</c>.</response>
    /// <response code="422">The Message cannot be sent: no canned Message has the id, it is not Approved, or the target reaches no screen.</response>
    [HttpPost("activations")]
    public async Task<IActionResult> Send([FromBody] MessageApiSendRequest request, CancellationToken ct)
    {
        if (this.CheckScope(ApiKeyScopes.MessagingSend) is { } denied) return denied;
        if (RequestError(request) is { } error) return BadRequest(new { message = error });

        var target = request.DisplayId is { } displayId ? MessageSendTarget.Display(displayId) : MessageSendTarget.DisplayZones([.. request.ZoneIds!]);
        MessageSendTiming when = request.Start is { } start
            ? new MessageSendTiming.Scheduled(start, request.End!.Value)
            : new MessageSendTiming.SendNow(request.Minutes);
        var caller = new MessageApiCaller(KeyName(), UserEventActor.FormatIp(HttpContext.Connection.RemoteIpAddress));

        var result = await _sending.SendFromApiAsync(request.MessageId!.Value, target, when, caller, ct);
        return result.Outcome switch
        {
            MessageSendOutcome.Sent => StatusCode(StatusCodes.Status201Created, new MessageApiSendResponse(
                result.MessageActivationId!.Value, result.ScreenCount, result.Screens.Select(s => s.DisplayId).ToList())),
            MessageSendOutcome.Invalid => BadRequest(new { message = "The send is not valid.", errors = result.Errors }),
            MessageSendOutcome.NotAllowed => StatusCode(StatusCodes.Status403Forbidden, new { message = result.Problem }),
            _ => UnprocessableEntity(new { message = result.Problem }),
        };
    }

    /// <summary>What is wrong with the request's shape, before anything is read; null when nothing is.</summary>
    private static string? RequestError(MessageApiSendRequest request)
    {
        // Free text is never accepted (MSG-19), under any field name, so a field this route does not know is refused
        // rather than dropped from a send that would then go out without it.
        if (request.Unknown is { Count: > 0 } unknown)
            return $"Unknown field {string.Join(", ", unknown.Keys)}. Free text is never accepted: name an Approved canned Message by messageId.";
        if (request.MessageId is null) return "Name the canned Message to send (messageId).";

        var zones = request.ZoneIds is { Count: > 0 };
        if (zones == request.DisplayId.HasValue) return "Name Display Zones (zoneIds) or one display (displayId), not both.";

        if (request.Start.HasValue != request.End.HasValue) return "Give both start and end, or neither to send now.";
        if (request.Start is { } start)
        {
            if (request.Minutes is not null) return "Give minutes to send now, or start and end, not both.";
            // Start and end are wall-clock times read on each screen's own clock, as the send dialog reads them.
            if (start.Kind != DateTimeKind.Unspecified || request.End!.Value.Kind != DateTimeKind.Unspecified)
                return "Give start and end as local times without an offset; each screen reads them in its own time zone.";
        }

        return null;
    }

    // The scheme puts the key's name on the principal; the event log names the System actor by it (A-26).
    private string KeyName() =>
        User.FindFirst("ApiKeyName")?.Value is { Length: > 0 } name ? name : "API key";
}

/// <summary>
/// One API send. Name Display Zones (<see cref="ZoneIds"/>) or one display (<see cref="DisplayId"/>). Leave
/// <see cref="Start"/> and <see cref="End"/> out to send now, for <see cref="Minutes"/> or, without it, until cancelled.
/// </summary>
public sealed class MessageApiSendRequest
{
    /// <summary>An Approved canned Message.</summary>
    public int? MessageId { get; set; }

    public List<int>? ZoneIds { get; set; }

    public int? DisplayId { get; set; }

    /// <summary>Send now for this many minutes; null shows until cancelled.</summary>
    public int? Minutes { get; set; }

    /// <summary>Local wall-clock start (inclusive), with no offset, read in each screen's time zone.</summary>
    public DateTime? Start { get; set; }

    /// <summary>Local wall-clock end (exclusive), with no offset, read in each screen's time zone.</summary>
    public DateTime? End { get; set; }

    /// <summary>Fields the route does not know, such as free text; any is refused.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <param name="ActivationId">The Message Activation the send created.</param>
/// <param name="ScreenCount">How many displays it reaches, frozen at send.</param>
/// <param name="DisplayIds">The displays it reaches.</param>
public sealed record MessageApiSendResponse(int ActivationId, int ScreenCount, IReadOnlyList<int> DisplayIds);
