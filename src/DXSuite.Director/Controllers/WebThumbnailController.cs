using DXSuite.Director.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DXSuite.Director.Controllers;

/// <summary>
/// Serves web-asset preview thumbnails to authenticated users. Thumbnails live outside wwwroot
/// (never reachable through the anonymous static-files middleware) and the tenant folder is
/// taken from the request's resolved tenant — not the URL — so a caller can only ever read
/// thumbnails belonging to the tenant they are signed into.
/// </summary>
[Route("WebThumbnails")]
[ApiController]
[Authorize]
public class WebThumbnailController : ControllerBase
{
    private readonly WebPreviewService _webPreviewService;

    public WebThumbnailController(WebPreviewService webPreviewService)
    {
        _webPreviewService = webPreviewService;
    }

    [HttpGet("{assetId:int}.png")]
    public IActionResult GetThumbnail(int assetId)
    {
        var path = _webPreviewService.GetThumbnailLocalPath(assetId);
        if (!System.IO.File.Exists(path)) return NotFound();

        // Thumbnails are regenerated in place (aspect changes, Refresh Preview), so require
        // revalidation of the stable URL; lastModified lets unchanged files answer with 304.
        // private: the response is tenant-scoped, so no shared cache may store it.
        Response.Headers.CacheControl = "private, no-cache";
        return PhysicalFile(path, "image/png",
            lastModified: new DateTimeOffset(System.IO.File.GetLastWriteTimeUtc(path)),
            entityTag: null);
    }
}
