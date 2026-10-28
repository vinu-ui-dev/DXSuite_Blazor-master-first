namespace DXSuite.Director.Authorization;

/// <summary>
/// Checks that a request speaking for a device is carrying that device's own token.
///
/// A device token was authenticated but never scoped: validation tested that the hash existed and
/// that the token was active, and the bound DeviceId travelled downstream as a claim nobody
/// compared against the DeviceId in the request. A leaked token could therefore read any device's
/// content in the tenant, and on the native screenshot route the device id was a free query
/// parameter beside a token that was never checked against it.
/// </summary>
public static class DeviceTokenBinding
{
    /// <summary>
    /// True when <paramref name="requestDeviceId"/> is the device this request's token belongs to.
    ///
    /// An unbound token passes. Binding happens on the first heartbeat, after HeartbeatService has
    /// created the device row, so a freshly paired player legitimately arrives unbound once.
    /// </summary>
    public static bool Matches(HttpContext context, string? requestDeviceId)
    {
        var boundDeviceId = context.Items["DeviceId"] as string;

        if (string.IsNullOrEmpty(boundDeviceId))
            return true;

        return string.Equals(boundDeviceId, requestDeviceId, StringComparison.OrdinalIgnoreCase);
    }
}
