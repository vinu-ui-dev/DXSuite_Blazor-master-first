using System.Text.RegularExpressions;
using DXSuite.Aviation.Entities;
using DXSuite.Aviation.Services.CounterAllocation;
using DXSuite.Aviation.Services.FlightDataEditor;
using DXSuite.Shared.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Pages.CounterAllocation;

// The counter-allocation timeline (GTAA FSD 1.2 section 3.4, Diagram-style Gantt view). All data is real:
// allocations and airline names come from the database, there is no live RMS/CoreAPI feed behind any of it
// yet (see CounterAllocationService), and zones are derived from each counter id's letter prefix since the
// schema has no separate terminal/aisle table. "OPEN" (what an agent is currently showing) isn't shown here
// because CUPPS never persists published state anywhere this page could read it.
public partial class CounterAllocations
{
    [Inject] private CounterAllocationService allocationService { get; set; } = null!;
    [Inject] private CounterAllocationOutageState outageState { get; set; } = null!;
    [Inject] private ISessionState sessionState { get; set; } = null!;
    [Inject] private IFlightListClock flightListClock { get; set; } = null!;
    [Inject] private IDbContextFactory<AviationDbContext> AviationDbFactory { get; set; } = null!;

    private static readonly Regex ZonePrefixRegex = new(@"^([A-Za-z]+)", RegexOptions.Compiled);
    private static readonly Regex NumberSuffixRegex = new(@"(\d+)$", RegexOptions.Compiled);

    private sealed record AllocView(TblCounterAllocation Row, DateTime StartLocal, DateTime EndLocal);
    private sealed record CounterRow(string Id, List<AllocView> Allocations);
    private sealed record ZoneGroup(string Key, List<CounterRow> Counters);

    private sealed class AllocFormModel
    {
        public int? Id;
        public string Counter = "";
        public string ToCounter = "";
        public string AirlineCode = "";
        public string AllocationType = "Common";
        public string? Destination;
        public string? FlightNumber;
        public DateTime Start;
        public DateTime End;
    }

    private TimeZoneInfo zone = TimeZoneInfo.Utc;
    private DateTime fromLocal;
    private DateTime toLocal;
    private List<AllocView> views = [];
    private Dictionary<string, string?> airlineNames = [];
    private List<string> allCounterIds = [];
    private List<string> zones = [];
    private string selectedZone = "";
    private readonly HashSet<int> selected = [];
    private string? error;
    private bool showOutageConfirm;

    private AllocFormModel? modal;
    private string? modalError;

    private DateTime? copyTargetDate;
    private string? copyError;

    // Sample data for reviewing the grid's layout before a real RMS/CoreAPI feed or seeded counters exist in
    // this environment. Generated in memory only — nothing here is read from or written to the database.
    private bool showDummyData;

    private static readonly (string Code, string Name)[] DummyAirlines =
    [
        ("LH", "Lufthansa"), ("AC", "Air Canada"), ("UA", "United Airlines"), ("AF", "Air France"), ("KL", "KLM"),
    ];

    private static readonly string[] DummyCounterIds =
        ["A11", "A12", "A13", "A14", "A15", "A16", "A17", "A18", "B21", "B22", "B23", "B24"];

    private List<AllocView> GenerateDummyAllocations(DateTime fromLocal, DateTime toLocal)
    {
        var result = new List<AllocView>();
        var rng = new Random(42); // fixed seed: same sample layout every time, for a stable preview
        var nextId = -1;

        foreach (var counterId in DummyCounterIds)
        {
            var cursor = fromLocal;
            var segments = 2 + rng.Next(0, 3);
            for (var i = 0; i < segments && cursor < toLocal; i++)
            {
                var remainingMinutes = (toLocal - cursor).TotalMinutes;
                if (remainingMinutes < 30) break;

                var durationMinutes = Math.Min(remainingMinutes, 60 + rng.Next(0, 240));
                var start = cursor;
                var end = start.AddMinutes(durationMinutes);
                var (code, _) = DummyAirlines[rng.Next(DummyAirlines.Length)];
                var type = rng.NextDouble() switch { < 0.1 => "Info", < 0.25 => "Dedicated", _ => "Common" };
                var isManual = rng.NextDouble() < 0.15;

                var row = new TblCounterAllocation
                {
                    CounterAllocationId = nextId--,
                    Counter = counterId,
                    AirlineCode = code,
                    AllocationType = type,
                    Destination = type == "Dedicated" ? "Sample Destination" : null,
                    FlightNumber = type == "Dedicated" ? $"{code}{100 + rng.Next(900)}" : null,
                    StartUtc = ToUtc(start),
                    EndUtc = ToUtc(end),
                    IsManual = isManual,
                    CreatedUtc = DateTime.UtcNow,
                    CreatedBy = "Sample Data",
                };
                result.Add(new AllocView(row, start, end));

                cursor = end.AddMinutes(rng.Next(0, 3) == 0 ? 15 + rng.Next(0, 30) : 0);
            }
        }

        return result;
    }

