using System.Linq.Expressions;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Services.Messaging;

/// <summary>Where a Message Activation stands at a moment, as the Active Messages tab shows it (MSG-18).</summary>
public enum ActivationStatus
{
    /// <summary>
    /// Not cancelled and not ended, but not showing now on any of its screens: before its start, or outside its weekly
    /// window.
    /// </summary>
    Scheduled,

    /// <summary>Showing now on at least one of its screens, read on that screen's own clock (MSG-17).</summary>
    Active,

    /// <summary>Its end has passed without a cancel.</summary>
    Ended,

    /// <summary>Someone cancelled it (Deactivate).</summary>
    Cancelled,
}

/// <summary>
/// The one definition of a Message Activation that is "active or scheduled" (MSG-14, ZONE-12, A-33): not cancelled,
/// and its end unset (until cancelled) or still ahead. A scheduled one has not started yet. While one exists, its
/// Message cannot be retired or deleted, its Message Type and the Display Zones it went through cannot be deleted or
/// retired, and the Active Messages tab offers to cancel it. Every such check reads this class, as a query over
/// Activations or over their frozen screen rows, or in memory.
/// </summary>
/// <remarks>
/// Active versus Scheduled is MSG-17's "showing now", which also needs the weekly window and each screen's time zone,
/// so <see cref="Of"/> takes the zones of the Activation's frozen screens. An Activation outside its window today is
/// still active or scheduled.
/// </remarks>
public static class ActivationState
{
    private static readonly Expression<Func<TblMessageActivation, DateTime, bool>> Rule =
        (a, nowUtc) => a.CancelledUtc == null && (a.EndUtc == null || a.EndUtc > nowUtc);

    private static readonly Func<TblMessageActivation, DateTime, bool> CompiledRule = Rule.Compile();

    /// <summary>The rule as a query over Activations.</summary>
    public static Expression<Func<TblMessageActivation, bool>> ActiveOrScheduled(DateTime nowUtc)
    {
        var activation = Rule.Parameters[0];
        return Expression.Lambda<Func<TblMessageActivation, bool>>(Bind(Rule.Body, Rule.Parameters[1], nowUtc), activation);
    }

    /// <summary>The rule as a query over frozen screen rows, through each row's Activation.</summary>
    public static Expression<Func<TblMessageActivationTarget, bool>> TargetActiveOrScheduled(DateTime nowUtc) =>
        Through<TblMessageActivationTarget>(nameof(TblMessageActivationTarget.MessageActivation), nowUtc);

    /// <summary>The rule as a query over the Display Zones picked at send, through each row's Activation.</summary>
    public static Expression<Func<TblMessageActivationZone, bool>> ChosenZoneActiveOrScheduled(DateTime nowUtc) =>
        Through<TblMessageActivationZone>(nameof(TblMessageActivationZone.MessageActivation), nowUtc);

    private static Expression<Func<T, bool>> Through<T>(string activationProperty, DateTime nowUtc)
    {
        var row = Expression.Parameter(typeof(T), "r");
        var body = new Replace(Rule.Parameters[0], Expression.Property(row, activationProperty))
            .Visit(Bind(Rule.Body, Rule.Parameters[1], nowUtc));
        return Expression.Lambda<Func<T, bool>>(body, row);
    }

    /// <summary>The Activations whose end has passed without a cancel, as a query.</summary>
    public static Expression<Func<TblMessageActivation, bool>> Ended(DateTime nowUtc)
    {
        var activation = Rule.Parameters[0];
        var notCancelled = Expression.Equal(Expression.Property(activation, nameof(TblMessageActivation.CancelledUtc)),
            Expression.Constant(null, typeof(DateTime?)));
        var over = Expression.Not(Bind(Rule.Body, Rule.Parameters[1], nowUtc));
        return Expression.Lambda<Func<TblMessageActivation, bool>>(Expression.AndAlso(notCancelled, over), activation);
    }

    public static bool IsActiveOrScheduled(TblMessageActivation activation, DateTime nowUtc) => CompiledRule(activation, nowUtc);

    /// <summary>
    /// The Activation's status at <paramref name="nowUtc"/>. <paramref name="screenTimeZones"/> are the time zones of its
    /// frozen screens (<see cref="DisplayTimeZones"/>); it is Active while it shows on at least one of them.
    /// </summary>
    public static ActivationStatus Of(TblMessageActivation activation, DateTime nowUtc, IReadOnlyCollection<TimeZoneInfo> screenTimeZones)
    {
        if (!IsActiveOrScheduled(activation, nowUtc))
            return activation.CancelledUtc is null ? ActivationStatus.Ended : ActivationStatus.Cancelled;

        var now = new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc));
        // A send-now Activation reads no wall clock, so even one whose screens are unknown has an answer.
        IReadOnlyCollection<TimeZoneInfo> timeZones = screenTimeZones.Count > 0 ? screenTimeZones : [TimeZoneInfo.Utc];
        return timeZones.Any(timeZone => ActivationTiming.IsShowingAt(activation, now, timeZone))
            ? ActivationStatus.Active
            : ActivationStatus.Scheduled;
    }

    // A member of a closure object rather than a constant, so EF sends the time as a parameter and reuses the plan.
    private static Expression Bind(Expression body, ParameterExpression now, DateTime nowUtc) =>
        new Replace(now, Expression.Property(Expression.Constant(new Moment(nowUtc)), nameof(Moment.Utc))).Visit(body);

    private sealed record Moment(DateTime Utc);

    private sealed class Replace(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
