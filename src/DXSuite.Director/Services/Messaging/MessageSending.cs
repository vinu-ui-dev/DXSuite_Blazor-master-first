using DXSuite.Director.Entities;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.CustomerDeviations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// Sending a Message (MSG-05, MSG-12, MSG-15 to MSG-16, MSG-19, ZONE-05, ZONE-06, ZONE-08): the one place a Message
/// Activation and its frozen screen rows are written, from Director's send dialog (<c>DirectorAdmin</c>, acting as the
/// signed-in sender) or from an external system by API key (<c>Api</c>). Each call opens its own context.
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle for the send dialog: <see cref="OpenAsync"/> (canned) or <see cref="OpenOneOffAsync"/>, then
/// <see cref="CheckAsync"/> for the Confirm screens step, then <see cref="SendAsync"/> with the display ids the
/// sender confirmed. Every check runs again in <see cref="SendAsync"/>, since rules, Messages and Display Zones can
/// change while the dialog is open. An API call is one <see cref="SendFromApiAsync"/>.
/// </para>
/// <para>
/// Both sends write everything in one transaction (see <c>WriteAsync</c>). Anything that must exist with the Activation
/// belongs inside it, such as the first shown version (AUD-06) and the messaging delivery version that tells screens to
/// fetch it (INK-09). Times come from <see cref="ActivationTiming.Resolve"/>, read against each screen's zone from <see
/// cref="DisplayTimeZones"/>.
/// </para>
/// </remarks>
public sealed class MessageSending
{
    public const string TargetField = "Target";
    public const string WhenField = "When";
    public const string DisplaySecondsField = "DisplaySeconds";
    public const string HideSecondsField = "HideSeconds";
    public const string DaysField = "Days";
    public const string DailyWindowField = "DailyWindow";

    private const int MessageNameLength = 100;

    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly IMessageSendAccess _access;
    private readonly IMessageLanguageCatalogue _languages;
    private readonly ISessionState _session;
    private readonly bool _gtaa;
    private readonly TimeProvider _clock;
    private readonly DisplayTimeZones _timeZones;
    private readonly ShownVersions _versions;
    private readonly IUserEventLog _events;
    private readonly ILogger<MessageSending> _logger;

    public MessageSending(IDbContextFactory<DirectorDbContext> dbFactory, IMessageSendAccess access,
        IMessageLanguageCatalogue languages, ISessionState session, IOptions<D02GtaaMessagingOptions> d02, TimeProvider clock,
        DisplayTimeZones timeZones, ShownVersions versions, IUserEventLog events, ILogger<MessageSending> logger)
    {
        _timeZones = timeZones;
        _versions = versions;
        _events = events;
        _logger = logger;
        _dbFactory = dbFactory;
        _access = access;
        _languages = languages;
        _session = session;
        _gtaa = d02.Value.Enabled;
        _clock = clock;
    }

    /// <summary>A one-off Message is named by its first text, so lists can show it.</summary>
    private static string OneOffName(string firstText) =>
        firstText.Length <= MessageNameLength ? firstText : firstText[..MessageNameLength];

    /// <summary>The error key for one language's one-off text.</summary>
    public static string TextField(string languageCode) => "Text:" + languageCode;

    /// <summary>
    /// The send dialog for a canned Message, or null when it cannot be sent: gone, not Approved (MSG-12), or of a type
    /// that is not sent from Director.
    /// </summary>
    public async Task<MessageSendSetup?> OpenAsync(int messageId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var message = await db.TblMessages.AsNoTracking()
            .Include(m => m.Texts)
            .FirstOrDefaultAsync(m => m.MessageId == messageId && !m.IsDeleted && m.IsCanned, ct);
        if (message is null || message.Status != MessageStatus.Approved) return null;

        var type = await SendableTypeAsync(db, message.MessageTypeId, ct);
        if (type is null) return null;

        var texts = message.Texts.ToDictionary(t => t.LanguageCode, t => t.Body, StringComparer.OrdinalIgnoreCase);
        return await SetupAsync(type, message.MessageId, message.Name, texts, ct);
    }

