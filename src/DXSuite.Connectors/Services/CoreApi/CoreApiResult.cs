using System.Net;

namespace DXSuite.Connectors.Services.CoreApi;

public enum CoreApiOutcome
{
    /// <summary>A server answered with a success status and a readable body.</summary>
    Succeeded,

    /// <summary>A server answered, but with an error status or a body that could not be read.</summary>
    Failed,

    /// <summary>Every configured server failed to answer within its time (CAPI-06).</summary>
    Unreachable,

    /// <summary>No key or no server is saved for the tenant, or the saved key does not unprotect on this host.</summary>
    NotConfigured
}

/// <summary>
/// The result of one CoreAPI call. <see cref="Value"/> is set only when the call succeeded, so a caller that
/// only needs the data can read it and treat null as "no data".
/// </summary>
public sealed record CoreApiResult<T>(
    CoreApiOutcome Outcome,
    T? Value = default,
    HttpStatusCode? StatusCode = null,
    string? Error = null,
    string? Server = null)
{
    public bool Succeeded => Outcome == CoreApiOutcome.Succeeded;

    /// <summary>True when a CoreAPI server answered, whatever it said.</summary>
    public bool Reached => Outcome is CoreApiOutcome.Succeeded or CoreApiOutcome.Failed;

    /// <summary>
    /// The servers this call tried, in the order it tried them, ending with the one that answered when one did.
    /// The watcher logs them with <c>coreapi.unreachable</c> and <c>coreapi.reachable</c> (AUD-08).
    /// </summary>
    public IReadOnlyList<string> ServersTried { get; init; } = [];
}
