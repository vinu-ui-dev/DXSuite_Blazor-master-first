using DXSuite.Aviation.Services.Cupps;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Services.Identity;
using Microsoft.AspNetCore.Components;
using static DXSuite.Aviation.Pages.Cupps.CuppsData;

namespace DXSuite.Aviation.Pages.Cupps;

// State and actions for the CUPPS agent screens. Everything is loaded from Director/CoreAPI through
// CuppsLiveDataProvider (airlines, allocations, page templates, approved Messages, flights, and what each counter
// currently shows), and every publish/open/close is written back to Director's counter-publish tables.
public partial class CuppsAgentScreens : IDisposable
{
    [Inject] private CuppsLiveDataProvider LiveData { get; set; } = null!;
    [Inject] private ISessionState SessionState { get; set; } = null!;
    [Inject] private Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider AuthProvider { get; set; } = null!;
    // Name/login of a user signed in from Director (no local user row): taken from the sign-in cookie.
    private string? directorLogin, directorName;
    [Inject] private IUserGroupService UserGroupService { get; set; } = null!;

    /// <summary>Signed-in users holding this role skip the RMU airline/counter picker entirely and land
    /// straight on their counter's terminal screen, the same as the "agent" demo scenario.</summary>
    private const string AirlineAgentRoleName = "Director Airline_Agent";

    /// <summary>Signed-in users holding this role see only their own airline in the RMU picker's airline
    /// grid, matching the same group-name-equals-airline-name rule Configuration > Airlines already uses.</summary>
    private const string AirlineSupervisorRoleName = "Airline Supervisor";

    private Dictionary<string, Airline> liveAirlines = [];
    private Dictionary<string, int> liveAirlineIds = [];
    private Dictionary<string, int> liveHolders = [];
    private Dictionary<string, Counter> liveCounters = [];
    private List<Alloc> liveAllocations = [];
    private List<Flight> liveFlights = [];
    private List<Template> liveTemplates = [];
    private List<Canned> liveApprovedMessages = [];
    private bool liveLoading;
    private bool liveLoadFailed;
    private bool liveAllocLoadFailed;

    private string scenario = "rmu";
    private string screen = "login";
    private int now = 630;
    private string tab = "mine";
    private bool coreDown;
    private bool deviation = true;
    private readonly bool fallback = false;
    private string tabPref = "Standard";
    private Dictionary<string, CounterSetup> published = [];
    private readonly Dictionary<string, Template> templateCache = new(StringComparer.OrdinalIgnoreCase);
    private Scenario? sc;
    private CounterSetup draft = new();
    private string? pickTab;
    private bool confirmClose;
    private bool waitingReady;
    private CounterSetup? pending;
    private HubMessage? message;
    private int? idle;
    private readonly HashSet<string> sel = [];
    private string zone = "";
    private string bulkPage = "";
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

