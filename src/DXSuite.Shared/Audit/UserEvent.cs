using System.Net;

namespace DXSuite.Shared.Audit;

/// <summary>Whether the action the event records actually happened.</summary>
public enum UserEventOutcome
{
    Succeeded,
    Refused,
    Failed,
}

/// <summary>What a <see cref="UserEvent"/> acted on, captured at write time (the row never has to be joined back
/// to a since-deleted entity to stay readable).</summary>
/// <param name="Type">One of <see cref="UserEventTargetTypes"/>.</param>
/// <param name="Id">The target's id (an int, a string, or null for a type with no single id, e.g. a feed).</param>
/// <param name="Name">The target's display name at write time.</param>
public sealed record UserEventTarget(string Type, object? Id, string? Name);

/// <summary>Who or what performed a <see cref="UserEvent"/>: a signed-in user, or a background service acting for
/// nobody in particular.</summary>
/// <param name="Kind">"User" or "System".</param>
/// <param name="ActorId">The user id, or null for a System actor.</param>
/// <param name="DisplayName">A human-readable name for logs: the user's friendly name, or the service's name.</param>
public sealed record UserEventActor(string Kind, string? ActorId, string DisplayName)
{
    public static UserEventActor System(string serviceName) => new("System", null, serviceName);

    /// <summary>A System actor named by an API key and (optionally) the address it called from.</summary>
    public static UserEventActor System(string keyName, string? clientIp) =>
        new("System", null, clientIp is null ? keyName : $"{keyName} ({clientIp})");

    /// <summary>Formats a remote address for an actor's display name; "unknown" when there isn't one.</summary>
    public static string FormatIp(IPAddress? address) => address?.ToString() ?? "unknown";
}

/// <summary>One row of the User Event Log (FSD 1.1 AUD-01 to AUD-18): what happened, to what, with what outcome
/// and detail. <see cref="DXSuite.Shared.Audit.IUserEventLog"/> writes it; who did it is either passed explicitly
/// (background services) or resolved from the current session.</summary>
/// <param name="ActionKey">One of <see cref="UserEventActionKeys"/>.</param>
/// <param name="Target">What the action acted on, or null for an action with no single target.</param>
/// <param name="Outcome">Succeeded unless the write passes a different one (by position or with <c>Outcome:</c>).</param>
/// <param name="Details">A fixed-shape, per-action-key payload (an anonymous object or dictionary), serialized as-is.</param>
public sealed record UserEvent(string ActionKey, UserEventTarget? Target,
    UserEventOutcome Outcome = UserEventOutcome.Succeeded, object? Details = null);
