namespace DXSuite.Aviation.Services.CounterAllocation;

/// <summary>
/// Whether counter allocation is in manual (Outage) mode (GTAA FSD 1.2 section 3.4), simplified to a single
/// per-process switch: this build has no real CoreAPI/RMS allocation feed to detect going down or come back
/// from, so there is no automatic return — a person turns manual mode on and off directly. Held in memory
/// (not the database) because it is process-local, advisory state, not a record that needs to survive a
/// restart; a restart finding it back off is the safe default.
/// </summary>
public sealed class CounterAllocationOutageState
{
    public bool IsOn { get; private set; }

    public DateTime? SwitchedOnUtc { get; private set; }

    public void SwitchOn()
    {
        IsOn = true;
        SwitchedOnUtc = DateTime.UtcNow;
    }

    public void SwitchOff()
    {
        IsOn = false;
        SwitchedOnUtc = null;
    }
}