    private async Task ToggleDummyDataAsync()
    {
        showDummyData = !showDummyData;
        selectedZone = "";
        selected.Clear();
        await RefreshAsync();
    }

    private List<ZoneGroup> Groups =>
        views
            .GroupBy(v => v.Row.Counter)
            .Select(g => new CounterRow(g.Key, [.. g.OrderBy(v => v.StartLocal)]))
            .Where(r => selectedZone == "" || ZoneKeyFor(r.Id) == selectedZone)
            .GroupBy(r => ZoneKeyFor(r.Id))
            .OrderBy(g => g.Key)
            .Select(g => new ZoneGroup(g.Key, [.. g.OrderBy(r => NumericSuffix(r.Id))]))
            .ToList();

    private double SpanMinutes => Math.Max(1, (toLocal - fromLocal).TotalMinutes);
    private double Pct(DateTime local) => Math.Clamp((local - fromLocal).TotalMinutes / SpanMinutes * 100, 0, 100);

    /// <summary>Grid width in pixels, scaled to the window so each hour keeps a readable minimum width no
    /// matter how wide From/To is set — a fixed width made hour columns (and tick labels) shrink to
    /// illegible slivers once the window grew past a day or so.</summary>
    private double GridWidthPx
    {
        get
        {
            var hours = Math.Max(1, SpanMinutes / 60);
            var pxPerHour = hours switch
            {
                <= 16 => 85.0,
                <= 30 => 55.0,
                <= 48 => 40.0,
                <= 72 => 30.0,
                _ => 16.0,
            };
            return Math.Clamp(hours * pxPerHour, 1100, 20000);
        }
    }
    private DateTime NowLocal => ToLocal(flightListClock.Time.GetUtcNow().UtcDateTime);
    private bool NowInWindow => NowLocal >= fromLocal && NowLocal <= toLocal;

    protected override async Task OnInitializedAsync()
    {
        zone = flightListClock.CurrentWindow().AirportTimeZone;
        SetNowWindowValues();
        await LoadAirlinesAsync();
        await LoadCountersAsync();
        await RefreshAsync();

        // No real allocations anywhere for this tenant (nothing seeded, no RMS/CoreAPI feed yet) — default to
        // the sample view instead of an empty grid. Switching the toggle off still shows the real (empty) grid.
        if (views.Count == 0 && allCounterIds.Count == 0)
        {
            showDummyData = true;
            await RefreshAsync();
        }
    }

    private DateTime ToUtc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
    private DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    private static string ZoneKeyFor(string counterId)
    {
        var m = ZonePrefixRegex.Match(counterId);
        return m.Success ? $"AISLE {m.Value.ToUpperInvariant()}" : "OTHER COUNTERS";
    }

    /// <summary>A friendlier form of <see cref="ZoneKeyFor"/> for inline labels, e.g. "Aisle A". There's no
    /// terminal number to show here — the schema has no terminal/aisle table, only the counter id itself.</summary>
    private static string ZoneName(string counterId)
    {
        var key = ZoneKeyFor(counterId);
        return key.StartsWith("AISLE ") ? $"Aisle {key["AISLE ".Length..]}" : "Other";
    }

    private static int NumericSuffix(string counterId)
    {
        var m = NumberSuffixRegex.Match(counterId);
        return m.Success ? int.Parse(m.Value) : 0;
    }

    private static string ColorFor(string code)
    {
        var hash = 0;
        foreach (var c in code) hash = hash * 31 + c;
        var hue = Math.Abs(hash) % 360;
        return $"hsl({hue},52%,42%)";
    }

    private string AirlineName(string code)
    {
        if (showDummyData)
        {
            var match = DummyAirlines.FirstOrDefault(a => a.Code == code);
            if (match.Code is not null) return match.Name;
        }
        return airlineNames.TryGetValue(code, out var name) && !string.IsNullOrWhiteSpace(name) ? name! : code;
    }

    private async Task LoadAirlinesAsync()
    {
        // Projected to just these two columns: some deployments of this externally-owned table predate
        // newer columns (CarrierLogoPath, Pincode, ...) that other, unrelated pages read — loading full
        // TblAirline entities here would fail on those deployments for columns this page never uses.
        await using var db = await AviationDbFactory.CreateDbContextAsync();
        airlineNames = await db.TblAirlines
            .Select(a => new { a.AirlineCode, a.CarrierName })
            .ToDictionaryAsync(a => a.AirlineCode, a => a.CarrierName);
    }

