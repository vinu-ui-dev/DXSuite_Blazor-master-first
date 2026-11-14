using System.Net.Http.Json;
using DXSuite.Aviation.Pages.Cupps;
using DXSuite.Aviation.Services.CounterAllocation;
using DXSuite.Aviation.Services.FlightDataEditor;
using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Connectors.Services.CoreApi.Models;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Services.Identity;
using Microsoft.AspNetCore.Components;

namespace DXSuite.Aviation.Services.Cupps;

/// <summary>Live replacements for CUPPS's RMU airline/counter picker: airline accounts from Director's
/// CUPPS airlines endpoint and real counter allocations (from <see cref="CounterAllocationService"/>,
/// the only live counter-allocation data in the solution). The rest of the CUPPS prototype keeps using
/// <see cref="CuppsData"/>'s static demo data.</summary>
public sealed class CuppsLiveDataProvider(
    CoreApiClient coreApi,
    IFlightListClock flightListClock,
    CounterAllocationService counterAllocationService,
    IHttpClientFactory httpClientFactory,
    NavigationManager navigationManager,
    ISessionState sessionState,
    IInternalCallerTokenService tokenService)
{
    public async Task<IReadOnlyList<OperatingAirlineAccount>> GetOperatingAirlineAccountsAsync(CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient();
        var uri = new Uri(new Uri(navigationManager.BaseUri), "api/cupps/airlines");
        var airlines = await client.GetFromJsonAsync<List<CuppsAirlineDto>>(uri, ct) ?? [];

        return airlines
            .Where(a => !string.IsNullOrWhiteSpace(a.Code))
            .GroupBy(a => a.Code.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var a = g.First();
                var code = g.Key;
                var name = string.IsNullOrWhiteSpace(a.Name) ? code : a.Name.Trim();
                var bg = string.IsNullOrWhiteSpace(a.Bg) ? ColorFor(code) : a.Bg.Trim();
                var id = (a.AirlineId is > 0 ? a.AirlineId.Value : Math.Abs(code.GetHashCode(StringComparison.OrdinalIgnoreCase)) + 1);
                return new OperatingAirlineAccount(id, new Airline(code, name, bg));
            })
            .OrderBy(a => a.Airline.Name)
            .ToList();
    }

    public async Task<IReadOnlyList<Airline>> GetOperatingAirlinesAsync(CancellationToken ct = default) =>
        (await GetOperatingAirlineAccountsAsync(ct)).Select(a => a.Airline).ToList();

    public async Task<(IReadOnlyList<Template> Templates, string? PickerTab)> GetPageOptionsAsync(int airlineId, string airlineCode,
        CancellationToken ct = default)
    {
        if (airlineId <= 0) return ([], null);

        var client = httpClientFactory.CreateClient();
        var uri = new Uri(new Uri(navigationManager.BaseUri), $"api/counter-templates/page-options?airlineId={airlineId}");
        var response = await client.GetFromJsonAsync<CounterTemplatePageOptionsResponse>(uri, ct);
        if (response is null) return ([], null);

        Template Map(CounterTemplatePageOptionDto t)
        {
            var kind = string.Equals(t.Kind, "custom", StringComparison.OrdinalIgnoreCase) ? "Custom" : "Standard";
            var scope = string.Equals(t.Scope, "airport", StringComparison.OrdinalIgnoreCase) ? "airport" : airlineCode;
            var en = string.IsNullOrWhiteSpace(t.TextEn) ? t.Name : t.TextEn!;
            var fr = string.IsNullOrWhiteSpace(t.TextFr) ? en : t.TextFr!;
            return new Template(t.CounterTemplateId.ToString(), kind, scope, en, fr, t.EndsToday);
        }

        var templates = response.StandardTemplates.Concat(response.CustomTemplates).Select(Map).ToList();
        var pickerTab = response.Airline.PickerTab switch
        {
            1 => "Custom",
            0 => "Standard",
            _ => null
        };
        return (templates, pickerTab);
    }

    /// <summary>Persists a counter publish (CounterAgentScreens' Publish/SendDraft/TileOpen-open actions)
    /// to Director's real counter-publish schema, scoped server-side to the signed-in user's own airline
    /// if they're an Airline Supervisor. Returns false with no persistence if nobody is signed in, or if
    /// the server rejects the airline as outside the caller's scope.</summary>
    public async Task<(bool Success, string? Error)> PublishCounterAsync(
        string counterId, int airlineId, string? counterTemplateId, string? counterTemplateKind,
        string? counterTemplateName, string? destination, bool? sunflower, CancellationToken ct = default)
    {
        if (sessionState.UserName is not { } username) return (false, "Not signed in.");

        var client = AuthenticatedClient(username);
        var uri = new Uri(new Uri(navigationManager.BaseUri), "api/counter-publish/publish");
        var body = new
        {
            counterId,
            airlineId,
            counterTemplateId = int.TryParse(counterTemplateId, out var id) ? id : (int?)null,
            counterTemplateKind,
            counterTemplateName,
            destination,
            sunflower,
            pageShowSeconds = (int?)null,
        };

        var response = await client.PostAsJsonAsync(uri, body, ct);
        return response.IsSuccessStatusCode
            ? (true, null)
            : (false, await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>Persists a counter close (CounterAgentScreens' CloseCounter/TileOpen-close actions).</summary>
    public async Task<(bool Success, string? Error)> CloseCounterAsync(string counterId, CancellationToken ct = default)
    {
        if (sessionState.UserName is not { } username) return (false, "Not signed in.");

        var client = AuthenticatedClient(username);
        var uri = new Uri(new Uri(navigationManager.BaseUri), "api/counter-publish/close");
        var response = await client.PostAsJsonAsync(uri, new { counterId }, ct);
        return response.IsSuccessStatusCode
            ? (true, null)
            : (false, await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>An internal loopback HttpClient carrying a freshly-minted, short-lived token asserting
    /// the circuit's own already-authenticated username — see IInternalCallerTokenService. Only needed
    /// for writes; the plain GET calls above stay anonymous/best-effort scoped.</summary>
    private HttpClient AuthenticatedClient(string username)
    {
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Internal-Caller-Token", tokenService.IssueToken(username));
        return client;
    }

    public async Task<IReadOnlyList<Canned>> GetApprovedMessagesAsync(int airlineId, string languageCode = "en", CancellationToken ct = default)
    {
        if (airlineId <= 0) return [];

        // CUPPS message list uses one language only (English by default).
        var lang = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode;

        var client = httpClientFactory.CreateClient();
        var uri = new Uri(new Uri(navigationManager.BaseUri), $"api/cupps/messages/approved?airlineId={airlineId}&languageCode={Uri.EscapeDataString(lang)}");
        var response = await client.GetFromJsonAsync<CuppsApprovedMessagesResponse>(uri, ct);
        if (response is null) return [];

        return response.Messages
            .Select(m =>
            {
                var body = string.IsNullOrWhiteSpace(m.Body)
                    ? m.Texts.FirstOrDefault(t => t.LanguageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase))?.Body
                        ?? m.Name
                    : m.Body;
                return new Canned(m.MessageId.ToString(), m.Name, body);
            })
            .ToList();
    }

    public async Task<(IReadOnlyList<Alloc> Allocations, int NowMinutes)> GetLiveAllocationsAsync(CancellationToken ct = default)
    {
        var window = flightListClock.CurrentWindow();
        var zone = window.AirportTimeZone;
        var rows = await counterAllocationService.GetInRangeAsync(window.Start.UtcDateTime, window.End.UtcDateTime, ct);

        var nowLocal = TimeZoneInfo.ConvertTime(flightListClock.Time.GetUtcNow(), zone);
        var nowMinutes = nowLocal.Hour * 60 + nowLocal.Minute;

        var allocs = rows.Select(r => new Alloc(
            r.Counter,
            r.AirlineCode,
            MinutesSinceMidnight(r.StartUtc, zone),
            Math.Max(MinutesSinceMidnight(r.StartUtc, zone) + 1, MinutesSinceMidnight(r.EndUtc, zone)),
            r.AllocationType)).ToList();

        return (allocs, nowMinutes);
    }

    /// <summary>Today's departures in airport time, earliest first, shaped for the CUPPS flights table.</summary>
    public async Task<IReadOnlyList<Flight>> GetTodaysDeparturesAsync(CancellationToken ct = default)
    {
        var window = flightListClock.CurrentWindow();
        var zone = window.AirportTimeZone;
        var departuresTask = coreApi.GetDeparturesAsync(window.Start, window.End, ct);
        var airlinesTask = coreApi.GetAirlinesAsync(ct);
        await Task.WhenAll(departuresTask, airlinesTask);

        var departures = departuresTask.Result;
        if (!departures.Succeeded) return [];

        var nameByCode = airlinesTask.Result.Succeeded
            ? airlinesTask.Result.Value!
                .Where(a => a is { AirlineCode: not null })
                .GroupBy(a => a!.AirlineCode)
                .ToDictionary(g => g.Key, g => g.First()!.CarrierName)
            : new Dictionary<string, string?>();

        string NameOf(string? code, string? name) =>
            !string.IsNullOrWhiteSpace(name) ? name : (code is not null ? nameByCode.GetValueOrDefault(code) : null) ?? code ?? "";

        var today = TimeZoneInfo.ConvertTime(flightListClock.Time.GetUtcNow(), zone).Date;
        var flights = new List<Flight>();
        foreach (var f in departures.Value!)
        {
            if (f?.ScheduledTime is not { } scheduledRaw) continue;
            var sched = ToAirportLocal(scheduledRaw, zone);
            if (sched.Date != today) continue;

            var est = f.overrideFlightData?.ETD ?? f.EstimatedTime;
            var act = f.overrideFlightData?.ATD ?? f.ActualTime;
            var estLocal = est is { } e ? ToAirportLocal(e, zone) : (DateTime?)null;
            var actLocal = act is { } a ? ToAirportLocal(a, zone) : (DateTime?)null;
            var status = actLocal is not null ? "Departed" : estLocal > sched ? "Delayed" : "On Time";
            var best = actLocal ?? estLocal ?? sched;

            var carriers = new List<(string Airline, string No)> { (NameOf(f.FLC, f.Carrier), $"{f.FLC}{f.FLN}") };
            foreach (var cs in f.CodeShares)
                carriers.Add((NameOf(cs.FLC, null), $"{cs.FLC}{cs.FLN}"));

            flights.Add(new Flight(
                sched.Hour * 60 + sched.Minute,
                best.Hour * 60 + best.Minute,
                status,
                string.IsNullOrWhiteSpace(f.CTY) ? f.DES ?? "" : f.CTY,
                [.. carriers],
                f.GAT ?? ""));
        }

        return flights.OrderBy(x => x.Sched).ToList();
    }

    private static DateTime ToAirportLocal(DateTime time, TimeZoneInfo zone) => time.Kind switch
    {
        DateTimeKind.Utc => TimeZoneInfo.ConvertTimeFromUtc(time, zone),
        DateTimeKind.Local => TimeZoneInfo.ConvertTime(time, zone),
        _ => time,
    };

    private static int MinutesSinceMidnight(DateTime utc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        return Math.Clamp(local.Hour * 60 + local.Minute, 0, 1440);
    }

    private static string ColorFor(string code)
    {
        var hash = 0;
        foreach (var c in code) hash = hash * 31 + c;
        var hue = Math.Abs(hash) % 360;
        return $"hsl({hue},55%,32%)";
    }

    private sealed record CuppsAirlineDto(int? AirlineId, string Code, string Name, string? Bg);

    private sealed record CounterTemplatePageOptionsResponse(
        CounterTemplateAirlineDto Airline,
        IReadOnlyList<CounterTemplatePageOptionDto> StandardTemplates,
        IReadOnlyList<CounterTemplatePageOptionDto> CustomTemplates);

    private sealed record CounterTemplateAirlineDto(int AirlineId, byte? PickerTab);

    private sealed record CuppsApprovedMessagesResponse(int AirlineId, string LanguageCode, IReadOnlyList<CuppsApprovedMessageDto> Messages);

    private sealed record CuppsApprovedMessageDto(int MessageId, string Name, string? Body, IReadOnlyList<CuppsMessageTextDto> Texts);

    private sealed record CuppsMessageTextDto(string LanguageCode, string Body);

    private sealed record CounterTemplatePageOptionDto(
        int CounterTemplateId,
        string Name,
        int AirlineId,
        string Scope,
        string Kind,
        bool EndsToday,
        string? TextEn,
        string? TextFr);
}

public sealed record OperatingAirlineAccount(int AirlineId, Airline Airline);
