using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DXSuite.Connectors.Services.CoreApi.Models;
using DXSuite.Shared.Authorization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace DXSuite.Connectors.Services.CoreApi;

/// <summary>
/// Director's one client for CoreAPI (CAPI-01). Resolve it from the scope doing the work: a Blazor circuit,
/// a request, or a worker scope from <c>IWorkerScopeFactory</c>. The tenant comes from that scope's
/// <see cref="ICoreApiSettingsStore"/> and the user from its <see cref="ISessionState"/>; the client keeps
/// no state of its own between calls.
///
/// Every call sends <c>X-API-KEY</c> (the tenant's Director key) and <c>Username</c> (the signed-in user's
/// login, or <see cref="ServiceUsername"/> when the scope has no user) (CAPI-02). Servers are tried last good
/// first, then in their configured order, <see cref="PerServerTimeout"/> each; CoreAPI is unreachable only
/// when every server failed to answer (CAPI-06). Any HTTP answer, error statuses included, counts as the
/// server answering. Director never asks CoreAPI to change its primary.
/// </summary>
public sealed class CoreApiClient
{
    /// <summary>A fixed value, not a setting (1.1 section 3.10).</summary>
    public static readonly TimeSpan PerServerTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The <c>Username</c> sent for System work, such as the watcher and the startup switch-off.</summary>
    public const string ServiceUsername = "Director";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _http;
    private readonly ICoreApiSettingsStore _settings;
    private readonly ISessionState _session;
    private readonly TimeProvider _clock;
    private readonly ILogger<CoreApiClient> _logger;

    public CoreApiClient(HttpClient http, ICoreApiSettingsStore settings, ISessionState session, TimeProvider clock, ILogger<CoreApiClient> logger)
    {
        _http = http;
        _settings = settings;
        _session = session;
        _clock = clock;
        _logger = logger;
    }

    // ── Flight list (IF-01) ──────────────────────────────────────────────

