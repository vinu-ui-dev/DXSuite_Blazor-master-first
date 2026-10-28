using System.Text.RegularExpressions;
using DXSuite.Director.Entities;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.CustomerDeviations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// Configuration > Message Types (MSG-02 to MSG-04, MSG-07, MSG-08, DEV-11). The one place that saves a type, its
/// languages and Asset Slots, sets rank and moves Top priority. Each call opens its own context, so the dialog edits a
/// detached <see cref="MessageTypeDraft"/> and Cancel leaves nothing behind.
/// </summary>
public sealed partial class MessageTypeConfiguration
{
    /// <summary>The only slot key allowed under D-02: the type's ASL video (DEV-11).</summary>
    public const string AslSlotKey = "ASL";

    /// <summary>The seeded Counter type's Code; under D-02 that type is built in (DEV-11).</summary>
    public const string CounterTypeCode = "COUNTER";

    private const int CodeLength = 30;
    private const int DisplayNameLength = 100;
    private const int SlotKeyLength = 30;

    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly IMessageLanguageCatalogue _languages;
    private readonly bool _gtaa;
    private readonly TimeProvider _clock;
    private readonly ISessionState _session;
    private readonly IUserEventLog _events;
    private readonly ILogger<MessageTypeConfiguration> _logger;

    public MessageTypeConfiguration(IDbContextFactory<DirectorDbContext> dbFactory, IMessageLanguageCatalogue languages,
        IOptions<D02GtaaMessagingOptions> d02, TimeProvider clock, ISessionState session, IUserEventLog events,
        ILogger<MessageTypeConfiguration> logger)
    {
        _dbFactory = dbFactory;
        _languages = languages;
        _gtaa = d02.Value.Enabled;
        _clock = clock;
        _session = session;
        _events = events;
        _logger = logger;
    }

    /// <summary>The Business Rule key of the rank order (1.2 BRE-04).</summary>
    public const string OrderRuleKey = "message-types.order";

    /// <summary>The Business Rule key of one of a type's values, e.g. <c>message-type.EMERGENCY.display-seconds</c>.</summary>
    public static string RuleKey(string code, string value) => $"message-type.{code}.{value}";

    /// <summary>
    /// Live types in rank order, with only the columns the list shows. Under D-02 the built-in Counter type follows them
    /// with no rank (MSG-20).
    /// </summary>
    public async Task<IReadOnlyList<MessageTypeRow>> ListAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var types = (await db.TblMessageTypes.AsNoTracking()
            .Where(t => !t.IsDeleted)
            .OrderByDescending(t => t.IsTopPriority).ThenBy(t => t.Priority).ThenBy(t => t.MessageTypeId)
            .Select(t => new
            {
                t.MessageTypeId, t.Code, t.DisplayName, t.IsTopPriority, t.DisplayMode, t.MaxCharacters,
                t.DefaultTextColor, t.DefaultBackgroundColor,
                Languages = t.Languages.OrderBy(l => l.SortOrder).Select(l => l.LanguageCode).ToList(),
                Slots = t.AssetLibraries.OrderBy(s => s.SortOrder).Select(s => new AssetSlotSummary(s.SlotKey, s.IsRequired)).ToList()
            })
            .ToListAsync(ct))
            .OrderBy(t => IsBuiltInCounter(t.Code, _gtaa))
            .ToList();

