using DXSuite.Connectors.Services.CoreApi;

namespace DXSuite.Aviation.Services.FlightDataEditor;

/// <summary>
/// Whether the Flight Data Editor counts CoreAPI as unreachable, which makes it read-only (FDE-11). This is a third
/// state beside the feed being trusted or in flight Outage mode, and never opens fields the way Outage mode does.
/// It is declared only when every configured server failed (CAPI-06), by one of the editor's own calls or by the
/// CoreAPI watcher's poll, and it ends with the next call a server answers.
/// </summary>
/// <param name="WatcherChangedUtc">When the watcher's stored state last changed, as the editor last read it.</param>
public readonly record struct CoreApiReach(bool Unreachable, DateTime? WatcherChangedUtc)
{
    /// <summary>What the editor says while CoreAPI is unreachable.</summary>
    public const string Banner = "Flight data cannot be reached. The editor is read-only until the connection returns.";

    /// <summary>
    /// Any answer counts as reached, an error included: the editor needs CoreAPI to answer, not to agree. A call never
    /// sent because the connection is not set up says nothing about the servers.
    /// </summary>
    public CoreApiReach After<T>(CoreApiResult<T> result) => result.Outcome switch
    {
        CoreApiOutcome.Unreachable => this with { Unreachable = true },
        CoreApiOutcome.Succeeded or CoreApiOutcome.Failed => this with { Unreachable = false },
        _ => this,
    };

    /// <summary>
    /// The watcher's stored state counts only when it changed since the last read. The editor re-reads it every few
    /// seconds, so an unchanged state is an old observation and must not undo a later call of the editor's own.
    /// </summary>
    public CoreApiReach After(CoreApiFeedState? state) =>
        state is null || state.ChangedUtc == WatcherChangedUtc ? this : new(!state.Reachable, state.ChangedUtc);
}