    public async Task<CoreApiResult<List<FEDepartureFlight?>>> GetDeparturesAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        var result = await CallAsync<List<FEDepartureFlight?>>(HttpMethod.Get, "Override/Departures", Window(start, end), ct);
        return result.Succeeded ? result with { Value = result.Value!.OrderBy(f => f?.EstimatedTime).ToList() } : result;
    }

    public async Task<CoreApiResult<List<FEArrivalFlight?>>> GetArrivalsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        var result = await CallAsync<List<FEArrivalFlight?>>(HttpMethod.Get, "Override/Arrivals", Window(start, end), ct);
        return result.Succeeded ? result with { Value = result.Value!.OrderBy(f => f?.EstimatedTime).ToList() } : result;
    }

    // ── Flight Edit (IF-02) and Add Flight (IF-03) ───────────────────────

    public Task<CoreApiResult<OverrideResponse>> ModifyDepartureAsync(int recordId, OverrideDepartureFlight edit, CancellationToken ct = default) =>
        CallAsync<OverrideResponse>(HttpMethod.Patch, $"Override/Departures/Modify/{recordId}", null, ct, edit);

    public Task<CoreApiResult<OverrideResponse>> ModifyArrivalAsync(int recordId, OverrideArrivalFlight edit, CancellationToken ct = default) =>
        CallAsync<OverrideResponse>(HttpMethod.Patch, $"Override/Arrivals/Modify/{recordId}", null, ct, edit);

    /// <returns>The new flight's record id.</returns>
    public Task<CoreApiResult<int>> AddDepartureAsync(FEDepartureFlight flight, CancellationToken ct = default) =>
        CallAsync<int>(HttpMethod.Post, "Override/Departures/Add", null, ct, flight);

    /// <returns>The new flight's record id.</returns>
    public Task<CoreApiResult<int>> AddArrivalAsync(FEArrivalFlight flight, CancellationToken ct = default) =>
        CallAsync<int>(HttpMethod.Post, "Override/Arrivals/Add", null, ct, flight);

    // ── Review (IF-04, IF-05) ────────────────────────────────────────────

    public Task<CoreApiResult<OverrideResponse>> PreserveDepartureAsync(int recordId, CancellationToken ct = default) =>
        CallAsync<OverrideResponse>(HttpMethod.Put, $"Override/Departures/Preserve/{recordId}", null, ct);

    public Task<CoreApiResult<OverrideResponse>> ClearDepartureAsync(int recordId, CancellationToken ct = default) =>
        CallAsync<OverrideResponse>(HttpMethod.Put, $"Override/Departures/Clear/{recordId}", null, ct);

    public Task<CoreApiResult<OverrideResponse>> PreserveArrivalAsync(int recordId, CancellationToken ct = default) =>
        CallAsync<OverrideResponse>(HttpMethod.Put, $"Override/Arrivals/Preserve/{recordId}", null, ct);

    public Task<CoreApiResult<OverrideResponse>> ClearArrivalAsync(int recordId, CancellationToken ct = default) =>
        CallAsync<OverrideResponse>(HttpMethod.Put, $"Override/Arrivals/Clear/{recordId}", null, ct);

    public Task<CoreApiResult<string>> PreserveAllAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default) =>
        CallAsync<string>(HttpMethod.Put, "Override/PreserveAll", Window(start, end), ct);

    public Task<CoreApiResult<string>> ClearAllAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default) =>
        CallAsync<string>(HttpMethod.Put, "Override/ClearAll", Window(start, end), ct);

    // ── Feed status (IF-06) and the flight Outage mode switch (IF-07) ────

    public Task<CoreApiResult<FeedStatus>> GetFeedStatusAsync(CancellationToken ct = default) =>
        CallAsync<FeedStatus>(HttpMethod.Get, "Override/FeedStatus", null, ct);

    public Task<CoreApiResult<OverrideStatus>> SetOverrideToggleAsync(bool state, CancellationToken ct = default) =>
        CallAsync<OverrideStatus>(HttpMethod.Post, "Override/OverrideToggle",
            new Dictionary<string, string?> { ["state"] = state.ToString() }, ct);

    // ── Field settings (IF-08) ───────────────────────────────────────────

    public Task<CoreApiResult<UserSettings>> GetFieldSettingsAsync(string apiKeyId, CancellationToken ct = default) =>
        CallAsync<UserSettings>(HttpMethod.Get, $"Override/GetSettings/{Uri.EscapeDataString(apiKeyId)}", null, ct);

    public Task<CoreApiResult<string>> UpdateFieldSettingsAsync(string apiKeyId, UserSettings settings, CancellationToken ct = default) =>
        CallAsync<string>(HttpMethod.Patch, $"Override/UpdateSettings/{Uri.EscapeDataString(apiKeyId)}", null, ct, settings);

    // ── Lookups (IF-09) ──────────────────────────────────────────────────

    public Task<CoreApiResult<List<TblLocation?>>> GetLocationsAsync(CancellationToken ct = default) =>
        CallAsync<List<TblLocation?>>(HttpMethod.Get, "Override/Locations", null, ct);

    public Task<CoreApiResult<List<TblAirline?>>> GetAirlinesAsync(CancellationToken ct = default) =>
        CallAsync<List<TblAirline?>>(HttpMethod.Get, "Override/Airlines", null, ct);

    public Task<CoreApiResult<List<TblGate?>>> GetGatesAsync(CancellationToken ct = default) =>
        CallAsync<List<TblGate?>>(HttpMethod.Get, "Override/Gates", null, ct);

    public Task<CoreApiResult<List<TblCarousel?>>> GetCarouselsAsync(CancellationToken ct = default) =>
        CallAsync<List<TblCarousel?>>(HttpMethod.Get, "Override/Carousels", null, ct);

    public Task<CoreApiResult<List<TblFlightStatus?>>> GetStatusesAsync(CancellationToken ct = default) =>
        CallAsync<List<TblFlightStatus?>>(HttpMethod.Get, "Override/Statuses", null, ct);

    // ── Key page (IF-11) ─────────────────────────────────────────────────

    /// <summary>The id CoreAPI gives the Director key; field settings are stored against it.</summary>
    public Task<CoreApiResult<string>> GetApiKeyIdAsync(CancellationToken ct = default) =>
        CallAsync<string>(HttpMethod.Get, "ApiKey/ApiKeyID", null, ct);

    public Task<CoreApiResult<ApiKeyData>> GetApiKeyDataAsync(string apiKeyId, CancellationToken ct = default) =>
        CallAsync<ApiKeyData>(HttpMethod.Get, $"ApiKey/{Uri.EscapeDataString(apiKeyId)}", null, ct);

    public Task<CoreApiResult<List<ApiKeyData?>>> GetAllApiKeyDataAsync(CancellationToken ct = default) =>
        CallAsync<List<ApiKeyData?>>(HttpMethod.Get, "ApiKey/all", null, ct);

    public Task<CoreApiResult<List<HttpLogEntry>>> GetResponseLogsAsync(string apiKeyId, DateTime? start, DateTime? end, string? routePrefix, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>();
        if (start is not null) query["StartDate"] = start.Value.ToString(CultureInfo.CurrentCulture);
        if (end is not null) query["EndDate"] = end.Value.ToString(CultureInfo.CurrentCulture);
        if (routePrefix is not null) query["routePrefix"] = routePrefix;
        return CallAsync<List<HttpLogEntry>>(HttpMethod.Get, $"ApiKey/ResponseLogs/{Uri.EscapeDataString(apiKeyId)}", query, ct);
    }

    /// <summary>
    /// Checks a key and server list before they are saved, by asking for the key's id with them. Records no
    /// last good server, since nothing is saved yet.
    /// </summary>
    public Task<CoreApiResult<string>> CheckConnectionAsync(string directorKey, IReadOnlyList<string> servers, CancellationToken ct = default) =>
        CallAsync<string>(HttpMethod.Get, "ApiKey/ApiKeyID", null, ct, candidate: new CoreApiSettings(directorKey, servers, null));

    // ── Transport ────────────────────────────────────────────────────────

    // ISO 8601 with the airport's offset, to the second (C-02), so the window means the same instants whatever
    // the culture or time zone of the Director node or the CoreAPI server.
    private static Dictionary<string, string?> Window(DateTimeOffset start, DateTimeOffset end) => new()
    {
        ["StartDateTime"] = WindowTime(start),
        ["EndDateTime"] = WindowTime(end)
    };

    /// <summary>A window time as CoreAPI is sent it: ISO 8601 with offset, to the second (C-02).</summary>
    public static string WindowTime(DateTimeOffset time) => time.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private async Task<CoreApiResult<T>> CallAsync<T>(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string?>? query,
        CancellationToken ct,
        object? body = null,
        CoreApiSettings? candidate = null)
    {
        var settings = candidate ?? await _settings.ReadAsync(ct);
        if (settings is null || !settings.IsConfigured)
        {
            var reason = settings is { IsKeyReadable: false }
                ? CoreApiKeyProtector.UnreadableMessage
                : "CoreAPI is not configured for this tenant.";
            return new CoreApiResult<T>(CoreApiOutcome.NotConfigured, Error: reason);
        }

        var username = CurrentUsername();
        var tried = new List<string>();
        foreach (var server in settings.ServersInTryOrder())
        {
            tried.Add(server);
            using var timeout = new CancellationTokenSource(PerServerTimeout, _clock);
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            HttpStatusCode status;
            string content;
            try
            {
                using var request = BuildRequest(method, server, path, query, body, settings.DirectorKey!, username);
                using var response = await _http.SendAsync(request, attempt.Token);
                content = await response.Content.ReadAsStringAsync(attempt.Token);
                status = response.StatusCode;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("CoreAPI server {Server} did not answer {Method} {Path} within {Seconds} s",
                    server, method, path, PerServerTimeout.TotalSeconds);
                continue;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("CoreAPI server {Server} could not be reached for {Method} {Path}: {Error}",
                    server, method, path, ex.Message);
                continue;
            }

            if (candidate is null && !string.Equals(server, settings.LastGoodServer, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("CoreAPI server {Server} answered; it is now the last good server", server);
                await _settings.RecordLastGoodServerAsync(server, ct);
            }

            return Read<T>(server, status, content) with { ServersTried = tried };
        }

        _logger.LogWarning("CoreAPI is unreachable: none of {Count} servers answered {Method} {Path}",
            settings.Servers.Count, method, path);
        return new CoreApiResult<T>(CoreApiOutcome.Unreachable, Error: "CoreAPI is unreachable.") { ServersTried = tried };
    }

    private static HttpRequestMessage BuildRequest(
        HttpMethod method,
        string server,
        string path,
        IReadOnlyDictionary<string, string?>? query,
        object? body,
        string directorKey,
        string username)
    {
        var url = $"{server.TrimEnd('/')}/{path}";
        if (query is { Count: > 0 }) url = QueryHelpers.AddQueryString(url, query);

        var request = new HttpRequestMessage(method, url);
        // Per request, never on the HttpClient: handlers are pooled across tenants and users.
        request.Headers.TryAddWithoutValidation("X-API-KEY", directorKey);
        request.Headers.TryAddWithoutValidation("Username", username);
        request.Headers.Accept.ParseAdd("application/json");

        // CoreAPI's baseline reads a JSON body on every POST and PATCH, "null" included, and none on PUT.
        if (method == HttpMethod.Post || method == HttpMethod.Patch)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        return request;
    }

    private string CurrentUsername()
    {
        var login = _session.User?.UserPrincipalName;
        return string.IsNullOrWhiteSpace(login) ? ServiceUsername : login;
    }

    private static CoreApiResult<T> Read<T>(string server, HttpStatusCode status, string content)
    {
        if ((int)status is < 200 or > 299)
            return new CoreApiResult<T>(CoreApiOutcome.Failed, StatusCode: status, Error: Truncate(content), Server: server);

        try
        {
            var value = JsonSerializer.Deserialize<T>(content, JsonOptions);
            return value is null
                ? new CoreApiResult<T>(CoreApiOutcome.Failed, StatusCode: status, Error: "CoreAPI returned an empty answer.", Server: server)
                : new CoreApiResult<T>(CoreApiOutcome.Succeeded, value, status, Server: server);
        }
        catch (JsonException ex)
        {
            return new CoreApiResult<T>(CoreApiOutcome.Failed, StatusCode: status, Error: $"CoreAPI's answer could not be read: {ex.Message}", Server: server);
        }
    }

    private static string Truncate(string content) => content.Length <= 500 ? content : content[..500];
}
