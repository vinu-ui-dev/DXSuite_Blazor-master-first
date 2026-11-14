using DXSuite.Aviation.Services.Cupps;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Services.Identity;
using Microsoft.AspNetCore.Components;
using static DXSuite.Aviation.Pages.Cupps.CuppsData;

namespace DXSuite.Aviation.Pages.Cupps;

// State and actions for the CUPPS agent screens prototype. Ported from the DXME-500 HTML prototype;
// all state is in memory for this circuit and nothing is saved or sent anywhere. The RMU airline/counter
// picker is the one path wired to live data (CuppsLiveDataProvider); every other scenario keeps using the
// static CuppsData below, untouched.
public partial class CuppsAgentScreens : IDisposable
{
    [Inject] private CuppsLiveDataProvider LiveData { get; set; } = null!;
    [Inject] private ISessionState SessionState { get; set; } = null!;
    [Inject] private IUserGroupService UserGroupService { get; set; } = null!;

    /// <summary>Signed-in users holding this role skip the RMU airline/counter picker entirely and land
    /// straight on their counter's terminal screen, the same as the "agent" demo scenario.</summary>
    private const string AirlineAgentRoleName = "Director Airline_Agent";

    /// <summary>Signed-in users holding this role see only their own airline in the RMU picker's airline
    /// grid, matching the same group-name-equals-airline-name rule Configuration > Airlines already uses.</summary>
    private const string AirlineSupervisorRoleName = "Airline Supervisor";

    private Dictionary<string, Airline> liveAirlines = [];
    private Dictionary<string, int> liveAirlineIds = [];
    private Dictionary<string, Counter> liveCounters = [];
    private List<Alloc> liveAllocations = [];
    private List<Flight> liveFlights = [];
    private List<Template> liveTemplates = [];
    private List<Canned> liveApprovedMessages = [];
    private bool liveLoading;
    private bool liveLoadFailed;
    private bool liveAllocLoadFailed;

    private string scenario = "agent";
    private string screen = "login";
    private int now = 630;
    private string tab = "mine";
    private bool coreDown;
    private bool deviation = true;
    private readonly bool fallback = false;
    private string tabPref = "Standard";
    private bool otherOpen = true;
    private Dictionary<string, CounterSetup> published = SeedPublished();
    private CounterSetup draft = new();
    private string? pickTab;
    private bool confirmClose;
    private bool waitingReady;
    private CounterSetup? pending;
    private HubMessage? message;
    private int? idle;
    private readonly HashSet<string> sel = [];
    private string zone = "";
    private string bulkPage = "t1";
    private string? changePage;
    private bool viewSent;
    private string? rmuAirline;
    private (string Airline, string Counter)? acting;
    private string? rmuNotice;
    private string? endedReason;
    private string search = "";
    private readonly int many = 0;
    private string psearch = "";
    private int tick;
    private string? toast;

    private Timer? cycleTimer;
    private Timer? idleTimer;
    private CancellationTokenSource? toastCts;

    /// <summary>Scenario key to start in. "rmu" (RMU Staff, Entra) starts at the Microsoft sign-in screen.</summary>
    [Parameter] public string StartScenario { get; set; } = "rmu";

    [Parameter] public string Title { get; set; } = "CUPPS Agent Screens";

    protected override void OnInitialized()
    {
        // A real "Director Airline_Agent" user already has a fixed counter, so there's nothing for them
        // to pick — skip straight to the terminal screen instead of the RMU airline/counter picker.
        var effectiveScenario = string.Equals(SessionState.ActualRoleName, AirlineAgentRoleName, StringComparison.OrdinalIgnoreCase)
            ? "agent"
            : StartScenario;
        SetScenario(effectiveScenario);
        // Counter agents land straight in the app; RMU staff go through sign-in and the airline/counter picker.
        if (Sc.Kind != "Individual") EnterApp();
    }

    protected override void OnAfterRender(bool firstRender)
    {
        // Rotating text: Messages on the screen footer and codeshares in the flight list advance together every 3 s.
        if (firstRender)
            cycleTimer = new Timer(_ => InvokeAsync(() => { tick++; StateHasChanged(); }), null, 3000, 3000);
    }

