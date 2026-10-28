using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

public class PairingRequestResult
{
    public Guid PairingRequestId { get; set; }
    public string PairingCode { get; set; } = null!;
    public int ExpiresInSeconds { get; set; }
}

public class PairingStatusResult
{
    public string Status { get; set; } = null!;
    public string? DeviceToken { get; set; }
}

public interface IDevicePairingService
{
    /// <summary>
    /// Creates a new pairing request with a random 6-digit code.
    /// Throws <see cref="DeviceBlockedException"/> if the device is on the deny blocklist.
    /// </summary>
    Task<PairingRequestResult> CreatePairingRequestAsync(
        string? deviceId, string? manufacturer, string? model,
        string? serialNumber, string? ipAddress, string? macAddress, string? platform = null);

    /// <summary>
    /// Checks pairing status and returns token if approved.
    /// </summary>
    Task<PairingStatusResult> GetPairingStatusAsync(Guid pairingRequestId);

    /// <summary>
    /// Admin approves a pending request by entering the 6-digit code.
    /// Returns the pairing request that was approved.
    /// </summary>
    Task<TblDevicePairingRequest> ApprovePairingAsync(string pairingCode, int approvedByUserId);

    /// <summary>
    /// Admin denies a specific pending request.
    /// </summary>
    Task DenyPairingAsync(Guid pairingRequestId, int deniedByUserId);

    /// <summary>
    /// Returns all pending pairing requests for the admin UI.
    /// </summary>
    Task<List<TblDevicePairingRequest>> GetPendingRequestsAsync();

    /// <summary>
    /// Checks if a device is on the deny blocklist by matching DeviceId or MacAddress.
    /// </summary>
    Task<bool> IsDeviceBlockedAsync(string? deviceId, string? macAddress);

    /// <summary>
    /// Returns all denied pairing requests (the blocklist) for admin review.
    /// </summary>
    Task<List<TblDevicePairingRequest>> GetDeniedRequestsAsync();

    /// <summary>
    /// Removes a denied record, unblocking the device so it can pair again.
    /// </summary>
    Task UnblockDeviceAsync(Guid pairingRequestId, int unblockedByUserId);

    /// <summary>
    /// Expires stale pending requests (called by cleanup service).
    /// </summary>
    Task<int> ExpireStaleRequestsAsync();

    /// <summary>
    /// Purges resolved requests older than the retention period.
    /// Denied records are excluded so the blocklist persists.
    /// </summary>
    Task<int> PurgeOldRequestsAsync(TimeSpan retention);
}

/// <summary>
/// Thrown when a blocked device attempts to create a pairing request.
/// </summary>
public class DeviceBlockedException : Exception
{
    public DeviceBlockedException(string message) : base(message) { }
}
