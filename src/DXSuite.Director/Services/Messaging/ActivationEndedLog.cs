using DXSuite.Director.Entities;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// Logs <c>activation.ended</c> (AUD-08) once per Activation whose end has passed, as the System. A cancelled
/// Activation has its own <c>activation.cancelled</c> and no end of its own; one sent until cancelled never ends.
/// </summary>
/// <remarks>
/// Each Activation is claimed by setting <see cref="TblMessageActivation.EndedLoggedUtc"/> only while it is still
/// null, before its event is written, so two passes at once (two instances, HOST-04) log it once between them. A
/// claim whose event could not be written is released for the next pass.
/// </remarks>
public sealed class ActivationEndedLog(
    IDbContextFactory<DirectorDbContext> dbFactory,
    IUserEventLog events,
    ILogger<ActivationEndedLog> logger)
{
    /// <summary>The System actor's name on the events.</summary>
    public const string ServiceName = "Activation monitor";

    // A pass logs at most this many; the rest wait for the next pass, a minute later.
    private const int Batch = 500;

    /// <summary>Logs every Activation that has ended by <paramref name="nowUtc"/> and is not logged yet; returns how many.</summary>
    public async Task<int> LogEndedAsync(DateTime nowUtc, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ended = await db.TblMessageActivations.AsNoTracking()
            .Where(ActivationState.Ended(nowUtc))
            .Where(a => a.EndedLoggedUtc == null)
            .OrderBy(a => a.EndUtc).ThenBy(a => a.MessageActivationId)
            .Select(a => new { a.MessageActivationId, a.Message.Name })
            .Take(Batch)
            .ToListAsync(ct);

        var logged = 0;
        foreach (var activation in ended)
        {
            var claimed = await db.TblMessageActivations
                .Where(a => a.MessageActivationId == activation.MessageActivationId && a.EndedLoggedUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.EndedLoggedUtc, nowUtc), ct);
            if (claimed == 0) continue;

            try
            {
                await events.WriteAsync(UserEventActor.System(ServiceName), new UserEvent(UserEventActionKeys.ActivationEnded,
                    MessagingEvents.Activation(activation.MessageActivationId, activation.Name)), ct);
                logged++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not log the end of Activation {ActivationId}; the next pass tries again",
                    activation.MessageActivationId);
                await db.TblMessageActivations.Where(a => a.MessageActivationId == activation.MessageActivationId)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.EndedLoggedUtc, (DateTime?)null), CancellationToken.None);
            }
        }
        return logged;
    }
}
