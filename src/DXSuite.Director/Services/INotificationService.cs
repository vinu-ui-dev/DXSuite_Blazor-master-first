using DXSuite.Shared.Services;

namespace DXSuite.Director.Services;

/// <summary>
/// Extended notification service interface with Director-specific methods.
/// Inherits from Shared's INotificationService.
/// </summary>
public interface IDirectorNotificationService : INotificationService
{
    // All methods now on base INotificationService.
    // Kept for future Director-specific notification extensions.
}
