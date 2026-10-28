using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using SkiaSharp;

namespace DXSuite.Director.Services;

public class ScreenshotService : IScreenshotService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ScreenshotService> _logger;

    // Subfolder under wwwroot for screenshots
    private const string ScreenshotFolder = "DeviceScreenshots";

    // Screenshots are support-grade thumbnails: cap the longest edge and re-encode as a modest-quality JPEG
    // so storage and bandwidth stay small regardless of the device's native capture resolution.
    private const int MaxScreenshotDimension = 640;
    private const int ScreenshotJpegQuality = 60;

    public ScreenshotService(IWebHostEnvironment environment, ILogger<ScreenshotService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task SaveScreenshotAsync(string deviceId, byte[] imageData, IDbContextFactory<DirectorDbContext> dbContextFactory)
    {
        // Downscale/re-encode to a small JPEG before persisting (support-grade, low storage/bandwidth).
        imageData = Downscale(imageData);

        // Ensure directory exists
        var deviceFolder = Path.Combine(_environment.WebRootPath, ScreenshotFolder, deviceId);
        Directory.CreateDirectory(deviceFolder);

        // Delete any existing screenshots in the folder (cache busting)
        try
        {
            var existingFiles = Directory.GetFiles(deviceFolder, "screenshot_*.jpg");
            foreach (var oldFile in existingFiles)
            {
                File.Delete(oldFile);
                _logger.LogDebug("Deleted old screenshot: {Path}", oldFile);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up old screenshots for device {DeviceId}", deviceId);
        }

        // Save file with timestamp for cache busting
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"screenshot_{timestamp}.jpg";
        var filePath = Path.Combine(deviceFolder, fileName);

        await File.WriteAllBytesAsync(filePath, imageData);

        var fileInfo = new FileInfo(filePath);
        var relativePath = $"/{ScreenshotFolder}/{deviceId}/{fileName}";

        _logger.LogInformation("Screenshot saved for device {DeviceId}: {Path} ({Size} bytes)",
            deviceId, relativePath, fileInfo.Length);

        // Update device ExtendedData with screenshot metadata
        await UpdateDeviceExtendedDataAsync(deviceId, relativePath, fileInfo.Length, dbContextFactory);
    }

    public ScreenshotInfo GetScreenshotInfo(string deviceId)
    {
        var deviceFolder = Path.Combine(_environment.WebRootPath, ScreenshotFolder, deviceId);

        if (!Directory.Exists(deviceFolder))
        {
            return new ScreenshotInfo { Exists = false };
        }

        // Find the most recent screenshot file (with timestamp in name)
        var screenshotFiles = Directory.GetFiles(deviceFolder, "screenshot_*.jpg")
            .OrderByDescending(f => f)
            .ToList();

        // Also check for legacy filename without timestamp
        var legacyFile = Path.Combine(deviceFolder, "screenshot.jpg");
        if (File.Exists(legacyFile) && !screenshotFiles.Any())
        {
            screenshotFiles.Add(legacyFile);
        }

        if (screenshotFiles.Any())
        {
            var filePath = screenshotFiles.First();
            var fileName = Path.GetFileName(filePath);
            var fileInfo = new FileInfo(filePath);
            var relativePath = $"/{ScreenshotFolder}/{deviceId}/{fileName}";

            return new ScreenshotInfo
            {
                Path = filePath,
                WebPath = relativePath,
                CapturedAt = fileInfo.LastWriteTimeUtc,
                SizeBytes = fileInfo.Length,
                Exists = true
            };
        }

        return new ScreenshotInfo { Exists = false };
    }

    public async Task DeleteScreenshotAsync(string deviceId, IDbContextFactory<DirectorDbContext> dbContextFactory)
    {
        var deviceFolder = Path.Combine(_environment.WebRootPath, ScreenshotFolder, deviceId);

        if (Directory.Exists(deviceFolder))
        {
            try
            {
                Directory.Delete(deviceFolder, recursive: true);
                _logger.LogInformation("Screenshot folder deleted for device {DeviceId}", deviceId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete screenshot folder for device {DeviceId}", deviceId);
            }
        }

        // Clear screenshot data from device ExtendedData
        await ClearDeviceScreenshotDataAsync(deviceId, dbContextFactory);
    }

    /// <summary>
    /// Decode the incoming image, scale the longest edge down to <see cref="MaxScreenshotDimension"/> (never
    /// upscales), and re-encode as a modest-quality JPEG. Returns the original bytes unchanged if the image
    /// can't be decoded so a capture is never lost to a codec edge case.
    /// </summary>
    private byte[] Downscale(byte[] imageData)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(imageData);
            if (bitmap == null) return imageData;

            var longest = Math.Max(bitmap.Width, bitmap.Height);
            var scale = longest > MaxScreenshotDimension ? (float)MaxScreenshotDimension / longest : 1f;

            SKBitmap? resized = null;
            try
            {
                var source = bitmap;
                if (scale < 1f)
                {
                    var info = new SKImageInfo(
                        Math.Max(1, (int)Math.Round(bitmap.Width * scale)),
                        Math.Max(1, (int)Math.Round(bitmap.Height * scale)));
                    resized = bitmap.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                    if (resized == null) return imageData;
                    source = resized;
                }

                using var image = SKImage.FromBitmap(source);
                using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, ScreenshotJpegQuality);
                return encoded?.ToArray() ?? imageData;
            }
            finally
            {
                resized?.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to downscale screenshot; storing original bytes");
            return imageData;
        }
    }

    private async Task UpdateDeviceExtendedDataAsync(string deviceId, string path, long sizeBytes, IDbContextFactory<DirectorDbContext> dbContextFactory)
    {
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var device = await dbContext.TblDevices.FindAsync(deviceId);

            if (device == null)
            {
                _logger.LogWarning("Device {DeviceId} not found when updating screenshot data", deviceId);
                return;
            }

            // Parse existing ExtendedData or create new
            var extData = ExtendedDataHelper.Parse<DeviceExtendedData>(device.ExtendedData) ?? new DeviceExtendedData();

            // Update screenshot data
            extData.Screenshot = new ScreenshotData
            {
                Path = path,
                CapturedAt = DateTime.UtcNow,
                SizeBytes = sizeBytes
            };

            // Serialize back
            device.ExtendedData = JsonSerializer.Serialize(extData);
            await dbContext.SaveChangesAsync();

            _logger.LogDebug("Updated ExtendedData with screenshot info for device {DeviceId}", deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update ExtendedData for device {DeviceId}", deviceId);
        }
    }

    private async Task ClearDeviceScreenshotDataAsync(string deviceId, IDbContextFactory<DirectorDbContext> dbContextFactory)
    {
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var device = await dbContext.TblDevices.FindAsync(deviceId);

            if (device == null) return;

            var extData = ExtendedDataHelper.Parse<DeviceExtendedData>(device.ExtendedData);
            if (extData?.Screenshot != null)
            {
                extData.Screenshot = null;
                device.ExtendedData = JsonSerializer.Serialize(extData);
                await dbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear screenshot data for device {DeviceId}", deviceId);
        }
    }
}
