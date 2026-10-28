using DXSuite.Design.Entities;

namespace DXSuite.Design.Services;

/// <summary>Loads the active NetSuite connection record for the current tenant.</summary>
public interface INetsuiteConnectionProvider
{
    Task<TblDesignNetsuiteConnection?> GetActiveConnectionAsync(CancellationToken ct = default);
}
