using DXSuite.Director.Entities;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.CustomerDeviations;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Tests;

// Clause ids in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// Configuration > Message Types (B-31): the rules every save, reorder and delete keeps, through
/// <see cref="MessageTypeConfiguration"/> against a Director database on LocalDB.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessageTypeConfigurationTests : IClassFixture<MessageTypeDatabase>, IAsyncLifetime
{
    private readonly MessageTypeDatabase _db;

    public MessageTypeConfigurationTests(MessageTypeDatabase db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [LocalDbFact]
    public async Task A_reorder_rewrites_every_live_rank_one_to_n_in_the_dragged_order()
    {
        var a = await _db.AddTypeAsync("A", priority: 10);
        var b = await _db.AddTypeAsync("B", priority: 20);
        var c = await _db.AddTypeAsync("C", priority: 40);
        await _db.AddTypeAsync("GONE", priority: 15, deleted: true);

        // Drag C to the top of the list.
        Assert.True(await _db.Service().MoveAsync(c, 0, default));

        Assert.Equal([(c, 1), (a, 2), (b, 3)], await _db.LiveRanksAsync());
    }

    [LocalDbFact]
    public async Task Move_up_and_move_down_swap_neighbours_and_keep_ranks_unique()
    {
        var a = await _db.AddTypeAsync("A", priority: 10);
        var b = await _db.AddTypeAsync("B", priority: 20);
        var c = await _db.AddTypeAsync("C", priority: 30);
        var service = _db.Service();

        Assert.True(await service.MoveAsync(a, 1, default));
        Assert.True(await service.MoveAsync(c, 1, default));

        Assert.Equal([(b, 1), (c, 2), (a, 3)], await _db.LiveRanksAsync());
    }

    [LocalDbFact]
    public async Task The_Top_priority_type_stays_pinned_first_and_nothing_moves_above_it()
    {
        var top = await _db.AddTypeAsync("TOP", priority: 5, top: true);
        var a = await _db.AddTypeAsync("A", priority: 10);
        var b = await _db.AddTypeAsync("B", priority: 20);
        var service = _db.Service();

        Assert.False(await service.MoveAsync(top, 2, default));
        Assert.False(await service.MoveAsync(b, 0, default));
        Assert.True(await service.MoveAsync(b, 1, default));

        Assert.Equal([(top, 1), (b, 2), (a, 3)], await _db.LiveRanksAsync());
    }

    [LocalDbFact]
    public async Task The_list_reads_in_rank_order_with_the_Top_priority_type_first()
    {
        var a = await _db.AddTypeAsync("A", priority: 1, languages: ["fra", "eng"]);
        var top = await _db.AddTypeAsync("TOP", priority: 7, top: true);
        await _db.AddTypeAsync("GONE", priority: 2, deleted: true);

        var rows = await _db.Service().ListAsync(default);

        Assert.Equal([top, a], rows.Select(r => r.MessageTypeId));
        Assert.Equal([1, 2], rows.Select(r => r.Rank));
        Assert.Equal(["French", "English"], rows[1].Languages.Select(l => l.Name));
    }

    [LocalDbFact]
    public async Task A_new_type_joins_the_bottom_of_the_rank_order()
    {
        var a = await _db.AddTypeAsync("A", priority: 10);
        var b = await _db.AddTypeAsync("B", priority: 40);
        var service = _db.Service();

        var draft = Valid(service.NewDraft(), "NEW");
        var saved = await service.SaveAsync(draft, moveTopPriority: false, default);

        Assert.Equal(MessageTypeSaveOutcome.Saved, saved.Outcome);
        Assert.Equal([a, b, saved.MessageTypeId!.Value], (await _db.LiveRanksAsync()).Select(r => r.Id));
    }

    [LocalDbFact]
    public async Task Ticking_Top_priority_on_a_second_type_asks_first_and_changes_nothing()
    {
        var holder = await _db.AddTypeAsync("HOLDER", priority: 1, top: true);
        var other = await _db.AddTypeAsync("OTHER", priority: 2);
        var service = _db.Service();

        var draft = (await service.OpenAsync(other, default))!;
        draft.IsTopPriority = true;
        var result = await service.SaveAsync(draft, moveTopPriority: false, default);

        Assert.Equal(MessageTypeSaveOutcome.ConfirmTopPriorityMove, result.Outcome);
        Assert.Equal("HOLDER name", result.TopPriorityHolder);
        Assert.Equal([holder], await _db.TopPriorityTypesAsync());
    }

    [LocalDbFact]
    public async Task Confirming_moves_the_flag_so_at_most_one_type_is_Top_priority_and_it_is_ranked_first()
    {
        var holder = await _db.AddTypeAsync("HOLDER", priority: 1, top: true);
        var a = await _db.AddTypeAsync("A", priority: 2);
        var other = await _db.AddTypeAsync("OTHER", priority: 3);
        var service = _db.Service();

        var draft = (await service.OpenAsync(other, default))!;
        draft.IsTopPriority = true;
        var result = await service.SaveAsync(draft, moveTopPriority: true, default);

        Assert.Equal(MessageTypeSaveOutcome.Saved, result.Outcome);
        Assert.Equal([other], await _db.TopPriorityTypesAsync());
        Assert.Equal([(other, 1), (holder, 2), (a, 3)], await _db.LiveRanksAsync());
    }

    [LocalDbFact]
    public async Task A_new_type_can_take_Top_priority_from_another_after_the_confirm()
    {
        await _db.AddTypeAsync("HOLDER", priority: 1, top: true);
        var service = _db.Service();

        var draft = Valid(service.NewDraft(), "NEW");
        draft.IsTopPriority = true;
        var result = await service.SaveAsync(draft, moveTopPriority: true, default);

        Assert.Equal([result.MessageTypeId!.Value], await _db.TopPriorityTypesAsync());
    }

    [LocalDbFact]
    public async Task Code_is_fixed_after_the_first_save()
    {
        var id = await _db.AddTypeAsync("PAGING", priority: 1, languages: ["eng"]);
        var service = _db.Service();

        var draft = (await service.OpenAsync(id, default))!;
        Assert.True(draft.CodeLocked);
        draft.Code = "RENAMED";
        draft.DisplayName = "Paging";
        var result = await service.SaveAsync(draft, moveTopPriority: false, default);

        Assert.Equal(MessageTypeSaveOutcome.Saved, result.Outcome);
        await using var db = _db.Director();
        var stored = await db.TblMessageTypes.SingleAsync(t => t.MessageTypeId == id);
        Assert.Equal(("PAGING", "Paging"), (stored.Code, stored.DisplayName));
    }

    [LocalDbFact]
    public async Task A_new_type_needs_a_code_no_other_type_has_used()
    {
        await _db.AddTypeAsync("TAKEN", priority: 1, deleted: true);
        var service = _db.Service();

        var result = await service.SaveAsync(Valid(service.NewDraft(), "taken"), moveTopPriority: false, default);

        Assert.Equal(MessageTypeSaveOutcome.Invalid, result.Outcome);
        Assert.Contains(nameof(MessageTypeDraft.Code), result.Errors.Keys);
    }

    [LocalDbFact]
    public async Task Languages_are_saved_per_type_in_order_and_any_number_of_them_works()
    {
        var service = _db.Service();
        var draft = Valid(service.NewDraft(), "MULTI");
        draft.Languages.AddRange(["spa", "zho", "pan", "fra"]);
        var id = (await service.SaveAsync(draft, false, default)).MessageTypeId!.Value;

        var reopened = (await service.OpenAsync(id, default))!;
        Assert.Equal(["eng", "spa", "zho", "pan", "fra"], reopened.Languages);

        reopened.Languages.Remove("zho");
        reopened.Languages.Remove("fra");
        reopened.Languages.Insert(0, "fra");
        await service.SaveAsync(reopened, false, default);

        await using var db = _db.Director();
        var stored = await db.TblMessageTypeLanguages.Where(l => l.MessageTypeId == id).OrderBy(l => l.SortOrder)
            .Select(l => new { l.LanguageCode, l.SortOrder }).ToListAsync();
        Assert.Equal([("fra", 1), ("eng", 2), ("spa", 3), ("pan", 4)], stored.Select(l => (l.LanguageCode, l.SortOrder)));
    }

    [LocalDbFact]
    public async Task A_type_needs_a_language_and_a_new_one_comes_from_the_catalogue()
    {
        var service = _db.Service();
        var none = Valid(service.NewDraft(), "NONE");
        none.Languages.Clear();
        var unknown = Valid(service.NewDraft(), "UNKNOWN");
        unknown.Languages.Add("xxx");

        Assert.Contains(nameof(MessageTypeDraft.Languages), (await service.SaveAsync(none, false, default)).Errors.Keys);
        Assert.Contains(nameof(MessageTypeDraft.Languages), (await service.SaveAsync(unknown, false, default)).Errors.Keys);
    }

    [LocalDbFact]
    public async Task With_D02_on_every_type_is_English_and_French_whatever_the_draft_says()
    {
        var catalogue = new FakeLanguages();
        var service = _db.Service(gtaa: true, catalogue: catalogue);

        var draft = service.NewDraft();
        Assert.Equal(["eng", "fra"], draft.Languages);
        Valid(draft, "GTAA");
        draft.Languages.Add("spa");
        var id = (await service.SaveAsync(draft, false, default)).MessageTypeId!.Value;

        var options = await service.DialogOptionsAsync(default);
        Assert.True(options.LanguagesFixed);
        Assert.Equal(["English", "French"], options.Languages.Select(l => l.Name));
        Assert.Equal(["eng", "fra"], (await service.OpenAsync(id, default))!.Languages);
        Assert.Equal(0, catalogue.Calls);
    }

    [LocalDbFact]
    public async Task With_D02_off_the_catalogue_offers_every_language()
    {
        var options = await _db.Service().DialogOptionsAsync(default);

        Assert.False(options.LanguagesFixed);
        Assert.False(options.OneAslSlotOnly);
        Assert.Equal(["Chinese", "English", "French", "Punjabi", "Spanish"], options.Languages.Select(l => l.Name));
    }

    [LocalDbFact]
    public async Task With_D02_on_a_type_has_at_most_one_Asset_Slot_and_it_is_the_ASL_video()
    {
        var video = await _db.AddLibraryConstraintAsync("ASL video");
        var service = _db.Service(gtaa: true);

        var two = Valid(service.NewDraft(), "TWO");
        two.Slots.AddRange([new AssetSlotDraft { Key = "ASL", ConstraintId = video }, new AssetSlotDraft { Key = "BG", ConstraintId = video }]);
        Assert.Contains(nameof(MessageTypeDraft.Slots), (await service.SaveAsync(two, false, default)).Errors.Keys);

        var one = Valid(service.NewDraft(), "ONE");
        one.Slots.Add(new AssetSlotDraft { Key = "SIGN", ConstraintId = video, IsRequired = true });
        var id = (await service.SaveAsync(one, false, default)).MessageTypeId!.Value;

        var slot = Assert.Single((await service.OpenAsync(id, default))!.Slots);
        Assert.Equal(("ASL", video, true), (slot.Key, slot.ConstraintId, slot.IsRequired));
        Assert.True((await service.DialogOptionsAsync(default)).OneAslSlotOnly);
    }

    [LocalDbFact]
    public async Task With_D02_off_slot_keys_stay_open_and_slots_keep_their_dragged_order()
    {
        var hd = await _db.AddLibraryConstraintAsync("Full HD");
        var portrait = await _db.AddLibraryConstraintAsync("Portrait");
        var service = _db.Service();

        var draft = Valid(service.NewDraft(), "SLOTS");
        draft.Slots.AddRange([
            new AssetSlotDraft { Key = "BG_HD", ConstraintId = hd },
            new AssetSlotDraft { Key = "BG_PORTRAIT", ConstraintId = portrait },
            new AssetSlotDraft { Key = "ASL", ConstraintId = hd, IsRequired = true }]);
        var id = (await service.SaveAsync(draft, false, default)).MessageTypeId!.Value;

        var reopened = (await service.OpenAsync(id, default))!;
        var asl = reopened.Slots[2];
        reopened.Slots.RemoveAt(2);
        reopened.Slots.Insert(0, asl);
        await service.SaveAsync(reopened, false, default);

        await using var db = _db.Director();
        var stored = await db.TblMessageTypeAssetLibraries.Where(s => s.MessageTypeId == id).OrderBy(s => s.SortOrder)
            .Select(s => new { s.SlotKey, s.SortOrder }).ToListAsync();
        Assert.Equal([("ASL", 1), ("BG_HD", 2), ("BG_PORTRAIT", 3)], stored.Select(s => (s.SlotKey, s.SortOrder)));
    }

    [LocalDbFact]
    public async Task Only_constraints_with_an_asset_library_back_a_slot()
    {
        var library = await _db.AddLibraryConstraintAsync("Library");
        int plain;
        await using (var db = _db.Director())
        {
            var c = new TblConstraint { ConstraintName = "Display only", IsGlobal = false };
            db.TblConstraints.Add(c);
            await db.SaveChangesAsync();
            plain = c.ConstraintId;
        }
        var service = _db.Service();

        Assert.Equal([library], (await service.DialogOptionsAsync(default)).Constraints.Select(c => c.ConstraintId));

        var draft = Valid(service.NewDraft(), "PLAIN");
        draft.Slots.Add(new AssetSlotDraft { Key = "BG", ConstraintId = plain });
        Assert.Contains(nameof(MessageTypeDraft.Slots), (await service.SaveAsync(draft, false, default)).Errors.Keys);
    }

    [LocalDbTheory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public async Task Character_Limit_is_kept_between_1_and_500(int limit, bool saves)
    {
        var service = _db.Service();
        var draft = Valid(service.NewDraft(), "LIMIT");
        draft.MaxCharacters = limit;

        var result = await service.SaveAsync(draft, false, default);

        Assert.Equal(saves ? MessageTypeSaveOutcome.Saved : MessageTypeSaveOutcome.Invalid, result.Outcome);
    }

    [LocalDbFact]
    public async Task Colours_are_stored_as_hex_and_a_colour_name_from_the_ported_dialog_opens_as_hex()
    {
        var id = await _db.AddTypeAsync("OLD", priority: 1);
        await using (var db = _db.Director())
            await db.TblMessageTypes.Where(t => t.MessageTypeId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.DefaultTextColor, "white").SetProperty(t => t.DefaultBackgroundColor, "black"));
        var service = _db.Service();

        var draft = (await service.OpenAsync(id, default))!;
        Assert.Equal(("#FFFFFF", "#000000"), (draft.TextColour, draft.BackgroundColour));

        draft.BackgroundColour = "#c8102e";
        await service.SaveAsync(draft, false, default);
        draft.TextColour = "red";
        Assert.Contains(nameof(MessageTypeDraft.TextColour), (await service.SaveAsync(draft, false, default)).Errors.Keys);

        await using var check = _db.Director();
        Assert.Equal("#C8102E", (await check.TblMessageTypes.SingleAsync(t => t.MessageTypeId == id)).DefaultBackgroundColor);
    }

    // INK-09, MSG-07, MSG-08: ranks, Screen Part, colours and seconds are in the delivery body, so every type save
    // and reorder republishes. A refused write does not, nor does a delete: it is refused while the type has an
    // Activation on screen or scheduled, and ranks are left as they are.
    [LocalDbFact]
    public async Task A_reorder_a_Top_priority_move_and_a_value_edit_each_move_the_delivery_version()
    {
        var a = await _db.AddTypeAsync("A", priority: 1);
        var b = await _db.AddTypeAsync("B", priority: 2);
        var c = await _db.AddTypeAsync("C", priority: 3);
        var service = _db.Service();
        var seen = new List<Guid> { await DeliveryVersionProbe.ReadAsync(_db.Director) };
        async Task AssertMovedAsync()
        {
            var now = await DeliveryVersionProbe.ReadAsync(_db.Director);
            Assert.DoesNotContain(now, seen);
            seen.Add(now);
        }

        Assert.True(await service.MoveAsync(c, 0, default));
        await AssertMovedAsync();

        var top = (await service.OpenAsync(b, default))!;
        top.IsTopPriority = true;
        Assert.Equal(MessageTypeSaveOutcome.Saved, (await service.SaveAsync(top, false, default)).Outcome);
        await AssertMovedAsync();

        var colours = (await service.OpenAsync(a, default))!;
        colours.BackgroundColour = "#C8102E";
        Assert.Equal(MessageTypeSaveOutcome.Saved, (await service.SaveAsync(colours, false, default)).Outcome);
        await AssertMovedAsync();

        Assert.Equal(MessageTypeDeleteOutcome.Deleted, await service.DeleteAsync(a, default));
        Assert.False(await service.MoveAsync(c, 0, default));
        colours = (await service.OpenAsync(c, default))!;
        colours.TextColour = "red";
        Assert.Equal(MessageTypeSaveOutcome.Invalid, (await service.SaveAsync(colours, false, default)).Outcome);
        Assert.Equal(seen[^1], await DeliveryVersionProbe.ReadAsync(_db.Director));
    }

    [LocalDbFact]
    public async Task A_type_with_a_Message_on_screen_or_scheduled_cannot_be_deleted()
    {
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var busy = await _db.AddTypeAsync("BUSY", priority: 1);
        var idle = await _db.AddTypeAsync("IDLE", priority: 2);
        await _db.AddMessageAsync(busy, start: now.AddHours(1), end: null);
        var idleMessage = await _db.AddMessageAsync(idle, start: now.AddHours(-2), end: now.AddHours(-1));
        var service = _db.Service(clock: new FixedClock(now));

        Assert.True((await service.OpenAsync(busy, default))!.HasLiveActivations);
        Assert.Equal(MessageTypeDeleteOutcome.Busy, await service.DeleteAsync(busy, default));
        Assert.Equal(MessageTypeDeleteOutcome.Deleted, await service.DeleteAsync(idle, default));

        await using var db = _db.Director();
        Assert.True((await db.TblMessages.SingleAsync(m => m.MessageId == idleMessage)).IsDeleted);
        Assert.Equal([busy], (await _db.LiveRanksAsync()).Select(r => r.Id));
    }

    [LocalDbFact]
    public async Task A_type_whose_Activations_have_all_ended_or_been_cancelled_deletes_and_hides_its_Messages_as_the_user()
    {
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var type = await _db.AddTypeAsync("OLD", priority: 1);
        var ended = await _db.AddMessageAsync(type, start: now.AddDays(-2), end: now.AddDays(-1));
        var cancelled = await _db.AddMessageAsync(type, start: now.AddHours(-1), end: null, cancelled: now.AddMinutes(-5));
        var service = _db.Service(clock: new FixedClock(now), userId: 77);

        Assert.False((await service.OpenAsync(type, default))!.HasLiveActivations);
        Assert.Equal(MessageTypeDeleteOutcome.Deleted, await service.DeleteAsync(type, default));

        await using var db = _db.Director();
        Assert.True((await db.TblMessageTypes.SingleAsync(t => t.MessageTypeId == type)).IsDeleted);
        var hidden = await db.TblMessages.Where(m => m.MessageId == ended || m.MessageId == cancelled).ToListAsync();
        Assert.All(hidden, m => Assert.Equal((true, (int?)77, (DateTime?)now), (m.IsDeleted, m.ModifiedByUserId, m.ModifiedUtc)));
        Assert.Equal(2, await db.TblMessageActivations.CountAsync());
    }

    // ---- The built-in Counter type (DEV-11, DEV-04) ----

    [LocalDbFact]
    public async Task With_D02_on_the_Counter_type_is_built_in_and_cannot_be_deleted()
    {
        var counter = await _db.AddTypeAsync("COUNTER", priority: 1);
        var service = _db.Service(gtaa: true);

        Assert.True((await service.OpenAsync(counter, default))!.IsBuiltIn);
        Assert.Equal(MessageTypeDeleteOutcome.BuiltIn, await service.DeleteAsync(counter, default));

        Assert.Equal([counter], (await _db.LiveRanksAsync()).Select(r => r.Id));
    }

    // MSG-20: under D-02 the Counter type sits outside the ranking.
    [LocalDbFact]
    public async Task With_D02_on_the_Counter_type_is_listed_after_the_ranked_types_without_a_rank()
    {
        var counter = await _db.AddTypeAsync("COUNTER", priority: 1);
        var a = await _db.AddTypeAsync("A", priority: 2);
        var b = await _db.AddTypeAsync("B", priority: 3);

        var rows = await _db.Service(gtaa: true).ListAsync(default);

        Assert.Equal([(a, (int?)1), (b, 2), (counter, null)], rows.Select(r => (r.MessageTypeId, r.Rank)));
        var draft = (await _db.Service(gtaa: true).OpenAsync(b, default))!;
        Assert.Equal((2, 2), (draft.Rank, draft.RankOf));
    }

    [LocalDbFact]
    public async Task With_D02_on_the_Counter_type_neither_moves_nor_is_renumbered_by_a_reorder()
    {
        var counter = await _db.AddTypeAsync("COUNTER", priority: 1);
        var a = await _db.AddTypeAsync("A", priority: 2);
        var b = await _db.AddTypeAsync("B", priority: 3);
        var events = new RecordingEventLog();
        var service = _db.Service(gtaa: true, events: events);

        Assert.False(await service.MoveAsync(counter, 0, default));
        Assert.True(await service.MoveAsync(b, 0, default));

        var priorities = (await _db.LiveRanksAsync()).ToDictionary(r => r.Id, r => r.Priority);
        Assert.Equal((1, 1, 2), (priorities[counter], priorities[b], priorities[a]));
        Assert.Equal("""{"key":"message-types.order","old":["A","B"],"new":["B","A"],"unit":"order"}""",
            RecordingEventLog.DetailsJson(events.Single(UserEventActionKeys.BusinessRuleChanged)));
    }

    [LocalDbFact]
    public async Task With_D02_on_the_Counter_type_cannot_take_Top_priority()
    {
        var counter = await _db.AddTypeAsync("COUNTER", priority: 1);
        var top = await _db.AddTypeAsync("EMERGENCY", priority: 2, top: true);
        var service = _db.Service(gtaa: true);

        var draft = (await service.OpenAsync(counter, default))!;
        draft.IsTopPriority = true;
        var result = await service.SaveAsync(draft, moveTopPriority: true, default);

        Assert.Equal(MessageTypeSaveOutcome.Invalid, result.Outcome);
        Assert.Contains(nameof(MessageTypeDraft.IsTopPriority), result.Errors!.Keys);
        await using var db = _db.Director();
        Assert.Equal(top, await db.TblMessageTypes.Where(t => t.IsTopPriority).Select(t => t.MessageTypeId).SingleAsync());
    }

    [LocalDbFact]
    public async Task With_D02_off_the_Counter_type_is_ordinary_and_deletes()
    {
        var counter = await _db.AddTypeAsync("COUNTER", priority: 1);
        var service = _db.Service(gtaa: false);

        Assert.False((await service.OpenAsync(counter, default))!.IsBuiltIn);
        Assert.Equal(MessageTypeDeleteOutcome.Deleted, await service.DeleteAsync(counter, default));

        Assert.Empty(await _db.LiveRanksAsync());
    }

    [LocalDbFact]
    public async Task With_D02_on_only_the_Counter_type_is_built_in()
    {
        var paging = await _db.AddTypeAsync("PAGING", priority: 1);
        var service = _db.Service(gtaa: true);

        Assert.False((await service.OpenAsync(paging, default))!.IsBuiltIn);
        Assert.Equal(MessageTypeDeleteOutcome.Deleted, await service.DeleteAsync(paging, default));
    }

    [LocalDbTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_Counter_type_keeps_its_Code_with_D02_on_or_off(bool gtaa)
    {
        var counter = await _db.AddTypeAsync("COUNTER", priority: 1, languages: ["eng", "fra"]);
        var service = _db.Service(gtaa);

        var draft = (await service.OpenAsync(counter, default))!;
        Assert.True(draft.CodeLocked);
        draft.Code = "KIOSK";
        draft.DisplayName = "Counter desks";
        var result = await service.SaveAsync(draft, moveTopPriority: false, default);

        Assert.Equal(MessageTypeSaveOutcome.Saved, result.Outcome);
        await using var db = _db.Director();
        var stored = await db.TblMessageTypes.SingleAsync(t => t.MessageTypeId == counter);
        Assert.Equal(("COUNTER", "Counter desks"), (stored.Code, stored.DisplayName));
    }

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }

    private static MessageTypeDraft Valid(MessageTypeDraft draft, string code)
    {
        draft.Code = code;
        draft.DisplayName = code + " type";
        draft.ScreenPart = MessageDisplayMode.Overlay;
        if (draft.Languages.Count == 0) draft.Languages.Add("eng");
        return draft;
    }
}

/// <summary>
/// One Director database per test class, created from the model, with the Message Types emptied before each test
/// so every test seeds the types it needs.
/// </summary>
public sealed class MessageTypeDatabase : IAsyncLifetime
{
    private readonly string _database = $"MessageTypes_{Guid.NewGuid():N}"[..30];

    public string Connection =>
        LocalDb.Connection(_database);

    public DirectorDbContext Director() =>
        new(new DbContextOptionsBuilder<DirectorDbContext>().UseSqlServer(Connection).Options);

    public IDbContextFactory<DirectorDbContext> Factory => new DirectorFactory(Director);

    public MessageTypeConfiguration Service(bool gtaa = false, ILanguageProvider? catalogue = null, TimeProvider? clock = null,
        int userId = 1, RecordingEventLog? events = null)
    {
        var d02 = Options.Create(new D02GtaaMessagingOptions { Enabled = gtaa });
        var session = new KeySession((int)Roles.Role.Admin) { User = new TblUser { UserId = userId, RoleId = (int)Roles.Role.Admin } };
        return new MessageTypeConfiguration(Factory, new MessageLanguageCatalogue(d02, catalogue ?? new FakeLanguages()), d02,
            clock ?? TimeProvider.System, session, events ?? new RecordingEventLog(userId),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MessageTypeConfiguration>.Instance);
    }

    public async Task InitializeAsync()
    {
        if (!LocalDbFactAttribute.IsAvailable) return;
        await using var db = Director();
        await LocalDb.EnsureCreatedAsync(db.Database);
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
        await db.Database.ExecuteSqlRawAsync("""
            DELETE FROM tblApprovalReminder; DELETE FROM tblApprovalRequest; DELETE FROM tblAssetApproval; DELETE FROM tblPendingFileDelete;
            DELETE FROM tblMessageActivationTarget; DELETE FROM tblMessageActivationVersion; DELETE FROM tblMessageActivation; DELETE FROM tblMessageText;
            DELETE FROM tblMessageAsset; DELETE FROM tblMessage; DELETE FROM tblAsset; DELETE FROM tblMessagePermission; DELETE FROM tblApprovalChain;
            DELETE FROM tblMessageTypeAssetLibrary; DELETE FROM tblMessageTypeLanguage; DELETE FROM tblMessageType;
            DELETE FROM tblConstraint; DELETE FROM tblDisplay; DELETE FROM tblLocation;
            """);
    }

    public async Task<int> AddTypeAsync(string code, int priority, bool top = false, bool deleted = false, params string[] languages)
    {
        await using var db = Director();
        var type = new TblMessageType
        {
            Code = code,
            DisplayName = code + " name",
            Priority = priority,
            IsTopPriority = top,
            IsDeleted = deleted,
            DisplayMode = MessageDisplayMode.Overlay,
            DefaultDisplaySeconds = 10,
            DefaultHideSeconds = 30,
            DefaultTextColor = "#FFFFFF",
            DefaultBackgroundColor = "#000000",
            Languages = (languages.Length == 0 ? ["eng"] : languages).Select((l, i) => new TblMessageTypeLanguage { LanguageCode = l, SortOrder = i + 1 }).ToList()
        };
        db.TblMessageTypes.Add(type);
        await db.SaveChangesAsync();
        return type.MessageTypeId;
    }

    public async Task AddSlotAsync(int messageTypeId, string slotKey, int constraintId, bool required)
    {
        await using var db = Director();
        db.TblMessageTypeAssetLibraries.Add(new TblMessageTypeAssetLibrary
        {
            MessageTypeId = messageTypeId, SlotKey = slotKey, ConstraintId = constraintId, IsRequired = required, SortOrder = 1
        });
        await db.SaveChangesAsync();
    }

    public async Task<int?> LibraryLocationOfAsync(int constraintId)
    {
        await using var db = Director();
        return await db.TblConstraints.Where(c => c.ConstraintId == constraintId).Select(c => c.OwningLocationId).SingleAsync();
    }

    public async Task AddTextAsync(int messageId, string languageCode, string body)
    {
        await using var db = Director();
        db.TblMessageTexts.Add(new TblMessageText { MessageId = messageId, LanguageCode = languageCode, Body = body });
        await db.SaveChangesAsync();
    }

    /// <summary>Turns a Message into a one-off, as typed at send time (MSG-05).</summary>
    public async Task MakeOneOffAsync(int messageId)
    {
        await using var db = Director();
        await db.TblMessages.Where(m => m.MessageId == messageId).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsCanned, false));
    }

    public async Task SetMaxCharactersAsync(int messageTypeId, int maxCharacters)
    {
        await using var db = Director();
        await db.TblMessageTypes.Where(t => t.MessageTypeId == messageTypeId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.MaxCharacters, maxCharacters));
    }

    /// <summary>Gives the type an approval chain, as Configuration > Message Types > Access does (MSG-11).</summary>
    public async Task AddTypeChainAsync(int messageTypeId, bool enabled = true)
    {
        await using var db = Director();
        db.TblApprovalChains.Add(new TblApprovalChain { MessageTypeId = messageTypeId, IsEnabled = enabled, CreateDate = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    /// <summary>A global constraint owned by an Asset Library location, so it has an asset library.</summary>
    public async Task<int> AddLibraryConstraintAsync(string name)
    {
        await using var db = Director();
        var locationId = await db.Database.SqlQueryRaw<int>("""
            ALTER TABLE tblLocation NOCHECK CONSTRAINT ALL;
            INSERT INTO tblLocation (LocationTypeId, LocationGroupId, LocationName, EnableShuffle, EnableFade, EnableContentApproval)
            OUTPUT INSERTED.LocationId AS Value
            VALUES (1, 1, {0}, 0, 0, 0);
            """, name + " library").ToListAsync();
        var constraint = new TblConstraint { ConstraintName = name, IsGlobal = true, OwningLocationId = locationId.Single() };
        db.TblConstraints.Add(constraint);
        await db.SaveChangesAsync();
        return constraint.ConstraintId;
    }

    /// <summary>A canned Message of the type with one Activation over the given window, cancelled when given.</summary>
    public async Task<int> AddMessageAsync(int messageTypeId, DateTime start, DateTime? end, DateTime? cancelled = null)
    {
        await using var db = Director();
        var message = new TblMessage
        {
            MessageTypeId = messageTypeId,
            Name = "Message",
            IsCanned = true,
            Status = MessageStatus.Approved,
            CreatedUtc = start,
            Activations = [new TblMessageActivation { StartUtc = start, EndUtc = end, CancelledUtc = cancelled, ActivatedUtc = start, Source = MessageActivationSource.DirectorAdmin }]
        };
        db.TblMessages.Add(message);
        await db.SaveChangesAsync();
        return message.MessageId;
    }

    /// <summary>Live types as (id, stored Priority), lowest Priority first.</summary>
    public async Task<List<(int Id, int Priority)>> LiveRanksAsync()
    {
        await using var db = Director();
        return (await db.TblMessageTypes.Where(t => !t.IsDeleted).OrderBy(t => t.Priority)
                .Select(t => new { t.MessageTypeId, t.Priority }).ToListAsync())
            .Select(t => (t.MessageTypeId, t.Priority)).ToList();
    }

    public async Task<List<int>> TopPriorityTypesAsync()
    {
        await using var db = Director();
        return await db.TblMessageTypes.Where(t => t.IsTopPriority && !t.IsDeleted).Select(t => t.MessageTypeId).ToListAsync();
    }

    private sealed class DirectorFactory(Func<DirectorDbContext> create) : IDbContextFactory<DirectorDbContext>
    {
        public DirectorDbContext CreateDbContext() => create();
    }
}

/// <summary>A product language catalogue with a handful of languages, standing in for CoreData's.</summary>
internal sealed class FakeLanguages : ILanguageProvider
{
    public int Calls { get; private set; }

    public Task<IReadOnlyList<LanguageInfo>> GetLanguagesAsync()
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<LanguageInfo>>(
        [
            new(0, "eng", "English", true), new(0, "fra", "French", true), new(0, "spa", "Spanish", true),
            new(0, "pan", "Punjabi", true), new(0, "zho", "Chinese", true)
        ]);
    }

    public Task<IReadOnlyList<CountryLanguageInfo>> GetCountryLanguagesAsync() =>
        Task.FromResult<IReadOnlyList<CountryLanguageInfo>>([]);
}
