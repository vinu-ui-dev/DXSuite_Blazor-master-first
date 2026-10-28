using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using DXSuite.Director.Models.BrightSign;

namespace DXSuite.Director.Filters;

/// <summary>
/// Stamps the device's Orientation onto every PlayerContent response, following
/// PlayerOsTimeZoneFilter and for the same reason: GetPlayerContent builds many response literals,
/// including the "no location or display assigned" branch and the NotModified path, and a
/// hand-copied field would miss one.
/// </summary>
public class PlayerOrientationFilter : IAsyncResultFilter
{
    /// <summary>Where the action stashes the widget transform, at the point the device row is loaded.</summary>
    public const string ContextItemKey = "PlayerOrientation";

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { Value: PlayerContentResponse response }
            && context.HttpContext.Items.TryGetValue(ContextItemKey, out var stashed)
            && stashed is string widgetTransform)
        {
            response.Orientation = widgetTransform;
        }

        await next();
    }
}
