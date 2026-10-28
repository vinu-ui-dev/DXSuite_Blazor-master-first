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
/// A Message Type's Access Rules section (ZONE-07): adding and removing Message access rules, who may, and the
/// User events each change writes (AUD-08), over a Director database on LocalDB and the real event writer.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessageAccessRulesTests : IClassFixture<MessageAccessDatabase>, IAsyncLifetime
{
    private readonly MessageAccessDatabase _db;

    public MessageAccessRulesTests(MessageAccessDatabase db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [LocalDbFact]
    public async Task Adding_a_users_rule_on_one_Display_Zone_lists_it_and_logs_message_access_rule_added()
    {
        using var scope = Scope();
        var rules = scope.ServiceProvider.GetRequiredService<MessageAccessRules>();

        var result = await rules.AddAsync(Paging, new MessageAccessRuleDraft(UserId: Ana, UserGroupId: null, ZoneId: GatesA, CanFreeText: true));

        Assert.Equal(MessageAccessRuleOutcome.Added, result.Outcome);
        var row = Assert.Single((await rules.OpenAsync(Paging))!.Rules);
        Assert.Equal((result.RuleId, false, Ana, "Ana Agent", (int?)GatesA, "Gates A", false, true),
            (row.RuleId, row.IsGroup, row.GranteeId, row.GranteeName, row.ZoneId, row.ZoneName, row.ZoneRetired, row.CanFreeText));

        var logged = await SingleEventAsync();
        Assert.Equal((UserEventActionKeys.MessageAccessRuleAdded, UserEventTargetTypes.MessageType, Paging.ToString(), Admin),
            (logged.EventTitle, logged.TargetType, logged.TargetId, logged.UserId));
        Assert.Equal("""{"userId":2,"zoneId":1,"canFreeText":true}""", Normalised(logged.Details));
    }

    [LocalDbFact]
    public async Task A_groups_rule_for_every_Display_Zone_logs_userGroupId_and_every()
    {
        using var scope = Scope();
        var rules = scope.ServiceProvider.GetRequiredService<MessageAccessRules>();

        await rules.AddAsync(Paging, new MessageAccessRuleDraft(null, Rmu, ZoneId: null, CanFreeText: false));

        var row = Assert.Single((await rules.OpenAsync(Paging))!.Rules);
        Assert.Equal((true, Rmu, "RMU", (int?)null, "Every Display Zone"), (row.IsGroup, row.GranteeId, row.GranteeName, row.ZoneId, row.ZoneName));
        Assert.Equal("""{"userGroupId":5,"zoneId":"every","canFreeText":false}""", Normalised((await SingleEventAsync()).Details));
    }

    [LocalDbFact]
    public async Task Removing_a_rule_deletes_it_and_logs_message_access_rule_removed_with_the_rules_details()
    {
        var ruleId = await _db.AddRuleAsync(Paging, CheckIn, canFreeText: true, groupId: Rmu);
        using var scope = Scope();
        var rules = scope.ServiceProvider.GetRequiredService<MessageAccessRules>();

        Assert.True(await rules.RemoveAsync(Paging, ruleId));

        Assert.Empty((await rules.OpenAsync(Paging))!.Rules);
        var logged = await SingleEventAsync();
        Assert.Equal((UserEventActionKeys.MessageAccessRuleRemoved, Paging.ToString()), (logged.EventTitle, logged.TargetId));
        Assert.Equal("""{"userGroupId":5,"zoneId":2,"canFreeText":true}""", Normalised(logged.Details));
    }

    [LocalDbFact]
    public async Task A_rule_names_exactly_one_user_or_group()
    {
        using var scope = Scope();
        var rules = scope.ServiceProvider.GetRequiredService<MessageAccessRules>();

        Assert.Equal(MessageAccessRuleOutcome.GranteeRequired, (await rules.AddAsync(Paging, new(null, null, GatesA, false))).Outcome);
        Assert.Equal(MessageAccessRuleOutcome.GranteeRequired, (await rules.AddAsync(Paging, new(Ana, Rmu, GatesA, false))).Outcome);
        Assert.Empty((await rules.OpenAsync(Paging))!.Rules);
        Assert.Equal(0, await EventCountAsync());
    }

    [LocalDbFact]
    public async Task A_second_rule_for_the_same_grantee_and_Display_Zone_is_refused_whatever_its_one_off_text()
    {
        await _db.AddRuleAsync(Paging, zoneId: null, canFreeText: false, userId: Ana);
        using var scope = Scope();
        var rules = scope.ServiceProvider.GetRequiredService<MessageAccessRules>();

        var result = await rules.AddAsync(Paging, new(Ana, null, ZoneId: null, CanFreeText: true));

        Assert.Equal(MessageAccessRuleOutcome.AlreadyExists, result.Outcome);
        Assert.Single((await rules.OpenAsync(Paging))!.Rules);
        Assert.Equal(0, await EventCountAsync());
    }

    [LocalDbFact]
    public async Task Retired_Display_Zones_are_not_offered_and_a_rule_on_one_is_shown_as_granting_nothing()
    {
        await _db.AddRuleAsync(Paging, OldCheckIn, userId: Ana);
        using var scope = Scope();
        var rules = scope.ServiceProvider.GetRequiredService<MessageAccessRules>();

        var view = (await rules.OpenAsync(Paging))!;

        Assert.Equal([GatesA, CheckIn], view.Zones.Select(z => z.Id));
        Assert.True(Assert.Single(view.Rules).ZoneRetired);
        Assert.Equal(MessageAccessRuleOutcome.ZoneNotOffered, (await rules.AddAsync(Paging, new(Bo, null, OldCheckIn, false))).Outcome);
    }

    [LocalDbFact]
    public async Task The_dialog_offers_enabled_users_and_every_group_by_name()
    {
        using var scope = Scope();
        var view = (await scope.ServiceProvider.GetRequiredService<MessageAccessRules>().OpenAsync(Paging))!;

        Assert.Equal(["Ada Admin", "Ana Agent", "Bo Duty"], view.Users.Select(u => u.Name));
        Assert.Equal(["RMU"], view.Groups.Select(g => g.Name));
        Assert.Equal("Visual Paging", view.TypeName);
    }

    [LocalDbFact]
    public async Task Only_holders_of_config_tab_message_types_may_open_add_or_remove()
    {
        var ruleId = await _db.AddRuleAsync(Paging, GatesA, userId: Ana);
        using var scope = Scope(new KeySession(PermissionKeys.ConfigTabZoneBuilder));
        var rules = scope.ServiceProvider.GetRequiredService<MessageAccessRules>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rules.OpenAsync(Paging));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rules.AddAsync(Paging, new(Bo, null, GatesA, false)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rules.RemoveAsync(Paging, ruleId));

        await using var db = _db.Director();
        Assert.Equal([ruleId], await db.TblMessagePermissions.Select(p => p.MessagePermissionId).ToListAsync());
        // B-53: a refused change is an event with that outcome (AUD-08); opening the dialog changes nothing, so logs nothing.
        Assert.Equal(
            [(UserEventActionKeys.MessageAccessRuleAdded, UserEventOutcome.Refused), (UserEventActionKeys.MessageAccessRuleRemoved, UserEventOutcome.Refused)],
            (await db.TblEventLogs.OrderBy(e => e.RecordId).Select(e => new { e.EventTypeId, e.Outcome }).ToListAsync())
                .Select(e => (UserEventActionKeys.Catalogue.Single(c => c.EventTypeId == e.EventTypeId).ActionKey, e.Outcome)));
    }

    [LocalDbFact]
    public async Task A_rule_of_another_Message_Type_cannot_be_removed_through_this_one()
    {
        var ruleId = await _db.AddRuleAsync(Emergency, GatesA, userId: Ana);
        using var scope = Scope();

        Assert.False(await scope.ServiceProvider.GetRequiredService<MessageAccessRules>().RemoveAsync(Paging, ruleId));

        await using var db = _db.Director();
        Assert.True(await db.TblMessagePermissions.AnyAsync(p => p.MessagePermissionId == ruleId));
    }

    /// <summary>A circuit scope signed in as the Admin holding the given role keys (Admin holds every key by default).</summary>
    private IServiceScope Scope(KeySession? session = null)
    {
        session ??= new KeySession((int)Roles.Role.Admin);
        session.User = new TblUser { UserId = Admin, UserPrincipalName = "ada@gtaa.test", FriendlyName = "Ada Admin", RoleId = 1 };
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(_db.Factory);
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        services.AddScoped<ISessionState>(_ => session);
        services.AddUserEventLog();
        services.AddScoped<MessageAccessRules>();
        return services.BuildServiceProvider().CreateScope();
    }

    private async Task<TblEventLog> SingleEventAsync()
    {
        await using var db = _db.Director();
        return await db.TblEventLogs.SingleAsync();
    }

    private async Task<int> EventCountAsync()
    {
        await using var db = _db.Director();
        return await db.TblEventLogs.CountAsync();
    }

    private static string Normalised(string? json) => JsonSerializer.Serialize(JsonDocument.Parse(json!).RootElement);
}
