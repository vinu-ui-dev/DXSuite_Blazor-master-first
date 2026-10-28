using DXSuite.Connectors.Services.CoreApi.Models;

namespace DXSuite.Aviation.Services.FlightDataEditor;

/// <summary>
/// The "Keep after the feed returns" box on Add and Manage (FDE-04), which decides the kind a save makes: a Preserved
/// Edit stays after the feed returns, an Outage Edit waits for the return review (FDE-08).
/// The box takes the feed mode as the form opens, so a save makes the kind the user saw offered.
/// </summary>
/// <param name="Shown">Only during flight Outage mode; while the feed is trusted there is no choice.</param>
/// <param name="Ticked">Ticked saves a Preserved Edit, unticked an Outage Edit.</param>
/// <param name="Locked">A Preserved Edit cannot go back to an Outage Edit (C-20).</param>
public readonly record struct KeepAfterFeedReturns(bool Shown, bool Ticked, bool Locked)
{
    public const string Label = "Keep after the feed returns";

    public const string LockedNote = "Kept edits can only be cleared";

    /// <summary>
    /// While the feed is trusted every save is Preserved, which also turns a leftover Outage Edit into a Preserved
    /// one. In Outage mode the box opens unticked, except on a Preserved Edit, where it is ticked and locked.
    /// </summary>
    public static KeepAfterFeedReturns Open(FlightFeedMode mode, FlightEditKind current) =>
        mode.Trusted ? new(Shown: false, Ticked: true, Locked: false)
        : current == FlightEditKind.PreservedEdit ? new(Shown: true, Ticked: true, Locked: true)
        : new(Shown: true, Ticked: false, Locked: false);

    public static KeepAfterFeedReturns ForAdd(FlightFeedMode mode) => Open(mode, FlightEditKind.None);

    public static KeepAfterFeedReturns ForManage(FlightFeedMode mode, FEDepartureFlight flight) =>
        Open(mode, FlightListCue.For(flight).Edit);

    public static KeepAfterFeedReturns ForManage(FlightFeedMode mode, FEArrivalFlight flight) =>
        Open(mode, FlightListCue.For(flight).Edit);

    /// <summary>The box after the user ticks or unticks it; a hidden or locked box does not change.</summary>
    public KeepAfterFeedReturns Tick(bool ticked) => Shown && !Locked ? this with { Ticked = ticked } : this;

    /// <summary>Whether a save makes a Preserved Edit.</summary>
    public bool Preserved => !Shown || Ticked;

    /// <summary>The kind of Flight Edit a save makes.</summary>
    public FlightEditKind Kind => Preserved ? FlightEditKind.PreservedEdit : FlightEditKind.OutageEdit;

    /// <summary>A Flight Edit carries its kind as <c>Preserved</c> (IF-02).</summary>
    public void WriteKind(OverrideDepartureFlight edit) => edit.Preserved = Preserved;

    /// <inheritdoc cref="WriteKind(OverrideDepartureFlight)"/>
    public void WriteKind(OverrideArrivalFlight edit) => edit.Preserved = Preserved;

    /// <summary>
    /// Add Flight carries the kind (C-19). C-19 leaves the field to CoreAPI and is still open, so Director sets the
    /// flight's own <c>preserveOverride</c>, the field CoreAPI reports the kind in (A-25); CoreAPI's answer changes
    /// only these two lines.
    /// </summary>
    public void WriteKind(FEDepartureFlight flight) => flight.preserveOverride = Preserved;

    /// <inheritdoc cref="WriteKind(FEDepartureFlight)"/>
    public void WriteKind(FEArrivalFlight flight) => flight.preserveOverride = Preserved;
}
