namespace DXSuite.Director.Models;

/// <summary>
/// Maps an asset's type to the coarse rendering category external consumers switch on.
/// Players, Moxie scripts, and panel documents all need to know whether a URL is a picture,
/// a movie, or a web page, and none of them should be interpreting AssetTypeId integers.
/// </summary>
public static class AssetContentKind
{
    public const string Image = "image";
    public const string Video = "video";
    public const string Web = "web";
    public const string None = "none";

    /// <summary>
    /// Streams render through the same video path as files, so they collapse to "video".
    /// Anything unrecognized falls back to "image", matching the long-standing behavior of
    /// the zone-content builder.
    /// </summary>
    public static string FromAssetTypeId(int assetTypeId) =>
        (Types.AssetType)assetTypeId switch
        {
            Types.AssetType.Video => Video,
            Types.AssetType.Stream => Video,
            Types.AssetType.Web => Web,
            _ => Image
        };

    public static string FromAsset(Entities.TblAsset? asset) =>
        asset == null ? Image : FromAssetTypeId(asset.AssetTypeId);

    /// <summary>
    /// Web assets carry their destination in AssetPath verbatim; file assets need the site
    /// prefix. Callers that hand URLs to an external consumer must not conflate the two.
    /// </summary>
    public static string ResolveUrl(Entities.TblAsset asset, string siteUrl) =>
        asset.AssetTypeId == (int)Types.AssetType.Web
            ? asset.AssetPath ?? string.Empty
            : siteUrl + asset.AssetPath;
}
