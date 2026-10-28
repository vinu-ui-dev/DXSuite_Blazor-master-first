using Microsoft.Extensions.Configuration;

namespace DXSuite.Aviation.Services.FlightDataEditor;

/// <summary>Gives the Flight Data Editor its list window, now, in the airport time zone (FDE-10).</summary>
public interface IFlightListClock
{
    FlightListWindow CurrentWindow();

    /// <summary>The clock the window is taken from, so whatever moves the window runs on the same time.</summary>
    TimeProvider Time { get; }

    /// <summary>Why the airport time zone setting could not be used, or null when it was.</summary>
    string? ConfigurationProblem { get; }
}

/// <summary>
/// Reads the airport time zone from the per-instance setting <see cref="AirportTimeZoneKey"/> (an IANA id). The
/// spec places the window in airport time but names no source for the zone; Messaging's default zone is not used
/// so the editor does not depend on the Messaging flag. Every node behind a load balancer carries the same value.
/// </summary>
public sealed class FlightListClock : IFlightListClock
{
    public const string AirportTimeZoneKey = "Aviation:FlightDataEditor:AirportTimeZone";

    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _zone;

    public FlightListClock(IConfiguration config, TimeProvider time)
    {
        _time = time;
        var id = config[AirportTimeZoneKey];
        if (string.IsNullOrWhiteSpace(id))
        {
            _zone = TimeZoneInfo.Local;
            ConfigurationProblem = $"{AirportTimeZoneKey} is not set, so the flight list uses this server's time zone ({_zone.Id}).";
            return;
        }

        try
        {
            _zone = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _zone = TimeZoneInfo.Local;
            ConfigurationProblem = $"{AirportTimeZoneKey} \"{id}\" is not a known time zone, so the flight list uses this server's time zone ({_zone.Id}).";
        }
    }

    public string? ConfigurationProblem { get; }

    public TimeProvider Time => _time;

    public FlightListWindow CurrentWindow() => FlightListWindow.At(_time.GetUtcNow(), _zone);
}
