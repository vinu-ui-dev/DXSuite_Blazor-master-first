using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using DXSuite.Connectors.Services.CoreApi.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace DXSuite.Connectors.Services.CoreApi.Demo;

/// <summary>
/// An in-memory stand-in for CoreAPI, so the Flight Data Editor can be shown with sample flights where no CoreAPI
/// exists. Switched on by <c>CoreApi:DemoMode</c>; nothing here reaches the network.
/// </summary>
public static class DemoCoreApiExtensions
{
    public const string ServerUrl = "http://demo.coreapi.local";

    public static IHttpClientBuilder UseDemoCoreApi(this IHttpClientBuilder client)
    {
        client.Services.AddSingleton<DemoCoreApiData>();
        client.Services.AddScoped<ICoreApiSettingsStore, DemoCoreApiSettingsStore>();
        return client.ConfigurePrimaryHttpMessageHandler(sp => new DemoCoreApiHandler(sp.GetRequiredService<DemoCoreApiData>()));
    }
}

/// <summary>A saved connection that always points at the in-memory CoreAPI; saving changes nothing.</summary>
public sealed class DemoCoreApiSettingsStore : ICoreApiSettingsStore
{
    private static readonly CoreApiSettings Settings = new("demo-director-key", [DemoCoreApiExtensions.ServerUrl], DemoCoreApiExtensions.ServerUrl);

