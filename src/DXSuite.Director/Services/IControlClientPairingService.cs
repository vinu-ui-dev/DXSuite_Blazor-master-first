using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

public class ControlClientPairingRequestResult
{
    public Guid PairingRequestId { get; set; }
    public string PairingCode { get; set; } = null!;
    public int ExpiresInSeconds { get; set; }
}

public class ControlClientPairingStatusResult
{
    public string Status { get; set; } = null!;
    public string? DeviceToken { get; set; }
}

public interface IControlClientPairingService
{
    Task<ControlClientPairingRequestResult> CreatePairingRequestAsync(string? hostname, string? ipAddress);

    Task<ControlClientPairingStatusResult> GetPairingStatusAsync(Guid pairingRequestId);

    /// <summary>
    /// Admin approves a pending request by entering the 6-digit code and assigning a control client profile.
    /// </summary>
    Task<TblControlClientPairingRequest> ApprovePairingAsync(string pairingCode, int controlClientId, int approvedByUserId);

    Task DenyPairingAsync(Guid pairingRequestId, int deniedByUserId);

    Task<List<TblControlClientPairingRequest>> GetPendingRequestsAsync();

    Task<int> ExpireStaleRequestsAsync();

    Task<int> PurgeOldRequestsAsync(TimeSpan retention);
}
