using DXSuite.Director.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DXSuite.Director.Tests;

/// <summary>
/// The Messaging and Display Zones schema (FSD 1.1 B-30: MSG-02 to MSG-04, MSG-08, MSG-13, MSG-16, ZONE-05 to
/// ZONE-07, ZONE-09, ZONE-12). The migration runs over a database that already holds Messaging rows in the shape
/// the ported Messaging screens wrote, as the dev database does.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessagingSchemaTests : IClassFixture<MessagingSchemaDatabase>
{
    private readonly MessagingSchemaDatabase _db;

    public MessagingSchemaTests(MessagingSchemaDatabase db) => _db = db;

    private DirectorDbContext Director() => _db.Director();

    // ---- Legacy rows after the upgrade ----

    [LocalDbFact]
    public async Task Every_existing_type_takes_the_languages_its_messages_were_written_in()
    {
        await using var db = Director();
        var languages = await db.TblMessageTypeLanguages
            .OrderBy(l => l.MessageTypeId).ThenBy(l => l.SortOrder)
            .Select(l => new { l.MessageTypeId, l.LanguageCode, l.SortOrder })
            .ToListAsync();

        // Type 5 has no Messages; the old editor gave every type the same languages, so it gets them too.
        foreach (var typeId in new[] { 1, 2, 3, 4, 5 })
        {
            var forType = languages.Where(l => l.MessageTypeId == typeId).ToList();
            Assert.Equal(["eng", "fra"], forType.Select(l => l.LanguageCode));
            Assert.Equal([1, 2], forType.Select(l => l.SortOrder));
        }
    }

    [LocalDbFact]
    public async Task Existing_types_get_the_default_character_limit_and_no_top_priority()
    {
        await using var db = Director();
        var types = await db.TblMessageTypes.Where(t => t.MessageTypeId <= 5).ToListAsync();

        Assert.Equal(5, types.Count);
        Assert.All(types, t => Assert.Equal(160, t.MaxCharacters));
        Assert.All(types, t => Assert.False(t.IsTopPriority));
    }

    [LocalDbFact]
    public async Task The_sign_video_flag_and_the_location_target_are_gone()
    {
        Assert.DoesNotContain("AllowsSignVideo", await ColumnsAsync("tblMessageType"));
        Assert.DoesNotContain("LocationId", await ColumnsAsync("tblMessageActivationTarget"));
    }

    [LocalDbFact]
    public async Task Legacy_zone_targets_become_one_row_per_display_reached()
    {
        // Activation 1 targeted Display Zones 1 (displays 101, 102) and 2 (displays 102, 201) with no display.
        Assert.Equal([(101, 1), (102, 1), (201, 2)], await TargetsAsync(1));
    }

    [LocalDbFact]
    public async Task A_legacy_location_target_becomes_a_row_per_display_in_the_location_with_no_zone()
    {
        Assert.Equal([(201, (int?)null), (202, null)], await TargetsAsync(2));
    }

    [LocalDbFact]
    public async Task Rows_that_already_name_a_display_are_kept_as_they_are()
    {
        Assert.Equal([(101, 1), (102, 1)], await TargetsAsync(3));

        // Activation 5 named display 101 alone and Display Zone 1; the Display Zone adds only display 102.
        Assert.Equal([(101, (int?)null), (102, 1)], await TargetsAsync(5));
    }

    [LocalDbFact]
    public async Task A_legacy_target_on_an_empty_zone_leaves_no_row()
    {
        Assert.Empty(await TargetsAsync(4));
    }

    [LocalDbFact]
    public async Task Existing_access_rules_chains_activations_and_zones_keep_their_meaning()
    {
        await using var db = Director();

        var rule = await db.TblMessagePermissions.SingleAsync(p => p.MessageTypeId == 1 && p.UserGroupId == 5);
        Assert.Null(rule.UserId);

        var chain = await db.TblApprovalChains.SingleAsync(c => c.LocationId == 10);
        Assert.Null(chain.MessageTypeId);

        // No weekly window: every day, all day.
        var activation = await db.TblMessageActivations.SingleAsync(a => a.MessageActivationId == 1);
        Assert.True(activation.Sunday && activation.Monday && activation.Tuesday && activation.Wednesday
            && activation.Thursday && activation.Friday && activation.Saturday);
        Assert.Null(activation.WindowStartTime);
        Assert.Null(activation.WindowEndTime);

        Assert.All(await db.TblZones.ToListAsync(), z => Assert.Null(z.RetiredUtc));

        var message = await db.TblMessages.SingleAsync(m => m.MessageId == 1);
        Assert.Equal(0, message.RevisionNumber);
    }

    // ---- Message Types (MSG-02, MSG-03, MSG-08) ----

    [LocalDbFact]
    public async Task A_type_keeps_its_languages_in_the_order_given()
    {
        await using (var db = Director())
        {
            var type = NewType("LANG_ORDER");
            type.Languages.Add(new TblMessageTypeLanguage { LanguageCode = "fra", SortOrder = 1 });
            type.Languages.Add(new TblMessageTypeLanguage { LanguageCode = "eng", SortOrder = 2 });
            db.TblMessageTypes.Add(type);
            await db.SaveChangesAsync();
        }

        await using (var db = Director())
        {
            var type = await db.TblMessageTypes.Include(t => t.Languages).SingleAsync(t => t.Code == "LANG_ORDER");
            Assert.Equal(["fra", "eng"], type.Languages.OrderBy(l => l.SortOrder).Select(l => l.LanguageCode));
        }
    }

    [LocalDbFact]
    public async Task A_type_cannot_list_the_same_language_twice()
    {
        await using var db = Director();
        var type = NewType("LANG_TWICE");
        type.Languages.Add(new TblMessageTypeLanguage { LanguageCode = "eng", SortOrder = 1 });
        db.TblMessageTypes.Add(type);
        await db.SaveChangesAsync();

        await using var second = Director();
        second.TblMessageTypeLanguages.Add(new TblMessageTypeLanguage { MessageTypeId = type.MessageTypeId, LanguageCode = "eng", SortOrder = 2 });
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [LocalDbFact]
    public async Task A_new_type_defaults_to_160_characters()
    {
        await using var db = Director();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO tblMessageType (Code, DisplayName, Priority, DisplayMode, DefaultDisplaySeconds, DefaultHideSeconds, DefaultTextColor, DefaultBackgroundColor, IsDeleted)
            VALUES ('SQL_DEFAULT', 'Sql default', 900, 2, 10, 30, '#FFFFFF', '#000000', 0);
            """);

        Assert.Equal(160, await db.TblMessageTypes.Where(t => t.Code == "SQL_DEFAULT").Select(t => t.MaxCharacters).SingleAsync());
    }

    [LocalDbTheory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task A_character_limit_outside_1_to_500_is_refused(int limit)
    {
        await using var db = Director();
        var type = NewType($"LIMIT_{limit}");
        type.MaxCharacters = limit;
        db.TblMessageTypes.Add(type);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [LocalDbTheory]
    [InlineData(1)]
    [InlineData(500)]
    public async Task A_character_limit_at_either_bound_is_kept(int limit)
    {
        await using var db = Director();
        var type = NewType($"LIMIT_OK_{limit}");
        type.MaxCharacters = limit;
        db.TblMessageTypes.Add(type);
        await db.SaveChangesAsync();

        Assert.Equal(limit, await Director().TblMessageTypes.Where(t => t.Code == type.Code).Select(t => t.MaxCharacters).SingleAsync());
    }

    [LocalDbFact]
    public async Task Only_one_live_type_may_be_top_priority()
    {
        TblMessageType first;
        await using (var db = Director())
        {
            first = NewType("TOP_FIRST");
            first.IsTopPriority = true;
            db.TblMessageTypes.Add(first);
            await db.SaveChangesAsync();
        }

        await using (var db = Director())
        {
            var second = NewType("TOP_SECOND");
            second.IsTopPriority = true;
            db.TblMessageTypes.Add(second);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        // Moving the flag, as the confirm on a second type does: clear, then set, in one transaction.
        await using (var db = Director())
        {
            var second = NewType("TOP_MOVED");
            db.TblMessageTypes.Add(second);
            await db.SaveChangesAsync();

            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                var current = await db.TblMessageTypes.SingleAsync(t => t.MessageTypeId == first.MessageTypeId);
                current.IsTopPriority = false;
                await db.SaveChangesAsync();
                second.IsTopPriority = true;
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            // A deleted type no longer holds the flag.
            second.IsDeleted = true;
            await db.SaveChangesAsync();
            var third = NewType("TOP_AFTER_DELETE");
            third.IsTopPriority = true;
            db.TblMessageTypes.Add(third);
            await db.SaveChangesAsync();
        }
    }

    // ---- Pending revisions (MSG-13) ----

    [LocalDbFact]
    public async Task A_message_holds_one_pending_revision_with_its_texts()
    {
        var messageId = await NewMessageAsync("REV_ONE");
        await using (var db = Director())
        {
            db.TblMessageRevisions.Add(new TblMessageRevision
            {
                MessageId = messageId, Name = "Gate change", SubmittedUtc = DateTime.UtcNow, SubmittedByUserId = 7,
                Texts =
                {
                    new TblMessageRevisionText { LanguageCode = "eng", Body = "Gate changed to B12" },
                    new TblMessageRevisionText { LanguageCode = "fra", Body = "Porte changée pour B12" },
                },
            });
            await db.SaveChangesAsync();
        }

        await using (var db = Director())
        {
            var message = await db.TblMessages.Include(m => m.PendingRevision!).ThenInclude(r => r.Texts)
                .SingleAsync(m => m.MessageId == messageId);
            Assert.NotNull(message.PendingRevision);
            Assert.Equal("Porte changée pour B12", message.PendingRevision.Texts.Single(t => t.LanguageCode == "fra").Body);
        }

        await using (var db = Director())
        {
            db.TblMessageRevisions.Add(new TblMessageRevision { MessageId = messageId, Name = "Second", SubmittedUtc = DateTime.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [LocalDbFact]
    public async Task A_second_edit_replaces_the_pending_revision()
    {
        var messageId = await NewMessageAsync("REV_REPLACE");
        await using (var db = Director())
        {
            db.TblMessageRevisions.Add(new TblMessageRevision
            {
                MessageId = messageId, Name = "First", SubmittedUtc = DateTime.UtcNow,
                Texts = { new TblMessageRevisionText { LanguageCode = "eng", Body = "First" } },
            });
            await db.SaveChangesAsync();
        }

        await using (var db = Director())
        {
            var pending = await db.TblMessageRevisions.SingleAsync(r => r.MessageId == messageId);
            db.TblMessageRevisions.Remove(pending);
            db.TblMessageRevisions.Add(new TblMessageRevision
            {
                MessageId = messageId, Name = "Second", SubmittedUtc = DateTime.UtcNow,
                Texts = { new TblMessageRevisionText { LanguageCode = "eng", Body = "Second" } },
            });
            await db.SaveChangesAsync();
        }

        await using (var db = Director())
        {
            var revision = await db.TblMessageRevisions.Include(r => r.Texts).SingleAsync(r => r.MessageId == messageId);
            Assert.Equal("Second", revision.Texts.Single().Body);
            Assert.Equal(1, await db.TblMessageRevisionTexts.CountAsync(t => t.MessageRevisionId == revision.MessageRevisionId));
        }
    }

    // ---- Weekly window (MSG-16) ----

    [LocalDbFact]
    public async Task An_activation_keeps_a_weekday_window()
    {
        int id;
        await using (var db = Director())
        {
            var activation = NewActivation();
            activation.Saturday = activation.Sunday = false;
            activation.WindowStartTime = new TimeOnly(7, 0);
            activation.WindowEndTime = new TimeOnly(19, 30);
            db.TblMessageActivations.Add(activation);
            await db.SaveChangesAsync();
            id = activation.MessageActivationId;
        }

        await using (var db = Director())
        {
            var activation = await db.TblMessageActivations.SingleAsync(a => a.MessageActivationId == id);
            Assert.False(activation.Saturday || activation.Sunday);
            Assert.True(activation.Monday && activation.Friday);
            Assert.Equal((new TimeOnly(7, 0), new TimeOnly(19, 30)), (activation.WindowStartTime, activation.WindowEndTime));
        }
    }

    [LocalDbFact]
    public async Task A_window_with_a_start_time_and_no_end_time_is_refused()
    {
        await using var db = Director();
        var activation = NewActivation();
        activation.WindowStartTime = new TimeOnly(7, 0);
        db.TblMessageActivations.Add(activation);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [LocalDbFact]
    public async Task A_window_with_no_day_is_refused()
    {
        await using var db = Director();
        var activation = NewActivation();
        activation.Sunday = activation.Monday = activation.Tuesday = activation.Wednesday =
            activation.Thursday = activation.Friday = activation.Saturday = false;
        db.TblMessageActivations.Add(activation);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ---- Message access rules (ZONE-07) ----

    [LocalDbFact]
    public async Task Two_users_may_each_have_a_rule_for_the_same_type_on_every_zone()
    {
        await using var db = Director();
        db.TblMessagePermissions.Add(new TblMessagePermission { UserId = 7, MessageTypeId = 2 });
        db.TblMessagePermissions.Add(new TblMessagePermission { UserId = 8, MessageTypeId = 2 });
        await db.SaveChangesAsync();

        await using var again = Director();
        again.TblMessagePermissions.Add(new TblMessagePermission { UserId = 7, MessageTypeId = 2 });
        await Assert.ThrowsAsync<DbUpdateException>(() => again.SaveChangesAsync());
    }

    [LocalDbFact]
    public async Task A_group_rule_is_still_unique_per_type_and_zone()
    {
        await using var db = Director();
        db.TblMessagePermissions.Add(new TblMessagePermission { UserGroupId = 5, MessageTypeId = 1 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [LocalDbTheory]
    [InlineData(7, 5)]
    [InlineData(null, null)]
    public async Task A_rule_names_exactly_one_of_a_user_or_a_group(int? userId, int? groupId)
    {
        await using var db = Director();
        db.TblMessagePermissions.Add(new TblMessagePermission { UserId = userId, UserGroupId = groupId, MessageTypeId = 3, ZoneId = 1 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ---- Approval chains (ZONE-09) ----

    [LocalDbFact]
    public async Task A_type_may_own_one_approval_chain()
    {
        await using (var db = Director())
        {
            db.TblApprovalChains.Add(new TblApprovalChain { MessageTypeId = 3, IsEnabled = true, IsDefault = true });
            await db.SaveChangesAsync();
        }

        await using (var db = Director())
        {
            db.TblApprovalChains.Add(new TblApprovalChain { MessageTypeId = 3, IsEnabled = true, IsDefault = true });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [LocalDbTheory]
    [InlineData(10, 4)]
    [InlineData(null, null)]
    public async Task A_chain_is_owned_by_exactly_one_location_or_type(int? locationId, int? messageTypeId)
    {
        await using var db = Director();
        db.TblApprovalChains.Add(new TblApprovalChain { LocationId = locationId, MessageTypeId = messageTypeId, IsEnabled = true, IsDefault = false });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ---- Approval emails (B-34, section 12 point 7; the columns are B-33's) ----

    [LocalDbFact]
    public async Task An_existing_chain_has_approval_emails_off_and_a_24_hour_reminder_interval()
    {
        await using var db = Director();
        var chain = await db.TblApprovalChains.AsNoTracking().SingleAsync(c => c.LocationId == 10 && c.IsDefault);

        Assert.False(chain.EmailApprovers);
        Assert.Equal(24, chain.ReminderIntervalHours);
    }

    [LocalDbTheory]
    [InlineData(0, false)]
    [InlineData(-3, false)]
    [InlineData(1, true)]
    public async Task A_reminder_interval_is_at_least_one_hour(int hours, bool saves)
    {
        await using var db = Director();
        // Rolled back on dispose: other tests expect location 10 to own one chain.
        await using var tx = await db.Database.BeginTransactionAsync();
        db.TblApprovalChains.Add(new TblApprovalChain { LocationId = 10, IsEnabled = true, IsDefault = false, ReminderIntervalHours = hours });

        if (saves) await db.SaveChangesAsync();
        else await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ---- Message approval requests (MSG-11, B-34) ----

    [LocalDbTheory]
    [InlineData(null, false)]
    [InlineData(1, true)]
    public async Task An_approval_request_carries_an_asset_approval_or_a_Message_never_both_or_neither(int? messageId, bool saves)
    {
        await using var db = Director();
        await using var tx = await db.Database.BeginTransactionAsync();
        var chain = await db.TblApprovalChains.Select(c => c.ApprovalChainId).FirstAsync();
        db.TblApprovalRequests.Add(new TblApprovalRequest { MessageId = messageId, ApprovalChainId = chain, SubmittedDate = DateTime.UtcNow });

        if (saves) await db.SaveChangesAsync();
        else await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [LocalDbFact]
    public async Task A_Message_has_at_most_one_approval_request()
    {
        await using var db = Director();
        await using var tx = await db.Database.BeginTransactionAsync();
        var chain = await db.TblApprovalChains.Select(c => c.ApprovalChainId).FirstAsync();
        db.TblApprovalRequests.Add(new TblApprovalRequest { MessageId = 2, ApprovalChainId = chain, SubmittedDate = DateTime.UtcNow });
        await db.SaveChangesAsync();
        db.TblApprovalRequests.Add(new TblApprovalRequest { MessageId = 2, ApprovalChainId = chain, SubmittedDate = DateTime.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ---- Activation targets (ZONE-05, ZONE-06) ----

    [LocalDbFact]
    public async Task A_target_row_without_a_display_is_refused()
    {
        await using var db = Director();
        db.TblMessageActivationTargets.Add(new TblMessageActivationTarget { MessageActivationId = 3, ZoneId = 1, DisplayName = "T1 A1", LocationName = "Terminal 1" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [LocalDbFact]
    public async Task A_one_display_target_has_no_zone()
    {
        await using var db = Director();
        db.TblMessageActivationTargets.Add(new TblMessageActivationTarget { MessageActivationId = 4, DisplayId = 202, DisplayName = "T3 B2", LocationName = "Terminal 3" });
        await db.SaveChangesAsync();
    }

    // ---- Display Zones (ZONE-12) ----

    [LocalDbFact]
    public async Task A_retired_zone_keeps_when_it_was_retired()
    {
        var retired = new DateTime(2026, 10, 20, 14, 5, 0, DateTimeKind.Utc);
        await using (var db = Director())
        {
            db.TblZones.Add(new TblZone { ZoneName = "Retired pier", SortOrder = 9, RetiredUtc = retired });
            await db.SaveChangesAsync();
        }

        await using (var db = Director())
            Assert.Equal(retired, await db.TblZones.Where(z => z.ZoneName == "Retired pier").Select(z => z.RetiredUtc).SingleAsync());
    }

    private static TblMessageType NewType(string code) => new()
    {
        Code = code, DisplayName = code, Priority = 1000 + Math.Abs(code.GetHashCode() % 100000),
        DisplayMode = MessageDisplayMode.Overlay, DefaultDisplaySeconds = 10, DefaultHideSeconds = 30,
        DefaultTextColor = "#FFFFFF", DefaultBackgroundColor = "#000000",
    };

    private static TblMessageActivation NewActivation() => new()
    {
        MessageId = 1, StartUtc = DateTime.UtcNow, Source = MessageActivationSource.DirectorAdmin,
    };

    private async Task<int> NewMessageAsync(string name)
    {
        await using var db = Director();
        var message = new TblMessage { MessageTypeId = 1, Name = name, IsCanned = true, Status = MessageStatus.Approved };
        db.TblMessages.Add(message);
        await db.SaveChangesAsync();
        return message.MessageId;
    }

    private async Task<List<(int DisplayId, int? ZoneId)>> TargetsAsync(int activationId)
    {
        await using var db = Director();
        var rows = await db.TblMessageActivationTargets
            .Where(t => t.MessageActivationId == activationId)
            .Select(t => new { t.DisplayId, t.ZoneId })
            .ToListAsync();
        return rows.Select(r => (r.DisplayId!.Value, r.ZoneId)).OrderBy(r => r.Item1).ToList();
    }

    private async Task<List<string>> ColumnsAsync(string table)
    {
        await using var connection = new SqlConnection(_db.Connection);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table", connection);
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values;
    }
}

/// <summary>
/// One Director database for the class. It stops before the Messaging schema migration to seed rows in the shape
/// the ported Messaging screens wrote (Display Zone targets with no display, a location target, a group access
/// rule, a location approval chain), then migrates to the head.
/// </summary>
public sealed class MessagingSchemaDatabase : IAsyncLifetime
{
    internal const string BeforeMessagingSchema = "20261020024112_RemoveFlightOverrideToggleParameter";

    private readonly string _database = $"MessagingSchema_{Guid.NewGuid():N}"[..30];

    public string Connection =>
        LocalDb.Connection(_database);

    public DirectorDbContext Director() =>
        new(new DbContextOptionsBuilder<DirectorDbContext>().UseSqlServer(Connection).Options);

    public async Task InitializeAsync()
    {
        if (!LocalDbFactAttribute.IsAvailable) return;

        await using (var db = Director())
            await db.GetService<IMigrator>().MigrateAsync(BeforeMessagingSchema);

        await using (var db = Director())
            await db.Database.ExecuteSqlRawAsync(LegacyRows);

        await using (var db = Director())
            await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (!LocalDbFactAttribute.IsAvailable) return;

        await using var db = Director();
        await db.Database.EnsureDeletedAsync();
    }

    /// <summary>
    /// Locations 10 (displays 101, 102) and 20 (displays 201, 202). Display Zone 1 holds 101 and 102, Zone 2 holds
    /// 102 and 201, Zone 3 is empty. Activation 1 targets Zones 1 and 2 with no display, 2 targets location 20, 3 has
    /// the per-display rows the ported send dialog writes, 4 targets the empty Zone 3, and 5 names display 101 and
    /// Zone 1. Types 1 to 4 are the migration's seed; type 5 has no Messages.
    /// </summary>
    internal const string LegacyRows = """
        ALTER TABLE tblLocation NOCHECK CONSTRAINT ALL;
        SET IDENTITY_INSERT tblLocation ON;
        INSERT INTO tblLocation (LocationId, LocationTypeId, LocationGroupId, LocationName, EnableShuffle, EnableFade, EnableContentApproval)
        VALUES (10, 1, 1, 'Terminal 1', 0, 0, 0), (20, 1, 1, 'Terminal 3', 0, 0, 0);
        SET IDENTITY_INSERT tblLocation OFF;

        ALTER TABLE tblDisplay NOCHECK CONSTRAINT ALL;
        SET IDENTITY_INSERT tblDisplay ON;
        INSERT INTO tblDisplay (DisplayId, LocationId, DisplayName, DisplayMode, DisplayTypeId, ConstraintId, UseClock, UseVideoMask, EnableFade, EnableContentApproval)
        VALUES (101, 10, 'T1 A1', 1, 1, 1, 0, 0, 0, 0), (102, 10, 'T1 A2', 1, 1, 1, 0, 0, 0, 0), (201, 20, 'T3 B1', 1, 1, 1, 0, 0, 0, 0), (202, 20, 'T3 B2', 1, 1, 1, 0, 0, 0, 0);
        SET IDENTITY_INSERT tblDisplay OFF;

        SET IDENTITY_INSERT tblZone ON;
        INSERT INTO tblZone (ZoneId, ZoneName, SortOrder) VALUES (1, 'Gates A', 1), (2, 'Gates B', 2), (3, 'Empty', 3);
        SET IDENTITY_INSERT tblZone OFF;
        INSERT INTO tblZoneDisplay (ZoneId, DisplayId) VALUES (1, 101), (1, 102), (2, 102), (2, 201);

        ALTER TABLE tblUser NOCHECK CONSTRAINT ALL;
        SET IDENTITY_INSERT tblUser ON;
        INSERT INTO tblUser (UserId, RoleId, PulseAccess, IsDisabled) VALUES (7, 1, 0, 0), (8, 1, 0, 0);
        SET IDENTITY_INSERT tblUser OFF;

        SET IDENTITY_INSERT tblUserGroup ON;
        INSERT INTO tblUserGroup (UserGroupId, GroupName) VALUES (5, 'RMU');
        SET IDENTITY_INSERT tblUserGroup OFF;

        SET IDENTITY_INSERT tblMessageType ON;
        INSERT INTO tblMessageType (MessageTypeId, Code, DisplayName, Priority, DisplayMode, AllowsSignVideo, DefaultDisplaySeconds, DefaultHideSeconds, DefaultTextColor, DefaultBackgroundColor, IsDeleted)
        VALUES (5, 'LEGACY', 'Legacy', 60, 2, 1, 10, 30, '#FFFFFF', '#000000', 0);
        SET IDENTITY_INSERT tblMessageType OFF;

        SET IDENTITY_INSERT tblMessage ON;
        INSERT INTO tblMessage (MessageId, MessageTypeId, Name, IsCanned, Status, IsDeleted) VALUES (1, 1, 'Fire', 1, 1, 0), (2, 2, 'Delay', 1, 1, 0);
        SET IDENTITY_INSERT tblMessage OFF;
        INSERT INTO tblMessageText (MessageId, LanguageCode, Body) VALUES (1, 'eng', 'Fire'), (1, 'fra', 'Feu'), (2, 'fra', 'Retard'), (2, 'eng', 'Delay');

        SET IDENTITY_INSERT tblMessageActivation ON;
        INSERT INTO tblMessageActivation (MessageActivationId, MessageId, StartUtc, Source) VALUES
            (1, 1, '2026-10-01T10:00:00', 1), (2, 1, '2026-10-01T10:00:00', 1), (3, 1, '2026-10-01T10:00:00', 1),
            (4, 2, '2026-10-01T10:00:00', 1), (5, 2, '2026-10-01T10:00:00', 1);
        SET IDENTITY_INSERT tblMessageActivation OFF;

        INSERT INTO tblMessageActivationTarget (MessageActivationId, ZoneId, DisplayId, LocationId) VALUES
            (1, 1, NULL, NULL), (1, 2, NULL, NULL),
            (2, NULL, NULL, 20),
            (3, 1, 101, NULL), (3, 1, 102, NULL),
            (4, 3, NULL, NULL),
            (5, NULL, 101, NULL), (5, 1, NULL, NULL);

        INSERT INTO tblMessagePermission (UserGroupId, MessageTypeId, ZoneId, CanFreeText) VALUES (5, 1, NULL, 1);

        INSERT INTO tblApprovalChain (LocationId, IsEnabled, EnableLlmAutoApproval, LlmAutoApprovalUpToStageOrder, DeclinedContentRetentionDays) VALUES (10, 1, 0, 0, 3);
        """;
}
