using System.Text.Json;
using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Entities.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static DXSuite.Director.Tests.MessageAccessDatabase;

namespace DXSuite.Director.Tests;

// Clause ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// A Message Type's approval chain, the Approval Workflows section of its Access dialog (ZONE-09, MSG-11): what is
/// stored against the type, who the approvers are, who may change it, and the User event a change writes (AUD-08),
/// over a Director database on LocalDB and the real event writer.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessageApprovalChainTests : IClassFixture<MessageAccessDatabase>, IAsyncLifetime
{
    private readonly MessageAccessDatabase _db;

    public MessageApprovalChainTests(MessageAccessDatabase db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [LocalDbFact]
    public async Task A_type_with_no_chain_opens_switched_off_with_one_empty_stage_emails_off_and_a_24_hour_reminder()
    {
        using var scope = Scope();

        var view = (await Chains(scope).OpenAsync(Paging))!;

        Assert.Equal("Visual Paging", view.TypeName);
        Assert.False(view.Chain.IsEnabled);
        Assert.False(view.Chain.EmailApprovers);
        Assert.Equal(24, view.Chain.ReminderIntervalHours);
        var stage = Assert.Single(view.Chain.Stages);
        Assert.Equal([0], stage.GroupIds);
        Assert.Contains(view.Groups, g => g.Id == Rmu && g.Name == "RMU");
    }

    [LocalDbFact]
    public async Task Saving_stores_the_chain_against_the_type_and_never_a_location()
    {
        var duty = await _db.AddGroupAsync("Duty Managers", Ana);
        using var scope = Scope();
        var draft = Draft(new StageDefinition { IntraStageOperator = "AND", GroupIds = [Rmu, duty] });
        draft.EnableLlmAutoApproval = true;
        draft.DeclinedContentRetentionDays = 7;
        draft.EmailApprovers = true;
        draft.ReminderIntervalHours = 6;

        var result = await Chains(scope).SaveAsync(Paging, draft);

        Assert.Equal(MessageChainSaveOutcome.Saved, result.Outcome);
        await using var db = _db.Director();
        var chain = await db.TblApprovalChains.Include(c => c.Stages).ThenInclude(s => s.StageGroups).SingleAsync();
        Assert.Equal(((int?)Paging, (int?)null, true), (chain.MessageTypeId, chain.LocationId, chain.IsEnabled));
        Assert.Equal((true, 7, true, 6), (chain.EnableLlmAutoApproval, chain.DeclinedContentRetentionDays, chain.EmailApprovers, chain.ReminderIntervalHours));
        var stage = Assert.Single(chain.Stages);
        Assert.Equal("AND", stage.IntraStageOperator);
        Assert.Equal([Rmu, duty], stage.StageGroups.OrderBy(g => g.SortOrder).Select(g => g.UserGroupId));
    }

    [LocalDbFact]
    public async Task Saving_again_replaces_the_stages_on_the_same_chain_and_reopens_as_saved()
    {
        var duty = await _db.AddGroupAsync("Duty Managers", Ana);
        using var scope = Scope();
        await Chains(scope).SaveAsync(Paging, Draft(new StageDefinition { GroupIds = [Rmu] }));

        await Chains(scope).SaveAsync(Paging, Draft(
            new StageDefinition { GroupIds = [duty] },
            new StageDefinition { GroupIds = [Rmu] }));

        await using var db = _db.Director();
        Assert.Equal(1, await db.TblApprovalChains.CountAsync());
        var reopened = (await Chains(scope).OpenAsync(Paging))!.Chain;
        Assert.True(reopened.IsEnabled);
        Assert.Equal([[duty], [Rmu]], reopened.Stages.Select(s => s.GroupIds.ToArray()));
    }

    [LocalDbFact]
    public async Task An_enabled_chain_with_a_stage_and_no_group_is_refused_and_nothing_is_stored()
    {
        using var scope = Scope();

        var result = await Chains(scope).SaveAsync(Paging, Draft(
            new StageDefinition { GroupIds = [Rmu] },
            new StageDefinition { GroupIds = [0] }));

        Assert.Equal(MessageChainSaveOutcome.Invalid, result.Outcome);
        Assert.Contains("Stage 2 has no groups assigned.", result.Errors);
        await using var db = _db.Director();
        Assert.False(await db.TblApprovalChains.AnyAsync());
        Assert.Equal(0, await db.TblEventLogs.CountAsync());
    }

    [LocalDbTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_reminder_interval_under_one_hour_is_refused(int hours)
    {
        using var scope = Scope();
        var draft = Draft(new StageDefinition { GroupIds = [Rmu] });
        draft.EmailApprovers = true;
        draft.ReminderIntervalHours = hours;

        var result = await Chains(scope).SaveAsync(Paging, draft);

        Assert.Equal(MessageChainSaveOutcome.Invalid, result.Outcome);
        Assert.Contains("The reminder interval must be at least 1 hour.", result.Errors);
    }

    [LocalDbFact]
    public async Task The_approvers_are_the_enabled_members_of_each_stages_groups()
    {
        var duty = await _db.AddGroupAsync("Duty Managers", Ana, Admin);
        await using (var db = _db.Director())
            await db.Database.ExecuteSqlRawAsync($"UPDATE tblUser SET IsDisabled = 1 WHERE UserId = {Admin}");
        try
        {
            using var scope = Scope();
            await Chains(scope).SaveAsync(Paging, Draft(
                new StageDefinition { GroupIds = [Rmu] },
                new StageDefinition { GroupIds = [duty] }));

            var stages = await Chains(scope).ApproversAsync(Paging);

            Assert.Equal([0, 1], stages.Select(s => s.StageOrder));
            Assert.Equal([Bo], stages[0].Approvers.Select(a => a.UserId));
            Assert.Equal([Ana], stages[1].Approvers.Select(a => a.UserId));
            Assert.Equal(("Ana Agent", "ana@gtaa.test"), (stages[1].Approvers[0].Name, stages[1].Approvers[0].UserPrincipalName));
        }
        finally
        {
            await using var db = _db.Director();
            await db.Database.ExecuteSqlRawAsync($"UPDATE tblUser SET IsDisabled = 0 WHERE UserId = {Admin}");
        }
    }

    [LocalDbFact]
    public async Task A_type_with_no_enabled_chain_has_no_approvers()
    {
        using var scope = Scope();
        var draft = Draft(new StageDefinition { GroupIds = [Rmu] });
        draft.IsEnabled = false;
        await Chains(scope).SaveAsync(Paging, draft);

        Assert.Empty(await Chains(scope).ApproversAsync(Paging));
        Assert.Empty(await Chains(scope).ApproversAsync(Emergency));
    }

    [LocalDbFact]
    public async Task A_change_logs_approval_chain_changed_with_the_stages_old_and_new()
    {
        var duty = await _db.AddGroupAsync("Duty Managers", Ana);
        using var scope = Scope();
        await Chains(scope).SaveAsync(Paging, Draft(new StageDefinition { GroupIds = [Rmu] }));

        await Chains(scope).SaveAsync(Paging, Draft(new StageDefinition { IntraStageOperator = "AND", GroupIds = [Rmu, duty] }));

        await using var db = _db.Director();
        var events = await db.TblEventLogs.OrderBy(e => e.RecordId).ToListAsync();
        Assert.Equal(2, events.Count);
        var second = events[1];
        Assert.Equal((UserEventActionKeys.ApprovalChainChanged, UserEventTargetTypes.MessageType, Paging.ToString(), "Visual Paging", Admin),
            (second.EventTitle, second.TargetType, second.TargetId, second.TargetName, second.UserId));
        using var details = JsonDocument.Parse(second.Details!);
        var old = details.RootElement.GetProperty("old");
        var now = details.RootElement.GetProperty("new");
        Assert.Equal(
            $$"""[{"operator":"OR","groups":[{"userGroupId":{{Rmu}},"name":"RMU"}]}]""",
            JsonSerializer.Serialize(old.GetProperty("stages")));
        Assert.Equal(
            $$"""[{"operator":"AND","groups":[{"userGroupId":{{Rmu}},"name":"RMU"},{"userGroupId":{{duty}},"name":"Duty Managers"}]}]""",
            JsonSerializer.Serialize(now.GetProperty("stages")));
        Assert.True(now.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, events[0].Details is { } first
            ? JsonDocument.Parse(first).RootElement.GetProperty("old").ValueKind
            : JsonValueKind.Undefined);
    }

    [LocalDbFact]
    public async Task Saving_with_nothing_changed_writes_nothing()
    {
        using var scope = Scope();
        await Chains(scope).SaveAsync(Paging, Draft(new StageDefinition { GroupIds = [Rmu] }));

        var result = await Chains(scope).SaveAsync(Paging, Draft(new StageDefinition { GroupIds = [Rmu] }));

        Assert.Equal(MessageChainSaveOutcome.Unchanged, result.Outcome);
        await using var db = _db.Director();
        Assert.Equal(1, await db.TblEventLogs.CountAsync());
    }

    [LocalDbFact]
    public async Task A_role_without_config_tab_message_types_can_neither_open_nor_change_the_chain()
    {
        using var scope = Scope(new KeySession(PermissionKeys.ConfigTabZoneBuilder));
        var chains = Chains(scope);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => chains.OpenAsync(Paging));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => chains.SaveAsync(Paging, Draft(new StageDefinition { GroupIds = [Rmu] })));
        await using var db = _db.Director();
        Assert.False(await db.TblApprovalChains.AnyAsync());
    }

    [LocalDbFact]
    public async Task A_role_with_config_tab_message_types_may_change_it()
    {
        using var scope = Scope(new KeySession(PermissionKeys.ConfigTabMessageTypes));

        var result = await Chains(scope).SaveAsync(Paging, Draft(new StageDefinition { GroupIds = [Rmu] }));

        Assert.Equal(MessageChainSaveOutcome.Saved, result.Outcome);
    }

    [LocalDbFact]
    public async Task A_deleted_type_has_no_chain_to_open_or_save()
    {
        await using (var db = _db.Director())
            await db.Database.ExecuteSqlRawAsync($"UPDATE tblMessageType SET IsDeleted = 1 WHERE MessageTypeId = {Paging}");
        try
        {
            using var scope = Scope();
            Assert.Null(await Chains(scope).OpenAsync(Paging));
            Assert.Equal(MessageChainSaveOutcome.TypeNotFound,
                (await Chains(scope).SaveAsync(Paging, Draft(new StageDefinition { GroupIds = [Rmu] }))).Outcome);
        }
        finally
        {
            await using var db = _db.Director();
            await db.Database.ExecuteSqlRawAsync($"UPDATE tblMessageType SET IsDeleted = 0 WHERE MessageTypeId = {Paging}");
        }
    }

    private static ApprovalChainDraft Draft(params StageDefinition[] stages) => new() { IsEnabled = true, Stages = stages.ToList() };

    private static MessageApprovalChain Chains(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<MessageApprovalChain>();

    /// <summary>A circuit scope signed in as the Admin holding the given role keys (Admin holds every key by default).</summary>
    private IServiceScope Scope(KeySession? session = null)
    {
        session ??= new KeySession((int)Roles.Role.Admin);
        session.User = new TblUser { UserId = Admin, UserPrincipalName = "ada@gtaa.test", FriendlyName = "Ada Admin", RoleId = 1 };
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(_db.Factory);
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ISessionState>(_ => session);
        services.AddUserEventLog();
        services.AddSingleton<DXSuite.Shared.Services.IEmailService>(new RecordingEmailService());
        services.AddScoped<ApprovalEmails>();
        services.AddScoped<MessageApprovalChain>();
        return services.BuildServiceProvider().CreateScope();
    }
}
