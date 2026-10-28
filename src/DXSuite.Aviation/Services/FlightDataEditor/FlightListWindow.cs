using DXSuite.Connectors.Services.CoreApi.Models;

namespace DXSuite.Aviation.Services.FlightDataEditor;

/// <summary>
/// The Flight Data Editor's list window (FDE-10): from <see cref="LookBack"/> before now to <see cref="LookAhead"/>
/// after it, in the airport time zone. The window is Director's: it is sent to CoreAPI with each list request and
/// CoreAPI's own window (the hub's <c>WindowUpdate</c>) is never applied. Start is inclusive, end exclusive.
/// </summary>
public sealed record FlightListWindow(DateTimeOffset Start, DateTimeOffset End, TimeZoneInfo AirportTimeZone)
{
    /// <summary>A fixed value, not a setting (1.1 section 3.10); FSD 1.1 UC 5 asks for at least 6 hours.</summary>
    public static readonly TimeSpan LookBack = TimeSpan.FromHours(6);

    /// <summary>A fixed value, not a setting (1.1 section 3.10).</summary>
    public static readonly TimeSpan LookAhead = TimeSpan.FromHours(18);

    /// <summary>
    /// The window around <paramref name="now"/>. The ends are elapsed hours from now, each carrying the offset the
    /// airport had at that instant, so a window across a clock change is still 24 hours long.
    /// </summary>
    public static FlightListWindow At(DateTimeOffset now, TimeZoneInfo airportTimeZone)
    {
        var whole = new DateTimeOffset(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
        return new FlightListWindow(
            TimeZoneInfo.ConvertTime(whole - LookBack, airportTimeZone),
            TimeZoneInfo.ConvertTime(whole + LookAhead, airportTimeZone),
            airportTimeZone);
    }

    /// <summary>The window's start as an airport wall-clock time, as CoreAPI's flight times and the edit forms use.</summary>
    public DateTime LocalStart => Start.DateTime;

    /// <summary>The window's end as an airport wall-clock time.</summary>
    public DateTime LocalEnd => End.DateTime;

    public bool Contains(FEArrivalFlight flight) => Contains(BestKnownTime(flight));

    public bool Contains(FEDepartureFlight flight) => Contains(BestKnownTime(flight));

    /// <summary>
    /// When the flight lands as best known: actual, else estimated, else scheduled, each the Flight Edit's value
    /// first. The window keeps a flight by it and the list is ordered by it (FDE-10). Null when no time is known.
    /// </summary>
    public DateTimeOffset? BestKnownTime(FEArrivalFlight flight) =>
        BestKnownTime(flight.overrideFlightData?.ATA ?? flight.ActualTime, flight.overrideFlightData?.ETA ?? flight.EstimatedTime, flight.ScheduledTime);

    /// <summary>When the flight leaves as best known, as for an arrival.</summary>
    public DateTimeOffset? BestKnownTime(FEDepartureFlight flight) =>
        BestKnownTime(flight.overrideFlightData?.ATD ?? flight.ActualTime, flight.overrideFlightData?.ETD ?? flight.EstimatedTime, flight.ScheduledTime);

    private DateTimeOffset? BestKnownTime(DateTime? actual, DateTime? estimated, DateTime? scheduled) =>
        (actual ?? estimated ?? scheduled) is { } time ? InAirportTime(time) : null;

    private bool Contains(DateTimeOffset? instant) => instant >= Start && instant < End;

    /// <summary>
    /// A CoreAPI flight time as an instant. Times without an offset are the airport's wall clock; a time that
    /// arrived with an offset was already converted to an instant by the JSON reader.
    /// </summary>
    private DateTimeOffset InAirportTime(DateTime time) => time.Kind switch
    {
        DateTimeKind.Utc => new DateTimeOffset(time),
        DateTimeKind.Local => new DateTimeOffset(time.ToUniversalTime()),
        _ => new DateTimeOffset(time, AirportTimeZone.GetUtcOffset(time)),
    };
}
