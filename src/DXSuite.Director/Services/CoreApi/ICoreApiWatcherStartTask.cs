using DXSuite.Connectors.Services.CoreApi;

namespace DXSuite.Director.Services.CoreApi;

/// <summary>
/// Once-only work that runs under the CoreAPI watcher's lease (HOST-04 item 2), such as the startup switch-off
/// (FDE-03). Register it scoped; the watcher resolves it from the tenant's worker scope each time this instance
/// takes the tenant's lease, after its first <c>FeedStatus</c> poll, so a task resolves its own
/// <see cref="CoreApiClient"/> and <c>IUserEventLog</c> for that tenant. The lease is taken again every lease term
/// and moves between instances, so a task must decide from the state it is given whether there is anything to do,
/// and record through <see cref="ICoreApiWatcherHold"/> what it changed at CoreAPI.
/// </summary>
public interface ICoreApiWatcherStartTask
{
    /// <param name="state">The tenant's feed state after that first poll; null when CoreAPI is not configured.</param>
    /// <param name="hold">The watcher's hold of the lease, for recording what the task changed.</param>
    Task RunAsync(CoreApiFeedState? state, ICoreApiWatcherHold hold, CancellationToken ct);
}

/// <summary>
/// Work the CoreAPI watcher runs under its lease each time CoreAPI's hub reports the flight Outage mode switch,
/// after the report is recorded in the feed state. Only the lease holder's hub connection reports, so one instance
/// acts on each report. Register it scoped; it is resolved from the tenant's worker scope like
/// <see cref="ICoreApiWatcherStartTask"/>.
/// </summary>
public interface ICoreApiWatcherSwitchTask
{
    Task OnSwitchReportedAsync(bool on, ICoreApiWatcherHold hold, CancellationToken ct);
}

/// <summary>
/// The CoreAPI watcher's current hold of a tenant's lease, as its tasks see it. Only the watcher writes the feed
/// state, by compare-and-set, so a task records what it changed at CoreAPI through here.
/// </summary>
public interface ICoreApiWatcherHold
{
    /// <summary>
    /// Records the flight Outage mode switch as a successful call left it. CoreAPI may never report its own switch
    /// (no hub address, or no <c>ToggleOverride</c> broadcast after a toggle), and the lease is taken again every
    /// term, so without this record each hold would find the switch unknown and act again.
    /// </summary>
    Task RecordSwitchAsync(bool on, CancellationToken ct);
}
