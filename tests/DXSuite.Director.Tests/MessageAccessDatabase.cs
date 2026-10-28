using DXSuite.Director.Entities;
using DXSuite.Shared.Entities.Identity;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Tests;

/// <summary>
/// A Director database on LocalDB for the Message access rule tests: two terminals with displays, two live Display
/// Zones and a retired one, an Admin, two other users and a group, over the seeded Message Types. Each test starts with no
/// Message access rules, Messages, User events or type languages, and with the seeded Display Zone membership.
/// </summary>
public sealed class MessageAccessDatabase : IAsyncLifetime
{
    public const int Terminal1 = 11, Terminal3 = 13;
    public const int GateA1 = 101, GateA2 = 102, Lounge = 103, CheckIn1 = 301, CheckIn2 = 302;
    public const int GatesA = 1, CheckIn = 2, OldCheckIn = 3;
    public const int Admin = 1, Ana = 2, Bo = 3;
    public const int Rmu = 5;
    // The model seeds the four built-in Message Types.
    public const int Emergency = 1, Paging = 3, Counter = 4;

    private readonly string _database = $"MessageAccess_{Guid.NewGuid():N}"[..30];

    public string Connection =>
        LocalDb.Connection(_database);

    public DirectorDbContext Director() =>
        new(new DbContextOptionsBuilder<DirectorDbContext>().UseSqlServer(Connection).Options);

    public IDbContextFactory<DirectorDbContext> Factory => new DirectorFactory(Director);

    public async Task InitializeAsync()
    {
        if (!LocalDbFactAttribute.IsAvailable) return;

        await using var db = Director();
        await LocalDb.EnsureCreatedAsync(db.Database);

        // Parents the rules do not read (location types, display types, roles) are left out.
        await db.Database.ExecuteSqlRawAsync($"""
            ALTER TABLE tblLocation NOCHECK CONSTRAINT ALL;
            ALTER TABLE tblDisplay NOCHECK CONSTRAINT ALL;
            ALTER TABLE tblUser NOCHECK CONSTRAINT ALL;
            SET IDENTITY_INSERT tblLocation ON;
            INSERT INTO tblLocation (LocationId, LocationTypeId, LocationGroupId, LocationName, SortOrder, EnableShuffle, EnableFade, EnableContentApproval)
            VALUES ({Terminal3}, 1, 1, 'Terminal 3 Check-in', 1, 0, 1, 0), ({Terminal1}, 1, 1, 'Terminal 1 Gates', 2, 0, 1, 0);
            SET IDENTITY_INSERT tblLocation OFF;
            SET IDENTITY_INSERT tblDisplay ON;
            INSERT INTO tblDisplay (DisplayId, LocationId, DisplayName, DisplayMode, DisplayTypeId, ConstraintId, UseClock, UseVideoMask, EnableFade, EnableContentApproval)
            VALUES ({GateA2}, {Terminal1}, 'Gate A2', 0, 1, 1, 0, 0, 1, 0), ({GateA1}, {Terminal1}, 'Gate A1', 0, 1, 1, 0, 0, 1, 0),
                   ({Lounge}, {Terminal1}, 'Lounge', 0, 1, 1, 0, 0, 1, 0),
                   ({CheckIn1}, {Terminal3}, 'Check-in 1', 0, 1, 1, 0, 0, 1, 0), ({CheckIn2}, {Terminal3}, 'Check-in 2', 0, 1, 1, 0, 0, 1, 0);
            SET IDENTITY_INSERT tblDisplay OFF;
            SET IDENTITY_INSERT tblZone ON;
            INSERT INTO tblZone (ZoneId, ZoneName, SortOrder, RetiredUtc)
            VALUES ({CheckIn}, 'Check-in', 2, NULL), ({GatesA}, 'Gates A', 1, NULL), ({OldCheckIn}, 'Old check-in', 3, '2026-01-01');
            SET IDENTITY_INSERT tblZone OFF;
            INSERT INTO tblZoneDisplay (ZoneId, DisplayId)
            VALUES ({GatesA}, {GateA1}), ({GatesA}, {GateA2}), ({CheckIn}, {CheckIn1}), ({OldCheckIn}, {CheckIn2});
            SET IDENTITY_INSERT tblUser ON;
            INSERT INTO tblUser (UserId, RoleId, FriendlyName, UserPrincipalName, PulseAccess, IsDisabled)
            VALUES ({Admin}, 1, 'Ada Admin', 'ada@gtaa.test', 0, 0), ({Ana}, 3, 'Ana Agent', 'ana@gtaa.test', 0, 0),
                   ({Bo}, 3, 'Bo Duty', 'bo@gtaa.test', 0, 0);
            SET IDENTITY_INSERT tblUser OFF;
            SET IDENTITY_INSERT tblUserGroup ON;
            INSERT INTO tblUserGroup (UserGroupId, GroupName) VALUES ({Rmu}, 'RMU');
            SET IDENTITY_INSERT tblUserGroup OFF;
            INSERT INTO tblUserGroupMembership (UserGroupId, UserId, AddedDate) VALUES ({Rmu}, {Bo}, '2026-01-01');
            """);

        // The User Event Log writer looks severities up; director-seed.sql owns these rows, not the model.
        await db.Database.ExecuteSqlRawAsync("""
            IF NOT EXISTS (SELECT 1 FROM tblEventSeverity)
            BEGIN
                SET IDENTITY_INSERT tblEventSeverity ON;
                INSERT INTO tblEventSeverity (EventSeverityId, SeverityName) VALUES (1, 'INFO'), (2, 'WARNING'), (3, 'ERROR'), (4, 'CRITICAL');
                SET IDENTITY_INSERT tblEventSeverity OFF;
            END
            """);
    }

