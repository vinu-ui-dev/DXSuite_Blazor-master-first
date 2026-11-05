using DXSuite.Aviation.Pages.Cupps;
using DXSuite.Aviation.Services.CounterAllocation;
using DXSuite.Aviation.Services.FlightDataEditor;
using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Connectors.Services.CoreApi.Models;

namespace DXSuite.Aviation.Services.Cupps;

/// <summary>Live replacements for CUPPS's RMU airline/counter picker: airlines with flights right now
/// (from CoreAPI, the same source Flight Management uses) and real counter allocations (from
/// <see cref="CounterAllocationService"/>, the only live counter-allocation data in the solution).
/// The rest of the CUPPS prototype keeps using <see cref="CuppsData"/>'s static demo data.</summary>
public sealed class CuppsLiveDataProvider(
    CoreApiClient coreApi,
    IFlightListClock flightListClock,
    CounterAllocationService counterAllocationService)
{
    public async Task<IReadOnlyList<Airline>> GetOperatingAirlinesAsync(CancellationToken ct = default)
    {
        var window = flightListClock.CurrentWindow();
        var arrivalsTask = coreApi.GetArrivalsAsync(window.Start, window.End, ct);
        var departuresTask = coreApi.GetDeparturesAsync(window.Start, window.End, ct);
        var airlinesTask = coreApi.GetAirlinesAsync(ct);
        await Task.WhenAll(arrivalsTask, departuresTask, airlinesTask);

        var arrivals = arrivalsTask.Result;
        var departures = departuresTask.Result;
        if (!arrivals.Succeeded && !departures.Succeeded) return [];

        var nameByCode = airlinesTask.Result.Succeeded
            ? airlinesTask.Result.Value!
                .Where(a => a is { AirlineCode: not null })
                .GroupBy(a => a!.AirlineCode)
                .ToDictionary(g => g.Key, g => g.First()!.CarrierName)
            : new Dictionary<string, string?>();

        var result = new Dictionary<string, Airline>();

        void Add(string? code, string? name)
        {
            if (string.IsNullOrWhiteSpace(code) || result.ContainsKey(code)) return;
            var resolvedName = string.IsNullOrWhiteSpace(name) ? nameByCode.GetValueOrDefault(code) : name;
            result[code] = new Airline(code, string.IsNullOrWhiteSpace(resolvedName) ? code : resolvedName, ColorFor(code));
        }

        if (arrivals.Succeeded)
            foreach (var f in arrivals.Value!)
            {
                if (f is null) continue;
                Add(f.FLC, f.Carrier);
                foreach (var cs in f.CodeShares) Add(cs.FLC, null);
            }

        if (departures.Succeeded)
            foreach (var f in departures.Value!)
            {
                if (f is null) continue;
                Add(f.FLC, f.Carrier);
                foreach (var cs in f.CodeShares) Add(cs.FLC, null);
            }

        return result.Values.OrderBy(a => a.Name).ToList();
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
}
