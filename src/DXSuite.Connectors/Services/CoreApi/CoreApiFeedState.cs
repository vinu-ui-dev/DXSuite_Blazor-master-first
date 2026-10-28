namespace DXSuite.Connectors.Services.CoreApi;

/// <summary>
/// The tenant's CoreAPI feed state as the leased watcher last saw it (CAPI-05), shared by every Director instance
/// through SQL (HOST-04). The Flight Data Editor reads its "RMS Feed" indicator, edit kinds and field gates from it.
/// </summary>
/// <param name="Status">CoreAPI's last <c>FeedStatus.Status</c>; kept while CoreAPI is unreachable.</param>
/// <param name="Reachable">False while no configured CoreAPI server answers (FDE-11).</param>
/// <param name="SwitchOn">
/// CoreAPI's flight Outage mode switch as its hub last reported it, or as the watcher's own successful switch-off
/// left it; null until either has happened (CoreAPI does not report the switch on <c>FeedStatus</c>, C-03).
/// </param>
/// <param name="ChangedUtc">When any of the above last changed.</param>
public sealed record CoreApiFeedState(string? Status, bool Reachable, bool? SwitchOn, DateTime ChangedUtc)
{
    /// <summary>The only <c>FeedStatus.Status</c> value that means the flight feed is up (CAPI-04).</summary>
    public const string UpStatus = "OK";

    /// <summary>Whether the flight feed is up (FDE-02); every other status, and none, means detected down.</summary>
    public bool FeedUp => IsUp(Status);

    public static bool IsUp(string? status) => status == UpStatus;

    /// <summary>
    /// Whether a reported status says the feed is up, or null when no status was reported (no answer, an error, or
    /// an answer without one). Not knowing is not the same as detected down, so the editor never takes it for
    /// flight Outage mode (FDE-02).
    /// </summary>
    public static bool? UpOrUnknown(string? status) => string.IsNullOrWhiteSpace(status) ? null : IsUp(status);
}

/// <summary>
/// Reads the current tenant's <see cref="CoreApiFeedState"/>. Implemented by the host that owns the tenant's
/// database; resolve it from the scope doing the work (a circuit, a request or a worker tenant scope).
/// </summary>
public interface ICoreApiFeedStateReader
{
    /// <summary>The tenant's feed state, or null before the watcher has first reached a decision.</summary>
    Task<CoreApiFeedState?> ReadAsync(CancellationToken ct);
}
