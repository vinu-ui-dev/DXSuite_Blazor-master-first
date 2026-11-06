# CUPPS Agent Screens — How It Works

A walkthrough of `src/DXSuite.Aviation/Pages/Cupps/`, for understanding how the screens are built,
how variables are bound to the UI, and how data flows through the component.

## 1. What this is

CUPPS ("Common Use Passenger Processing System") is the check-in counter software airport agents
use to put a page (Check-In, Bag Drop, a sale banner, etc.) and Messages on a counter's screen.
This folder is a **prototype** (ticket DXME-500) of the agent-facing screens — everything except
the RMU airline/counter picker runs on data held in memory for the current circuit. Nothing is
saved to a database and nothing is sent anywhere.

Three files do all the work:

| File | Role |
|---|---|
| `CuppsData.cs` | Static demo data (records, dictionaries, seed lists) + small formatting helpers |
| `CuppsAgentScreens.razor.cs` | All state (fields) and all actions (methods) — the "view model" |
| `CuppsAgentScreens.razor` | Markup, built almost entirely from C# methods that return `RenderFragment` |
| `CuppsRmuStaff.razor` | A thin page (`/cupps-rmu-staff`) that just renders `<CuppsAgentScreens StartScenario="rmu" />` |
| `Services/Cupps/CuppsLiveDataProvider.cs` | The *only* part wired to real data — fetches airlines/allocations/flights from CoreAPI for the RMU picker |

Routes: `/cupps-agent-screens` (agent/supervisor demo scenarios) and `/cupps-rmu-staff` (RMU sign-in flow).

## 2. The data model (`CuppsData.cs`)

Everything is a C# `record` or a small mutable `class`:

- `Airline(Code, Name, Bg, Alliance)` — airline branding
- `Counter(Id, Zones, Split, Ssbd)` — a physical counter; `Split` names the counter id it shares a
  screen with (e.g. G2-1 and G2-2 share one physical display)
- `Alloc(CounterId, Code, From, To, Type)` — "airline X owns counter Y from time A to time B"; this
  is what CoreAPI would hand over in real life (`Type` is `Common | Dedicated | Info`)
- `Template(Id, Kind, Scope, En, Fr, EndsToday, Img)` — a page an agent can publish; `Kind` is
  `Standard` or `Custom`, `Scope` is `"airport"` (anyone can use it) or an airline code (only that
  airline sees it)
- `Canned(Id, En, Fr)` — a pre-approved ticker Message
- `Flight(...)` — a departure row for the flights table
- `Scenario(Kind, User, Who, Role, Station, Now, Airline)` — a named demo starting point (see §5)
- **`CounterSetup`** — the one mutable class; it is **what a counter's screen currently shows**:
  `Open`, `Page` (a Template id), `Msgs` (up to 2 canned Message ids), `Text` (free-typed Message),
  `Asl`, `Sunflower`, `Dest`, `Layout`, `SentAt`. It has `.Clone()` (always clone before mutating a
  copy — never hand out a reference to a stored `CounterSetup`) and `.Closed()`.

All the `Allocations`, `Counters`, `Templates`, `CannedMessages`, `Flights`, `Scenarios` fields are
`static readonly` seed data — think of them as a fake database loaded once per process.

`SeedPublished()` builds the starting "what's already on every screen" dictionary
(`Dictionary<string, CounterSetup>` keyed by counter id) — this is the in-memory stand-in for
"what Director last published."

## 3. State: Draft vs Published — the core pattern

This is the single most important idea in the whole prototype, so understand this before anything else.

```
published : Dictionary<string, CounterSetup>   // what every counter's screen is currently showing
draft      : CounterSetup                       // what the agent is editing for THEIR counter, unsent
```

- `published[id]` is the source of truth for counter `id`'s screen. It only changes when something
  is actually "sent" (`Publish()`, `TileOpen()`, `BulkOpen()`, `CloseCounter()`, `PickChangePage()`).
- `draft` is a **separate, cloned** `CounterSetup` the agent is actively editing. Every toggle,
  page pick, and Message selection on the "My Counter" tab mutates `draft`, not `published`.
- `FreshDraft()` clones `published[Station]` into `draft` whenever you enter the app, switch
  counters (via RMU), or the published state is replaced from under you (close/reopen).
- `IsDirty()` compares `draft` against what the screen currently shows, field by field, to decide
  whether to show the orange "Not published" badge and enable/disable the Publish button.
- `Publish()` clones `draft`, stamps `SentAt = now`, and writes it into `published[Station]` —
  this is the only place "editing" becomes "live."
- `DraftPreview()` is a read-only projection of `draft` *as if* it had been published, used to
  render the live preview (`SentBox()`, the "View Sent" modal) before the agent actually publishes.

Why clone instead of just storing one object? Because `draft` and `published[Station]` must be
able to diverge — the agent can make changes, see a preview, and *not* publish them yet. If they
were the same reference, every keystroke would already be "live."

