using DXSuite.Director.Entities;

namespace DXSuite.Director.Services.WallPlatforms;

/// <summary>
/// Pixera wall layout platform stub. ApplyLayoutAsync is not yet implemented —
/// it requires access to a physical Pixera dev instance for JSON-RPC timeline/clip
/// development. See docs/wall-layout-platform-abstraction.md for the planned exchange.
/// </summary>
public class PixeraWallLayoutPlatform : IWallLayoutPlatform
{
    public string PlatformType => "pixera";

    public Task<bool> SupportsEndpointAsync(TblEndpoint endpoint)
        => Task.FromResult(string.Equals(endpoint.Type, "Pixera", StringComparison.OrdinalIgnoreCase));

    public Task<LayoutPushResult> ApplyLayoutAsync(
        TblEndpoint endpoint, IReadOnlyList<ResolvedCluster> clusters, CancellationToken ct)
        => Task.FromResult(LayoutPushResult.NotImplemented());

    public Task<LayoutCapabilities> GetCapabilitiesAsync(TblEndpoint endpoint)
        => Task.FromResult(new LayoutCapabilities
        {
            SupportsWebBackground = false,
            SupportsDynamicClipSwap = false,
            MaxZonesPerScreen = 1
        });
}
