using System.Linq;
using System.Threading.Tasks;
using DXSuite.Director.Authorization;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.LocationBundles;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Controllers;

/// <summary>
/// Management endpoints for location bundles (upload, versioning, delete, history).
/// Gated by Features:LocationBundles — every action returns 404 when the flag is off.
/// Accepts cookie sessions (Config UI) and admin API keys (the dx-bundle push tool,
/// which cannot cookie-auth). Cookie callers must be Admin/Architect — controllers run
/// outside the Blazor circuit, so the Configuration page's role guard does not apply
/// here and the role is re-verified per request.
///
/// API keys are admin-issued (Settings &gt; API Keys), and possession of a key is the
/// authorization for every action here except deletion: keys live on unattended players
/// and build agents, so bundle and version deletes are restricted to interactive
/// Admin/Architect sessions. See <see cref="RejectApiKeyDeletion"/>.
/// </summary>
[Route("api/location-bundles")]
[ApiController]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + AuthSchemes.AdminApiKey)]
public class LocationBundleAdminController : ControllerBase
{
    private readonly ILocationBundleService _bundleService;
    private readonly ILocationBundleImporter _bundleImporter;
    private readonly IDirectorDBContext _db;
    private readonly IOptions<FeatureFlags> _featureFlags;
    private readonly ILogger<LocationBundleAdminController> _logger;

    public LocationBundleAdminController(
        ILocationBundleService bundleService,
        ILocationBundleImporter bundleImporter,
        IDirectorDBContext db,
        IOptions<FeatureFlags> featureFlags,
        ILogger<LocationBundleAdminController> logger)
    {
        _bundleService = bundleService;
        _bundleImporter = bundleImporter;
        _db = db;
        _featureFlags = featureFlags;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetBundles()
    {
        if (!_featureFlags.Value.LocationBundles) return NotFound();
        if (!await CallerIsAdminOrArchitectAsync()) return Forbid();

        var bundles = await _bundleService.GetBundlesAsync(HttpContext.RequestAborted);
        return Ok(bundles.Select(b => new
        {
            b.Bundle.BundleKey,
            b.Bundle.Name,
            b.Bundle.Description,
            b.Bundle.CurrentVersion,
            b.Bundle.IsEnabled,
            Versions = b.Versions.Select(v => new { v.Version, v.UploadDate, v.UploadedBy, v.FileCount, v.TotalSizeBytes }),
            ReferencedBy = b.ReferencedBy.Select(r => new { r.LocationId, r.LocationName, r.PinnedVersion })
        }));
    }

    /// <summary>
    /// Uploads a bundle zip. Installs a new bundle or adds a version to an existing one;
    /// duplicate (key, version) pairs are rejected because stored versions are immutable.
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(200L * 1024 * 1024)]
    public async Task<IActionResult> ImportBundle(IFormFile bundle)
    {
        if (!_featureFlags.Value.LocationBundles) return NotFound();
        if (!await CallerIsAdminOrArchitectAsync()) return Forbid();

        if (bundle == null || bundle.Length == 0)
            return BadRequest(new { errors = new[] { "No bundle file supplied." } });

        await using var stream = bundle.OpenReadStream();
        var result = await _bundleImporter.ImportAsync(stream, CallerName, HttpContext.RequestAborted);

        if (!result.Success)
            return BadRequest(new { result.Errors, result.Warnings });

        _logger.LogInformation("Location bundle {BundleKey} {Version} imported ({Action}) by {User}",
            result.BundleKey, result.Version, result.Action, CallerName);

        return Ok(new { result.BundleKey, result.Version, result.Name, result.Action, result.Warnings });
    }

    [HttpPost("{bundleKey}/current-version")]
    public async Task<IActionResult> SetCurrentVersion(string bundleKey, [FromBody] SetVersionRequest request)
    {
        if (!_featureFlags.Value.LocationBundles) return NotFound();
        if (!await CallerIsAdminOrArchitectAsync()) return Forbid();

        if (string.IsNullOrWhiteSpace(request?.Version))
            return BadRequest(new { error = "Version is required." });

        var result = await _bundleService.SetCurrentVersionAsync(bundleKey, request.Version, CallerName, HttpContext.RequestAborted);
        return result.Success ? Ok() : BadRequest(new { result.Error });
    }

    [HttpDelete("{bundleKey}")]
    public async Task<IActionResult> DeleteBundle(string bundleKey)
    {
        if (!_featureFlags.Value.LocationBundles) return NotFound();
        if (RejectApiKeyDeletion() is { } denied) return denied;
        if (!await CallerIsAdminOrArchitectAsync()) return Forbid();

        var result = await _bundleService.DeleteBundleAsync(bundleKey, CallerName, HttpContext.RequestAborted);
        return result.Success
            ? Ok()
            : Conflict(new { result.Error, ReferencingLocations = result.ReferencingLocations.Select(r => new { r.LocationId, r.LocationName }) });
    }

    [HttpDelete("{bundleKey}/versions/{version}")]
    public async Task<IActionResult> DeleteVersion(string bundleKey, string version)
    {
        if (!_featureFlags.Value.LocationBundles) return NotFound();
        if (RejectApiKeyDeletion() is { } denied) return denied;
        if (!await CallerIsAdminOrArchitectAsync()) return Forbid();

        var result = await _bundleService.DeleteVersionAsync(bundleKey, version, CallerName, HttpContext.RequestAborted);
        return result.Success
            ? Ok()
            : Conflict(new { result.Error, ReferencingLocations = result.ReferencingLocations.Select(r => new { r.LocationId, r.LocationName }) });
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int take = 100)
    {
        if (!_featureFlags.Value.LocationBundles) return NotFound();
        if (!await CallerIsAdminOrArchitectAsync()) return Forbid();

        var history = await _bundleService.GetHistoryAsync(take, HttpContext.RequestAborted);
        return Ok(history);
    }

    public class SetVersionRequest
    {
        public string? Version { get; set; }
    }

    private bool IsApiKeyCaller => User.Identity?.AuthenticationType == AuthSchemes.AdminApiKey;

    /// <summary>
    /// Blocks bundle and version deletion for API-key callers. Keys are deployed to
    /// unattended players and build agents, where destroying a bundle is never a
    /// legitimate operation and a leaked key would otherwise be able to wipe every
    /// bundle in the tenant. Deleting is an interactive Admin/Architect action, or a
    /// direct database change. Uploading stays available so the push tool keeps working:
    /// versions are immutable, so an import can only ever add.
    /// </summary>
    private IActionResult? RejectApiKeyDeletion() =>
        IsApiKeyCaller
            // Explicit 403 rather than Forbid(): the cookie handler turns Forbid() into a
            // redirect to the access-denied page, which a CLI caller cannot interpret.
            ? StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Location bundles cannot be deleted with an API key. Delete from Configuration > Location Bundles."
            })
            : null;

    /// <summary>Audit name: UPN for cookie sessions, key name for API-key callers.</summary>
    private string? CallerName => IsApiKeyCaller
        ? $"apikey:{User.FindFirst("ApiKeyName")?.Value}"
        : User.Identity?.Name;

    private async Task<bool> CallerIsAdminOrArchitectAsync()
    {
        if (IsApiKeyCaller)
            return true;

        var upn = User.Identity?.Name;
        if (string.IsNullOrEmpty(upn)) return false;

        var user = await _db.TblUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserPrincipalName == upn, HttpContext.RequestAborted);

        if (user == null || user.IsDisabled) return false;

        var role = (Roles.Role)user.RoleId;
        return role == Roles.Role.Admin || role == Roles.Role.Architect;
    }
}
