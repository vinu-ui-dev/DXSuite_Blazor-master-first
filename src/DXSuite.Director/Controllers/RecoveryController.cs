using DXSuite.Director.Services;
using DXSuite.Shared.Multitenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DXSuite.Director.Controllers;

/// <summary>
/// The URL a BrightSign player calls when its own card cannot start it.
///
/// Anonymous by necessity: the caller is a player with a broken bootloader, no running app and
/// therefore no device token. Trust is carried in the URL instead, and the anti-enumeration
/// discipline is copied from PanelController: an unknown tenant token, an unarmed serial and a
/// disabled tenant all produce exactly the same 204, and nothing tells a prober which.
///
/// The tenant is the path segment and is authoritative regardless of the Host header. That is a
/// workaround for a real hazard rather than a preference: tenant resolution reads the Host alone
/// and falls through to a default for a bare IP or a two-part hostname, so an on-prem Director
/// reached at director.customer.local or by IP would otherwise serve one tenant's bootloader to
/// another tenant's player.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/[controller]")]
[EnableRateLimiting(RecoveryRateLimiting.PolicyName)]
public sealed class RecoveryController : ControllerBase
{
    private readonly IWorkerScopeFactory _workerScopeFactory;
    private readonly ILogger<RecoveryController> _logger;

