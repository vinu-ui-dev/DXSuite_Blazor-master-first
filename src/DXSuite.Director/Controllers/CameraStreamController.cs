using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using System.Diagnostics;
using System.Text.Json;

namespace DXSuite.Director.Controllers;

[ApiController]
[Route("api/camera")]
public class CameraStreamController : ControllerBase
{
    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly ILogger<CameraStreamController> _logger;
    private readonly IConfiguration _configuration;

    public CameraStreamController(
        IDbContextFactory<DirectorDbContext> dbFactory,
        ILogger<CameraStreamController> logger,
        IConfiguration configuration)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Streams camera feed by transcoding RTSP to MJPEG using FFmpeg
    /// </summary>
    [HttpGet("stream/{locationId}")]
    public async Task<IActionResult> StreamCamera(int locationId, CancellationToken cancellationToken)
    {
        try
        {
            // Get camera configuration from location
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var location = await db.TblLocations.FindAsync(new object[] { locationId }, cancellationToken);

            if (location == null || string.IsNullOrEmpty(location.ExtendedData))
            {
                return NotFound("Location or camera configuration not found");
            }

            var extendedData = JsonSerializer.Deserialize<PtzCameraExtendedData>(location.ExtendedData);
            if (extendedData?.CameraConfig == null)
            {
                return BadRequest("Invalid camera configuration");
            }

            var rtspUrl = extendedData.CameraConfig.RtspUrl;
            if (string.IsNullOrEmpty(rtspUrl))
            {
                return BadRequest("RTSP URL not configured for this camera");
            }

            _logger.LogInformation("Starting RTSP stream transcoding for location {LocationId}, RTSP: {RtspUrl}",
                locationId, rtspUrl);

            // Set response headers for MJPEG stream
            // FFmpeg's mpjpeg format uses this boundary by default
            Response.ContentType = "multipart/x-mixed-replace; boundary=ffmpeg";
            Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
            Response.Headers.Append("Pragma", "no-cache");
            Response.Headers.Append("Expires", "0");

            // Get FFmpeg path from configuration
            var ffmpegPath = _configuration["Ffmpeg:Path"];
            var ffmpegExe = Path.Combine(ffmpegPath ?? "", "ffmpeg.exe");

            if (!System.IO.File.Exists(ffmpegExe))
            {
                _logger.LogError("FFmpeg not found at: {Path}", ffmpegExe);
                return StatusCode(500, "FFmpeg not available");
            }

            // FFmpeg command to transcode RTSP to MJPEG with proper HTTP multipart boundaries
            // -rtsp_transport tcp: Use TCP for RTSP (more reliable than UDP)
            // -i {rtspUrl}: Input RTSP stream
            // -f mpjpeg: Multipart JPEG format (includes HTTP boundaries)
            // -q:v 5: Quality (2-31, lower is better, 5 is good quality)
            // -r 15: Frame rate (15 fps for reasonable bandwidth)
            // -vf scale=1280:720: Scale to 720p for performance
            // pipe:1: Output to stdout
            var arguments = $"-rtsp_transport tcp -i \"{rtspUrl}\" -f mpjpeg -q:v 5 -r 15 -vf scale=1280:720 pipe:1";

            var processInfo = new ProcessStartInfo
            {
                FileName = ffmpegExe,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processInfo };

            // Log FFmpeg errors in background
            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _logger.LogDebug("FFmpeg: {Message}", e.Data);
                }
            };

            process.Start();
            process.BeginErrorReadLine();

            try
            {
                // Stream FFmpeg's MPJPEG output directly (already has proper boundaries)
                await using var outputStream = process.StandardOutput.BaseStream;
                await outputStream.CopyToAsync(Response.Body, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Stream cancelled for location {LocationId}", locationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error streaming camera feed for location {LocationId}", locationId);
            }
            finally
            {
                // Cleanup FFmpeg process
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error killing FFmpeg process");
                    }
                }
            }

            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting up camera stream for location {LocationId}", locationId);
            return StatusCode(500, "Failed to start camera stream");
        }
    }

    /// <summary>
    /// Test endpoint to verify RTSP connectivity without streaming
    /// </summary>
    [HttpGet("test/{locationId}")]
    public async Task<IActionResult> TestStream(int locationId, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var location = await db.TblLocations.FindAsync(new object[] { locationId }, cancellationToken);

            if (location == null || string.IsNullOrEmpty(location.ExtendedData))
            {
                return NotFound("Location or camera configuration not found");
            }

            var extendedData = JsonSerializer.Deserialize<PtzCameraExtendedData>(location.ExtendedData);
            if (extendedData?.CameraConfig == null)
            {
                return BadRequest("Invalid camera configuration");
            }

            var rtspUrl = extendedData.CameraConfig.RtspUrl;
            if (string.IsNullOrEmpty(rtspUrl))
            {
                return BadRequest("RTSP URL not configured");
            }

            // Quick test: try to probe the RTSP stream
            var ffmpegPath = _configuration["Ffmpeg:Path"];
            var ffprobeExe = Path.Combine(ffmpegPath ?? "", "ffprobe.exe");

            if (!System.IO.File.Exists(ffprobeExe))
            {
                return Ok(new { success = false, message = "FFprobe not available" });
            }

            var arguments = $"-rtsp_transport tcp -i \"{rtspUrl}\" -v quiet -print_format json -show_streams -show_format";

            var processInfo = new ProcessStartInfo
            {
                FileName = ffprobeExe,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                return Ok(new { success = false, message = "Failed to start FFprobe" });
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            try
            {
                await process.WaitForExitAsync(cts.Token);
                var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
                var success = process.ExitCode == 0 && !string.IsNullOrEmpty(output);

                return Ok(new
                {
                    success,
                    rtspUrl,
                    message = success ? "RTSP stream accessible" : "Failed to access RTSP stream"
                });
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return Ok(new { success = false, message = "RTSP stream probe timeout" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing camera stream for location {LocationId}", locationId);
            return StatusCode(500, "Failed to test camera stream");
        }
    }
}
