using DXSuite.Connectors.Services.CoreApi.Models;

namespace DXSuite.Host.Services.MockCoreApi;

/// <summary>
/// Mirrors just enough of CoreAPI's REST surface (CAPI-01) for <see cref="DXSuite.Connectors.Services.CoreApi.CoreApiClient"/>
/// to talk to, backed by <see cref="MockCoreApiStore"/>'s in-memory flights. Mapped only in local SQLite mode —
/// see the registration in Program.cs — so Flight Management and Counter Allocation have something to show
/// without a real airport RMS. Not a protocol-complete CoreAPI implementation: it covers the calls Director's
/// client actually makes and nothing it doesn't.
/// </summary>
public static class MockCoreApiEndpoints
{
    public const string BasePath = "/mock-coreapi";

    public static void MapMockCoreApiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(BasePath);

        group.MapGet("/Override/Departures", (string? StartDateTime, string? EndDateTime, MockCoreApiStore store) =>
            Results.Ok(store.Departures(ParseDate(StartDateTime), ParseDate(EndDateTime))));

        group.MapGet("/Override/Arrivals", (string? StartDateTime, string? EndDateTime, MockCoreApiStore store) =>
            Results.Ok(store.Arrivals(ParseDate(StartDateTime), ParseDate(EndDateTime))));

        group.MapPatch("/Override/Departures/Modify/{id:int}", (int id, OverrideDepartureFlight edit, MockCoreApiStore store) =>
        {
            var flight = store.FindDeparture(id);
            if (flight is null) return Results.NotFound();
            ApplyEdit(flight, edit);
            return Results.Ok(new OverrideResponse { recordID = id, overrideResponse = "OK", preserveOverride = edit.Preserved ?? false });
        });

        group.MapPatch("/Override/Arrivals/Modify/{id:int}", (int id, OverrideArrivalFlight edit, MockCoreApiStore store) =>
        {
            var flight = store.FindArrival(id);
            if (flight is null) return Results.NotFound();
            ApplyEdit(flight, edit);
            return Results.Ok(new OverrideResponse { recordID = id, overrideResponse = "OK", preserveOverride = edit.Preserved ?? false });
        });

        group.MapPost("/Override/Departures/Add", (FEDepartureFlight flight, MockCoreApiStore store) =>
            Results.Ok(store.AddDeparture(flight)));

        group.MapPost("/Override/Arrivals/Add", (FEArrivalFlight flight, MockCoreApiStore store) =>
            Results.Ok(store.AddArrival(flight)));

        group.MapPut("/Override/Departures/Preserve/{id:int}", (int id, MockCoreApiStore store) =>
            Results.Ok(new OverrideResponse { recordID = id, overrideResponse = "OK", preserveOverride = true }));

        group.MapPut("/Override/Departures/Clear/{id:int}", (int id, MockCoreApiStore store) =>
            Results.Ok(new OverrideResponse { recordID = id, overrideResponse = "OK", preserveOverride = false }));

        group.MapPut("/Override/Arrivals/Preserve/{id:int}", (int id, MockCoreApiStore store) =>
            Results.Ok(new OverrideResponse { recordID = id, overrideResponse = "OK", preserveOverride = true }));

        group.MapPut("/Override/Arrivals/Clear/{id:int}", (int id, MockCoreApiStore store) =>
            Results.Ok(new OverrideResponse { recordID = id, overrideResponse = "OK", preserveOverride = false }));

        group.MapPut("/Override/PreserveAll", () => Results.Ok("OK"));
        group.MapPut("/Override/ClearAll", () => Results.Ok("OK"));

        group.MapGet("/Override/FeedStatus", () =>
        {
            var now = DateTime.UtcNow;
            return Results.Ok(new FeedStatus { Status = "Connected", ArrivalsLast = now, ArrivalsStatus = "OK", DeparturesLast = now, DeparturesStatus = "OK" });
        });

        group.MapPost("/Override/OverrideToggle", (bool state) =>
            Results.Ok(new OverrideStatus { feedOverride = state, arrivalsFeedStatus = "OK", departuresFeedStatus = "OK" }));

        group.MapGet("/Override/GetSettings/{id}", (string id) => Results.Ok(new UserSettings
        {
            GATModify = true, ETModify = true, ATModify = true, CARModify = true, RCPModify = true,
            ROPModify = true, ROBModify = true, ROSModify = true, ROTModify = true,
            CodeSharesModify = true, PreserveAllOverridesOption = true,
        }));

        group.MapPatch("/Override/UpdateSettings/{id}", (string id) => Results.Ok("OK"));

