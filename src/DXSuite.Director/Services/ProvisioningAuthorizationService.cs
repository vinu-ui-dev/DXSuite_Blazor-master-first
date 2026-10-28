using DXSuite.Director.Entities;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services;

/// <summary>
/// Operator permission for one serial to convert itself into this tenant.
///
/// Arming is the operator-initiated inverse of pairing: it names a serial before that player has
/// asked for anything, and it lives until the conversion happens or somebody disarms it. The
/// players it covers are not rows in tblDevice yet, which is why it has a table of its own and why
/// "how many armed serials have converted" is a query nothing else could answer.
/// </summary>
public interface IProvisioningAuthorizationService
{
    Task<TblProvisioningAuthorization> ArmAsync(
        string serialNumber, TimeSpan validFor, int? pinnedAppVersionId, int? pinnedBootloaderVersionId,
        int? provisioningPackageId, string? armedBy);

    /// <summary>The kill switch for a takeover in flight.</summary>
    Task<bool> DisarmAsync(int provisioningAuthorizationId, string? disarmedBy);

    /// <summary>
    /// The live authorisation for a serial, or null. Expiry is evaluated here rather than trusted
    /// from the stored state, so an armed row that nobody swept still reads as expired.
    /// </summary>
    Task<TblProvisioningAuthorization?> FindLiveAsync(string serialNumber);

    /// <summary>
    /// Stamps that an install bootstrap went out, which holds off the next one. Separate from
    /// conversion because it records an attempt, not an outcome: the grace lapses and the serial
    /// is served again, which is what keeps a repeating periodic GET self-healing.
    /// </summary>
    Task MarkBootstrapServedAsync(int provisioningAuthorizationId);

    /// <summary>Marks an authorisation converted and records which device it became.</summary>
    Task<bool> RecordConversionAsync(int provisioningAuthorizationId, string deviceId);

    Task<List<TblProvisioningAuthorization>> GetAllAsync();

    /// <summary>Moves lapsed rows out of Armed so the list means what it says.</summary>
    Task<int> ExpireLapsedAsync();
}

public sealed class ProvisioningAuthorizationService : IProvisioningAuthorizationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProvisioningAuthorizationService> _logger;

    public ProvisioningAuthorizationService(
        IServiceScopeFactory scopeFactory, ILogger<ProvisioningAuthorizationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<TblProvisioningAuthorization> ArmAsync(
        string serialNumber, TimeSpan validFor, int? pinnedAppVersionId, int? pinnedBootloaderVersionId,
        int? provisioningPackageId, string? armedBy)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
            throw new ArgumentException("A serial number is required.", nameof(serialNumber));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        // Re-arming replaces rather than stacks, and picks up whatever is current. A hundred-player
        // takeover straddles a release, and two live authorisations for one serial would make
        // "what does this conversion install" unanswerable.
        var existing = await db.TblProvisioningAuthorizations
            .Where(a => a.SerialNumber == serialNumber && a.State == ProvisioningAuthorizationStates.Armed)
            .ToListAsync();

        foreach (var superseded in existing)
        {
            superseded.State = ProvisioningAuthorizationStates.Disarmed;
            superseded.DisarmedBy = armedBy;
            superseded.DisarmedDate = DateTime.UtcNow;
        }

        var authorization = new TblProvisioningAuthorization
        {
            SerialNumber = serialNumber.Trim(),
            State = ProvisioningAuthorizationStates.Armed,
            ArmedBy = armedBy,
            ArmedDate = DateTime.UtcNow,
            ExpiresDate = DateTime.UtcNow.Add(validFor),
            PinnedAppVersionId = pinnedAppVersionId,
            PinnedBootloaderVersionId = pinnedBootloaderVersionId,
            ProvisioningPackageId = provisioningPackageId
        };

        db.TblProvisioningAuthorizations.Add(authorization);
        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Armed provisioning for serial {SerialNumber} until {ExpiresDate} by {User}, pinned to app {AppVersionId} and bootloader {BootloaderVersionId}",
            serialNumber, authorization.ExpiresDate, armedBy ?? "unknown", pinnedAppVersionId, pinnedBootloaderVersionId);

        return authorization;
    }

    public async Task MarkBootstrapServedAsync(int provisioningAuthorizationId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var authorization = await db.TblProvisioningAuthorizations.FindAsync(provisioningAuthorizationId);
        if (authorization == null)
            return;

        authorization.BootstrapServedDate = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<bool> DisarmAsync(int provisioningAuthorizationId, string? disarmedBy)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var authorization = await db.TblProvisioningAuthorizations.FindAsync(provisioningAuthorizationId);
        if (authorization == null || authorization.State != ProvisioningAuthorizationStates.Armed)
            return false;

        authorization.State = ProvisioningAuthorizationStates.Disarmed;
        authorization.DisarmedBy = disarmedBy;
        authorization.DisarmedDate = DateTime.UtcNow;
        await db.SaveChangesAsync();

        _logger.LogInformation("Disarmed provisioning for serial {SerialNumber} by {User}",
            authorization.SerialNumber, disarmedBy ?? "unknown");

        return true;
    }

    public async Task<TblProvisioningAuthorization?> FindLiveAsync(string serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
            return null;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var now = DateTime.UtcNow;

        return await db.TblProvisioningAuthorizations
            .Include(a => a.PinnedAppVersion)
            .Include(a => a.PinnedBootloaderVersion)
            .Include(a => a.ProvisioningPackage)
            .FirstOrDefaultAsync(a => a.SerialNumber == serialNumber
                                   && a.State == ProvisioningAuthorizationStates.Armed
                                   && a.ExpiresDate > now);
    }

    public async Task<bool> RecordConversionAsync(int provisioningAuthorizationId, string deviceId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var authorization = await db.TblProvisioningAuthorizations.FindAsync(provisioningAuthorizationId);
        if (authorization == null || authorization.State != ProvisioningAuthorizationStates.Armed)
            return false;

        authorization.State = ProvisioningAuthorizationStates.Converted;
        authorization.ConvertedDate = DateTime.UtcNow;
        authorization.ConvertedDeviceId = deviceId;
        await db.SaveChangesAsync();

        _logger.LogInformation("Serial {SerialNumber} converted as device {DeviceId}",
            authorization.SerialNumber, deviceId);

        return true;
    }

    public async Task<List<TblProvisioningAuthorization>> GetAllAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await db.TblProvisioningAuthorizations
            .Include(a => a.PinnedAppVersion)
            .Include(a => a.PinnedBootloaderVersion)
            .OrderByDescending(a => a.ArmedDate)
            .ToListAsync();
    }

    public async Task<int> ExpireLapsedAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var now = DateTime.UtcNow;
        var lapsed = await db.TblProvisioningAuthorizations
            .Where(a => a.State == ProvisioningAuthorizationStates.Armed && a.ExpiresDate < now)
            .ToListAsync();

        foreach (var authorization in lapsed)
            authorization.State = ProvisioningAuthorizationStates.Expired;

        if (lapsed.Count > 0)
            await db.SaveChangesAsync();

        return lapsed.Count;
    }
}
