using DXSuite.Shared.Services;
using Microsoft.Extensions.Logging;

namespace DXSuite.Connectors.Services.Connectors;

public class CloudPlatformConnectorRegistry : ICloudPlatformConnectorRegistry
{
    private readonly Dictionary<string, ICloudPlatformConnector> _connectors;
    private readonly IConnectorConfigProvider? _configProvider;
    private readonly ConnectorConfigProtector _configProtector;
    private readonly ILogger<CloudPlatformConnectorRegistry> _logger;

    public CloudPlatformConnectorRegistry(
        IEnumerable<ICloudPlatformConnector> connectors,
        ConnectorConfigProtector configProtector,
        ILogger<CloudPlatformConnectorRegistry> logger,
        IConnectorConfigProvider? configProvider = null)
    {
        // Deduplicate by PlatformId — HttpClient factory can produce duplicate registrations
        _connectors = connectors
            .GroupBy(c => c.PlatformId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _configProtector = configProtector;
        _logger = logger;
        _configProvider = configProvider;

        foreach (var c in _connectors)
            _logger.LogInformation("Registered cloud platform connector: {PlatformId} ({DisplayName})", c.Key, c.Value.DisplayName);
    }

    public ICloudPlatformConnector? GetConnector(string platformId)
        => _connectors.TryGetValue(platformId, out var connector) ? connector : null;

    public IReadOnlyCollection<string> GetRegisteredPlatforms()
        => _connectors.Keys.ToList().AsReadOnly();

    public async Task<PlatformConnectionConfig?> ResolveConfigAsync(string platformId)
    {
        if (_configProvider == null)
        {
            _logger.LogDebug("No connector config provider registered");
            return null;
        }

        try
        {
            var record = await _configProvider.GetFirstEnabledConfigAsync(platformId);
            if (record == null)
            {
                _logger.LogDebug("No enabled connector found for platform {PlatformId}", platformId);
                return null;
            }
            return _configProtector.Decrypt(record.ConfigJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve config for platform {PlatformId}", platformId);
            return null;
        }
    }
}
