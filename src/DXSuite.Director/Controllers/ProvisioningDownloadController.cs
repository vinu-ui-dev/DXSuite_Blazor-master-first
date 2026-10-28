using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace DXSuite.Director.Controllers;

/// <summary>
/// Streams provisioning downloads built by the Blazor page: a package card image, or the
/// off-origin host report as CSV.
///
/// Mirrors the content log and content package flows. The page stashes the bytes under a
/// single-use token and navigates the browser here, so the file saves without pushing base64
/// through the SignalR channel, and so a card image never travels as a data URL.
/// </summary>
[ApiController]
[Route("api/provisioning")]
public class ProvisioningDownloadController : ControllerBase
{
    internal static readonly ConcurrentDictionary<Guid, PendingDownload> PendingDownloads = new();

    internal sealed class PendingDownload
    {
        public required byte[] Bytes { get; init; }
        public required string FileName { get; init; }
        public required string ContentType { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    }

    [HttpGet("download/{token:guid}")]
    public IActionResult Download(Guid token)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        foreach (var kvp in PendingDownloads)
        {
            if (kvp.Value.CreatedAt < cutoff)
                PendingDownloads.TryRemove(kvp.Key, out _);
        }

        if (!PendingDownloads.TryRemove(token, out var download))
            return NotFound(new { error = "Download token not found or already used." });

        return File(download.Bytes, download.ContentType, download.FileName);
    }
}