    /// <summary>
    /// The send dialog for one-off text of a type, or null when the type is not sent from Director or takes no one-off
    /// text (<see cref="TakesOneOffTextAsync"/>).
    /// </summary>
    public async Task<MessageSendSetup?> OpenOneOffAsync(int messageTypeId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var type = await SendableTypeAsync(db, messageTypeId, ct);
        return type is null || !await TakesOneOffTextAsync(db, messageTypeId, ct)
            ? null
            : await SetupAsync(type, null, "", new Dictionary<string, string>(), ct);
    }

    /// <summary>
    /// Whether the sender may send one-off text of the type anywhere: only through a Message access rule that allows
    /// one-off text (MSG-05, ZONE-08), and only for a type that takes one-off text.
    /// </summary>
    public async Task<bool> CanSendOneOffAsync(int messageTypeId, CancellationToken ct)
    {
        if (_session.User is not { } user || !await _session.HasPermissionAsync(PermissionKeys.PageMessages)) return false;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (await SendableTypeAsync(db, messageTypeId, ct) is null || !await TakesOneOffTextAsync(db, messageTypeId, ct)) return false;
        return !(await _access.ReachAsync(user.UserId, messageTypeId, oneOff: true, ct)).IsEmpty;
    }

    /// <summary>Why a type takes no one-off text, as the send refuses it.</summary>
    public const string RequiredSlotRefusal =
        "This Message Type has a required Asset Slot, so it is sent only as a canned Message that holds the file.";

    /// <summary>
    /// A one-off is typed at send and has no Asset Slots, so a type with a required slot takes none: a Message of it
    /// must hold a file before it saves (MSG-04).
    /// </summary>
    private static async Task<bool> TakesOneOffTextAsync(DirectorDbContext db, int messageTypeId, CancellationToken ct) =>
        !await db.TblMessageTypeAssetLibraries.AnyAsync(l => l.MessageTypeId == messageTypeId && l.IsRequired, ct);

    /// <summary>
    /// The screens a target reaches now, for the screen count, before any other check. Targets the sender may not
    /// pick still count here; <see cref="CheckAsync"/> and <see cref="SendAsync"/> refuse them.
    /// </summary>
    public async Task<IReadOnlyList<FrozenScreen>> ScreensAsync(MessageSendTarget target, CancellationToken ct)
    {
        if (TargetError(target) is not null) return [];
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await ScreensAsync(db, target, ct);
    }

    /// <summary>
    /// Runs every check a send runs and writes nothing. <see cref="MessageSendOutcome.Ready"/> carries the screen list
    /// the Confirm screens step shows (ZONE-16).
    /// </summary>
    public async Task<MessageSendResult> CheckAsync(MessageSendRequest request, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (refusal, plan) = await PlanAsync(db, request, apiCaller: null, ct);
        return refusal ?? new MessageSendResult(MessageSendOutcome.Ready, plan!.Screens, NoErrors,
            ScreenTimeZones: ZoneIds(plan));
    }

    /// <summary>
    /// Sends: checks everything again, then saves the Activation and one frozen row per display in one transaction
    /// (MSG-15, ZONE-06). When <paramref name="confirmedDisplayIds"/> is given and the target now reaches other
    /// displays (a Display Zone was edited since the Confirm screens step), nothing is saved and the result is
    /// <see cref="MessageSendOutcome.ScreensChanged"/> with the new list. Once saved it logs, as the sender,
    /// <c>message.created</c> for a one-off's new Message, <c>activation.sent</c>, and
    /// <c>activation.priority-override-set</c> when the send carries a priority override (AUD-07, AUD-08); a send the
    /// sender may not make, or one that fails to save, logs <c>activation.sent</c> with that outcome.
    /// </summary>
    public async Task<MessageSendResult> SendAsync(MessageSendRequest request, IReadOnlyCollection<int>? confirmedDisplayIds,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (refusal, plan) = await PlanAsync(db, request, apiCaller: null, ct);
        if (refusal is not null)
        {
            if (refusal.Outcome == MessageSendOutcome.NotAllowed)
                await _events.TryWriteAsync(_logger, AttemptEvent(request, MessageActivationSource.DirectorAdmin, UserEventOutcome.Refused));
            return refusal;
        }

        if (confirmedDisplayIds is not null && !plan!.Screens.Select(s => s.DisplayId).ToHashSet().SetEquals(confirmedDisplayIds))
            return new MessageSendResult(MessageSendOutcome.ScreensChanged, plan.Screens, NoErrors,
                "The screens this send reaches have changed. Check the new list before sending.", ScreenTimeZones: ZoneIds(plan));

        var activation = await _events.FailureLoggedAsync(_logger,
            AttemptEvent(request, MessageActivationSource.DirectorAdmin, UserEventOutcome.Failed), () => CommitAsync(db, plan!, ct));
        if (plan!.MessageId is null)
            await _events.TryWriteAsync(_logger, MessagingEvents.Created(activation.Message, plan.Type.Code));
        await _events.TryWriteAsync(_logger, SentEvent(plan, activation));
        if (activation.PriorityOverride is { } priorityOverride)
            await _events.TryWriteAsync(_logger, new UserEvent(UserEventActionKeys.ActivationPriorityOverrideSet,
                MessagingEvents.Activation(activation.MessageActivationId, plan!.MessageName),
                Details: new UserEventChanges().Always("priorityOverride", null, priorityOverride)));
        return new MessageSendResult(MessageSendOutcome.Sent, plan!.Screens, NoErrors, MessageActivationId: activation.MessageActivationId);
    }

