using DXSuite.Director.Entities;
using DXSuite.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services;

/// <summary>The eight documented headers a BrightSign recovery GET carries, all of them forgeable.</summary>
public sealed record RecoveryRequest(
    string? DeviceId,
    string? DeviceModel,
    string? DeviceFamily,
    string? DeviceFirmwareVersion,
    string? DeviceUptime,
    string? RecoveryMode,
    string? CrashDump,
    string? StorageStatus);

/// <summary>
/// Decides what a recovery GET gets back, inside a scope already bound to the tenant named in the
/// URL path.
///
/// One contract with one implementation: all three callers send the same GET and differ only in
/// trust, and trust is carried in the URL rather than in any header. Three route registrations
/// over one contract would be one contract with three call sites.
/// </summary>
public interface IRecoveryHandlerService
{
    /// <param name="boundSerial">
    /// Set only for a per-player URL, whose token is bound to one serial. A mismatch is a logged
    /// rejection rather than a quiet 204, because it is the one case that cannot be a prober
    /// guessing.
    /// </param>
    Task<RecoveryDecision> DecideAsync(RecoveryRequest request, string? boundSerial);
}

public sealed class RecoveryHandlerService : IRecoveryHandlerService
{
    /// <summary>
    /// A converted healthy player already has a heartbeat channel, so its periodic recovery GET is
    /// pure redundancy. This is the map's second bandwidth lever after batching.
    /// </summary>
    private static readonly TimeSpan ConvertedHealthyRetry = TimeSpan.FromHours(24);

    /// <summary>An armed player is mid-operation and should convert on the next cycle.</summary>
    private static readonly TimeSpan ArmedRetry = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long a served install bootstrap suppresses the next one.
    ///
    /// The OS checks the recovery override BEFORE running the card's autorun, so an armed serial
    /// served on every boot never boots the autorun it was just given, and conversion, which is the
    /// only thing that ends the loop, can never happen. Long enough to cover a reboot, an unpack,
    /// a widget start and an enrolment on a slow link; short enough that a genuinely failed install
    /// is retried while an operator is still watching.
    /// </summary>
    private static readonly TimeSpan BootstrapGrace = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Every other 204. Retry-After is honoured in last-resort too, not only on the periodic poll,
    /// so any 204 a broken player can reach must carry a short value or none. Every fallback path
    /// below is reachable in last-resort.
    /// </summary>
    private static readonly TimeSpan FallbackRetry = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProvisioningAuthorizationService _authorizations;
    private readonly IPairingGrantService _pairingGrants;
    private readonly IPlayerCallbackUrlResolver _callbackUrls;
    private readonly IPlayerArtifactStore _artifacts;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<RecoveryHandlerService> _logger;

