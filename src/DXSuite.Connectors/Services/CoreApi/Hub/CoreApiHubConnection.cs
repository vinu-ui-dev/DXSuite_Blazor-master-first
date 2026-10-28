using System.Text.Json;
using System.Text.Json.Serialization;
using DXSuite.Connectors.Services.CoreApi.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace DXSuite.Connectors.Services.CoreApi.Hub;

/// <summary>
/// One connection to CoreAPI's SignalR hub (IF-10) at the tenant's configured address, sending the tenant's
/// Director key and the service name (C-08, CAPI-02). Once started it keeps itself connected, retrying after
/// a drop or a failed start, until disposed. The only place Director opens a hub connection: the leased watcher
/// holds one per tenant and each instance's listener one per tenant (CAPI-05, A-35).
/// </summary>
/// <remarks>
/// <c>WindowUpdate</c> (the window is Director's, FDE-10) and <c>ActiveServerUpdate</c> (CoreAPI chooses its
/// primary, CAPI-06) have no handler, so the client drops them. Events of a further CoreAPI feed belong here as
/// further handlers and methods on <see cref="ICoreApiHubObserver"/>, so every hub event still arrives through this
/// one connection.
/// </remarks>
public sealed class CoreApiHubConnection : IAsyncDisposable
{
    private static readonly JsonSerializerOptions ListJson = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HubConnection _hub;
    private readonly ICoreApiHubObserver _observer;
    private readonly ILogger _logger;
    private readonly TimeSpan _retryDelay;
    private readonly CancellationTokenSource _stop = new();
    private Task? _keepConnected;

    public CoreApiHubConnection(string hubUrl, string directorKey, ICoreApiHubObserver observer, ILogger logger, TimeSpan retryDelay)
    {
        HubUrl = hubUrl;
        DirectorKey = directorKey;
        _observer = observer;
        _logger = logger;
        _retryDelay = retryDelay;

        _hub = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.Headers["X-API-KEY"] = directorKey;
                options.Headers["Username"] = CoreApiClient.ServiceUsername;
            })
            .Build();

        // CoreAPI sends its active server and server list with every feed update; both are ignored (CAPI-06).
        _hub.On<FeedStatus, object?, object?>("FeedUpdate", (status, _, _) =>
            Deliver("FeedUpdate", () => _observer.OnFeedStatusAsync(status)));
        _hub.On<bool>("ToggleOverride", on =>
            Deliver("ToggleOverride", () => _observer.OnOutageSwitchAsync(on)));
        _hub.On<FEDepartureFlight>("FlightDepartureUpdate", flight =>
            Departures("FlightDepartureUpdate", [flight]));
        _hub.On<string>("FlightDepartureListUpdate", json =>
            Departures("FlightDepartureListUpdate", ReadList<FEDepartureFlight>(json)));
        _hub.On<FEArrivalFlight>("FlightArrivalUpdate", flight =>
            Arrivals("FlightArrivalUpdate", [flight]));
        _hub.On<string>("FlightArrivalListUpdate", json =>
            Arrivals("FlightArrivalListUpdate", ReadList<FEArrivalFlight>(json)));
        _hub.On<string, UserSettings>("UserSettingsUpdated", (apiKeyId, settings) =>
            Deliver("UserSettingsUpdated", () => _observer.OnFieldSettingsAsync(apiKeyId, settings)));
    }

    public string HubUrl { get; }

    public string DirectorKey { get; }

    /// <summary>Whether this connection already uses the hub address and key these settings name.</summary>
    public bool Uses(CoreApiSettings settings) =>
        string.Equals(settings.HubUrl, HubUrl, StringComparison.OrdinalIgnoreCase) && settings.DirectorKey == DirectorKey;

    /// <summary>Starts connecting in the background; a hub that is down is retried until this is disposed.</summary>
    public void Start() => _keepConnected ??= KeepConnectedAsync(_stop.Token);

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_keepConnected is not null) await _keepConnected;
        await _hub.DisposeAsync();
        _stop.Dispose();
    }

    private async Task KeepConnectedAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_hub.State == HubConnectionState.Disconnected)
            {
                try
                {
                    await _hub.StartAsync(ct);
                    _logger.LogInformation("Connected to the CoreAPI hub at {HubUrl}", HubUrl);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Could not connect to the CoreAPI hub at {HubUrl}: {Error}", HubUrl, ex.Message);
                }
            }

            try { await Task.Delay(_retryDelay, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    // The type field tells departures and arrivals apart on CoreAPI's side; a mismatched record is not shown.
    private Task Departures(string hubEvent, IEnumerable<FEDepartureFlight> flights)
    {
        var departures = flights.Where(f => f.Type == "D").ToList();
        return departures.Count == 0 ? Task.CompletedTask : Deliver(hubEvent, () => _observer.OnDeparturesAsync(departures));
    }

    private Task Arrivals(string hubEvent, IEnumerable<FEArrivalFlight> flights)
    {
        var arrivals = flights.Where(f => f.Type == "A").ToList();
        return arrivals.Count == 0 ? Task.CompletedTask : Deliver(hubEvent, () => _observer.OnArrivalsAsync(arrivals));
    }

    // List events carry the flights as a JSON string rather than as a payload object.
    private List<T> ReadList<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, ListJson) ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("A CoreAPI hub list update could not be read: {Error}", ex.Message);
            return [];
        }
    }

    private async Task Deliver(string hubEvent, Func<Task> handler)
    {
        try
        {
            await handler();
        }
        catch (OperationCanceledException)
        {
            // The watcher or page that owns this connection is stopping.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handling the CoreAPI hub event {HubEvent} failed", hubEvent);
        }
    }
}