    /// <summary>
    /// <c>activation.sent</c> for a send that saved no Activation: no target id, and of the AUD-08 details only what
    /// the request itself says.
    /// </summary>
    private static UserEvent AttemptEvent(MessageSendRequest request, MessageActivationSource source, UserEventOutcome outcome) => new(
        UserEventActionKeys.ActivationSent,
        new UserEventTarget(UserEventTargetTypes.Activation, null, null),
        outcome,
        new
        {
            source = source.ToString(),
            oneOff = request.IsOneOff,
            messageId = request.MessageId,
            mode = request.Target?.Mode == MessageTargetMode.OneDisplay ? "one-display" : "zones",
            zoneIds = request.Target?.ZoneIds.Distinct().ToList() ?? [],
            priorityOverride = request.PriorityOverride,
        });

    /// <summary>
    /// Sends an Approved canned Message for an external system (MSG-19): the checks, frozen screen rows and transaction of
    /// <see cref="SendAsync"/>, with source <c>Api</c> and no sending account. The key's <c>messaging:send</c> scope stands
    /// in for Message access rules, which belong to accounts, so none are read. Then logs <c>activation.sent</c> as the
    /// System actor named by the key (A-26).
    /// </summary>
    public async Task<MessageSendResult> SendFromApiAsync(int messageId, MessageSendTarget target, MessageSendTiming when,
        MessageApiCaller caller, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        // One-off Messages are typed in the send dialog only, so their ids are not sendable here (MSG-19).
        var messageTypeId = await db.TblMessages.AsNoTracking()
            .Where(m => m.MessageId == messageId && !m.IsDeleted && m.IsCanned)
            .Select(m => (int?)m.MessageTypeId)
            .FirstOrDefaultAsync(ct);
        if (messageTypeId is null)
            return MessageSendResult.Refused(MessageSendOutcome.NotSendable, "No canned Message has this id.");

        var request = new MessageSendRequest { MessageTypeId = messageTypeId.Value, MessageId = messageId, Target = target, When = when };
        var (refusal, plan) = await PlanAsync(db, request, caller, ct);
        if (refusal is not null) return refusal;

        var actor = UserEventActor.System(caller.KeyName, caller.ClientIp);
        var activation = await _events.FailureLoggedForActorAsync(_logger,
            AttemptEvent(request, MessageActivationSource.Api, UserEventOutcome.Failed), () => CommitAsync(db, plan!, ct), actor);
        // The send is saved: a failed log write is reported, never turned into a failed call the system would retry.
        await _events.TryWriteForActorAsync(_logger, SentEvent(plan!, activation), actor);
        return new MessageSendResult(MessageSendOutcome.Sent, plan!.Screens, NoErrors, MessageActivationId: activation.MessageActivationId);
    }

    private async Task<TblMessageActivation> CommitAsync(DirectorDbContext db, SendPlan plan, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var activation = await WriteAsync(db, plan, ct);
        await tx.CommitAsync(ct);
        return activation;
    }

