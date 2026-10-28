using System.Globalization;
using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Connectors.Services.CoreApi.Models;
using DXSuite.Shared.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DXSuite.Aviation.Services.FlightDataEditor;

/// <summary>
/// Writes the Flight Data Editor's <c>fde.*</c> events (FDE-12, AUD-08) for the signed-in account. CoreAPI owns and
/// overwrites a Flight Edit, so each event holds the whole story itself: the flight key, old and new values, the kind
/// and, for a bulk action, every flight it touched (AUD-05).
/// </summary>
/// <remarks>
/// Call it once CoreAPI has answered, with that answer: a refusal or failure is logged with that outcome (AUD-08).
/// A failed event write never fails the action, which CoreAPI has already taken.
/// </remarks>
public sealed class FlightEditAudit(IServiceProvider services, ILogger<FlightEditAudit> logger)
{
    // Optional so a host without the User Event Log (no Director module) still runs the editor.
    private readonly IUserEventLog? log = services.GetService<IUserEventLog>();

    /// <summary>
    /// A Manage save: one <c>fde.flight-edited</c> per changed field and one <c>fde.roll-changed</c> when the roll
    /// categories changed. A save that changes neither still records the kind it saved, so every save leaves a row.
    /// </summary>
    /// <param name="opened">The values the form loaded, which the save compared against (FDE-06).</param>
    /// <param name="kindOpened">The kind the flight's edit had when the form loaded.</param>
    /// <param name="kindSaved">The kind the save made (FDE-04).</param>
    public async Task EditedAsync<T>(FlightKey flight, FlightFieldValues opened, FlightFieldValues chosen,
        RollCategories rollOpened, RollCategories rollChosen, FlightEditKind kindOpened, FlightEditKind kindSaved, CoreApiResult<T> result)
    {
        var kind = KindName(kindSaved);
        var changes = opened.ChangesTo(chosen);
        var events = new List<UserEvent>();
        void Field(string field, object? old, object? @new) =>
            events.Add(Flight(UserEventActionKeys.FdeFlightEdited, flight, result, new() { ["field"] = field, ["old"] = old, ["new"] = @new, ["kind"] = kind }));

        if (changes.Estimated is { } estimated) Field("estimated", opened.Estimated, estimated);
        if (changes.Actual is { } actual) Field("actual", opened.Actual, actual);
        if (changes.Status is { } status) Field("status", opened.Status, status);
        if (changes.Place is { } place) Field(flight.PlaceField, opened.Place, place);
        if (rollOpened != rollChosen)
            events.Add(Flight(UserEventActionKeys.FdeRollChanged, flight, result, new() { ["old"] = rollOpened, ["new"] = rollChosen, ["kind"] = kind }));
        // A flight with no edit yet has no old kind; each event above already carries the new one.
        var kindWas = KindName(kindOpened);
        if ((kindWas is not null && kindWas != kind) || events.Count == 0) Field("kind", kindWas, kind);

        foreach (var e in events) await WriteAsync(e);
    }

    /// <summary>
    /// Add Flight: one <c>fde.flight-added</c> with every field the form set. CoreAPI's answer carries the new record
    /// id, so a flight it did not add has none.
    /// </summary>
    public Task AddedAsync(FEDepartureFlight flight, RollCategories categories, FlightEditKind kind, CoreApiResult<int> result) =>
        AddedAsync(FlightKey.Of(flight) with { RecordId = result.Succeeded ? result.Value : null }, new()
        {
            ["scheduled"] = flight.ScheduledTime, ["estimated"] = flight.EstimatedTime, ["actual"] = flight.ActualTime,
            ["status"] = flight.RCP, ["gate"] = flight.GAT, ["destination"] = flight.DES, ["codeShares"] = CodeShares(flight.CodeShares),
        }, categories, kind, result);

    /// <inheritdoc cref="AddedAsync(FEDepartureFlight, RollCategories, FlightEditKind, CoreApiResult{int})"/>
    public Task AddedAsync(FEArrivalFlight flight, RollCategories categories, FlightEditKind kind, CoreApiResult<int> result) =>
        AddedAsync(FlightKey.Of(flight) with { RecordId = result.Succeeded ? result.Value : null }, new()
        {
            ["scheduled"] = flight.ScheduledTime, ["estimated"] = flight.EstimatedTime, ["actual"] = flight.ActualTime,
            ["status"] = flight.RCP, ["belt"] = flight.CAR, ["origin"] = flight.ORG, ["codeShares"] = CodeShares(flight.CodeShares),
        }, categories, kind, result);

    private Task AddedAsync<T>(FlightKey flight, Dictionary<string, object?> fields, RollCategories categories, FlightEditKind kind, CoreApiResult<T> result)
    {
        var set = fields.Where(f => f.Value is not (null or "" or string[] { Length: 0 })).ToDictionary();
        return WriteAsync(Flight(UserEventActionKeys.FdeFlightAdded, flight, result,
            new() { ["fields"] = set, ["categories"] = categories, ["kind"] = KindName(kind) }));
    }

    private static string[] CodeShares(IEnumerable<FECodeShare>? codeShares) =>
        codeShares?.Select(c => $"{c.FLC}{c.FLN}").ToArray() ?? [];

    /// <summary>One flight's Outage Edit kept as a Preserved Edit: <c>fde.edit-preserved</c>.</summary>
    public Task PreservedAsync<T>(FlightKey flight, CoreApiResult<T> result) =>
        WriteAsync(Flight(UserEventActionKeys.FdeEditPreserved, flight, result, []));

