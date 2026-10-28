namespace DXSuite.Director.Services;

/// <summary>
/// Cached publications are shared by every device at a display or location for thirty minutes, so they must
/// not carry the host, scheme or media access token of whichever request built them. Builders write these
/// placeholders in place of the site and media bases, and delivery swaps in the asking request's.
/// </summary>
public static class PublicationUrls
{
    /// <summary>Stands for the request's site base, for example <c>https://tenant.example.com</c>.</summary>
    public const string SiteBase = "dx-publication-site:";

    /// <summary>Stands for the request's media base, which carries the tenant's media access token.</summary>
    public const string MediaBase = "dx-publication-media:";

    /// <summary>
    /// Replaces a leading placeholder with the request's base. Any other URL, including a null one from a web
    /// content file with no url, is returned unchanged.
    /// </summary>
    public static string Resolve(string url, string siteUrl, Func<string> mediaUrl)
    {
        if (string.IsNullOrEmpty(url))
            return url;
        if (url.StartsWith(MediaBase, StringComparison.Ordinal))
            return mediaUrl() + url[MediaBase.Length..];
        if (url.StartsWith(SiteBase, StringComparison.Ordinal))
            return siteUrl + url[SiteBase.Length..];
        return url;
    }
}
