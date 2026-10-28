using DXSuite.Director.ConfigTabs;
using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.CustomerDeviations;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static DXSuite.Director.Tests.MessageAccessDatabase;

namespace DXSuite.Director.Tests;

// Clause and gap ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// The Access column on Configuration > Message Types and the Access dialog's Access Rules section (ZONE-07), as an
/// admin uses them, driven through <see cref="InteractiveRenderer"/> over a Director database on LocalDB.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessageTypeAccessDialogTests : IClassFixture<MessageAccessDatabase>, IAsyncLifetime
{
    private readonly MessageAccessDatabase _db;

    public MessageTypeAccessDialogTests(MessageAccessDatabase db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [LocalDbFact]
    public async Task The_Access_column_opens_the_Access_dialog_for_that_type()
    {
        using var tab = await RenderTabAsync();
        Assert.Contains(">Access<", tab.Html);

        await tab.ClickAsync($"#mt-access-{Paging}");

        Assert.Contains("Access Control — Visual Paging", tab.Html);
        Assert.Contains("Access Rules", tab.Html);
        Assert.Contains("Admin needs no Message access rule", tab.Html);
        Assert.Contains("No Message access rules", tab.Html);

        await tab.ClickAsync("access-close");
        Assert.DoesNotContain("Access Control", tab.Html);
    }

    [LocalDbFact]
    public async Task Adding_a_group_rule_on_one_Display_Zone_with_one_off_text_stores_it_and_lists_it()
    {
        using var tab = await RenderTabAsync();
        await tab.ClickAsync($"#mt-access-{Paging}");

        await tab.ClickAsync("#mar-add-open");
        await tab.ChangeAsync("#mar-kind", "Group");
        await tab.ChangeAsync("#mar-group", Rmu.ToString());
        await tab.ChangeAsync("#mar-zone", CheckIn.ToString());
        await tab.ChangeAsync("#mar-free-text", true);
        await tab.ClickAsync("#mar-add");

        await using var db = _db.Director();
        var rule = await db.TblMessagePermissions.SingleAsync();
        Assert.Equal((Paging, (int?)null, (int?)Rmu, (int?)CheckIn, true), (rule.MessageTypeId, rule.UserId, rule.UserGroupId, rule.ZoneId, rule.CanFreeText));
        Assert.Contains($"id=\"mar-row-{rule.MessagePermissionId}\"", tab.Html);
        Assert.DoesNotContain("id=\"mar-add\"", tab.Html);
        Assert.Equal(1, await db.TblEventLogs.CountAsync());
    }

    [LocalDbFact]
    public async Task A_users_rule_for_every_Display_Zone_is_the_default_zone_choice()
    {
        using var tab = await RenderTabAsync();
        await tab.ClickAsync($"#mt-access-{Paging}");

        await tab.ClickAsync("#mar-add-open");
        await tab.ChangeAsync("#mar-user", Ana.ToString());
        await tab.ClickAsync("#mar-add");

        await using var db = _db.Director();
        var rule = await db.TblMessagePermissions.SingleAsync();
        Assert.Equal(((int?)Ana, (int?)null, false), (rule.UserId, rule.ZoneId, rule.CanFreeText));
        Assert.Contains("Every Display Zone", tab.Html);
    }

    [LocalDbFact]
    public async Task Remove_deletes_the_rule()
    {
        var ruleId = await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        using var tab = await RenderTabAsync();
        await tab.ClickAsync($"#mt-access-{Paging}");
        Assert.Contains("Ana Agent", tab.Html);

        await tab.ClickAsync($"#mar-remove-{ruleId}");

        await using var db = _db.Director();
        Assert.False(await db.TblMessagePermissions.AnyAsync());
        Assert.DoesNotContain($"mar-row-{ruleId}", tab.Html);
    }

    [LocalDbFact]
    public async Task The_Display_Zone_picker_leaves_out_retired_zones_and_a_rule_on_one_reads_as_granting_nothing()
    {
        await _db.AddRuleAsync(Paging, OldCheckIn, userId: Bo);
        using var tab = await RenderTabAsync();
        await tab.ClickAsync($"#mt-access-{Paging}");
        await tab.ClickAsync("#mar-add-open");

        var picker = Between(tab.Html, "id=\"mar-zone\"", "</select>");
        Assert.Contains("Every Display Zone", picker);
        Assert.Contains("Gates A", picker);
        Assert.Contains("Check-in", picker);
        Assert.DoesNotContain("Old check-in", picker);
        Assert.Contains("Retired, grants nothing", tab.Html);
    }

    [LocalDbFact]
    public async Task Adding_with_no_user_chosen_says_so_and_stores_nothing()
    {
        using var tab = await RenderTabAsync();
        await tab.ClickAsync($"#mt-access-{Paging}");
        await tab.ClickAsync("#mar-add-open");

        await tab.ClickAsync("#mar-add");

        Assert.Contains("Choose a user.", tab.Html);
        await using var db = _db.Director();
        Assert.False(await db.TblMessagePermissions.AnyAsync());
    }

    [LocalDbFact]
    public async Task A_second_rule_for_the_same_Display_Zone_is_explained()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        using var tab = await RenderTabAsync();
        await tab.ClickAsync($"#mt-access-{Paging}");
        await tab.ClickAsync("#mar-add-open");

        await tab.ChangeAsync("#mar-user", Ana.ToString());
        await tab.ChangeAsync("#mar-zone", GatesA.ToString());
        await tab.ClickAsync("#mar-add");

        Assert.Contains("already has a rule for that Display Zone", tab.Html);
    }

    [LocalDbFact]
    public async Task A_role_without_config_tab_message_types_cannot_open_the_rules()
    {
        await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        using var tab = await RenderTabAsync(new KeySession(PermissionKeys.ConfigTabZoneBuilder));

        await tab.ClickAsync($"#mt-access-{Paging}");

        Assert.Contains("Your role cannot change Message Type Access", tab.Html);
        Assert.DoesNotContain("Ana Agent", tab.Html);
    }

    [LocalDbFact]
    public async Task The_dialog_uses_StyleGuide_classes_and_no_inline_styles()
    {
        await _db.AddRuleAsync(Paging, OldCheckIn, canFreeText: true, groupId: Rmu);
        using var tab = await RenderTabAsync();
        await tab.ClickAsync($"#mt-access-{Paging}");
        await tab.ClickAsync("#mar-add-open");

        var dialog = tab.Html[tab.Html.IndexOf("dx-modal-overlay", StringComparison.Ordinal)..];
        Assert.DoesNotContain("style=", dialog);
        Assert.Contains("dx-section-title", dialog);
        Assert.Contains("dx-table", dialog);
    }

    private static string Between(string html, string from, string to)
    {
        var start = html.IndexOf(from, StringComparison.Ordinal);
        return html[start..html.IndexOf(to, start, StringComparison.Ordinal)];
    }

    private async Task<TabUnderTest> RenderTabAsync(KeySession? session = null)
    {
        session ??= new KeySession((int)Roles.Role.Admin);
        session.User = new TblUser { UserId = Admin, UserPrincipalName = "ada@gtaa.test", FriendlyName = "Ada Admin", RoleId = 1 };
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(Options.Create(new D02GtaaMessagingOptions()));
        services.AddSingleton(_db.Factory);
        services.AddSingleton<ILanguageProvider>(new FakeLanguages());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        services.AddSingleton<ISessionState>(session);
        services.AddUserEventLog();
        services.AddScoped<IMessageLanguageCatalogue, MessageLanguageCatalogue>();
        services.AddScoped<MessageTypeConfiguration>();
        services.AddScoped<MessageAccessRules>();
        services.AddSingleton<IEmailService>(new RecordingEmailService());
        services.AddScoped<ApprovalEmails>();
        services.AddScoped<MessageApprovalChain>();
        var provider = services.BuildServiceProvider();
        return new TabUnderTest(await InteractiveRenderer.RenderAsync<MessageTypeTab>(new Dictionary<string, object?>(), provider));
    }

    private sealed class TabUnderTest(InteractiveRenderer renderer) : IDisposable
    {
        public string Html => renderer.Html;
        public Task ClickAsync(string selector) => renderer.ClickAsync(selector);
        public Task ChangeAsync(string selector, object? value) => renderer.ChangeAsync(selector, value);
        public void Dispose() => renderer.Dispose();
    }
}
