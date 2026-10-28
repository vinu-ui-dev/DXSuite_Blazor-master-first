namespace DXSuite.Director.Services.Messaging;

/// <summary>How a send picks its screens (ZONE-05): one or more Display Zones, or exactly one display.</summary>
/// <remarks>1.2 AM-02 adds a counter target for CUPPS Counter Messages; it is never offered in the send dialog.</remarks>
public enum MessageTargetMode
{
    DisplayZones,
    OneDisplay
}

/// <summary>
/// Where a send goes. Display Zones mode names at least one zone and no display; one-display mode names one display and
/// no zone. Anything else is refused, so a send can never target both, several single displays, or a location.
/// </summary>
public sealed record MessageSendTarget(MessageTargetMode Mode, IReadOnlyList<int> ZoneIds, int? DisplayId)
{
    public static MessageSendTarget DisplayZones(params int[] zoneIds) => new(MessageTargetMode.DisplayZones, zoneIds, null);

    public static MessageSendTarget Display(int displayId) => new(MessageTargetMode.OneDisplay, [], displayId);
}

/// <summary>When a send shows (MSG-16): Send now, or scheduled. <see cref="ActivationTiming"/> turns it into the Activation's times.</summary>
public abstract record MessageSendTiming
{
    /// <summary>Send now, for <paramref name="Minutes"/> or, when null, until cancelled.</summary>
    public sealed record SendNow(int? Minutes) : MessageSendTiming;

    /// <summary>
    /// A date range from <paramref name="Start"/> (inclusive) to <paramref name="End"/> (exclusive), with an optional
    /// weekly window. All times are wall-clock times, read in each target display's own time zone.
    /// </summary>
    public sealed record Scheduled(DateTime Start, DateTime End, MessageWeeklyWindow? Weekly = null) : MessageSendTiming;
}

/// <summary>
/// The days a scheduled send shows on and, when both times are set, the daily window inside them (wall-clock, start
/// inclusive, end exclusive). A window whose start is later than its end runs overnight and belongs to the day it
/// starts on.
/// </summary>
public sealed record MessageWeeklyWindow(IReadOnlySet<DayOfWeek> Days, TimeOnly? DailyStart = null, TimeOnly? DailyEnd = null);

/// <summary>
/// One send as the sender asked for it. A canned send names <see cref="MessageId"/>; a one-off send leaves it null and
/// carries <see cref="OneOffTexts"/>, one text per language of the type, typed at send time (MSG-05).
/// </summary>
public sealed record MessageSendRequest
{
    public required int MessageTypeId { get; init; }

    public int? MessageId { get; init; }

    /// <summary>Text by language code for a one-off Message; ignored for a canned one.</summary>
    public IReadOnlyDictionary<string, string>? OneOffTexts { get; init; }

    public required MessageSendTarget Target { get; init; }

    public required MessageSendTiming When { get; init; }

    /// <summary>Seconds each showing lasts; null takes the type's value at send (MSG-16).</summary>
    public int? DisplaySeconds { get; init; }

    /// <summary>Seconds between showings; null takes the type's value at send (MSG-16).</summary>
    public int? HideSeconds { get; init; }

    /// <summary>
    /// The Activation's effective rank in place of its type's Priority (MSG-09), any integer; null keeps the type's.
    /// Only holders of <c>feature:messages-priority-override</c> may set it.
    /// </summary>
    public int? PriorityOverride { get; init; }

    public bool IsOneOff => MessageId is null;
}

/// <summary>
/// The external system behind an API send (MSG-19): the API key's name, which the User Event Log records as the System
/// actor (A-26), and the caller's address.
/// </summary>
public sealed record MessageApiCaller(string KeyName, string? ClientIp);

/// <summary>
/// One display of a send's frozen screen list (ZONE-06), as the Confirm screens step shows it and the send saves it.
/// <see cref="ZoneId"/> is the Display Zone the display came through, null in one-display mode.
/// </summary>
public sealed record FrozenScreen(int DisplayId, string DisplayName, string LocationName, int? ZoneId, string? ZoneName);

public enum MessageSendOutcome
{
    /// <summary>Every check passed and nothing was written: the Confirm screens step may show <see cref="MessageSendResult.Screens"/>.</summary>
    Ready,

    /// <summary>The Activation and its frozen screen rows were saved.</summary>
    Sent,

    /// <summary>The request is malformed: see <see cref="MessageSendResult.Errors"/>.</summary>
    Invalid,

    /// <summary>The sender may not send this: no <c>page:messages</c>, or no Message access rule covers the target (ZONE-08).</summary>
    NotAllowed,

    /// <summary>The Message or type cannot be sent: gone, not Approved (MSG-12), or the built-in Counter under D-02 (MSG-20).</summary>
    NotSendable,

    /// <summary>The target reaches no screen.</summary>
    NoScreens,

    /// <summary>The screen list differs from the one the sender confirmed; <see cref="MessageSendResult.Screens"/> holds the new one.</summary>
    ScreensChanged
}

/// <summary>
/// What a check or a send came to. <see cref="Problem"/> is a sentence for the sender when the outcome is a refusal.
/// <see cref="ScreenTimeZones"/> holds each screen's time zone id for a scheduled send, so the sender sees whose clock applies.
/// </summary>
public sealed record MessageSendResult(
    MessageSendOutcome Outcome,
    IReadOnlyList<FrozenScreen> Screens,
    IReadOnlyDictionary<string, string> Errors,
    string? Problem = null,
    int? MessageActivationId = null,
    IReadOnlyDictionary<int, string>? ScreenTimeZones = null)
{
    public int ScreenCount => Screens.Count;

    internal static MessageSendResult Refused(MessageSendOutcome outcome, string problem) =>
        new(outcome, [], new Dictionary<string, string>(), problem);
}

/// <summary>A language of the Message being sent: its text for a canned Message, empty for a one-off.</summary>
public sealed record MessageSendText(string LanguageCode, string LanguageName, string Body);

/// <summary>
/// What the send dialog opens on: the Message (or the type, for a one-off), the type's display and hide seconds, and
/// what the sender may pick in each target mode (ZONE-08). <see cref="CanOverridePriority"/> offers the priority
/// override (MSG-09); <see cref="DefaultTimeZone"/> is the zone of displays with no device (A-15).
/// </summary>
public sealed record MessageSendSetup(
    int MessageTypeId,
    string MessageTypeName,
    int? MessageId,
    string MessageName,
    IReadOnlyList<MessageSendText> Texts,
    int MaxCharacters,
    int DefaultDisplaySeconds,
    int DefaultHideSeconds,
    IReadOnlyList<SendableZone> Zones,
    IReadOnlyList<SendableLocation> Locations,
    bool CanOverridePriority = false,
    int TypePriority = 0,
    string DefaultTimeZone = "")
{
    public bool IsOneOff => MessageId is null;

    /// <summary>True when no Message access rule lets the sender send this anywhere.</summary>
    public bool NothingToPick => Zones.Count == 0 && Locations.Count == 0;
}
