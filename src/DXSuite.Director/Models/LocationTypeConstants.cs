using DXSuite.Director.Entities;

namespace DXSuite.Director.Models;

/// <summary>
/// Identifies built-in location types by their component name. LocationTypeId is
/// identity-generated and differs per environment, so it is never the key — the same rule
/// LocationBundleConstants follows for its "bundle:host" sentinel.
/// </summary>
public static class LocationTypeConstants
{
    public const string AssetLibraryComponent = "DXSuite.Director.Components.LocationTypes.AssetLibrary";
    public const string PlaylistComponent = "DXSuite.Director.Components.LocationTypes.Playlist";

    public static bool IsAssetLibrary(TblLocationType? locationType) =>
        locationType?.ComponentTypeName == AssetLibraryComponent;

    public static bool IsPlaylist(TblLocationType? locationType) =>
        locationType?.ComponentTypeName == PlaylistComponent;
}