    public async Task DisposeAsync()
    {
        if (!LocalDbFactAttribute.IsAvailable) return;
        await using var db = Director();
        await db.Database.EnsureDeletedAsync();
    }

    public async Task ResetAsync()
    {
        if (!LocalDbFactAttribute.IsAvailable) return;
        await using var db = Director();
        await db.Database.ExecuteSqlRawAsync($"""
            DELETE FROM tblMessageActivationTarget; DELETE FROM tblMessageActivationZone; DELETE FROM tblMessageActivationVersion;
            DELETE FROM tblMessageActivation; DELETE FROM tblMessageText; DELETE FROM tblMessageAsset; DELETE FROM tblAsset; DELETE FROM tblPendingFileDelete;
            DELETE FROM tblMessage; DELETE FROM tblMessagePermission; DELETE FROM tblEventLog;
            DELETE FROM tblMessageTypeLanguage;
            DELETE FROM tblDevice;
            DELETE FROM tblZoneDisplay;
            INSERT INTO tblZoneDisplay (ZoneId, DisplayId)
            VALUES ({GatesA}, {GateA1}), ({GatesA}, {GateA2}), ({CheckIn}, {CheckIn1}), ({OldCheckIn}, {CheckIn2});
            UPDATE tblMessageType SET DefaultDisplaySeconds = 10, DefaultHideSeconds = 30, MaxCharacters = 160;
            DELETE FROM tblApprovalChain; DELETE FROM tblLocationAccessRule;
            DELETE FROM tblMessageTypeAssetLibrary;
            UPDATE tblMessagingDeliveryState SET NudgedVersion = NULL, NudgedETag = NULL, NudgedUtc = NULL;
            """);
    }

