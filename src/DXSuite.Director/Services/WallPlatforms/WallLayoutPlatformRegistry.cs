using DXSuite.Director.Entities;

namespace DXSuite.Director.Services.WallPlatforms;

public class WallLayoutPlatformRegistry : IWallLayoutPlatformRegistry
{
    private readonly IReadOnlyList<IWallLayoutPlatform> _platforms;

    // Resolved via DI IEnumerable — register each platform as IWallLayoutPlatform in the container.
    public WallLayoutPlatformRegistry(IEnumerable<IWallLayoutPlatform> platforms)
    {
        _platforms = platforms.ToList();
    }

    public void Register(IWallLayoutPlatform platform)
    {
        // No-op on this implementation — use DI registration instead.
    }

    public async Task<IWallLayoutPlatform?> GetForAsync(TblEndpoint endpoint)
    {
        foreach (var platform in _platforms)
        {
            if (await platform.SupportsEndpointAsync(endpoint))
                return platform;
        }
        return null;
    }
}
