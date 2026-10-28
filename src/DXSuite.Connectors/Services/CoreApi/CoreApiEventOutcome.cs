using DXSuite.Shared.Audit;

namespace DXSuite.Connectors.Services.CoreApi;

/// <summary>How a CoreAPI answer reads on a User event of the action that made the call (AUD-08).</summary>
public static class CoreApiEventOutcome
{
    /// <summary>
    /// CoreAPI saying no (a 4xx, such as C-20's refusal) is Refused; no answer, no connection or a server error is
    /// Failed.
    /// </summary>
    public static UserEventOutcome Of<T>(CoreApiResult<T> result) => result.Outcome switch
    {
        CoreApiOutcome.Succeeded => UserEventOutcome.Succeeded,
        CoreApiOutcome.Failed when result.StatusCode is { } status && (int)status is >= 400 and < 500 => UserEventOutcome.Refused,
        _ => UserEventOutcome.Failed,
    };

    /// <summary>The reason for a refused or failed call, so its row explains itself; null when it succeeded.</summary>
    public static string? Error<T>(CoreApiResult<T> result) => result.Outcome switch
    {
        CoreApiOutcome.Succeeded => null,
        CoreApiOutcome.Unreachable => "no CoreAPI server answered",
        CoreApiOutcome.NotConfigured => "the CoreAPI connection is not set up",
        _ => $"{(result.StatusCode is { } status ? (int)status + " " : "")}{result.Error}".Trim(),
    };

    /// <summary>Adds <c>error</c> to an event's details when the call did not succeed.</summary>
    public static void AddError<T>(IDictionary<string, object?> details, CoreApiResult<T> result)
    {
        if (Error(result) is { } error) details["error"] = error;
    }
}
