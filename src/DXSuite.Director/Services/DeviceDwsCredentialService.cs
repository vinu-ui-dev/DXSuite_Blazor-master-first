using DXSuite.Director.Entities;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services;

/// <summary>
/// Director's device row is the authoritative store for a player's local web server password.
///
/// Sentinel's own <c>CredentialsJson</c> is <c>varchar(max)</c> deserialised straight to a plaintext
/// dictionary with no data protector, in a solution that protects five other integration
/// credentials, and it has <b>no write path anywhere in the repo</b>: rows are populated out of
/// band. So nothing there could ever have stored or rotated the per-device secret that enrollment
/// promises. Enrollment is a Director event, so the secret is born where it is generated rather
/// than copied across a link that nothing enforces.
/// </summary>
public interface IDeviceDwsCredentialService
{
    /// <summary>
    /// The player's current DWS password, or null when it has not rotated off the package's shared
    /// bootstrap yet. Sentinel's driver calls this rather than reading its own credentials blob.
    /// </summary>
    Task<string?> GetPasswordAsync(string deviceId);

    /// <summary>
    /// Records the per-device secret a player generated for itself after enrollment, so a leaked
    /// provisioning card does not open the fleet.
    /// </summary>
    Task<bool> RecordRotationAsync(string deviceId, string password);

    /// <summary>
    /// Drops the rotation, which puts the device back in the visible "enrolled but not rotated"
    /// state. Called when a config re-apply writes the package's bootstrap password back over the
    /// rotated one: leaving the rotated secret recorded would have Sentinel authenticating with a
    /// password the player no longer has, and failing silently.
    /// </summary>
    Task ClearRotationAsync(string deviceId);
}

public sealed class DeviceDwsCredentialService : IDeviceDwsCredentialService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ProvisioningSecretProtector _secretProtector;
    private readonly ILogger<DeviceDwsCredentialService> _logger;

    public DeviceDwsCredentialService(
        IServiceScopeFactory scopeFactory,
        ProvisioningSecretProtector secretProtector,
        ILogger<DeviceDwsCredentialService> logger)
    {
        _scopeFactory = scopeFactory;
        _secretProtector = secretProtector;
        _logger = logger;
    }

    public async Task<string?> GetPasswordAsync(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return null;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var protectedPassword = await db.TblDevices
            .AsNoTracking()
            .Where(d => d.DeviceId == deviceId)
            .Select(d => d.DwsPasswordProtected)
            .FirstOrDefaultAsync();

        return _secretProtector.Decrypt(protectedPassword);
    }

    public async Task<bool> RecordRotationAsync(string deviceId, string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var device = await db.TblDevices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
        if (device == null)
            return false;

        device.DwsPasswordProtected = _secretProtector.Encrypt(password);
        device.DwsPasswordRotatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        _logger.LogInformation("Device {DeviceId} rotated its DWS password off the package bootstrap", deviceId);
        return true;
    }

    public async Task ClearRotationAsync(string deviceId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var device = await db.TblDevices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
        if (device?.DwsPasswordRotatedAt == null)
            return;

        device.DwsPasswordProtected = null;
        device.DwsPasswordRotatedAt = null;
        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Device {DeviceId} was re-provisioned, so its DWS rotation is cleared and it will rotate again on next start",
            deviceId);
    }
}
