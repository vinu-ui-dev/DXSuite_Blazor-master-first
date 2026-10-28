using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DXSuite.Director.Entities;
using DXSuite.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services;

/// <summary>What an operator fills in to generate a provisioning card image.</summary>
public sealed record ProvisioningPackageRequest(
    string PackageName,
    string HardwareFamily,
    int? FirmwareImageId,
    string? TimeSourceUrl,
    string? CustomRootCaPem,
    string? ProxyHostPort,
    string? WifiSsid,
    string? WifiPsk,
    string DwsPosition);

/// <summary>
/// The bootstrap DWS password, returned once at generation and never again.
/// </summary>
public sealed record ProvisioningPackageResult(TblProvisioningPackage Package, string BootstrapDwsPassword);

/// <summary>
/// Generates and reissues provisioning packages.
///
/// A package is a persisted record rather than a fire-and-forget download, because a technician
/// calling six weeks later with a corrupt card needs <i>that</i> package back, not a new one with
/// a new DWS password that the players already in the field will not accept.
/// </summary>
public interface IProvisioningPackageService
{
    Task<ProvisioningPackageResult> GenerateAsync(ProvisioningPackageRequest request, string? createdBy);

    /// <summary>
    /// Rebuilds the same card from an existing record. Same fingerprint, same bootstrap password,
    /// same firmware pin: this is the corrupt-card path, not a regeneration.
    /// </summary>
    Task<TblProvisioningPackage> ReissueAsync(int provisioningPackageId, string? reissuedBy);

    /// <summary>
    /// Withdraws a leaked package. A superseded package stays downloadable because cards in the
    /// field reference it; revocation is the deliberate escape hatch, and the fingerprint recorded
    /// on each device row is what answers which players came off it.
    /// </summary>
    Task<bool> RevokeAsync(int provisioningPackageId, string? revokedBy);

    Task<List<TblProvisioningPackage>> GetAllAsync();
    Task<TblProvisioningPackage?> GetByIdAsync(int provisioningPackageId);
}

public sealed class ProvisioningPackageService : IProvisioningPackageService
{
    private const string PackageDirectory = "ProvisioningPackages";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPlayerCallbackUrlResolver _callbackUrls;
    private readonly ITenantContext _tenantContext;
    private readonly ProvisioningSecretProtector _secretProtector;
    private readonly IPlayerArtifactStore _artifactStore;
    private readonly ILogger<ProvisioningPackageService> _logger;

    public ProvisioningPackageService(
        IServiceScopeFactory scopeFactory,
        IPlayerCallbackUrlResolver callbackUrls,
        ITenantContext tenantContext,
        ProvisioningSecretProtector secretProtector,
        IPlayerArtifactStore artifactStore,
        ILogger<ProvisioningPackageService> logger)
    {
        _scopeFactory = scopeFactory;
        _callbackUrls = callbackUrls;
        _tenantContext = tenantContext;
        _secretProtector = secretProtector;
        _artifactStore = artifactStore;
        _logger = logger;
    }

