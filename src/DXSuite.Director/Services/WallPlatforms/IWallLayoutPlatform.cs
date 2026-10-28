using DXSuite.Director.Entities;

namespace DXSuite.Director.Services.WallPlatforms;

public interface IWallLayoutPlatform
{
    /// <summary>Identifies this platform implementation, e.g. "pixera" or "avalonia-dxsuite".</summary>
    string PlatformType { get; }

    Task<bool> SupportsEndpointAsync(TblEndpoint endpoint);

    /// <summary>
    /// Push the resolved cluster layout to the wall controller endpoint.
    /// Returns NotImplemented when the platform stub is not yet fully wired (e.g., Pixera
    /// pending hardware availability). The scheduler UI must handle this gracefully.
    /// </summary>
    Task<LayoutPushResult> ApplyLayoutAsync(
        TblEndpoint endpoint,
        IReadOnlyList<ResolvedCluster> clusters,
        CancellationToken ct);

    Task<LayoutCapabilities> GetCapabilitiesAsync(TblEndpoint endpoint);
}
