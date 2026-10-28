using DXSuite.Connectors.Services.CoreApi.Models;

namespace DXSuite.Aviation.Services.FlightDataEditor;

/// <summary>
/// The editable fields of an existing flight other than its roll flags (those are <c>RollCategories</c>):
/// estimated time, actual time, status, and gate (departures) or belt (arrivals) as <paramref name="Place"/>.
/// The Manage form reads them as it opens and a save compares against them, so they are also the old values of the
/// edit's audit record (FDE-06).
/// </summary>
public sealed record FlightFieldValues(DateTime? Estimated, DateTime? Actual, string? Status, string? Place)
{
    /// <summary>The values screens show now: the Flight Edit's where it carries one, else the feed's.</summary>
    public static FlightFieldValues Current(FEDepartureFlight flight)
    {
        var edit = flight.overrideFlightData;
        return new(edit?.ETD ?? flight.EstimatedTime, edit?.ATD ?? flight.ActualTime, edit?.RCP ?? flight.RCP, edit?.GAT ?? flight.GAT);
    }

    /// <inheritdoc cref="Current(FEDepartureFlight)"/>
    public static FlightFieldValues Current(FEArrivalFlight flight)
    {
        var edit = flight.overrideFlightData;
        return new(edit?.ETA ?? flight.EstimatedTime, edit?.ATA ?? flight.ActualTime, edit?.RCP ?? flight.RCP, edit?.CAR ?? flight.CAR);
    }

    /// <summary>
    /// Writes a Flight Edit's fields for going from these values (as the form opened) to <paramref name="chosen"/>:
    /// a changed field gets its new value, an unchanged one null, which the wire DTOs omit (CAPI-07).
    /// </summary>
    public void WriteChanges(FlightFieldValues chosen, OverrideDepartureFlight edit)
    {
        var changes = ChangesTo(chosen);
        (edit.ETD, edit.ATD, edit.RCP, edit.GAT) = (changes.Estimated, changes.Actual, changes.Status, changes.Place);
    }

    /// <inheritdoc cref="WriteChanges(FlightFieldValues, OverrideDepartureFlight)"/>
    public void WriteChanges(FlightFieldValues chosen, OverrideArrivalFlight edit)
    {
        var changes = ChangesTo(chosen);
        (edit.ETA, edit.ATA, edit.RCP, edit.CAR) = (changes.Estimated, changes.Actual, changes.Status, changes.Place);
    }

    /// <summary>
    /// The chosen values that differ from these; the others null. An empty status is the form's "Select Status",
    /// not a value, and gates and belts compare as CoreAPI stores them (upper case, trimmed).
    /// </summary>
    public FlightFieldValues ChangesTo(FlightFieldValues chosen)
    {
        var status = string.IsNullOrWhiteSpace(chosen.Status) ? Status : chosen.Status;
        var place = Code(chosen.Place);
        return new(
            chosen.Estimated == Estimated ? null : chosen.Estimated,
            chosen.Actual == Actual ? null : chosen.Actual,
            status == Status ? null : status,
            place == Code(Place) ? null : place ?? "");
    }

    private static string? Code(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