    /// <summary>The <c>activation.sent</c> event of a saved send, in the AUD-08 shape.</summary>
    private static UserEvent SentEvent(SendPlan plan, TblMessageActivation activation) => new(
        UserEventActionKeys.ActivationSent,
        MessagingEvents.Activation(activation.MessageActivationId, plan.MessageName),
        Details: new
        {
            source = activation.Source.ToString(),
            oneOff = plan.MessageId is null,
            messageId = activation.MessageId,
            mode = plan.Target.Mode == MessageTargetMode.DisplayZones ? "zones" : "one-display",
            zoneIds = plan.Target.Mode == MessageTargetMode.DisplayZones ? plan.Target.ZoneIds.Distinct().ToList() : [],
            displayCount = plan.Screens.Count,
            startUtc = activation.StartUtc,
            endUtc = activation.EndUtc,
            weeklyWindow = plan.Timing.Weekly is { } weekly
                ? new { days = weekly.Days.Order().Select(d => d.ToString()).ToList(), dailyStart = weekly.DailyStart, dailyEnd = weekly.DailyEnd }
                : null,
            priorityOverride = activation.PriorityOverride,
        });

    private static readonly IReadOnlyDictionary<string, string> NoErrors = new Dictionary<string, string>();

    private static IReadOnlyDictionary<int, string> ZoneIds(SendPlan plan) => plan.ScreenTimeZones.ToDictionary(z => z.Key, z => z.Value.Id);

    /// <summary>
    /// Everything a send writes, worked out and checked. <see cref="SenderUserId"/> is null for an <c>Api</c> send,
    /// which no account makes.
    /// </summary>
    private sealed record SendPlan(
        TblMessageType Type,
        int? MessageId,
        string MessageName,
        MessageSendTarget Target,
        MessageActivationSource Source,
        IReadOnlyList<(string LanguageCode, string Body)> OneOffTexts,
        IReadOnlyList<FrozenScreen> Screens,
        IReadOnlyDictionary<int, TimeZoneInfo> ScreenTimeZones,
        ResolvedTiming Timing,
        int DisplaySeconds,
        int HideSeconds,
        int? PriorityOverride,
        int? SenderUserId,
        DateTime NowUtc);

