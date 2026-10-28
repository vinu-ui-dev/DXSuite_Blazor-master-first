using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Connectors.Services.CoreApi.Models;

namespace DXSuite.Aviation.Services.FlightDataEditor;

/// <summary>
/// Which fields of an existing flight the Manage form opens (FDE-05). Destination, origin and codeshares are never
/// open on an existing flight (D-03; codeshares follow Diagram 8), so they have no gate.
/// </summary>
/// <param name="Gate">Departures only.</param>
/// <param name="Belt">Arrivals only (CoreAPI's <c>CAR</c>).</param>
public readonly record struct FlightFieldGates(
    bool EstimatedTime,
    bool ActualTime,
    bool Status,
    bool Gate,
    bool Belt,
    bool Public,
    bool Baggage,
    bool Staff,
    bool Throw)
{
    public static FlightFieldGates AllOpen { get; } = new(true, true, true, true, true, true, true, true, true);

    /// <summary>While CoreAPI is unreachable every field is closed, whatever the feed's mode (FDE-11).</summary>
    public static FlightFieldGates AllClosed { get; } = default;

    /// <summary>The Display Categories whose Roll On / Off box is open.</summary>
    public bool Opens(DisplayCategory category) => new RollCategories(Public, Baggage, Staff, Throw).IsShown(category);

    /// <summary>
    /// While the feed is trusted the instance-wide field settings decide, the same way for arrivals and departures,
    /// and a setting CoreAPI leaves unset keeps its field closed. During flight Outage mode every field is open,
    /// because the feed can no longer be relied on to correct them.
    /// </summary>
    public static FlightFieldGates For(FlightFeedMode mode, UserSettings settings)
    {
        if (mode.OutageMode) return AllOpen;
        var categories = RollCategories.Of(c => RollCategories.ModifySetting(settings, c) == true);
        return new(
            EstimatedTime: settings.ETModify == true,
            ActualTime: settings.ATModify == true,
            Status: settings.RCPModify == true,
            Gate: settings.GATModify == true,
            Belt: settings.CARModify == true,
            Public: categories.Public,
            Baggage: categories.Baggage,
            Staff: categories.Staff,
            Throw: categories.Throw);
    }
}
