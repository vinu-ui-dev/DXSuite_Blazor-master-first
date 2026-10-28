using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using DXSuite.Director.Services.Messaging;
using DXSuite.Director.Tests.TwoHost;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.CustomerDeviations;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Tests;

/// <summary>
/// What the Message approval tests (B-34) run against: the <see cref="MessageTypeDatabase"/> with approvers and their
/// groups, type chains with stages, Approved Messages to edit, a clock the test moves and a mail server that records
/// what it is given.
/// </summary>
internal sealed class MessageApprovalWorld
{
    public const int Ivy = 501, Jon = 502, Kim = 503, Disabled = 504;

    private readonly MessageTypeDatabase _db;

    public MessageApprovalWorld(MessageTypeDatabase db, AuthoringWorld authoring)
    {
        _db = db;
        Authoring = authoring;
    }

    public AuthoringWorld Authoring { get; }

    public ManualClock Clock { get; } = new(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));

    public RecordingEmailService Mail { get; } = new();

    public RecordingAssetApprovals AssetApprovals { get; } = new();

    /// <summary>Approver accounts: Ivy, Jon and Kim, and a disabled account that is never emailed or listed.</summary>
    public async Task SeedApproversAsync()
    {
        await using var db = _db.Director();
        await db.Database.ExecuteSqlRawAsync($"""
            ALTER TABLE tblUser NOCHECK CONSTRAINT ALL;
            IF NOT EXISTS (SELECT 1 FROM tblUser WHERE UserId = {Ivy})
            BEGIN
                SET IDENTITY_INSERT tblUser ON;
                INSERT INTO tblUser (UserId, RoleId, FriendlyName, UserPrincipalName, PulseAccess, IsDisabled)
                VALUES ({Ivy}, 3, 'Ivy Approver', 'ivy@gtaa.test', 0, 0), ({Jon}, 3, 'Jon Approver', 'jon@gtaa.test', 0, 0),
                       ({Kim}, 3, 'Kim Approver', 'kim@gtaa.test', 0, 0), ({Disabled}, 3, 'Gone Approver', 'gone@gtaa.test', 0, 1),
                       ({AuthoringWorld.UserId}, 3, 'Author', 'author@gtaa.test', 0, 0);
                SET IDENTITY_INSERT tblUser OFF;
            END
            DELETE FROM tblApprovalReminder; DELETE FROM tblApprovalRequest;
            """);
    }

    /// <summary>A group with exactly the given members, made the first time a test names it.</summary>
    public async Task<int> GroupAsync(string name, params int[] memberIds)
    {
        await using var db = _db.Director();
        var group = await db.TblUserGroups.SingleOrDefaultAsync(g => g.GroupName == name);
        if (group is null)
        {
            group = new TblUserGroup { GroupName = name, IsActive = true };
            db.TblUserGroups.Add(group);
            await db.SaveChangesAsync();
        }
        db.TblUserGroupMemberships.RemoveRange(db.TblUserGroupMemberships.Where(m => m.UserGroupId == group.UserGroupId));
        foreach (var userId in memberIds)
            db.TblUserGroupMemberships.Add(new TblUserGroupMembership { UserGroupId = group.UserGroupId, UserId = userId, AddedDate = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return group.UserGroupId;
    }

    /// <summary>The type's chain with one stage per entry (THEN between them), each stage's groups joined by its operator.</summary>
    public async Task<int> ChainAsync(int messageTypeId, bool email = false, int reminderHours = 24, params (string Operator, int[] Groups)[] stages)
    {
        await using var db = _db.Director();
        var chain = new TblApprovalChain
        {
            MessageTypeId = messageTypeId, IsEnabled = true, CreateDate = DateTime.UtcNow, EmailApprovers = email,
            ReminderIntervalHours = reminderHours,
            Stages = stages.Select((s, i) => new TblApprovalStage
            {
                StageOrder = i, IntraStageOperator = s.Operator,
                StageGroups = s.Groups.Select((g, j) => new TblApprovalStageGroup { UserGroupId = g, SortOrder = j }).ToList()
            }).ToList()
        };
        db.TblApprovalChains.Add(chain);
        await db.SaveChangesAsync();
        return chain.ApprovalChainId;
    }

    /// <summary>An Approved canned Message with one text per language, shown nowhere.</summary>
    public async Task<int> ApprovedMessageAsync(int messageTypeId, string name, params (string Code, string Body)[] texts)
    {
        await using var db = _db.Director();
        var message = new TblMessage
        {
            MessageTypeId = messageTypeId, Name = name, IsCanned = true, Status = MessageStatus.Approved, CreatedUtc = DateTime.UtcNow,
            Texts = texts.Select(t => new TblMessageText { LanguageCode = t.Code, Body = t.Body }).ToList()
        };
        db.TblMessages.Add(message);
        await db.SaveChangesAsync();
        return message.MessageId;
    }

    public ApprovalEmails Emails() => new(_db.Factory, Mail);

    /// <summary>
    /// An Activation of the Message with its first shown version, as a send writes them: <paramref name="start"/> to
    /// <paramref name="end"/> (null until cancelled), cancelled when given, showing <paramref name="texts"/> (the shown
    /// version's JSON object of texts by language).
    /// </summary>
    public async Task<int> ActivationAsync(int messageId, DateTime start, DateTime? end, DateTime? cancelled = null, int? priorityOverride = null,
        string texts = "{}")
    {
        await using var db = _db.Director();
        var type = await db.TblMessages.Where(m => m.MessageId == messageId).Select(m => m.MessageType).SingleAsync();
        var activation = new TblMessageActivation
        {
            MessageId = messageId, StartUtc = start, EndUtc = end, CancelledUtc = cancelled, ActivatedUtc = start,
            PriorityOverride = priorityOverride, Source = MessageActivationSource.DirectorAdmin,
            Versions =
            [
                new TblMessageActivationVersion
                {
                    Sequence = 1, Reason = ShownVersionReason.FirstSend, MessageTypeId = type.MessageTypeId, MessageTypeCode = type.Code,
                    EffectiveRank = priorityOverride ?? type.Priority, Texts = texts, AssetSlots = "[]", SentUtc = start,
                },
            ],
        };
        db.TblMessageActivations.Add(activation);
        await db.SaveChangesAsync();
        return activation.MessageActivationId;
    }

    /// <summary>A new display in a new location, made a screen of the Activation as a send freezes it; returns the display.</summary>
    public async Task<int> ShowOnNewDisplayAsync(int activationId)
    {
        await using var db = _db.Director();
        var displayId = (await db.Database.SqlQueryRaw<int>("""
            ALTER TABLE tblLocation NOCHECK CONSTRAINT ALL;
            ALTER TABLE tblDisplay NOCHECK CONSTRAINT ALL;
            INSERT INTO tblLocation (LocationTypeId, LocationGroupId, LocationName, EnableShuffle, EnableFade, EnableContentApproval)
            VALUES (1, 1, 'Terminal 1', 0, 1, 0);
            INSERT INTO tblDisplay (LocationId, DisplayName, DisplayMode, DisplayTypeId, ConstraintId, UseClock, UseVideoMask, EnableFade, EnableContentApproval)
            OUTPUT INSERTED.DisplayId AS Value
            VALUES (SCOPE_IDENTITY(), 'Gate A1', 0, 1, 1, 0, 0, 1, 0);
            """).ToListAsync()).Single();
        db.TblMessageActivationTargets.Add(new TblMessageActivationTarget
        {
            MessageActivationId = activationId, DisplayId = displayId, DisplayName = "Gate A1", LocationName = "Terminal 1",
        });
        await db.SaveChangesAsync();
        return displayId;
    }

    /// <summary>An Activation's shown versions, oldest first.</summary>
    public async Task<List<TblMessageActivationVersion>> VersionsAsync(int activationId)
    {
        await using var db = _db.Director();
        return await db.TblMessageActivationVersions.AsNoTracking()
            .Where(v => v.MessageActivationId == activationId).OrderBy(v => v.Sequence).ToListAsync();
    }

    /// <summary>
    /// A location with a one-stage chain of <paramref name="groupId"/> and an asset the author uploaded to it, pending
    /// approval: the asset approval to submit.
    /// </summary>
    public async Task<(int LocationId, int AssetApprovalId)> UploadToLocationChainAsync(string name, int groupId, bool email = false, bool autoApproveMembers = false)
    {
        var constraint = await _db.AddLibraryConstraintAsync(name);
        var location = (await _db.LibraryLocationOfAsync(constraint))!.Value;
        await using var db = _db.Director();
        db.TblApprovalChains.Add(new TblApprovalChain
        {
            LocationId = location, IsEnabled = true, IsDefault = true, EmailApprovers = email, AutoApproveMembers = autoApproveMembers,
            CreateDate = DateTime.UtcNow,
            Stages = [new TblApprovalStage { StageOrder = 0, IntraStageOperator = "OR", StageGroups = [new TblApprovalStageGroup { UserGroupId = groupId }] }]
        });
        await db.SaveChangesAsync();
        var approvalId = (await db.Database.SqlQueryRaw<int>("""
            ALTER TABLE tblAsset NOCHECK CONSTRAINT ALL;
            INSERT INTO tblAsset (LocationId, AssetName, AssetPath, AssetGroupId, AssetTypeId, AssetHeight, AssetWidth,
                ConstraintId, Duration, EnableHealthMonitoring, SortOrder, CreateDate, IsDeleted, ContentPackageId, UploadedByUserId)
            VALUES ({0}, 'lobby.png', '/MediaAssets/lobby.png', 1, 1, 0, 0, {1}, 0, 0, 0, SYSUTCDATETIME(), 0, NEWID(), {2});
            INSERT INTO tblAssetApproval (AssetId, ApprovalStatus, UploadedByUserId, SubmittedDate)
            OUTPUT INSERTED.AssetApprovalId AS Value
            VALUES (SCOPE_IDENTITY(), 0, {2}, SYSUTCDATETIME());
            """, location, constraint, AuthoringWorld.UserId).ToListAsync()).Single();
        return (location, approvalId);
    }

    /// <summary>The asset chain service on <paramref name="db"/>, emailing through this world's mail server.</summary>
    public ApprovalChainService ChainService(DirectorDbContext db) =>
        new(db, AssetApprovals, new SilentBells(), NullLogger<ApprovalChainService>.Instance, null!,
            new SilentInvalidator(), Emails());

    /// <summary>The approval queue and decisions as <paramref name="userId"/> sees them.</summary>
    public MessageApprovals Approvals(int userId, RecordingEventLog? events = null) =>
        new(_db.Factory, new KeySession { User = new Person(userId) }, Languages(), Clock, events ?? new RecordingEventLog(userId), Emails(),
            Authoring.Versions(), Authoring.Files(Clock));

    /// <summary>A Message Type's Access dialog chain editor, as an administrator.</summary>
    public MessageApprovalChain Chains() =>
        new(_db.Factory, new KeySession(PermissionKeys.ConfigTabMessageTypes) { User = new Person(Ivy) },
            new AccountKindSignInTests.RecordingUserEventLog(), Microsoft.Extensions.Logging.Abstractions.NullLogger<MessageApprovalChain>.Instance,
            Clock, Emails());

    /// <summary>The authoring service of <see cref="AuthoringWorld"/>, on this world's clock and mail server.</summary>
    public MessageAuthoring Authors() =>
        new(_db.Factory, Languages(), Authoring.Session(PermissionKeys.MessagesAuthor), Authoring.Library, Authoring.MediaPaths,
            Clock, Authoring.Events, Authoring.Versions(), Authoring.Files(Clock), emails: Emails());

    /// <summary>Opens, edits and saves a Message's English text as the author.</summary>
    public async Task<MessageSaveResult> EditAsync(int messageId, string englishText, string? name = null)
    {
        var authoring = Authors();
        var draft = (await authoring.OpenAsync(messageId, default))!;
        draft.Texts.First(t => t.LanguageCode == "eng").Body = englishText;
        if (name is not null) draft.Name = name;
        return await authoring.SaveAsync(draft, default);
    }

    public async Task<MessageQueueItem> OnlyQueueItemAsync(int userId) => Assert.Single(await Approvals(userId).MyQueueAsync(default));

    private static MessageLanguageCatalogue Languages() =>
        new(Options.Create(new D02GtaaMessagingOptions()), new FakeLanguages());

    private sealed class Person(int userId) : ICurrentUser
    {
        public int UserId => userId;
        public string UserPrincipalName => $"user{userId}@gtaa.test";
        public string? FriendlyName => "User " + userId;
        public string? GivenName => null;
        public string? Surname => null;
        public bool IsDisabled => false;
        public int RoleId => 3;
        public string? ExtendedData => null;
        public Guid SecurityStamp => Guid.Empty;
    }
}

/// <summary>The asset approval records as the chain sees them: approvals and declines are recorded, nothing else is used.</summary>
internal sealed class RecordingAssetApprovals : IAssetApprovalService
{
    public List<(int ApprovalId, string? Notes)> Approved { get; } = new();

    public Task<bool> ApproveAssetAsync(int approvalId, int reviewerId, string? notes = null)
    {
        Approved.Add((approvalId, notes));
        return Task.FromResult(true);
    }

    public Task<bool> DeclineAssetAsync(int approvalId, int reviewerId, string? notes = null) => Task.FromResult(true);
    public Task<List<TblAssetApproval>> GetPendingApprovalsAsync() => throw new NotSupportedException();
    public Task<List<TblAssetApproval>> GetApprovalsByStatusAsync(Models.Types.ApprovalStatus status) => throw new NotSupportedException();
    public Task<TblAssetApproval> CreateApprovalRequestAsync(int assetId, int userId, string metadataTag) => throw new NotSupportedException();
    public Task<TblAssetApproval?> GetAssetApprovalStatusAsync(int assetId) => throw new NotSupportedException();
    public Task<TblAssetApproval?> GetApprovalByIdAsync(int approvalId) => throw new NotSupportedException();
    public Task<bool> IsAssetApprovedAsync(int assetId) => throw new NotSupportedException();
    public Task<bool> PurgeDeclinedAssetAsync(int assetApprovalId) => throw new NotSupportedException();
    public Task<bool> ResubmitAsync(int assetApprovalId, int userId, string comment) => throw new NotSupportedException();
    public Task<List<int>> GetApprovedAssetIdsForTagAsync(string metadataTag) => throw new NotSupportedException();
    public Task<List<TblAssetApproval>> GetApprovalsByLocationAsync(int locationId) => throw new NotSupportedException();
    public Task<int> BulkApproveExistingAsync(int locationId, int reviewerId) => throw new NotSupportedException();
    public Task WithdrawPendingForAssetAsync(int assetId) => throw new NotSupportedException();
    public string GetApprovalMetadataTag(TblAssetApproval approval) => throw new NotSupportedException();
}

internal sealed class SilentBells : INotificationService
{
    public Task CreateNotificationAsync(int userId, string title, string message, string type, int? referenceId = null, string? referenceUrl = null) => Task.CompletedTask;
    public Task CreateBulkNotificationsAsync(List<int> userIds, string title, string message, string type, int? referenceId = null, string? referenceUrl = null) => Task.CompletedTask;
    public Task<int> GetUnreadCountAsync(int userId) => Task.FromResult(0);
    public Task MarkAsReadAsync(int notificationId, int userId) => Task.CompletedTask;
    public Task MarkAllAsReadAsync(int userId) => Task.CompletedTask;
    public Task<List<NotificationDto>> GetRecentNotificationsAsync(int userId, int count = 20) => Task.FromResult(new List<NotificationDto>());
}

internal sealed class SilentInvalidator : IPublicationInvalidator
{
    public void DisplayChanged(int displayId) { }
    public void DisplayScheduleChanged(int displayId) { }
    public void DisplayRemoved(int displayId) { }
    public void LocationChanged(int locationId) { }
    public void AssetsChanged(IEnumerable<int> locationIds) { }
}

/// <summary>A mail server that keeps every message it is given; <see cref="Configured"/> false stands for no SMTP.</summary>
internal sealed class RecordingEmailService : IEmailService
{
    private readonly object _gate = new();
    private readonly List<EmailMessage> _sent = new();

    public bool Configured { get; set; } = true;

    public IReadOnlyList<EmailMessage> Sent
    {
        get { lock (_gate) return _sent.ToList(); }
    }

    /// <summary>The subjects sent to one address, in order.</summary>
    public IReadOnlyList<string> SubjectsTo(string address) =>
        Sent.Where(m => m.To.Contains(address, StringComparer.OrdinalIgnoreCase)).Select(m => m.Subject).ToList();

    public void Clear()
    {
        lock (_gate) _sent.Clear();
    }

    public Task SendAsync(string toAddress, string subject, string htmlBody) =>
        SendAsync(new EmailMessage([toAddress], subject, htmlBody, ""));

    public Task<bool> IsConfiguredAsync() => Task.FromResult(Configured);

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (!Configured) return Task.FromResult(new EmailSendResult(false, "No SMTP.", NotConfigured: true));
        lock (_gate) _sent.Add(message);
        return Task.FromResult(EmailSendResult.Sent);
    }
}
