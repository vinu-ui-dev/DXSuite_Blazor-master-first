using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace DXSuite.Director.Controllers;

/// <summary>
/// Streams Content Log exports built by the Blazor page. Mirrors the content package
/// download flow: the page stashes the bytes under a single-use token and navigates the
/// browser here so the file saves without pushing base64 through the SignalR channel.
/// </summary>
[ApiController]
[Route("api/content-log")]
public class ContentLogExportController : ControllerBase
{
    internal static readonly ConcurrentDictionary<Guid, PendingExport> PendingExports = new();

    internal sealed class PendingExport
    {
        public required byte[] Bytes { get; init; }
        public required string FileName { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    }

    [HttpGet("download/{token:guid}")]
    public IActionResult Download(Guid token)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        foreach (var kvp in PendingExports)
        {
            if (kvp.Value.CreatedAt < cutoff)
                PendingExports.TryRemove(kvp.Key, out _);
        }

        if (!PendingExports.TryRemove(token, out var export))
            return NotFound(new { error = "Download token not found or already used." });

        return File(export.Bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", export.FileName);
    }
}
