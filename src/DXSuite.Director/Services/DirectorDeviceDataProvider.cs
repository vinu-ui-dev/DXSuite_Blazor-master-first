using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Shared.Services;

namespace DXSuite.Director.Services;

/// <summary>
/// Director's implementation of IDeviceDataProvider.
/// Provides device data from the Director database for cross-module consumption (e.g., Sentinel sync).
/// </summary>
public class DirectorDeviceDataProvider : IDeviceDataProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DirectorDeviceDataProvider> _logger;

    public DirectorDeviceDataProvider(
        IServiceScopeFactory scopeFactory,
        ILogger<DirectorDeviceDataProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DeviceSyncData>> GetDevicesForSyncAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
        var onlineThreshold = await GetOnlineThresholdAsync(scope);

        var now = DateTime.UtcNow;
        var devices = await db.TblDevices
            .AsNoTracking()
            .Include(d => d.Location)
            .Include(d => d.Display)
            .ToListAsync();

        return devices.Select(d => MapToSyncData(d, now, onlineThreshold)).ToList();
    }

    // Offline detection must track the configured poll cadence or a slower fleet
    // pace would mark every healthy device offline
    private static async Task<TimeSpan> GetOnlineThresholdAsync(IServiceScope scope)
    {
        var settings = await scope.ServiceProvider
            .GetRequiredService<IPlayerSyncSettingsService>()
            .GetAsync();
        return settings.OnlineThreshold;
    }

    public async Task<DeviceSyncData?> GetDeviceByIdAsync(int deviceId)
    {
        // Director uses string DeviceId, but the interface uses int.
        // Map by finding the device at the numeric position or by parsing.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var idStr = deviceId.ToString();
        var d = await db.TblDevices
            .AsNoTracking()
            .Include(d => d.Location)
            .Include(d => d.Display)
            .FirstOrDefaultAsync(d => d.DeviceId == idStr);

        if (d == null) return null;

        return MapToSyncData(d, DateTime.UtcNow, await GetOnlineThresholdAsync(scope));
    }

    private static DeviceSyncData MapToSyncData(TblDevice d, DateTime now, TimeSpan onlineThreshold)
    {
        return new DeviceSyncData
        {
            DeviceId = int.TryParse(d.DeviceId, out var id) ? id : 0,
            DeviceName = d.Description ?? d.DeviceId,
            LocationId = d.LocationId,
            LocationName = d.Location?.LocationName,
            Manufacturer = d.Manufacturer,
            Model = d.Model,
            SerialNumber = d.SerialNumber,
            IpAddress = d.Ipaddress,
            MacAddress = d.Macaddress,
            LastHeartbeat = d.Heartbeat,
            IsOnline = (now - d.Heartbeat) < onlineThreshold,
            ClientVersion = d.ClientAppVersion
        };
    }
}
