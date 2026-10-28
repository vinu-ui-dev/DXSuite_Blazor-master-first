using DXSuite.Shared.Authorization;
using Microsoft.Extensions.Logging;

namespace DXSuite.Shared.Audit;

/// <summary>
/// Writes one row to the tenant's User Event Log (FSD 1.1 AUD-01 to AUD-18: every state change and session event,
/// never edited or deleted). Resolve from DI, scoped per tenant.
/// </summary>
/// <remarks>
/// This interface lives in Shared so both Connectors and Director code can depend on it without Connectors
/// depending on Director. <see cref="LoggingUserEventLog"/> is the default implementation, writing to
/// <see cref="ILogger"/> rather than a dedicated audit table — the User Event Log's own report screens are a
/// separate, not-yet-built feature (see the GTAA FSD 1.1 completion notes); this keeps every caller's audit
/// trail real and inspectable in the meantime without inventing that screen's storage shape ahead of it.
/// </remarks>
public interface IUserEventLog
{
    /// <summary>Writes <paramref name="evt"/> with the actor resolved from the current session (a signed-in user,
    /// or <see cref="UserEventActor.System"/> when none is signed in).</summary>
    Task WriteAsync(UserEvent evt, CancellationToken ct = default);

    /// <summary>Writes <paramref name="evt"/> for an explicit actor — a background service with no session
    /// (e.g. the CoreAPI watcher), or code acting on behalf of someone other than the caller.</summary>
    Task WriteAsync(UserEventActor actor, UserEvent evt, CancellationToken ct = default);
}

public sealed class LoggingUserEventLog(ILogger<LoggingUserEventLog> logger, ISessionState session) : IUserEventLog
{
    public Task WriteAsync(UserEvent evt, CancellationToken ct = default) => WriteAsync(CurrentActor(), evt, ct);

    public Task WriteAsync(UserEventActor actor, UserEvent evt, CancellationToken ct = default)
    {
        logger.LogInformation(
            "UserEvent {ActionKey} outcome={Outcome} actor={ActorKind}:{ActorId} ({ActorName}) target={TargetType}:{TargetId} ({TargetName}) details={@Details}",
            evt.ActionKey, evt.Outcome, actor.Kind, actor.ActorId, actor.DisplayName,
            evt.Target?.Type, evt.Target?.Id, evt.Target?.Name, evt.Details);
        return Task.CompletedTask;
    }

    private UserEventActor CurrentActor() => session.User is { } u
        ? new UserEventActor("User", u.UserId.ToString(), u.FriendlyName ?? u.UserPrincipalName)
        : UserEventActor.System("Unknown");
}

public static class UserEventLogExtensions
{
    /// <summary>Writes <paramref name="evt"/> for the current session, logging (rather than throwing) if the
    /// write itself fails — used where losing one audit row must never break the caller's own operation.</summary>
    public static async Task TryWriteAsync(this IUserEventLog log, ILogger logger, UserEvent evt, CancellationToken ct = default)
    {
        try
        {
            await log.WriteAsync(evt, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to write user event {ActionKey}", evt.ActionKey);
        }
    }

    /// <summary>Runs <paramref name="write"/> (typically a <see cref="IUserEventLog.WriteAsync(UserEventActor,UserEvent,CancellationToken)"/>
    /// call for an explicit actor), logging rather than throwing if it fails.</summary>
    public static async Task TryWriteAsync(this IUserEventLog log, ILogger logger, string what,
        Func<IUserEventLog, CancellationToken, Task> write, CancellationToken ct = default)
    {
        try
        {
            await write(log, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to write user event: {What}", what);
        }
    }

    /// <summary>As <see cref="TryWriteAsync(IUserEventLog,ILogger,UserEvent,CancellationToken)"/>, but for an
    /// explicit actor rather than the current session's.</summary>
    public static Task TryWriteForActorAsync(this IUserEventLog log, ILogger logger, UserEvent evt, UserEventActor actor,
        CancellationToken ct = default) =>
        log.TryWriteAsync(logger, evt.ActionKey, (l, c) => l.WriteAsync(actor, evt, c), ct);

    /// <summary>
    /// Runs <paramref name="operation"/> (typically a database save); if it throws, writes <paramref name="evt"/>
    /// with its outcome overridden to <see cref="UserEventOutcome.Failed"/> (best-effort — a failure to log that
    /// does not mask the original exception) and rethrows, so the caller's own failure handling still runs.
    /// </summary>
    public static async Task<T> FailureLoggedAsync<T>(this IUserEventLog log, ILogger logger, UserEvent evt,
        Func<Task<T>> operation, CancellationToken ct = default)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await log.TryWriteAsync(logger, evt with { Outcome = UserEventOutcome.Failed }, ct);
            throw;
        }
    }

    /// <summary>As <see cref="FailureLoggedAsync{T}(IUserEventLog,ILogger,UserEvent,Func{Task{T}},CancellationToken)"/>,
    /// but for a background caller (e.g. an API trigger) that already knows its own actor rather than the current
    /// session's. A distinct name, not an overload, so a 4-argument call is never ambiguous about which parameter
    /// a <see cref="UserEventActor"/> in that position is filling.</summary>
    public static async Task<T> FailureLoggedForActorAsync<T>(this IUserEventLog log, ILogger logger, UserEvent evt,
        Func<Task<T>> operation, UserEventActor actor, CancellationToken ct = default)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await log.TryWriteAsync(logger, "failure-logged", (l, c) => l.WriteAsync(actor, evt with { Outcome = UserEventOutcome.Failed }, c), ct);
            throw;
        }
    }

    /// <summary>The non-generic form, for an operation with nothing to return.</summary>
    public static async Task FailureLoggedAsync(this IUserEventLog log, ILogger logger, UserEvent evt,
        Func<Task> operation, CancellationToken ct = default)
    {
        try
        {
            await operation();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await log.TryWriteAsync(logger, evt with { Outcome = UserEventOutcome.Failed }, ct);
            throw;
        }
    }
}