        var rows = new List<MessageTypeRow>(types.Count);
        var rank = 0;
        foreach (var t in types)
        {
            int? rowRank = IsBuiltInCounter(t.Code, _gtaa) ? null : ++rank;
            rows.Add(new MessageTypeRow(t.MessageTypeId, t.Code, t.DisplayName, rowRank, t.IsTopPriority, t.DisplayMode,
                t.MaxCharacters, HexOr(t.DefaultTextColor, MessageTypeDraft.DefaultTextColour),
                HexOr(t.DefaultBackgroundColor, MessageTypeDraft.DefaultBackgroundColour),
                await _languages.NameAsync(t.Languages), t.Slots));
        }
        return rows;
    }

    /// <summary>A blank type. Under D-02 its languages start as the fixed English and French.</summary>
    public MessageTypeDraft NewDraft() => new()
    {
        Languages = _languages.LanguagesFixed ? MessageLanguageCatalogue.GtaaLanguages.Select(l => l.Code).ToList() : []
    };

    /// <summary>A detached copy of a live type, or null when it no longer exists.</summary>
    public async Task<MessageTypeDraft?> OpenAsync(int messageTypeId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var type = await db.TblMessageTypes.AsNoTracking()
            .Include(t => t.Languages)
            .Include(t => t.AssetLibraries)
            .FirstOrDefaultAsync(t => t.MessageTypeId == messageTypeId && !t.IsDeleted, ct);
        if (type is null) return null;

        var rankOrder = (await RankOrderAsync(db, ct)).Select(t => t.MessageTypeId).ToList();

        return new MessageTypeDraft
        {
            MessageTypeId = type.MessageTypeId,
            Code = type.Code,
            DisplayName = type.DisplayName,
            ScreenPart = type.DisplayMode,
            MaxCharacters = type.MaxCharacters,
            IsTopPriority = type.IsTopPriority,
            DisplaySeconds = type.DefaultDisplaySeconds,
            HideSeconds = type.DefaultHideSeconds,
            TextColour = HexOr(type.DefaultTextColor, MessageTypeDraft.DefaultTextColour),
            BackgroundColour = HexOr(type.DefaultBackgroundColor, MessageTypeDraft.DefaultBackgroundColour),
            Languages = type.Languages.OrderBy(l => l.SortOrder).Select(l => l.LanguageCode).ToList(),
            Slots = type.AssetLibraries.OrderBy(s => s.SortOrder)
                .Select(s => new AssetSlotDraft { Key = s.SlotKey, ConstraintId = s.ConstraintId, IsRequired = s.IsRequired, SavedConstraintId = s.ConstraintId })
                .ToList(),
            Rank = rankOrder.IndexOf(type.MessageTypeId) + 1,
            RankOf = rankOrder.Count,
            MessageCount = await db.TblMessages.CountAsync(m => m.MessageTypeId == messageTypeId && !m.IsDeleted, ct),
            HasLiveActivations = await LiveActivations(db, messageTypeId).AnyAsync(ct),
            IsBuiltIn = IsBuiltIn(type)
        };
    }

    /// <summary>
    /// What the dialog offers. Only constraints with an asset library can back a slot (MSG-04): a global constraint
    /// owned by an Asset Library location, which is where a Message's uploads land.
    /// </summary>
    public async Task<MessageTypeDialogOptions> DialogOptionsAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var constraints = await db.TblConstraints.AsNoTracking()
            .Where(c => c.IsGlobal && c.OwningLocationId != null)
            .OrderBy(c => c.ConstraintName)
            .Select(c => new SlotConstraint(c.ConstraintId, c.ConstraintName ?? "", c.OwningLocation!.LocationName ?? ""))
            .ToListAsync(ct);
        return new MessageTypeDialogOptions(await _languages.OfferedAsync(), constraints, _languages.LanguagesFixed, _gtaa);
    }

    /// <summary>
    /// Saves the draft. A new type joins the bottom of the rank order (MSG-07). Ticking Top priority while another type
    /// holds it returns <see cref="MessageTypeSaveOutcome.ConfirmTopPriorityMove"/> unless
    /// <paramref name="moveTopPriority"/> confirms the move (MSG-08). Once saved it logs <c>message-type.created</c> or
    /// <c>.edited</c> with the fields that are not Business Rules, <c>message-type.top-priority-moved</c> when the flag
    /// moved, and one <c>business-rule.changed</c> per Business Rule value that changed, the rank order included (AUD-08).
    /// </summary>
    public async Task<MessageTypeSaveResult> SaveAsync(MessageTypeDraft draft, bool moveTopPriority, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        TblMessageType? type = null;
        if (draft.MessageTypeId is int id)
        {
            type = await db.TblMessageTypes.Include(t => t.Languages).Include(t => t.AssetLibraries)
                .FirstOrDefaultAsync(t => t.MessageTypeId == id && !t.IsDeleted, ct);
            if (type is null) return new MessageTypeSaveResult(MessageTypeSaveOutcome.Gone);
        }

        var errors = await ValidateAsync(db, draft, type, ct);
        if (errors.Count > 0) return new MessageTypeSaveResult(MessageTypeSaveOutcome.Invalid, draft.MessageTypeId, errors);

        // Ranked types alone hold Top priority, so the old holder is never the built-in Counter type.
        var holder = draft.IsTopPriority
            ? await db.TblMessageTypes.FirstOrDefaultAsync(t => t.IsTopPriority && !t.IsDeleted && t.MessageTypeId != draft.MessageTypeId, ct)
            : null;
        if (holder is not null && !moveTopPriority)
            return new MessageTypeSaveResult(MessageTypeSaveOutcome.ConfirmTopPriorityMove, draft.MessageTypeId, TopPriorityHolder: holder.DisplayName);

        var topChanges = draft.IsTopPriority != (type?.IsTopPriority ?? false);
        var before = type is null ? null : Values(type);
        var orderBefore = await OrderCodesAsync(db, ct);
        var topBefore = type?.IsTopPriority == true ? type.Code : holder?.Code;
        var attempt = new UserEvent(type is null ? UserEventActionKeys.MessageTypeCreated : UserEventActionKeys.MessageTypeEdited,
            MessagingEvents.MessageType(type?.MessageTypeId, draft.DisplayName.Trim()));
        await _events.FailureLoggedAsync(_logger, attempt, async () =>
        {
            type = await WriteAsync(db, draft, type, holder, topChanges, ct);
        });

        var target = MessagingEvents.MessageType(type!.MessageTypeId, type.DisplayName);
        var after = Values(type);
        await _events.TryWriteAsync(_logger, before is null
            ? new UserEvent(UserEventActionKeys.MessageTypeCreated, target, Details: Fields(null, after))
            : new UserEvent(UserEventActionKeys.MessageTypeEdited, target, Details: Fields(before, after)));
        if (topChanges)
            await _events.TryWriteAsync(_logger, new UserEvent(UserEventActionKeys.MessageTypeTopPriorityMoved, target,
                Details: new { from = topBefore, to = type.IsTopPriority ? type.Code : null }));
        await LogRuleAsync(target, RuleKey(type.Code, "display-seconds"), before?.DisplaySeconds, after.DisplaySeconds, "seconds");
        await LogRuleAsync(target, RuleKey(type.Code, "hide-seconds"), before?.HideSeconds, after.HideSeconds, "seconds");
        await LogRuleAsync(target, RuleKey(type.Code, "max-characters"), before?.MaxCharacters, after.MaxCharacters, "characters");
        await LogOrderAsync(target, orderBefore, ct);
        return new MessageTypeSaveResult(MessageTypeSaveOutcome.Saved, type.MessageTypeId);
    }

    /// <summary>
    /// The save itself, in one transaction; returns the type saved. Any save moves the messaging delivery version
    /// (INK-09): the body carries the type's rank, Screen Part, colours and seconds, so screens republish (MSG-07,
    /// MSG-08).
    /// </summary>
    private async Task<TblMessageType> WriteAsync(DirectorDbContext db, MessageTypeDraft draft, TblMessageType? type,
        TblMessageType? holder, bool topChanges, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // The filtered unique index allows one live Top priority type, and EF does not order a clear before a set
        // across rows, so the old holder is cleared in a save of its own first.
        if (holder is not null)
        {
            holder.IsTopPriority = false;
            await db.SaveChangesAsync(ct);
        }

        if (type is null)
        {
            var bottom = await db.TblMessageTypes.Where(t => !t.IsDeleted).MaxAsync(t => (int?)t.Priority, ct) ?? 0;
            type = new TblMessageType { Code = NormaliseCode(draft.Code), Priority = bottom + 1 };
            db.TblMessageTypes.Add(type);
        }

        type.DisplayName = draft.DisplayName.Trim();
        type.DisplayMode = draft.ScreenPart;
        type.MaxCharacters = draft.MaxCharacters;
        type.IsTopPriority = draft.IsTopPriority;
        type.DefaultDisplaySeconds = draft.DisplaySeconds;
        type.DefaultHideSeconds = draft.HideSeconds;
        type.DefaultTextColor = draft.TextColour.ToUpperInvariant();
        type.DefaultBackgroundColor = draft.BackgroundColour.ToUpperInvariant();
        ApplyLanguages(type, LanguagesToSave(draft));
        ApplySlots(type, SlotsToSave(draft));
        await DeliveryVersion.BumpAsync(db, ct);
        await db.SaveChangesAsync(ct);

        // Rank order puts Top priority first, so moving the flag renumbers the list to match what it shows.
        if (topChanges)
        {
            Renumber(await RankOrderAsync(db, ct));
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
        return type;
    }

    /// <summary>A type's saved values as its events compare them.</summary>
    private sealed record TypeValues(
        string Code, string DisplayName, string ScreenPart, string? TextColour, string? BackgroundColour,
        IReadOnlyList<string> Languages, IReadOnlyList<SlotValue> Slots, int DisplaySeconds, int HideSeconds, int MaxCharacters);

    private sealed record SlotValue(string Key, int ConstraintId, bool Required);

    private static TypeValues Values(TblMessageType type) => new(
        type.Code, type.DisplayName, type.DisplayMode.ToString(), type.DefaultTextColor, type.DefaultBackgroundColor,
        type.Languages.OrderBy(l => l.SortOrder).Select(l => l.LanguageCode).ToList(),
        type.AssetLibraries.OrderBy(s => s.SortOrder).Select(s => new SlotValue(s.SlotKey, s.ConstraintId, s.IsRequired)).ToList(),
        type.DefaultDisplaySeconds, type.DefaultHideSeconds, type.MaxCharacters);

    /// <summary>
    /// The changed fields that are not Business Rule values, old and new (AUD-08); a new type has every one, old null.
    /// Top priority has its own event and the Business Rule values theirs.
    /// </summary>
    private static UserEventChanges Fields(TypeValues? before, TypeValues after)
    {
        if (before is null)
            return new UserEventChanges()
                .Always("code", null, after.Code)
                .Always("displayName", null, after.DisplayName)
                .Always("screenPart", null, after.ScreenPart)
                .Always("textColour", null, after.TextColour)
                .Always("backgroundColour", null, after.BackgroundColour)
                .Always("languages", null, after.Languages)
                .Always("slots", null, after.Slots);
        return new UserEventChanges()
            .Field("displayName", before.DisplayName, after.DisplayName)
            .Field("screenPart", before.ScreenPart, after.ScreenPart)
            .Field("textColour", before.TextColour, after.TextColour)
            .Field("backgroundColour", before.BackgroundColour, after.BackgroundColour)
            .List("languages", before.Languages, after.Languages)
            .List("slots", before.Slots, after.Slots);
    }

    /// <summary>One <c>business-rule.changed</c> for a type's value (AUD-08); a new type's values change from null.</summary>
    private Task LogRuleAsync(UserEventTarget target, string key, int? old, int @new, string unit) =>
        old == @new ? Task.CompletedTask
        : _events.TryWriteAsync(_logger, new UserEvent(UserEventActionKeys.BusinessRuleChanged, target,
            Details: new { key, old, @new, unit }));

    /// <summary>
    /// <c>business-rule.changed</c> for the rank order when it differs from <paramref name="before"/>: a reorder, a new
    /// type at the bottom, a Top priority move or a delete. The order is the live types' Codes, first wins.
    /// </summary>
    private async Task LogOrderAsync(UserEventTarget target, IReadOnlyList<string> before, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var after = await OrderCodesAsync(db, ct);
        if (before.SequenceEqual(after)) return;
        await _events.TryWriteAsync(_logger, new UserEvent(UserEventActionKeys.BusinessRuleChanged, target,
            Details: new { key = OrderRuleKey, old = before, @new = after, unit = "order" }));
    }

    private async Task<IReadOnlyList<string>> OrderCodesAsync(DirectorDbContext db, CancellationToken ct) =>
        (await RankOrderAsync(db, ct)).Select(t => t.Code).ToList();

    /// <summary>
    /// Moves a type to <paramref name="toIndex"/> in the rank order and rewrites every ranked type's Priority 1 to n
    /// (MSG-07). The Top priority type stays pinned first: it cannot move, and nothing can move above it (MSG-08).
    /// Under D-02 the built-in Counter type is not ranked, so it neither moves nor is renumbered (MSG-20). The new
    /// ranks reach screens through the messaging delivery version, which moves in the same save (INK-09).
    /// </summary>
    public async Task<bool> MoveAsync(int messageTypeId, int toIndex, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var live = await RankOrderAsync(db, ct);
        var from = live.FindIndex(t => t.MessageTypeId == messageTypeId);
        var firstMovable = live.Count > 0 && live[0].IsTopPriority ? 1 : 0;
        if (from < firstMovable || toIndex < firstMovable || toIndex >= live.Count || from == toIndex) return false;

        var orderBefore = live.Select(t => t.Code).ToList();
        var moved = live[from];
        live.RemoveAt(from);
        live.Insert(toIndex, moved);
        Renumber(live);
        await DeliveryVersion.BumpAsync(db, ct);
        var target = MessagingEvents.MessageType(moved.MessageTypeId, moved.DisplayName);
        var reorder = new UserEvent(UserEventActionKeys.BusinessRuleChanged, target,
            Details: new { key = OrderRuleKey, old = orderBefore, @new = live.Select(t => t.Code).ToList(), unit = "order" });
        await _events.FailureLoggedAsync(_logger, reorder, () => db.SaveChangesAsync(ct));
        await _events.TryWriteAsync(_logger, reorder);
        return true;
    }

    /// <summary>
    /// Deletes the type and hides its canned Messages (MSG-14). Refused while an Activation of one of its Messages is
    /// active or scheduled (<see cref="ActivationState"/>), because deleting would strand it, and always for the
    /// built-in Counter type under D-02 (DEV-11). Activations that are over do not block; they keep their rows.
    /// Logs <c>message-type.deleted</c>, a refused or failed attempt with that outcome, and the rank order it leaves
    /// (AUD-08).
    /// </summary>
    public async Task<MessageTypeDeleteOutcome> DeleteAsync(int messageTypeId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var type = await db.TblMessageTypes.Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.MessageTypeId == messageTypeId && !t.IsDeleted, ct);
        if (type is null) return MessageTypeDeleteOutcome.Gone;

        var target = MessagingEvents.MessageType(type.MessageTypeId, type.DisplayName);
        var deleted = new UserEvent(UserEventActionKeys.MessageTypeDeleted, target,
            Details: new UserEventChanges().Always("isDeleted", false, true));
        var refusal = IsBuiltIn(type) ? MessageTypeDeleteOutcome.BuiltIn
            : await LiveActivations(db, messageTypeId).AnyAsync(ct) ? MessageTypeDeleteOutcome.Busy
            : (MessageTypeDeleteOutcome?)null;
        if (refusal is { } refused)
        {
            await _events.TryWriteAsync(_logger, deleted with { Outcome = UserEventOutcome.Refused });
            return refused;
        }

        var orderBefore = await OrderCodesAsync(db, ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (var message in type.Messages.Where(m => !m.IsDeleted))
        {
            message.IsDeleted = true;
            message.ModifiedUtc = now;
            message.ModifiedByUserId = _session.User?.UserId;
        }
        type.IsDeleted = true;
        type.IsTopPriority = false;
        await _events.FailureLoggedAsync(_logger, deleted, () => db.SaveChangesAsync(ct));
        await _events.TryWriteAsync(_logger, deleted);
        await LogOrderAsync(target, orderBefore, ct);
        return MessageTypeDeleteOutcome.Deleted;
    }

    // Codes never change after the first save, so the Code alone names the Counter type on every tenant.
    private bool IsBuiltIn(TblMessageType type) => IsBuiltInCounter(type.Code, _gtaa);

    /// <summary>
    /// True for the built-in Counter type under D-02 (DEV-11). It sits outside the Display Zone ranking (MSG-20), so
    /// the Messages page neither gives it a tab nor sends it in 1.1; with D-02 off Counter is an ordinary type.
    /// </summary>
    public static bool IsBuiltInCounter(string code, bool gtaaMessaging) => gtaaMessaging && code == CounterTypeCode;

    private IQueryable<TblMessageActivation> LiveActivations(DirectorDbContext db, int messageTypeId)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        return db.TblMessageActivations.Where(ActivationState.ActiveOrScheduled(now))
            .Where(a => a.Message.MessageTypeId == messageTypeId);
    }

    private async Task<Dictionary<string, string>> ValidateAsync(DirectorDbContext db, MessageTypeDraft draft,
        TblMessageType? type, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();

        if (type is null)
        {
            var code = NormaliseCode(draft.Code);
            if (code.Length == 0) errors[nameof(draft.Code)] = "Code is required.";
            else if (code.Length > CodeLength || !CodePattern().IsMatch(code))
                errors[nameof(draft.Code)] = $"Use up to {CodeLength} capital letters, digits and underscores.";
            // The unique index on Code includes deleted types, so a deleted type's Code stays taken.
            else if (await db.TblMessageTypes.AnyAsync(t => t.Code == code, ct))
                errors[nameof(draft.Code)] = "Code in use.";
        }

        var name = draft.DisplayName.Trim();
        if (name.Length == 0) errors[nameof(draft.DisplayName)] = "Display Name is required.";
        else if (name.Length > DisplayNameLength) errors[nameof(draft.DisplayName)] = $"Use up to {DisplayNameLength} characters.";
        else if (await db.TblMessageTypes.AnyAsync(t => !t.IsDeleted && t.DisplayName == name && t.MessageTypeId != draft.MessageTypeId, ct))
            errors[nameof(draft.DisplayName)] = "Display Name in use.";

        if (!Enum.IsDefined(draft.ScreenPart)) errors[nameof(draft.ScreenPart)] = "Screen Part is required.";

        // MSG-20: under D-02 the Counter type sits outside the ranking that Top priority heads.
        if (draft.IsTopPriority && type is not null && IsBuiltIn(type))
            errors[nameof(draft.IsTopPriority)] = $"{type.DisplayName} is built in and sits outside the ranking, so it cannot be Top priority.";

        if (draft.MaxCharacters is < TblMessageType.MaxCharactersLowerBound or > TblMessageType.MaxCharactersUpperBound)
            errors[nameof(draft.MaxCharacters)] =
                $"Between {TblMessageType.MaxCharactersLowerBound} and {TblMessageType.MaxCharactersUpperBound}.";

        if (draft.DisplaySeconds < 1) errors[nameof(draft.DisplaySeconds)] = "At least 1 second.";
        if (draft.HideSeconds < 0) errors[nameof(draft.HideSeconds)] = "0 or more seconds.";
        if (!IsHex(draft.TextColour)) errors[nameof(draft.TextColour)] = "A hex colour such as #FFFFFF.";
        if (!IsHex(draft.BackgroundColour)) errors[nameof(draft.BackgroundColour)] = "A hex colour such as #000000.";

        var languages = LanguagesToSave(draft);
        if (languages.Count == 0) errors[nameof(draft.Languages)] = "Add at least one language.";
        else if (languages.Distinct(StringComparer.OrdinalIgnoreCase).Count() != languages.Count)
            errors[nameof(draft.Languages)] = "Each language once.";
        else if (!_languages.LanguagesFixed)
        {
            // Codes the type already has stay valid; a newly added one must come from the catalogue.
            var offered = (await _languages.OfferedAsync()).Select(l => l.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var kept = type?.Languages.Select(l => l.LanguageCode).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            if (languages.Any(l => !offered.Contains(l) && !kept.Contains(l)))
                errors[nameof(draft.Languages)] = "Pick languages from the list.";
        }

        var slotError = await ValidateSlotsAsync(db, SlotsToSave(draft), ct);
        if (slotError is not null) errors[nameof(draft.Slots)] = slotError;

        return errors;
    }

    private async Task<string?> ValidateSlotsAsync(DirectorDbContext db, List<AssetSlotDraft> slots, CancellationToken ct)
    {
        if (_gtaa && slots.Count > 1) return "Under the GTAA deviation a Message Type has at most one Asset Slot, an ASL video.";
        if (slots.Any(s => string.IsNullOrWhiteSpace(s.Key))) return "Every slot needs a key.";
        if (slots.Any(s => s.Key.Trim().Length > SlotKeyLength)) return $"Slot keys are up to {SlotKeyLength} characters.";
        if (slots.Select(s => s.Key.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != slots.Count)
            return "Each slot key once.";

        // A slot keeps the constraint it was saved with; a new or changed binding needs a constraint with a library.
        var bound = slots.Where(s => s.ConstraintId != s.SavedConstraintId).Select(s => s.ConstraintId).Distinct().ToList();
        if (bound.Count == 0) return null;
        var withLibrary = await db.TblConstraints
            .CountAsync(c => bound.Contains(c.ConstraintId) && c.IsGlobal && c.OwningLocationId != null, ct);
        return withLibrary == bound.Count ? null : "Pick a constraint that has an asset library.";
    }

    private List<string> LanguagesToSave(MessageTypeDraft draft) =>
        _languages.LanguagesFixed
            ? MessageLanguageCatalogue.GtaaLanguages.Select(l => l.Code).ToList()
            : draft.Languages.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    // Under D-02 the one slot is always the ASL video, whatever key the draft carries.
    private List<AssetSlotDraft> SlotsToSave(MessageTypeDraft draft) =>
        _gtaa
            ? draft.Slots.Select(s => new AssetSlotDraft { Key = AslSlotKey, ConstraintId = s.ConstraintId, IsRequired = s.IsRequired, SavedConstraintId = s.SavedConstraintId }).ToList()
            : draft.Slots;

    private static void ApplyLanguages(TblMessageType type, List<string> codes)
    {
        foreach (var gone in type.Languages.Where(l => !codes.Contains(l.LanguageCode, StringComparer.OrdinalIgnoreCase)).ToList())
            type.Languages.Remove(gone);
        for (var i = 0; i < codes.Count; i++)
        {
            var row = type.Languages.FirstOrDefault(l => string.Equals(l.LanguageCode, codes[i], StringComparison.OrdinalIgnoreCase));
            if (row is null) type.Languages.Add(row = new TblMessageTypeLanguage { LanguageCode = codes[i] });
            row.SortOrder = i + 1;
        }
    }

    private static void ApplySlots(TblMessageType type, List<AssetSlotDraft> slots)
    {
        var keys = slots.Select(s => s.Key.Trim()).ToList();
        foreach (var gone in type.AssetLibraries.Where(s => !keys.Contains(s.SlotKey, StringComparer.OrdinalIgnoreCase)).ToList())
            type.AssetLibraries.Remove(gone);
        for (var i = 0; i < slots.Count; i++)
        {
            var row = type.AssetLibraries.FirstOrDefault(s => string.Equals(s.SlotKey, keys[i], StringComparison.OrdinalIgnoreCase));
            if (row is null) type.AssetLibraries.Add(row = new TblMessageTypeAssetLibrary { SlotKey = keys[i] });
            row.ConstraintId = slots[i].ConstraintId;
            row.IsRequired = slots[i].IsRequired;
            row.SortOrder = i + 1;
        }
    }

    /// <summary>
    /// The ranked live types as the list shows them and Messages win: Top priority first, then by Priority. Under D-02
    /// the built-in Counter type is left out (MSG-20).
    /// </summary>
    private async Task<List<TblMessageType>> RankOrderAsync(DirectorDbContext db, CancellationToken ct) =>
        (await db.TblMessageTypes.Where(t => !t.IsDeleted)
            .OrderByDescending(t => t.IsTopPriority).ThenBy(t => t.Priority).ThenBy(t => t.MessageTypeId)
            .ToListAsync(ct))
        .Where(t => !IsBuiltIn(t))
        .ToList();

    private static void Renumber(List<TblMessageType> rankOrder)
    {
        for (var i = 0; i < rankOrder.Count; i++) rankOrder[i].Priority = i + 1;
    }

    private static string NormaliseCode(string code) => code.Trim().ToUpperInvariant();

    private static bool IsHex(string? colour) => colour is not null && HexPattern().IsMatch(colour);

    // Types saved by the ported dialog may hold a colour name such as "black"; the dialog's colour inputs need hex.
    private static string HexOr(string? colour, string fallback)
    {
        if (IsHex(colour)) return colour!.ToUpperInvariant();
        try
        {
            var c = System.Drawing.ColorTranslator.FromHtml(colour ?? "");
            return c.IsEmpty ? fallback : $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    [GeneratedRegex("^[A-Z0-9_]+$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexPattern();
}