    public void Dispose()
    {
        cycleTimer?.Dispose();
        idleTimer?.Dispose();
        toastCts?.Cancel();
    }

    // ---------- Derived state ----------
    private Scenario Sc => Scenarios[scenario];
    private bool Can(string key) => Keys[Sc.Role].Contains(key);
    private string? Station => acting?.Counter ?? Sc.Station;
    private string MyAirline => acting?.Airline ?? Sc.Airline ?? "WS";

    /// <summary>The RMU scenario runs on CoreAPI and counter-allocation data only; the other scenarios are static demos.</summary>
    private bool Live => scenario == "rmu";

    private List<Alloc> AllAllocs => Live ? liveAllocations : Allocations;

    private List<Counter> CounterList => Live
        ? [.. liveCounters.Values.OrderBy(c => c.Id)]
        : Counters;

    private List<string> ZoneList => Live
        ? [.. liveCounters.Values.SelectMany(c => c.Zones.Skip(1)).Distinct().Order()]
        : [.. Zones];

    private List<Canned> MessageList => Live ? liveApprovedMessages : CannedMessages;

    private Alloc? HolderNow(string id) => AllAllocs.FirstOrDefault(a => a.CounterId == id && a.From <= now && now < a.To);
    private Alloc? AllocFor(string? id, string code) => AllAllocs.FirstOrDefault(a => a.CounterId == id && a.Code == code && a.To > now);
    private bool IsWaiting => AllocFor(Station, MyAirline) is { } a && a.From > now;
    private CounterSetup? Pub(string? id) => id is not null && published.TryGetValue(id, out var p) ? p : null;
    private bool InApp => screen == "app";

    /// <summary>Airline metadata for a code: live-fetched data first (the RMU picker), falling back to the
    /// static demo dictionary, falling back to a generic entry so a live code never throws.</summary>
    private Airline AirlineInfo(string code) =>
        liveAirlines.TryGetValue(code, out var live) ? live
        : Airlines.TryGetValue(code, out var demo) ? demo
        : new Airline(code, code, "#555");

    /// <summary>A counter's zone/SSBD metadata from the active source (live map for RMU, static demo otherwise).</summary>
    private Counter? CounterInfo(string? id)
    {
        if (id is null) return null;
        return Live
            ? liveCounters.TryGetValue(id, out var live) ? live : null
            : Counters.FirstOrDefault(c => c.Id == id);
    }

    private List<Template> Usable()
    {
        var source = Live
            ? liveTemplates
            : Templates.Concat(Many.Take(many > 0 ? many - 8 : 0)).ToList();
        return source.Where(t => t.Scope == "airport" || t.Scope == MyAirline).ToList();
    }

    /// <summary>Another airline's view swaps the operating carrier so the table reads as its own flights.</summary>
    private List<Flight> NextFlights(int n)
    {
        if (Live)
        {
            var upcoming = liveFlights.Where(f => f.Sched > now).ToList();
            var mine = upcoming.Where(f => OperatesOrShares(f, MyAirline)).Take(n).ToList();
            return mine.Count > 0 ? mine : upcoming.Take(n).ToList();
        }

        return Flights.Where(f => f.Sched > now).Take(n).Select(f =>
        {
            var c = MyAirline;
            if (c == "WS") return f;
            var pt = Partners.GetValueOrDefault(c) ?? [];
            var shares = f.Carriers.Skip(1).Select(x => pt.TryGetValue(x.Airline, out var p) ? (p.Name, p.Code + x.No[2..]) : x);
            return f with { Carriers = [(AirlineInfo(c).Name, f.Carriers[0].No.Replace("WS", c)), .. shares] };
        }).ToList();
    }

    private static bool OperatesOrShares(Flight f, string code) =>
        f.Carriers.Any(c => c.No.Length > code.Length && c.No.StartsWith(code, StringComparison.OrdinalIgnoreCase) && char.IsDigit(c.No[code.Length]));

    private record SplitInfo(bool Split, string? Other = null, bool SameAirline = false, bool OtherOpen = false);

