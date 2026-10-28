using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Connectors.Services.CoreApi.Models;

namespace DXSuite.Aviation.Services.FlightDataEditor;

/// <summary>The kind of Flight Edit a flight carries, as the list's first icon slot shows it.</summary>
public enum FlightEditKind
{
    None,
    OutageEdit,
    PreservedEdit,
}

/// <summary>
/// What the Flight Data Editor list's two icon slots show for one flight (FDE-09): the Flight Edit's kind, and the
/// Display Categories the flight is hidden from.
/// </summary>
public sealed record FlightListCue(FlightEditKind Edit, IReadOnlyList<DisplayCategory> HiddenFrom)
{
    public static IReadOnlyList<DisplayCategory> AllCategories => RollCategories.All;

    public int HiddenCount => HiddenFrom.Count;

    public bool HiddenEverywhere => HiddenCount == AllCategories.Count;

    public bool IsHiddenFrom(DisplayCategory category) => HiddenFrom.Contains(category);

    public static FlightListCue For(FEDepartureFlight flight) => Build(flight.preserveOverride, flight.overrideFlightData);

    public static FlightListCue For(FEArrivalFlight flight) => Build(flight.preserveOverride, flight.overrideFlightData);

    /// <summary>
    /// Whether the list marks the flight with the Outage Edit triangle; the review offers exactly these flights
    /// (FDE-08).
    /// </summary>
    public static bool IsOutageEdit(FEDepartureFlight? flight) => flight is not null && For(flight).Edit == FlightEditKind.OutageEdit;

    /// <inheritdoc cref="IsOutageEdit(FEDepartureFlight?)"/>
    public static bool IsOutageEdit(FEArrivalFlight? flight) => flight is not null && For(flight).Edit == FlightEditKind.OutageEdit;

    // Only categories a user rolled off count (FDE-09), so only the Flight Edit's flags are read: the feed's flags,
    // set or not, never give a cue, and a flight CoreAPI's own timing took off screens has none.
    private static FlightListCue Build(bool? preserveOverride, IRollFlags? edit)
    {
        var kind = edit is null ? FlightEditKind.None
            : preserveOverride == true ? FlightEditKind.PreservedEdit
            : FlightEditKind.OutageEdit;
        DisplayCategory[] hidden = edit is null ? [] : AllCategories.Where(c => RollCategories.IsRolledOff(RollCategories.Flag(edit, c))).ToArray();
        return new FlightListCue(kind, hidden);
    }
}
