namespace DXSuite.Connectors.Services.Connectors;

/// <summary>
/// Provides cloud connector configuration without coupling to a specific DB context.
/// Director module will implement this to provide TblCloudPlatformConnector records.
/// </summary>
public interface IConnectorConfigProvider
{
    Task<ConnectorConfigRecord?> GetFirstEnabledConfigAsync(string platformId, CancellationToken cancellationToken = default);
}

public class ConnectorConfigRecord
{
    public int ConnectorId { get; set; }
    public string PlatformId { get; set; } = string.Empty;
    public string? ConfigJson { get; set; }
    public bool IsEnabled { get; set; }
    public string? DisplayName { get; set; }
}
