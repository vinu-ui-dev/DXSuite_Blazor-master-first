using DXSuite.Director.Entities;
using DXSuite.Director.Models.CoreAPI;
using DXSuite.Director.Pages.Messages;
using DXSuite.Director.Services;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.CustomerDeviations;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Tests;

// Clause and gap ids in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// The Message authoring dialog as an author uses it from a Message Type's tab on the Messages page (B-32), driven
/// through <see cref="InteractiveRenderer"/> over a Director database on LocalDB.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessageAuthoringDialogTests : IClassFixture<MessageTypeDatabase>, IAsyncLifetime
{
    private readonly MessageTypeDatabase _db;
    private readonly AuthoringWorld _world;

    public MessageAuthoringDialogTests(MessageTypeDatabase db)
    {
        _db = db;
        _world = new AuthoringWorld(db);
    }

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    [LocalDbFact]
    public async Task Only_a_holder_of_messages_author_is_offered_Add_Message_and_Edit()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        var message = await _db.AddMessageAsync(type, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-1));

        using (var reader = await RenderPageAsync(type, PermissionKeys.PageMessages))
        {
            Assert.DoesNotContain("id=\"mp-add\"", reader.Html);
            Assert.DoesNotContain($"id=\"mp-edit-{message}\"", reader.Html);
        }

        using var author = await RenderPageAsync(type, PermissionKeys.PageMessages, PermissionKeys.MessagesAuthor);
        Assert.Contains("id=\"mp-add\"", author.Html);
        Assert.Contains($"id=\"mp-edit-{message}\"", author.Html);
    }

    [LocalDbFact]
    public async Task The_dialog_asks_for_a_name_and_one_text_per_language_each_with_its_own_counter()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1, top: false, deleted: false, "eng", "fra");
        await _db.SetMaxCharactersAsync(type, 120);
        using var page = await RenderPageAsync(type, PermissionKeys.MessagesAuthor);

        await page.ClickAsync("#mp-add");
        await page.InputAsync("#mm-text-fra", "Bonjour");

        var dialog = DialogHtml(page);
        Assert.Contains("id=\"mm-name\"", dialog);
        Assert.Contains("English", dialog);
        Assert.Contains("French", dialog);
        Assert.Contains("0 / 120", dialog);
        Assert.Contains("7 / 120", dialog);
        Assert.DoesNotContain("Single Use", dialog);
        Assert.DoesNotContain("style=", dialog);
    }

    [LocalDbFact]
    public async Task Saving_a_new_Message_on_a_type_with_a_chain_lists_it_as_Pending()
    {
        var type = await _db.AddTypeAsync("EMERGENCY", priority: 1);
        await _db.AddTypeChainAsync(type);
        using var page = await RenderPageAsync(type, PermissionKeys.MessagesAuthor);

        await page.ClickAsync("#mp-add");
        Assert.Contains("Saves as Pending", DialogHtml(page));
        await page.InputAsync("#mm-name", "Evacuate");
        await page.InputAsync("#mm-text-eng", "Leave the building");
        await page.ClickAsync("#mm-save");

        Assert.DoesNotContain("mm-dialog", page.Html);
        Assert.Contains("Leave the building", page.Html);
        Assert.Contains("Pending approval", page.Html);
        await using var db = _db.Director();
        Assert.Equal(("Evacuate", MessageStatus.Pending), await db.TblMessages.Select(m => ValueTuple.Create(m.Name, m.Status)).SingleAsync());
    }

    [LocalDbFact]
    public async Task Save_shows_what_is_missing_and_keeps_the_dialog_open()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        using var page = await RenderPageAsync(type, PermissionKeys.MessagesAuthor);

        await page.ClickAsync("#mp-add");
        await page.ClickAsync("#mm-save");

        var dialog = DialogHtml(page);
        Assert.Contains("Name the Message.", dialog);
        Assert.Contains("Type the Message in this language.", dialog);
        await using var db = _db.Director();
        Assert.False(await db.TblMessages.AnyAsync());
    }

    [LocalDbFact]
    public async Task Cancel_discards_every_change_to_a_Message()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        var message = await _db.AddMessageAsync(type, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-1));
        await _db.AddTextAsync(message, "eng", "Original");
        using var page = await RenderPageAsync(type, PermissionKeys.MessagesAuthor);

        await page.ClickAsync($"#mp-edit-{message}");
        Assert.Contains("Original", DialogHtml(page));
        await page.InputAsync("#mm-name", "Renamed");
        await page.InputAsync("#mm-text-eng", "Changed");
        await page.ClickAsync("#mm-cancel");

        Assert.DoesNotContain("mm-dialog", page.Html);
        await using var db = _db.Director();
        var saved = await db.TblMessages.Include(m => m.Texts).SingleAsync();
        Assert.Equal(("Message", "Original"), (saved.Name, saved.Texts.Single().Body));
        Assert.Contains("Original", page.Html);
        Assert.DoesNotContain("Changed", page.Html);
    }

    [LocalDbFact]
    public async Task Cancel_after_an_upload_leaves_nothing_staged_and_nothing_in_the_library()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        var constraint = await _db.AddLibraryConstraintAsync("ASL videos");
        await _db.AddSlotAsync(type, "ASL", constraint, required: true);
        using var page = await RenderPageAsync(type, PermissionKeys.MessagesAuthor);

        await page.ClickAsync("#mp-add");
        Assert.Contains("Required", DialogHtml(page));
        var dialog = page.Find<ManageMessageModal>();
        await page.InvokeAsync(() => dialog.StageAsync("ASL", new StagedUpload(_world.UploadedFile("sign.mp4"), "sign.mp4")));
        Assert.Contains("sign.mp4", DialogHtml(page));
        await page.ClickAsync("#mm-cancel");

        Assert.Empty(_world.StagedFiles());
        Assert.Empty(_world.Library.Ingested);
        await using var db = _db.Director();
        Assert.False(await db.TblAssets.AnyAsync());
    }

    [LocalDbFact]
    public async Task An_upload_saved_with_the_Message_fills_its_slot()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        var constraint = await _db.AddLibraryConstraintAsync("ASL videos");
        await _db.AddSlotAsync(type, "ASL", constraint, required: true);
        using var page = await RenderPageAsync(type, PermissionKeys.MessagesAuthor);

        await page.ClickAsync("#mp-add");
        await page.InputAsync("#mm-name", "Gate change");
        await page.InputAsync("#mm-text-eng", "Gate changed");
        var dialog = page.Find<ManageMessageModal>();
        await page.InvokeAsync(() => dialog.StageAsync("ASL", new StagedUpload(_world.UploadedFile("sign.mp4"), "sign.mp4")));
        await page.ClickAsync("#mm-save");

        Assert.DoesNotContain("mm-dialog", page.Html);
        Assert.Single(_world.Library.Ingested);
        Assert.Empty(_world.StagedFiles());
        await using var db = _db.Director();
        Assert.Equal("sign.mp4", (await db.TblMessageAssets.Include(a => a.Asset).SingleAsync()).Asset.AssetName);
    }

    // The dialog's markup, from its overlay to the end of the page's markup.
    private static string DialogHtml(InteractiveRenderer page)
    {
        var html = page.Html;
        var start = html.IndexOf("mm-dialog", StringComparison.Ordinal);
        Assert.True(start >= 0, "The authoring dialog is not open.");
        return html[html.LastIndexOf('<', start)..];
    }

    private async Task<InteractiveRenderer> RenderPageAsync(int messageTypeId, params string[] keys)
    {
        var services = new ServiceCollection().AddLogging();
        var context = _db.Director();
        services.AddSingleton<IDirectorDBContext>(context);
        services.AddSingleton(_db.Factory);
        services.AddSingleton(Options.Create(new D02GtaaMessagingOptions()));
        services.AddSingleton<ILanguageProvider>(new FakeLanguages());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISessionState>(_world.Session(keys));
        services.AddSingleton<IAssetIngestService>(_world.Library);
        services.AddSingleton(_world.MediaPaths);
        services.AddScoped<IMessageLanguageCatalogue, MessageLanguageCatalogue>();
        services.AddScoped<MessageAuthoring>();
        // The page also holds the send dialog, which reads Message access rules (ZONE-08).
        services.AddScoped<IMessageSendAccess, MessageSendAccess>();
        services.AddScoped<MessageSending>();
        // Saves log their message.* events (B-53); MessageAuthoringTests and MessageEventsTests check them.
        services.AddSingleton<IUserEventLog>(new RecordingEventLog());
        services.Configure<MessagingOptions>(o => o.DefaultTimeZone = "America/Toronto");
        services.AddSingleton<DisplayTimeZones>();
        services.AddScoped<ShownVersions>();
        services.AddScoped<DXSuite.Director.Services.ContentSource.PendingFileDeletes>();
        services.AddScoped<ActiveMessages>();
        services.AddSingleton<Microsoft.JSInterop.IJSRuntime>(new TorontoBrowser());
        var provider = services.BuildServiceProvider();

        // The Messages page hands each tab its type as read through the same context the tab reads with.
        var type = await context.TblMessageTypes.Include(t => t.Languages).Include(t => t.AssetLibraries)
            .SingleAsync(t => t.MessageTypeId == messageTypeId);
        var languages = (await new MessageLanguageCatalogue(Options.Create(new D02GtaaMessagingOptions()), new FakeLanguages())
                .NameAsync(type.Languages.OrderBy(l => l.SortOrder).Select(l => l.LanguageCode)))
            .Select(l => new TblLanguage { LanguageCode = l.Code, LanguageName = l.Name })
            .ToList();
        return await InteractiveRenderer.RenderAsync<MessagePage>(
            new Dictionary<string, object?> { ["MessageType"] = type, ["Languages"] = languages }, provider);
    }
}