    private SplitInfo GetSplit()
    {
        var c = CounterInfo(Station);
        if (c?.Split is null) return new(false);
        var h = HolderNow(c.Split);
        var same = h is not null && h.Code == MyAirline;
        var oOpen = c.Split == "G2-5" ? otherOpen : Pub(c.Split)?.Open == true;
        return new(true, c.Split, same, oOpen);
    }

    private CounterSetup FreshDraft()
    {
        var p = Pub(Station);
        if (p is not { Open: true }) return new CounterSetup();
        var d = p.Clone();
        d.SaveText = false;
        return d;
    }

    private bool IsDirty()
    {
        var d = draft;
        CounterSetup? p;
        if (IsWaiting && waitingReady && pending is not null)
        {
            p = pending.Clone();
            p.Open = true;
            p.Text = p.Text.Trim();
        }
        else p = Pub(Station);
        if (p is not { Open: true })
            return d.Page is not null || d.Msgs.Count > 0 || d.Text.Trim() != "" || d.Asl || d.Sunflower || d.Dest != "";
        return d.Page != p.Page || string.Join(",", d.Msgs) != string.Join(",", p.Msgs) || d.Text.Trim() != p.Text
            || d.Asl != p.Asl || d.Sunflower != p.Sunflower || d.Dest != p.Dest
            || (GetSplit().SameAirline && d.Layout != p.Layout);
    }

    /// <summary>The draft as the screen would show it after Publish.</summary>
    private CounterSetup DraftPreview()
    {
        var d = draft.Clone();
        d.Open = true;
        d.Text = d.Text.Trim();
        return d;
    }

    private int SlotsUsed => draft.Msgs.Count + (draft.Text.Trim() != "" ? 1 : 0);

    private string EffectivePickTab
    {
        get
        {
            if (pickTab is not null) return pickTab;
            var list = Usable();
            return list.Any(t => t.Kind == tabPref) ? tabPref : (tabPref == "Standard" ? "Custom" : "Standard");
        }
    }

    private List<string> MessagesOf(CounterSetup s)
    {
        var byId = MessageList.ToDictionary(m => m.Id, m => m, StringComparer.OrdinalIgnoreCase);
        return [.. s.Msgs.Select(id => byId.TryGetValue(id, out var msg) ? msg.En : id), .. (s.Text != "" ? new[] { s.Text } : [])];
    }

    private string Cycle(IReadOnlyList<string> seq) => seq.Count == 0 ? "" : seq[tick % seq.Count];

    // ---------- Actions ----------
    private void EnterApp()
    {
        screen = "app"; tab = "mine"; draft = FreshDraft(); pickTab = null; confirmClose = false;
        sel.Clear(); waitingReady = false; viewSent = false; changePage = null;
    }

    private void SetScenario(string key)
    {
        StopIdle();
        scenario = key; screen = "login"; now = Scenarios[key].Now; acting = null; rmuAirline = null; rmuNotice = null;
        search = ""; message = null; endedReason = null; published = Live ? [] : SeedPublished(); coreDown = false; draft = new();
        deviation = true;
        liveAirlines = []; liveAirlineIds = []; liveCounters = []; liveAllocations = []; liveFlights = []; liveTemplates = []; liveApprovedMessages = []; liveLoading = false; liveLoadFailed = false;
    }

    private async Task SignIn()
    {
        if (Sc.Kind == "Individual")
        {
            screen = "rmu-pick";
            await LoadLiveCuppsDataAsync();
        }
        else EnterApp();
    }

