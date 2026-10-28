namespace DXSuite.Connectors.Services.Itsm;

/// <summary>
/// Default no-op implementation when no module provides ITSM connection data.
/// Sentinel module overrides this registration when enabled.
/// </summary>
public class NoOpItsmConnectionProvider : IItsmConnectionProvider
{
    public Task<ItsmConnectionInfo?> GetConnectionAsync(string adapterType, CancellationToken cancellationToken = default)
        => Task.FromResult<ItsmConnectionInfo?>(null);

    public Task<ItsmConnectionInfo?> GetConnectionByCustomerAsync(string customerId, CancellationToken cancellationToken = default)
        => Task.FromResult<ItsmConnectionInfo?>(null);
}
