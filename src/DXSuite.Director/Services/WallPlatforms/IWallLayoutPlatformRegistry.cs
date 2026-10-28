using DXSuite.Director.Entities;

namespace DXSuite.Director.Services.WallPlatforms;

public interface IWallLayoutPlatformRegistry
{
    /// <summary>
    /// Returns the platform implementation that claims the given endpoint, or null if none match.
    /// </summary>
    Task<IWallLayoutPlatform?> GetForAsync(TblEndpoint endpoint);

    void Register(IWallLayoutPlatform platform);
}
