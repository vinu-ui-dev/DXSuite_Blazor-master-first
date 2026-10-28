namespace DXSuite.Aviation.Services.FlightDataEditor;

/// <summary>
/// The Flight Data Editor's per-instance settings (1.1 section 3.10), bound from <see cref="SectionName"/>.
/// Every node behind a load balancer carries the same values (HOST-04).
/// </summary>
public sealed class FlightDataEditorOptions
{
    public const string SectionName = "Aviation:FlightDataEditor";

    /// <summary>
    /// Whether users may switch flight Outage mode on and off (FDE-03); default false, and false at GTAA. Where it
    /// is false, the CoreAPI watcher keeps CoreAPI's switch off.
    /// </summary>
    public bool AllowOutageModeSwitch { get; set; }
}
