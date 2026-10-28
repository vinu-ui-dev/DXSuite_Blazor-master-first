using DXSuite.Director.Entities;
using DXSuite.Director.Services.ContentSource;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// Writing canned Messages (MSG-03 to MSG-05, MSG-12, section 3.10 <c>feature:messages-author</c>): the one place a
/// Message's name, texts and Asset Slots are saved. The dialog edits a detached <see cref="MessageDraft"/>; uploads
/// wait in a staging folder of the draft's own and join the asset library only when the Message saves, so Cancel
/// (<see cref="Discard"/>) leaves nothing behind.
/// </summary>
/// <remarks>
/// Lifecycle for callers: <see cref="NewDraftAsync"/> or <see cref="OpenAsync"/>, then any number of
/// <see cref="StageAsync"/> and <see cref="ClearSlot"/>, then either <see cref="SaveAsync"/> or <see cref="Discard"/>.
/// One-off Messages are typed at send time and never come through here (MSG-05).
/// </remarks>
public sealed class MessageAuthoring
{
    public const string NameField = "Name";

    private const int NameLength = 100;
    private const string TempFolder = "TempFiles";

    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly IMessageLanguageCatalogue _languages;
    private readonly ISessionState _session;
    private readonly IAssetIngestService _library;
    private readonly ITenantMediaPathResolver _mediaPaths;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly ApprovalEmails? _emails;
    private readonly IUserEventLog _events;
    private readonly ShownVersions _versions;
    private readonly PendingFileDeletes _files;

    public MessageAuthoring(IDbContextFactory<DirectorDbContext> dbFactory, IMessageLanguageCatalogue languages,
        ISessionState session, IAssetIngestService library, ITenantMediaPathResolver mediaPaths, TimeProvider clock,
        IUserEventLog events, ShownVersions versions, PendingFileDeletes files, ILogger<MessageAuthoring>? logger = null,
        ApprovalEmails? emails = null)
    {
        _versions = versions;
        _files = files;
        _emails = emails;
        _events = events;
        _dbFactory = dbFactory;
        _languages = languages;
        _session = session;
        _library = library;
        _mediaPaths = mediaPaths;
        _clock = clock;
        _logger = logger ?? NullLogger<MessageAuthoring>.Instance;
    }

    /// <summary>The error key for one language's text.</summary>
    public static string TextField(string languageCode) => "Text:" + languageCode;

    /// <summary>The error key for one Asset Slot.</summary>
    public static string SlotField(string slotKey) => "Slot:" + slotKey;

    /// <summary>Whether the signed-in user may create and edit canned Messages.</summary>
    public Task<bool> CanAuthorAsync() => _session.HasPermissionAsync(PermissionKeys.MessagesAuthor);

    /// <summary>A blank canned Message of a live type, or null when the type no longer exists.</summary>
    public async Task<MessageDraft?> NewDraftAsync(int messageTypeId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var type = await LoadTypeAsync(db, messageTypeId, ct);
        if (type is null) return null;

        return new MessageDraft
        {
            MessageTypeId = type.MessageTypeId,
            MessageTypeName = type.DisplayName,
            MaxCharacters = type.MaxCharacters,
            NeedsApproval = await HasChainAsync(db, type.MessageTypeId, ct),
            Texts = await TextsAsync(type, codeBodies: null),
            Slots = Slots(type, assets: [])
        };
    }