    protected override async Task OnInitializedAsync()
    {
        if (!SessionState.SessionLoaded.Task.IsCompleted)
            await SessionState.SessionLoaded.Task;

        var auth = (await AuthProvider.GetAuthenticationStateAsync()).User;
        directorLogin = auth.Identity?.Name;
        directorName = auth.FindFirst("name")?.Value;

        SetScenario("rmu");

        // The page only renders for a user already signed in to DXSuite (directly or from Director), so there is
        // no second sign-in: go straight to the airline/counter step. Airline agents and supervisors are scoped to
        // their own airline and land on their counter when it's unambiguous.
        await SignIn();
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
    private Scenario Sc => sc ??= BuildScenario();
    private bool Can(string key) => Keys[Sc.Role].Contains(key);
    private string? Station => acting?.Counter ?? Sc.Station;
    private string MyAirline => acting?.Airline ?? Sc.Airline ?? "";

    /// <summary>The signed-in user as a CUPPS identity. Role decides which features are offered.</summary>
    private Scenario BuildScenario()
    {
        var role = SessionState.ActualRoleName;
        var kind = IsAirlineAgentRole(role) ? "Airline Agent"
            : string.Equals(role, AirlineSupervisorRoleName, StringComparison.OrdinalIgnoreCase) ? "Airline Supervisor"
            : "RMU";
        var user = SessionState.UserName ?? directorLogin ?? "";
        var who = SessionState.User?.FriendlyName is { Length: > 0 } f ? f
            : !string.IsNullOrWhiteSpace(directorName) ? directorName : user;
        return new Scenario("Individual", user, who, kind, null, now);
    }

    private static bool IsAirlineAgentRole(string? role) =>
        string.Equals(role, AirlineAgentRoleName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Airline Agent", StringComparison.OrdinalIgnoreCase);

    /// <summary>Airline agents and supervisors only ever work for their own airline.</summary>
    private bool IsAirlineScoped =>
        IsAirlineAgentRole(SessionState.ActualRoleName)
        || string.Equals(SessionState.ActualRoleName, AirlineSupervisorRoleName, StringComparison.OrdinalIgnoreCase);

    private List<Alloc> AllAllocs => liveAllocations;

    private List<Counter> CounterList => [.. liveCounters.Values.OrderBy(c => c.Id)];

    private List<string> ZoneList => [.. liveCounters.Values.SelectMany(c => c.Zones.Skip(1)).Distinct().Order()];

    private List<Canned> MessageList => liveApprovedMessages;

    /// <summary>A page template by id: the acting airline's own page options first, then any template seen on a
    /// counter's persisted state (so another counter's tile can still show its page name).</summary>
    private Template? Tpl(string? id) =>
        id is null ? null : liveTemplates.FirstOrDefault(t => t.Id == id) ?? templateCache.GetValueOrDefault(id);

    /// <summary>The first Standard page the airline can use - what a bare "Open" publishes.</summary>
    private string? DefaultPage() =>
        Usable().FirstOrDefault(t => t.Kind == "Standard")?.Id ?? Usable().FirstOrDefault()?.Id;

    private Alloc? HolderNow(string id) => AllAllocs.FirstOrDefault(a => a.CounterId == id && a.From <= now && now < a.To);
    private Alloc? AllocFor(string? id, string code) => AllAllocs.FirstOrDefault(a => a.CounterId == id && a.Code == code && a.To > now);
    private bool IsWaiting => AllocFor(Station, MyAirline) is { } a && a.From > now;
    private CounterSetup? Pub(string? id) => id is not null && published.TryGetValue(id, out var p) ? p : null;
    private bool InApp => screen == "app";

    /// <summary>Airline metadata for a code, from Director's airline accounts; a generic entry if unknown.</summary>
    private Airline AirlineInfo(string code) =>
        liveAirlines.TryGetValue(code, out var live) ? live : new Airline(code, code, "#555");

    /// <summary>A counter's zone/SSBD metadata, from the counters that appear in today's allocations.</summary>
    private Counter? CounterInfo(string? id) =>
        id is not null && liveCounters.TryGetValue(id, out var live) ? live : null;

    private List<Template> Usable() =>
        liveTemplates.Where(t => t.Scope == "airport" || t.Scope == MyAirline).ToList();

    /// <summary>The next departures from CoreAPI; the acting airline's own (operated or codeshared) first.</summary>
    private List<Flight> NextFlights(int n)
    {
        var upcoming = liveFlights.Where(f => f.Sched > now).ToList();
        var mine = upcoming.Where(f => OperatesOrShares(f, MyAirline)).Take(n).ToList();
        return mine.Count > 0 ? mine : upcoming.Take(n).ToList();
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
        var oOpen = Pub(c.Split)?.Open == true;
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
        bulkPage = DefaultPage() ?? "";
    }

    private void SetScenario(string key)
    {
        StopIdle();
        scenario = key; sc = null; screen = "login"; acting = null; rmuAirline = null; rmuNotice = null;
        search = ""; message = null; endedReason = null; published = []; templateCache.Clear(); coreDown = false; draft = new();
        deviation = true;
        liveAirlines = []; liveAirlineIds = []; liveCounters = []; liveAllocations = []; liveFlights = []; liveTemplates = []; liveApprovedMessages = []; liveLoading = false; liveLoadFailed = false;
    }

    /// <summary>After "Signed out": back to the airline/counter step (the user is still signed in to DXSuite).</summary>
    private async Task RestartAsync()
    {
        SetScenario(scenario);
        await SignIn();
    }

    private async Task SignIn()
    {
        StopIdle();
        screen = "rmu-pick";
        await LoadLiveCuppsDataAsync();
        await AutoSelectOwnCounterAsync();
    }

    /// <summary>For an airline agent/supervisor scoped to exactly one airline: skip the airline step, and if that
    /// airline holds exactly one counter right now, skip the counter step too.</summary>
    private async Task AutoSelectOwnCounterAsync()
    {
        if (!IsAirlineScoped || liveLoadFailed || liveAirlines.Count != 1) return;
        var code = liveAirlines.Keys.First();
        rmuAirline = code;

        // The airline's counters: those allocated right now, plus any the publish state says it holds.
        // Open counters come first; if none is open, a closed one is shown. This also works when the
        // allocation table can't be read, since open/closed and holder come from the publish state.
        var held = AllAllocs
            .Where(a => a.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && a.From <= now && now < a.To)
            .Select(a => a.CounterId).ToList();
        if (liveAirlineIds.TryGetValue(code, out var airlineId))
            held.AddRange(liveHolders.Where(kv => kv.Value == airlineId).Select(kv => kv.Key));
        held = [.. held.Distinct(StringComparer.OrdinalIgnoreCase)];

        var open = held.Where(c => published.TryGetValue(c, out var s) && s.Open).ToList();
        var pick = open.Count > 0 ? open : held;
        if (pick.Count >= 1 && (pick.Count == 1 || open.Count > 0))
            await PickRmuCounter(pick[0]);
        else
            StateHasChanged();
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
            BuildLiveCounterMap();
        }
        catch (Exception)
        {
            liveAllocLoadFailed = true;
            liveAllocations = [];
            liveCounters = [];
        }

        try
        {
            liveFlights = [.. await LiveData.GetTodaysDeparturesAsync()];
        }
        catch (Exception)
        {
            liveFlights = [];
        }

        try
        {
            var states = await LiveData.GetCounterStatesAsync();
            published = states.Published;
            liveHolders = states.HolderAirlineIds;
            foreach (var (id, t) in states.Templates) templateCache[id] = t;
        }
        catch (Exception)
        {
            published = [];
        }

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
        if (!IsAirlineScoped)
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
    private int? MyAirlineId => liveAirlineIds.TryGetValue(MyAirline, out var id) ? id : null;

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
        if (Station is null || MyAirlineId is not { } airlineId) return;

        var tpl = Usable().FirstOrDefault(t => t.Id == draft.Page);
        var (ok, error) = await LiveData.PublishCounterAsync(
            Station, airlineId, draft.Page, tpl?.Kind, tpl?.En, draft.Dest, draft.Sunflower, draft.Asl);
        if (!ok)
            ShowToast($"{Station} is updated here but Director wasn't saved: {error ?? "unknown error"}.");
    }

    private async Task CloseCounter()
    {
        published[Station!] = CounterSetup.Closed();
        draft = FreshDraft();
        confirmClose = false;

        if (Station is not null)
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

    /// <summary>Writes one counter's new state through to Director: a publish when it's open, a close otherwise.
    /// The screen already reflects the change in <see cref="published"/>; a failure only means Director has no
    /// durable record of it, shown as a toast.</summary>
    private async Task PersistCounterAsync(string id, CounterSetup s)
    {
        if (!s.Open)
        {
            var (closed, closeError) = await LiveData.CloseCounterAsync(id);
            if (!closed) ShowToast($"{id} is closed here but Director wasn't saved: {closeError ?? "unknown error"}.");
            return;
        }

        var holder = HolderNow(id)?.Code ?? MyAirline;
        if (!liveAirlineIds.TryGetValue(holder, out var airlineId)) return;
        var tpl = Tpl(s.Page);
        var (ok, error) = await LiveData.PublishCounterAsync(id, airlineId, s.Page, tpl?.Kind, tpl?.En, s.Dest, s.Sunflower, s.Asl);
        if (!ok) ShowToast($"{id} is updated here but Director wasn't saved: {error ?? "unknown error"}.");
    }

    private async Task TileOpen(string id, bool on)
    {
        var s = on ? new CounterSetup { Open = true, Page = deviation ? DefaultPage() : null, SentAt = now } : CounterSetup.Closed();
        published[id] = s;
        if (id == Station) draft = FreshDraft();
        ShowToast($"{(on ? "Opened" : "Closed")} {id}.");
        await PersistCounterAsync(id, s);
    }

    private async Task PickChangePage(string page)
    {
        var id = changePage!;
        var s = Reopen(Pub(id));
        s.Page = page; s.SentAt = now;
        published[id] = s;
        if (id == Station) draft = FreshDraft();
        changePage = null;
        ShowToast($"{id} now shows {Tpl(page)?.En ?? "the new page"}.");
        await PersistCounterAsync(id, s);
    }

    private async Task BulkOpen()
    {
        var n = sel.Count;
        var changed = new List<(string Id, CounterSetup Setup)>();
        foreach (var id in sel)
        {
            var s = Reopen(Pub(id));
            s.Page = deviation ? bulkPage : null; s.SentAt = now;
            published[id] = s;
            changed.Add((id, s));
        }
        if (sel.Contains(Station!)) draft = FreshDraft();
        sel.Clear();
        ShowToast($"Opened {n} counter{(n > 1 ? "s" : "")}.");
        foreach (var (id, setup) in changed) await PersistCounterAsync(id, setup);
    }

    private async Task BulkClose()
    {
        var n = sel.Count;
        var ids = sel.ToList();
        foreach (var id in ids) published[id] = CounterSetup.Closed();
        if (sel.Contains(Station!)) draft = FreshDraft();
        sel.Clear();
        ShowToast($"Closed {n} counter{(n > 1 ? "s" : "")}.");
        foreach (var id in ids) await PersistCounterAsync(id, CounterSetup.Closed());
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
