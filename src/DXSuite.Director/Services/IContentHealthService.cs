using DXSuite.Director.Entities;
using DXSuite.Director.Models.BrightSign;

namespace DXSuite.Director.Services;

public interface IContentHealthService
{
    Task RecordEventAsync(ContentHealthEvent evt, string deviceId);
    Task UpdateHealthStatusAsync(string deviceId, ContentHealthStatus status);
    Task<List<TblContentHealthEvent>> GetRecentEventsAsync(int count = 100);
    Task<List<TblDevice>> GetUnhealthyDevicesAsync();
}
