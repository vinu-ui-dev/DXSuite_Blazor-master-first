using DXSuite.Director.Models.BrightSign;

namespace DXSuite.Director.Services
{
    public interface IHeartbeatService
    {
        void QueueHeartbeat(string tenantSubdomain, PlayerHeartbeatModel payload);
        Task<bool> DeviceExistsAsync(string tenantSubdomain, string deviceId);
    }
}
