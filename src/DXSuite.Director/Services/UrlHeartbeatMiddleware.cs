using DXSuite.Director.Entities;
using Microsoft.AspNetCore.Http;

namespace DXSuite.Director.Services;

/// <summary>
/// Captures heartbeats for player-consumed Director URLs: any request on a
/// monitored route prefix carrying a valid `id=` query parameter is recorded.
/// New URL surfaces join the monitoring system by adding their prefix here —
/// no per-endpoint wiring.
/// </summary>
public class UrlHeartbeatMiddleware
{
    private static readonly string[] MonitoredPrefixes =
    [
        "/videowall", "/pixera", "/api/videowall", "/api/pixera",
    ];

    private readonly RequestDelegate _next;

    public UrlHeartbeatMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, UrlHeartbeatService heartbeats)
    {
        var path = context.Request.Path;
        if (MonitoredPrefixes.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase))
            && context.Request.Query.TryGetValue("id", out var idValues))
        {
            var monitorId = idValues.ToString();
            if (UrlHeartbeatService.MonitorIdPattern.IsMatch(monitorId))
            {
                int? locationId = null;
                if (context.Request.Query.TryGetValue("locationId", out var locVal)
                    && int.TryParse(locVal, out var loc))
                    locationId = loc;
                // /api/videowall/allocation/{locationId} carries it in the path instead.
                else if (path.Value is { } pv)
                {
                    var lastSegment = pv.TrimEnd('/').Split('/')[^1];
                    if (int.TryParse(lastSegment, out var pathLoc)) locationId = pathLoc;
                }

                var db = context.RequestServices.GetRequiredService<IDirectorDBContext>();
                // Page documents (non-/api) are the operator-entered URLs; they own
                // the row's displayed UrlKey while API polls just beat the heart.
                bool isPageRequest = !path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
                try
                {
                    await heartbeats.RecordAsync(
                        db, monitorId,
                        UrlHeartbeatService.NormalizeUrl(path, context.Request.Query),
                        isPageRequest,
                        locationId,
                        context.Connection.RemoteIpAddress?.ToString());
                }
                catch (Exception ex)
                {
                    // A heartbeat must never take down the page it monitors: with the
                    // DB unreachable the wall shell still serves (it needs no data),
                    // and the in-memory heartbeat still counts for the sweeper.
                    context.RequestServices.GetRequiredService<ILogger<UrlHeartbeatMiddleware>>()
                        .LogDebug(ex, "URL heartbeat record failed for '{MonitorId}'", monitorId);
                }
            }
        }

        await _next(context);
    }
}