## 4. Blazor binding mechanics used here

### 4.1 Two-way `@bind` on primitives
```razor
<input class="dx-form-control" @bind="draft.Dest" @bind:event="oninput" placeholder="For example Cancun">
```
- `@bind="draft.Dest"` wires the input's value to the `draft.Dest` string field both ways.
- `@bind:event="oninput"` makes it update on every keystroke (Blazor's default for `<input>` is
  `onchange`, i.e. only on blur/Enter). The project uses `oninput` wherever typing should be felt
  immediately — free-text Messages, destination, search boxes.
- Same pattern for `<select>` (`@bind="zone"`, `@bind="bulkPage"`) and checkboxes
  (`@bind="draft.SaveText"`).

Because `draft` is a plain mutable field on the component, `@bind` works directly against its
properties (`draft.Dest`, `draft.Text`) — no wrapper view-model or `EditForm` is needed since this
isn't a validated form, just live prototype state.

### 4.2 Custom toggle binding — the `Action<bool> set` pattern
There's no built-in two-way bindable toggle component here, so booleans that aren't plain
`<input>` elements use a small delegate pattern instead of `@bind`:

```csharp
private RenderFragment Switch(bool on, Action<bool> set, string label, bool disabled = false) =>
    @<button type="button" role="switch" class="dx-toggle cp-switch" aria-checked="@(on ? "true" : "false")"
             disabled="@disabled" @onclick="() => set(!on)">
        <span class="dx-toggle-slider @(on ? "on" : "")"></span>
    </button>;
```
Call sites pass the *current value* and a lambda that writes it back:
```csharp
@Toggle(draft.Asl, v => draft.Asl = v, "ASL Videos")
@Toggle(draft.Sunflower, v => draft.Sunflower = v, "International Sunflower")
```
This is effectively manual two-way binding: `Switch` reads `on` to decide how to render, and
`@onclick` calls `set(!on)` which runs the caller's lambda to flip the real field. It's the same
idea as passing `Value` + `ValueChanged` to a child component, just without an actual child
component — `Switch`/`Toggle` are just functions that return markup (see §5).

### 4.3 Click handlers (`@onclick`)
Every action in the UI is a method or lambda wired with `@onclick`:
```razor
<button @onclick="Publish">Publish</button>
<button @onclick="() => draft.Page = t.Id">...</button>
<button @onclick="() => SetTab(k)">...</button>
```
Plain method group (`Publish`) when no arguments are needed; a lambda when a parameter (like a
loop variable `t.Id` or `k`) needs to be captured.

