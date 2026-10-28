using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using DXSuite.Shared.Multitenancy;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Director.Services;

/// <summary>
/// The solution's only ASP.NET rate limiter, deliberately scoped to the two Engage routes a
/// stranger can reach: the recovery handler and pairing.
///
/// There is no request throttling anywhere in DXSuite today. ApiKeyRateLimiter is a per-IP
/// failed-auth lockout consumed by the auth handlers, and bending it was rejected because a lockout
/// and a throttle want different behaviour on the same request. Keeping this route-scoped keeps the
/// blast radius of a first-of-its-kind change inside a feature nobody is using yet.
/// </summary>
public static class RecoveryRateLimiting
{
    public const string PolicyName = "engage-recovery";

    /// <summary>
    /// Pairing is a denial-of-service and database-noise problem rather than an authentication
    /// one, because approval is still a human act. Unthrottled, an unauthenticated caller can
    /// create unbounded pairing-request rows in any tenant's Director database.
    /// </summary>
    public const string PairingPolicyName = "engage-pairing";

    /// <summary>
    /// Short, because a 204 from an unresolved tenant is reachable by a player in last-resort and
    /// Retry-After is honoured there too. A long value would park a broken player for that long.
    /// </summary>
    public static readonly TimeSpan UnresolvedRetry = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Configures the policy. The Host owns the pipeline, so it calls AddRateLimiter and hands the
    /// options here rather than this module reaching into startup.
    /// </summary>
    public static void Configure(RateLimiterOptions options)
    {
        {
            options.AddPolicy(PolicyName, context =>
            {
                // Partitioned by tenant token plus serial rather than by IP: a site behind one NAT
                // is many players, and throttling them as one would starve a recovering fleet.
                var tenantToken = context.Request.RouteValues.TryGetValue("tenantToken", out var tenant)
                    ? tenant?.ToString() ?? "unknown"
                    : "unknown";

                var serial = context.Request.Headers.TryGetValue("deviceid", out var deviceId)
                    ? deviceId.ToString()
                    : context.Request.Query["deviceid"].ToString();

                return RateLimitPartition.GetFixedWindowLimiter(
                    $"{tenantToken}|{(string.IsNullOrWhiteSpace(serial) ? "unknown" : serial)}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        // A healthy player is told to come back in 24 hours and a recovering one
                        // polls every two minutes, so this is generous by a wide margin and only
                        // catches something that is not behaving like a player.
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0
                    });
            });

            options.AddPolicy(PairingPolicyName, context =>
            {
                var tenant = context.RequestServices.GetService<ITenantContext>() is { IsPopulated: true } t
                    ? t.Subdomain
                    : "unknown";

                // A status poll partitions on the pairing request it names, NOT on the caller's IP.
                //
                // The player polls this every 5 seconds for the code's 600-second life, 120 polls
                // per code. Per IP at 20 per 5 minutes, one player exceeded its own allowance
                // threefold on its own, and a site behind one NAT shared that budget: the largest
                // fleet site measured carries 15 players on one external address, so enrolment
                // there could never have completed. Seen on the bench, where two players sufficed.
                //
                // Partitioning on the request id is not weaker. The id is a server-minted GUID, so
                // a caller polling one they invented gets their own partition and starves nobody,
                // which is exactly what the per-IP partition failed to guarantee.
                if (context.Request.Path.StartsWithSegments("/api/signage/Pairing/status", StringComparison.OrdinalIgnoreCase))
                {
                    var requestId = context.Request.Headers.TryGetValue("x-pairing-request-id", out var header)
                        ? header.ToString()
                        : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                    return RateLimitPartition.GetFixedWindowLimiter(
                        $"{tenant}|status|{requestId}",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            // Sized to the poll: 120 polls a code, with headroom for a retry cycle,
                            // over a window longer than a code lives.
                            PermitLimit = 300,
                            Window = TimeSpan.FromMinutes(15),
                            QueueLimit = 0
                        });
                }

                // Minting a code stays per IP: it is genuinely anonymous, it creates a database row,
                // and it carries no identity that is not attacker-supplied. The limit allows a whole
                // site to boot at once and to re-request on expiry, which is the real burst.
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(
                    $"{tenant}|request|{ip}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0
                    });
            });

            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.Headers["Retry-After"] =
                    ((int)UnresolvedRetry.TotalSeconds).ToString();
                return ValueTask.CompletedTask;
            };
        }
    }
}
