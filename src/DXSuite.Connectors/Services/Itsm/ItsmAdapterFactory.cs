using DXSuite.Shared.Services;
using Microsoft.Extensions.Logging;

namespace DXSuite.Connectors.Services.Itsm;

/// <summary>
/// Resolves the correct ITSM adapter for a customer by reading their integration
/// configuration from IItsmConnectionProvider. Adapters are registered via DI and
/// indexed by PlatformId for O(1) lookup.
/// </summary>
public class ItsmAdapterFactory : IItsmAdapterFactory
{
    private readonly Dictionary<string, IItsmAdapter> _adapters;
    private readonly IItsmConnectionProvider _connectionProvider;
    private readonly ILogger<ItsmAdapterFactory> _logger;

    public ItsmAdapterFactory(
        IEnumerable<IItsmAdapter> adapters,
        IItsmConnectionProvider connectionProvider,
        ILogger<ItsmAdapterFactory> logger)
    {
        _adapters = adapters.ToDictionary(a => a.PlatformId, StringComparer.OrdinalIgnoreCase);
        _connectionProvider = connectionProvider;
        _logger = logger;
    }

    public async Task<IItsmAdapter?> GetAdapterAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionProvider.GetConnectionByCustomerAsync(customerId, cancellationToken);
        if (connection == null)
        {
            _logger.LogDebug("No ITSM connection configured for customer {CustomerId}", customerId);
            return null;
        }
        return GetAdapterByPlatform(connection.AdapterType);
    }

    public IItsmAdapter? GetAdapterByPlatform(string platformId)
    {
        if (_adapters.TryGetValue(platformId, out var adapter))
            return adapter;
        _logger.LogWarning("No ITSM adapter registered for platform: {PlatformId}", platformId);
        return null;
    }
}