    /// <summary>Loads the RMU picker's airline grid from Director's CUPPS airlines API and its counter grid from
    /// <see cref="CuppsLiveDataProvider"/>, replacing <see cref="now"/> with the real current time so live
    /// allocations filter correctly. Demo scenarios never call this, so they're unaffected. The three sources
    /// fail independently — e.g. a missing counter-allocation table shouldn't hide an airline list that loaded
    /// fine, it should just mean no airline shows anything "allocated".</summary>
    private async Task LoadLiveCuppsDataAsync()
    {
        liveLoading = true;
        liveLoadFailed = false;
        liveAllocLoadFailed = false;
        StateHasChanged();

        try
        {
            var accounts = await LiveData.GetOperatingAirlineAccountsAsync();
            liveAirlines = accounts.ToDictionary(a => a.Airline.Code, a => a.Airline, StringComparer.OrdinalIgnoreCase);
            liveAirlineIds = accounts
                .GroupBy(a => a.Airline.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().AirlineId, StringComparer.OrdinalIgnoreCase);
            await ScopeToSupervisorAirlineAsync();
        }
        catch (Exception)
        {
            liveLoadFailed = true;
        }

        try
        {
            var (allocations, nowMinutes) = await LiveData.GetLiveAllocationsAsync();
            liveAllocations = [.. allocations];
            now = nowMinutes;
            if (UseDemoAllocations && liveAllocations.Count == 0) liveAllocations = BuildDemoAllocations();
            BuildLiveCounterMap();
        }
        catch (Exception)
        {
            liveAllocLoadFailed = true;
            if (UseDemoAllocations)
            {
                liveAllocations = BuildDemoAllocations();
                BuildLiveCounterMap();
                liveAllocLoadFailed = false;
            }
            else
            {
                liveAllocations = [];
                liveCounters = [];
            }
        }

        try
        {
            liveFlights = [.. await LiveData.GetTodaysDeparturesAsync()];
        }
        catch (Exception)
        {
            liveFlights = [];
        }

        SeedDemoPublishedState();

        liveLoading = false;
        StateHasChanged();
    }

