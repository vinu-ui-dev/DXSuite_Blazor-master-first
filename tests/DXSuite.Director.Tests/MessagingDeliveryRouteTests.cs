using DXSuite.Director.Controllers;
using DXSuite.Director.Services;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.CustomerDeviations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static DXSuite.Director.Tests.MessageAccessDatabase;

namespace DXSuite.Director.Tests;

// Clause ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// <c>GET /api/messaging/delivery</c> (B-81, INK-03, INK-05, INK-06) over a Director database on LocalDB: the
/// <c>messaging:read</c> scope, the hash ETag and 304, and Delivered to signage stamped by the first 200 only. The
/// scheme, the internal networks refusal (HOST-03) and the tenant gate sit in front of the action; see
/// <see cref="TenantFeatureRouteGateTests"/>. The body itself is covered in <see cref="MessageDeliveryTests"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessagingDeliveryRouteTests : IClassFixture<MessageAccessDatabase>, IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);

    private readonly MessageAccessDatabase _db;

    public MessagingDeliveryRouteTests(MessageAccessDatabase db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // INK-03: the script's key holds messaging:read, which the API Keys settings offer; signage:heartbeat is device-only.
    [LocalDbFact]
    public async Task A_key_without_messaging_read_is_refused_and_nothing_is_stamped()
    {
        var version = await LiveVersionAsync();

        var refused = await GetAsync(scopes: ApiKeyScopes.SignageHeartbeat);

        Assert.Equal(StatusCodes.Status403Forbidden, refused.Status);
        Assert.Null(await FirstServedAsync(version));
        Assert.Contains(ApiKeyScopes.MessagingRead, ApiKeyScopes.UserSelectableScopes);
        Assert.Equal("messaging:read", ApiKeyScopes.MessagingRead);
        Assert.Equal(StatusCodes.Status200OK, (await GetAsync(scopes: ApiKeyScopes.Wildcard)).Status);
    }

    // INK-05, INK-06: a 200 serves the built document under its ETag and stamps Delivered to signage; a later 200 does not
    // stamp again.
    [LocalDbFact]
    public async Task The_first_200_serves_the_document_with_its_ETag_and_stamps_Delivered_to_signage_once()
    {
        var version = await LiveVersionAsync();
        var expected = await Delivery(Now).BuildAsync(default);

        var first = await GetAsync();
        var later = await GetAsync(clockUtc: Now.AddMinutes(1));

        Assert.Equal(StatusCodes.Status200OK, first.Status);
        Assert.Equal(expected.Json, first.Body);
        Assert.Equal("application/json", first.ContentType);
        Assert.Equal(expected.ETag, first.ETag);
        Assert.Equal(expected.ETag, later.ETag);
        Assert.Equal(StatusCodes.Status200OK, later.Status);
        Assert.Equal(Now, await FirstServedAsync(version));
    }

    // INK-05: a matching If-None-Match gets 304 with no body; a 304 never stamps, since only a 200 delivers.
    [LocalDbFact]
    public async Task A_matching_If_None_Match_gets_304_with_no_body_and_stamps_nothing()
    {
        var version = await LiveVersionAsync();
        var etag = (await Delivery(Now).BuildAsync(default)).ETag;

        var unchanged = await GetAsync(ifNoneMatch: etag);
        var stale = await GetAsync(ifNoneMatch: "\"c3RhbGU=\"");

        Assert.Equal(StatusCodes.Status304NotModified, unchanged.Status);
        Assert.Null(unchanged.Body);
        Assert.Equal(etag, unchanged.ETag);
        Assert.Equal(StatusCodes.Status200OK, stale.Status);
        Assert.Equal(Now, await FirstServedAsync(version));
        await using var db = _db.Director();
        await db.TblMessageActivationVersions.ExecuteUpdateAsync(s => s.SetProperty(v => v.FirstServedUtc, (DateTime?)null));
        Assert.Equal(StatusCodes.Status304NotModified, (await GetAsync(ifNoneMatch: etag)).Status);
        Assert.Null(await FirstServedAsync(version));
    }

    // INK-03, HOST-03: a key restricted to a location reads only that location's displays, as on every other key route,
    // under that body's own ETag; an unrestricted key keeps the whole body.
    [LocalDbFact]
    public async Task A_location_restricted_key_gets_only_that_locations_displays_under_its_own_ETag()
    {
        var version = await LiveVersionAsync();

        var all = await GetAsync();
        var terminal1 = await GetAsync(keyLocationId: Terminal1);
        var terminal3 = await GetAsync(keyLocationId: Terminal3);

        Assert.Equal([GateA1, GateA2, Lounge, CheckIn1, CheckIn2], DisplayIdsIn(all.Body));
        Assert.Equal([GateA1, GateA2, Lounge], DisplayIdsIn(terminal1.Body));
        Assert.Equal([CheckIn1, CheckIn2], DisplayIdsIn(terminal3.Body));
        Assert.Equal(3, new[] { all.ETag, terminal1.ETag, terminal3.ETag }.Distinct().Count());
        Assert.Equal(StatusCodes.Status304NotModified, (await GetAsync(ifNoneMatch: terminal1.ETag, keyLocationId: Terminal1)).Status);
        Assert.Equal(StatusCodes.Status200OK, (await GetAsync(ifNoneMatch: terminal1.ETag)).Status);
        Assert.Equal(StatusCodes.Status200OK, (await GetAsync(ifNoneMatch: all.ETag, keyLocationId: Terminal1)).Status);
        Assert.Equal(StatusCodes.Status304NotModified, (await GetAsync(ifNoneMatch: all.ETag)).Status);
        Assert.Equal(Now, await FirstServedAsync(version));
    }

    // INK-06: a body that does not carry a version leaves it undelivered, so the first body that does carry it stamps it.
    [LocalDbFact]
    public async Task A_restricted_body_without_the_version_does_not_stamp_it()
    {
        var version = await LiveVersionAsync();

        await GetAsync(keyLocationId: Terminal3);
        Assert.Null(await FirstServedAsync(version));

        await GetAsync(keyLocationId: Terminal1, clockUtc: Now.AddMinutes(1));
        Assert.Equal(Now.AddMinutes(1), await FirstServedAsync(version));
    }

    private static int[] DisplayIdsIn(byte[]? body)
    {
        using var json = System.Text.Json.JsonDocument.Parse(body ?? throw new InvalidOperationException("No body"));
        return json.RootElement.GetProperty("displays").EnumerateArray().Select(d => d.GetProperty("displayId").GetInt32()).ToArray();
    }

    private sealed record Response(int Status, byte[]? Body, string? ContentType, string? ETag);

    private async Task<Response> GetAsync(string scopes = ApiKeyScopes.MessagingRead, string? ifNoneMatch = null, DateTime? clockUtc = null,
        int? keyLocationId = null)
    {
        var http = new DefaultHttpContext();
        // What AdminApiKeyAuthHandler leaves on an authenticated key call.
        http.Items["ApiKeyPermissions"] = scopes;
        if (keyLocationId is not null) http.Items["ApiKeyLocationId"] = keyLocationId;
        if (ifNoneMatch is not null) http.Request.Headers.IfNoneMatch = ifNoneMatch;
        var controller = new MessagingDeliveryController(Delivery(clockUtc ?? Now))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        var result = await controller.Get(default);

        var etag = http.Response.Headers.ETag.ToString() is { Length: > 0 } header ? header : null;
        return result switch
        {
            FileContentResult file => new Response(StatusCodes.Status200OK, file.FileContents, file.ContentType, etag),
            ObjectResult o => new Response(o.StatusCode ?? StatusCodes.Status200OK, null, null, etag),
            StatusCodeResult s => new Response(s.StatusCode, null, null, etag),
            _ => throw new InvalidOperationException($"Unexpected result {result.GetType().Name}"),
        };
    }

    private async Task<int> LiveVersionAsync()
    {
        var message = await _db.AddMessageAsync(Paging);
        var activation = await _db.AddActivationAsync(message, Now.AddMinutes(-10), null, [GateA1]);
        return await _db.AddShownVersionAsync(activation, 1, Now.AddMinutes(-10), new Dictionary<string, string> { ["eng"] = "Gate change" });
    }

    private async Task<DateTime?> FirstServedAsync(int versionId)
    {
        await using var db = _db.Director();
        return await db.TblMessageActivationVersions.Where(v => v.MessageActivationVersionId == versionId).Select(v => v.FirstServedUtc).SingleAsync();
    }

    private MessageDelivery Delivery(DateTime clockUtc) =>
        new(_db.Factory, new MessageArbitration(Options.Create(new MessagingOptions())),
            new DisplayTimeZones(Options.Create(new MessagingOptions { DefaultTimeZone = "America/Toronto" }), NullLogger<DisplayTimeZones>.Instance),
            Options.Create(new D02GtaaMessagingOptions()), new FixedClock(clockUtc), NullLogger<MessageDelivery>.Instance);

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }
}
