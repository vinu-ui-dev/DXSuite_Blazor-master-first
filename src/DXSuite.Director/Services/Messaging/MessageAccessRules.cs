using DXSuite.Director.Entities;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// The Access Rules section of a Message Type's Access dialog (FSD 1.1 ZONE-07): the type's Message access rules,
/// each naming a user or a group, one Display Zone or every Display Zone, and whether one-off text is allowed.
/// Every call needs <c>config-tab:message-types</c> (section 3.10), checked here as well as by the tab. Each change
/// writes <c>message-access-rule.added</c> or <c>.removed</c> (AUD-08). What the rules allow is
/// <see cref="IMessageSendAccess"/>'s to read.
/// </summary>
public sealed class MessageAccessRules(
    IDbContextFactory<DirectorDbContext> dbFactory,
    ISessionState session,
    IUserEventLog eventLog,
    ILogger<MessageAccessRules> logger)
{
    public const string EveryDisplayZone = "Every Display Zone";

    /// <summary>The type's rules and what a new rule can name; null when the type is gone.</summary>
    public async Task<MessageAccessView?> OpenAsync(int messageTypeId, CancellationToken ct = default)
    {
        await DemandAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var typeName = await db.TblMessageTypes
            .Where(t => t.MessageTypeId == messageTypeId && !t.IsDeleted)
            .Select(t => t.DisplayName)
            .SingleOrDefaultAsync(ct);
        if (typeName is null) return null;

        var rules = await db.TblMessagePermissions
            .Where(p => p.MessageTypeId == messageTypeId)
            .Select(p => new
            {
                p.MessagePermissionId,
                p.UserId,
                p.UserGroupId,
                UserName = p.User == null ? null : p.User.FriendlyName ?? p.User.UserPrincipalName,
                GroupName = p.UserGroup == null ? null : p.UserGroup.GroupName,
                p.ZoneId,
                ZoneName = p.Zone == null ? null : p.Zone.ZoneName,
                ZoneRetired = p.Zone != null && p.Zone.RetiredUtc != null,
                p.CanFreeText,
            })
            .ToListAsync(ct);

        var users = await db.TblUsers
            .Where(u => !u.IsDisabled)
            .Select(u => new MessageAccessChoice(u.UserId, u.FriendlyName ?? u.UserPrincipalName ?? "User " + u.UserId))
            .ToListAsync(ct);
        var groups = await db.TblUserGroups
            .Select(g => new MessageAccessChoice(g.UserGroupId, g.GroupName))
            .ToListAsync(ct);
        // Retired Display Zones leave the pickers (ZONE-12).
        var zones = await db.TblZones
            .Where(z => z.RetiredUtc == null)
            .OrderBy(z => z.SortOrder).ThenBy(z => z.ZoneId)
            .Select(z => new MessageAccessChoice(z.ZoneId, z.ZoneName))
            .ToListAsync(ct);

        var rows = rules
            .Select(r => new MessageAccessRuleRow(
                r.MessagePermissionId,
                IsGroup: r.UserId is null,
                GranteeId: r.UserId ?? r.UserGroupId ?? 0,
                GranteeName: (r.UserId is null ? r.GroupName : r.UserName) ?? "",
                r.ZoneId,
                r.ZoneId is null ? EveryDisplayZone : r.ZoneName ?? $"Display Zone {r.ZoneId}",
                r.ZoneRetired,
                r.CanFreeText))
            .OrderBy(r => r.GranteeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ZoneId is null ? 0 : 1).ThenBy(r => r.ZoneName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MessageAccessView(messageTypeId, typeName, rows,
            users.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            zones);
    }

    public async Task<MessageAccessRuleResult> AddAsync(int messageTypeId, MessageAccessRuleDraft draft, CancellationToken ct = default)
    {
        await DemandAsync(UserEventActionKeys.MessageAccessRuleAdded, messageTypeId);
        if ((draft.UserId is null) == (draft.UserGroupId is null))
            return new(MessageAccessRuleOutcome.GranteeRequired);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var type = await db.TblMessageTypes.SingleOrDefaultAsync(t => t.MessageTypeId == messageTypeId && !t.IsDeleted, ct);
        if (type is null) return new(MessageAccessRuleOutcome.TypeNotFound);

        var granteeExists = draft.UserId is { } userId
            ? await db.TblUsers.AnyAsync(u => u.UserId == userId && !u.IsDisabled, ct)
            : await db.TblUserGroups.AnyAsync(g => g.UserGroupId == draft.UserGroupId, ct);
        if (!granteeExists) return new(MessageAccessRuleOutcome.GranteeNotFound);

        if (draft.ZoneId is { } zoneId && !await db.TblZones.AnyAsync(z => z.ZoneId == zoneId && z.RetiredUtc == null, ct))
            return new(MessageAccessRuleOutcome.ZoneNotOffered);

        // One rule per grantee, type and Display Zone (the unique indexes count every Display Zone once too), so
        // changing one-off text is a remove and an add.
        if (await db.TblMessagePermissions.AnyAsync(p => p.MessageTypeId == messageTypeId && p.ZoneId == draft.ZoneId
                && p.UserId == draft.UserId && p.UserGroupId == draft.UserGroupId, ct))
            return new(MessageAccessRuleOutcome.AlreadyExists);

        var rule = new TblMessagePermission
        {
            MessageTypeId = messageTypeId,
            UserId = draft.UserId,
            UserGroupId = draft.UserGroupId,
            ZoneId = draft.ZoneId,
            CanFreeText = draft.CanFreeText,
        };
        db.TblMessagePermissions.Add(rule);
        await eventLog.FailureLoggedAsync(logger, Event(UserEventActionKeys.MessageAccessRuleAdded, type, rule), () => db.SaveChangesAsync(ct));

        await eventLog.TryWriteAsync(logger, Event(UserEventActionKeys.MessageAccessRuleAdded, type, rule));
        return new(MessageAccessRuleOutcome.Added, rule.MessagePermissionId);
    }

    /// <summary>Removes one of the type's rules; false when the type holds no such rule.</summary>
    public async Task<bool> RemoveAsync(int messageTypeId, int ruleId, CancellationToken ct = default)
    {
        await DemandAsync(UserEventActionKeys.MessageAccessRuleRemoved, messageTypeId);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rule = await db.TblMessagePermissions
            .Include(p => p.MessageType)
            .SingleOrDefaultAsync(p => p.MessagePermissionId == ruleId && p.MessageTypeId == messageTypeId, ct);
        if (rule is null) return false;

        db.TblMessagePermissions.Remove(rule);
        await eventLog.FailureLoggedAsync(logger, Event(UserEventActionKeys.MessageAccessRuleRemoved, rule.MessageType, rule),
            () => db.SaveChangesAsync(ct));

        await eventLog.TryWriteAsync(logger, Event(UserEventActionKeys.MessageAccessRuleRemoved, rule.MessageType, rule));
        return true;
    }

    // AUD-08 details: userId or userGroupId, zoneId or "every", canFreeText.
    private static UserEvent Event(string actionKey, TblMessageType type, TblMessagePermission rule)
    {
        var details = new Dictionary<string, object>();
        if (rule.UserId is { } userId) details["userId"] = userId;
        else details["userGroupId"] = rule.UserGroupId!.Value;
        details["zoneId"] = rule.ZoneId is { } zoneId ? zoneId : "every";
        details["canFreeText"] = rule.CanFreeText;
        return new UserEvent(actionKey, MessagingEvents.MessageType(type.MessageTypeId, type.DisplayName), Details: details);
    }

    /// <param name="actionKey">The change asked for, logged as refused when the role lacks the key; null for a read.</param>
    private Task DemandAsync(string? actionKey = null, int? messageTypeId = null) =>
        session.DemandAsync(PermissionKeys.ConfigTabMessageTypes,
            actionKey is null ? null : new UserEvent(actionKey, MessagingEvents.MessageType(messageTypeId, null)), eventLog, logger);
}

/// <summary>A user or a group (exactly one), one Display Zone or every Display Zone (null), and one-off text.</summary>
public sealed record MessageAccessRuleDraft(int? UserId, int? UserGroupId, int? ZoneId, bool CanFreeText);

/// <param name="ZoneRetired">The rule names a retired Display Zone, so it grants nothing (ZONE-12).</param>
public sealed record MessageAccessRuleRow(
    int RuleId, bool IsGroup, int GranteeId, string GranteeName, int? ZoneId, string ZoneName, bool ZoneRetired, bool CanFreeText);

public sealed record MessageAccessChoice(int Id, string Name);

/// <param name="Zones">Live Display Zones in list order; a rule can also name every Display Zone.</param>
public sealed record MessageAccessView(
    int MessageTypeId,
    string TypeName,
    IReadOnlyList<MessageAccessRuleRow> Rules,
    IReadOnlyList<MessageAccessChoice> Users,
    IReadOnlyList<MessageAccessChoice> Groups,
    IReadOnlyList<MessageAccessChoice> Zones);

public enum MessageAccessRuleOutcome
{
    Added,

    /// <summary>Neither or both of a user and a group were named.</summary>
    GranteeRequired,
    GranteeNotFound,

    /// <summary>The Display Zone is retired or gone.</summary>
    ZoneNotOffered,

    /// <summary>The grantee already has a rule for this Display Zone (or for every Display Zone).</summary>
    AlreadyExists,
    TypeNotFound,
}

public sealed record MessageAccessRuleResult(MessageAccessRuleOutcome Outcome, int RuleId = 0);
