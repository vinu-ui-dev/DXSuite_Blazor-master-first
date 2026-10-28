using DXSuite.Director.Entities;
using DXSuite.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// The Active Messages tab and Deactivate (MSG-18): the list of Message Activations and the one place an Activation is
/// cancelled. Each call opens its own context and acts as the signed-in user; both need <c>page:messages</c>.
/// </summary>
/// <remarks>
/// <see cref="CancelAsync"/> writes in one transaction (see <c>WriteCancelAsync</c>). Anything that must change with
/// the cancel belongs inside it, as the messaging delivery version does: it makes screens drop the Message on their next
/// publish (INK-09).
/// </remarks>
public sealed class ActiveMessages
{
    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly ISessionState _session;
    private readonly TimeProvider _clock;
    private readonly DisplayTimeZones _timeZones;
    private readonly IUserEventLog _events;
    private readonly ILogger<ActiveMessages> _logger;

    public ActiveMessages(IDbContextFactory<DirectorDbContext> dbFactory, ISessionState session, TimeProvider clock,
        DisplayTimeZones timeZones, IUserEventLog events, ILogger<ActiveMessages> logger)
    {
        _dbFactory = dbFactory;
        _session = session;
        _clock = clock;
        _timeZones = timeZones;
        _events = events;
        _logger = logger;
    }

    /// <summary>How many Activations that are over the tab shows at most; the Message Playback Log holds them all.</summary>
    public const int OverLimit = 100;

    /// <summary>
    /// Every Activation that is active or scheduled, newest sent first; with <paramref name="includeOver"/>, also the
    /// <see cref="OverLimit"/> most recently sent that have ended or were cancelled, so a cancelled one-off stays in view.
    /// </summary>
    public async Task<IReadOnlyList<ActiveMessageRow>> ListAsync(bool includeOver, CancellationToken ct)
    {
        if (!await _session.HasPermissionAsync(PermissionKeys.PageMessages)) return [];

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        var rows = await RowsAsync(db, db.TblMessageActivations.Where(ActivationState.ActiveOrScheduled(now)), now, ct);
        if (!includeOver) return rows;

        var activeOrScheduled = db.TblMessageActivations.Where(ActivationState.ActiveOrScheduled(now)).Select(a => a.MessageActivationId);
        var over = db.TblMessageActivations.Where(a => !activeOrScheduled.Contains(a.MessageActivationId))
            .OrderByDescending(a => a.ActivatedUtc).ThenByDescending(a => a.MessageActivationId)
            .Take(OverLimit);
        return [.. rows, .. await RowsAsync(db, over, now, ct)];
    }

    /// <summary>The Message's Activations that are active or scheduled, newest sent first: what its Deactivate cancels.</summary>
    public async Task<IReadOnlyList<ActiveMessageRow>> ListForMessageAsync(int messageId, CancellationToken ct)
    {
        if (!await _session.HasPermissionAsync(PermissionKeys.PageMessages)) return [];

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        return await RowsAsync(db, db.TblMessageActivations.Where(ActivationState.ActiveOrScheduled(now)).Where(a => a.MessageId == messageId),
            now, ct);
    }

