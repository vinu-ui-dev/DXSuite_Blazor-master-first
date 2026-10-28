namespace DXSuite.Shared.Services;

/// <summary>
/// Exposes Sentinel subsystem availability to all modules.
/// True only when Sentinel is enabled in config AND the Sentinel DB is reachable.
/// </summary>
public interface ISentinelAvailabilityService
{
    bool IsAvailable { get; }
    Task<bool> CheckAvailabilityAsync();
}
