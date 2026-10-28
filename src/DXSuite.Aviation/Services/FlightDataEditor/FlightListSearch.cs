using DXSuite.Connectors.Services.CoreApi.Models;

namespace DXSuite.Aviation.Services.FlightDataEditor;

/// <summary>
/// The Flight Data Editor list's search and paging (FDE-10). Search is by flight number, the flight's own or a
/// code share's, and runs over the whole window before paging, so a flight is found whichever page is open.
/// </summary>
public static class FlightListSearch
{
    public static List<FEArrivalFlight> Filter(IEnumerable<FEArrivalFlight?> flights, string? flightNumber)
    {
        var wanted = Normalise(flightNumber);
        return flights.OfType<FEArrivalFlight>()
            .Where(f => wanted.Length == 0 || Matches(wanted, f.FLT, f.FLC, f.FLN, f.CodeShares))
            .ToList();
    }

    public static List<FEDepartureFlight> Filter(IEnumerable<FEDepartureFlight?> flights, string? flightNumber)
    {
        var wanted = Normalise(flightNumber);
        return flights.OfType<FEDepartureFlight>()
            .Where(f => wanted.Length == 0 || Matches(wanted, f.FLT, f.FLC, f.FLN, f.CodeShares))
            .ToList();
    }

    /// <summary>
    /// One page of <paramref name="flights"/>, counted from 1. A page past the last shows the last, so a search
    /// typed on a later page shows its matches rather than an empty page.
    /// </summary>
    public static List<T> Page<T>(IReadOnlyList<T> flights, int page, int pageSize)
    {
        if (pageSize < 1) return flights.ToList();
        var lastPage = Math.Max(1, (flights.Count + pageSize - 1) / pageSize);
        var shown = Math.Clamp(page, 1, lastPage);
        return flights.Skip((shown - 1) * pageSize).Take(pageSize).ToList();
    }

    private static bool Matches(string wanted, string? flt, string? flc, string? fln, IEnumerable<FECodeShare>? codeShares) =>
        Normalise(flt).Contains(wanted, StringComparison.Ordinal)
        || Normalise(flc + fln).Contains(wanted, StringComparison.Ordinal)
        || (codeShares ?? []).Any(cs => Normalise(cs.FLC + cs.FLN).Contains(wanted, StringComparison.Ordinal));

    // Flight numbers are typed as "AC145", "ac 145" or just "145".
    private static string Normalise(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : string.Concat(text.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
}
