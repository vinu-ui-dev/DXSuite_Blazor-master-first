using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;

namespace DXSuite.Director.Services;

/// <summary>
/// Puts a display into Override mode on one asset, or back to its schedule. The one write path for a
/// display's override, so the display's publication is invalidated with every change.
/// </summary>
public interface IDisplayOverrideService
{
    /// <returns>False when the display does not exist.</returns>
    Task<bool> SetOverrideAsync(int displayId, int assetId, CancellationToken ct = default);

    /// <returns>False when the display does not exist.</returns>
    Task<bool> ReturnToScheduleAsync(int displayId, CancellationToken ct = default);
}

public class DisplayOverrideService : IDisplayOverrideService
{
    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly IPublicationInvalidator _invalidator;

    public DisplayOverrideService(IDbContextFactory<DirectorDbContext> dbFactory, IPublicationInvalidator invalidator)
    {
        _dbFactory = dbFactory;
        _invalidator = invalidator;
    }

    public Task<bool> SetOverrideAsync(int displayId, int assetId, CancellationToken ct = default) =>
        WriteAsync(displayId, assetId, Types.DisplayMode.Override, ct);

    public Task<bool> ReturnToScheduleAsync(int displayId, CancellationToken ct = default) =>
        WriteAsync(displayId, null, Types.DisplayMode.Schedule, ct);

    private async Task<bool> WriteAsync(int displayId, int? overrideAssetId, Types.DisplayMode mode, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var display = await db.TblDisplays.FirstOrDefaultAsync(d => d.DisplayId == displayId, ct);
        if (display == null)
            return false;

        display.OverrideAssetId = overrideAssetId;
        display.DisplayMode = (int)mode;
        await db.SaveChangesAsync(ct);
        _invalidator.DisplayChanged(displayId);
        return true;
    }
}
