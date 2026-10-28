using DXSuite.Director.Entities;

namespace DXSuite.Director.Services
{
    public interface IOverrideService
    {
        // CRUD Operations
        Task<List<TblOverride>> GetAllOverridesAsync();
        Task<TblOverride?> GetOverrideByIdAsync(int overrideId);
        Task<TblOverride> CreateOverrideAsync(TblOverride overrideEntity, string? createdBy = null);
        Task<TblOverride> UpdateOverrideAsync(TblOverride overrideEntity, string? modifiedBy = null);
        Task DeleteOverrideAsync(int overrideId);

        // Device Management
        Task<List<string>> GetDeviceIdsForOverrideAsync(int overrideId);
        Task<List<TblDevice>> GetDevicesForOverrideAsync(int overrideId);
        Task SetDevicesForOverrideAsync(int overrideId, List<string> deviceIds);

        // Display Management
        Task<List<int>> GetDisplayIdsForOverrideAsync(int overrideId);
        Task SetDisplaysForOverrideAsync(int overrideId, List<int> displayIds);

        // Content Management
        Task<List<TblOverrideContent>> GetContentForOverrideAsync(int overrideId);
        Task<TblOverrideContent> SetContentForResolutionAsync(int overrideId, int width, int height, string contentPath, int? assetId, int duration = 7);
        Task<TblOverrideContent> SetContentForDeviceAsync(int overrideId, string deviceId, int width, int height, string contentPath, int? assetId, int duration = 7);
        Task DeleteContentAsync(int overrideContentId);

        // Activation
        Task<bool> ActivateOverrideAsync(int overrideId, string triggerSource, string? user, string? ip);
        Task<bool> DeactivateOverrideAsync(int overrideId, string triggerSource, string? user, string? ip);

        // Status Queries
        Task<TblOverride?> GetActiveOverrideForDeviceAsync(string deviceId);
        Task<bool> IsOverrideEffectivelyActiveAsync(int overrideId);
        Task<List<TblOverride>> GetActiveOverridesAsync();
        Task<OverrideStatusInfo> GetOverrideStatusAsync(int overrideId);
        Task<List<OverrideStatusInfo>> GetAllOverrideStatusesAsync();

        // Priority Management
        Task ReorderOverridesAsync(List<int> overrideIdsInOrder);

        // Resolution Detection
        Task<List<ResolutionInfo>> GetResolutionsForDevicesAsync(List<string> deviceIds);

        // Logging
        Task LogOverrideActionAsync(int overrideId, string? overrideName, string action, string triggerSource, string? user, string? ip, string? notes = null);

        // Override Control Location
        Task<int> EnsureOverrideControlLocationExistsAsync();
    }

    public class OverrideStatusInfo
    {
        public int OverrideId { get; set; }
        public string OverrideName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public bool IsScheduleActive { get; set; }
        public bool IsEffectivelyActive { get; set; }
        public int Priority { get; set; }
        public int DeviceCount { get; set; }
        public DateTime? LastActivated { get; set; }
        public DateTime? LastDeactivated { get; set; }
    }

    public class ResolutionInfo
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int DeviceCount { get; set; }
        public string DisplayName => $"{Width}x{Height}";
    }
}
