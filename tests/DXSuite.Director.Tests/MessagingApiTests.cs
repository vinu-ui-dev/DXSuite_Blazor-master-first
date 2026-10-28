using System.Net;
using System.Security.Claims;
using System.Text.Json;
using DXSuite.Director.Controllers;
using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.CustomerDeviations;
using DXSuite.Shared.Entities.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static DXSuite.Director.Tests.MessageAccessDatabase;

namespace DXSuite.Director.Tests;

// Clause ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// The API trigger (B-38, MSG-19): an external system sends an Approved canned Message by API key through
/// <c>POST /api/messaging/activations</c>, over a Director database on LocalDB. The key's scheme, the internal networks
/// refusal (HOST-03) and the tenant gate (DEV-06) sit in front of the action; see <see cref="TenantFeatureRouteGateTests"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessagingApiTests : IClassFixture<MessageAccessDatabase>, IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);
    private const string KeyName = "Gate system";
    private const string CallerIp = "10.4.0.20";

    private readonly MessageAccessDatabase _db;
    private readonly RecordingLog _log = new();

    public MessagingApiTests(MessageAccessDatabase db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [LocalDbFact]
    public async Task A_canned_Message_sent_by_key_to_a_Display_Zone_is_an_Api_Activation_with_frozen_screen_rows()
    {
        var message = await _db.AddMessageAsync(Paging);

        var response = await CallAsync(new MessageApiSendRequest { MessageId = message, ZoneIds = [GatesA], Minutes = 15 });

        Assert.Equal(StatusCodes.Status201Created, response.Status);
        var sent = Assert.IsType<MessageApiSendResponse>(response.Body);
        await using var db = _db.Director();
        var activation = await db.TblMessageActivations.Include(a => a.Targets).SingleAsync();
        Assert.Equal(activation.MessageActivationId, sent.ActivationId);
        Assert.Equal(2, sent.ScreenCount);
        Assert.Equal(message, activation.MessageId);
        Assert.Equal(MessageActivationSource.Api, activation.Source);
        Assert.Null(activation.ActivatedByUserId);
        Assert.Equal(Now, activation.StartUtc);
        Assert.Equal(Now.AddMinutes(15), activation.EndUtc);
        Assert.Equal(10, activation.DisplaySecondsOverride);
        Assert.Equal(30, activation.HideSecondsOverride);
        Assert.Equal([(GateA1, "Gate A1", "Terminal 1 Gates", (int?)GatesA), (GateA2, "Gate A2", "Terminal 1 Gates", GatesA)],
            activation.Targets.OrderBy(t => t.DisplayId).Select(t => (t.DisplayId!.Value, t.DisplayName, t.LocationName, t.ZoneId)));
    }

    // AUD-06, MSG-15: a send by key records its first shown version in the send's transaction, as the dialog does.
    [LocalDbFact]
    public async Task A_send_by_key_writes_shown_version_1_sent_at_the_call()
    {
        var message = await _db.AddMessageAsync(Paging, name: "Boarding moved");

        var response = await CallAsync(new MessageApiSendRequest { MessageId = message, ZoneIds = [GatesA] });

        var sent = Assert.IsType<MessageApiSendResponse>(response.Body);
        await using var db = _db.Director();
        var version = await db.TblMessageActivationVersions.SingleAsync();
        Assert.Equal((sent.ActivationId, 1, ShownVersionReason.FirstSend), (version.MessageActivationId, version.Sequence, version.Reason));
        Assert.Equal(("VISUAL_PAGING", 40), (version.MessageTypeCode, version.EffectiveRank));
        Assert.Equal("""{"eng":"Boarding moved"}""", version.Texts);
        Assert.Equal((Now, (DateTime?)null), (version.SentUtc, version.FirstServedUtc));
    }

    // INK-09: a send by key moves the delivery version with the Activation; a refused one leaves it.
    [LocalDbFact]
    public async Task A_send_by_key_moves_the_delivery_version_and_a_refused_one_does_not()
    {
        var message = await _db.AddMessageAsync(Paging);
        var pending = await _db.AddMessageAsync(Paging, MessageStatus.Pending);
        var before = await DeliveryVersionProbe.ReadAsync(_db.Director);

        Assert.IsType<MessageApiSendResponse>((await CallAsync(new MessageApiSendRequest { MessageId = message, ZoneIds = [GatesA] })).Body);
        var afterSend = await DeliveryVersionProbe.ReadAsync(_db.Director);
        await CallAsync(new MessageApiSendRequest { MessageId = pending, ZoneIds = [GatesA] });

        Assert.NotEqual(before, afterSend);
        Assert.Equal(afterSend, await DeliveryVersionProbe.ReadAsync(_db.Director));
    }

    // ZONE-06: one send path. Message access rules belong to accounts, so the key's scope is the API's only gate.
    [LocalDbFact]
    public async Task A_send_by_key_freezes_the_same_screen_rows_as_the_send_dialog_and_needs_no_Message_access_rule()
    {
        await _db.AddRuleAsync(Paging, null, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);
        var dialog = new MessageSending(_db.Factory, new MessageSendAccess(_db.Factory),
            new MessageLanguageCatalogue(Options.Create(new D02GtaaMessagingOptions())),
            new KeySession(PermissionKeys.PageMessages) { User = new TblUser { UserId = Ana, RoleId = 3 } },
            Options.Create(new D02GtaaMessagingOptions()), new FixedClock(Now), TimeZones(), NoAssetVersions(), _log, NullLogger<MessageSending>.Instance);
        var byDialog = await dialog.SendAsync(new MessageSendRequest
        {
            MessageTypeId = Paging, MessageId = message, Target = MessageSendTarget.DisplayZones(GatesA, CheckIn), When = new MessageSendTiming.SendNow(null),
        }, null, default);

        var byKey = await CallAsync(new MessageApiSendRequest { MessageId = message, ZoneIds = [GatesA, CheckIn] });

        Assert.Equal(StatusCodes.Status201Created, byKey.Status);
        await using var db = _db.Director();
        var rows = await db.TblMessageActivationTargets
            .Select(t => new { t.MessageActivationId, t.DisplayId, t.ZoneId, t.DisplayName, t.LocationName })
            .ToListAsync();
        var dialogRows = rows.Where(r => r.MessageActivationId == byDialog.MessageActivationId).Select(r => (r.DisplayId, r.ZoneId, r.DisplayName, r.LocationName));
        var keyRows = rows.Where(r => r.MessageActivationId == ((MessageApiSendResponse)byKey.Body!).ActivationId).Select(r => (r.DisplayId, r.ZoneId, r.DisplayName, r.LocationName));
        Assert.Equal(3, keyRows.Count());
        Assert.Equal(dialogRows.OrderBy(r => r.DisplayId), keyRows.OrderBy(r => r.DisplayId));
    }

    // A-26, AUD-08.
    [LocalDbFact]
    public async Task A_send_by_key_logs_activation_sent_with_System_as_the_actor_named_by_the_key()
    {
        var message = await _db.AddMessageAsync(Paging, name: "Boarding moved");

        var response = await CallAsync(new MessageApiSendRequest { MessageId = message, ZoneIds = [GatesA], Minutes = 15 });

        var (actor, logged) = Assert.Single(_log.Written);
        Assert.Equal(UserEventActorKind.System, actor.Kind);
        Assert.Equal(KeyName, actor.ActorName);
        Assert.Null(actor.UserId);
        Assert.Equal(CallerIp, actor.ClientIp);
        Assert.Equal(UserEventActionKeys.ActivationSent, logged.ActionKey);
        Assert.Equal(new UserEventTarget(UserEventTargetTypes.Activation, ((MessageApiSendResponse)response.Body!).ActivationId, "Boarding moved"),
            logged.Target);
        Assert.Equal(UserEventOutcome.Succeeded, logged.Outcome);
        Assert.Equal(
            $$"""{"source":"Api","oneOff":false,"messageId":{{message}},"mode":"zones","zoneIds":[{{GatesA}}],"displayCount":2,"startUtc":"2026-10-20T14:00:00Z","endUtc":"2026-10-20T14:15:00Z","weeklyWindow":null,"priorityOverride":null}""",
            JsonSerializer.Serialize(logged.Details, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [LocalDbFact]
    public async Task A_send_is_kept_when_its_event_cannot_be_written()
    {
        var message = await _db.AddMessageAsync(Paging);
        _log.Fail = true;

        var response = await CallAsync(new MessageApiSendRequest { MessageId = message, ZoneIds = [GatesA] });

        Assert.Equal(StatusCodes.Status201Created, response.Status);
        await using var db = _db.Director();
        Assert.True(await db.TblMessageActivations.AnyAsync());
    }

    // MSG-19, ZONE-05: one display, over a start and an end read on the screen's own clock as the dialog reads them.
    [LocalDbFact]
    public async Task A_send_by_key_to_one_display_over_a_start_and_end_is_scheduled_in_the_screens_time_zone()
    {
        var message = await _db.AddMessageAsync(Paging);
        await _db.AddDeviceAsync("dev-lounge", Lounge, "America/Vancouver");
        var start = new DateTime(2026, 10, 21, 8, 0, 0);
        var end = new DateTime(2026, 10, 21, 12, 0, 0);

        var response = await CallAsync(new MessageApiSendRequest { MessageId = message, DisplayId = Lounge, Start = start, End = end });

        Assert.Equal(StatusCodes.Status201Created, response.Status);
        Assert.Equal([Lounge], ((MessageApiSendResponse)response.Body!).DisplayIds);
        await using var db = _db.Director();
        var activation = await db.TblMessageActivations.Include(a => a.Targets).SingleAsync();
        Assert.Equal(MessageActivationSource.Api, activation.Source);
        Assert.Equal(start, activation.StartLocal);
        Assert.Equal(end, activation.EndLocal);
        // Vancouver is UTC-7 in October.
        Assert.Equal(new DateTime(2026, 10, 21, 15, 0, 0), activation.StartUtc);
        Assert.Equal(new DateTime(2026, 10, 21, 19, 0, 0), activation.EndUtc);
        var target = Assert.Single(activation.Targets);
        Assert.Equal((Lounge, (int?)null, "Lounge"), (target.DisplayId!.Value, target.ZoneId, target.DisplayName));
    }

    // MSG-12, MSG-19.
    [LocalDbFact]
    public async Task A_Message_that_is_not_Approved_is_refused_and_nothing_is_saved_or_logged()
    {
        var draft = await _db.AddMessageAsync(Paging, MessageStatus.Draft);

        var response = await CallAsync(new MessageApiSendRequest { MessageId = draft, ZoneIds = [GatesA] });

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, response.Status);
        Assert.Contains("Approved", Message(response));
        await AssertNothingSentAsync();
    }

    // MSG-19: a one-off Message is typed in the dialog and never sent again by key.
    [LocalDbFact]
    public async Task A_one_off_Message_is_refused()
    {
        int oneOff;
        await using (var db = _db.Director())
        {
            var message = new TblMessage
            {
                MessageTypeId = Paging, Name = "Typed once", IsCanned = false, Status = MessageStatus.Approved, CreatedUtc = Now,
                Texts = [new TblMessageText { LanguageCode = "eng", Body = "Typed once" }],
            };
            db.TblMessages.Add(message);
            await db.SaveChangesAsync();
            oneOff = message.MessageId;
        }

        var response = await CallAsync(new MessageApiSendRequest { MessageId = oneOff, ZoneIds = [GatesA] });

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, response.Status);
        await AssertNothingSentAsync();
    }

    // MSG-19: free text is never accepted, under whatever name it arrives.
    [LocalDbFact]
    public async Task A_request_carrying_free_text_is_refused()
    {
        var message = await _db.AddMessageAsync(Paging);
        var request = JsonSerializer.Deserialize<MessageApiSendRequest>(
            $$$"""{"messageId":{{{message}}},"zoneIds":[{{{GatesA}}}],"texts":{"eng":"Gate closed"}}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var response = await CallAsync(request);

        Assert.Equal(StatusCodes.Status400BadRequest, response.Status);
        Assert.Contains("texts", Message(response));
        await AssertNothingSentAsync();
    }

    [LocalDbFact]
    public async Task A_key_without_messaging_send_is_refused()
    {
        var message = await _db.AddMessageAsync(Paging);

        var response = await CallAsync(new MessageApiSendRequest { MessageId = message, ZoneIds = [GatesA] }, ApiKeyScopes.OverrideTrigger);

        Assert.Equal(StatusCodes.Status403Forbidden, response.Status);
        await AssertNothingSentAsync();
    }

    public static TheoryData<string, MessageApiSendRequest> MalformedRequests => new()
    {
        { "no Message", new MessageApiSendRequest { ZoneIds = [GatesA] } },
        { "no target", new MessageApiSendRequest { MessageId = 1 } },
        { "zones and a display", new MessageApiSendRequest { MessageId = 1, ZoneIds = [GatesA], DisplayId = Lounge } },
        { "start without end", new MessageApiSendRequest { MessageId = 1, ZoneIds = [GatesA], Start = new DateTime(2026, 10, 21, 8, 0, 0) } },
        { "minutes and a range", new MessageApiSendRequest
            { MessageId = 1, ZoneIds = [GatesA], Minutes = 5, Start = new DateTime(2026, 10, 21, 8, 0, 0), End = new DateTime(2026, 10, 21, 9, 0, 0) } },
        { "an instant, not a local time", new MessageApiSendRequest
            { MessageId = 1, ZoneIds = [GatesA], Start = new DateTime(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc), End = new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc) } },
        { "no minutes", new MessageApiSendRequest { MessageId = 1, ZoneIds = [GatesA], Minutes = 0 } },
        { "end before start", new MessageApiSendRequest
            { MessageId = 1, ZoneIds = [GatesA], Start = new DateTime(2026, 10, 21, 9, 0, 0), End = new DateTime(2026, 10, 21, 8, 0, 0) } },
    };

    [LocalDbTheory]
    [MemberData(nameof(MalformedRequests))]
    public async Task A_malformed_request_is_refused(string why, MessageApiSendRequest request)
    {
        _ = why;
        var message = await _db.AddMessageAsync(Paging);
        if (request.MessageId is not null) request.MessageId = message;

        var response = await CallAsync(request);

        Assert.Equal(StatusCodes.Status400BadRequest, response.Status);
        await AssertNothingSentAsync();
    }

    [Fact]
    public void Messaging_send_is_a_scope_an_API_key_can_be_given()
    {
        Assert.Contains(ApiKeyScopes.MessagingSend, ApiKeyScopes.UserSelectableScopes);
        Assert.Equal("messaging:send", ApiKeyScopes.MessagingSend);
    }

    private async Task AssertNothingSentAsync()
    {
        await using var db = _db.Director();
        Assert.False(await db.TblMessageActivations.AnyAsync());
        Assert.Empty(_log.Written);
    }

    private static string Message(Response response) =>
        JsonSerializer.SerializeToElement(response.Body).GetProperty("message").GetString() ?? "";

    private sealed record Response(int Status, object? Body);

    private async Task<Response> CallAsync(MessageApiSendRequest request, string scopes = ApiKeyScopes.MessagingSend)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(CallerIp);
        // What AdminApiKeyAuthHandler leaves on an authenticated key call.
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("ApiKeyName", KeyName), .. scopes.Split(' ').Select(s => new Claim("scope", s))], "AdminApiKey"));
        http.Items["ApiKeyName"] = KeyName;
        http.Items["ApiKeyPermissions"] = scopes;

        var controller = new MessagingApiController(Sending())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        var result = await controller.Send(request, default);
        return result switch
        {
            ObjectResult o => new Response(o.StatusCode ?? StatusCodes.Status200OK, o.Value),
            StatusCodeResult s => new Response(s.StatusCode, null),
            _ => throw new InvalidOperationException($"Unexpected result {result.GetType().Name}"),
        };
    }

    // No account is signed in on an API call: the session is empty.
    private MessageSending Sending()
    {
        var session = new KeySession();
        var d02 = Options.Create(new D02GtaaMessagingOptions { Enabled = false });
        return new MessageSending(_db.Factory, new MessageSendAccess(_db.Factory), new MessageLanguageCatalogue(d02), session, d02,
            new FixedClock(Now), TimeZones(), NoAssetVersions(), _log, NullLogger<MessageSending>.Instance);
    }

    private static DisplayTimeZones TimeZones() =>
        new(Options.Create(new MessagingOptions { DefaultTimeZone = "America/Toronto" }), NullLogger<DisplayTimeZones>.Instance);

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }

    internal sealed class RecordingLog : IUserEventLog
    {
        public List<(UserEventActor Actor, UserEvent Event)> Written { get; } = new();

        public bool Fail { get; set; }

        public Task WriteAsync(UserEventActor actor, UserEvent userEvent, CancellationToken ct = default)
        {
            if (Fail) throw new InvalidOperationException("The log is unavailable.");
            Written.Add((actor, userEvent));
            return Task.CompletedTask;
        }

        public Task WriteAsync(UserEvent userEvent, CancellationToken ct = default) =>
            throw new InvalidOperationException("No account is signed in.");

        public Task<UserEventActor> CurrentAccountAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("No account is signed in.");

        public Task<UserEventActor> AccountAsync(int userId, CancellationToken ct = default) =>
            Task.FromResult(new UserEventActor { Kind = UserEventActorKind.Account, UserId = userId, ActorName = $"user {userId}" });
    }
}
