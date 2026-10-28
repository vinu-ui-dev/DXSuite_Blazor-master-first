using Microsoft.Extensions.Logging;

namespace DXSuite.Sentinel.Services.Drivers;

/// <summary>
/// Registry of available device drivers, resolved by protocol ID.
/// New drivers register themselves here and any descriptor referencing that protocol will use it.
/// </summary>
public class DeviceDriverRegistry
{
    private readonly Dictionary<string, IDeviceDriver> _drivers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<DeviceDriverRegistry> _logger;

    public DeviceDriverRegistry(IEnumerable<IDeviceDriver> drivers, ILogger<DeviceDriverRegistry> logger)
    {
        _logger = logger;
        foreach (var driver in drivers)
        {
            _drivers[driver.ProtocolId] = driver;
            _logger.LogInformation("Registered device driver: {ProtocolId}", driver.ProtocolId);
        }
    }

    public IDeviceDriver? GetDriver(string protocolId)
    {
        return _drivers.TryGetValue(protocolId, out var driver) ? driver : null;
    }

    public IReadOnlyCollection<string> GetRegisteredProtocols()
    {
        return _drivers.Keys.ToList().AsReadOnly();
    }
}
