using DXSuite.Director.Entities;
using DXSuite.Director.Models.CoreAPI;
using DXSuite.Director.Pages.Messages;
using DXSuite.Director.Services;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.CustomerDeviations;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static DXSuite.Director.Tests.MessageAccessDatabase;

namespace DXSuite.Director.Tests;

// Clause and gap ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// The "Schedule Message Activation" dialog (Diagram 36, ZONE-16) as a sender uses it from a Message Type's tab on the
/// Messages page, driven through <see cref="InteractiveRenderer"/> over a Director database on LocalDB.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessageSendDialogTests : IClassFixture<MessageAccessDatabase>, IAsyncLifetime
{
    private readonly MessageAccessDatabase _db;

    public MessageSendDialogTests(MessageAccessDatabase db) => _db = db;

    /// <summary>The User events of the page last rendered.</summary>
    private RecordingEventLog Events { get; set; } = new();

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [LocalDbFact]
    public async Task Display_Zones_mode_offers_only_covered_zones_and_counts_the_screens_they_reach()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);
        using var page = await RenderPageAsync(Ana);

        await page.ClickAsync($"#mp-schedule-{message}");
        Assert.Contains("Reaches 0 screens. Fixed when sent.", Dialog(page));
        await page.ChangeAsync($"#mse-zone-{GatesA}", true);

        var dialog = Dialog(page);
        Assert.Contains("Select Where to Show Your Message", dialog);
        Assert.DoesNotContain($"id=\"mse-zone-{CheckIn}\"", dialog);
        Assert.DoesNotContain($"id=\"mse-zone-{OldCheckIn}\"", dialog);
        Assert.Contains("Reaches 2 screens. Fixed when sent.", dialog);
        Assert.DoesNotContain("style=", dialog);
    }

    [LocalDbFact]
    public async Task One_display_mode_lists_only_displays_in_covered_zones_grouped_by_location()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);
        using var page = await RenderPageAsync(Ana);

        await page.ClickAsync($"#mp-schedule-{message}");
        await page.ClickAsync("#mse-mode-display");
        await page.ChangeAsync($"#mse-display-{GateA2}", true);

        var dialog = Dialog(page);
        Assert.Contains("Terminal 1 Gates", dialog);
        Assert.Contains($"id=\"mse-display-{GateA1}\"", dialog);
        Assert.DoesNotContain($"id=\"mse-display-{Lounge}\"", dialog);
        Assert.DoesNotContain($"id=\"mse-display-{CheckIn1}\"", dialog);
        Assert.DoesNotContain($"id=\"mse-zone-{GatesA}\"", dialog);
        Assert.Contains("Reaches 1 screen. Fixed when sent.", dialog);
    }

    [LocalDbFact]
    public async Task Sending_to_a_Display_Zone_confirms_the_screens_then_says_sent_and_the_list_shows_the_target()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);
        using var page = await RenderPageAsync(Ana);

        await page.ClickAsync($"#mp-schedule-{message}");
        await page.ChangeAsync($"#mse-zone-{GatesA}", true);
        await page.ClickAsync("#mse-next");

        var confirm = Dialog(page);
        Assert.Contains("Confirm screens", confirm);
        Assert.Contains($"id=\"mse-screen-{GateA1}\"", confirm);
        Assert.Contains($"id=\"mse-screen-{GateA2}\"", confirm);
        await using (var db = _db.Director())
            Assert.False(await db.TblMessageActivations.AnyAsync());

        await page.ClickAsync("#mse-send");
        Assert.Contains("Sent to 2 screens.", Dialog(page));
        await page.ClickAsync("#mse-done");

        Assert.DoesNotContain("mse-dialog", page.Html);
        Assert.Contains(">Target<", page.Html);
        Assert.Contains("Gates A", page.Html);
        Assert.Equal(UserEventOutcome.Succeeded, Events.Single(UserEventActionKeys.ActivationSent).Outcome);
    }

    [LocalDbFact]
    public async Task A_one_display_send_shows_that_display_as_its_target()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);
        using var page = await RenderPageAsync(Ana);

        await page.ClickAsync($"#mp-schedule-{message}");
        await page.ClickAsync("#mse-mode-display");
        await page.ChangeAsync($"#mse-display-{GateA2}", true);
        await page.ClickAsync("#mse-next");
        Assert.Contains("One display", Dialog(page));
        await page.ClickAsync("#mse-send");
        Assert.Contains("Sent to 1 screen.", Dialog(page));
        await page.ClickAsync("#mse-done");

        Assert.Contains("Gate A2", page.Html);
    }

    [LocalDbFact]
    public async Task Send_one_off_is_offered_only_under_a_rule_that_allows_one_off_text_and_sends_the_typed_text()
    {
        await _db.SetLanguagesAsync(Paging, "eng");
        await _db.AddRuleAsync(Paging, GatesA, canFreeText: false, userId: Ana);
        using (var without = await RenderPageAsync(Ana))
            Assert.DoesNotContain("id=\"mp-one-off\"", without.Html);

        await _db.AddRuleAsync(Paging, CheckIn, canFreeText: true, userId: Ana);
        using var page = await RenderPageAsync(Ana);
        await page.ClickAsync("#mp-one-off");

        Assert.Contains($"id=\"mse-zone-{CheckIn}\"", Dialog(page));
        Assert.DoesNotContain($"id=\"mse-zone-{GatesA}\"", Dialog(page));
        await page.InputAsync("#mse-text-eng", "Check-in closes early");
        await page.ChangeAsync($"#mse-zone-{CheckIn}", true);
        await page.ClickAsync("#mse-next");
        await page.ClickAsync("#mse-send");

        Assert.Contains("Sent to 1 screen.", Dialog(page));
        await using var db = _db.Director();
        var oneOff = await db.TblMessages.Include(m => m.Texts).SingleAsync();
        Assert.False(oneOff.IsCanned);
        Assert.Equal("Check-in closes early", Assert.Single(oneOff.Texts).Body);
    }

    [LocalDbFact]
    public async Task A_sender_with_no_rule_is_told_and_cannot_go_on()
    {
        var message = await _db.AddMessageAsync(Paging);
        using var page = await RenderPageAsync(Bo);

        await page.ClickAsync($"#mp-schedule-{message}");

        var dialog = Dialog(page);
        Assert.Contains("No Message access rule lets you send this type", dialog);
        Assert.DoesNotContain("mse-zone-", dialog);
        Assert.Contains("id=\"mse-next\" type=\"button\" class=\"dx-btn dx-btn-blue\" disabled", dialog);
    }

    [LocalDbFact]
    public async Task A_refused_send_keeps_the_dialog_open_with_the_reason()
    {
        var ruleId = await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);
        using var page = await RenderPageAsync(Ana);
        await page.ClickAsync($"#mp-schedule-{message}");
        await page.ChangeAsync($"#mse-zone-{GatesA}", true);
        await page.ClickAsync("#mse-next");

        await using (var db = _db.Director())
            await db.TblMessagePermissions.Where(p => p.MessagePermissionId == ruleId).ExecuteDeleteAsync();
        await page.ClickAsync("#mse-send");

        Assert.Contains("No Message access rule lets you send this type to Gates A.", Dialog(page));
        await using var check = _db.Director();
        Assert.False(await check.TblMessageActivations.AnyAsync());
    }

    // B-35 part 2: a scheduled send (MSG-16) and the priority override (MSG-09). Dates are years ahead because the
    // dialog runs on the real clock.

    [LocalDbFact]
    public async Task A_scheduled_send_with_a_weekly_window_confirms_each_screens_time_zone_and_saves_the_wall_clock_range()
    {
        await _db.AddDeviceAsync("van-1", GateA1, "America/Vancouver");
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);
        using var page = await RenderPageAsync(Ana);

        await page.ClickAsync($"#mp-schedule-{message}");
        await page.ChangeAsync($"#mse-zone-{GatesA}", true);
        await page.ChangeAsync("#mse-scheduled", true);
        await page.ChangeAsync("#mse-start", "2030-10-21T09:00");
        await page.ChangeAsync("#mse-end", "2030-10-26T17:00");
        await page.ChangeAsync("#mse-weekly", true);
        await page.ChangeAsync($"#mse-day-{(int)DayOfWeek.Saturday}", false);
        await page.ChangeAsync($"#mse-day-{(int)DayOfWeek.Sunday}", false);
        await page.ChangeAsync("#mse-daily-start", "08:00");
        await page.ChangeAsync("#mse-daily-end", "18:00");
        Assert.DoesNotContain("style=", Dialog(page));
        await page.ClickAsync("#mse-next");

        var confirm = Dialog(page);
        Assert.Contains("America/Vancouver", confirm);
        Assert.Contains("America/Toronto", confirm);
        Assert.Contains("Shows from Mon 21 Oct 2030 09:00 to Sat 26 Oct 2030 17:00, Mon Tue Wed Thu Fri, 08:00 to 18:00, local time on each screen.", confirm);
        await page.ClickAsync("#mse-send");

        Assert.Contains("Sent to 2 screens.", Dialog(page));
        await using var db = _db.Director();
        var activation = await db.TblMessageActivations.SingleAsync();
        Assert.Equal(new DateTime(2030, 10, 21, 9, 0, 0), activation.StartLocal);
        Assert.Equal(new DateTime(2030, 10, 26, 17, 0, 0), activation.EndLocal);
        Assert.Equal((new TimeOnly(8, 0), new TimeOnly(18, 0)), (activation.WindowStartTime!.Value, activation.WindowEndTime!.Value));
        Assert.False(activation.Saturday || activation.Sunday);
        Assert.True(activation.Monday && activation.Friday);
    }

    [LocalDbFact]
    public async Task A_schedule_ending_before_it_starts_is_refused_in_the_dialog()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);
        using var page = await RenderPageAsync(Ana);

        await page.ClickAsync($"#mp-schedule-{message}");
        await page.ChangeAsync($"#mse-zone-{GatesA}", true);
        await page.ChangeAsync("#mse-scheduled", true);
        await page.ChangeAsync("#mse-start", "2030-10-22T09:00");
        await page.ChangeAsync("#mse-end", "2030-10-21T09:00");
        await page.ClickAsync("#mse-next");

        var dialog = Dialog(page);
        Assert.Contains("The end must be after the start.", dialog);
        Assert.Contains("id=\"mse-next\"", dialog);
        await using var db = _db.Director();
        Assert.False(await db.TblMessageActivations.AnyAsync());
    }

    [LocalDbFact]
    public async Task The_priority_override_is_offered_only_to_holders_of_its_key()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        var message = await _db.AddMessageAsync(Paging);
        using (var without = await RenderPageAsync(Ana))
        {
            await without.ClickAsync($"#mp-schedule-{message}");
            Assert.DoesNotContain("mse-priority-override", Dialog(without));
        }

        using var page = await RenderPageAsync(Ana, PermissionKeys.MessagesPriorityOverride);
        await page.ClickAsync($"#mp-schedule-{message}");
        await page.ChangeAsync($"#mse-zone-{GatesA}", true);
        await page.ChangeAsync("#mse-priority-override", "-5");
        await page.ClickAsync("#mse-next");
        Assert.Contains("Priority override -5.", Dialog(page));
        await page.ClickAsync("#mse-send");

        await using var db = _db.Director();
        Assert.Equal(-5, (await db.TblMessageActivations.SingleAsync()).PriorityOverride);
    }

    private static string Dialog(InteractiveRenderer page)
    {
        var html = page.Html;
        var start = html.IndexOf("mse-dialog", StringComparison.Ordinal);
        Assert.True(start >= 0, "The send dialog is not open.");
        return html[html.LastIndexOf('<', start)..];
    }

    /// <summary>Renders Visual Paging's tab of the Messages page, signed in as <paramref name="userId"/> with page:messages.</summary>
    private async Task<InteractiveRenderer> RenderPageAsync(int userId, params string[] moreKeys)
    {
        var d02 = Options.Create(new D02GtaaMessagingOptions());
        var session = new KeySession([PermissionKeys.PageMessages, .. moreKeys]) { User = new TblUser { UserId = userId, RoleId = 3 } };
        var services = new ServiceCollection().AddLogging();
        var context = _db.Director();
        services.AddSingleton<IDirectorDBContext>(context);
        services.AddSingleton(_db.Factory);
        services.AddSingleton(d02);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISessionState>(session);
        services.AddScoped<IMessageLanguageCatalogue, MessageLanguageCatalogue>();
        services.AddScoped<IMessageSendAccess, MessageSendAccess>();
        services.AddScoped<MessageSending>();
        // A dialog send logs activation.sent as the sender (B-53).
        Events = new RecordingEventLog(userId);
        services.AddSingleton<IUserEventLog>(Events);
        services.Configure<MessagingOptions>(o => o.DefaultTimeZone = "America/Toronto");
        services.AddSingleton<DisplayTimeZones>();
        services.AddScoped<ShownVersions>();
        services.AddScoped<DXSuite.Director.Services.ContentSource.PendingFileDeletes>();
        services.AddScoped<ActiveMessages>();
        services.AddSingleton<Microsoft.JSInterop.IJSRuntime>(new TorontoBrowser());
        // The page also holds the authoring dialog; nothing here uploads, so its asset services are never called.
        services.AddSingleton((IAssetIngestService)ThrowingProxy.For(typeof(IAssetIngestService)));
        services.AddSingleton((ITenantMediaPathResolver)ThrowingProxy.For(typeof(ITenantMediaPathResolver)));
        services.AddScoped<MessageAuthoring>();
        var provider = services.BuildServiceProvider();

        var type = await context.TblMessageTypes.Include(t => t.Languages).SingleAsync(t => t.MessageTypeId == Paging);
        var languages = new List<TblLanguage> { new() { LanguageCode = "eng", LanguageName = "English" } };
        return await InteractiveRenderer.RenderAsync<MessagePage>(
            new Dictionary<string, object?> { ["MessageType"] = type, ["Languages"] = languages }, provider);
    }
}