    public async Task<ProvisioningPackageResult> GenerateAsync(ProvisioningPackageRequest request, string? createdBy)
    {
        if (!HardwareFamilies.IsValid(request.HardwareFamily))
            throw new InvalidOperationException($"Unknown hardware family '{request.HardwareFamily}'.");

        if (!DwsPositions.IsValid(request.DwsPosition))
            throw new InvalidOperationException($"Unknown DWS position '{request.DwsPosition}'.");

        if (!string.IsNullOrWhiteSpace(request.ProxyHostPort) && !LooksLikeHostPort(request.ProxyHostPort))
            throw new InvalidOperationException("The proxy must be a literal host:port. PAC files are not accepted.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var firmware = await ResolveFirmwareAsync(db, request);

        // Never from the request: the operator generating a package is on whatever host they
        // logged in to, and the card boots at a customer site months later.
        var endpointUrl = _callbackUrls.ResolveForProvisioning();
        if (string.IsNullOrWhiteSpace(endpointUrl))
            throw new InvalidOperationException(
                "This tenant has no site URL configured, so a package would carry no endpoint for the player to call back on.");

        // Shared by every player made from this package, shown once, and rotated per device after
        // enrollment. Reissuing keeps it; regenerating mints a new one, which bounds a leak to a
        // package rather than to a tenant.
        var bootstrapPassword = GenerateBootstrapPassword();

        var config = ProvisioningCardConfig.Build(new ProvisioningCardConfig.Inputs(
            EndpointUrl: endpointUrl,
            CustomerCode: _tenantContext.CustomerCode,
            TenantToken: _tenantContext.Subdomain,
            TimeSourceUrl: request.TimeSourceUrl,
            ProxyHostPort: request.ProxyHostPort,
            DwsPosition: request.DwsPosition,
            BootstrapDwsPassword: bootstrapPassword,
            HasCustomRootCa: !string.IsNullOrWhiteSpace(request.CustomRootCaPem)));

        var fingerprint = ProvisioningCardConfig.ComputeFingerprint(
            config, request.HardwareFamily, firmware.VersionNumber, request.CustomRootCaPem);

        var package = new TblProvisioningPackage
        {
            PackageName = request.PackageName,
            HardwareFamily = request.HardwareFamily,
            FirmwareImageId = firmware.FirmwareImageId,
            FirmwarePinOverridden = request.FirmwareImageId.HasValue && !firmware.IsDefaultForFamily,
            EndpointUrl = endpointUrl,
            TimeSourceUrl = request.TimeSourceUrl,
            CustomRootCaPem = request.CustomRootCaPem,
            ProxyHostPort = request.ProxyHostPort,
            WifiSsid = request.WifiSsid,
            WifiPskProtected = _secretProtector.Encrypt(request.WifiPsk),
            DwsPosition = request.DwsPosition,
            BootstrapDwsPasswordProtected = _secretProtector.Encrypt(bootstrapPassword)!,
            // Off in every shipped package. Set deliberately either way, because the Chromium
            // inspector needs this registry key in addition to the widget's port and the legacy
            // autorun set only the port.
            WebInspectorEnabled = false,
            ConfigFingerprint = fingerprint,
            CreatedBy = createdBy,
            CreatedDate = DateTime.UtcNow,
            ModifiedBy = createdBy,
            ModifiedDate = DateTime.UtcNow,
            ZipFileName = "",
            ZipFilePath = ""
        };

        db.TblProvisioningPackages.Add(package);
        await db.SaveChangesAsync();

        await WritePackageAsync(db, package, config, firmware, request.CustomRootCaPem);
        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Generated provisioning package {PackageId} '{PackageName}' for family {Family} with fingerprint {Fingerprint}",
            package.ProvisioningPackageId, package.PackageName, package.HardwareFamily, fingerprint);

        return new ProvisioningPackageResult(package, bootstrapPassword);
    }

    public async Task<TblProvisioningPackage> ReissueAsync(int provisioningPackageId, string? reissuedBy)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var package = await db.TblProvisioningPackages
            .Include(p => p.FirmwareImage)
            .FirstOrDefaultAsync(p => p.ProvisioningPackageId == provisioningPackageId)
            ?? throw new KeyNotFoundException($"Provisioning package {provisioningPackageId} not found");

        if (package.IsRevoked)
            throw new InvalidOperationException("A revoked package cannot be reissued.");

        var bootstrapPassword = _secretProtector.Decrypt(package.BootstrapDwsPasswordProtected)
            ?? throw new InvalidOperationException(
                "This package's bootstrap password cannot be read, so the card it produced cannot be rebuilt.");

        // Rebuilt from the stored record rather than from current defaults, so the card is the one
        // the players in the field already trust. A different fingerprint here would make every
        // player from this package re-apply its config on the next boot.
        var config = ProvisioningCardConfig.Build(new ProvisioningCardConfig.Inputs(
            EndpointUrl: package.EndpointUrl,
            CustomerCode: _tenantContext.CustomerCode,
            TenantToken: _tenantContext.Subdomain,
            TimeSourceUrl: package.TimeSourceUrl,
            ProxyHostPort: package.ProxyHostPort,
            DwsPosition: package.DwsPosition,
            BootstrapDwsPassword: bootstrapPassword,
            HasCustomRootCa: !string.IsNullOrWhiteSpace(package.CustomRootCaPem)));

        await WritePackageAsync(db, package, config, package.FirmwareImage, package.CustomRootCaPem);
        package.ModifiedBy = reissuedBy;
        package.ModifiedDate = DateTime.UtcNow;
        await db.SaveChangesAsync();

        _logger.LogInformation("Reissued provisioning package {PackageId} by {User}",
            provisioningPackageId, reissuedBy ?? "unknown");

