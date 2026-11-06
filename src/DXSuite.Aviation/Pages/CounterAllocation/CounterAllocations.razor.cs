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
    private DateTime NowLocal => ToLocal(flightListClock.Time.GetUtcNow().UtcDateTime);
    private bool NowInWindow => NowLocal >= fromLocal && NowLocal <= toLocal;

    protected override async Task OnInitializedAsync()
    {
        zone = flightListClock.CurrentWindow().AirportTimeZone;
        SetNowWindowValues();
        await LoadAirlinesAsync();
        await LoadCountersAsync();
        await RefreshAsync();
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

    private string AirlineName(string code) =>
        airlineNames.TryGetValue(code, out var name) && !string.IsNullOrWhiteSpace(name) ? name! : code;

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

    private IEnumerable<DateTime> HourTicks()
    {
        var start = new DateTime(fromLocal.Year, fromLocal.Month, fromLocal.Day, fromLocal.Hour, 0, 0);
        if (start < fromLocal) start = start.AddHours(1);
        for (var t = start; t <= toLocal; t = t.AddHours(1)) yield return t;
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

    private void OnRowClick(string counterId) => OpenAdd(counterId);

    private void OnBlockClick(TblCounterAllocation row, MouseEventArgs e)
    {
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
