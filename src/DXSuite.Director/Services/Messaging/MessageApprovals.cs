using DXSuite.Director.Entities;
using DXSuite.Director.Services.ContentSource;
using DXSuite.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// Approving canned Messages on their type's chain (MSG-11 to MSG-13): the Messages section of My Queue and the
/// approvers' decisions. A Message runs through the chain as one <c>tblApprovalRequest</c> (MessageId set), with the
/// same stages, queue entries and actions as an asset; the request carries either a new Message (status Pending) or
/// the pending revision of an Approved one. One decision covers every language and every Asset Slot, the ASL video
/// included. Approvers are the enabled members of the current stage's groups, acting as the signed-in user.
/// </summary>
/// <remarks>
/// A decision is one transaction (<see cref="DecideAsync"/>): the action, the queue, the request and the Message
/// change commit together or not at all. The Message change itself is <see cref="ApplyApprovalAsync"/> and
/// <see cref="ApplyRejectionAsync"/>; work that must happen with an approval or a rejection joins them there.
/// </remarks>
public sealed class MessageApprovals
{
    private const int NotesLength = 500;

    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly ISessionState _session;
    private readonly IMessageLanguageCatalogue _languages;
    private readonly TimeProvider _clock;
    private readonly ApprovalEmails _emails;
    private readonly ILogger _logger;
    private readonly IUserEventLog _events;
    private readonly ShownVersions _versions;
    private readonly PendingFileDeletes _files;

