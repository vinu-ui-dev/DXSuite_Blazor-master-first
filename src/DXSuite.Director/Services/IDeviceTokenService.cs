using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

public record DeviceTokenCreationResult(TblDeviceToken Token, string PlaintextToken);

public class DeviceTokenValidationResult
{
    public bool IsValid { get; set; }
    public int? DeviceTokenId { get; set; }
    public string? DeviceId { get; set; }
    public string? Permissions { get; set; }
    public string? InvalidReason { get; set; }
}

public interface IDeviceTokenService
{
    /// <summary>
    /// Validates a device token (dt_xxx value) and returns result.
    /// </summary>
    Task<DeviceTokenValidationResult> ValidateDeviceTokenAsync(string token);

    /// <summary>
    /// Creates a new device token and returns the plaintext value once.
    /// </summary>
    Task<DeviceTokenCreationResult> CreateDeviceTokenAsync(Guid? pairingRequestId, int? approvedByUserId);

    /// <summary>
    /// Returns all device tokens for admin management.
    /// </summary>
    Task<List<TblDeviceToken>> GetAllDeviceTokensAsync();

    /// <summary>
    /// Revokes a device token (sets IsActive = false).
    /// </summary>
    Task RevokeDeviceTokenAsync(int deviceTokenId);

    /// <summary>Revokes every other active token bound to the device. Returns how many.</summary>
    Task<int> RevokeOtherTokensForDeviceAsync(string deviceId, int keepDeviceTokenId);

    /// <summary>
    /// Reactivates a previously revoked device token.
    /// </summary>
    Task ReactivateDeviceTokenAsync(int deviceTokenId);

    /// <summary>
    /// Permanently deletes a device token.
    /// </summary>
    Task DeleteDeviceTokenAsync(int deviceTokenId);

    /// <summary>
    /// Records last-used timestamp (fire-and-forget from middleware).
    /// </summary>
    Task RecordUsageAsync(int deviceTokenId);

    /// <summary>
    /// Binds a device token to a Director device after the first heartbeat.
    /// </summary>
    Task BindToDeviceAsync(int deviceTokenId, string deviceId);
}