    /// <summary>
    /// A detached copy of a live canned Message, or null when it is gone, retired or a one-off. Its texts and slots
    /// follow the type as configured now, so a language added to the type since shows as an empty box. An Approved
    /// Message with a pending revision opens on the revision, so a second edit builds on the first (MSG-13).
    /// </summary>
    public async Task<MessageDraft?> OpenAsync(int messageId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var message = await db.TblMessages.AsNoTracking().AsSplitQuery()
            .Include(m => m.Texts)
            .Include(m => m.Assets).ThenInclude(a => a.Asset)
            .Include(m => m.PendingRevision).ThenInclude(r => r!.Texts)
            .Include(m => m.PendingRevision).ThenInclude(r => r!.Assets).ThenInclude(a => a.Asset)
            .FirstOrDefaultAsync(m => m.MessageId == messageId, ct);
        if (message is null || !IsEditable(message)) return null;

        var type = await LoadTypeAsync(db, message.MessageTypeId, ct);
        if (type is null) return null;

        var revision = message.PendingRevision;
        var texts = revision is null
            ? message.Texts.ToDictionary(t => t.LanguageCode, t => t.Body, StringComparer.OrdinalIgnoreCase)
            : revision.Texts.ToDictionary(t => t.LanguageCode, t => t.Body, StringComparer.OrdinalIgnoreCase);
        var assets = revision is null
            ? message.Assets.Select(a => new HeldAsset(a.SlotKey, a.AssetId, a.Asset?.AssetName))
            : revision.Assets.Select(a => new HeldAsset(a.SlotKey, a.AssetId, a.Asset?.AssetName));

        return new MessageDraft
        {
            MessageId = message.MessageId,
            MessageTypeId = type.MessageTypeId,
            MessageTypeName = type.DisplayName,
            MaxCharacters = type.MaxCharacters,
            Status = message.Status,
            NeedsApproval = await HasChainAsync(db, type.MessageTypeId, ct),
            HasPendingRevision = revision is not null,
            Name = revision?.Name ?? message.Name,
            Texts = await TextsAsync(type, texts),
            Slots = Slots(type, assets.ToList())
        };
    }