        return package;
    }

    public async Task<bool> RevokeAsync(int provisioningPackageId, string? revokedBy)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var package = await db.TblProvisioningPackages.FindAsync(provisioningPackageId);
        if (package == null || package.IsRevoked)
            return false;

        package.IsRevoked = true;
        package.RevokedBy = revokedBy;
        package.RevokedDate = DateTime.UtcNow;
        await db.SaveChangesAsync();

        _logger.LogWarning("Revoked provisioning package {PackageId} ('{PackageName}') by {User}",
            provisioningPackageId, package.PackageName, revokedBy ?? "unknown");

        return true;
    }

    public async Task<List<TblProvisioningPackage>> GetAllAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await db.TblProvisioningPackages
            .Include(p => p.FirmwareImage)
            .OrderByDescending(p => p.CreatedDate)
            .ToListAsync();
    }

    public async Task<TblProvisioningPackage?> GetByIdAsync(int provisioningPackageId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await db.TblProvisioningPackages
            .Include(p => p.FirmwareImage)
            .FirstOrDefaultAsync(p => p.ProvisioningPackageId == provisioningPackageId);
    }

    private static async Task<TblFirmwareImage> ResolveFirmwareAsync(IDirectorDBContext db, ProvisioningPackageRequest request)
    {
        if (request.FirmwareImageId is { } pinned)
        {
            var chosen = await db.TblFirmwareImages.FindAsync(pinned)
                ?? throw new InvalidOperationException($"Firmware image {pinned} not found.");

            if (!string.Equals(chosen.Family, request.HardwareFamily, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Firmware {chosen.VersionNumber} is for family {chosen.Family}, not {request.HardwareFamily}.");

            return chosen;
        }

        return await db.TblFirmwareImages
            .Where(f => f.Family == request.HardwareFamily && f.IsDefaultForFamily)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException(
                $"No default firmware is set for family {request.HardwareFamily}. Upload one, or pin a version on this package.");
    }

    /// <summary>
    /// Writes the wrapper zip: <c>autorun.zip</c> plus the family firmware file beside it.
    ///
    /// The firmware cannot live inside <c>autorun.zip</c>, because the OS scans the card root for a
    /// .bsfw and cannot see inside a zip. That keeps the technician instruction to one line: unzip
    /// to the root of a blank card.
    /// </summary>
    private async Task WritePackageAsync(
        IDirectorDBContext db, TblProvisioningPackage package, Dictionary<string, string> config,
        TblFirmwareImage? firmware, string? customRootCaPem)
    {
        var configJson = ProvisioningCardConfig.Serialize(config, package.ConfigFingerprint);

        using var buffer = new MemoryStream();

        using (var wrapper = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var autorunBuffer = new MemoryStream())
            {
                using (var autorun = new ZipArchive(autorunBuffer, ZipArchiveMode.Create, leaveOpen: true))
                {
                    await WriteEntryAsync(autorun, "config.json", Encoding.UTF8.GetBytes(configJson));

                    // Beside config.json rather than inside it: the bootloader installs a CA by file
                    // name, and a PEM's newlines cannot survive the card's flat config format.
                    if (!string.IsNullOrWhiteSpace(customRootCaPem))
                    {
                        await WriteEntryAsync(autorun, ProvisioningCardConfig.CustomRootCaFileName,
                            Encoding.UTF8.GetBytes(customRootCaPem));
                    }
                }

                autorunBuffer.Position = 0;
                await WriteEntryAsync(wrapper, "autorun.zip", autorunBuffer.ToArray());
            }

            if (firmware != null)
            {
                var firmwarePath = _artifactStore.ResolveExisting(firmware.FilePath);
                if (firmwarePath != null)
                {
                    await WriteEntryAsync(wrapper, Path.GetFileName(firmware.FileName),
                        await File.ReadAllBytesAsync(firmwarePath));
                }
                else
                {
                    _logger.LogWarning(
                        "Firmware image {FirmwareImageId} is recorded but missing from disk; package {PackageId} ships without it",
                        firmware.FirmwareImageId, package.ProvisioningPackageId);
                }
            }
        }

        var bytes = buffer.ToArray();
        var fileName = $"Provisioning_{package.HardwareFamily}_{package.ProvisioningPackageId}.zip";
        var storedPath = await _artifactStore.SaveAsync(new MemoryStream(bytes),
            $"{PackageDirectory}/{package.ProvisioningPackageId}/{fileName}");

        package.ZipFileName = fileName;
        package.ZipFilePath = storedPath;
        package.ZipFileSize = bytes.Length;
        package.ZipSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(content);
    }

    /// <summary>A literal host:port. A URL here would be a PAC file, which is refused.</summary>
    private static bool LooksLikeHostPort(string value)
    {
        if (value.Contains("://", StringComparison.Ordinal) || value.Contains('/'))
            return false;

        var parts = value.Split(':');
        return parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && int.TryParse(parts[1], out var port)
               && port is > 0 and <= 65535;
    }

    private static string GenerateBootstrapPassword()
    {
        // Never blank and never port 80 open, which is what the legacy autorun shipped.
        var bytes = RandomNumberGenerator.GetBytes(18);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}