    /// <summary>One flight's edit cleared, so the feed's values show again: <c>fde.edit-cleared</c>.</summary>
    public Task ClearedAsync<T>(FlightKey flight, CoreApiResult<T> result) =>
        WriteAsync(Flight(UserEventActionKeys.FdeEditCleared, flight, result, []));

    /// <summary>
    /// The return review's Preserve All (FDE-08): <c>fde.review-preserve-all</c> with the window sent and every
    /// flight the action covers.
    /// </summary>
    /// <param name="flights">The flights the review listed when the user acted, as the user saw them.</param>
    public Task ReviewPreserveAllAsync<T>(FlightListWindow window, IEnumerable<FlightKey> flights, CoreApiResult<T> result) =>
        ReviewAsync(UserEventActionKeys.FdeReviewPreserveAll, window, flights, result);

    /// <summary>The return review's Use Feed (FDE-08): <c>fde.review-use-feed</c>, as for Preserve All.</summary>
    /// <inheritdoc cref="ReviewPreserveAllAsync"/>
    public Task ReviewUseFeedAsync<T>(FlightListWindow window, IEnumerable<FlightKey> flights, CoreApiResult<T> result) =>
        ReviewAsync(UserEventActionKeys.FdeReviewUseFeed, window, flights, result);

    private Task ReviewAsync<T>(string actionKey, FlightListWindow window, IEnumerable<FlightKey> flights, CoreApiResult<T> result)
    {
        var details = new Dictionary<string, object?>
        {
            ["windowStart"] = window.Start,
            ["windowEnd"] = window.End,
            ["flights"] = flights.Select(f => f.Details()).ToArray(),
        };
        CoreApiEventOutcome.AddError(details, result);
        var name = string.Create(CultureInfo.InvariantCulture, $"Flights {window.Start:yyyy-MM-dd HH:mm} to {window.End:yyyy-MM-dd HH:mm}");
        return WriteAsync(new UserEvent(actionKey, new UserEventTarget(UserEventTargetTypes.FlightList, null, name), CoreApiEventOutcome.Of(result), details));
    }

    /// <summary>
    /// A user turning CoreAPI's flight Outage mode switch where the instance allows it (FDE-03): an Account
    /// <c>fde.outage-switched</c>. The System one, when Director turns a disallowed switch off, is the watcher's.
    /// </summary>
    public Task OutageSwitchedAsync<T>(bool on, CoreApiResult<T> result)
    {
        var details = new Dictionary<string, object?> { ["on"] = on };
        CoreApiEventOutcome.AddError(details, result);
        return WriteAsync(new UserEvent(UserEventActionKeys.FdeOutageSwitched,
            new UserEventTarget(UserEventTargetTypes.FlightFeed, null, "Flight feed"), CoreApiEventOutcome.Of(result), details));
    }

    /// <summary>The kind as AUD-08 names it; null for a flight with no edit.</summary>
    public static string? KindName(FlightEditKind kind) => kind switch
    {
        FlightEditKind.PreservedEdit => "preserved",
        FlightEditKind.OutageEdit => "outage",
        _ => null,
    };

    private static UserEvent Flight<T>(string actionKey, FlightKey flight, CoreApiResult<T> result, Dictionary<string, object?> details)
    {
        var all = flight.Details();
        foreach (var (name, value) in details) all[name] = value;
        CoreApiEventOutcome.AddError(all, result);
        return new UserEvent(actionKey, flight.Target, CoreApiEventOutcome.Of(result), all);
    }

    private Task WriteAsync(UserEvent userEvent) =>
        log.TryWriteAsync(logger, userEvent.ActionKey, (l, ct) => l.WriteAsync(userEvent, ct));
}

/// <summary>
/// A flight as the User Event Log names it (AUD-08): direction and CoreAPI's record id, and the flight key a person
/// reads (carrier, number, scheduled date in airport time).
/// </summary>
public sealed record FlightKey(string Direction, int? RecordId, string? Carrier, string? Number, DateTime? Scheduled)
{
    public const string Departure = "departure";
    public const string Arrival = "arrival";

    public static FlightKey Of(FEDepartureFlight flight) => new(Departure, flight.RecordId, flight.FLC, flight.FLN, flight.ScheduledTime);

    public static FlightKey Of(FEArrivalFlight flight) => new(Arrival, flight.RecordId, flight.FLC, flight.FLN, flight.ScheduledTime);

    public string? ScheduledDate => Scheduled?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Gate on a departure, belt on an arrival: the field <see cref="FlightFieldValues.Place"/> holds.</summary>
    public string PlaceField => Direction == Departure ? "gate" : "belt";

    // Record ids are CoreAPI's per direction, so the direction is part of the target id.
    public UserEventTarget Target => new(UserEventTargetTypes.Flight, RecordId is { } id ? $"{Direction}:{id}" : null,
        $"{Carrier}{Number} {ScheduledDate}".Trim());

    public Dictionary<string, object?> Details() => new()
    {
        ["direction"] = Direction,
        ["recordId"] = RecordId,
        ["carrier"] = Carrier,
        ["number"] = Number,
        ["scheduledDate"] = ScheduledDate,
    };
}

public static class FlightEditAuditExtensions
{
    public static IServiceCollection AddFlightEditAudit(this IServiceCollection services)
    {
        services.TryAddScoped<FlightEditAudit>();
        return services;
    }
}
