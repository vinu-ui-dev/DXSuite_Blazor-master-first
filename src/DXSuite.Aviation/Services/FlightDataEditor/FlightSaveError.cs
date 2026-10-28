using DXSuite.Connectors.Services.CoreApi;

namespace DXSuite.Aviation.Services.FlightDataEditor;

/// <summary>
/// What the Flight Data Editor's forms and the review say when CoreAPI did not take a save or an action (FDE-06:
/// nothing fails silently). A refusal, such as turning a Preserved Edit back into an Outage Edit (C-20), comes back as
/// CoreAPI's own error text.
/// </summary>
public static class FlightSaveError
{
    public static string Describe<T>(CoreApiResult<T> result) => Describe(result, "Could not save the flight");

    /// <summary>
    /// The editor's one wording of a failed CoreAPI call, for saves, loads and actions alike. The reason is the one
    /// the call's User event records (<see cref="CoreApiEventOutcome.Error"/>), so page and log say the same.
    /// </summary>
    /// <param name="couldNot">What failed, as the start of the sentence, e.g. "Could not preserve AC100".</param>
    public static string Describe<T>(CoreApiResult<T> result, string couldNot)
    {
        var reason = CoreApiEventOutcome.Error(result) ?? "";
        return $"{couldNot}: {(result.Outcome == CoreApiOutcome.Failed ? $"CoreAPI answered {reason}".TrimEnd() : reason)}.";
    }
}