    private async Task LoadCountersAsync()
    {
        await using var db = await AviationDbFactory.CreateDbContextAsync();
        var counters = await db.TblCounterAllocations.Select(a => a.Counter).Distinct().ToListAsync();
        allCounterIds = [.. counters.OrderBy(ZoneKeyFor).ThenBy(NumericSuffix)];
        zones = [.. allCounterIds.Select(ZoneKeyFor).Distinct().OrderBy(z => z)];
    }

    /// <summary>The counters from From to To, inclusive, in the same order as <see cref="allCounterIds"/> — may
    /// cross a zone boundary; that's fine, it just means the range spans two aisles.</summary>
    private List<string> CounterRange(string from, string to)
    {
        var i1 = allCounterIds.IndexOf(from);
        var i2 = allCounterIds.IndexOf(to);
        if (i1 < 0 || i2 < 0) return from == to && from != "" ? [from] : [];
        var lo = Math.Min(i1, i2);
        var hi = Math.Max(i1, i2);
        return allCounterIds[lo..(hi + 1)];
    }

    private async Task RefreshAsync()
    {
        error = null;
        if (showDummyData)
        {
            views = [.. GenerateDummyAllocations(fromLocal, toLocal).OrderBy(v => v.Row.Counter).ThenBy(v => v.StartLocal)];
            StateHasChanged();
            return;
        }
        var rows = await allocationService.GetInRangeAsync(ToUtc(fromLocal), ToUtc(toLocal));
        views = [.. rows.Select(r => new AllocView(r, ToLocal(r.StartUtc), ToLocal(r.EndUtc))).OrderBy(v => v.Row.Counter).ThenBy(v => v.StartLocal)];
        StateHasChanged();
    }

    private void SetNowWindowValues()
    {
        var now = ToLocal(flightListClock.Time.GetUtcNow().UtcDateTime);
        fromLocal = now.AddHours(-1);
        toLocal = now.AddHours(12);
    }

    private async Task SetNowWindow()
    {
        SetNowWindowValues();
        await RefreshAsync();
    }

    private async Task SetTomorrowWindow()
    {
        var today = ToLocal(flightListClock.Time.GetUtcNow().UtcDateTime).Date;
        fromLocal = today.AddDays(1);
        toLocal = today.AddDays(2);
        await RefreshAsync();
    }

    /// <summary>Hourly ticks for narrow windows; wider windows thin the interval so labels stop overlapping
    /// (at 1 tick/hour, a 24h+ window packs more labels than the grid's width can legibly show). Day-boundary
    /// ticks (the "Mon 9 Nov" style labels) are always included regardless of step, so dates stay visible.</summary>
    private IEnumerable<DateTime> HourTicks()
    {
        var start = new DateTime(fromLocal.Year, fromLocal.Month, fromLocal.Day, fromLocal.Hour, 0, 0);
        if (start < fromLocal) start = start.AddHours(1);

        var stepHours = (toLocal - fromLocal).TotalHours switch
        {
            <= 16 => 1,
            <= 30 => 2,
            <= 48 => 3,
            <= 72 => 4,
            _ => 6,
        };

        var ticks = new SortedSet<DateTime>();
        for (var t = start; t <= toLocal; t = t.AddHours(stepHours)) ticks.Add(t);
        for (var d = fromLocal.Date.AddDays(1); d <= toLocal; d = d.AddDays(1)) ticks.Add(d);

        return ticks;
    }

    private static string TickLabel(DateTime t) => t.Hour == 0 ? t.ToString("ddd d MMM") : t.ToString("HH:mm");

    private void ToggleManualMode(ChangeEventArgs e)
    {
        if (e.Value is true) outageState.SwitchOn();
        else showOutageConfirm = true; // confirm before turning off; the checkbox snaps back until confirmed
    }

    private void ConfirmOutageOff()
    {
        outageState.SwitchOff();
        showOutageConfirm = false;
    }

    private void CancelOutageOff() => showOutageConfirm = false;

