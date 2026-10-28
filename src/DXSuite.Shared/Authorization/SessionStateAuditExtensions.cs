using DXSuite.Shared.Audit;
using Microsoft.Extensions.Logging;

namespace DXSuite.Shared.Authorization;

/// <summary>
/// Pairs a permission check with the User Event Log: a service that must both authorize a write and record a
/// refused attempt calls <see cref="DemandAsync(ISessionState,string,UserEvent?,IUserEventLog,ILogger)"/> once
/// instead of doing the check-then-log by hand at every call site.
/// </summary>
public static class SessionStateAuditExtensions
{
    /// <summary>Throws <see cref="UnauthorizedAccessException"/> when the session lacks <paramref name="permissionKey"/>.
    /// For a read with nothing to log on refusal.</summary>
    public static async Task DemandAsync(this ISessionState session, string permissionKey)
    {
        if (!await session.HasPermissionAsync(permissionKey))
            throw new UnauthorizedAccessException($"Missing permission '{permissionKey}'.");
    }

    /// <summary>
    /// Throws <see cref="UnauthorizedAccessException"/> when the session lacks <paramref name="permissionKey"/>,
    /// first writing <paramref name="refusal"/> with its outcome set to <see cref="UserEventOutcome.Refused"/>
    /// (when non-null — a read-only caller with nothing to attribute to the refusal passes null).
    /// </summary>
    public static async Task DemandAsync(this ISessionState session, string permissionKey, UserEvent? refusal,
        IUserEventLog eventLog, ILogger logger)
    {
        if (await session.HasPermissionAsync(permissionKey)) return;
        if (refusal is not null)
            await eventLog.TryWriteAsync(logger, refusal with { Outcome = UserEventOutcome.Refused });
        throw new UnauthorizedAccessException($"Missing permission '{permissionKey}'.");
    }
}
