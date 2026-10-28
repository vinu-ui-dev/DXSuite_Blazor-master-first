using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace DXSuite.Director.Services;

/// <summary>
/// Validates the tenant path token on <c>/MediaAssets</c> requests and strips it before the tenant
/// media rewrite sees the path.
///
/// Must run before <c>UseTenantMedia</c> and therefore before <c>UseStaticFiles</c>, since the
/// point is to stand in front of the media root rather than beside it.
///
/// Every failure is an identical 404. A wrong token, an absent token and a file that does not
/// exist are indistinguishable from outside, which is the discipline <c>PanelController</c>
/// already applies to tenant content reached by a path token.
/// </summary>
public sealed class MediaAssetTokenMiddleware
{
    private const string MediaSegment = "/MediaAssets";

    private readonly RequestDelegate _next;
    private readonly IConfiguration _config;

    public MediaAssetTokenMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _config = config;
    }

    /// <summary>
    /// Enforcement is opt-in, default off, matching <c>DXSuite:TenantMedia:Enabled</c>.
    ///
    /// The server emits tokenised URLs from the moment this ships, but a player caches assets by
    /// URL and holds a manifest it was served earlier, so enforcing immediately would 404 every
    /// already-cached asset until each player happened to re-poll. Emitting first and enforcing
    /// after one manifest cycle closes the window instead of accepting it.
    /// </summary>
    public static bool IsEnforced(IConfiguration config)
        => config.GetValue("DXSuite:MediaAssets:RequireToken", false);

    public async Task InvokeAsync(HttpContext context, IMediaAccessTokenService tokenService)
    {
        var path = context.Request.Path;

        if (!path.StartsWithSegments(MediaSegment, out var remainder))
        {
            await _next(context);
            return;
        }

        var segments = remainder.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];

        var isTokenised = segments.Length >= 2
            && string.Equals(segments[0], MediaAccessTokenService.PathMarker, StringComparison.Ordinal);

        if (isTokenised)
        {
            if (!await tokenService.IsValidAsync(segments[1]))
            {
                await NotFoundAsync(context);
                return;
            }

            // Strip the marker and the token, so everything downstream sees the path it expects
            // and the tenant rewrite and static handler need no knowledge of any of this.
            context.Request.Path = MediaSegment + "/" + string.Join('/', segments.Skip(2));
            await _next(context);
            return;
        }

        if (IsEnforced(_config))
        {
            await NotFoundAsync(context);
            return;
        }

        await _next(context);
    }

    private static Task NotFoundAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }
}

public static class MediaAssetTokenMiddlewareExtensions
{
    public static IApplicationBuilder UseMediaAssetTokens(this IApplicationBuilder app)
        => app.UseMiddleware<MediaAssetTokenMiddleware>();
}