    public RecoveryController(IWorkerScopeFactory workerScopeFactory, ILogger<RecoveryController> logger)
    {
        _workerScopeFactory = workerScopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// The tenant-scoped URL a provisioned player carries for life. Serves the recovery courier,
    /// and an install bootstrap with no pairing grant is never served from here: a package binds to
    /// a tenant and a family and never to a device, so this URL cannot be bound to a serial.
    /// </summary>
    [HttpGet("{tenantToken}")]
    public Task<IActionResult> Get(string tenantToken)
        => HandleAsync(tenantToken, boundSerial: null);

    /// <summary>
    /// The per-player URL, written by the remote DWS at takeover trigger only. Its token is bound
    /// to one serial, and this is the only response that ever carries a secret. The bootloader
    /// replaces this URL with the tenant-scoped one on the first healthy boot after enrollment.
    /// </summary>
    [HttpGet("{tenantToken}/{boundSerial}")]
    public Task<IActionResult> GetForPlayer(string tenantToken, string boundSerial)
        => HandleAsync(tenantToken, boundSerial);

    private async Task<IActionResult> HandleAsync(string tenantToken, string? boundSerial)
    {
        var request = ReadRequest();

        IServiceScope scope;
        try
        {
            scope = await _workerScopeFactory.CreateTenantScopeAsync(tenantToken, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            // Logged at host level with the token and the serial, because by definition no tenant
            // database could be chosen. Without this an operator sees only silence where four
            // players are calling a URL that resolves to nothing.
            _logger.LogWarning(ex,
                "Recovery GET for serial {Serial} named tenant token {TenantToken}, which resolves to nothing",
                request.DeviceId ?? "unknown", tenantToken);
            return NoContentWithRetry(RecoveryRateLimiting.UnresolvedRetry);
        }

        using (scope)
        {
            var handler = scope.ServiceProvider.GetRequiredService<IRecoveryHandlerService>();
            var decision = await handler.DecideAsync(request, boundSerial);

            _logger.LogDebug("Recovery GET for serial {Serial} on tenant {TenantToken}: {Reason}",
                request.DeviceId ?? "unknown", tenantToken, decision.Reason);

            if (!decision.HasBody)
                return NoContentWithRetry(decision.RetryAfter);

            SetRetryAfter(decision.RetryAfter);
            return Content(decision.Script!, "text/plain");
        }
    }

    /// <summary>
    /// Streams a bootloader artifact anonymously, which is the whole reason the courier can work:
    /// the player fetching it has no token to present. Safe because the bootloader artifact is
    /// tenant-neutral by construction, carrying no endpoint, credentials, CA, proxy or tenant
    /// identifier; it reads all of those from the registry at boot.
    ///
    /// This route serves the zip, and it is the one the ordinary update pipeline offers: the app
    /// downloads it to sd:/staging/bootloader.zip and the running bootloader unpacks it. The
    /// courier cannot unpack anything, so it uses the sibling route below rather than this one.
    /// </summary>
    [HttpGet("bootloader/{tenantToken}/{appVersionId:int}")]
    public Task<IActionResult> GetBootloader(string tenantToken, int appVersionId)
        => ServeBootloaderAsync(tenantToken, appVersionId, asScript: false);

    /// <summary>
    /// The same bootloader, unwrapped: the autorun.brs inside the artifact, served as text.
    ///
    /// A second route rather than a second payload on the first, because the two consumers want
    /// genuinely different bytes and each is right. The update pipeline wants the zip, which the
    /// running bootloader unpacks. The courier commits what it downloads by a single rename onto
    /// sd:/autorun.brs and has no unpack step at all, so a zip reaches it as a file the OS will
    /// never run and the player boots straight back into recovery.
    ///
    /// "One URL rather than two" held for as long as both wanted the same bytes. It stopped being
    /// free the moment they did not, and the cost of collapsing them was a bootloader update that
    /// could never install.
    /// </summary>
    [HttpGet("bootloader/{tenantToken}/{appVersionId:int}/autorun")]
    public Task<IActionResult> GetBootloaderScript(string tenantToken, int appVersionId)
        => ServeBootloaderAsync(tenantToken, appVersionId, asScript: true);

    private async Task<IActionResult> ServeBootloaderAsync(string tenantToken, int appVersionId, bool asScript)
    {
        IServiceScope scope;
        try
        {
            scope = await _workerScopeFactory.CreateTenantScopeAsync(tenantToken, HttpContext.RequestAborted);
        }
        catch
        {
            return NotFound();
        }

        using (scope)
        {
            var updateService = scope.ServiceProvider.GetRequiredService<IUpdateService>();
            var artifactStore = scope.ServiceProvider.GetRequiredService<IPlayerArtifactStore>();

            var version = await updateService.GetVersionByIdAsync(appVersionId);

            // Only bootloaders. An app bundle is not tenant-neutral and must not be reachable here.
            if (version is not { ArtifactKind: Entities.ArtifactKinds.Bootloader })
                return NotFound();

            var path = artifactStore.ResolveExisting(version.ZipFilePath);
            if (path == null)
                return NotFound();

            if (!asScript)
                return File(System.IO.File.OpenRead(path), "application/zip", version.ZipFileName);

            var script = BootloaderScript.Extract(path);
            if (script == null)
            {
                _logger.LogError("Bootloader artifact {Path} carries no root {Entry}",
                    version.ZipFilePath, BootloaderScript.EntryName);
                return NotFound();
            }

            return File(script, "text/plain", BootloaderScript.EntryName);
        }
    }

    private RecoveryRequest ReadRequest() => new(
        DeviceId: Header("deviceid"),
        DeviceModel: Header("devicemodel"),
        DeviceFamily: Header("devicefamily"),
        DeviceFirmwareVersion: Header("devicefwversion"),
        DeviceUptime: Header("deviceuptime"),
        RecoveryMode: Header("recoverymode"),
        CrashDump: Header("crashdump"),
        StorageStatus: Header("storagestatus"));

    /// <summary>
    /// Headers first, query second: the OS sends these as headers, and the query form is what makes
    /// the endpoint testable from a browser without a player.
    /// </summary>
    private string? Header(string name)
        => Request.Headers.TryGetValue(name, out var header) && !string.IsNullOrWhiteSpace(header)
            ? header.ToString()
            : Request.Query[name].ToString() is { Length: > 0 } query ? query : null;

    private IActionResult NoContentWithRetry(TimeSpan? retryAfter)
    {
        SetRetryAfter(retryAfter);
        return NoContent();
    }

    private void SetRetryAfter(TimeSpan? retryAfter)
    {
        if (retryAfter is { } value)
            Response.Headers["Retry-After"] = ((int)value.TotalSeconds).ToString();
    }
}