### 4.4 Conditional attributes
`disabled="@(...)"`, `class="@(cond ? "a" : "b")"`, `style="@bg"` — ordinary Razor expressions
inside attributes. Nothing special, but worth noticing how much of the UI logic (what's clickable,
what's highlighted) lives in these inline expressions rather than in `@code`.

## 5. Why everything is a `RenderFragment` method, not a sub-component

Instead of splitting the UI into `.razor` child components with `[Parameter]`s, almost the entire
file is private methods like:
```csharp
private RenderFragment SentBox() { ... return @<div class="cp-sent">...</div>; }
```
and the template calls them like tags: `@SentBox()`.

This is a deliberate, if unusual, choice for a single-circuit prototype:
- **No parameter plumbing.** A real child component would need `[Parameter] CounterSetup Draft`,
  `[Parameter] EventCallback<...> OnChange`, etc. A private method just closes over `this` and
  reads/writes the component's fields directly — fewer moving parts for logic that only this page
  ever uses.
- **Everything re-renders together anyway.** These aren't meant to skip re-renders independently
  (unlike real components with `ShouldRender()`); the whole page is one component, so this is
  purely a markup-organization technique, not a performance one.
- **Trade-off:** none of these pieces are independently testable with bUnit and none of them
  memoize — acceptable here because it's explicitly a throwaway prototype, but **not** the pattern
  to copy for production features (see the global Blazor guidance on splitting into real
  components + `ShouldRender()`).

`Mark`, `Half`, `Band`, `ScreenFor`, `TextSummary`, `Navbar`, `CounterBand`, `Alerts`, `TabRow`,
`SentBox`, `PageOptions`, `SharedNote`, `Destination`, `Switch`/`Toggle`, `Switches`,
`MessageRows`, `MyMessageSummary`, `FlightsTable`, `MyCounter`, `MessagesTab`, `CounterTile`,
`AllCounters`, `ChangePageModal`, `AppScreen`, `LoginBrand`, `LoginScreen`, `AirlineTile`,
`RmuCounterTile`, `RmuPick`, `EndedScreen` — each is one visual region; `AppScreen()` assembles the
top-level ones for the signed-in app, and the top of the `.razor` file's `@switch (screen)` picks
which whole screen to show.

## 6. Screen flow (the `screen` field)

```
"login" ──SignIn()──> "rmu-pick" (only for Individual/RMU scenarios) ──PickRmuCounter()──> "app"
"login" ──SignIn()──> "app"                                            (agent/supervisor scenarios)
"app"   ──SignOut()──> "ended" ──"Back to sign in"──> "login"
```
- `screen` is a plain string field; the `.razor` file's `@switch (screen)` chooses `LoginScreen()`,
  `RmuPick()`, `EndedScreen()`, or the default `AppScreen()`.
- `OnInitialized()` calls `SetScenario(StartScenario)`; non-Individual scenarios skip straight to
  `EnterApp()` (counter agents don't need the airline/counter picker — they're pre-assigned).
- Inside `AppScreen()`, a **second** switch on the `tab` field (`"mine" | "all" | "flights" |
  "messages"`) picks the content below the tab row — `MyCounter()`, `AllCounters()`,
  `FlightsTable()`, or `MessagesTab()`.

## 7. Key derived (computed) properties

These are `private X Foo => ...` expression-bodied properties — not stored fields — recomputed
every render from the current state:

- `Sc` → the current `Scenario` record (`Scenarios[scenario]`)
- `Station` → `acting?.Counter ?? Sc.Station` (an RMU "acting for" override takes priority)
- `MyAirline` → same idea, defaults to `"WS"`
- `Live` → `scenario == "rmu"` — switches several other properties (`AllAllocs`, `CounterList`,
  `MessageList`) between the live-fetched data and the static `CuppsData` demo data
- `IsWaiting` → true if the agent's allocation hasn't started yet (`Publish` becomes "Ready for
  HH:MM" instead of publishing immediately — see `waitingReady`/`pending`)
- `GetSplit()` → figures out if this counter shares a physical screen with another counter and
  whether that other counter belongs to the same airline (drives `SharedNote()` and the two-halves
  layout in `ScreenFor()`)
- `IsDirty()` → see §3
- `Can(key)` → simple role-based feature flag check against the `Keys` dictionary (e.g.
  `"feature:cupps-free-text"` is only available to Supervisor/RMU roles)

## 8. Live data vs static data

Only the **RMU airline/counter picker** (`scenario == "rmu"`) talks to anything real:

```
LoadLiveCuppsDataAsync()
 ├─ CuppsLiveDataProvider.GetOperatingAirlinesAsync()   → CoreAPI arrivals/departures/airlines
 ├─ CuppsLiveDataProvider.GetLiveAllocationsAsync()     → CounterAllocationService
 └─ CuppsLiveDataProvider.GetTodaysDeparturesAsync()    → CoreAPI departures
```
These populate `liveAirlines`, `liveAllocations`, `liveFlights`, and replace `now` with the real
current time (in airport-local minutes-since-midnight). Every other scenario (`agent`, `ac`,
`super`, `split2`, `splitboth`, `waiting`) keeps using `CuppsData`'s static seed lists untouched —
look at `AllAllocs`, `CounterList`, `MessageList`, `AirlineInfo()`, `CounterInfo()` to see the
`Live ? liveX : staticX` fallback pattern used throughout.

## 9. Small mechanisms worth knowing

- **Toast**: `ShowToast(msg)` sets `toast`, starts a `CancellationTokenSource`-backed 3.2s delay,
  then clears it and calls `StateHasChanged()`. Calling `ShowToast` again cancels the previous
  timer so toasts don't stack.
- **Ticker/clock cycling**: a `System.Threading.Timer` (`cycleTimer`) ticks every 3s in
  `OnAfterRender(firstRender)`, incrementing `tick` and calling `StateHasChanged()` via
  `InvokeAsync` (required because the timer callback runs off the Blazor sync context). `Cycle()`
  uses `tick % seq.Count` to rotate through Messages/codeshares.
- **Idle timeout modal**: `idle` (an `int?` seconds-remaining) and `idleTimer` drive the "Still
  there?" modal; `StopIdle()` disposes the timer and clears `idle`.
- **`IDisposable`**: the component disposes `cycleTimer`, `idleTimer`, and cancels `toastCts` —
  required because `@rendermode InteractiveServer` keeps the circuit (and these timers) alive
  until the component is torn down; leaking a `Timer` here would keep firing against a disposed
  component.

## 10. If you want to change something

- **Add a new Template/page**: add a row to `CuppsData.Templates` (or `Many` for the "big list"
  demo). `Scope: "airport"` = visible to everyone; `Scope: "WS"` (etc.) = only that airline.
- **Add a new canned Message**: add to `CuppsData.CannedMessages`.
- **Add a new demo scenario**: add an entry to `CuppsData.Scenarios` and route to it via
  `StartScenario` (see `CuppsRmuStaff.razor` for the pattern) or wire a selector into `screen`.
- **Change what a toggle controls**: find its `@Toggle(currentValue, v => field = v, "label")` call
  site — the three arguments are the only thing you need to touch.
- **Change Publish behavior**: everything funnels through `SendDraft()`/`Publish()` in
  `CuppsAgentScreens.razor.cs` — that's the only place that writes to `published`.
