using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Connectors.Services.Connectors;

namespace DXSuite.Director.Services;

/// <summary>
/// Director's implementation of IConnectorConfigProvider.
/// Provides cloud connector configuration from TblCloudPlatformConnector to the Connectors module.
/// </summary>
public class DirectorConnectorConfigProvider : IConnectorConfigProvider
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DirectorConnectorConfigProvider(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<ConnectorConfigRecord?> GetFirstEnabledConfigAsync(string platformId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var connector = await db.TblCloudPlatformConnectors
            .AsNoTracking()
            .Where(c => c.PlatformId == platformId && c.IsEnabled)
            .OrderBy(c => c.ConnectorId)
            .FirstOrDefaultAsync(cancellationToken);

        if (connector == null) return null;

        return new ConnectorConfigRecord
        {
            ConnectorId = connector.ConnectorId,
            PlatformId = connector.PlatformId,
            ConfigJson = connector.ConfigJson,
            IsEnabled = connector.IsEnabled,
            DisplayName = connector.DisplayName
        };
    }
}
