using System.Text.Json;
using DXSuite.Director.Entities;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// A Message Type's approval chain, the Approval Workflows section of its Access dialog (FSD 1.1 ZONE-09, MSG-11).
/// The chain is a <c>tblApprovalChain</c> row owned by the type (<c>MessageTypeId</c>, never a location), with the
/// same stages of groups as a location's chain. Approvers are the members of the stage groups. An enabled chain
/// makes new canned Messages of the type save as Pending (<see cref="MessageAuthoring"/>); edits to an Approved
/// Message always need approval under it, so the type has no switch for that. Opening and saving need
/// <c>config-tab:message-types</c> (section 3.10); a change writes <c>approval-chain.changed</c> (AUD-08).
/// </summary>
public sealed class MessageApprovalChain(
    IDbContextFactory<DirectorDbContext> dbFactory,
    ISessionState session,
    IUserEventLog eventLog,
    ILogger<MessageApprovalChain> logger,
    TimeProvider clock,
    ApprovalEmails emails)
{
    /// <summary>The type's chain (or a switched-off one to start from) and the groups a stage can name; null when the type is gone.</summary>
    public async Task<MessageChainView?> OpenAsync(int messageTypeId, CancellationToken ct = default)
    {
        await DemandAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var typeName = await db.TblMessageTypes
            .Where(t => t.MessageTypeId == messageTypeId && !t.IsDeleted)
            .Select(t => t.DisplayName)
            .SingleOrDefaultAsync(ct);
        if (typeName is null) return null;

        var chain = await LoadChainAsync(db, messageTypeId, tracked: false, ct);
        var groups = await db.TblUserGroups
            .Select(g => new MessageAccessChoice(g.UserGroupId, g.GroupName))
            .ToListAsync(ct);
        var members = await db.TblUserGroupMemberships
            .Where(m => !m.User.IsDisabled)
            .Select(m => new { m.UserGroupId, Name = m.User.FriendlyName ?? m.User.UserPrincipalName ?? "User " + m.UserId })
            .ToListAsync(ct);

        return new MessageChainView(
            messageTypeId,
            typeName,
            ApprovalChainDraft.From(chain),
            groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            members.GroupBy(m => m.UserGroupId).ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(m => m.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()));
    }

    /// <summary>
    /// Stores <paramref name="draft"/> as the type's chain. An enabled chain must pass the stage rules every chain
    /// meets; a switched-off one keeps whatever stages have a group, ready to switch back on. Messages waiting on the
    /// chain follow it in the same transaction: new stages, or switching the chain on, put them on its first stage
    /// (and email that stage once saved); switching it off withdraws them, so their next save approves them at once
    /// as with no chain. A change that keeps the stages keeps the approvals already made.
    /// </summary>
    public async Task<MessageChainSaveResult> SaveAsync(int messageTypeId, ApprovalChainDraft draft, CancellationToken ct = default)
    {
        await DemandAsync(messageTypeId);

        var stages = draft.Stages
            .Select((s, i) => new StageDefinition
            {
                StageOrder = i,
                StageName = s.StageName,
                IntraStageOperator = s.IntraStageOperator,
                GroupIds = s.GroupIds.Where(id => id > 0).ToList()
            })
            .ToList();
        var errors = new List<string>();
        if (draft.IsEnabled)
            errors.AddRange(ApprovalChainService.Validate(stages).Errors);
        if (draft.ReminderIntervalHours < TblApprovalChain.ReminderIntervalHoursMinimum)
            errors.Add($"The reminder interval must be at least {TblApprovalChain.ReminderIntervalHoursMinimum} hour.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var type = await db.TblMessageTypes.AsNoTracking()
            .SingleOrDefaultAsync(t => t.MessageTypeId == messageTypeId && !t.IsDeleted, ct);
        if (type is null) return new(MessageChainSaveOutcome.TypeNotFound);

        var groupIds = stages.SelectMany(s => s.GroupIds).Distinct().ToList();
        var groupNames = await db.TblUserGroups
            .Where(g => groupIds.Contains(g.UserGroupId))
            .ToDictionaryAsync(g => g.UserGroupId, g => g.GroupName, ct);
        if (groupIds.Any(id => !groupNames.ContainsKey(id)))
            errors.Add("A stage names a group that no longer exists.");
        if (errors.Count > 0) return new(MessageChainSaveOutcome.Invalid, errors);

        var chain = await LoadChainAsync(db, messageTypeId, tracked: true, ct);
        var before = chain is null ? null : await SnapshotAsync(db, chain, ct);

        var kept = stages.Where(s => s.GroupIds.Count > 0).ToList();
        var after = new ChainSnapshot(
            draft.IsEnabled,
            kept.Select(s => new StageSnapshot(s.IntraStageOperator,
                s.GroupIds.Select(id => new StageGroupSnapshot(id, groupNames[id])).ToList())).ToList(),
            draft.EnableLlmAutoApproval,
            draft.ClampedLlmAutoApprovalUpToStageOrder,
            draft.ClampedDeclinedContentRetentionDays,
            draft.EmailApprovers,
            draft.ClampedReminderIntervalHours);
        if (before is not null && Json(before) == Json(after))
            return new(MessageChainSaveOutcome.Unchanged);

        var editUser = session.User?.UserId.ToString() ?? "system";
        var wasEnabled = chain?.IsEnabled == true;
        var stagesChanged = before is null
            || JsonSerializer.Serialize(before.Stages, SnapshotJson) != JsonSerializer.Serialize(after.Stages, SnapshotJson);
        var submitted = new List<int>();
        var target = MessagingEvents.MessageType(type.MessageTypeId, type.DisplayName);
        var changed = new UserEvent(UserEventActionKeys.ApprovalChainChanged, target,
            Details: new Dictionary<string, object?> { ["old"] = before, ["new"] = after });
        var now = clock.GetUtcNow().UtcDateTime;
        await eventLog.FailureLoggedAsync(logger, changed, async () =>
        {
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                if (chain is null)
                {
                    chain = new TblApprovalChain { MessageTypeId = messageTypeId };
                    db.TblApprovalChains.Add(chain);
                }
                draft.ApplyTo(chain, editUser, now);
                await db.SaveChangesAsync(ct);

                // Replacing the stages drops the queue entries on them, so it happens only when the stages change.
                if (stagesChanged)
                    await ApprovalChainService.ReplaceStagesAsync(db, chain, kept, ct);

                if (after.Enabled && (stagesChanged || !wasEnabled))
                {
                    foreach (var messageId in await WaitingMessagesAsync(db, messageTypeId, ct))
                        if (await MessageApprovals.SubmitAsync(db, messageId, messageTypeId, now, ct) is int requestId)
                            submitted.Add(requestId);
                }
                else if (!after.Enabled && wasEnabled)
                {
                    foreach (var messageId in await WaitingMessagesAsync(db, messageTypeId, ct))
                        await MessageApprovals.WithdrawAsync(db, messageId, now, ct);
                }
                await tx.CommitAsync(ct);
            }
        });

        await eventLog.TryWriteAsync(logger, changed);
        foreach (var requestId in submitted)
            await emails.StageReachedAsync(requestId, ct);
        return new(MessageChainSaveOutcome.Saved);
    }

    /// <summary>
    /// Who approves the type's Messages, stage by stage: the enabled members of each stage's groups (MSG-11). Empty
    /// when the type has no enabled chain. Needs no permission, so the approval emails and reminder digests can ask
    /// from a worker scope.
    /// </summary>
    public async Task<IReadOnlyList<ApprovalStageApprovers>> ApproversAsync(int messageTypeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var stages = await db.TblApprovalStages
            .Where(s => s.ApprovalChain.MessageTypeId == messageTypeId && s.ApprovalChain.IsEnabled)
            .Select(s => new { s.ApprovalStageId, s.StageOrder })
            .ToListAsync(ct);
        var stageIds = stages.Select(s => s.ApprovalStageId).ToList();
        var members = await (
                from g in db.TblApprovalStageGroups
                join m in db.TblUserGroupMemberships on g.UserGroupId equals m.UserGroupId
                where stageIds.Contains(g.ApprovalStageId) && !m.User.IsDisabled
                select new { g.ApprovalStageId, m.UserId, Name = m.User.FriendlyName ?? m.User.UserPrincipalName, m.User.UserPrincipalName })
            .ToListAsync(ct);

        return stages
            .OrderBy(s => s.StageOrder)
            .Select(s => new ApprovalStageApprovers(s.ApprovalStageId, s.StageOrder, members
                .Where(m => m.ApprovalStageId == s.ApprovalStageId)
                .DistinctBy(m => m.UserId)
                .Select(m => new ChainApprover(m.UserId, m.Name ?? "", m.UserPrincipalName))
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()))
            .ToList();
    }

    // Canned Messages of the type that wait on its chain: new ones Pending, and Approved ones with a pending revision.
    private static Task<List<int>> WaitingMessagesAsync(DirectorDbContext db, int messageTypeId, CancellationToken ct) =>
        db.TblMessages
            .Where(m => m.MessageTypeId == messageTypeId && m.IsCanned && !m.IsDeleted
                && (m.Status == MessageStatus.Pending || (m.Status == MessageStatus.Approved && m.PendingRevision != null)))
            .Select(m => m.MessageId)
            .ToListAsync(ct);

    private static async Task<TblApprovalChain?> LoadChainAsync(DirectorDbContext db, int messageTypeId, bool tracked, CancellationToken ct)
    {
        var query = db.TblApprovalChains
            .Include(c => c.Stages)
                .ThenInclude(s => s.StageGroups)
            .Where(c => c.MessageTypeId == messageTypeId);
        return await (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(ct);
    }

    private static async Task<ChainSnapshot> SnapshotAsync(DirectorDbContext db, TblApprovalChain chain, CancellationToken ct)
    {
        var ids = chain.Stages.SelectMany(s => s.StageGroups).Select(g => g.UserGroupId).Distinct().ToList();
        var names = await db.TblUserGroups
            .Where(g => ids.Contains(g.UserGroupId))
            .ToDictionaryAsync(g => g.UserGroupId, g => g.GroupName, ct);
        return new ChainSnapshot(
            chain.IsEnabled,
            chain.Stages.OrderBy(s => s.StageOrder)
                .Select(s => new StageSnapshot(s.IntraStageOperator,
                    s.StageGroups.OrderBy(g => g.SortOrder)
                        .Select(g => new StageGroupSnapshot(g.UserGroupId, names.GetValueOrDefault(g.UserGroupId) ?? "")).ToList()))
                .ToList(),
            chain.EnableLlmAutoApproval,
            chain.LlmAutoApprovalUpToStageOrder,
            chain.DeclinedContentRetentionDays,
            chain.EmailApprovers,
            chain.ReminderIntervalHours);
    }

    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

    private static string Json(ChainSnapshot snapshot) => JsonSerializer.Serialize(snapshot, SnapshotJson);

    /// <param name="changing">The type whose chain a save would change, logged as refused when the role lacks the key; null for a read.</param>
    private Task DemandAsync(int? changing = null) =>
        session.DemandAsync(PermissionKeys.ConfigTabMessageTypes,
            changing is { } messageTypeId
                ? new UserEvent(UserEventActionKeys.ApprovalChainChanged, MessagingEvents.MessageType(messageTypeId, null))
                : null,
            eventLog, logger);

    // The approval-chain.changed details (AUD-08: stages old and new), with the chain's other settings beside them.
    private sealed record ChainSnapshot(
        bool Enabled,
        IReadOnlyList<StageSnapshot> Stages,
        bool LlmAutoApproval,
        int LlmAutoApprovalUpToStage,
        int RetentionDays,
        bool EmailApprovers,
        int ReminderIntervalHours);

    private sealed record StageSnapshot(string Operator, IReadOnlyList<StageGroupSnapshot> Groups);

    private sealed record StageGroupSnapshot(int UserGroupId, string Name);
}

/// <param name="Chain">The type's chain to edit; switched off with defaults when the type has none.</param>
/// <param name="GroupMembers">Each group's enabled members by name: the approvers a stage naming it adds.</param>
public sealed record MessageChainView(
    int MessageTypeId,
    string TypeName,
    ApprovalChainDraft Chain,
    IReadOnlyList<MessageAccessChoice> Groups,
    IReadOnlyDictionary<int, IReadOnlyList<string>> GroupMembers);

public enum MessageChainSaveOutcome
{
    Saved,

    /// <summary>The chain already reads as the draft; nothing was written or logged.</summary>
    Unchanged,
    Invalid,
    TypeNotFound,
}

public sealed record MessageChainSaveResult(MessageChainSaveOutcome Outcome, IReadOnlyList<string>? Errors = null)
{
    public IReadOnlyList<string> Errors { get; } = Errors ?? [];
}

/// <param name="UserPrincipalName">The sign-in name; tblUser holds no other address.</param>
public sealed record ChainApprover(int UserId, string Name, string? UserPrincipalName);

public sealed record ApprovalStageApprovers(int ApprovalStageId, int StageOrder, IReadOnlyList<ChainApprover> Approvers);