    /// <summary>
    /// Takes over a file the uploader staged and holds it for <paramref name="slotKey"/> until the Message saves. A
    /// file already staged for the slot is deleted. The file moves into this draft's staging folder, which stays under
    /// TempFiles so the temp file sweep removes it if the session ends without a save or a Cancel.
    /// </summary>
    public Task StageAsync(MessageDraft draft, string slotKey, StagedUpload upload)
    {
        var slot = draft.Slots.FirstOrDefault(s => string.Equals(s.SlotKey, slotKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"The type has no Asset Slot '{slotKey}'.", nameof(slotKey));

        var tempRoot = Path.GetFullPath(Path.Combine(_mediaPaths.GetRoot(), TempFolder));
        var source = Path.GetFullPath(upload.DiskPath);
        if (!source.StartsWith(tempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A staged upload must come from the media root's TempFiles.", nameof(upload));

        draft.StagingFolder ??= Path.Combine(tempRoot, "message-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(draft.StagingFolder);

        // The uploader's staged name already carries a unique prefix, which keeps two uploads of one file name
        // apart here and in the library folder.
        var target = Path.Combine(draft.StagingFolder, Path.GetFileName(source));
        File.Move(source, target, overwrite: true);

        DeleteStaged(slot);
        slot.Staged = upload with { DiskPath = target, FileName = Path.GetFileName(upload.FileName) };
        return Task.CompletedTask;
    }

    /// <summary>Empties a slot: drops any staged upload and unlinks the saved asset when the Message saves.</summary>
    public void ClearSlot(MessageDraft draft, string slotKey)
    {
        var slot = draft.Slots.FirstOrDefault(s => string.Equals(s.SlotKey, slotKey, StringComparison.OrdinalIgnoreCase));
        if (slot is null) return;
        DeleteStaged(slot);
        slot.AssetId = null;
        slot.AssetName = null;
    }

    /// <summary>Cancel: deletes every staged upload of the draft. Nothing was written to the database or the library.</summary>
    public void Discard(MessageDraft draft)
    {
        foreach (var slot in draft.Slots) slot.Staged = null;
        if (draft.StagingFolder is null) return;
        try
        {
            if (Directory.Exists(draft.StagingFolder)) Directory.Delete(draft.StagingFolder, recursive: true);
            draft.StagingFolder = null;
        }
        catch (IOException ex)
        {
            // The temp file sweep removes what is left.
            _logger.LogWarning(ex, "Could not delete the staged Message uploads in {Folder}", draft.StagingFolder);
        }
    }

    /// <summary>
    /// Saves the draft. A new canned Message saves as Pending when its type has an approval chain, otherwise Approved
    /// (MSG-11, MSG-12); a Pending or rejected (Draft) Message re-saves the same way. Under a chain, an edit to an
    /// Approved Message becomes its pending revision, replacing any earlier one, and the Message keeps its approved
    /// content until the chain approves it (MSG-13). With no chain the edit is approved as it saves: when it changes
    /// what screens show (a text or a slot's asset), the Message's RevisionNumber moves on and each of its active or
    /// scheduled Activations gains a shown version (AUD-06), as an approved revision does; a rename alone does neither.
    /// An ASL video an in-place edit replaces leaves its library (INK-10). Whatever waits on the chain is submitted to
    /// its first stage in the same transaction, and the stage's approvers are emailed once it commits. Staged uploads
    /// are ingested into each slot's asset library first, and removed again if the Message itself then fails to save. A
    /// failed save keeps the draft and its staged files so the author can try again. Once saved it logs
    /// <c>message.created</c>, <c>message.edited</c> or <c>message.revision-submitted</c> (AUD-08); a refused or failed
    /// save logs its attempt with that outcome.
    /// </summary>
    public async Task<MessageSaveResult> SaveAsync(MessageDraft draft, CancellationToken ct)
    {
        var attemptKey = draft.MessageId is null ? UserEventActionKeys.MessageCreated : UserEventActionKeys.MessageEdited;
        var attemptTarget = MessagingEvents.Message(draft.MessageId, draft.Name.Trim());
        if (!await CanAuthorAsync())
        {
            await _events.TryWriteAsync(_logger, new UserEvent(attemptKey, attemptTarget, UserEventOutcome.Refused));
            return new MessageSaveResult(MessageSaveOutcome.Forbidden, draft.MessageId);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var type = await LoadTypeAsync(db, draft.MessageTypeId, ct);
        if (type is null) return new MessageSaveResult(MessageSaveOutcome.Gone, draft.MessageId);

        TblMessage? message = null;
        if (draft.MessageId is int id)
        {
            message = await db.TblMessages.Include(m => m.Texts).Include(m => m.Assets).Include(m => m.PendingRevision)
                .FirstOrDefaultAsync(m => m.MessageId == id, ct);
            if (message is null || !IsEditable(message) || message.MessageTypeId != type.MessageTypeId)
                return new MessageSaveResult(MessageSaveOutcome.Gone, draft.MessageId);
        }

        var errors = Validate(draft, type);
        if (errors.Count > 0) return new MessageSaveResult(MessageSaveOutcome.Invalid, draft.MessageId, Errors: errors);

        var (ingested, ingestErrors) = await IngestStagedAsync(draft, type, ct);
        if (ingestErrors.Count > 0)
        {
            await RemoveFromLibraryAsync(ingested.Values.Select(a => a.AssetId));
            return new MessageSaveResult(MessageSaveOutcome.Invalid, draft.MessageId, Errors: ingestErrors);
        }

        int? submitted;
        var before = message is null ? null : ContentOf(message);
        TblMessageRevision? revision = null;
        var approvedInPlace = false;
        List<int> replacedVideos = [];
        try
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var userId = _session.User?.UserId;
            var needsApproval = await HasChainAsync(db, type.MessageTypeId, ct);
            var ingestedBySlot = ingested.ToDictionary(p => p.Key, p => p.Value.AssetId, StringComparer.OrdinalIgnoreCase);
            if (message is null)
            {
                message = new TblMessage
                {
                    MessageTypeId = type.MessageTypeId,
                    IsCanned = true,
                    Status = needsApproval ? MessageStatus.Pending : MessageStatus.Approved,
                    CreatedUtc = now,
                    CreatedByUserId = userId
                };
                db.TblMessages.Add(message);
                ApplyContent(message, draft, type, ingestedBySlot);
            }
            else if (message.Status == MessageStatus.Approved && needsApproval)
            {
                // Screens keep showing the Message's own content; the edit waits beside it.
                if (message.PendingRevision is not null) db.TblMessageRevisions.Remove(message.PendingRevision);
                revision = Revision(message, draft, type, ingestedBySlot, now, userId);
                db.TblMessageRevisions.Add(revision);
            }
            else
            {
                var wasApproved = message.Status == MessageStatus.Approved;
                if (!wasApproved) message.Status = needsApproval ? MessageStatus.Pending : MessageStatus.Approved;
                message.ModifiedUtc = now;
                message.ModifiedByUserId = userId;
                var videosBefore = AslVideos.IdsIn(message.Assets);
                ApplyContent(message, draft, type, ingestedBySlot);
                replacedVideos = AslVideos.Replaced(videosBefore, AslVideos.IdsIn(message.Assets));
                // With no chain an edit to an Approved Message is approved as it saves (MSG-11): an approved revision,
                // provided screens would show something new; an unchanged save must not publish a new shown version.
                approvedInPlace = wasApproved && !before!.ShowsTheSameAs(ContentOf(message));
                if (approvedInPlace) message.RevisionNumber++;
                // With no chain left on the type an edit saves in place, and an earlier pending edit is moot.
                if (message.PendingRevision is not null) db.TblMessageRevisions.Remove(message.PendingRevision);
            }

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);

            // The library names an ingested file by its unique staged name; the asset keeps the name the user uploaded.
            foreach (var (_, asset) in ingested)
                await db.TblAssets.Where(a => a.AssetId == asset.AssetId)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.AssetName, asset.FileName), ct);

            // After the assets are named, so a new file is recorded under the name the user uploaded.
            if (approvedInPlace)
            {
                await _versions.AddRevisionAsync(db, message, now, ct);
                await DeliveryVersion.BumpAsync(db, ct);
            }

            await AslVideos.DeleteReplacedAsync(db, message.MessageId, replacedVideos, _files, ct);
            await db.SaveChangesAsync(ct);

            if (needsApproval)
            {
                submitted = await MessageApprovals.SubmitAsync(db, message.MessageId, type.MessageTypeId, now, ct);
            }
            else
            {
                submitted = null;
                await MessageApprovals.WithdrawAsync(db, message.MessageId, now, ct);
            }

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await RemoveFromLibraryAsync(ingested.Values.Select(a => a.AssetId));
            if (ex is not OperationCanceledException)
                await _events.TryWriteAsync(_logger, new UserEvent(attemptKey, attemptTarget, UserEventOutcome.Failed));
            throw;
        }

        Discard(draft);
        await _events.TryWriteAsync(_logger, SavedEvent(type, message, before, revision));
        if (submitted is int requestId && _emails is not null)
            await _emails.StageReachedAsync(requestId, ct);
        return new MessageSaveResult(MessageSaveOutcome.Saved, message.MessageId, message.Status);
    }

    /// <summary>
    /// The event of a saved Message (AUD-08): a new Message with its type, status and texts; a pending revision with its
    /// id and proposed texts; an edit saved in place with each changed field, old and new.
    /// </summary>
    private static UserEvent SavedEvent(TblMessageType type, TblMessage message, MessageContent? before, TblMessageRevision? revision)
    {
        if (revision is not null)
            return new UserEvent(UserEventActionKeys.MessageRevisionSubmitted, MessagingEvents.Message(message.MessageId, revision.Name),
                Details: new { revisionId = revision.MessageRevisionId, texts = MessagingEvents.Texts(revision.Texts.Select(t => (t.LanguageCode, t.Body))) });

        if (before is null) return MessagingEvents.Created(message, type.Code);

        var target = MessagingEvents.Message(message.MessageId, message.Name);
        var after = ContentOf(message);

        var changes = new UserEventChanges()
            .Field("name", before.Name, after.Name)
            .Field("status", before.Status.ToString(), after.Status.ToString());
        MessagingEvents.ChangedEntries(changes, "texts", before.Texts, after.Texts);
        MessagingEvents.ChangedEntries(changes, "assets", before.Assets, after.Assets);
        return new UserEvent(UserEventActionKeys.MessageEdited, target, Details: changes);
    }

    private static MessageContent ContentOf(TblMessage message) => new(
        message.Name,
        message.Status,
        MessagingEvents.Texts(message.Texts.Select(t => (t.LanguageCode, t.Body))),
        new SortedDictionary<string, object?>(
            message.Assets.ToDictionary(a => a.SlotKey, a => (object?)a.AssetId, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase));


    /// <summary>A Message's content as an edit's event compares it: texts by language and asset ids by slot.</summary>
    private sealed record MessageContent(
        string Name, MessageStatus Status, SortedDictionary<string, object?> Texts, SortedDictionary<string, object?> Assets)
    {
        /// <summary>Whether screens would show the same: the name and status never reach a screen.</summary>
        public bool ShowsTheSameAs(MessageContent other) => SameEntries(Texts, other.Texts) && SameEntries(Assets, other.Assets);

        private static bool SameEntries(SortedDictionary<string, object?> a, SortedDictionary<string, object?> b) =>
            a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var value) && Equals(value, p.Value));
    }

