using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

/// <summary>
/// Flips a location's playback flags from the content page, invalidating the location's publication with
/// each change.
/// </summary>
public interface ILocationPlaybackService
{
    /// <returns>The new value, or null when the location does not exist.</returns>
    Task<bool?> ToggleShuffleAsync(int locationId, CancellationToken ct = default);

    /// <returns>The new value, or null when the location does not exist.</returns>
    Task<bool?> ToggleFadeAsync(int locationId, CancellationToken ct = default);
}

public class LocationPlaybackService : ILocationPlaybackService
{
    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly IPublicationInvalidator _invalidator;

    public LocationPlaybackService(IDbContextFactory<DirectorDbContext> dbFactory, IPublicationInvalidator invalidator)
    {
        _dbFactory = dbFactory;
        _invalidator = invalidator;
    }

    public Task<bool?> ToggleShuffleAsync(int locationId, CancellationToken ct = default) =>
        ToggleAsync(locationId, l => l.EnableShuffle = !l.EnableShuffle, l => l.EnableShuffle, ct);

    public Task<bool?> ToggleFadeAsync(int locationId, CancellationToken ct = default) =>
        ToggleAsync(locationId, l => l.EnableFade = !l.EnableFade, l => l.EnableFade, ct);

    private async Task<bool?> ToggleAsync(int locationId, Action<TblLocation> toggle, Func<TblLocation, bool> read, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var location = await db.TblLocations.FirstOrDefaultAsync(l => l.LocationId == locationId, ct);
        if (location == null)
            return null;

        toggle(location);
        await db.SaveChangesAsync(ct);
        _invalidator.LocationChanged(locationId);
        return read(location);
    }
}