    /// <summary>
    /// Checks a send and works out what it writes. A null <paramref name="apiCaller"/> is the signed-in sender, held to
    /// <c>page:messages</c> and their Message access rules; an API caller was already held to its key's scope.
    /// </summary>
    private async Task<(MessageSendResult? Refusal, SendPlan? Plan)> PlanAsync(DirectorDbContext db, MessageSendRequest request,
        MessageApiCaller? apiCaller, CancellationToken ct)
    {
        int? senderUserId = null;
        if (apiCaller is null)
        {
            if (_session.User is not { } user || !await _session.HasPermissionAsync(PermissionKeys.PageMessages))
                return (MessageSendResult.Refused(MessageSendOutcome.NotAllowed, "You may not send Messages."), null);
            senderUserId = user.UserId;
        }

        var type = await SendableTypeAsync(db, request.MessageTypeId, ct);
        if (type is null)
            return (MessageSendResult.Refused(MessageSendOutcome.NotSendable, "This Message Type cannot be sent."), null);

        if (request.PriorityOverride is not null && !await _session.HasPermissionAsync(PermissionKeys.MessagesPriorityOverride))
            return (MessageSendResult.Refused(MessageSendOutcome.NotAllowed, "You may not set a priority override."), null);

        var errors = new Dictionary<string, string>();
        if (TargetError(request.Target) is { } targetError) errors[TargetField] = targetError;
        TimingErrors(request.When, errors);
        if (request.DisplaySeconds is < 1) errors[DisplaySecondsField] = "At least 1 second.";
        if (request.HideSeconds is < 0) errors[HideSecondsField] = "0 or more seconds.";

        var oneOffTexts = new List<(string, string)>();
        var messageName = "";
        if (request.IsOneOff && !await TakesOneOffTextAsync(db, type.MessageTypeId, ct))
            return (MessageSendResult.Refused(MessageSendOutcome.NotSendable, RequiredSlotRefusal), null);
        if (request.IsOneOff)
        {
            var codes = type.Languages.OrderBy(l => l.SortOrder).Select(l => l.LanguageCode).ToList();
            if (codes.Count == 0) errors[TargetField] = "This Message Type has no languages to write in.";
            foreach (var code in codes)
            {
                var body = (request.OneOffTexts?.GetValueOrDefault(code) ?? "").Trim();
                if (body.Length == 0) errors[TextField(code)] = "Type the text.";
                else if (body.Length > type.MaxCharacters) errors[TextField(code)] = $"At most {type.MaxCharacters} characters.";
                oneOffTexts.Add((code, body));
            }
            if (oneOffTexts.Count > 0) messageName = OneOffName(oneOffTexts[0].Item2);
        }
        else
        {
            var message = await db.TblMessages.AsNoTracking()
                .Where(m => m.MessageId == request.MessageId && m.MessageTypeId == type.MessageTypeId && !m.IsDeleted && m.IsCanned)
                .Select(m => new { m.Status, m.Name })
                .FirstOrDefaultAsync(ct);
            if (message is null)
                return (MessageSendResult.Refused(MessageSendOutcome.NotSendable, "This Message no longer exists."), null);
            if (message.Status != MessageStatus.Approved)
                return (MessageSendResult.Refused(MessageSendOutcome.NotSendable, "Only Approved Messages can be sent."), null);
            messageName = message.Name;
        }

        if (errors.Count > 0) return (new MessageSendResult(MessageSendOutcome.Invalid, [], errors), null);

        if (senderUserId is { } senderId && await NotCoveredAsync(db, senderId, type.MessageTypeId, request, ct) is { } notCovered)
            return (MessageSendResult.Refused(MessageSendOutcome.NotAllowed, notCovered), null);

        var screens = await ScreensAsync(db, request.Target, ct);
        if (screens.Count == 0)
            return (MessageSendResult.Refused(MessageSendOutcome.NoScreens, "This send reaches no screens."), null);

        var now = _clock.GetUtcNow().UtcDateTime;
        // Only a scheduled send reads wall-clock times, so only it needs each screen's time zone.
        IReadOnlyDictionary<int, TimeZoneInfo> screenTimeZones = request.When is MessageSendTiming.Scheduled
            ? await _timeZones.ForDisplaysAsync(db, screens.Select(s => s.DisplayId).ToList(), ct)
            : new Dictionary<int, TimeZoneInfo>();
        var timing = ActivationTiming.Resolve(request.When, now, screenTimeZones.Values.Distinct().ToList());
        if (timing.EndUtc <= now)
            return (new MessageSendResult(MessageSendOutcome.Invalid, [], new Dictionary<string, string>
            {
                [WhenField] = "This range has already ended on every screen.",
            }), null);

        // Frozen from the type at send, so a later type change affects new sends only (MSG-16).
        return (null, new SendPlan(type, request.MessageId, messageName, request.Target,
            apiCaller is null ? MessageActivationSource.DirectorAdmin : MessageActivationSource.Api, oneOffTexts, screens, screenTimeZones, timing,
            request.DisplaySeconds ?? type.DefaultDisplaySeconds, request.HideSeconds ?? type.DefaultHideSeconds,
            request.PriorityOverride, senderUserId, now));
    }

    /// <summary>What is wrong with the send's timing, by field. Whether a range has already ended needs the screens' zones.</summary>
    private static void TimingErrors(MessageSendTiming? when, Dictionary<string, string> errors)
    {
        switch (when)
        {
            case null:
                errors[WhenField] = "Pick when to show the Message.";
                break;
            case MessageSendTiming.SendNow { Minutes: < 1 }:
                errors[WhenField] = "At least 1 minute.";
                break;
            case MessageSendTiming.Scheduled scheduled:
                if (scheduled.End <= scheduled.Start) errors[WhenField] = "The end must be after the start.";
                if (scheduled.Weekly is not { } weekly) break;
                if (weekly.Days.Count == 0) errors[DaysField] = "Pick at least one day.";
                if (weekly.DailyStart.HasValue != weekly.DailyEnd.HasValue)
                    errors[DailyWindowField] = "Give both times of the daily window, or neither.";
                else if (weekly.DailyStart is { } from && from == weekly.DailyEnd)
                    errors[DailyWindowField] = "The daily window's start and end must differ.";
                break;
        }
    }