    public Task<CoreApiSettings?> ReadAsync(CancellationToken ct) => Task.FromResult<CoreApiSettings?>(Settings);
    public Task SaveAsync(string directorKey, IReadOnlyList<string> servers, string? hubUrl, CancellationToken ct) => Task.CompletedTask;
    public Task SaveHubUrlAsync(string? hubUrl, CancellationToken ct) => Task.CompletedTask;
    public Task RecordLastGoodServerAsync(string server, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>The sample data, shared by every circuit so an edit made in one shows in the next.</summary>
public sealed class DemoCoreApiData
{
    private readonly object _gate = new();
    private readonly List<FEDepartureFlight> _departures = new();
    private readonly List<FEArrivalFlight> _arrivals = new();
    private int _nextId = 1000;
    private bool _seeded;

    public bool OutageSwitchOn { get; set; }

    public UserSettings Settings { get; set; } = new()
    {
        GATModify = true, ETModify = true, ATModify = true, CARModify = true, RCPModify = true,
        ROPModify = true, ROBModify = true, ROSModify = true, ROTModify = true, CodeSharesModify = true,
    };

    public static readonly List<TblAirline?> Airlines =
    [
        Airline(1, "AC", "ACA", "Air Canada"), Airline(2, "LH", "DLH", "Lufthansa"), Airline(3, "BR", "EVA", "EVA Air"),
        Airline(4, "CX", "CPA", "Cathay Pacific"), Airline(5, "LO", "LOT", "LOT Polish Airlines"), Airline(6, "WS", "WJA", "WestJet"),
        Airline(7, "BA", "BAW", "British Airways"), Airline(8, "AF", "AFR", "Air France"), Airline(9, "DL", "DAL", "Delta Air Lines"),
        Airline(10, "UA", "UAL", "United Airlines"), Airline(11, "QK", "JZA", "Air Canada Jazz"), Airline(12, "TP", "TAP", "TAP Portugal"),
        Airline(13, "AV", "AVA", "Avianca Airlines"), Airline(14, "EY", "ETD", "Etihad Airways"), Airline(15, "G3", "GLO", "Gol Transportes Aereos"),
    ];

    public static readonly List<TblLocation?> Locations =
    [
        Location("LHR", "London (LHR)", "GBR"), Location("YOW", "Ottawa (ON)", "CAN"), Location("TPE", "Taipei (TPE)", "TWN"),
        Location("HKG", "Hong Kong (HKG)", "HKG"), Location("WAW", "Warsaw (WAW)", "POL"), Location("YUL", "Montreal (QC)", "CAN"),
        Location("CDG", "Paris (CDG)", "FRA"), Location("FRA", "Frankfurt (FRA)", "DEU"), Location("JFK", "New York (JFK)", "USA"),
        Location("ORD", "Chicago (ORD)", "USA"), Location("YVR", "Vancouver (BC)", "CAN"), Location("YYC", "Calgary (AB)", "CAN"),
        Location("BOG", "Bogota", "COL"), Location("LIS", "Lisbon", "PRT"), Location("AUH", "Abu Dhabi", "ARE"), Location("GRU", "Sao Paulo", "BRA"),
    ];

    public static readonly List<TblGate?> Gates = Enumerable.Range(0, 12)
        .Select(i => (TblGate?)new TblGate { Gat = $"{(i < 6 ? 'D' : 'E')}{30 + i * 3}", Ter = i < 6 ? "1" : "3", Orientation = "N", Defaultstyle = "default", SortOrder = i })
        .ToList();

    public static readonly List<TblCarousel?> Carousels = Enumerable.Range(1, 8)
        .Select(i => (TblCarousel?)new TblCarousel { CarouselId = i.ToString(), Terminal = i <= 4 ? 1 : 3, SortOrder = i, Output = true, BeltRelayIp = "" })
        .ToList();

    public static readonly List<TblFlightStatus?> Statuses =
    [
        Status("SCH", "Scheduled", false), Status("ONT", "On Time", false), Status("DLY", "Delayed", false),
        Status("DEP", "Departed", false), Status("LND", "Landed", true), Status("CNX", "Cancelled", false),
    ];

    public List<FEDepartureFlight> Departures(DateTimeOffset start, DateTimeOffset end)
    {
        lock (_gate)
        {
            Seed(start);
            return _departures.Where(f => InWindow(f.overrideFlightData?.ATD ?? f.ActualTime ?? f.overrideFlightData?.ETD ?? f.EstimatedTime ?? f.ScheduledTime, start, end)).ToList();
        }
    }

    public List<FEArrivalFlight> Arrivals(DateTimeOffset start, DateTimeOffset end)
    {
        lock (_gate)
        {
            Seed(start);
            return _arrivals.Where(f => InWindow(f.overrideFlightData?.ATA ?? f.ActualTime ?? f.overrideFlightData?.ETA ?? f.EstimatedTime ?? f.ScheduledTime, start, end)).ToList();
        }
    }

    public T WithLock<T>(Func<List<FEDepartureFlight>, List<FEArrivalFlight>, T> work)
    {
        lock (_gate) return work(_departures, _arrivals);
    }

    public int NextId() => Interlocked.Increment(ref _nextId);

    // Times are airport wall-clock, so the window's own wall-clock reading is the frame of reference.
    private static bool InWindow(DateTime? time, DateTimeOffset start, DateTimeOffset end) =>
        time is { } t && t >= start.DateTime && t < end.DateTime;

    private void Seed(DateTimeOffset windowStart)
    {
        if (_seeded) return;
        _seeded = true;

        // The window opens 6 hours before now, so "now" on the airport clock is 6 hours after its wall-clock start.
        var now = windowStart.DateTime.AddHours(6);
        var cities = Locations.ToDictionary(l => l!.IataCode, l => l!);

        foreach (var s in SampleDepartures)
        {
            var scheduled = Round(now.AddMinutes(s.Offset));
            var city = cities[s.City];
            var delay = s.Status == "Delayed" ? 25 : 0;
            var departed = s.Status == "Departed";
            var flight = new FEDepartureFlight
            {
                RecordId = NextId(), Type = "Departure", FLC = s.Flc, FLN = s.Fln, FLT = s.Flc + s.Fln,
                Carrier = CarrierName(s.Flc), DES = city.IataCode, CTY = $"{city.Name}, {city.Country}",
                GAT = s.Place, ScheduledTime = scheduled, EstimatedTime = scheduled.AddMinutes(delay),
                ActualTime = departed ? scheduled.AddMinutes(7) : null, RCP = s.Status,
                CodeShares = CodeShares(s.Shares),
            };
            if (s.Edit != null)
            {
                var edit = new OverrideDepartureFlight { Preserved = s.Edit == "preserved" };
                Roll(edit, s.Hidden);
                flight.overrideFlightData = edit;
                flight.preserveOverride = edit.Preserved;
            }
            _departures.Add(flight);
        }

        foreach (var s in SampleArrivals)
        {
            var scheduled = Round(now.AddMinutes(s.Offset));
            var city = cities[s.City];
            var delay = s.Status == "Delayed" ? 18 : 0;
            var landed = s.Status == "Landed";
            var flight = new FEArrivalFlight
            {
                RecordId = NextId(), Type = "Arrival", FLC = s.Flc, FLN = s.Fln, FLT = s.Flc + s.Fln,
                Carrier = CarrierName(s.Flc), ORG = city.IataCode, CTY = $"{city.Name}, {city.Country}",
                CAR = s.Place, ScheduledTime = scheduled, EstimatedTime = scheduled.AddMinutes(delay),
                ActualTime = landed ? scheduled.AddMinutes(-3) : null, RCP = s.Status,
                CodeShares = CodeShares(s.Shares),
            };
            if (s.Edit != null)
            {
                var edit = new OverrideArrivalFlight { Preserved = s.Edit == "preserved" };
                Roll(edit, s.Hidden);
                flight.overrideFlightData = edit;
                flight.preserveOverride = edit.Preserved;
            }
            _arrivals.Add(flight);
        }
    }

    /// <summary>One sample flight: minutes from now, airline and number, the other airport, gate or carousel, status.</summary>
    /// <param name="Edit">Null for none, "outage" for the triangle, "preserved" for the padlock.</param>
    /// <param name="Hidden">Display categories rolled off: P public, B baggage, S staff, T throw.</param>
    /// <param name="Shares">Code shares as space-separated airline+number, e.g. "LH6831 BR3142".</param>
    private sealed record Sample(int Offset, string Flc, string Fln, string City, string Place, string Status,
        string? Edit = null, string Hidden = "", string Shares = "");

    private static readonly Sample[] SampleDepartures =
    [
        new(-35, "AC", "856", "LHR", "E73", "Departed", Shares: "LH6831"),
        new(-5, "AC", "472", "YOW", "D32", "On Time", Edit: "outage", Shares: "BR3142 CX1606 LO4143 TP8050"),
        new(4, "QK", "7884", "YUL", "D5", "On Time", Edit: "preserved", Hidden: "PB"),
        new(10, "AV", "205", "BOG", "E3", "On Time", Edit: "outage", Hidden: "T", Shares: "EY2621 G36616"),
        new(25, "BR", "35", "TPE", "D36", "On Time"),
        new(40, "LH", "7", "FRA", "E39", "On Time", Shares: "AC7010"),
        new(55, "BA", "98", "LHR", "D33", "Delayed"),
        new(75, "AF", "345", "CDG", "E42", "On Time", Edit: "preserved", Hidden: "S"),
        new(95, "DL", "1234", "JFK", "D30", "On Time"),
        new(120, "UA", "5678", "ORD", "E45", "On Time"),
        new(150, "WS", "302", "YVR", "D36", "On Time", Edit: "outage"),
        new(180, "CX", "829", "HKG", "E48", "On Time"),
        new(210, "LO", "42", "WAW", "D39", "Delayed"),
        new(240, "TP", "253", "LIS", "E51", "On Time"),
    ];

    private static readonly Sample[] SampleArrivals =
    [
        new(-50, "AC", "874", "LHR", "3", "Landed", Shares: "LH6830"),
        new(-20, "LH", "6830", "FRA", "5", "Landed"),
        new(5, "AC", "471", "YOW", "2", "On Time", Edit: "outage", Shares: "BR3141 CX1605"),
        new(15, "BR", "36", "TPE", "4", "On Time", Edit: "preserved", Hidden: "B"),
        new(30, "AV", "206", "BOG", "1", "On Time", Edit: "outage", Hidden: "PT", Shares: "EY2620"),
        new(45, "QK", "7885", "YUL", "6", "Delayed"),
        new(65, "BA", "99", "LHR", "7", "On Time"),
        new(85, "AF", "346", "CDG", "8", "On Time", Shares: "DL8346"),
        new(110, "DL", "1235", "JFK", "2", "On Time"),
        new(140, "UA", "5679", "ORD", "3", "On Time", Edit: "preserved", Hidden: "PBST"),
        new(170, "WS", "303", "YVR", "4", "On Time"),
        new(200, "CX", "828", "HKG", "5", "Delayed"),
        new(230, "LO", "43", "WAW", "1", "On Time"),
        new(260, "TP", "252", "LIS", "6", "On Time"),
    ];

    private static List<FECodeShare> CodeShares(string shares) =>
        shares.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => new FECodeShare { FLC = s[..2], FLN = s[2..] })
            .ToList();

    private static void Roll(IRollFlags edit, string hidden)
    {
        if (hidden.Contains('P')) edit.ROP = RollCategories.Off;
        if (hidden.Contains('B')) edit.ROB = RollCategories.Off;
        if (hidden.Contains('S')) edit.ROS = RollCategories.Off;
        if (hidden.Contains('T')) edit.ROT = RollCategories.Off;
    }

    private static DateTime Round(DateTime time) => new(time.Ticks - time.Ticks % TimeSpan.FromMinutes(5).Ticks, DateTimeKind.Unspecified);

    private static string CarrierName(string code) => Airlines.First(a => a!.AirlineCode == code)!.CarrierName!;

    private static TblAirline Airline(int id, string iata, string icao, string name) =>
        new() { RecordId = id, AirlineCode = iata, Icao = icao, CarrierName = name, DisplayWithinSettingsMenu = true };

    private static TblLocation Location(string iata, string name, string country) =>
        new() { IataCode = iata, Name = name, Country = country, LanguageCodes = "en", DisplayWithinDsc = true };

    private static TblFlightStatus Status(string id, string name, bool landed) =>
        new() { StatusId = id, StatusName = name, IsLanded = landed };
}

/// <summary>Answers the calls <see cref="CoreApiClient"/> makes, from <see cref="DemoCoreApiData"/>.</summary>
internal sealed class DemoCoreApiHandler(DemoCoreApiData data) : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath.Trim('/');
        var query = QueryHelpers.ParseQuery(uri.Query);
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            return Route(request.Method, path, query, body);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException)
        {
            return Reply(HttpStatusCode.BadRequest, ex.Message);
        }
    }

    private HttpResponseMessage Route(HttpMethod method, string path, Dictionary<string, StringValues> query, string body)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (method == HttpMethod.Get)
        {
            switch (path)
            {
                case "Override/Departures": return Ok(data.Departures(Time(query, "StartDateTime"), Time(query, "EndDateTime")));
                case "Override/Arrivals": return Ok(data.Arrivals(Time(query, "StartDateTime"), Time(query, "EndDateTime")));
                case "Override/FeedStatus":
                    var now = DateTime.Now;
                    return Ok(new FeedStatus { Status = CoreApiFeedState.UpStatus, ArrivalsLast = now, ArrivalsStatus = CoreApiFeedState.UpStatus, DeparturesLast = now, DeparturesStatus = CoreApiFeedState.UpStatus });
                case "Override/Locations": return Ok(DemoCoreApiData.Locations);
                case "Override/Airlines": return Ok(DemoCoreApiData.Airlines);
                case "Override/Gates": return Ok(DemoCoreApiData.Gates);
                case "Override/Carousels": return Ok(DemoCoreApiData.Carousels);
                case "Override/Statuses": return Ok(DemoCoreApiData.Statuses);
                case "ApiKey/ApiKeyID": return Ok("demo-key-id");
                case "ApiKey/all": return Ok(new List<ApiKeyData?> { DemoKey() });
            }
            if (parts is ["Override", "GetSettings", _]) return Ok(data.Settings);
            if (parts is ["ApiKey", "ResponseLogs", _]) return Ok(new List<HttpLogEntry>());
            if (parts is ["ApiKey", _]) return Ok(DemoKey());
        }
        else if (method == HttpMethod.Post)
        {
            switch (path)
            {
                case "Override/Departures/Add":
                    return Ok(data.WithLock((deps, _) => Add(deps, Read<FEDepartureFlight>(body), data.NextId())));
                case "Override/Arrivals/Add":
                    return Ok(data.WithLock((_, arrs) => Add(arrs, Read<FEArrivalFlight>(body), data.NextId())));
                case "Override/OverrideToggle":
                    data.OutageSwitchOn = bool.Parse(query["state"].ToString());
                    return Ok(new OverrideStatus { feedOverride = data.OutageSwitchOn, arrivalsFeedStatus = CoreApiFeedState.UpStatus, departuresFeedStatus = CoreApiFeedState.UpStatus });
            }
        }
        else if (method == HttpMethod.Patch)
        {
            if (parts is ["Override", "Departures", "Modify", var depId])
                return Ok(data.WithLock((deps, _) => Modify(deps, int.Parse(depId), f => f.overrideFlightData = Read<OverrideDepartureFlight>(body))));
            if (parts is ["Override", "Arrivals", "Modify", var arrId])
                return Ok(data.WithLock((_, arrs) => Modify(arrs, int.Parse(arrId), f => f.overrideFlightData = Read<OverrideArrivalFlight>(body))));
            if (parts is ["Override", "UpdateSettings", _])
            {
                data.Settings = Read<UserSettings>(body);
                return Ok("Settings updated");
            }
        }
        else if (method == HttpMethod.Put)
        {
            if (parts is ["Override", "Departures", var depAction, var depRecord])
                return Ok(data.WithLock((deps, _) => Modify(deps, int.Parse(depRecord), f => Review(f, depAction))));
            if (parts is ["Override", "Arrivals", var arrAction, var arrRecord])
                return Ok(data.WithLock((_, arrs) => Modify(arrs, int.Parse(arrRecord), f => Review(f, arrAction))));
            if (path is "Override/PreserveAll" or "Override/ClearAll")
            {
                var action = path == "Override/PreserveAll" ? "Preserve" : "Clear";
                data.WithLock((deps, arrs) =>
                {
                    deps.ForEach(f => Review(f, action));
                    arrs.ForEach(f => Review(f, action));
                    return 0;
                });
                return Ok(action == "Preserve" ? "All overrides preserved" : "All overrides cleared");
            }
        }

        return Reply(HttpStatusCode.NotFound, $"The demo CoreAPI has no {method} {path}.");
    }

    private static int Add(List<FEDepartureFlight> flights, FEDepartureFlight flight, int id)
    {
        flight.RecordId = id;
        flights.Add(flight);
        return id;
    }

    private static int Add(List<FEArrivalFlight> flights, FEArrivalFlight flight, int id)
    {
        flight.RecordId = id;
        flights.Add(flight);
        return id;
    }

    private static OverrideResponse Modify<T>(List<T> flights, int recordId, Action<T> change) where T : class
    {
        var flight = flights.FirstOrDefault(f => RecordIdOf(f) == recordId) ?? throw new KeyNotFoundException($"No flight {recordId}.");
        change(flight);
        return new OverrideResponse { recordID = recordId, overrideResponse = "Override applied", preserveOverride = PreservedOf(flight) };
    }

    private static void Review(object flight, string action)
    {
        switch (flight)
        {
            case FEDepartureFlight d:
                if (action == "Clear") d.overrideFlightData = null;
                d.preserveOverride = action == "Preserve";
                break;
            case FEArrivalFlight a:
                if (action == "Clear") a.overrideFlightData = null;
                a.preserveOverride = action == "Preserve";
                break;
        }
    }

    private static int RecordIdOf(object flight) => flight switch { FEDepartureFlight d => d.RecordId, FEArrivalFlight a => a.RecordId, _ => -1 };

    private static bool PreservedOf(object flight) => flight switch { FEDepartureFlight d => d.preserveOverride == true, FEArrivalFlight a => a.preserveOverride == true, _ => false };

    private static ApiKeyData DemoKey() => new() { RecordID = 1, Name = "Director (demo)", ApiKeyID = "demo-key-id", Scope = "Override", CreateDate = DateTime.Today };

    private static DateTimeOffset Time(Dictionary<string, StringValues> query, string name) =>
        DateTimeOffset.Parse(query[name].ToString(), CultureInfo.InvariantCulture);

    private static T Read<T>(string body) => JsonSerializer.Deserialize<T>(body, Json) ?? throw new JsonException("Empty body.");

    private static HttpResponseMessage Ok<T>(T value) => Reply(HttpStatusCode.OK, JsonSerializer.Serialize(value), "application/json");

    private static HttpResponseMessage Reply(HttpStatusCode status, string content, string mediaType = "text/plain") =>
        new(status) { Content = new StringContent(content, Encoding.UTF8, mediaType) };
}