    public MessageApprovals(IDbContextFactory<DirectorDbContext> dbFactory, ISessionState session,
        IMessageLanguageCatalogue languages, TimeProvider clock, IUserEventLog events, ApprovalEmails emails,
        ShownVersions versions, PendingFileDeletes files, ILogger<MessageApprovals>? logger = null)
    {
        _versions = versions;
        _files = files;
        _events = events;
        _dbFactory = dbFactory;
        _session = session;
        _languages = languages;
        _clock = clock;
        _emails = emails;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>
    /// The Messages waiting on the signed-in user: one item per pending queue entry of a group the user belongs to,
    /// oldest submission first, each with its type's languages side by side and a pending revision beside the
    /// approved content.
    /// </summary>
    public async Task<IReadOnlyList<MessageQueueItem>> MyQueueAsync(CancellationToken ct)
    {
        if (_session.User?.UserId is not int userId) return [];
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var entries = await db.TblApprovalQueueEntries.AsNoTracking()
            .Where(e => e.Status == "pending"
                && e.ApprovalRequest.Status == "pending"
                && e.ApprovalRequest.MessageId != null
                && db.TblUserGroupMemberships.Any(m => m.UserId == userId && m.UserGroupId == e.UserGroupId))
            .Select(e => new
            {
                e.QueueEntryId,
                e.ApprovalRequestId,
                MessageId = e.ApprovalRequest.MessageId!.Value,
                e.UserGroupId,
                e.UserGroup.GroupName,
                StageNumber = e.ApprovalStage.StageOrder + 1,
                StageCount = e.ApprovalRequest.ApprovalChain.Stages.Count,
                e.ApprovalRequest.SubmittedDate
            })
            .OrderBy(e => e.SubmittedDate).ThenBy(e => e.QueueEntryId)
            .ToListAsync(ct);
        if (entries.Count == 0) return [];

        var messageIds = entries.Select(e => e.MessageId).Distinct().ToList();
        var messages = await db.TblMessages.AsNoTracking().AsSplitQuery()
            .Include(m => m.MessageType).ThenInclude(t => t.Languages)
            .Include(m => m.Texts)
            .Include(m => m.Assets).ThenInclude(a => a.Asset)
            .Include(m => m.PendingRevision).ThenInclude(r => r!.Texts)
            .Include(m => m.PendingRevision).ThenInclude(r => r!.Assets).ThenInclude(a => a.Asset)
            .Where(m => messageIds.Contains(m.MessageId))
            .ToDictionaryAsync(m => m.MessageId, ct);

        var submitterIds = messages.Values
            .Select(m => m.PendingRevision?.SubmittedByUserId ?? m.ModifiedByUserId ?? m.CreatedByUserId)
            .OfType<int>().Distinct().ToList();
        var names = await db.TblUsers.AsNoTracking()
            .Where(u => submitterIds.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, u => u.FriendlyName ?? u.UserPrincipalName, ct);

        var items = new List<MessageQueueItem>();
        foreach (var entry in entries)
        {
            if (!messages.TryGetValue(entry.MessageId, out var message)) continue;
            var languages = await _languages.NameAsync(message.MessageType.Languages.OrderBy(l => l.SortOrder).Select(l => l.LanguageCode));
            var approved = Version(message.Name, message.RevisionNumber, message.Texts.Select(t => (t.LanguageCode, t.Body)),
                message.Assets.Select(a => (a.SlotKey, a.Asset)));
            var revision = message.PendingRevision;
            var submitter = revision?.SubmittedByUserId ?? message.ModifiedByUserId ?? message.CreatedByUserId;

            items.Add(new MessageQueueItem(
                entry.QueueEntryId,
                entry.ApprovalRequestId,
                message.MessageId,
                message.MessageType.DisplayName,
                entry.GroupName,
                entry.StageNumber,
                entry.StageCount,
                DateTime.SpecifyKind(entry.SubmittedDate, DateTimeKind.Utc),
                submitter is int id ? names.GetValueOrDefault(id) : null,
                languages,
                revision is null ? null : approved,
                revision is null
                    ? approved
                    : Version(revision.Name, null, revision.Texts.Select(t => (t.LanguageCode, t.Body)),
                        revision.Assets.Select(a => (a.SlotKey, a.Asset)))));
        }
        return items;
    }

    /// <summary>The signed-in approver approves for the entry's group; the last stage's approval approves the Message.</summary>
    public Task<MessageDecisionResult> ApproveAsync(int queueEntryId, string? notes, CancellationToken ct) =>
        DecideAsync(queueEntryId, approve: true, notes, ct);

    /// <summary>The signed-in approver rejects for the entry's group, which ends the chain at once.</summary>
    public Task<MessageDecisionResult> RejectAsync(int queueEntryId, string? notes, CancellationToken ct) =>
        DecideAsync(queueEntryId, approve: false, notes, ct);

    /// <summary>
    /// Records one group's decision and everything it causes, in one transaction: the action, the queue entries, the
    /// request's stage or outcome and, when the chain is decided, the Message itself (<see cref="ApplyApprovalAsync"/>
    /// or <see cref="ApplyRejectionAsync"/>). The next stage's approvers are emailed only after it commits.
    /// Each decision logs <c>message.approved</c> (one per stage approved) or <c>message.rejected</c> once it commits
    /// (AUD-08); a decision by someone outside the stage's group, or one that fails to save, logs with that outcome.
    /// </summary>
    private async Task<MessageDecisionResult> DecideAsync(int queueEntryId, bool approve, string? notes, CancellationToken ct)
    {
        if (_session.User?.UserId is not int userId)
            return new(MessageDecisionOutcome.NotAllowed, "Sign in to decide approvals.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var entry = await db.TblApprovalQueueEntries
            .Include(e => e.ApprovalRequest).ThenInclude(r => r.ApprovalChain).ThenInclude(c => c.Stages).ThenInclude(s => s.StageGroups)
            .SingleOrDefaultAsync(e => e.QueueEntryId == queueEntryId && e.ApprovalRequest.MessageId != null, ct);
        if (entry is null) return new(MessageDecisionOutcome.Gone, "This approval no longer exists.");
        if (entry.Status != "pending" || entry.ApprovalRequest.Status != "pending")
            return new(MessageDecisionOutcome.AlreadyDecided, "This item has already been decided.");
        var request = entry.ApprovalRequest;
        var messageId = request.MessageId!.Value;
        // Read before the decision: an approval moves the revision onto the Message and removes it.
        var subject = await db.TblMessages.Where(m => m.MessageId == messageId)
            .Select(m => new { m.Name, RevisionId = m.PendingRevision == null ? (int?)null : m.PendingRevision.MessageRevisionId })
            .SingleAsync(ct);
        var decisionEvent = approve
            ? new UserEvent(UserEventActionKeys.MessageApproved, MessagingEvents.Message(messageId, subject.Name), Details: new
            {
                revisionId = subject.RevisionId,
                chainStage = request.ApprovalChain.Stages.Single(s => s.ApprovalStageId == entry.ApprovalStageId).StageOrder + 1,
            })
            : new UserEvent(UserEventActionKeys.MessageRejected, MessagingEvents.Message(messageId, subject.Name), Details: new
            {
                revisionId = subject.RevisionId,
                reason = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            });

        if (!await db.TblUserGroupMemberships.AnyAsync(m => m.UserId == userId && m.UserGroupId == entry.UserGroupId, ct))
        {
            await _events.TryWriteAsync(_logger, decisionEvent with { Outcome = UserEventOutcome.Refused });
            return new(MessageDecisionOutcome.NotAllowed, "Only a member of this stage's group can decide it.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        db.TblApprovalActions.Add(new TblApprovalAction
        {
            ApprovalRequestId = request.ApprovalRequestId,
            ApprovalStageId = entry.ApprovalStageId,
            UserGroupId = entry.UserGroupId,
            ActingUserId = userId,
            ActionType = approve ? "approved" : "declined",
            Notes = notes is { Length: > NotesLength } ? notes[..NotesLength] : notes,
            ActionDate = now
        });
        Resolve(entry, approve ? "approved" : "declined", now, userId);

        var pending = await db.TblApprovalQueueEntries
            .Where(e => e.ApprovalRequestId == request.ApprovalRequestId && e.Status == "pending" && e.QueueEntryId != entry.QueueEntryId)
            .ToListAsync(ct);

        MessageDecisionResult result;
        var emailNextStage = false;
        if (!approve)
        {
            foreach (var other in pending) Resolve(other, "superseded", now, null);
            Complete(request, "declined", now);
            await ApplyRejectionAsync(db, messageId, now, ct);
            result = new(MessageDecisionOutcome.Rejected, "The Message was rejected.");
        }
        else
        {
            var stage = request.ApprovalChain.Stages.Single(s => s.ApprovalStageId == entry.ApprovalStageId);
            var sameStage = pending.Where(e => e.ApprovalStageId == stage.ApprovalStageId).ToList();
            if (stage.IntraStageOperator == "OR")
                foreach (var other in sameStage) Resolve(other, "superseded", now, null);

            if (stage.IntraStageOperator != "OR" && sameStage.Count > 0)
            {
                result = new(MessageDecisionOutcome.GroupRecorded, "Your group's approval is recorded; the stage waits for its other groups.");
            }
            else if (request.ApprovalChain.Stages
                         .Where(s => s.StageOrder > stage.StageOrder && s.StageGroups.Count > 0)
                         .OrderBy(s => s.StageOrder)
                         .FirstOrDefault() is { } next)
            {
                request.CurrentStageId = next.ApprovalStageId;
                AddQueueEntries(db, request.ApprovalRequestId, next, now);
                emailNextStage = true;
                result = new(MessageDecisionOutcome.Advanced, "Approved for this stage; the next stage's approvers decide it now.");
            }
            else
            {
                Complete(request, "approved", now);
                await ApplyApprovalAsync(db, messageId, now, _versions, _files, ct);
                result = new(MessageDecisionOutcome.Approved, "The Message is approved.");
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(MessageDecisionOutcome.AlreadyDecided, "Another approver decided this first.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _events.TryWriteAsync(_logger, decisionEvent with { Outcome = UserEventOutcome.Failed });
            throw;
        }

        await _events.TryWriteAsync(_logger, decisionEvent);

        if (emailNextStage)
            await _emails.StageReachedAsync(request.ApprovalRequestId, ct);
        _logger.LogInformation("Message {MessageId}: {Outcome} by user {UserId} for group {GroupId}",
            messageId, result.Outcome, userId, entry.UserGroupId);
        return result;
    }

    /// <summary>
    /// What approval does to the Message, inside the decision's transaction: a pending revision becomes the approved
    /// content (name, colours, every language's text and every slot's asset) and the Message's RevisionNumber moves on
    /// by one; a new Pending Message becomes Approved. No Activation starts (MSG-15): each Activation that is active or
    /// scheduled gains a shown version of the new content (AUD-06), which it shows from its next publish, and the
    /// messaging delivery version moves so the doorbell tells the script to fetch it (INK-09). An ASL video the
    /// revision replaces leaves its library (INK-10), its file kept for the replaced-file grace period.
    /// </summary>
    /// <remarks>
    /// This is the approval of a revision, so whatever must change with the approved text belongs in this transaction.
    /// A new Message has no Activation, so its approval changes nothing on screens.
    /// </remarks>
    internal static async Task ApplyApprovalAsync(DirectorDbContext db, int messageId, DateTime now, ShownVersions versions,
        PendingFileDeletes files, CancellationToken ct)
    {
        var message = await db.TblMessages
            .Include(m => m.Texts)
            .Include(m => m.Assets)
            .Include(m => m.PendingRevision).ThenInclude(r => r!.Texts)
            .Include(m => m.PendingRevision).ThenInclude(r => r!.Assets)
            .SingleAsync(m => m.MessageId == messageId, ct);

        if (message.PendingRevision is not { } revision)
        {
            if (message.Status == MessageStatus.Pending) message.Status = MessageStatus.Approved;
            return;
        }

        var videosBefore = AslVideos.IdsIn(message.Assets);
        message.Name = revision.Name;
        message.TextColor = revision.TextColor;
        message.BackgroundColor = revision.BackgroundColor;
        foreach (var text in revision.Texts)
        {
            var row = message.Texts.FirstOrDefault(t => string.Equals(t.LanguageCode, text.LanguageCode, StringComparison.OrdinalIgnoreCase));
            if (row is null) message.Texts.Add(new TblMessageText { LanguageCode = text.LanguageCode, Body = text.Body });
            else row.Body = text.Body;
        }

        // The revision holds every slot of the type as it was edited; a slot it leaves empty was emptied.
        foreach (var row in message.Assets.ToList())
        {
            if (!revision.Assets.Any(a => string.Equals(a.SlotKey, row.SlotKey, StringComparison.OrdinalIgnoreCase)))
                message.Assets.Remove(row);
        }
        foreach (var asset in revision.Assets)
        {
            var row = message.Assets.FirstOrDefault(a => string.Equals(a.SlotKey, asset.SlotKey, StringComparison.OrdinalIgnoreCase));
            if (row is null) message.Assets.Add(new TblMessageAsset { SlotKey = asset.SlotKey, AssetId = asset.AssetId });
            else row.AssetId = asset.AssetId;
        }

        message.RevisionNumber++;
        message.ModifiedUtc = now;
        message.ModifiedByUserId = revision.SubmittedByUserId;
        db.TblMessageRevisions.Remove(revision);
        await versions.AddRevisionAsync(db, message, now, ct);
        await DeliveryVersion.BumpAsync(db, ct);
        await AslVideos.DeleteReplacedAsync(db, messageId, AslVideos.Replaced(videosBefore, AslVideos.IdsIn(message.Assets)), files, ct);
    }

    /// <summary>
    /// What rejection does to the Message, inside the decision's transaction: a pending revision is discarded and the
    /// approved content stays as it is (MSG-13); a new Message goes back to Draft, which cannot be sent and is
    /// submitted again when its author saves it. The ASL video the rejected content brought is deleted from its library
    /// (INK-10): a revision's new video, or a new Message's own, which its author uploads again before resubmitting.
    /// </summary>
    internal static async Task ApplyRejectionAsync(DirectorDbContext db, int messageId, DateTime now, CancellationToken ct)
    {
        var message = await db.TblMessages
            .Include(m => m.Assets)
            .Include(m => m.PendingRevision).ThenInclude(r => r!.Assets)
            .SingleAsync(m => m.MessageId == messageId, ct);

        var approvedVideos = AslVideos.IdsIn(message.Assets);
        if (message.PendingRevision is { } revision)
        {
            var proposedVideos = AslVideos.IdsIn(revision.Assets).Except(approvedVideos).ToList();
            db.TblMessageRevisions.Remove(revision);
            await AslVideos.DeleteAsync(db, messageId, proposedVideos, now, ct);
        }
        else if (message.Status == MessageStatus.Pending)
        {
            message.Status = MessageStatus.Draft;
            await AslVideos.DeleteAsync(db, messageId, AslVideos.RemoveFrom(message.Assets), now, ct);
        }
    }

    /// <summary>
    /// Puts a saved Message (new, re-saved or with a fresh pending revision) on its type's chain at the first stage,
    /// inside the caller's transaction. A Message keeps one request: a resubmission resets it, and the queue entries it
    /// still had are superseded, so approvers decide only the latest content. Returns the request to email about,
    /// or null when the type has no enabled chain with a stage.
    /// </summary>
    internal static async Task<int?> SubmitAsync(DirectorDbContext db, int messageId, int messageTypeId, DateTime now, CancellationToken ct)
    {
        var chain = await db.TblApprovalChains
            .Include(c => c.Stages).ThenInclude(s => s.StageGroups)
            .SingleOrDefaultAsync(c => c.MessageTypeId == messageTypeId && c.IsEnabled, ct);
        var first = chain?.Stages.Where(s => s.StageGroups.Count > 0).OrderBy(s => s.StageOrder).FirstOrDefault();
        if (chain is null || first is null) return null;

        var request = await SupersedeAsync(db, messageId, now, ct);
        if (request is null)
        {
            request = new TblApprovalRequest { MessageId = messageId };
            db.TblApprovalRequests.Add(request);
        }
        request.ApprovalChainId = chain.ApprovalChainId;
        request.Status = "pending";
        request.SubmittedDate = now;
        request.CompletedDate = null;
        request.CurrentStageId = first.ApprovalStageId;
        await db.SaveChangesAsync(ct);

        AddQueueEntries(db, request.ApprovalRequestId, first, now);
        await db.SaveChangesAsync(ct);
        return request.ApprovalRequestId;
    }

    /// <summary>
    /// Takes a Message off its chain inside the caller's transaction, when it saves with no enabled chain on its type:
    /// a pending request is withdrawn, which also ends its reminders.
    /// </summary>
    internal static async Task WithdrawAsync(DirectorDbContext db, int messageId, DateTime now, CancellationToken ct)
    {
        var request = await SupersedeAsync(db, messageId, now, ct);
        if (request is null || request.Status != "pending") return;
        Complete(request, "withdrawn", now);
        await db.SaveChangesAsync(ct);
    }

    // The Message's request with its open queue entries closed, or null when it has never had one.
    private static async Task<TblApprovalRequest?> SupersedeAsync(DirectorDbContext db, int messageId, DateTime now, CancellationToken ct)
    {
        var request = await db.TblApprovalRequests
            .Include(r => r.QueueEntries.Where(e => e.Status == "pending"))
            .SingleOrDefaultAsync(r => r.MessageId == messageId, ct);
        foreach (var entry in request?.QueueEntries ?? [])
            Resolve(entry, "superseded", now, null);
        return request;
    }

    private static void AddQueueEntries(DirectorDbContext db, int requestId, TblApprovalStage stage, DateTime now)
    {
        foreach (var group in stage.StageGroups)
        {
            db.TblApprovalQueueEntries.Add(new TblApprovalQueueEntry
            {
                ApprovalRequestId = requestId,
                ApprovalStageId = stage.ApprovalStageId,
                UserGroupId = group.UserGroupId,
                Status = "pending",
                CreatedDate = now
            });
        }
    }

    private static void Resolve(TblApprovalQueueEntry entry, string status, DateTime now, int? userId)
    {
        entry.Status = status;
        entry.ResolvedDate = now;
        entry.ResolvedByUserId = userId;
    }

    private static void Complete(TblApprovalRequest request, string status, DateTime now)
    {
        request.Status = status;
        request.CurrentStageId = null;
        request.CompletedDate = now;
    }

    private static MessageQueueVersion Version(string name, int? revisionNumber, IEnumerable<(string Code, string Body)> texts,
        IEnumerable<(string SlotKey, TblAsset Asset)> assets) =>
        new(name, revisionNumber,
            texts.ToDictionary(t => t.Code, t => t.Body, StringComparer.OrdinalIgnoreCase),
            assets.OrderBy(a => a.SlotKey, StringComparer.OrdinalIgnoreCase)
                .Select(a => new MessageQueueAsset(a.SlotKey, a.Asset.AssetName, a.Asset.AssetPath)).ToList());
}
