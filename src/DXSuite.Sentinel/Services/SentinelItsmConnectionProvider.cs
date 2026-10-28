using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DXSuite.Sentinel.Entities;
using DXSuite.Connectors.Services.Itsm;

namespace DXSuite.Sentinel.Services;

/// <summary>
/// Sentinel's implementation of IItsmConnectionProvider.
/// Provides ITSM connection configuration from the Sentinel database to the Connectors module.
/// </summary>
public class SentinelItsmConnectionProvider : IItsmConnectionProvider
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SentinelItsmConnectionProvider(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<ItsmConnectionInfo?> GetConnectionAsync(string adapterType, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

        var connection = await db.ItsmConnections
            .AsNoTracking()
            .Where(c => c.AdapterType == adapterType && c.IsActive)
            .OrderBy(c => c.Priority)
            .FirstOrDefaultAsync(cancellationToken);

        return MapToInfo(connection);
    }

    public async Task<ItsmConnectionInfo?> GetConnectionByCustomerAsync(string customerId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

        var connection = await db.ItsmConnections
            .AsNoTracking()
            .Where(c => c.CustomerId == customerId && c.IsActive)
            .OrderBy(c => c.Priority)
            .FirstOrDefaultAsync(cancellationToken);

        return MapToInfo(connection);
    }

    private static ItsmConnectionInfo? MapToInfo(ItsmConnection? connection)
    {
        if (connection == null) return null;

        return new ItsmConnectionInfo
        {
            ConnectionId = connection.ConnectionId,
            AdapterType = connection.AdapterType,
            Server = connection.Server,
            UserName = connection.UserName,
            Password = connection.Password,
            Token = connection.Token,
            ConfigJson = connection.ConfigJson,
            CustomerId = connection.CustomerId,
            IsActive = connection.IsActive,
            Priority = connection.Priority
        };
    }
}
