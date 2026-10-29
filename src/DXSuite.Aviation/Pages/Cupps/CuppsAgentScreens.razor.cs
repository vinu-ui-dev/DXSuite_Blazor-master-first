using Microsoft.AspNetCore.Components;
using static DXSuite.Aviation.Pages.Cupps.CuppsData;

namespace DXSuite.Aviation.Pages.Cupps;

// State and actions for the CUPPS agent screens prototype. Ported from the DXME-500 HTML prototype;
// all state is in memory for this circuit and nothing is saved or sent anywhere.
public partial class CuppsAgentScreens : IDisposable
{
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
    [Parameter] public string StartScenario { get; set; } = "agent";

    [Parameter] public string Title { get; set; } = "CUPPS Agent Screens";

    protected override void OnInitialized()
    {
        SetScenario(StartScenario);
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
    private Alloc? HolderNow(string id) => Allocations.FirstOrDefault(a => a.CounterId == id && a.From <= now && now < a.To);
    private Alloc? AllocFor(string? id, string code) => Allocations.FirstOrDefault(a => a.CounterId == id && a.Code == code && a.To > now);
    private bool IsWaiting => AllocFor(Station, MyAirline) is { } a && a.From > now;
    private CounterSetup? Pub(string? id) => id is not null && published.TryGetValue(id, out var p) ? p : null;
    private bool InApp => screen == "app";

    private List<Template> Usable() =>
        Templates.Concat(Many.Take(many > 0 ? many - 8 : 0))
            .Where(t => t.Scope == "airport" || t.Scope == MyAirline).ToList();

    /// <summary>Another airline's view swaps the operating carrier so the table reads as its own flights.</summary>
    private List<Flight> NextFlights(int n) => Flights.Where(f => f.Sched > now).Take(n).Select(f =>
    {
        var c = MyAirline;
        if (c == "WS") return f;
        var pt = Partners.GetValueOrDefault(c) ?? [];
        var shares = f.Carriers.Skip(1).Select(x => pt.TryGetValue(x.Airline, out var p) ? (p.Name, p.Code + x.No[2..]) : x);
        return f with { Carriers = [(Airlines[c].Name, f.Carriers[0].No.Replace("WS", c)), .. shares] };
    }).ToList();

    private record SplitInfo(bool Split, string? Other = null, bool SameAirline = false, bool OtherOpen = false);

    private SplitInfo GetSplit()
    {
        var c = Counters.FirstOrDefault(x => x.Id == Station);
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

    private static List<string> MessagesOf(CounterSetup s) =>
        [.. s.Msgs.Select(MsgText), .. (s.Text != "" ? new[] { s.Text } : [])];

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
        search = ""; message = null; endedReason = null; published = SeedPublished(); coreDown = false; draft = new();
    }

    private void SignIn()
    {
        if (Sc.Kind == "Individual") screen = "rmu-pick";
        else EnterApp();
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

    private void Publish()
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
        ShowToast($"Published to {Station}.{(saved ? " The typed Message also went for approval." : "")}");
    }

    private void CloseCounter()
    {
        published[Station!] = CounterSetup.Closed();
        draft = FreshDraft();
        confirmClose = false;
        ShowToast($"Closed {Station}.");
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

    private void PickRmuCounter(string id)
    {
        acting = (rmuAirline!, id); rmuNotice = null; EnterApp();
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
