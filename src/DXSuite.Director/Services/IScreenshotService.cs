using DXSuite.Director.Entities;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services;

/// <summary>
/// Information about an existing screenshot
/// </summary>
public class ScreenshotInfo
{
    public string? Path { get; set; }
    public string? WebPath { get; set; }
    public DateTime? CapturedAt { get; set; }
    public long? SizeBytes { get; set; }
    public bool Exists { get; set; }
}

/// <summary>
/// Screenshot file storage and device metadata. Queueing and upload-credential
/// resolution live in <see cref="IDeviceCommandService"/> (SQL-backed command queue).
/// </summary>
public interface IScreenshotService
{
    /// <summary>
    /// Save a screenshot image for a device
    /// </summary>
    Task SaveScreenshotAsync(string deviceId, byte[] imageData, IDbContextFactory<DirectorDbContext> dbContextFactory);

    /// <summary>
    /// Get screenshot information for a device
    /// </summary>
    ScreenshotInfo GetScreenshotInfo(string deviceId);

    /// <summary>
    /// Delete screenshot for a device
    /// </summary>
    Task DeleteScreenshotAsync(string deviceId, IDbContextFactory<DirectorDbContext> dbContextFactory);
}