    /// <summary>What a refused retire or delete tells the user (MSG-14).</summary>
    public const string CancelFirst = "This Message is on screen or scheduled. Cancel its Activations on Active Messages first.";

    /// <summary>
    /// Retires the Message: it stays listed with its history but can no longer be edited or sent (MSG-14). Refused
    /// while one of its Activations is active or scheduled.
    /// </summary>
    public Task<MessageRemovalOutcome> RetireAsync(int messageId, CancellationToken ct) =>
        RemoveAsync(messageId, UserEventActionKeys.MessageRetired, (_, message, _, _) =>
        {
            message.Status = MessageStatus.Retired;
            return Task.CompletedTask;
        }, ct);

    /// <summary>
    /// Deletes the Message (a soft delete; its Activations and their history stay) (MSG-14). Refused while one of its
    /// Activations is active or scheduled. In the same transaction its ASL videos leave their library (INK-10): its own
    /// and that of an edit still waiting for approval, which is discarded with its request.
    /// </summary>
    public Task<MessageRemovalOutcome> DeleteAsync(int messageId, CancellationToken ct) =>
        RemoveAsync(messageId, UserEventActionKeys.MessageDeleted, DeleteWithVideosAsync, ct);

    private static async Task DeleteWithVideosAsync(DirectorDbContext db, TblMessage message, DateTime now, CancellationToken ct)
    {
        message.IsDeleted = true;
        await db.Entry(message).Collection(m => m.Assets).LoadAsync(ct);
        await db.Entry(message).Reference(m => m.PendingRevision).LoadAsync(ct);

        var videoIds = AslVideos.RemoveFrom(message.Assets);
        if (message.PendingRevision is { } revision)
        {
            await db.Entry(revision).Collection(r => r.Assets).LoadAsync(ct);
            videoIds.AddRange(AslVideos.IdsIn(revision.Assets));
            db.TblMessageRevisions.Remove(revision);
        }

        await MessageApprovals.WithdrawAsync(db, message.MessageId, now, ct);
        await AslVideos.DeleteAsync(db, message.MessageId, videoIds, now, ct);
    }

