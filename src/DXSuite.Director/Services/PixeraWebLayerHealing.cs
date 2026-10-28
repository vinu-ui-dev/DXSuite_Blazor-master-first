namespace DXSuite.Director.Services;

/// <summary>
/// Decides which Pixera web resources qualify for post-outage re-navigation.
/// A resource qualifies only when its URL is a wall path carrying a valid id=
/// monitor parameter — the caller then intersects that id with the monitors
/// currently in the missing state, so pages that resumed heartbeating are never
/// re-navigated and live walls see no reload.
/// </summary>
internal static class PixeraWebLayerHealing
{
    /// <summary>
    /// Extracts the monitor id from a wall URL (absolute or path-relative).
    /// Returns null for non-wall paths and missing or invalid id= values —
    /// those resources are skipped, never guessed at.
    /// </summary>
    internal static string? ExtractMonitorId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        string path, query;
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs))
        {
            path = abs.AbsolutePath;
            query = abs.Query;
        }
        else
        {
            var q = url.IndexOf('?');
            path = q < 0 ? url : url[..q];
            query = q < 0 ? "" : url[q..];
        }

        if (!path.StartsWith("/videowall/", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/pixera/", StringComparison.OrdinalIgnoreCase))
            return null;

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            if (!pair[..eq].Equals("id", StringComparison.OrdinalIgnoreCase)) continue;
            var value = Uri.UnescapeDataString(pair[(eq + 1)..]);
            return UrlHeartbeatService.MonitorIdPattern.IsMatch(value) ? value : null;
        }
        return null;
    }
}
