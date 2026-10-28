using DXSuite.Aviation.Services.FlightDataEditor;
using DXSuite.Connectors.Services.CoreApi;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Services.CoreApi;

/// <summary>
/// Keeps CoreAPI's flight Outage mode switch off where the instance does not allow it (FDE-03): the watcher's lease
/// holder sends <c>OverrideToggle(false)</c> when it takes the lease and finds the switch on or unknown, and again
/// whenever the hub reports the switch on. A successful call records the switch as off, so later holds of the lease
/// do nothing until CoreAPI reports it on. Outage Edits made while it was on are left alone for the normal review.
/// </summary>
internal sealed class OutageSwitchOff(
    CoreApiClient coreApi,
    IUserEventLog log,
    IOptions<FlightDataEditorOptions> options,
    ILogger<OutageSwitchOff> logger) : ICoreApiWatcherStartTask, ICoreApiWatcherSwitchTask
{
    public const string Reason = "Outage mode switch turned off because the instance no longer allows it";

    // No state means CoreAPI is not configured. A switch not reported yet is sent off as well: CoreAPI reports it
    // only through its hub (C-03), so at startup Director cannot tell whether it is on.
    public Task RunAsync(CoreApiFeedState? state, ICoreApiWatcherHold hold, CancellationToken ct) =>
        state is null || state.SwitchOn == false ? Task.CompletedTask : TurnOffAsync(hold, ct);

    public Task OnSwitchReportedAsync(bool on, ICoreApiWatcherHold hold, CancellationToken ct) =>
        on ? TurnOffAsync(hold, ct) : Task.CompletedTask;

    private async Task TurnOffAsync(ICoreApiWatcherHold hold, CancellationToken ct)
    {
        if (options.Value.AllowOutageModeSwitch) return;

        var result = await coreApi.SetOverrideToggleAsync(false, ct);
        if (!result.Succeeded)
        {
            logger.LogWarning("CoreAPI's Outage mode switch could not be turned off ({Outcome}: {Error})", result.Outcome, result.Error);
            return;
        }

        await hold.RecordSwitchAsync(false, ct);
        await log.TryWriteAsync(logger, "Outage mode switch-off", (l, c) => l.WriteAsync(
            UserEventActor.System(CoreApiWatcherService.ServiceName),
            new UserEvent(UserEventActionKeys.FdeOutageSwitched,
                new UserEventTarget(UserEventTargetTypes.FlightFeed, null, "Flight feed"),
                Details: new { on = false, reason = Reason }), c), ct);
    }
}
