using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DXSuite.Director.Authorization;
using DXSuite.Director.Services;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Director.Controllers;

/// <summary>
/// The delivery route Radiant's Moxie script pulls (INK-03): one document for every display (or every display of the
/// key's location, for a location-restricted key), with a hash ETag so an unchanged pull is a 304 (INK-05). API keys
/// only, so the AdminApiKey scheme's internal networks refusal applies (HOST-03); 404 while the tenant's Messaging flag
/// is off (DEV-06).
/// </summary>
[Route("api/messaging")]
[ApiController]
[Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
[RequiresTenantFeature(nameof(TenantFeatureFlags.Messaging))]
public sealed class MessagingDeliveryController : ControllerBase
{
    private readonly MessageDelivery _delivery;

    public MessagingDeliveryController(MessageDelivery delivery) => _delivery = delivery;

    /// <summary>The schema 1 delivery document (INK-04).</summary>
    /// <response code="200">The document, with its ETag. Shown versions in it are stamped Delivered to signage the first time.</response>
    /// <response code="304">The document is unchanged since the ETag in <c>If-None-Match</c>.</response>
    /// <response code="403">The key lacks <c>messaging:read</c>.</response>
    [HttpGet("delivery")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (this.CheckScope(ApiKeyScopes.MessagingRead) is { } denied) return denied;

        // A key restricted to a location reads that location's displays only, as on every other key route (HOST-03).
        var document = await _delivery.BuildAsync(HttpContext.Items["ApiKeyLocationId"] as int?, ct);
        if (this.NotModified(document.ETag) is { } notModified) return notModified;

        // A 304 carries nothing new, so only a 200 can be the first time a version left Director (INK-06).
        await _delivery.MarkDeliveredAsync(document, ct);
        return File(document.Json, "application/json");
    }
}
