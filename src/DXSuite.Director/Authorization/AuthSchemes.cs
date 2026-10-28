namespace DXSuite.Director.Authorization;

/// <summary>
/// Named authentication scheme identifiers for device and admin API access.
/// </summary>
public static class AuthSchemes
{
    /// <summary>
    /// Scheme for paired signage devices authenticating with a dt_ prefixed device token.
    /// </summary>
    public const string DeviceToken = "DeviceToken";

    /// <summary>
    /// Scheme for admin/integration callers authenticating with a TblApiKey admin key.
    /// </summary>
    public const string AdminApiKey = "AdminApiKey";
}
