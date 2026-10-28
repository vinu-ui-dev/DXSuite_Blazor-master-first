using DXSuite.Director.ConfigTabs;
using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Audit;
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

// Clause ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// The Approval Workflows section of a Message Type's Access dialog (ZONE-09, MSG-11), as an admin uses it from the
/// Access column on Configuration > Message Types, driven through <see cref="InteractiveRenderer"/> over a Director
/// database on LocalDB.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessageTypeApprovalDialogTests : IClassFixture<MessageAccessDatabase>, IAsyncLifetime
{
    private readonly MessageAccessDatabase _db;

    public MessageTypeApprovalDialogTests(MessageAccessDatabase db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [LocalDbFact]
    public async Task The_section_sits_below_Access_Rules_with_no_chain_and_says_edits_always_need_approval()
    {
        using var tab = await RenderTabAsync();

        await tab.ClickAsync($"#mt-access-{Paging}");

        var html = tab.Html;
        Assert.True(html.IndexOf("Access Rules", StringComparison.Ordinal) < html.IndexOf("Approval Workflows", StringComparison.Ordinal));
        Assert.Contains("id=\"mac-enabled\"", html);
        Assert.Contains("Edits to an Approved Message always need approval", html);
        Assert.DoesNotContain("Require approval when content is replaced", html);
        Assert.DoesNotContain("id=\"mac-add-stage\"", html);
        Assert.Contains("id=\"mac-save\"", html);
    }

    [LocalDbFact]
    public async Task Switching_the_chain_on_picking_a_group_and_emails_saves_it_against_the_type_and_shows_the_approvers()
    {
        using var tab = await RenderTabAsync();
        await tab.ClickAsync($"#mt-access-{Paging}");

        await tab.ChangeAsync("#mac-enabled", true);
        await tab.ChangeAsync("#mac-stage-0-group-0", Rmu.ToString());
        Assert.Contains("Approvers: Bo Duty", tab.Html);
        await tab.ChangeAsync("#mac-email", true);
        Assert.Contains("value=\"24\"", Between(tab.Html, "id=\"mac-reminder\"", ">"));
        await tab.ChangeAsync("#mac-reminder", "12");
        await tab.ClickAsync("#mac-save");

        Assert.Contains("Approval chain saved.", tab.Html);
        await using var db = _db.Director();
        var chain = await db.TblApprovalChains.Include(c => c.Stages).ThenInclude(s => s.StageGroups).SingleAsync();
        Assert.Equal(((int?)Paging, (int?)null, true, true, 12), (chain.MessageTypeId, chain.LocationId, chain.IsEnabled, chain.EmailApprovers, chain.ReminderIntervalHours));
        Assert.Equal([Rmu], chain.Stages.Single().StageGroups.Select(g => g.UserGroupId));
        Assert.Equal(UserEventActionKeys.ApprovalChainChanged, (await db.TblEventLogs.SingleAsync()).EventTitle);
    }

    [LocalDbFact]
    public async Task Two_groups_joined_by_AND_then_a_second_stage_are_stored_in_order()
    {
        var duty = await _db.AddGroupAsync("Duty Managers", Ana);
        var ops = await _db.AddGroupAsync("Operations", Bo);
        using var tab = await RenderTabAsync();
        await tab.ClickAsync($"#mt-access-{Paging}");
        await tab.ChangeAsync("#mac-enabled", true);

        await tab.ChangeAsync("#mac-stage-0-group-0", Rmu.ToString());
        await tab.ClickAsync("#mac-stage-0-add-group");
        await tab.ChangeAsync("#mac-stage-0-group-1", duty.ToString());
        await tab.ChangeAsync("#mac-stage-0-operator", "AND");
        await tab.ClickAsync("#mac-add-stage");
        Assert.Contains("THEN", tab.Html);
        await tab.ChangeAsync("#mac-stage-1-group-0", ops.ToString());
        await tab.ClickAsync("#mac-add-stage");
        await tab.ClickAsync("#mac-stage-2-remove");
        await tab.ClickAsync("#mac-save");

        await using var db = _db.Director();
        var stages = (await db.TblApprovalChains.Include(c => c.Stages).ThenInclude(s => s.StageGroups).SingleAsync())
            .Stages.OrderBy(s => s.StageOrder).ToList();
        Assert.Equal(["AND", "OR"], stages.Select(s => s.IntraStageOperator));
        Assert.Equal([[Rmu, duty], [ops]], stages.Select(s => s.StageGroups.OrderBy(g => g.SortOrder).Select(g => g.UserGroupId).ToArray()));
    }

    [LocalDbFact]
    public async Task An_enabled_chain_with_no_group_picked_says_so_and_stores_nothing()
    {
        using var tab = await RenderTabAsync();
        await tab.ClickAsync($"#mt-access-{Paging}");
        await tab.ChangeAsync("#mac-enabled", true);

        await tab.ClickAsync("#mac-save");

        Assert.Contains("Stage 1 has no groups assigned.", tab.Html);
        await using var db = _db.Director();
        Assert.False(await db.TblApprovalChains.AnyAsync());
    }

    [LocalDbFact]
    public async Task LLM_auto_approval_and_retention_are_stored_with_the_chain()
    {
        using var tab = await RenderTabAsync();
        await tab.ClickAsync($"#mt-access-{Paging}");
        await tab.ChangeAsync("#mac-enabled", true);
        await tab.ChangeAsync("#mac-stage-0-group-0", Rmu.ToString());

        await tab.ChangeAsync("#mac-llm", true);
        await tab.ChangeAsync("#mac-retention", "9");
        await tab.ClickAsync("#mac-save");

        await using var db = _db.Director();
        var chain = await db.TblApprovalChains.SingleAsync();
        Assert.Equal((true, 0, 9), (chain.EnableLlmAutoApproval, chain.LlmAutoApprovalUpToStageOrder, chain.DeclinedContentRetentionDays));
    }

    [LocalDbFact]
    public async Task A_stored_chain_reopens_as_saved()
    {
        using (var first = await RenderTabAsync())
        {
            await first.ClickAsync($"#mt-access-{Paging}");
            await first.ChangeAsync("#mac-enabled", true);
            await first.ChangeAsync("#mac-stage-0-group-0", Rmu.ToString());
            await first.ChangeAsync("#mac-email", true);
            await first.ClickAsync("#mac-save");
        }

        using var tab = await RenderTabAsync();
        await tab.ClickAsync($"#mt-access-{Paging}");

        Assert.Contains("checked", Between(tab.Html, "id=\"mac-enabled\"", ">"));
        Assert.Contains("checked", Between(tab.Html, "id=\"mac-email\"", ">"));
        Assert.Contains("Approvers: Bo Duty", tab.Html);
    }

    [LocalDbFact]
    public async Task A_role_without_config_tab_message_types_sees_no_chain_and_no_Save()
    {
        using var tab = await RenderTabAsync(new KeySession(PermissionKeys.ConfigTabZoneBuilder));

        await tab.ClickAsync($"#mt-access-{Paging}");

        Assert.Contains("Your role cannot change Message Type Access", tab.Html);
        Assert.DoesNotContain("Approval Workflows", tab.Html);
        Assert.DoesNotContain("id=\"mac-save\"", tab.Html);
    }

    [LocalDbFact]
    public async Task The_section_and_stage_editor_use_StyleGuide_classes_and_no_inline_styles()
    {
        using var tab = await RenderTabAsync();
        await tab.ClickAsync($"#mt-access-{Paging}");
        await tab.ChangeAsync("#mac-enabled", true);
        await tab.ChangeAsync("#mac-stage-0-group-0", Rmu.ToString());
        await tab.ClickAsync("#mac-stage-0-add-group");
        await tab.ClickAsync("#mac-add-stage");
        await tab.ChangeAsync("#mac-llm", true);
        await tab.ChangeAsync("#mac-email", true);

        var dialog = tab.Html[tab.Html.IndexOf("dx-modal-overlay", StringComparison.Ordinal)..];
        Assert.DoesNotContain("style=", dialog);
        Assert.Contains("dx-section-title", dialog);
        Assert.Contains("dx-form-control", dialog);
    }

    [LocalDbFact]
    public async Task With_no_SMTP_the_email_switch_is_disabled_with_a_note_and_with_SMTP_it_is_not()
    {
        using (var noSmtp = await RenderTabAsync(email: new RecordingEmailService { Configured = false }))
        {
            await noSmtp.ClickAsync($"#mt-access-{Paging}");
            await noSmtp.ChangeAsync("#mac-enabled", true);

            Assert.Contains("disabled", Between(noSmtp.Html, "id=\"mac-email\"", ">"));
            Assert.Contains("id=\"mac-email-note\"", noSmtp.Html);
            Assert.Contains("Approval emails need an SMTP server", noSmtp.Html);
        }

        using var smtp = await RenderTabAsync(email: new RecordingEmailService());
        await smtp.ClickAsync($"#mt-access-{Paging}");
        await smtp.ChangeAsync("#mac-enabled", true);
        Assert.DoesNotContain("disabled", Between(smtp.Html, "id=\"mac-email\"", ">"));
        Assert.DoesNotContain("id=\"mac-email-note\"", smtp.Html);
    }

    private static string Between(string html, string from, string to)
    {
        var start = html.IndexOf(from, StringComparison.Ordinal);
        return html[start..html.IndexOf(to, start, StringComparison.Ordinal)];
    }

    private async Task<TabUnderTest> RenderTabAsync(KeySession? session = null, IEmailService? email = null)
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
        if (email is not null) services.AddSingleton(email);
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
