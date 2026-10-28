using DXSuite.Connectors.Services.CoreApi.Models;

namespace DXSuite.Connectors.Services.CoreApi.Hub;

/// <summary>
/// Receives the CoreAPI hub events Director uses (IF-10). Implement only the ones you need; the rest do nothing.
/// Calls arrive on the hub connection's thread, so a Blazor page marshals them with <c>InvokeAsync</c>.
/// </summary>
public interface ICoreApiHubObserver
{
    /// <summary><c>FeedUpdate</c>: CoreAPI's feed status. Its server list and active server are ignored (CAPI-06).</summary>
    Task OnFeedStatusAsync(FeedStatus status) => Task.CompletedTask;

    /// <summary><c>ToggleOverride</c>: CoreAPI's flight Outage mode switch (FDE-03).</summary>
    Task OnOutageSwitchAsync(bool on) => Task.CompletedTask;

    /// <summary><c>FlightDepartureUpdate</c> and <c>FlightDepartureListUpdate</c>: departures CoreAPI changed.</summary>
    Task OnDeparturesAsync(IReadOnlyList<FEDepartureFlight> flights) => Task.CompletedTask;

    /// <summary><c>FlightArrivalUpdate</c> and <c>FlightArrivalListUpdate</c>: arrivals CoreAPI changed.</summary>
    Task OnArrivalsAsync(IReadOnlyList<FEArrivalFlight> flights) => Task.CompletedTask;

    /// <summary><c>UserSettingsUpdated</c>: the field settings stored against a key changed (IF-08).</summary>
    Task OnFieldSettingsAsync(string apiKeyId, UserSettings settings) => Task.CompletedTask;
}
