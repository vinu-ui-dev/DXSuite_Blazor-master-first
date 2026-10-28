using DXSuite.Director.Pages.Messages;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using static DXSuite.Director.Tests.MessageAccessDatabase;

namespace DXSuite.Director.Tests;

// Clause and gap ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// The fixed Active Messages tab of the Messages page (B-36: MSG-18, ZONE-16, G-38, G-39, G-64), driven through
/// <see cref="InteractiveRenderer"/> over a Director database on LocalDB, seen from a browser in Toronto.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ActiveMessagesTabTests : IClassFixture<MessageAccessDatabase>, IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);

    private readonly MessageAccessDatabase _db;

    public ActiveMessagesTabTests(MessageAccessDatabase db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [LocalDbFact]
    public async Task The_tab_lists_each_Activation_with_Message_Type_Target_Start_End_and_Status_in_local_time()
    {
        var canned = await _db.AddMessageAsync(Paging, name: "Gate change");
        var oneOff = await _db.AddOneOffAsync(Paging, "Boarding now");
        var active = await _db.AddActivationAsync(canned, Now.AddMinutes(-10), null, [GateA1, GateA2], GatesA, byUserId: Ana);
        var scheduled = await _db.AddActivationAsync(oneOff, Now.AddHours(1), Now.AddHours(2), [GateA2], byUserId: Bo);
        using var page = await RenderTabAsync(Ana);

        var html = page.Html;
        foreach (var column in new[] { "Message", "Type", "Target", "Start", "End", "Status", "Actions" })
            Assert.Contains($">{column}<", html);
        var activeRow = Row(html, active);
        Assert.Contains("Gate change", activeRow);
        Assert.Contains("Visual Paging", activeRow);
        Assert.Contains("Gates A", activeRow);
        Assert.Contains("2026-10-20 09:50", activeRow);
        Assert.Contains("Until cancelled", activeRow);
        Assert.Contains(">Active<", activeRow);
        Assert.Contains("Ana Agent", activeRow);
        var scheduledRow = Row(html, scheduled);
        Assert.Contains("Boarding now", scheduledRow);
        Assert.Contains("One-off", scheduledRow);
        Assert.Contains("Gate A2", scheduledRow);
        Assert.Contains("2026-10-20 11:00", scheduledRow);
        Assert.Contains("2026-10-20 12:00", scheduledRow);
        Assert.Contains(">Scheduled<", scheduledRow);
        Assert.DoesNotContain("Single Use", html);
        Assert.DoesNotContain("style=", html);
    }

    [LocalDbFact]
    public async Task Deactivate_asks_first_then_cancels_and_a_cancelled_one_off_stays_under_ended_and_cancelled()
    {
        var oneOff = await _db.AddOneOffAsync(Paging, "Boarding now");
        var activation = await _db.AddActivationAsync(oneOff, Now.AddMinutes(-10), null, [GateA2], byUserId: Bo);
        using var page = await RenderTabAsync(Ana);

        await page.ClickAsync($"#am-deactivate-{activation}");
        await using (var before = _db.Director())
            Assert.Null((await before.TblMessageActivations.SingleAsync()).CancelledUtc);
        await page.ClickAsync($"#am-confirm-{activation}");

        Assert.DoesNotContain($"am-row-{activation}", page.Html);
        Assert.Contains("No Messages are on screen or scheduled.", page.Html);
        await using (var db = _db.Director())
        {
            var row = await db.TblMessageActivations.Include(a => a.Message).SingleAsync();
            Assert.Equal((Now, (int?)Ana, false), (row.CancelledUtc!.Value, row.CancelledByUserId, row.Message.IsDeleted));
        }

        await page.ChangeAsync("#am-show-over", true);
        var shown = Row(page.Html, activation);
        Assert.Contains("Boarding now", shown);
        Assert.Contains(">Cancelled<", shown);
        Assert.DoesNotContain("am-deactivate-", shown);
    }

    [LocalDbFact]
    public async Task Keep_leaves_the_Activation_running()
    {
        var message = await _db.AddMessageAsync(Paging);
        var activation = await _db.AddActivationAsync(message, Now.AddMinutes(-10), null, [GateA1], GatesA);
        using var page = await RenderTabAsync(Ana);

        await page.ClickAsync($"#am-deactivate-{activation}");
        await page.ClickAsync($"#am-keep-{activation}");

        Assert.Contains($"id=\"am-deactivate-{activation}\"", page.Html);
        await using var db = _db.Director();
        Assert.Null((await db.TblMessageActivations.SingleAsync()).CancelledUtc);
    }

    private static string Row(string html, int activationId)
    {
        var start = html.IndexOf($"id=\"am-row-{activationId}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No row for Activation {activationId}.");
        var next = html.IndexOf("id=\"am-row-", start + 1, StringComparison.Ordinal);
        return next < 0 ? html[start..] : html[start..next];
    }

    private async Task<InteractiveRenderer> RenderTabAsync(int userId)
    {
        var session = new KeySession(PermissionKeys.PageMessages) { User = new TblUser { UserId = userId, RoleId = 3 } };
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(_db.Factory);
        services.AddSingleton<TimeProvider>(new FixedClock(Now));
        services.AddSingleton<ISessionState>(session);
        services.AddSingleton<IJSRuntime>(new TorontoBrowser());
        services.AddSingleton<IUserEventLog>(new RecordingEventLog(userId));
        services.Configure<MessagingOptions>(o => o.DefaultTimeZone = "America/Toronto");
        services.AddSingleton<DisplayTimeZones>();
        services.AddScoped<ActiveMessages>();
        return await InteractiveRenderer.RenderAsync<ActiveMessagePage>(new Dictionary<string, object?>(), services.BuildServiceProvider());
    }

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }
}
