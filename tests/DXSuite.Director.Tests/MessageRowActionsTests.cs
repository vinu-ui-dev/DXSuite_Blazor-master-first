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
/// Deactivate, Retire and Delete on a Message Type's tab of the Messages page (B-36: MSG-14, MSG-18, G-38, G-64),
/// driven through <see cref="InteractiveRenderer"/> over a Director database on LocalDB.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessageRowActionsTests : IClassFixture<MessageAccessDatabase>, IAsyncLifetime
{
    private readonly MessageAccessDatabase _db;

    public MessageRowActionsTests(MessageAccessDatabase db) => _db = db;

    /// <summary>The User events of the page last rendered.</summary>
    private RecordingEventLog Events { get; set; } = new();

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static DateTime Now => DateTime.UtcNow;

    [LocalDbFact]
    public async Task Deactivate_on_a_Message_lists_its_running_Activations_and_cancels_them_all_keeping_the_Message()
    {
        var message = await _db.AddMessageAsync(Paging);
        var onScreen = await _db.AddActivationAsync(message, Now.AddMinutes(-10), null, [GateA1, GateA2], GatesA);
        var scheduled = await _db.AddActivationAsync(message, Now.AddHours(1), Now.AddHours(2), [GateA2]);
        var ended = await _db.AddActivationAsync(message, Now.AddHours(-3), Now.AddHours(-2), [GateA1], GatesA);
        using var page = await RenderPageAsync(Ana);

        await page.ClickAsync($"#mp-deactivate-{message}");
        var dialog = page.Html;
        Assert.Contains($"id=\"mam-row-{onScreen}\"", dialog);
        Assert.Contains($"id=\"mam-row-{scheduled}\"", dialog);
        Assert.DoesNotContain($"id=\"mam-row-{ended}\"", dialog);
        Assert.Contains("Gates A", dialog);
        Assert.DoesNotContain("Deactivating Will Delete Message", dialog);
        await page.ClickAsync("#mam-deactivate-all");

        Assert.DoesNotContain("mam-dialog", page.Html);
        Assert.DoesNotContain($"id=\"mp-deactivate-{message}\"", page.Html);
        await using var db = _db.Director();
        var rows = await db.TblMessageActivations.OrderBy(a => a.MessageActivationId).ToListAsync();
        Assert.Equal([(true, (int?)Ana), (true, Ana), (false, null)],
            rows.Select(a => (a.CancelledUtc is not null, a.CancelledByUserId)));
        Assert.False((await db.TblMessages.SingleAsync()).IsDeleted);
        Assert.Equal([onScreen.ToString(), scheduled.ToString()],
            Events.Of(UserEventActionKeys.ActivationCancelled).Select(e => e.Target!.Id).Order());
    }

    [LocalDbFact]
    public async Task One_Activation_can_be_deactivated_from_the_list_and_the_others_keep_running()
    {
        var message = await _db.AddMessageAsync(Paging);
        var first = await _db.AddActivationAsync(message, Now.AddMinutes(-10), null, [GateA1], GatesA);
        var second = await _db.AddActivationAsync(message, Now.AddMinutes(-5), null, [CheckIn1], CheckIn);
        using var page = await RenderPageAsync(Ana);

        await page.ClickAsync($"#mp-deactivate-{message}");
        await page.ClickAsync($"#mam-deactivate-{first}");

        Assert.DoesNotContain($"id=\"mam-row-{first}\"", page.Html);
        Assert.Contains($"id=\"mam-row-{second}\"", page.Html);
        await using var db = _db.Director();
        Assert.Equal([true, false], await db.TblMessageActivations.OrderBy(a => a.MessageActivationId)
            .Select(a => a.CancelledUtc != null).ToListAsync());
    }

    [LocalDbFact]
    public async Task One_off_Messages_are_not_listed_on_the_type_tab()
    {
        await _db.AddMessageAsync(Paging, name: "Gate change");
        var oneOff = await _db.AddOneOffAsync(Paging, "Boarding now");
        await _db.AddActivationAsync(oneOff, Now.AddMinutes(-10), null, [GateA2]);
        using var page = await RenderPageAsync(Ana);

        Assert.Contains("Gate change", page.Html);
        Assert.DoesNotContain("Boarding now", page.Html);
        Assert.DoesNotContain("Single Use", page.Html);
    }

    [LocalDbFact]
    public async Task Retire_and_Delete_are_refused_with_a_message_while_an_Activation_is_active_or_scheduled()
    {
        var message = await _db.AddMessageAsync(Paging);
        await _db.AddActivationAsync(message, Now.AddHours(1), null, [GateA1], GatesA);
        using var page = await RenderPageAsync(Ana, author: true);

        await page.ClickAsync($"#mp-delete-{message}");
        await page.ClickAsync($"#mp-confirm-delete-{message}");
        Assert.Contains(MessageAuthoring.CancelFirst, page.Html);
        await page.ClickAsync($"#mp-retire-{message}");
        await page.ClickAsync($"#mp-confirm-retire-{message}");
        Assert.Contains(MessageAuthoring.CancelFirst, page.Html);

        await using var db = _db.Director();
        var row = await db.TblMessages.SingleAsync();
        Assert.Equal((false, MessageStatus.Approved), (row.IsDeleted, row.Status));
    }

    [LocalDbFact]
    public async Task A_Message_whose_Activations_are_over_retires_then_deletes_and_only_authors_see_either()
    {
        var message = await _db.AddMessageAsync(Paging, name: "Gate change");
        await _db.AddActivationAsync(message, Now.AddHours(-3), Now.AddHours(-2), [GateA1], GatesA);
        using (var reader = await RenderPageAsync(Ana))
        {
            Assert.DoesNotContain($"id=\"mp-delete-{message}\"", reader.Html);
            Assert.DoesNotContain($"id=\"mp-retire-{message}\"", reader.Html);
        }
        using var page = await RenderPageAsync(Ana, author: true);

        await page.ClickAsync($"#mp-retire-{message}");
        await page.ClickAsync($"#mp-confirm-retire-{message}");
        Assert.Contains("Retired", page.Html);
        Assert.DoesNotContain($"id=\"mp-schedule-{message}\"", page.Html);
        Assert.DoesNotContain($"id=\"mp-edit-{message}\"", page.Html);
        await page.ClickAsync($"#mp-delete-{message}");
        await page.ClickAsync($"#mp-confirm-delete-{message}");

        Assert.DoesNotContain("Gate change", page.Html);
        Assert.DoesNotContain("style=", page.Html);
        await using var db = _db.Director();
        var row = await db.TblMessages.SingleAsync();
        Assert.Equal((true, MessageStatus.Retired, (int?)Ana), (row.IsDeleted, row.Status, row.ModifiedByUserId));
    }

    // MSG-03: a type has any number of languages, in its own order, English or not.
    [LocalDbTheory]
    [InlineData("fra")]
    [InlineData("fra,eng,spa")]
    public async Task The_list_shows_one_text_column_per_language_of_the_type(string languages)
    {
        var codes = languages.Split(',');
        var message = await _db.AddMessageAsync(Paging);
        await using (var db = _db.Director())
        {
            await db.TblMessageTexts.Where(t => t.MessageId == message).ExecuteDeleteAsync();
            db.TblMessageTexts.AddRange(codes.Select(c => new TblMessageText { MessageId = message, LanguageCode = c, Body = $"Text in {c}" }));
            await db.SaveChangesAsync();
        }

        using var page = await RenderPageAsync(Ana, languages: codes);

        foreach (var code in codes)
        {
            Assert.Contains($"{Name(code)} Message", page.Html);
            Assert.Matches($"id=\"mp-text-{message}-{code}\"[^>]*>Text in {code}</div>", page.Html);
        }
        Assert.Equal(codes.Length, page.Html.Split(">Text in ").Length - 1);
    }

    private static string Name(string code) => code switch { "eng" => "English", "fra" => "French", _ => "Spanish" };

    /// <summary>
    /// Renders Visual Paging's tab of the Messages page, signed in as <paramref name="userId"/> with page:messages, with
    /// the type's languages as the Messages page passes them (English unless given).
    /// </summary>
    private async Task<InteractiveRenderer> RenderPageAsync(int userId, bool author = false, string[]? languages = null)
    {
        var d02 = Options.Create(new D02GtaaMessagingOptions());
        string[] keys = author ? [PermissionKeys.PageMessages, PermissionKeys.MessagesAuthor] : [PermissionKeys.PageMessages];
        var session = new KeySession(keys) { User = new TblUser { UserId = userId, RoleId = 3 } };
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
        // Deactivate, Retire and Delete log their events as the user (B-53).
        Events = new RecordingEventLog(userId);
        services.AddSingleton<IUserEventLog>(Events);
        services.Configure<MessagingOptions>(o => o.DefaultTimeZone = "America/Toronto");
        services.AddSingleton<DisplayTimeZones>();
        services.AddScoped<ShownVersions>();
        services.AddScoped<DXSuite.Director.Services.ContentSource.PendingFileDeletes>();
        services.AddScoped<ActiveMessages>();
        services.AddSingleton<Microsoft.JSInterop.IJSRuntime>(new TorontoBrowser());
        // Retire and delete touch no asset, so the authoring service's asset services are never called.
        services.AddSingleton((IAssetIngestService)ThrowingProxy.For(typeof(IAssetIngestService)));
        services.AddSingleton((ITenantMediaPathResolver)ThrowingProxy.For(typeof(ITenantMediaPathResolver)));
        services.AddScoped<MessageAuthoring>();
        var provider = services.BuildServiceProvider();

        var type = await context.TblMessageTypes.Include(t => t.Languages).SingleAsync(t => t.MessageTypeId == Paging);
        var typeLanguages = (languages ?? ["eng"]).Select(c => new TblLanguage { LanguageCode = c, LanguageName = Name(c) }).ToList();
        return await InteractiveRenderer.RenderAsync<MessagePage>(
            new Dictionary<string, object?> { ["MessageType"] = type, ["Languages"] = typeLanguages }, provider);
    }
}