    /// <summary>
    /// The one write of a send, inside the caller's transaction: the one-off Message (if any), the Activation, its
    /// frozen screen rows, the Display Zones picked, its first shown version (AUD-06) and the messaging delivery version
    /// (INK-09). Other rows that must exist with the Activation belong here too.
    /// </summary>
    private async Task<TblMessageActivation> WriteAsync(DirectorDbContext db, SendPlan plan, CancellationToken ct)
    {
        var activation = new TblMessageActivation
        {
            PriorityOverride = plan.PriorityOverride,
            DisplaySecondsOverride = plan.DisplaySeconds,
            HideSecondsOverride = plan.HideSeconds,
            Source = plan.Source,
            ActivatedByUserId = plan.SenderUserId,
            ActivatedUtc = plan.NowUtc,
            Targets = plan.Screens.Select(s => new TblMessageActivationTarget
            {
                DisplayId = s.DisplayId, ZoneId = s.ZoneId, DisplayName = s.DisplayName, LocationName = s.LocationName,
            }).ToList(),
            // Every zone picked, including one whose displays all came through another and so has no screen row.
            ChosenZones = plan.Target.Mode == MessageTargetMode.DisplayZones
                ? plan.Target.ZoneIds.Distinct().Select(id => new TblMessageActivationZone { ZoneId = id }).ToList()
                : [],
        };
        plan.Timing.ApplyTo(activation);

        if (plan.MessageId is { } messageId)
        {
            activation.MessageId = messageId;
        }
        else
        {
            // A one-off is typed at send and skips approval (MSG-05).
            activation.Message = new TblMessage
            {
                MessageTypeId = plan.Type.MessageTypeId,
                Name = plan.MessageName,
                IsCanned = false,
                Status = MessageStatus.Approved,
                CreatedUtc = plan.NowUtc,
                CreatedByUserId = plan.SenderUserId,
                Texts = plan.OneOffTexts.Select(t => new TblMessageText { LanguageCode = t.LanguageCode, Body = t.Body }).ToList(),
            };
        }

        await _versions.AddFirstAsync(db, activation, plan.Type, plan.MessageId, plan.OneOffTexts, plan.NowUtc, ct);
        db.TblMessageActivations.Add(activation);
        await DeliveryVersion.BumpAsync(db, ct);
        await db.SaveChangesAsync(ct);
        return activation;
    }

    /// <summary>
    /// Why the sender's Message access rules do not cover the target (ZONE-08), or null when they do. Read fresh, so a
    /// rule removed while the dialog was open refuses the send.
    /// </summary>
    private async Task<string?> NotCoveredAsync(DirectorDbContext db, int userId, int messageTypeId, MessageSendRequest request,
        CancellationToken ct)
    {
        var reach = await _access.ReachAsync(userId, messageTypeId, request.IsOneOff, ct);
        var withText = request.IsOneOff ? " with one-off text" : "";
        var target = request.Target;

        if (target.Mode == MessageTargetMode.DisplayZones)
        {
            if (reach.CoversZones(target.ZoneIds)) return null;
            var lost = target.ZoneIds.Where(id => !reach.ZoneIds.Contains(id)).ToList();
            var names = await db.TblZones.Where(z => lost.Contains(z.ZoneId)).OrderBy(z => z.SortOrder).Select(z => z.ZoneName).ToListAsync(ct);
            return $"No Message access rule lets you send this type{withText} to {(names.Count > 0 ? string.Join(", ", names) : "that Display Zone")}.";
        }

        var displayId = target.DisplayId!.Value;
        var zoneIds = reach.ZoneIds.ToList();
        var display = await db.TblDisplays.Where(d => d.DisplayId == displayId)
            .Select(d => new { d.DisplayName, InCoveredZone = db.TblZoneDisplays.Any(zd => zd.DisplayId == d.DisplayId && zoneIds.Contains(zd.ZoneId)) })
            .FirstOrDefaultAsync(ct);
        if (display is not null && (reach.AnyDisplay || display.InCoveredZone)) return null;
        return $"No Message access rule lets you send this type{withText} to {display?.DisplayName ?? "that display"}.";
    }

