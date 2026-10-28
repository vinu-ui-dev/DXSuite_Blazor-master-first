using DXSuite.Connectors.Services.CoreApi.Models;

namespace DXSuite.Host.Services.MockCoreApi;

/// <summary>
/// In-memory stand-in for a real CoreAPI/RMS server, used only in local SQLite mode (<see cref="DXSuite.Shared.Services.LocalSqliteProvider"/>)
/// so Flight Management and Counter Allocation have something to show without a real airport feed. Lives for the
/// process's lifetime only; nothing here is a substitute for the real CoreAPI integration.
/// </summary>
public sealed class MockCoreApiStore
{
    private readonly object _lock = new();
    private readonly List<FEDepartureFlight> _departures = [];
    private readonly List<FEArrivalFlight> _arrivals = [];
    private int _nextId = 1;

    public MockCoreApiStore()
    {
        var now = DateTime.UtcNow;

        Seed(_departures, now, [
            ("AC", "856", "Air Canada", "LHR", "London", "E73", -0.9, "Departed"),
            ("AC", "472", "Air Canada", "YOW", "Ottawa", "D32", 1.2, "On Time"),
            ("LH", "6831", "Lufthansa", "FRA", "Frankfurt", "D12", 2.4, "On Time"),
            ("BA", "99", "British Airways", "LHR", "London", "E80", 3.1, "Boarding"),
            ("UA", "938", "United Airlines", "ORD", "Chicago", "C14", 4.6, "On Time"),
            ("WS", "123", "WestJet", "YYC", "Calgary", "A05", 6.0, "Delayed"),
        ]);

        Seed(_arrivals, now, [
            ("AC", "857", "Air Canada", "LHR", "London", "E73", -1.5, "Arrived"),
            ("LH", "6830", "Lufthansa", "FRA", "Frankfurt", "D12", 0.5, "On Time"),
            ("BA", "98", "British Airways", "LHR", "London", "E80", 1.8, "On Time"),
            ("UA", "939", "United Airlines", "ORD", "Chicago", "C14", 3.2, "On Time"),
            ("AA", "712", "American Airlines", "DFW", "Dallas", "C20", 5.5, "On Time"),
        ]);
    }

    private void Seed(List<FEDepartureFlight> list, DateTime now, (string flc, string fln, string carrier, string des, string city, string gate, double hoursFromNow, string status)[] rows)
    {
        foreach (var r in rows)
        {
            var scheduled = now.AddHours(r.hoursFromNow);
            list.Add(new FEDepartureFlight
            {
                RecordId = _nextId++,
                FLC = r.flc,
                FLN = r.fln,
                Carrier = r.carrier,
                DES = r.des,
                CTY = r.city,
                GAT = r.gate,
                ScheduledTime = scheduled,
                EstimatedTime = scheduled,
                ActualTime = r.status is "Departed" or "Arrived" ? scheduled : null,
                RCP = r.status,
                ROP = "Y",
                ROB = "Y",
                ROS = "Y",
                ROT = "Y",
            });
        }
    }

    private void Seed(List<FEArrivalFlight> list, DateTime now, (string flc, string fln, string carrier, string org, string city, string gate, double hoursFromNow, string status)[] rows)
    {
        foreach (var r in rows)
        {
            var scheduled = now.AddHours(r.hoursFromNow);
            list.Add(new FEArrivalFlight
            {
                RecordId = _nextId++,
                FLC = r.flc,
                FLN = r.fln,
                Carrier = r.carrier,
                ORG = r.org,
                CTY = r.city,
                CAR = r.gate,
                ScheduledTime = scheduled,
                EstimatedTime = scheduled,
                ActualTime = r.status is "Departed" or "Arrived" ? scheduled : null,
                RCP = r.status,
                ROP = "Y",
                ROB = "Y",
                ROS = "Y",
                ROT = "Y",
            });
        }
    }

    public List<FEDepartureFlight> Departures(DateTime? start, DateTime? end)
    {
        lock (_lock) return Filter(_departures, f => f.ScheduledTime, start, end);
    }

    public List<FEArrivalFlight> Arrivals(DateTime? start, DateTime? end)
    {
        lock (_lock) return Filter(_arrivals, f => f.ScheduledTime, start, end);
    }

    private static List<T> Filter<T>(List<T> source, Func<T, DateTime?> time, DateTime? start, DateTime? end)
    {
        IEnumerable<T> query = source;
        if (start is not null) query = query.Where(f => time(f) is null || time(f) >= start);
        if (end is not null) query = query.Where(f => time(f) is null || time(f) <= end);
        return query.ToList();
    }

    public FEDepartureFlight? FindDeparture(int id)
    {
        lock (_lock) return _departures.FirstOrDefault(f => f.RecordId == id);
    }

    public FEArrivalFlight? FindArrival(int id)
    {
        lock (_lock) return _arrivals.FirstOrDefault(f => f.RecordId == id);
    }

    public int AddDeparture(FEDepartureFlight flight)
    {
        lock (_lock)
        {
            flight.RecordId = _nextId++;
            _departures.Add(flight);
            return flight.RecordId;
        }
    }

    public int AddArrival(FEArrivalFlight flight)
    {
        lock (_lock)
        {
            flight.RecordId = _nextId++;
            _arrivals.Add(flight);
            return flight.RecordId;
        }
    }

    public IEnumerable<FEDepartureFlight> AllDepartures() { lock (_lock) return _departures.ToList(); }
    public IEnumerable<FEArrivalFlight> AllArrivals() { lock (_lock) return _arrivals.ToList(); }
}
