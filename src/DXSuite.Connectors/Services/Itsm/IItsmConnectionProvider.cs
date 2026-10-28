namespace DXSuite.Connectors.Services.Itsm;

/// <summary>
/// Provides ITSM connection configuration without coupling to a specific DB context.
/// Sentinel module will implement this to provide ItsmConnection records.
/// </summary>
public interface IItsmConnectionProvider
{
    Task<ItsmConnectionInfo?> GetConnectionAsync(string adapterType, CancellationToken cancellationToken = default);
    Task<ItsmConnectionInfo?> GetConnectionByCustomerAsync(string customerId, CancellationToken cancellationToken = default);
}

public class ItsmConnectionInfo
{
    public int ConnectionId { get; set; }
    public string AdapterType { get; set; } = string.Empty;
    public string? Server { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? Token { get; set; }
    public string? ConfigJson { get; set; }
    public string? CustomerId { get; set; }
    public bool IsActive { get; set; }
    public int Priority { get; set; }
}