    /// <summary>
    /// The target's screens as they would be frozen now: one per display, through the first of its Display Zones in
    /// list order when two reach it (ZONE-06). Retired Display Zones reach nothing. Ordered as the Display Zone editor
    /// lists locations, then by display name.
    /// </summary>
    private static async Task<IReadOnlyList<FrozenScreen>> ScreensAsync(DirectorDbContext db, MessageSendTarget target, CancellationToken ct)
    {
        if (target.Mode == MessageTargetMode.OneDisplay)
        {
            var one = await db.TblDisplays.Where(d => d.DisplayId == target.DisplayId)
                .Select(d => new { d.DisplayId, d.DisplayName, d.LocationId, d.Location.LocationName })
                .FirstOrDefaultAsync(ct);
            return one is null ? [] : [new FrozenScreen(one.DisplayId, one.DisplayName, one.LocationName ?? $"Location {one.LocationId}", null, null)];
        }

        var zoneIds = target.ZoneIds.Distinct().ToList();
        var rows = await db.TblZoneDisplays.AsNoTracking()
            .Where(zd => zoneIds.Contains(zd.ZoneId) && zd.Zone.RetiredUtc == null)
            .Select(zd => new
            {
                zd.DisplayId,
                zd.Display.DisplayName,
                zd.Display.LocationId,
                zd.Display.Location.LocationName,
                LocationOrder = zd.Display.Location.SortOrder,
                zd.ZoneId,
                zd.Zone.ZoneName,
                ZoneOrder = zd.Zone.SortOrder,
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.DisplayId)
            .Select(g => g.OrderBy(r => r.ZoneOrder).ThenBy(r => r.ZoneId).First())
            .OrderBy(r => r.LocationOrder).ThenBy(r => r.LocationName).ThenBy(r => r.DisplayName)
            .Select(r => new FrozenScreen(r.DisplayId, r.DisplayName, r.LocationName ?? $"Location {r.LocationId}", r.ZoneId, r.ZoneName))
            .ToList();
    }

    private static string? TargetError(MessageSendTarget? target) => target switch
    {
        null => "Pick where to show the Message.",
        { Mode: MessageTargetMode.DisplayZones, ZoneIds.Count: 0 } => "Pick at least one Display Zone.",
        { Mode: MessageTargetMode.DisplayZones, DisplayId: not null } => "Send to Display Zones or to one display, not both.",
        { Mode: MessageTargetMode.OneDisplay, DisplayId: null } => "Pick a display.",
        { Mode: MessageTargetMode.OneDisplay, ZoneIds.Count: > 0 } => "Send to Display Zones or to one display, not both.",
        { Mode: MessageTargetMode.DisplayZones or MessageTargetMode.OneDisplay } => null,
        _ => "Pick where to show the Message.",
    };

    /// <summary>
    /// A live type that Director sends, with its languages. Under D-02 the built-in Counter type sits outside the
    /// Display Zone ranking (MSG-20) and is not sent from Director in 1.1.
    /// </summary>
    private async Task<TblMessageType?> SendableTypeAsync(DirectorDbContext db, int messageTypeId, CancellationToken ct)
    {
        var type = await db.TblMessageTypes.AsNoTracking()
            .Include(t => t.Languages)
            .FirstOrDefaultAsync(t => t.MessageTypeId == messageTypeId && !t.IsDeleted, ct);
        return type is null || MessageTypeConfiguration.IsBuiltInCounter(type.Code, _gtaa) ? null : type;
    }

    private async Task<MessageSendSetup> SetupAsync(TblMessageType type, int? messageId, string name,
        IReadOnlyDictionary<string, string> texts, CancellationToken ct)
    {
        var oneOff = messageId is null;
        var codes = type.Languages.OrderBy(l => l.SortOrder).Select(l => l.LanguageCode).ToList();
        var named = await _languages.NameAsync(codes);
        IReadOnlyList<SendableZone> displayZones = [];
        IReadOnlyList<SendableLocation> locations = [];
        var canOverride = false;
        if (_session.User is { } user && await _session.HasPermissionAsync(PermissionKeys.PageMessages))
        {
            displayZones = await _access.ZonesAsync(user.UserId, type.MessageTypeId, oneOff, ct);
            locations = await _access.DisplaysAsync(user.UserId, type.MessageTypeId, oneOff, ct);
            canOverride = await _session.HasPermissionAsync(PermissionKeys.MessagesPriorityOverride);
        }

        return new MessageSendSetup(type.MessageTypeId, type.DisplayName, messageId, name,
            named.Select(l => new MessageSendText(l.Code, l.Name, texts.GetValueOrDefault(l.Code) ?? "")).ToList(),
            type.MaxCharacters, type.DefaultDisplaySeconds, type.DefaultHideSeconds, displayZones, locations,
            canOverride, type.Priority, _timeZones.Default.Id);
    }
}