    /// <summary>A group with exactly the given members, made the first time a test names it.</summary>
    public async Task<int> AddGroupAsync(string name, params int[] memberIds)
    {
        await using var db = Director();
        var group = await db.TblUserGroups.SingleOrDefaultAsync(g => g.GroupName == name);
        if (group is null)
        {
            group = new TblUserGroup { GroupName = name };
            db.TblUserGroups.Add(group);
            await db.SaveChangesAsync();
        }
        db.TblUserGroupMemberships.RemoveRange(db.TblUserGroupMemberships.Where(m => m.UserGroupId == group.UserGroupId));
        foreach (var userId in memberIds)
            db.TblUserGroupMemberships.Add(new TblUserGroupMembership { UserGroupId = group.UserGroupId, UserId = userId, AddedDate = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return group.UserGroupId;
    }

    /// <summary>A Message access rule for a user (<paramref name="userId"/>) or a group; a null zone is every Display Zone.</summary>
    public async Task<int> AddRuleAsync(int messageTypeId, int? zoneId, bool canFreeText = false, int? userId = null, int? groupId = null)
    {
        await using var db = Director();
        var rule = new TblMessagePermission
        {
            MessageTypeId = messageTypeId, ZoneId = zoneId, CanFreeText = canFreeText, UserId = userId, UserGroupId = groupId,
        };
        db.TblMessagePermissions.Add(rule);
        await db.SaveChangesAsync();
        return rule.MessagePermissionId;
    }

    /// <summary>Gives a Message Type its languages, in the order given.</summary>
    public async Task SetLanguagesAsync(int messageTypeId, params string[] codes)
    {
        await using var db = Director();
        await db.TblMessageTypeLanguages.Where(l => l.MessageTypeId == messageTypeId).ExecuteDeleteAsync();
        for (var i = 0; i < codes.Length; i++)
            db.TblMessageTypeLanguages.Add(new TblMessageTypeLanguage { MessageTypeId = messageTypeId, LanguageCode = codes[i], SortOrder = i + 1 });
        await db.SaveChangesAsync();
    }

    /// <summary>An Asset Slot on the type. Its constraint is not read, so none is made.</summary>
    public async Task AddSlotAsync(int messageTypeId, string slotKey, bool required)
    {
        await using var db = Director();
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE tblMessageTypeAssetLibrary NOCHECK CONSTRAINT ALL;
            INSERT INTO tblMessageTypeAssetLibrary (MessageTypeId, ConstraintId, SlotKey, IsRequired, SortOrder) VALUES ({0}, 1, {1}, {2}, 1);
            """, messageTypeId, slotKey, required);
    }

    /// <summary>Makes the Display Zone hold exactly these displays; <see cref="ResetAsync"/> puts the seeded ones back.</summary>
    public async Task SetZoneDisplaysAsync(int zoneId, params int[] displayIds)
    {
        await using var db = Director();
        await db.TblZoneDisplays.Where(z => z.ZoneId == zoneId).ExecuteDeleteAsync();
        db.TblZoneDisplays.AddRange(displayIds.Select(id => new TblZoneDisplay { ZoneId = zoneId, DisplayId = id }));
        await db.SaveChangesAsync();
    }

    /// <summary>A canned Message of the type with an English text, in the given status.</summary>
    public async Task<int> AddMessageAsync(int messageTypeId, MessageStatus status = MessageStatus.Approved, string name = "Gate change")
    {
        await using var db = Director();
        var message = new TblMessage
        {
            MessageTypeId = messageTypeId, Name = name, IsCanned = true, Status = status, CreatedUtc = DateTime.UtcNow,
            Texts = [new TblMessageText { LanguageCode = "eng", Body = name }],
        };
        db.TblMessages.Add(message);
        await db.SaveChangesAsync();
        return message.MessageId;
    }

    /// <summary>The shown version writer for sends of Messages with no assets, whose media root is never read.</summary>
    public static DXSuite.Director.Services.Messaging.ShownVersions NoAssetVersions() =>
        new((DXSuite.Shared.Multitenancy.ITenantMediaPathResolver)ThrowingProxy.For(typeof(DXSuite.Shared.Multitenancy.ITenantMediaPathResolver)),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DXSuite.Director.Services.Messaging.ShownVersions>.Instance);

    /// <summary>Another language's text of a Message.</summary>
    public async Task AddTextAsync(int messageId, string languageCode, string body)
    {
        await using var db = Director();
        db.TblMessageTexts.Add(new TblMessageText { MessageId = messageId, LanguageCode = languageCode, Body = body });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// An asset in the Message's Asset Slot, stored at <paramref name="assetPath"/> under the name the user uploaded. Its
    /// location and library are not read, so none are made.
    /// </summary>
    public async Task<int> AddMessageAssetAsync(int messageId, string slotKey, string fileName, string assetPath)
    {
        await using var db = Director();
        var assetId = (await db.Database.SqlQueryRaw<int>("""
            ALTER TABLE tblAsset NOCHECK CONSTRAINT ALL;
            INSERT INTO tblAsset (LocationId, AssetName, AssetPath, AssetGroupId, AssetTypeId, AssetHeight, AssetWidth,
                ConstraintId, Duration, EnableHealthMonitoring, SortOrder, CreateDate, IsDeleted, ContentPackageId)
            OUTPUT INSERTED.AssetId AS Value
            VALUES (1, {0}, {1}, 1, 2, 0, 0, 1, 0, 0, 0, SYSUTCDATETIME(), 0, NEWID());
            """, fileName, assetPath).ToListAsync()).Single();
        db.TblMessageAssets.Add(new TblMessageAsset { MessageId = messageId, SlotKey = slotKey, AssetId = assetId });
        await db.SaveChangesAsync();
        return assetId;
    }

    /// <summary>A device attached to a display, carrying an IANA time zone. Displays with none are GTAA's reference displays.</summary>
    public async Task AddDeviceAsync(string deviceId, int displayId, string timeZone)
    {
        await using var db = Director();
        db.TblDevices.Add(new TblDevice
        {
            DeviceId = deviceId, CustomerId = "dev", DisplayId = displayId, SerialNumber = deviceId, Ipaddress = "127.0.0.1",
            Macaddress = "", Osversion = "9.0", Heartbeat = DateTime.UtcNow, CreateDate = DateTime.UtcNow, TimeZone = timeZone,
            TimeZoneSource = "Operator",
        });
        await db.SaveChangesAsync();
    }

    /// <summary>A one-off Message of the type, as a send saves it: not canned, Approved, named by its text.</summary>
    public async Task<int> AddOneOffAsync(int messageTypeId, string text)
    {
        await using var db = Director();
        var message = new TblMessage
        {
            MessageTypeId = messageTypeId, Name = text, IsCanned = false, Status = MessageStatus.Approved, CreatedUtc = DateTime.UtcNow,
            Texts = [new TblMessageText { LanguageCode = "eng", Body = text }],
        };
        db.TblMessages.Add(message);
        await db.SaveChangesAsync();
        return message.MessageId;
    }

    /// <summary>
    /// An Activation of the Message sent by <paramref name="byUserId"/>: one frozen row per display, through
    /// <paramref name="zoneId"/> when given (a Display Zone send), else a one-display send. <paramref name="alsoPicked"/>
    /// are further Display Zones picked at send that reached no display of their own. <paramref name="timing"/>, when
    /// given, is a scheduled send's times and replaces the start and end.
    /// </summary>
    public async Task<int> AddActivationAsync(int messageId, DateTime startUtc, DateTime? endUtc, int[] displayIds,
        int? zoneId = null, DateTime? cancelledUtc = null, int? byUserId = null, DateTime? sentUtc = null,
        DXSuite.Director.Services.Messaging.ResolvedTiming? timing = null, int[]? alsoPicked = null)
    {
        await using var db = Director();
        // Screen rows keep the display's and location's names as a send freezes them.
        var names = await db.TblDisplays.Where(d => displayIds.Contains(d.DisplayId))
            .Select(d => new { d.DisplayId, d.DisplayName, d.LocationId, d.Location.LocationName })
            .ToDictionaryAsync(d => d.DisplayId, d => (d.DisplayName, LocationName: d.LocationName ?? $"Location {d.LocationId}"));
        var activation = new TblMessageActivation
        {
            MessageId = messageId, StartUtc = startUtc, EndUtc = endUtc, CancelledUtc = cancelledUtc,
            Source = MessageActivationSource.DirectorAdmin, ActivatedByUserId = byUserId, ActivatedUtc = sentUtc ?? startUtc,
            Targets = displayIds.Select(d => new TblMessageActivationTarget
            {
                DisplayId = d, ZoneId = zoneId, DisplayName = names[d].DisplayName, LocationName = names[d].LocationName,
            }).ToList(),
            ChosenZones = (zoneId is { } picked ? [picked, .. alsoPicked ?? []] : alsoPicked ?? [])
                .Select(id => new TblMessageActivationZone { ZoneId = id }).ToList(),
        };
        timing?.ApplyTo(activation);
        db.TblMessageActivations.Add(activation);
        await db.SaveChangesAsync();
        return activation.MessageActivationId;
    }

    /// <summary>
    /// A shown version of the Activation as B-80 will write it: <paramref name="texts"/> by language code and
    /// <paramref name="assetSlots"/> as the JSON array the column holds.
    /// </summary>
    public async Task<int> AddShownVersionAsync(int activationId, int sequence, DateTime sentUtc, IReadOnlyDictionary<string, string> texts,
        string typeCode = "VISUAL_PAGING", int rank = 3, int? revisionNumber = null, string assetSlots = "[]", DateTime? firstServedUtc = null)
    {
        await using var db = Director();
        var version = new TblMessageActivationVersion
        {
            MessageActivationId = activationId, Sequence = sequence,
            Reason = revisionNumber is null ? ShownVersionReason.FirstSend : ShownVersionReason.Revision, RevisionNumber = revisionNumber,
            MessageTypeId = await db.TblMessageTypes.Where(t => t.Code == typeCode).Select(t => t.MessageTypeId).FirstOrDefaultAsync(),
            MessageTypeCode = typeCode, EffectiveRank = rank, Texts = System.Text.Json.JsonSerializer.Serialize(texts),
            AssetSlots = assetSlots, SentUtc = sentUtc, FirstServedUtc = firstServedUtc,
        };
        db.TblMessageActivationVersions.Add(version);
        await db.SaveChangesAsync();
        return version.MessageActivationVersionId;
    }

    internal sealed class DirectorFactory(Func<DirectorDbContext> create) : IDbContextFactory<DirectorDbContext>
    {
        public DirectorDbContext CreateDbContext() => create();
    }
}