    /// <summary>
    /// Retires or deletes in one transaction, then logs <paramref name="actionKey"/> (AUD-08); a refusal for the
    /// author key or while an Activation is active or scheduled, or a failed save, logs with that outcome.
    /// </summary>
    private async Task<MessageRemovalOutcome> RemoveAsync(int messageId, string actionKey,
        Func<DirectorDbContext, TblMessage, DateTime, CancellationToken, Task> remove, CancellationToken ct)
    {
        if (_session.User is not { } user || !await CanAuthorAsync())
        {
            await _events.TryWriteAsync(_logger, new UserEvent(actionKey, MessagingEvents.Message(messageId, null), UserEventOutcome.Refused));
            return MessageRemovalOutcome.Forbidden;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var message = await db.TblMessages.FirstOrDefaultAsync(m => m.MessageId == messageId && !m.IsDeleted, ct);
        if (message is null) return MessageRemovalOutcome.Gone;

        var removal = new UserEvent(actionKey, MessagingEvents.Message(messageId, message.Name));
        var now = _clock.GetUtcNow().UtcDateTime;
        if (await db.TblMessageActivations.Where(ActivationState.ActiveOrScheduled(now)).AnyAsync(a => a.MessageId == messageId, ct))
        {
            await _events.TryWriteAsync(_logger, removal with { Outcome = UserEventOutcome.Refused });
            return MessageRemovalOutcome.Busy;
        }

        await remove(db, message, now, ct);
        message.ModifiedUtc = now;
        message.ModifiedByUserId = user.UserId;
        await _events.FailureLoggedAsync(_logger, removal, async () =>
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        await _events.TryWriteAsync(_logger, removal);
        return MessageRemovalOutcome.Done;
    }

    /// <summary>
    /// Writes the draft's name, its text in each of the type's languages and its slot assets onto
    /// <paramref name="message"/>. Texts in a language the type has since dropped are left as they are.
    /// </summary>
    private static void ApplyContent(TblMessage message, MessageDraft draft, TblMessageType type,
        IReadOnlyDictionary<string, int> ingestedBySlot)
    {
        message.Name = draft.Name.Trim();

        foreach (var language in type.Languages)
        {
            var body = draft.Texts.First(t => string.Equals(t.LanguageCode, language.LanguageCode, StringComparison.OrdinalIgnoreCase)).Body;
            var row = message.Texts.FirstOrDefault(t => string.Equals(t.LanguageCode, language.LanguageCode, StringComparison.OrdinalIgnoreCase));
            if (row is null) message.Texts.Add(row = new TblMessageText { LanguageCode = language.LanguageCode });
            row.Body = body;
        }

        foreach (var slot in type.AssetLibraries)
        {
            var drafted = draft.Slots.FirstOrDefault(s => string.Equals(s.SlotKey, slot.SlotKey, StringComparison.OrdinalIgnoreCase));
            int? assetId = ingestedBySlot.TryGetValue(slot.SlotKey, out var fresh) ? fresh : drafted?.AssetId;
            var row = message.Assets.FirstOrDefault(a => string.Equals(a.SlotKey, slot.SlotKey, StringComparison.OrdinalIgnoreCase));
            if (assetId is null)
            {
                if (row is not null) message.Assets.Remove(row);
            }
            else if (row is null) message.Assets.Add(new TblMessageAsset { SlotKey = slot.SlotKey, AssetId = assetId.Value });
            else row.AssetId = assetId.Value;
        }
    }

    /// <summary>
    /// The draft as the pending revision of an Approved <paramref name="message"/>: its name, a text in each of the
    /// type's languages and the asset of every filled slot, with the Message's colours, which authoring does not edit.
    /// </summary>
    private static TblMessageRevision Revision(TblMessage message, MessageDraft draft, TblMessageType type,
        IReadOnlyDictionary<string, int> ingestedBySlot, DateTime now, int? userId) =>
        new()
        {
            MessageId = message.MessageId,
            Name = draft.Name.Trim(),
            TextColor = message.TextColor,
            BackgroundColor = message.BackgroundColor,
            SubmittedUtc = now,
            SubmittedByUserId = userId,
            Texts = type.Languages.Select(l => new TblMessageRevisionText
            {
                LanguageCode = l.LanguageCode,
                Body = draft.Texts.First(t => string.Equals(t.LanguageCode, l.LanguageCode, StringComparison.OrdinalIgnoreCase)).Body
            }).ToList(),
            Assets = type.AssetLibraries
                .Select(slot =>
                {
                    var drafted = draft.Slots.FirstOrDefault(s => string.Equals(s.SlotKey, slot.SlotKey, StringComparison.OrdinalIgnoreCase));
                    int? assetId = ingestedBySlot.TryGetValue(slot.SlotKey, out var fresh) ? fresh : drafted?.AssetId;
                    return assetId is int id ? new TblMessageRevisionAsset { SlotKey = slot.SlotKey, AssetId = id } : null;
                })
                .OfType<TblMessageRevisionAsset>()
                .ToList()
        };

    /// <summary>
    /// Ingests each staged upload into its slot's asset library. Message approval covers the asset (MSG-11), so the
    /// library's own content approval is not asked for. Returns what was ingested, by slot key, and any refusal.
    /// </summary>
    private async Task<(Dictionary<string, (int AssetId, string FileName)> Ingested, Dictionary<string, string> Errors)>
        IngestStagedAsync(MessageDraft draft, TblMessageType type, CancellationToken ct)
    {
        var ingested = new Dictionary<string, (int, string)>(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string>();
        foreach (var slot in draft.Slots.Where(s => s.Staged is not null))
        {
            var library = type.AssetLibraries.FirstOrDefault(s => string.Equals(s.SlotKey, slot.SlotKey, StringComparison.OrdinalIgnoreCase));
            if (library?.Constraint.OwningLocationId is not int locationId)
            {
                errors[SlotField(slot.SlotKey)] = "This slot no longer has an asset library.";
                break;
            }

            AssetIngestResult result;
            try
            {
                await using var content = File.OpenRead(slot.Staged!.DiskPath);
                result = await _library.IngestAsync(new AssetIngestRequest
                {
                    LocationId = locationId,
                    ConstraintId = library.ConstraintId,
                    FileName = Path.GetFileName(slot.Staged.DiskPath),
                    Content = content,
                    UploaderUserId = _session.User?.UserId ?? 0,
                    UploadingAsUserGroupId = slot.Staged.UploadingAsUserGroupId,
                    EnableContentApproval = false,
                    Source = "message-authoring"
                }, ct);
            }
            catch (FileNotFoundException)
            {
                result = new AssetIngestResult { Error = "The upload expired. Upload the file again." };
            }

            if (!result.Success)
            {
                errors[SlotField(slot.SlotKey)] = result.Error ?? "The file could not be added to the asset library.";
                break;
            }
            ingested[slot.SlotKey] = (result.AssetId, slot.Staged.FileName);
        }
        return (ingested, errors);
    }

    private async Task RemoveFromLibraryAsync(IEnumerable<int> assetIds)
    {
        foreach (var assetId in assetIds)
        {
            try { await _library.DeleteAssetAsync(assetId); }
            catch (Exception ex) { _logger.LogError(ex, "Could not remove asset {AssetId} after a failed Message save", assetId); }
        }
    }

    /// <summary>
    /// Every language of the type needs its text within the type's limit, every required slot a file, and the Message
    /// a name: the author names every canned Message (MSG-03 to MSG-05).
    /// </summary>
    private static Dictionary<string, string> Validate(MessageDraft draft, TblMessageType type)
    {
        var errors = new Dictionary<string, string>();

        var name = draft.Name.Trim();
        if (name.Length == 0) errors[NameField] = "Name the Message.";
        else if (name.Length > NameLength) errors[NameField] = $"Use up to {NameLength} characters.";

        foreach (var language in type.Languages.OrderBy(l => l.SortOrder))
        {
            var body = draft.Texts.FirstOrDefault(t => string.Equals(t.LanguageCode, language.LanguageCode, StringComparison.OrdinalIgnoreCase))?.Body;
            if (string.IsNullOrWhiteSpace(body)) errors[TextField(language.LanguageCode)] = "Type the Message in this language.";
            else if (body.Length > type.MaxCharacters) errors[TextField(language.LanguageCode)] = $"Up to {type.MaxCharacters} characters.";
        }

        foreach (var slot in type.AssetLibraries.Where(s => s.IsRequired))
        {
            var drafted = draft.Slots.FirstOrDefault(s => string.Equals(s.SlotKey, slot.SlotKey, StringComparison.OrdinalIgnoreCase));
            if (drafted?.IsFilled != true) errors[SlotField(slot.SlotKey)] = "This slot needs a file.";
        }

        return errors;
    }

    private static bool IsEditable(TblMessage message) =>
        message.IsCanned && !message.IsDeleted && message.Status != MessageStatus.Retired;

    private void DeleteStaged(MessageSlotDraft slot)
    {
        if (slot.Staged is null) return;
        try { File.Delete(slot.Staged.DiskPath); }
        catch (IOException ex) { _logger.LogWarning(ex, "Could not delete the staged upload {Path}", slot.Staged.DiskPath); }
        slot.Staged = null;
    }

    private async Task<IReadOnlyList<MessageTextDraft>> TextsAsync(TblMessageType type, IReadOnlyDictionary<string, string>? codeBodies)
    {
        var names = await _languages.NameAsync(type.Languages.OrderBy(l => l.SortOrder).Select(l => l.LanguageCode));
        return names.Select(n => new MessageTextDraft
        {
            LanguageCode = n.Code,
            LanguageName = n.Name,
            Body = codeBodies is not null && codeBodies.TryGetValue(n.Code, out var body) ? body : ""
        }).ToList();
    }

    private static IReadOnlyList<MessageSlotDraft> Slots(TblMessageType type, IReadOnlyList<HeldAsset> assets) =>
        type.AssetLibraries.OrderBy(s => s.SortOrder).Select(s =>
        {
            var held = assets.FirstOrDefault(a => string.Equals(a.SlotKey, s.SlotKey, StringComparison.OrdinalIgnoreCase));
            return new MessageSlotDraft
            {
                SlotKey = s.SlotKey,
                IsRequired = s.IsRequired,
                ConstraintId = s.ConstraintId,
                LibraryLocationId = s.Constraint.OwningLocationId,
                ExpectedWidth = s.Constraint.ResolutionWidth,
                ExpectedHeight = s.Constraint.ResolutionHeight,
                ResolutionMode = s.Constraint.ResolutionMode,
                AssetId = held?.AssetId,
                AssetName = held?.AssetName
            };
        }).ToList();

    /// <summary>
    /// Whether the type's Messages go through approval: it has an approval chain that is switched on (MSG-11). The
    /// chain itself is configured on the type's Access.
    /// </summary>
    private static Task<bool> HasChainAsync(DirectorDbContext db, int messageTypeId, CancellationToken ct) =>
        db.TblApprovalChains.AnyAsync(c => c.MessageTypeId == messageTypeId && c.IsEnabled, ct);

    private sealed record HeldAsset(string SlotKey, int AssetId, string? AssetName);

    private static Task<TblMessageType?> LoadTypeAsync(DirectorDbContext db, int messageTypeId, CancellationToken ct) =>
        db.TblMessageTypes.AsNoTracking()
            .Include(t => t.Languages)
            .Include(t => t.AssetLibraries).ThenInclude(s => s.Constraint)
            .FirstOrDefaultAsync(t => t.MessageTypeId == messageTypeId && !t.IsDeleted, ct);
}
