using DXSuite.Director.Entities;

namespace DXSuite.Director.Services.LocationBundles;

public static class LocationBundleConstants
{
    /// <summary>
    /// Sentinel stored in TblLocationType.ComponentTypeName for the seeded
    /// "Custom" / "Custom Display" types. Location.razor dispatches these to the
    /// bundle iframe host instead of reflection-resolving a razor component; the
    /// colon makes it impossible to collide with a real CLR type name. Types must
    /// always be recognized by this sentinel, never by LocationTypeId — identity
    /// PKs differ per environment.
    /// </summary>
    public const string BundleComponentSentinel = "bundle:host";

    /// <summary>Root prefix for bundle files within the tenant's media store.</summary>
    public const string StorageRoot = "location-bundles";

    /// <summary>
    /// Required header on Location Content API writes. Cookies are SameSite=Lax, so a
    /// custom header (unsendable cross-site without CORS preflight) adds CSRF
    /// defense-in-depth.
    /// </summary>
    public const string WriteHeaderName = "X-DX-Bundle";

    /// <summary>
    /// Storage keys carrying this prefix are readable by external players through the
    /// player API; every other key stays private to the bundle's cookie-authenticated UI.
    /// Publication is opt-in by the writer — a bundle exposes state by choosing to write
    /// under this prefix, so no manifest declaration or host change is needed per bundle.
    /// A dot rather than a slash keeps keys inside a single route segment.
    /// </summary>
    public const string PublicKeyPrefix = "public.";

    public static bool IsPublicStorageKey(string? storageKey) =>
        storageKey != null && storageKey.StartsWith(PublicKeyPrefix, StringComparison.Ordinal);

    public static bool IsBundleType(TblLocationType? locationType) =>
        locationType?.ComponentTypeName == BundleComponentSentinel;

    public static string StoragePath(string bundleKey, string version, string relativePath = "") =>
        string.IsNullOrEmpty(relativePath)
            ? $"{StorageRoot}/{bundleKey}/{version}"
            : $"{StorageRoot}/{bundleKey}/{version}/{relativePath}";
}
