using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using DXSuite.Director.Models.BrightSign;

namespace DXSuite.Director.Filters;

/// <summary>
/// Stamps the device's operator-confirmed time zone onto every PlayerContent response, following
/// PlayerPollIntervalFilter.
///
/// GetPlayerContent builds nine response literals and eight of them have a device row in hand. One
/// of those is the "no location or display assigned" branch, which is the state of a device just
/// commissioned and therefore exactly the device whose zone an operator has just set. A
/// hand-copied field would miss it, and it must also ride the NotModified path, which is the normal
/// path for almost every poll.
/// </summary>
public class PlayerOsTimeZoneFilter : IAsyncResultFilter
{
    /// <summary>Where the action stashes the resolved value, at the point the device row is loaded.</summary>
    public const string ContextItemKey = "PlayerOsTimeZone";

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { Value: PlayerContentResponse response }
            && context.HttpContext.Items.TryGetValue(ContextItemKey, out var stashed)
            && stashed is string setTimeZoneArgument)
        {
            response.OsTimeZone = setTimeZoneArgument;
        }

        await next();
    }
}
