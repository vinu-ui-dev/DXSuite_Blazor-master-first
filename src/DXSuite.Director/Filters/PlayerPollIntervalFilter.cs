using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using DXSuite.Director.Models.BrightSign;
using DXSuite.Director.Services;

namespace DXSuite.Director.Filters;

/// <summary>
/// Stamps the tenant's configured poll cadence onto every PlayerContent response so the
/// server can re-pace the fleet within one poll. Applied as a result filter so the many
/// return sites in GetPlayerContent stay untouched.
/// </summary>
public class PlayerPollIntervalFilter : IAsyncResultFilter
{
    private readonly IPlayerSyncSettingsService _settings;

    public PlayerPollIntervalFilter(IPlayerSyncSettingsService settings)
    {
        _settings = settings;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { Value: PlayerContentResponse response })
        {
            var settings = await _settings.GetAsync();
            response.NextPollSeconds = settings.PollIntervalSeconds;
            response.FullInventoryMinutes = settings.FullInventoryMinutes;
        }

        await next();
    }
}