    private void OpenAdd(string? counterId)
    {
        var start = NowLocal;
        var rounded = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0);
        var defaultCounter = counterId ?? allCounterIds.FirstOrDefault() ?? "";
        modal = new AllocFormModel { Counter = defaultCounter, ToCounter = defaultCounter, Start = rounded, End = rounded.AddHours(2) };
        modalError = null;
    }

    private void OpenEdit(TblCounterAllocation row)
    {
        modal = new AllocFormModel
        {
            Id = row.CounterAllocationId,
            Counter = row.Counter,
            AirlineCode = row.AirlineCode,
            AllocationType = row.AllocationType,
            Destination = row.Destination,
            FlightNumber = row.FlightNumber,
            Start = ToLocal(row.StartUtc),
            End = ToLocal(row.EndUtc),
        };
        modalError = null;
    }

    private void CloseModal()
    {
        modal = null;
        modalError = null;
        selected.Clear();
    }

    private void OnRowClick(string counterId)
    {
        if (showDummyData) return;
        OpenAdd(counterId);
    }

    private void OnBlockClick(TblCounterAllocation row, MouseEventArgs e)
    {
        if (showDummyData) return;
        if (e.CtrlKey || e.MetaKey)
        {
            if (!selected.Remove(row.CounterAllocationId)) selected.Add(row.CounterAllocationId);
        }
        else
        {
            selected.Clear();
            selected.Add(row.CounterAllocationId);
            OpenEdit(row);
        }
    }

    private static string? Describe(CounterAllocationSaveOutcome outcome, string? conflict) => outcome switch
    {
        CounterAllocationSaveOutcome.Overlap => $"That counter already has an allocation for {conflict} in that range.",
        CounterAllocationSaveOutcome.InvalidRange => "End must be after start.",
        CounterAllocationSaveOutcome.NotFound => "That allocation no longer exists — it may have been removed elsewhere.",
        _ => null,
    };

    private CounterAllocationDraft BuildDraft(string counter) => new(
        counter, modal!.AirlineCode.Trim().ToUpperInvariant(), modal.AllocationType,
        modal.AllocationType == "Dedicated" ? modal.Destination : null,
        modal.AllocationType == "Dedicated" ? modal.FlightNumber : null,
        ToUtc(modal.Start), ToUtc(modal.End));

    private async Task SaveModalAsync()
    {
        if (modal is null) return;
        modalError = null;
        if (string.IsNullOrWhiteSpace(modal.AirlineCode))
        {
            modalError = "Please choose an airline.";
            return;
        }

        if (modal.Id is { } id)
        {
            var result = await allocationService.UpdateAsync(id, BuildDraft(modal.Counter));
            modalError = Describe(result.Outcome, result.ConflictingAirline);
            if (modalError is not null) return;
        }
        else
        {
            var range = CounterRange(modal.Counter, modal.ToCounter);
            if (range.Count == 0)
            {
                modalError = "Choose a valid counter range.";
                return;
            }

            var failures = new List<string>();
            foreach (var counter in range)
            {
                var result = await allocationService.CreateAsync(BuildDraft(counter), sessionState.User?.FriendlyName ?? sessionState.User?.UserPrincipalName);
                if (result.Outcome != CounterAllocationSaveOutcome.Saved)
                    failures.Add($"{counter}: {Describe(result.Outcome, result.ConflictingAirline)}");
            }
            if (failures.Count > 0)
            {
                // Counters earlier in the range that succeeded before a later one failed are already saved —
                // the grid refresh below shows exactly what went through.
                modalError = string.Join(" ", failures);
                await LoadCountersAsync();
                await RefreshAsync();
                return;
            }
        }

        modal = null;
        selected.Clear();
        await LoadCountersAsync();
        await RefreshAsync();
    }

    private async Task DeleteFromModalAsync()
    {
        if (modal?.Id is not { } id) return;
        await allocationService.DeleteAsync(id);
        modal = null;
        selected.Clear();
        await RefreshAsync();
    }

    private async Task DeleteSelectedAsync()
    {
        foreach (var id in selected.ToList())
            await allocationService.DeleteAsync(id);
        selected.Clear();
        await RefreshAsync();
    }

    private void OpenCopy()
    {
        copyTargetDate = fromLocal.Date;
        copyError = null;
    }

    private void CloseCopy()
    {
        copyTargetDate = null;
        copyError = null;
    }

    private async Task SaveCopyAsync()
    {
        if (copyTargetDate is not { } targetDate) return;
        copyError = null;

        var ids = selected.ToList();
        var sourceRows = views.Where(v => ids.Contains(v.Row.CounterAllocationId)).ToList();
        var failures = new List<string>();

        foreach (var v in sourceRows)
        {
            var dayDelta = targetDate.Date - v.StartLocal.Date;
            var newStart = v.StartLocal + dayDelta;
            var newEnd = v.EndLocal + dayDelta;
            var draft = new CounterAllocationDraft(
                v.Row.Counter, v.Row.AirlineCode, v.Row.AllocationType, v.Row.Destination, v.Row.FlightNumber,
                ToUtc(newStart), ToUtc(newEnd));
            var result = await allocationService.CreateAsync(draft, sessionState.User?.FriendlyName ?? sessionState.User?.UserPrincipalName);
            if (result.Outcome != CounterAllocationSaveOutcome.Saved)
                failures.Add($"{v.Row.Counter}: {Describe(result.Outcome, result.ConflictingAirline)}");
        }

        if (failures.Count > 0)
        {
            copyError = string.Join(" ", failures);
        }
        else
        {
            copyTargetDate = null;
            selected.Clear();
            await RefreshAsync();
        }
    }
}
