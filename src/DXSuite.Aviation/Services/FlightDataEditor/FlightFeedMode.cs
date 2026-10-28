namespace DXSuite.Aviation.Services.FlightDataEditor;

/// <summary>
/// Whether the flight feed is trusted or in flight Outage mode (FDE-02, FDE-03). This one rule decides the edit kind
/// a save makes (FDE-04, <see cref="KeepAfterFeedReturns"/>) and when the field settings gate the edit forms (FDE-05).
/// </summary>
/// <param name="FeedUp">
/// Whether CoreAPI's <c>FeedStatus.Status</c> says the flight feed is up; null while no status is known.
/// </param>
/// <param name="SwitchAllowed">The instance setting <see cref="FlightDataEditorOptions.AllowOutageModeSwitch"/>.</param>
/// <param name="SwitchOn">CoreAPI's flight Outage mode switch as last reported; unknown counts as off.</param>
public readonly record struct FlightFeedMode(bool? FeedUp, bool SwitchAllowed, bool SwitchOn)
{
    /// <summary>
    /// The switch counts only where the instance allows it. Elsewhere the CoreAPI watcher turns it off, so a switch
    /// still reported on is a moment's leftover, not a user's choice.
    /// </summary>
    public bool OutageSwitchOn => SwitchAllowed && SwitchOn;

    /// <summary>
    /// Not detected down, and the switch off or not allowed: every edit is a Preserved Edit. A feed status not known
    /// counts as trusted, so the field settings keep gating the fields; only a detected outage opens them (the
    /// same principle as an unreachable CoreAPI, FDE-11).
    /// </summary>
    public bool Trusted => FeedUp != false && !OutageSwitchOn;

    /// <summary>Detected down, or switched on where allowed: the user chooses each edit's kind.</summary>
    public bool OutageMode => !Trusted;
}
