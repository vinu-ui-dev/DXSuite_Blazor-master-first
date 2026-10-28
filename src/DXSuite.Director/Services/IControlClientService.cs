using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

public interface IControlClientService
{
    Task<List<TblControlClient>> GetAllClientsAsync();

    Task<TblControlClient?> GetClientByIdAsync(int controlClientId);

    Task<TblControlClient> CreateClientAsync(string clientName, string authMode, int locationId, string? accessCode, int? createdBy);

    Task UpdateClientAsync(int controlClientId, string clientName, int locationId, bool isActive);

    Task SetAccessCodeAsync(int controlClientId, string accessCode);

    Task<bool> ValidateAccessCodeAsync(int controlClientId, string accessCode, string? clientIp);

    Task SetDisplayAssignmentsAsync(int controlClientId, List<int> displayIds);

    Task<List<TblDisplay>> GetAssignedDisplaysAsync(int controlClientId);

    Task<List<TblDisplay>> GetEffectiveDisplaysAsync(int controlClientId);

    Task DeleteClientAsync(int controlClientId);

    /// <summary>
    /// Sets an override on a display, validates that the control client has access.
    /// </summary>
    Task SetDisplayOverrideAsync(int controlClientId, int displayId, int assetId);

    /// <summary>
    /// Clears the override on a display, validates that the control client has access.
    /// </summary>
    Task ClearDisplayOverrideAsync(int controlClientId, int displayId);

    /// <summary>
    /// Returns assets available at the client's location.
    /// </summary>
    Task<List<TblAsset>> GetAvailableAssetsAsync(int controlClientId);
}
