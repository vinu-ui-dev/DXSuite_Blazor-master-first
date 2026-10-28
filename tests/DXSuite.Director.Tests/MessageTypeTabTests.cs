using DXSuite.Director.ConfigTabs;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.CustomerDeviations;
using DXSuite.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Tests;

// Clause and gap ids in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// Configuration > Message Types as an admin uses it (B-31): the list in rank order and the Message Type dialog,
/// driven through <see cref="InteractiveRenderer"/> over a Director database on LocalDB.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessageTypeTabTests : IClassFixture<MessageTypeDatabase>, IAsyncLifetime
{
    private readonly MessageTypeDatabase _db;

    public MessageTypeTabTests(MessageTypeDatabase db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [LocalDbFact]
    public async Task The_list_reads_Rank_and_Screen_Part_with_the_Top_priority_type_pinned_first()
    {
        await _db.AddTypeAsync("PAGING", priority: 1);
        var top = await _db.AddTypeAsync("EMERGENCY", priority: 9, top: true);
        using var tab = await RenderTabAsync();

        var html = tab.Html;
        Assert.Contains(">Rank<", html);
        Assert.Contains(">Screen Part<", html);
        Assert.DoesNotContain(">Priority", html);
        Assert.True(html.IndexOf("EMERGENCY", StringComparison.Ordinal) < html.IndexOf("PAGING", StringComparison.Ordinal));
        Assert.Contains("Pinned", html);
        Assert.DoesNotContain($"mt-up-{top}", html);
        Assert.DoesNotContain($"mt-down-{top}", html);
        Assert.Contains("draggable=\"false\"", html);
    }

    [LocalDbFact]
    public async Task Move_down_in_the_list_reranks_the_types_one_to_n()
    {
        var a = await _db.AddTypeAsync("A", priority: 10);
        var b = await _db.AddTypeAsync("B", priority: 20);
        var c = await _db.AddTypeAsync("C", priority: 30);
        using var tab = await RenderTabAsync();

        await tab.ClickAsync($"#mt-down-{a}");

        Assert.Equal([(b, 1), (a, 2), (c, 3)], await _db.LiveRanksAsync());
        Assert.True(tab.Html.IndexOf(">B<", StringComparison.Ordinal) < tab.Html.IndexOf(">A<", StringComparison.Ordinal));
    }

    [LocalDbFact]
    public async Task The_dialog_shows_its_settings_in_always_visible_cards()
    {
        var id = await _db.AddTypeAsync("PAGING", priority: 1);
        using var tab = await RenderTabAsync();

        await tab.ClickAsync($"#mt-edit-{id}");

        var html = tab.Html;
        foreach (var card in new[] { "General", "Text", "Display", "Colours", "Asset Slots" })
            Assert.Matches($"class=\"dx-card-header[^\"]*\"[^>]*><span[^>]*>{card}</span>", html);
        Assert.DoesNotContain("Additional Settings", html);
        Assert.Contains("Character Limit", html);
        Assert.Contains("Screen Part", html);
    }

    [LocalDbFact]
    public async Task Code_can_be_typed_for_a_new_type_and_is_locked_once_saved()
    {
        var id = await _db.AddTypeAsync("PAGING", priority: 1);
        using var tab = await RenderTabAsync();

        await tab.ClickAsync("mt-add");
        Assert.DoesNotMatch("<input[^>]*id=\"mt-code\"[^>]* disabled", tab.Html);
        await tab.ClickAsync("#mt-cancel");

        await tab.ClickAsync($"#mt-edit-{id}");
        Assert.Matches("<input[^>]*id=\"mt-code\"[^>]* disabled", tab.Html);
    }

    [LocalDbFact]
    public async Task Cancel_discards_every_change_made_in_the_dialog()
    {
        var id = await _db.AddTypeAsync("PAGING", priority: 1, languages: ["eng"]);
        using var tab = await RenderTabAsync();

        await tab.ClickAsync($"#mt-edit-{id}");
        await tab.ChangeAsync("#mt-name", "Changed");
        await tab.ChangeAsync("#mt-limit", "90");
        await tab.ChangeAsync("#mt-lang-add", "fra");
        await tab.ChangeAsync("#mt-back-colour", "#123456");
        await tab.ClickAsync("#mt-cancel");

        await using (var db = _db.Director())
        {
            var stored = await db.TblMessageTypes.Include(t => t.Languages).SingleAsync(t => t.MessageTypeId == id);
            Assert.Equal(("PAGING name", 160, "#000000"), (stored.DisplayName, stored.MaxCharacters, stored.DefaultBackgroundColor));
            Assert.Equal(["eng"], stored.Languages.Select(l => l.LanguageCode));
        }

        await tab.ClickAsync($"#mt-edit-{id}");
        Assert.Matches("id=\"mt-name\"[^>]*value=\"PAGING name\"", tab.Html);
        Assert.DoesNotContain("French", tab.Html[tab.Html.IndexOf("mt-langs", StringComparison.Ordinal)..tab.Html.IndexOf("mt-lang-add", StringComparison.Ordinal)]);
    }

    [LocalDbFact]
    public async Task Save_keeps_the_edits_and_the_preview_uses_the_types_own_colours()
    {
        var id = await _db.AddTypeAsync("EMERGENCY", priority: 1, languages: ["eng"]);
        using var tab = await RenderTabAsync();

        await tab.ClickAsync($"#mt-edit-{id}");
        await tab.ChangeAsync("#mt-text-colour", "#ffc72c");
        await tab.ChangeAsync("#mt-back-colour", "#1c3f94");
        Assert.Matches("class=\"mt-part[^\"]*\" style=\"--mt-text: #ffc72c; --mt-back: #1c3f94", tab.Html);

        await tab.ChangeAsync("#mt-limit", "120");
        await tab.ChangeAsync("#mt-lang-add", "spa");
        await tab.ClickAsync("#mt-save");

        await using var db = _db.Director();
        var stored = await db.TblMessageTypes.Include(t => t.Languages).SingleAsync(t => t.MessageTypeId == id);
        Assert.Equal((120, "#FFC72C", "#1C3F94"), (stored.MaxCharacters, stored.DefaultTextColor, stored.DefaultBackgroundColor));
        Assert.Equal(["eng", "spa"], stored.Languages.OrderBy(l => l.SortOrder).Select(l => l.LanguageCode));
        Assert.DoesNotContain("mt-dialog", tab.Html);
    }

    [LocalDbFact]
    public async Task Ticking_Top_priority_on_a_second_type_moves_the_flag_only_after_the_confirm()
    {
        var holder = await _db.AddTypeAsync("EMERGENCY", priority: 1, top: true);
        var other = await _db.AddTypeAsync("IRROPS", priority: 2);
        using var tab = await RenderTabAsync();

        await tab.ClickAsync($"#mt-edit-{other}");
        await tab.ChangeAsync("#mt-top", true);
        Assert.Contains("EMERGENCY name is the Top priority type", tab.Html);
        Assert.Equal([holder], await _db.TopPriorityTypesAsync());

        await tab.ClickAsync("#mt-top-move");
        await tab.ClickAsync("#mt-save");

        Assert.Equal([other], await _db.TopPriorityTypesAsync());
        Assert.True(tab.Html.IndexOf("IRROPS", StringComparison.Ordinal) < tab.Html.IndexOf("EMERGENCY", StringComparison.Ordinal));
    }

    [LocalDbFact]
    public async Task Keeping_the_Top_priority_where_it_is_leaves_the_box_unticked()
    {
        await _db.AddTypeAsync("EMERGENCY", priority: 1, top: true);
        var other = await _db.AddTypeAsync("IRROPS", priority: 2);
        using var tab = await RenderTabAsync();

        await tab.ClickAsync($"#mt-edit-{other}");
        await tab.ChangeAsync("#mt-top", true);
        await tab.ClickAsync("#mt-top-keep");

        Assert.DoesNotContain("is the Top priority type", tab.Html);
        Assert.DoesNotMatch("<input[^>]*id=\"mt-top\"[^>]* checked", tab.Html);
    }

    [LocalDbFact]
    public async Task Asset_Slots_are_added_from_library_constraints_and_reordered_with_move_buttons()
    {
        var id = await _db.AddTypeAsync("PAGING", priority: 1);
        var hd = await _db.AddLibraryConstraintAsync("Full HD");
        using var tab = await RenderTabAsync();

        await tab.ClickAsync($"#mt-edit-{id}");
        await AddSlotAsync(tab, "BG", hd);
        await AddSlotAsync(tab, "ASL", hd);
        await tab.ClickAsync("#mt-slot-up-1");
        await tab.ClickAsync("#mt-save");

        await using var db = _db.Director();
        Assert.Equal(["ASL", "BG"], await db.TblMessageTypeAssetLibraries.Where(s => s.MessageTypeId == id)
            .OrderBy(s => s.SortOrder).Select(s => s.SlotKey).ToListAsync());
    }

    [LocalDbFact]
    public async Task With_D02_on_languages_are_fixed_and_one_ASL_slot_is_the_most_a_type_has()
    {
        var id = await _db.AddTypeAsync("PAGING", priority: 1, languages: ["eng", "fra"]);
        var video = await _db.AddLibraryConstraintAsync("ASL video");
        using var tab = await RenderTabAsync(gtaa: true);

        await tab.ClickAsync($"#mt-edit-{id}");
        Assert.DoesNotContain("mt-lang-add", tab.Html);
        Assert.DoesNotContain("mt-lang-remove", tab.Html);
        Assert.DoesNotContain("id=\"mt-slot-key\"", tab.Html);
        Assert.Contains("GTAA Customer Deviation", tab.Html);

        await tab.ChangeAsync("#mt-slot-constraint", video.ToString());
        await tab.ClickAsync("#mt-slot-add");
        Assert.DoesNotContain("mt-slot-add", tab.Html);
        await tab.ClickAsync("#mt-save");

        await using var db = _db.Director();
        Assert.Equal("ASL", (await db.TblMessageTypeAssetLibraries.SingleAsync(s => s.MessageTypeId == id)).SlotKey);
    }

    [LocalDbFact]
    public async Task With_D02_off_languages_and_slot_keys_stay_open()
    {
        var id = await _db.AddTypeAsync("PAGING", priority: 1);
        using var tab = await RenderTabAsync();

        await tab.ClickAsync($"#mt-edit-{id}");

        Assert.Contains("id=\"mt-lang-add\"", tab.Html);
        Assert.Contains("id=\"mt-slot-key\"", tab.Html);
        Assert.DoesNotContain("GTAA Customer Deviation", tab.Html);
    }

    [LocalDbTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_dialog_offers_Delete_on_the_Counter_type_only_with_D02_off(bool gtaa)
    {
        var id = await _db.AddTypeAsync("COUNTER", priority: 1);
        using var tab = await RenderTabAsync(gtaa);

        await tab.ClickAsync($"#mt-edit-{id}");

        Assert.Equal(!gtaa, tab.Html.Contains("id=\"mt-delete\""));
        Assert.Equal(gtaa, tab.Html.Contains("id=\"mt-built-in\""));
    }

    [LocalDbFact]
    public async Task The_tab_loads_through_Message_Type_configuration_alone()
    {
        await _db.AddTypeAsync("PAGING", priority: 1);

        // No IDirectorDBContext is registered, so the tab cannot bulk-load Messages, texts or Activations.
        using var tab = await RenderTabAsync();

        Assert.Contains("PAGING", tab.Html);
        Assert.Null(tab.Services.GetService<IDirectorDBContext>());
    }

    private static async Task AddSlotAsync(TabUnderTest tab, string key, int constraintId)
    {
        await tab.ChangeAsync("#mt-slot-key", key);
        await tab.ChangeAsync("#mt-slot-constraint", constraintId.ToString());
        await tab.ClickAsync("#mt-slot-add");
    }

    private async Task<TabUnderTest> RenderTabAsync(bool gtaa = false)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(Options.Create(new D02GtaaMessagingOptions { Enabled = gtaa }));
        services.AddSingleton(_db.Factory);
        services.AddSingleton<ILanguageProvider>(new FakeLanguages());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IMessageLanguageCatalogue, MessageLanguageCatalogue>();
        services.AddScoped<MessageTypeConfiguration>();
        services.AddSingleton<ISessionState>(new KeySession((int)Roles.Role.Admin));
        services.AddSingleton<IUserEventLog>(new FlightEditAuditTests.RecordingLog());
        services.AddScoped<MessageAccessRules>();
        services.AddSingleton<IEmailService>(new RecordingEmailService());
        services.AddScoped<DXSuite.Director.Services.ApprovalEmails>();
        services.AddScoped<MessageApprovalChain>();
        var provider = services.BuildServiceProvider();
        return new TabUnderTest(await InteractiveRenderer.RenderAsync<MessageTypeTab>(new Dictionary<string, object?>(), provider), provider);
    }

    private sealed class TabUnderTest(InteractiveRenderer renderer, IServiceProvider services) : IDisposable
    {
        public IServiceProvider Services => services;
        public string Html => renderer.Html;
        public Task ClickAsync(string selector) => renderer.ClickAsync(selector);
        public Task ChangeAsync(string selector, object? value) => renderer.ChangeAsync(selector, value);
        public void Dispose() => renderer.Dispose();
    }
}