    public RecoveryHandlerService(
        IServiceScopeFactory scopeFactory,
        IProvisioningAuthorizationService authorizations,
        IPairingGrantService pairingGrants,
        IPlayerCallbackUrlResolver callbackUrls,
        IPlayerArtifactStore artifacts,
        ITenantContext tenantContext,
        ILogger<RecoveryHandlerService> logger)
    {
        _scopeFactory = scopeFactory;
        _authorizations = authorizations;
        _pairingGrants = pairingGrants;
        _callbackUrls = callbackUrls;
        _artifacts = artifacts;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<RecoveryDecision> DecideAsync(RecoveryRequest request, string? boundSerial)
    {
        var serial = request.DeviceId?.Trim();

        if (string.IsNullOrEmpty(serial))
            return new RecoveryDecision(null, FallbackRetry, "No deviceid on the request");

        // The serial selects; it never authorises. Every header here is forged by anyone who can
        // reach the endpoint and read a sticker.
        if (boundSerial != null && !string.Equals(boundSerial, serial, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Recovery token bound to serial {BoundSerial} was presented for serial {RequestedSerial}",
                boundSerial, serial);
            return new RecoveryDecision(null, FallbackRetry, "Per-player token does not match the serial");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var converted = await FindConvertedDeviceAsync(db, serial);

        // Conversion state first, recoverymode only to refine. Branching on recoverymode alone gets
        // the important case backwards: a player whose install failed reports last-resort while
        // what it needs is the bootstrap, not the courier.
        if (converted != null)
            return await DecideForConvertedAsync(db, request, serial);

        return await DecideForUnconvertedAsync(db, request, serial);
    }

    private async Task<RecoveryDecision> DecideForConvertedAsync(
        IDirectorDBContext db, RecoveryRequest request, string serial)
    {
        // Within a converted player, recoverymode does its one useful job. A 200 with a body on an
        // override GET means "use this as the autorun", so a handler that answered one
        // unconditionally would run instead of the card's autorun and reboot, on every boot of a
        // healthy player: a ~66 second cycle that never converges. This 204 is load-bearing.
        if (!IsLastResort(request.RecoveryMode))
            return new RecoveryDecision(null, ConvertedHealthyRetry, "Converted and healthy");

        var knownGood = await FindKnownGoodBootloaderAsync(db);
        if (knownGood == null)
        {
            _logger.LogError(
                "Serial {Serial} is in last-resort recovery and this tenant has blessed no known-good bootloader",
                serial);
            return new RecoveryDecision(null, FallbackRetry, "No known-good bootloader is blessed");
        }

        var script = RecoveryCourier.BuildRecoveryCourier(
            BuildBootloaderUrl(knownGood.AppVersionId), BootloaderScriptHash(knownGood));

        _logger.LogWarning("Serial {Serial} is in last-resort recovery; serving known-good bootloader {Version}",
            serial, knownGood.VersionNumber);

        // No Retry-After on a served courier: the player is about to reboot into what it fetched.
        return new RecoveryDecision(script, null, "Recovery courier served");
    }

    private async Task<RecoveryDecision> DecideForUnconvertedAsync(
        IDirectorDBContext db, RecoveryRequest request, string serial)
    {
        // Every install response requires an armed serial, for every caller. That reduces this
        // endpoint to one sentence: it only ever acts on serials an operator has named, and
        // everything else gets 204. Relaxing it later is one boolean; walking it back after a
        // customer builds on it is not.
        var authorization = await _authorizations.FindLiveAsync(serial);
        if (authorization == null)
            return new RecoveryDecision(null, FallbackRetry, "Serial is not armed");

        // The card was written on a previous boot and has not had a chance to run yet. Serving
        // again here is the ~66 second loop section 6.3 names, arrived at from the other side:
        // the courier rewrites the card and reboots, so the autorun it installed never starts.
        if (BootstrapHoldOff(authorization.BootstrapServedDate, DateTime.UtcNow) is { } holdOff)
            return new RecoveryDecision(null, holdOff,
                "Install bootstrap already served; letting the card boot");

        if (authorization.BootstrapServedDate is { } lapsed)
            _logger.LogWarning(
                "Serial {Serial} was served an install bootstrap at {ServedAt} and has not converted; serving again",
                serial, lapsed);

        var bootloader = authorization.PinnedBootloaderVersionId is { } pinnedId
            ? await db.TblAppVersions.FirstOrDefaultAsync(v => v.AppVersionId == pinnedId)
            : await FindKnownGoodBootloaderAsync(db);

        if (bootloader == null)
        {
            _logger.LogError("Serial {Serial} is armed but no bootloader is pinned or blessed", serial);
            return new RecoveryDecision(null, ArmedRetry, "Armed, but no bootloader to install");
        }

        // A bootloader on its own converts a player into a parked one: it boots with nothing in
        // either slot, stays reachable and idle, and has no widget to consume the grant below. So
        // an armed serial with no app bundle to send is a refusal, not a half install.
        var appBundle = authorization.PinnedAppVersionId is { } pinnedAppId
            ? await db.TblAppVersions.FirstOrDefaultAsync(v => v.AppVersionId == pinnedAppId)
            : await FindLatestAppBundleAsync(db);

        if (appBundle == null)
        {
            _logger.LogError("Serial {Serial} is armed but no app bundle is pinned or available", serial);
            return new RecoveryDecision(null, ArmedRetry, "Armed, but no app bundle to install");
        }

        // Minted fresh on every 200 rather than re-served, so a bootstrap left on a card carries
        // nothing usable. Consumed on confirmation rather than on serve: a GET is not a
        // transaction, and consume-on-serve would throw away the self-healing property of a
        // repeating periodic GET.
        var grant = await _pairingGrants.MintAsync(serial);

        var script = RecoveryCourier.BuildInstallBootstrap(
            BuildBootloaderUrl(bootloader.AppVersionId),
            BootloaderScriptHash(bootloader),
            BuildAppBundleUrl(appBundle.AppVersionId),
            appBundle.ZipFileHash,
            _callbackUrls.ResolveForProvisioning(),
            grant);

        await _authorizations.MarkBootstrapServedAsync(authorization.ProvisioningAuthorizationId);

        _logger.LogInformation(
            "Serial {Serial} is armed; serving install bootstrap with bootloader {Bootloader} and bundle {Bundle}",
            serial, bootloader.VersionNumber, appBundle.VersionNumber);

        return new RecoveryDecision(script, null, "Install bootstrap served");
    }

    /// <summary>
    /// Conversion means a device row for this serial enrolled as the native flavour. That is the
    /// unambiguous "it converted", so confirmation needs no new state of its own.
    /// </summary>
    private static Task<TblDevice?> FindConvertedDeviceAsync(IDirectorDBContext db, string serial)
        => db.TblDevices
            .Include(d => d.Platform)
            .FirstOrDefaultAsync(d => d.SerialNumber == serial
                                   && d.Platform != null
                                   && d.Platform.PlatformName == EngageBrightSignPlatformName);

    private static async Task<TblAppVersion?> FindKnownGoodBootloaderAsync(IDirectorDBContext db)
    {
        var setting = await db.TblSystemSettings.FindAsync("engage:knownGoodBootloaderVersionId");
        if (setting?.SettingValue is not { } raw || !int.TryParse(raw, out var appVersionId))
            return null;

        return await db.TblAppVersions.FirstOrDefaultAsync(v => v.AppVersionId == appVersionId);
    }

    /// <summary>
    /// The anonymous bootloader route, /autorun variant. Anonymity is deliberate and load-bearing:
    /// this courier runs on a player with a broken bootloader, no running app and therefore no
    /// device token, so "everything behind the device token" and "the courier can fetch the
    /// bootloader" cannot both hold. What makes it safe is an invariant rather than a property,
    /// that the bootloader artifact is tenant-neutral by construction.
    ///
    /// The /autorun suffix matters: the courier commits by renaming what it downloaded onto
    /// sd:/autorun.brs and cannot unpack a zip, so it needs the script rather than the artifact.
    /// The update pipeline offers the bare route and gets the zip, which the running bootloader
    /// unpacks. Hashes follow the bytes: this path pairs with BootloaderScriptHash, that one with
    /// the recorded ZipFileHash.
    /// </summary>
    private string BuildBootloaderUrl(int appVersionId)
        => $"{_callbackUrls.ResolveForProvisioning()}/api/Recovery/bootloader/{_tenantContext.Subdomain}/{appVersionId}/autorun";

    /// <summary>
    /// The ordinary authenticated artifact route, which the bootstrap reaches with the grant.
    ///
    /// Deliberately not a second anonymous route. The bootloader's anonymity rests on a bootloader
    /// being tenant-neutral by construction, an invariant that says nothing about an app bundle,
    /// and the grant makes a second one unnecessary: it is an ordinary device token, so this route
    /// already serves it.
    /// </summary>
    private string BuildAppBundleUrl(int appVersionId)
        => $"{_callbackUrls.ResolveForProvisioning()}/api/Update/versions/{appVersionId}/artifact";

    /// <summary>
    /// The newest app bundle on the native platform, for an armed serial that pinned none.
    ///
    /// Newest by upload rather than by version string: the pipeline already orders rows that way
    /// and a version comparator here would be a second opinion about ordering.
    /// </summary>
    private static async Task<TblAppVersion?> FindLatestAppBundleAsync(IDirectorDBContext db)
        => await db.TblAppVersions
            .Where(v => v.ArtifactKind == ArtifactKinds.App
                     && v.Platform != null
                     && v.Platform.PlatformName == EngageBrightSignPlatformName)
            .OrderByDescending(v => v.AppVersionId)
            .FirstOrDefaultAsync();

    /// <summary>
    /// How long to hold off serving another install bootstrap, or null to serve one now.
    ///
    /// Pure, because it is the rule that decides whether a takeover converges or loops forever and
    /// it should be readable without a player attached.
    /// </summary>
    public static TimeSpan? BootstrapHoldOff(DateTime? servedAtUtc, DateTime nowUtc)
    {
        if (servedAtUtc is not { } servedAt)
            return null;

        var elapsed = nowUtc - servedAt;
        if (elapsed >= BootstrapGrace)
            return null;

        // Never shorter than the ordinary armed retry, and never negative: a clock that moved
        // backwards, which a player without a battery-backed RTC routinely causes, must not turn
        // into a Retry-After the caller cannot use.
        var remaining = BootstrapGrace - elapsed;
        return remaining < ArmedRetry ? ArmedRetry : remaining;
    }

    /// <summary>
    /// The hash of what the bootloader route actually serves, which is the autorun inside the
    /// artifact rather than the artifact. The recorded ZipFileHash covers the zip, so using it here
    /// would make every courier reject a bootloader that arrived perfectly intact.
    ///
    /// Null on any failure, which skips verification rather than refusing: the artifact still
    /// arrived over TLS, and a player stuck in recovery is worse than one that installed a
    /// bootloader it could not second-guess. Same call the courier makes for a missing roEVPDigest.
    /// </summary>
    private string? BootloaderScriptHash(TblAppVersion bootloader)
    {
        var path = _artifacts.ResolveExisting(bootloader.ZipFilePath);
        if (path == null)
        {
            _logger.LogError("Bootloader {Version} has no artifact on disk at {Path}",
                bootloader.VersionNumber, bootloader.ZipFilePath);
            return null;
        }

        var script = BootloaderScript.Extract(path);
        if (script == null)
        {
            _logger.LogError("Bootloader artifact {Path} carries no root {Entry}",
                bootloader.ZipFilePath, BootloaderScript.EntryName);
            return null;
        }

        return BootloaderScript.Sha256Hex(script);
    }

    private static bool IsLastResort(string? recoveryMode)
        => string.Equals(recoveryMode?.Trim(), "last-resort", StringComparison.OrdinalIgnoreCase);

    public const string EngageBrightSignPlatformName = "EngageBrightSign";
}
