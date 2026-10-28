namespace DXSuite.Shared.Services;

/// <summary>
/// Cross-module interface for accessing device data. Implemented by Director,
/// consumed by Sentinel for device sync without direct DB coupling.
/// </summary>
public interface IDeviceDataProvider
{
    Task<IReadOnlyList<DeviceSyncData>> GetDevicesForSyncAsync();
    Task<DeviceSyncData?> GetDeviceByIdAsync(int deviceId);
}

/// <summary>
/// Lightweight DTO for cross-module device data exchange.
/// Contains everything Sentinel needs for sync without touching Director's DB directly.
/// </summary>
public class DeviceSyncData
{
    public int DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? LocationName { get; set; }
    public int? LocationId { get; set; }
    public DateTime? LastHeartbeat { get; set; }
    public bool IsOnline { get; set; }
    public string? ClientVersion { get; set; }
    public string? MacAddress { get; set; }
}