        group.MapGet("/Override/Locations", () => Results.Ok(new List<TblLocation>
        {
            new() { IataCode = "LHR", Name = "London Heathrow", Country = "United Kingdom", LanguageCodes = "en" },
            new() { IataCode = "YOW", Name = "Ottawa Macdonald-Cartier", Country = "Canada", LanguageCodes = "en,fr" },
            new() { IataCode = "FRA", Name = "Frankfurt", Country = "Germany", LanguageCodes = "de,en" },
            new() { IataCode = "ORD", Name = "Chicago O'Hare", Country = "United States", LanguageCodes = "en" },
        }));

        group.MapGet("/Override/Airlines", () => Results.Ok(new List<TblAirline>
        {
            new() { RecordId = 1, AirlineCode = "AC", CarrierName = "Air Canada" },
            new() { RecordId = 2, AirlineCode = "LH", CarrierName = "Lufthansa" },
            new() { RecordId = 3, AirlineCode = "BA", CarrierName = "British Airways" },
            new() { RecordId = 4, AirlineCode = "UA", CarrierName = "United Airlines" },
            new() { RecordId = 5, AirlineCode = "WS", CarrierName = "WestJet" },
            new() { RecordId = 6, AirlineCode = "AA", CarrierName = "American Airlines" },
        }));

        group.MapGet("/Override/Gates", () => Results.Ok(new List<TblGate>
        {
            new() { Gat = "E73", Ter = "1", Orientation = "Standard", Defaultstyle = "Standard" },
            new() { Gat = "D32", Ter = "1", Orientation = "Standard", Defaultstyle = "Standard" },
            new() { Gat = "D12", Ter = "1", Orientation = "Standard", Defaultstyle = "Standard" },
            new() { Gat = "C14", Ter = "2", Orientation = "Standard", Defaultstyle = "Standard" },
        }));

        group.MapGet("/Override/Carousels", () => Results.Ok(new List<TblCarousel>
        {
            new() { CarouselId = "1", Terminal = 1, BeltRelayIp = "" },
            new() { CarouselId = "2", Terminal = 1, BeltRelayIp = "" },
        }));

        group.MapGet("/Override/Statuses", () => Results.Ok(new List<TblFlightStatus>
        {
            new() { StatusId = "ONT", StatusName = "On Time" },
            new() { StatusId = "DEL", StatusName = "Delayed" },
            new() { StatusId = "BRD", StatusName = "Boarding" },
            new() { StatusId = "DEP", StatusName = "Departed", IsLanded = false },
            new() { StatusId = "ARR", StatusName = "Arrived", IsLanded = true },
        }));

        group.MapGet("/ApiKey/ApiKeyID", () => Results.Ok("mock-api-key-id"));

        group.MapGet("/ApiKey/all", () => Results.Ok(new List<ApiKeyData>
        {
            new() { RecordID = 1, Name = "Local mock key", ApiKeyID = "mock-api-key-id", CreateDate = DateTime.UtcNow },
        }));

        group.MapGet("/ApiKey/{id}", (string id) => Results.Ok(new ApiKeyData
        {
            RecordID = 1, Name = "Local mock key", ApiKeyID = id, CreateDate = DateTime.UtcNow,
        }));

        group.MapGet("/ApiKey/ResponseLogs/{id}", (string id) => Results.Ok(new List<HttpLogEntry>()));
    }

    private static DateTime? ParseDate(string? text) =>
        DateTimeOffset.TryParse(text, out var dto) ? dto.UtcDateTime : null;

    private static void ApplyEdit(FEDepartureFlight flight, OverrideDepartureFlight edit)
    {
        if (edit.GAT is not null) flight.GAT = edit.GAT;
        if (edit.ETD is not null) flight.EstimatedTime = edit.ETD;
        if (edit.ATD is not null) flight.ActualTime = edit.ATD;
        if (edit.RCP is not null) flight.RCP = edit.RCP;
        if (edit.ROP is not null) flight.ROP = edit.ROP;
        if (edit.ROB is not null) flight.ROB = edit.ROB;
        if (edit.ROS is not null) flight.ROS = edit.ROS;
        if (edit.ROT is not null) flight.ROT = edit.ROT;
    }

    private static void ApplyEdit(FEArrivalFlight flight, OverrideArrivalFlight edit)
    {
        if (edit.GAT is not null) flight.CAR = edit.GAT;
        if (edit.ETA is not null) flight.EstimatedTime = edit.ETA;
        if (edit.ATA is not null) flight.ActualTime = edit.ATA;
        if (edit.RCP is not null) flight.RCP = edit.RCP;
        if (edit.ROP is not null) flight.ROP = edit.ROP;
        if (edit.ROB is not null) flight.ROB = edit.ROB;
        if (edit.ROS is not null) flight.ROS = edit.ROS;
        if (edit.ROT is not null) flight.ROT = edit.ROT;
    }
}