    /// <summary>
    /// Cancels those of the Activations that are still active or scheduled, all in one transaction, and keeps their
    /// Messages, one-off Messages included (MSG-18, G-38). Ids that no longer exist or are already over are skipped.
    /// Once committed it logs <c>activation.cancelled</c> per Activation cancelled (AUD-08); a cancel the user may not
    /// make, or one that fails to save, logs each Activation asked for with that outcome.
    /// </summary>
    public async Task<ActivationCancelResult> CancelAsync(IReadOnlyCollection<int> activationIds, CancellationToken ct)
    {
        if (_session.User is not { } user || !await _session.HasPermissionAsync(PermissionKeys.PageMessages))
        {
            foreach (var id in activationIds)
                await _events.TryWriteAsync(_logger, Cancelled(id, null) with { Outcome = UserEventOutcome.Refused });
            return new ActivationCancelResult(ActivationCancelOutcome.NotAllowed, []);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var activeOrScheduled = await db.TblMessageActivations
            .Include(a => a.Message)
            .Where(a => activationIds.Contains(a.MessageActivationId))
            .Where(ActivationState.ActiveOrScheduled(now))
            .ToListAsync(ct);
        if (activeOrScheduled.Count == 0) return new ActivationCancelResult(ActivationCancelOutcome.AlreadyOver, []);

        try
        {
            await WriteCancelAsync(db, activeOrScheduled, user.UserId, now, ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            foreach (var activation in activeOrScheduled)
                await _events.TryWriteAsync(_logger, Cancelled(activation.MessageActivationId, activation.Message.Name) with
                {
                    Outcome = UserEventOutcome.Failed,
                });
            throw;
        }

        foreach (var activation in activeOrScheduled)
            await _events.TryWriteAsync(_logger, Cancelled(activation.MessageActivationId, activation.Message.Name));
        return new ActivationCancelResult(ActivationCancelOutcome.Cancelled, activeOrScheduled.Select(a => a.MessageActivationId).ToList());
    }

    private static UserEvent Cancelled(int activationId, string? messageName) =>
        new(UserEventActionKeys.ActivationCancelled, MessagingEvents.Activation(activationId, messageName));

    private async Task<IReadOnlyList<ActiveMessageRow>> RowsAsync(DirectorDbContext db, IQueryable<TblMessageActivation> activations,
        DateTime nowUtc, CancellationToken ct)
    {
        var loaded = await activations.AsNoTracking()
            .Include(a => a.Message).ThenInclude(m => m.MessageType)
            .Include(a => a.Targets).ThenInclude(t => t.Zone)
            .AsSplitQuery()
            .ToListAsync(ct);

        var userIds = loaded.Select(a => a.ActivatedByUserId).OfType<int>().Distinct().ToList();
        var names = await db.TblUsers.AsNoTracking().Where(u => userIds.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, u => u.FriendlyName ?? u.UserPrincipalName ?? $"User {u.UserId}", ct);
        var statuses = await _timeZones.StatusesAsync(db.TblDevices, loaded, nowUtc, ct);

        return loaded
            .OrderByDescending(a => a.ActivatedUtc).ThenByDescending(a => a.MessageActivationId)
            .Select(a => new ActiveMessageRow(
                a.MessageActivationId, a.MessageId, a.Message.Name, !a.Message.IsCanned, a.Message.MessageType.DisplayName,
                ActivationTargetText.Describe(a.Targets), a.Targets.Count, ActivationWhen.Of(a), statuses[a.MessageActivationId],
                SentBy(a, names)))
            .ToList();
    }

    private static string? SentBy(TblMessageActivation activation, IReadOnlyDictionary<int, string> names) => activation.Source switch
    {
        MessageActivationSource.Api => "API",
        MessageActivationSource.Cupps => "CUPPS",
        _ => activation.ActivatedByUserId is { } id ? names.GetValueOrDefault(id) : null,
    };

    /// <summary>
    /// The one write of a cancel, inside the caller's transaction: the Activations' cancel stamps and the delivery
    /// version (INK-09).
    /// </summary>
    private static async Task WriteCancelAsync(DirectorDbContext db, IReadOnlyList<TblMessageActivation> activations, int userId,
        DateTime nowUtc, CancellationToken ct)
    {
        foreach (var activation in activations)
        {
            activation.CancelledUtc = nowUtc;
            activation.CancelledByUserId = userId;
        }
        await DeliveryVersion.BumpAsync(db, ct);
        await db.SaveChangesAsync(ct);
    }
}

public enum ActivationCancelOutcome
{
    Cancelled,

    /// <summary>The user lacks <c>page:messages</c>.</summary>
    NotAllowed,

    /// <summary>None of the Activations was active or scheduled any more: ended, cancelled already, or gone.</summary>
    AlreadyOver,
}

/// <summary>
/// One Activation as the Active Messages tab lists it (MSG-18): Message, Type, Target (ZONE-16), when it shows, and
/// Status. <see cref="SentBy"/> is the sender's name, or the API or CUPPS for those sources.
/// </summary>
public sealed record ActiveMessageRow(
    int MessageActivationId,
    int MessageId,
    string MessageName,
    bool IsOneOff,
    string TypeName,
    string Target,
    int ScreenCount,
    ActivationWhen When,
    ActivationStatus Status,
    string? SentBy)
{
    public DateTime StartUtc => When.StartUtc;

    /// <summary>Null when it shows until cancelled.</summary>
    public DateTime? EndUtc => When.EndUtc;

    public DateTime? StartLocal => When.StartLocal;

    public DateTime? EndLocal => When.EndLocal;

    public string? Window => When.Window;

    /// <summary>Only an Activation that is active or scheduled can be cancelled.</summary>
    public bool CanCancel => Status is ActivationStatus.Active or ActivationStatus.Scheduled;
}

/// <summary>What a cancel did; <see cref="CancelledIds"/> are the Activations it cancelled.</summary>
public sealed record ActivationCancelResult(ActivationCancelOutcome Outcome, IReadOnlyList<int> CancelledIds);