    /// <summary>Narrows the RMU picker's airline grid to just the caller's own airline when they're a
    /// signed-in Airline Supervisor — same group-name-equals-airline-name rule AirlinesTab.razor already
    /// uses for Configuration > Airlines, so a supervisor's CUPPS access always lines up with what that
    /// tab already shows them. Every other role keeps seeing the full airline list, unchanged. If the
    /// supervisor's group doesn't match any onboarded airline, the full list is left in place rather than
    /// showing an empty, unexplained picker — the actual write-side enforcement (PublishCounterAsync /
    /// CloseCounterAsync) is server-side and doesn't depend on this filter being applied.</summary>
    private async Task ScopeToSupervisorAirlineAsync()
    {
        if (!string.Equals(SessionState.ActualRoleName, AirlineSupervisorRoleName, StringComparison.OrdinalIgnoreCase))
            return;
        if (SessionState.User is null || liveAirlines.Count == 0)
            return;

        List<TblUserGroup> myGroups;
        try
        {
            myGroups = await UserGroupService.GetGroupsForUserAsync(SessionState.User.UserId);
        }
        catch
        {
            return;
        }

        var myGroupNames = myGroups.Select(g => g.GroupName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mine = liveAirlines
            .Where(kv => myGroupNames.Contains(kv.Value.Name))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        if (mine.Count == 0)
            return;

        liveAirlines = mine;
        liveAirlineIds = liveAirlineIds
            .Where(kv => mine.ContainsKey(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    // Demo-only: set to false (and delete BuildDemoAllocations) once real counter allocations exist.
    private const bool UseDemoAllocations = true;

    /// <summary>Demo-only fallback: gives each listed airline one active counter so step 1 and step 2 stay
    /// usable when live allocation data is unavailable.</summary>
    private List<Alloc> BuildDemoAllocations()
    {
        var airlines = liveAirlines.Keys.Order().ToList();
        var result = new List<Alloc>();
        for (var i = 0; i < airlines.Count; i++)
        {
            var counter = $"G1-{i + 1}";
            result.Add(new Alloc(counter, airlines[i], Math.Max(0, now - 120), Math.Min(1440, now + 240), "Dedicated"));
        }
        return result;
    }

    private void BuildLiveCounterMap()
    {
        liveCounters = liveAllocations
            .Select(a => a.CounterId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id)
            .ToDictionary(
                id => id,
                id => new Counter(id, InferZones(id)),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string[] InferZones(string counterId)
    {
        var parts = counterId.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return ["Counters", "All counters"];

        var aisle = parts[0].ToUpperInvariant();
        var terminal = aisle.StartsWith("C", StringComparison.OrdinalIgnoreCase) ? "Terminal 1" : "Terminal 3";
        var zones = new List<string> { terminal, $"{terminal.Replace("Terminal ", "T")} Aisle {aisle}" };

        if (aisle == "G2" && int.TryParse(parts[1], out var n) && n >= 7)
            zones.Add("T3 SSBDs");

        return [.. zones];
    }

    /// <summary>Demo-only: pre-opens a few of the demo counters so a manager walkthrough doesn't need a live
    /// Publish click first — matching how the reference DXME-500 prototype always starts with some counters
    /// already open. In-memory only, same as the rest of CUPPS's published state; remove this method together
    /// with <see cref="BuildDemoAllocations"/> once the demo is done.</summary>
    private void SeedDemoPublishedState()
    {
        void Open(string counterId, string page, string dest = "", List<string>? msgs = null)
        {
            if (!liveAllocations.Any(a => a.CounterId == counterId)) return;
            published[counterId] = new CounterSetup { Open = true, Page = page, Dest = dest, Msgs = msgs ?? [], SentAt = now };
        }

        Open("G1-1", "t6");
        Open("G1-2", "t5", "Toronto");
        Open("G2-1", "t1");
        Open("G2-2", "t3", msgs: ["m2"]);
        Open("G2-3", "t2");
        Open("G2-4", "t1");
        Open("G2-7", "t5");
        Open("G2-8", "t3");
        Open("H1-1", "t2");
        Open("H1-2", "t6", "Toronto");
        Open("C1-1", "t1");
        Open("C1-2", "t5");
    }

    private void SignOut()
    {
        StopIdle();
        screen = "ended";
        endedReason = $"You signed out. {(Station is not null ? $"Counter {Station} stays as it is on the screen until the allocation ends or someone closes it." : "")}";
        acting = null;
    }

    private void SetTab(string v) { tab = v; confirmClose = false; }

    private void PickTab(string k) { pickTab = k; psearch = ""; }

    private void ToggleMsg(string id)
    {
        var i = draft.Msgs.IndexOf(id);
        if (i >= 0) draft.Msgs.RemoveAt(i);
        else if (SlotsUsed < 2) draft.Msgs.Add(id);
    }

    private void ClearMessages() { draft.Msgs.Clear(); draft.Text = ""; }

    private void SendDraft(CounterSetup d)
    {
        var s = d.Clone();
        s.Open = true; s.Text = d.Text.Trim(); s.SaveText = false; s.SentAt = now;
        published[Station!] = s;
    }

    /// <summary>The caller's own airline id for the live (RMU/scoped-supervisor) flow — null for every
    /// demo scenario, which never persists. Resolved from the real Director airline list, not the demo
    /// dictionary, so this only ever matches a real onboarded airline.</summary>
    private int? MyAirlineId => Live && liveAirlineIds.TryGetValue(MyAirline, out var id) ? id : null;

    private async Task Publish()
    {
        if (IsWaiting)
        {
            waitingReady = true;
            pending = draft.Clone();
            ShowToast($"Ready. {Station} publishes at {Hm(AllocFor(Station, MyAirline)!.From)}.");
            return;
        }
        var saved = draft.Text.Trim() != "" && draft.SaveText;
        SendDraft(draft);
        draft.SaveText = false;
        await PersistPublishAsync();
        ShowToast($"Published to {Station}.{(saved ? " The typed Message also went for approval." : "")}");
    }

    /// <summary>Writes the just-published draft through to Director's real counter-publish tables when
    /// this is the live flow (RMU staff or an Airline Supervisor/Agent reached through it) — the demo
    /// scenarios keep being purely in-memory, same as before this existed. The screen already reflects
    /// the publish via <see cref="published"/> regardless of whether this succeeds; a failure here only
    /// means Director doesn't have a durable record of it, surfaced as a toast rather than blocking the
    /// screen the agent/supervisor is looking at.</summary>
    private async Task PersistPublishAsync()
    {
        if (!Live || Station is null || MyAirlineId is not { } airlineId) return;

        var tpl = Usable().FirstOrDefault(t => t.Id == draft.Page);
        var (ok, error) = await LiveData.PublishCounterAsync(
            Station, airlineId, draft.Page, tpl?.Kind, tpl?.En, draft.Dest, draft.Sunflower);
        if (!ok)
            ShowToast($"{Station} is updated here but Director wasn't saved: {error ?? "unknown error"}.");
    }

    private async Task CloseCounter()
    {
        published[Station!] = CounterSetup.Closed();
        draft = FreshDraft();
        confirmClose = false;

        if (Live && Station is not null)
        {
            var (ok, error) = await LiveData.CloseCounterAsync(Station);
            ShowToast(ok
                ? $"Closed {Station}."
                : $"{Station} is closed here but Director wasn't saved: {error ?? "unknown error"}.");
        }
        else
        {
            ShowToast($"Closed {Station}.");
        }
    }

    private void ToggleSel(string id)
    {
        if (IsWaiting) return;
        if (!sel.Remove(id)) sel.Add(id);
    }

    private static CounterSetup Reopen(CounterSetup? p)
    {
        var s = p is { Open: true } ? p.Clone() : new CounterSetup();
        s.Open = true;
        return s;
    }

    private void TileOpen(string id, bool on)
    {
        published[id] = on ? new CounterSetup { Open = true, Page = deviation ? "t1" : null, SentAt = now } : CounterSetup.Closed();
        if (id == Station) draft = FreshDraft();
        ShowToast($"{(on ? "Opened" : "Closed")} {id}.");
    }

    private void PickChangePage(string page)
    {
        var id = changePage!;
        var s = Reopen(Pub(id));
        s.Page = page; s.SentAt = now;
        published[id] = s;
        if (id == Station) draft = FreshDraft();
        changePage = null;
        ShowToast($"{id} now shows {Tpl(page)!.En}.");
    }

    private void BulkOpen()
    {
        var n = sel.Count;
        foreach (var id in sel)
        {
            var s = Reopen(Pub(id));
            s.Page = deviation ? bulkPage : null; s.SentAt = now;
            published[id] = s;
        }
        if (sel.Contains(Station!)) draft = FreshDraft();
        sel.Clear();
        ShowToast($"Opened {n} counter{(n > 1 ? "s" : "")}.");
    }

    private void BulkClose()
    {
        var n = sel.Count;
        foreach (var id in sel) published[id] = CounterSetup.Closed();
        if (sel.Contains(Station!)) draft = FreshDraft();
        sel.Clear();
        ShowToast($"Closed {n} counter{(n > 1 ? "s" : "")}.");
    }

    private void ChangeActing()
    {
        screen = "rmu-pick"; rmuAirline = acting?.Airline; acting = null; rmuNotice = null;
    }

    private async Task PickRmuCounter(string id)
    {
        var airline = rmuAirline!;
        acting = (airline, id);
        rmuNotice = null;

        if (liveAirlineIds.TryGetValue(airline, out var airlineId))
        {
            try
            {
                var (templates, pickerTab) = await LiveData.GetPageOptionsAsync(airlineId, airline);
                liveTemplates = [.. templates];
                if (!string.IsNullOrWhiteSpace(pickerTab)) tabPref = pickerTab;
            }
            catch
            {
                liveTemplates = [];
            }

            try
            {
                liveApprovedMessages = [.. await LiveData.GetApprovedMessagesAsync(airlineId)];
            }
            catch
            {
                liveApprovedMessages = [];
            }
        }
        else
        {
            liveTemplates = [];
            liveApprovedMessages = [];
        }

        EnterApp();
    }

    private void StopIdle()
    {
        idleTimer?.Dispose();
        idleTimer = null;
        idle = null;
    }

    private async void ShowToast(string msg)
    {
        toast = msg;
        toastCts?.Cancel();
        var cts = toastCts = new CancellationTokenSource();
        try { await Task.Delay(3200, cts.Token); }
        catch (TaskCanceledException) { return; }
        toast = null;
        await InvokeAsync(StateHasChanged);
    }
}
