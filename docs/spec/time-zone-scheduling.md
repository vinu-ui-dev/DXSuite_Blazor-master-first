# Time zone correct scheduling across DXSuite

The handoff spec for making DXSuite schedule content in each player's own local
time. Assembled under
[DXME-201](https://radianttechnology.atlassian.net/browse/DXME-201) from the
closed tickets on the
[DXME-159](https://radianttechnology.atlassian.net/browse/DXME-159) wayfinder
map, the same way
[DXME-132](https://radianttechnology.atlassian.net/browse/DXME-132) assembled the
Engage for BrightSign spec from
[DXME-120](https://radianttechnology.atlassian.net/browse/DXME-120).

**This is the document a builder implements from.** The map at
[`docs/maps/dxme-159-time-zone-scheduling.md`](../maps/dxme-159-time-zone-scheduling.md)
is an index and behaves like one: 34 decisions, several of which overturn
earlier ones in place, pointing at 20 closed child tickets for the detail. The
originating defect is
[DXME-158](https://radianttechnology.atlassian.net/browse/DXME-158), which is
where the build happens.

**Every section cites the ticket that decided it. The ticket's resolution comment
holds the reasoning; this document holds the decision.** Where a later ticket
overturned an earlier one, only the surviving decision appears here, with the
correction noted so nobody reopens it from the older ticket. That matters more
here than it did on DXME-120, because more was overturned: the time zone moved
off the display onto the device, the cache key lost the time zone it had been
given, and the DST rule turned out to have no consumer at all.

Vocabulary is [`CONTEXT.md`](../../CONTEXT.md), section "Scheduling". Terms in
_italics_ on first use are defined there. The distinctions that trip people up:
an _instant_ is a moment and is stored as UTC, while a _wall-clock bound_ is the
edge of a schedule and carries no time zone; _publication_ is what a location
publishes and _delivery_ is what one device should be showing now; _acquisition_
is how a time zone gets onto a row and _resolution_ is which clock a window is
read against, and conflating those two reintroduces the defect this whole effort
exists to remove.

This document meets
[`src/DXSuite.Host/docs/engage-for-brightsign-hub.md`](../../src/DXSuite.Host/docs/engage-for-brightsign-hub.md)
at one point only, the `osTimeZone` field in section 7. The player half of that
field, meaning what a BrightSign player does with a time zone it has been sent,
is owned by the DXME-120 map and is not in scope here.

**Section 13 lists what is open.** One item is a real precondition. The rest
block nothing.

---

## 1. The shape of the work

Six things change, and a seventh is deliberately left alone.

1. **A device carries its own time zone.** A new non-nullable
   `tblDevice.TimeZone`, IANA, set by a person in Director. Section 2.
2. **One window rule replaces five.** A pure `DailyWindow.Contains(TimeOnly)`
   that reads no clock. Section 3.
3. **The server becomes the only consumer.** It resolves every window against
   the requesting device's stored time zone and ships the four window fields as
   null, which disables the player's own filter. Section 4.
4. **All three manifest caches move their boundary.** They cache the
   publication, which no clock touches, and resolve the delivery per request.
   Section 5.
5. **Failure gets a shape.** An identifier that does not resolve fails toward
   the path's own default. A host that cannot resolve IANA at all refuses to
   start. Section 6.
6. **A confirmed time zone is pushed to the player and verified on the way
   back.** Sections 7 and 8.

The seventh, which does not change: **the storage model.** `TimeOnly` plus
`DateTime` with no offset is the correct representation of "respect the player's
local time". The defect is in who evaluates and against which clock, never in
what is stored.

### The deployment this serves

_DXME-163._ One Director deployment, one Playlist location managing every screen
of one type across every service center, spanning Central, Mountain and Arizona
simultaneously, at roughly 90 players. **Requiring a display row per time zone,
or a playlist per time zone, is not an acceptable outcome.** The authoring
surface stays single and each device's own time zone is respected.

Arizona is the case that cannot be got right by luck. It does not observe
daylight saving, so it tracks Mountain in winter and Central in summer, and no
stored offset is correct year-round.

### Two constraints that bind every decision below

**This is an interim measure.** Device-driven time zones are the intended
destination and Director-set is the temporary path. Every modelling choice here
is one that discovery can later write into **without re-modelling**. Nothing is
built whose only justification is that a person is typing the values today.

**Minimum footprint.** _Set by the user during DXME-165._ A site changes only if
a wrong answer there is visible on a screen. Everything else is documented as
server-clock and left alone. The cost is that the codebase keeps two conventions
for a while, and that is acceptable only because the boundary is written down:
every out-of-scope item on the map carries a reopening condition, so the next
reader does not mistake an untouched site for the intended pattern.

Read the preference as **do not touch a working surface for consistency**, not
as do not fix anything invisible. Section 12 carries two deliberate carve-outs
that fix invisible things, both single tokens with no behavioural reach.

---

## 2. The storage model

_Decisions 2, 11, 20, 21 and 31. Settled by DXME-163, DXME-168 and DXME-192._

### Where it lives

**`tblDevice.TimeZone`**, `varchar(50)`, `[Unicode(false)]`, **non-nullable**,
holding an IANA identifier such as `America/Chicago` or `America/Phoenix`.

This **overturns** the map's original decision 2, which put the time zone on the
display. DXME-163 established why neither of the alternatives is a place: a
DXSuite _Location_ is a content role (`Weather`, `Airline Logos`,
`Center Content`) and a _Display_ is a canvas format (`Landscape`,
`Portrait 1`). `tblDevice` is the only row in the schema that is unambiguously
somewhere, and it is exactly what device-driven discovery later writes into.

**`tblDisplay.TimeZone` is dropped.** It is read by nothing, and its 240 dev rows
hold three incompatible spellings of one deployment's zone: 225
`America/New_York` from the model default at `DirectorDbContext.cs:354`, 8
`Eastern Standard Time`, and 7 `UTC` from its only writer at
`LocationEditModal.razor:979`. The `Down` migration is lossy and that is fine,
because there is no information in those rows to preserve.

**A plain column, not an EAV row, not a key in a JSON blob, not a mapping
table.** Bulk assignment is a script against `tblDevice.TimeZone`, which is what
keeps a later discovery mechanism cheap: one column to write.

### Never unset

`tblDevice` rows are created automatically on first full heartbeat with no
operator present, so an unset state would be guaranteed on day one. The column is
seeded at row creation with the Director host's time zone **converted to IANA**,
and is never null, so no evaluator carries an unset branch.

A device that is never commissioned plays on the server's clock. That is
accepted, and it is not the same thing as a runtime fallback: the wrong value is
queryable, and one `UPDATE` fixes a whole service center.

### Provenance: `tblDevice.TimeZoneSource`

_Decision 31, settled by DXME-192._ A second non-nullable column carrying
`Seeded` or `Operator`, with `Discovered` reserved and **deliberately not
shipped**.

It governs exactly one thing: **whether the value is good enough to send out to a
device** (section 7). **No evaluator reads this column.** The server still
resolves every window against `tblDevice.TimeZone` whatever its provenance.

This is not decision 12's deleted unset branch returning. Confirmation is an
_acquisition-path_ fact; never-unset is a _resolution-path_ guarantee.

**Who writes what, stated per writer**, because a single-exception rule breaks
the moment discovery exists:

| Writer | Value |
| --- | --- |
| The seed helper (section 10) | `Seeded` |
| Discovery, when it arrives | `Discovered` |
| **Every other write path** | `Operator` |

"Every other" includes the `DevicesTab` cell, the bulk SQL script of decision 11,
and the upgrade backfill of section 10. **Counting the backfill as confirming is
load-bearing**: those are upgraded single-zone deployments where a human did
choose the value, and without it they would stay permanently unconfirmed and
never push.

**Provenance rather than a `TimeZoneConfirmedUtc` timestamp.** A timestamp cannot
say what confirmed it, so discovery would have to stamp itself confirmed or earn
a second column. Same one-column footprint, and provenance is the shape discovery
writes into without re-modelling. Both live consumers only ask which value it is,
never when.

**The honest cost.** Nothing validates the column (section 6), so a careless
script can write a real zone and leave `Seeded` behind. The symptom is a device
that quietly never gets pushed, which is the safe direction and also the
invisible one.

### Sites

_Decision 18, settled by DXME-167, which widened the map's destination at the
user's direction._ A 90-device list is unnavigable without somewhere to record
where a device physically is, and every other home for that was wrong.

`tblSite`: `SiteId`, `CustomerId` for tenant scope, a required `SiteName`, and
nullable `Address1`, `Address2`, `City`, `StateProvince`, `PostalCode`,
`Country`. `tblDevice` gains a nullable `SiteId`, many devices to one site.
`DeleteBehavior.SetNull`, matching `FK_tblDevice_TargetAppVersion`, because
deleting a site must never delete players. `SiteName` is **not** unique per
customer. `IX_tblDevice_SiteId` is added.

All of it optional, because an airport has no use for it.

**Two binding clauses.**

1. **`tblSite` carries no time zone.** A site entity is exactly what DXME-163
   examined and rejected as a time zone home. Once `tblSite` exists, "surely the
   time zone belongs here" becomes the obvious-looking refactor. Taking it
   reintroduces the split DXME-163 spent a whole ticket killing, and breaks
   device-driven discovery.
2. **Nothing derives a time zone from the address.** No state or postal-code
   lookup. Decision 3 forbids it, and Arizona is exactly where it would be
   wrong.

Note the asymmetry: a device will one day report its own time zone, but **no
device will ever report its own street address**. Site assignment is manual on
every deployment, permanently.

---

## 3. The window rule

_Decision 10, settled by DXME-164 and resequenced by DXME-165._

### The surviving semantic

A **daily window** is a start and end time of day that recurs every day.

```
readonly record struct DailyWindow(TimeOnly? Start, TimeOnly? End)
{
    bool Contains(TimeOnly localTime);
}
```

* Start **inclusive**, end **exclusive**.
* A start later than the end **wraps midnight**.
* An end at or after **23:59 means through the end of the day**, because the
  authoring UI is minute-granular and cannot express 23:59:59.
* A missing start means 00:00, a missing end means end of day, both missing means
  always open.

The wrap rule and the 23:59 rule are mutually exclusive by construction: wrapping
needs a start later than the end, which no end at or after 23:59 permits. State
that flatly, because it is the only thing keeping the two exceptions from
interacting.

**It is pure.** It reads no clock, takes no time zone, and has no DST behaviour.
That is why no `TimeProvider` or `IClock` seam is needed anywhere in this effort,
and why decision 6's DST rule has no consumer (section 6).

The name `ScheduleWindow` was rejected because `IScheduleWindow` already exists
and means something adjacent. All five current implementations live in one
assembly, `DXSuite.Director.Services`, so the collapse needs no new project
reference.

### One window model, one type

`TblAccessSchedule.cs:45,48`, `TblFloorPlanIconSchedule.cs:51,54` and
`IScheduleWindow` itself use `TimeSpan?`, against `TblScheduleEvent.cs:32,34` and
`TblOverride.cs:34,36` on `TimeOnly?`. **Same model, one type.** `DailyWindow`
takes `TimeOnly`, and those tables convert at the edge, rejecting out-of-range
values loudly rather than clamping. `TimeSpan` is a wider type carrying a
narrower meaning, and that width is already causing divergence.

### Which implementations collapse, and when

**Four collapse with this work:**

| Implementation | Behaviour delta |
| --- | --- |
| `ScheduleTime.cs:14` (9 call sites) | A wrapping window loses its final instant. Zero wrapping rows in dev. Its wrap branch is currently inclusive, and its 23:59 branch currently discards the end bound entirely. |
| `ClusterResolutionService.IsTimeActive` (`:205`, one caller) | **Gains midnight wrap.** Overnight allocation events are permanently inactive today and will start firing. Zero such rows in dev. **Check a customer database before shipping.** |
| `StatusSelectorService.cs:412` inline | Loses the final second of a bounded window, gains the final 60 seconds of an all-day one. The class then uses one semantic in both its paths. |
| `AirlineStateResolver.IsActiveAt` (`:40`) | End becomes exclusive. The default end changes from a literal inclusive `23:59` to end of day, a **net gain of 60 seconds**. "Missing or unparseable means inactive" is preserved but moves into an explicit branch in `PickActive`, and starts logging. |

**One waits:** `ScheduleEvaluator` (8 call sites via `IsActive`) is deferred to
[DXME-176](https://radianttechnology.atlassian.net/browse/DXME-176), off-map.

It currently returns `true` when either bound is missing, so **a start-only
window is permanently open**, and four of its eight call sites are access
control. Collapsing it is a fail-open to fail-closed change, and bundling that
into a time zone fix means a rollback for a time zone bug also reopens the access
windows. It is unreachable from the player path
(`SignageController` injects `ILocationAccessService` but calls only
`GetMetadataFilterKeyForLocationAsync` and `HasAccessRulesAsync`, both plain EF
queries with no time predicate), so deferring it costs the player nothing.

### The player's copy

`displayManager.ts`'s `isAssetScheduled` is **live and load-bearing today**, and
its semantic disagrees with the rule above: it uses `currentTime > endTime`, so
end inclusive, and it has **no midnight-wrap branch**. It is called from three
sites (`:275`, `:1372`, `:1436`).

**It is not being fixed.** Decision 5's server-side nulling (section 4) makes it
unreachable, and fixing it first would mean a release on two fleets to correct
code this same effort is about to make dead.

> **Reopening condition.** If decision 5's server-side nulling is ever descoped
> or made conditional, this divergence becomes a live defect on 90 players and
> must be fixed in the player.

### Authoring badges stop evaluating time

Three inline copies compute a live "Active" badge from `DateTime.Now`, which in
Blazor Server is the **host's** clock, not the author's and not the display's.
**The time component of the badge is dropped.** Date range and day-of-week stay,
because they are unambiguous.

A badge that is right only when a display happens to sit in the host's zone is
worse than none, because it is silently wrong in exactly the multi-zone case this
effort exists for.

`TicketCounterScheduler.IsEventActiveNow` is a save-time gate rather than a badge
and keeps evaluating, on the rule.

---

## 4. Who evaluates what, against which clock

_Decisions 5, 14, 17 and 19. Settled by DXME-163, DXME-165 and DXME-167._

### One consumer, not two

**The server resolves every window** against the requesting device's stored time
zone, and ships only currently-active content with the four window fields
(`StartDate`, `EndDate`, `StartTime`, `EndTime`) set to **null**.

Because `isAssetScheduled` returns true when all four are absent, that disables
the player's own filter, so the device's OS clock stops affecting content
selection entirely. **This needs no player release on either fleet.**

This replaces the map's earlier position that two independent consumers evaluated
and could silently disagree.

**Qualified by DXME-165, and this is the size of the job.** On the playlist path
the server does not evaluate these fields **at all** today. `SignageController.cs:950-951`
filters day-of-week only, and `:925-931` forwards the four window fields to the
device unevaluated after `MetadataParser.cs:36-43` parses them out of the sidecar
XML. So implementing this is a **new** server-side evaluation, not a corrected
one. Larger than decision 5 implied, but it also repairs
[DXME-174](https://radianttechnology.atlassian.net/browse/DXME-174) for free.

Today's split is worth naming precisely, because it is the defect: **day-of-week
is decided on the server's clock and time-of-day on the device's**. A window
crossing midnight in a non-server zone gets the wrong day with the right time.

**Day-of-week stays server-side**, resolved in the device's time zone.
_Decision 15._ Moving it to the player was the map's earlier working position and
was rejected: it reinstates the second consumer, the player has no timer that
re-runs its schedule filter so it could not self-correct at its own midnight
anyway, and `Asset` in `types.ts` has no `days` field, so it would cost a schema
change plus an applet release.

### Four rules cover every clock-dependent site

_Decision 14._ There is no thirty-row per-site table. That was an artifact of the
overturned decision 2.

| Rule | Behaviour |
| --- | --- |
| **(a) Request-scoped, device in hand** | Resolve against `tblDevice.TimeZone`. |
| **(b) Tick-scoped sweeps** | Stop evaluating windows. The decision moves to request time. |
| **(c) Location-scoped, no device** | Out of scope. Server clock, documented, with a reopening condition. |
| **(d) Operator previews** | Out of scope. They share no code path with any player. |

Rule (b) exists because a tick service asking "is it inside the window now?" is
exactly the second consumer decision 5 abolished. It disagrees with the
request-time evaluation on a timer instead of across a network. This is not a
clock that can be swapped for a correct one: `AccessCacheRefreshService`,
`OverrideScheduleService` and `DefaultAssetActivatorService` each take **one**
`now` snapshot and share it across a tenant-wide loop, so there is no single
correct value to substitute.

### The clock follows the authentication scheme

_Decision 17, settled by DXME-167._ This sharpens rules (a) and (c) from a
per-site judgement into a mechanical test at the controller boundary.

* **A device token means the device's time zone.** `GetPlayerContent`
  (`SignageController.cs:367`) is device-token only, so a device is always in
  hand.
* **An API key means the server's clock, unchanged.** `GetAirline`,
  `GetMessages` and `GetAirlineStatus` are API-key only. A `signage:read`
  integration with an outside platform has no device.
* **`GetDisplays` (`:110`) accepts both and is the one endpoint that must
  branch.**

This generalises rule (c) past video walls to every external integration.

### A Display is not a place

_Decision 19, settled by DXME-167 and implemented by DXME-179._ A Display is a
collection of content set by the schedule editor. It has no time zone. The
devices attached to it do, and **each of those must be respected**.

An invariant that every device under one display shares a time zone was proposed
and **rejected**. So the display path needs per-device resolution exactly as the
playlist path does.

The case to design for is many devices under one display spread across several
time zones. But **the live defect is simpler than that**: a display whose devices
all agree on a single time zone is still evaluated against the Director's clock
today. The multi-device case is real but latent (in the dev database, 235 of 240
displays have zero devices), and the single-zone case is what earns the change
under minimum footprint.

### The five in-scope sites

_DXME-165._ Out of roughly 30 schedule-deciding sites in the September 2026
survey.

| Site | Why it stays |
| --- | --- |
| `SignageController.cs:951`, `:1011` | Playlist manifest day-of-week filter. |
| `SignageController.cs:925-931` + `MetadataParser.cs:36-43` | The four sidecar window fields that must start being server-evaluated. |
| `SignageController.cs:981` | Manifest cache TTL, clamped to server-local midnight. Resolved by section 5. |
| `OverrideService.cs:542` | Overrides re-evaluate at request time with the device already in scope. Clean swap, no cache work. |
| `ScheduleTime.cs:14` + `DisplayItem.cs:53` | The shared window helper and the display-path caller. |

Plus, added by DXME-179: **`SignageController.cs:1300`**, the layout zone build,
is **in**. It is a device-token caller with `deviceId` already in hand, being
shown wrong content on a screen.

### What is out, and why

Full reasoning and per-item reopening conditions are in the map's Out of scope
section. In summary:

* **Video walls and the entire Pixera path.** `PixeraController` is
  `[AllowAnonymous]` with **zero** references to `DeviceId` or `TblDevice`; wall
  shells identify by location id alone, and their hand-entered `/pixera` URLs
  live in Pixera project files and Moxie zone configs that Director cannot update
  remotely. Decision 5's "requesting device" does not exist there. Walls are an
  airport feature and an airport does not cross time zones. _Reopening: one
  Director serving airports in two different time zones._
* **The SLC airline monitor, the ticket-counter and airline write path.**
  Airport features, single-zone by construction, high-visibility surfaces where
  an unneeded change is pure risk.
* **Access and floor-plan schedules.** Split out to DXME-176, deliberately
  off-map: that discussion is about access semantics, with time zones secondary.
* **Operator previews.** All five Blazor display surfaces. None carries an
  `@page`; every one is hosted by an authenticated admin page.
  `EffectiveContentResolver` is preview-only by design. They share no code path
  with any player, so a fix would have to be made twice.
* **`PanelService.cs:311`**, an operator surface under rule (d), and on the
  unmerged `feature/panels` branch. _Reopening: a panel becoming a playback
  surface._

**One consequence, stated so it is not later filed as a bug.** Once the fleet
spans three time zones, Director's own preview of a Mountain display will
disagree with what that display is actually showing, because the preview
evaluates on the server clock by design.

---

## 5. The cache boundary: publication and delivery

_Decisions 15, 16 and 30. Settled by DXME-166 and extended by DXME-179._

> **Partly superseded by the DXME-204 metadata content targeting map (2026-09-15).**
> Three claims in this section are overturned; everything else stands. The shape
> above is **extended, not overturned**: DXME-213 applies the same publication and
> delivery seam to metadata targeting, so the cache key loses its **filter**
> component the way it already lost the time zone, for the identical reason.
>
> 1. "The playlist key stays `(tenant, locationId, filter)`" and "the layout key
>    keeps its `layoutFilterValue` component", below. Overturned: the layout half
>    by DXME-216, the playlist half by DXME-213. `BuildCacheKey` ends with no
>    filter component anywhere, one entry per location and one per display.
> 2. The `ContentManifestLogService.RecordIfChangedAsync` key, below. Loses its
>    `filterValue` component (DXME-213 decision 9). The column stays nullable so
>    existing history remains readable.
> 3. The TTL numbers, below. All three paths go to a uniform thirty minutes
>    (DXME-213 decision 8), on the condition this section itself named.
>
> **Untouched by that map**: the publication and delivery split, "the cache key
> does not gain the time zone", publication version versus delivery version,
> boundary latency, the `localNow` seam, and sections 3, 4, 6, 7, 8 and 9. No
> change to `DeviceLocalTime`, `PlaylistWindow`, `TimeZoneCatalog`,
> `TimeZoneVerification`, the `osTimeZone` emission, the rules fingerprint, the
> window rule, or which clock any consumer evaluates against.
>
> DXME-213 does add a second filter to `ResolvePlaylistDelivery`, alongside the
> window filter. The regression to guard is the two filters masking each other;
> DXME-226 owns that coverage.

**All three manifest paths take one shape: cache the publication, resolve the
delivery per request.**

### The move

The caches currently store the **filtered** result, welding an expensive
time-invariant computation to a cheap time-varying one. That weld is why the TTL
kept being mistaken for a correctness mechanism.

| Path | Cached artifact becomes | Resolved per request |
| --- | --- | --- |
| Playlist | The assembled `assetList` before `IsDayActiveForPlaylist` (`SignageController.cs:951`). Media folder walk, `ParseAllMetadata`, and the per-file access-rule loop, all clock-free. | Day-of-week filter, window filter, image-duration pass, `PlayOrder` sort, `WeightedLoopExpander.Expand`. |
| Display | The display, its schedule events, and the hydrated assets for **all** of them. | `GetActiveEvent`, the projection, expansion, the hash. |
| Layout | `BuildLayoutZonesAsync` output with display-sourced zones unresolved. | `GetContentAssignmentAsync` per display-sourced zone. |

On the layout path **only display-sourced zones move**, because playlist-sourced
zones (`:1325-1354`) read no clock at all today. That is a separate defect,
[DXME-191](https://radianttechnology.atlassian.net/browse/DXME-191), filed
off-map because switching the evaluation on makes currently-playing content start
disappearing.

### The cache key does not gain the time zone

**Decision 13 is overturned**, by DXME-166, and the overturn now holds on all
three paths, by DXME-179.

Decision 13 was correct for a cache holding **resolved** content. Once the cached
artifact is the pre-filter build, which no clock touches, a time zone in the key
would be a component that never changes the value it keys, tripling cache
cardinality and rebuild cost for identical bytes.

The playlist key stays `(tenant, locationId, filter)`, unchanged. The layout key
keeps its `layoutFilterValue` component, because device metadata filtering is a
real dependency of that artifact and is not clock-related.

> **Overturned (2026-09-15).** The layout half by DXME-216, the playlist half by
> DXME-213. Both moved metadata targeting to delivery time, so the cached artifact
> is metadata-free and a filter component keys nothing: the argument two
> paragraphs above, one dimension along. The sentence stated what was true before
> targeting moved, not a principle. The key becomes `(tenant, scope, id)` with a
> `display` / `loc` discriminator, which also fixes `StatusSelectorService`
> invalidating a display's manifest by location id.

**Rejected on the display path, and it is the closest call in this section:**
keying by `(tenant, displayId, timeZone)` would have _vindicated_ decision 13
rather than contradicted its overturn, because that artifact genuinely was not
clock-free. It loses because cache entries then scale with the number of time
zones under one display, and the 30 second TTL becomes load-bearing for window
boundaries again.

**Also rejected: dropping the display cache entirely.** Correct by construction
with the smallest diff of the three, and it loses because three read-only
consumers render display status from those entries (`Pages/Location.razor:306`,
`Components/Home/DirectorDashboardCard.razor:240`, `ConfigTabs/DevicesTab.razor:725`).

### Publication version and delivery version are two concepts

This is not cosmetic. It fixes a defect the split would otherwise introduce.

**Publication version** identifies the content set published to a location or
display. Changes when media, sidecars, approvals or access rules change. Zone
independent. Computed at cache rebuild. **This is what the content manifest log
records.**

**Delivery version** identifies what one device should be showing right now.
Additionally changes at every window and day boundary, and differs between time
zones at the same location. Computed per request. **This is what drives the
player's `NotModified`.**

`ContentManifestLogService.RecordIfChangedAsync` is keyed
`(tenantId, bucketType, bucketId, filterValue)` with no time zone. If it consumed
a per-request delivery version, three time zones would produce three different
manifests and every zone alternation would read as a change: a database
round-trip and a spurious row on a table with 400-day retention, thousands of
times a day. A window boundary is not a content change and must not look like one
in an audit table.

> **The key loses its `filterValue` component (2026-09-15).** DXME-213 decision 9
> drops that parameter from `RecordIfChangedAsync`, since one publication per
> bucket leaves nothing for it to name. The column stays nullable so existing
> history remains readable. The reasoning above is unchanged and is what justifies
> it: the log records the **publication**.

Two devices under one display in different time zones legitimately receive
different content, so a shared version also hands one of them a false
`NotModified`.

**Rejected on the display path: composing the delivery version cheaply as
`(publication version, active event id)`.** That pair is genuinely exact there,
because the event's assets, order and weights are deterministic once the event is
chosen. It is still rejected, because its only justification is cost, and
section 11's capacity finding forbids that.

### The TTL and boundary latency

The TTL stays at **five minutes** on the playlist path and the 30 second default
elsewhere. It becomes a pure freshness backstop, load-bearing for nothing.

**The TTL was never load-bearing for correctness**, despite the code comment at
`SignageController.cs:981` saying it is. `PlaylistManifestTtl` is 5 minutes and
the TTL is `min(untilMidnight, 5 min)`, so the midnight cap only bites in the
last five minutes of the Director's day. The wrong-day-for-an-hour defect comes
entirely from `DateTime.Now` inside `IsDayActiveForPlaylist`. **Delete that
comment when implementing; any ticket reasoning from it is reasoning from a false
premise.**

**Boundary latency is one poll interval**, accepted and written down rather than
engineered down. 30 seconds base, floored at 180 seconds when a wake channel is
connected, clamped at 600. Nothing wakes a player at a boundary:
`IPlayerWakeNotifier` fires from content writes, and a boundary is not a write.

A boundary-triggered wake push was **rejected** as machinery built for content
nobody is scheduling yet. There is no time-based content at go-live. And this is
an improvement on today regardless, since the boundary is currently gated by the
TTL **and** evaluated against the wrong clock at the same time.

**Raising the TTL to 30 minutes was rejected.** The saving is real and would have
offset the per-request cost, but the TTL is currently the only backstop for
approval and access-rule edits, which are database writes that trip no file
watcher. Raising it would stretch approval latency from roughly ten minutes to
roughly thirty-five. Filed as
[DXME-178](https://radianttechnology.atlassian.net/browse/DXME-178), off-map, and
it is **explicitly not a prerequisite**.

> **Taken (2026-09-15), on exactly the condition above.** DXME-213 absorbed and
> closed DXME-178, building the invalidation seam those database writes lacked, so
> all three paths go to a uniform thirty minutes. Three different TTLs in one file
> is how the 30 second layout default survived unexamined. DXME-177's sizing is
> unchanged and narrows the claim: the gain is 6x fewer database round-trips and
> approval latency, not capacity, which in CPU terms is noise.

### The seam

_Decision 30._ `GetActiveEvent(events, DateTime? at)` and
`GetContentAssignmentAsync(..., DateTime? at)` already exist, and `at` is null at
every production call site. The one non-null caller is
`EffectiveContentResolver.cs:218`, passing a host-local `asOf` for the preview
modal.

**`at` is renamed `localNow`** and documented as _the local wall-clock moment to
evaluate against, in whatever time zone the caller chose_. That is already its
meaning at its only live caller, so this settles an existing ambiguity rather
than introducing a concept.

**Rejected: passing a time zone instead.** It breaks the operator preview, which
wants to simulate a _time_, not a _place_.

**Rejected: a named type** so a local wall time cannot be confused with an
instant. The right instinct and the wrong ticket. ADR 0002 carries the
distinction in prose, and inventing a type for one parameter is exactly the
consistency work minimum footprint declines.

---

## 6. The failure shape

_Decisions 22, 23 and 25 to 28. Settled by DXME-168, qualified twice by DXME-170,
and generalised by DXME-179._

### No runtime fallback, anywhere

_Decision 3._ A default may be copied into a row at create time. **Nothing
resolves a missing time zone at evaluation time.** A silent resolution to the
server's clock is the defect being fixed, so shipping a smaller one as the fix is
not available.

### Nothing new validates the identifier

_Decision 23._ No CHECK constraint and no new validation attribute. The schema has
**zero** CHECK constraints across all 13 migrations, and `src/` has zero
`[RegularExpression]` or `[AllowedValues]`. A list of allowed IANA identifiers
would have no precedent and would rot as the IANA database changes.

The curated list (section 9) covers the operator path. The bulk script of
decision 11 is a trusted-operator tool by design. What follows is what catches
the rest.

### An identifier that does not resolve: fail toward the path's own default

_Decision 22, as generalised._ Catch, log, and **decline to discriminate**.

| Path | What "declining" means |
| --- | --- |
| Playlist | Serve the manifest **unfiltered**. Everything plays, including out-of-window content. |
| Display | Serve the display's **`DefaultContent`** event, already implemented at `DisplayItem.cs:81-85`. |
| Layout | Same as display, per source zone. |

This is a **narrow carve-out in decision 3, and it is deliberate.** Decision 3
forbids a fallback _clock_, because a wrong clock silently produces
wrong-but-plausible content. Declining to filter picks no clock; it fails toward
a screen that is showing something. A blank or errored screen at a customer site
is a truck roll; an unfiltered playlist is a logged defect fixed with one
`UPDATE`.

It is only safe because of decision 5: the four window fields ship nulled, so the
player does not re-filter what it receives.

**Rejected on the playlist path:** throwing, and serving an empty manifest.
**Rejected on the display path:** the literal reading of "unfiltered", meaning
ignore the predicates and take the last non-default event. `GetActiveEvent` keeps
the _last_ match in collection order, so that would put a lunchtime promotion on
screen at midnight, and it fails away from the display's own designed default
rather than toward it.

**One honest caveat:** a display authored with no `DefaultContent` shows nothing.
That is already its behaviour whenever no event matches, so this introduces no
new failure mode, but the fail-toward-something guarantee is conditional.

**One conversion helper owns this catch.** It takes the device's stored
identifier and returns either a local wall-clock time or an **explicit unresolved
result**, and it never throws, so the choice of what to serve stays with the
caller that knows its own default. Two call sites need it today. Duplicating the
catch doubles the chance one copy is silent, which matters more than usual here
because of the next point.

### "Log loudly" is not a mechanism this product has

_Measured by DXME-170. This is the load-bearing finding of section 6._

There is no Serilog, NLog, Application Insights, Seq, file sink or log shipping
anywhere in `src/` or `installer/`. `web.config:32` sets
`stdoutLogEnabled="false"` under `hostingModel="inprocess"`, so console output
goes nowhere. `AppSettingsWriter.cs` writes no `Logging` section at all, so a
customer runs the repo default of `Information`. And **`LogCritical` appears zero
times in the entire codebase.**

The one surviving channel is the framework's default Windows `EventLog` provider
at a `Warning` floor, which is framework behaviour rather than anything this repo
configures, and nothing alerts on it. The health endpoints are not a channel
either: `/health` and `/health/ready` exist but nothing on a customer site is
documented as polling them, and they are registered inside the web-role block so
a worker exposes none.

**A thrown startup exception is the only mechanism that reliably reaches a
human**, via ANCM's 500.30 page plus an event log entry.

So decision 22's serve-unfiltered half works as written, and **its loud half does
not**. Any ticket that specifies "log loudly" as its failure response has
specified silence. The instrument that actually covers a device stuck on a
sentinel is the **Time zone column on `DevicesTab`** (section 9), backed by sort
and filter: the sentinel is a literal non-resolving string, so it sorts as a
group and is visible next to the device.

### The Host throws at startup if IANA resolution is unavailable

_Decision 25._ Decision 22 is the handler for **one bad row**, a data defect in
one cell. A dead ICU is a **deployment defect** that routes every device through
that catch at once, which no operator edit fixes, and at 90 players on the 180
second poll floor it writes roughly 43,000 entries a day into a size-capped log,
evicting other diagnostics while the incident is happening.

Before `builder.Build()`, adjacent to the OIDC check and following
`Program.cs:129-139` in placement and message style, the Host asserts two
capabilities and throws `InvalidOperationException` if either fails, reporting
them as distinct failures.

1. **`FindSystemTimeZoneById("America/Chicago")` must resolve.** The id is
   **hardcoded**, so this is a capability test and not a data test. A stored or
   configured id would conflate a dead ICU with a mistyped zone, which is
   decision 22's job.
2. **`TryConvertWindowsIdToIanaId` must return true for the host's own
   `TimeZoneInfo.Local.Id`.** This is the exact conversion the seed helper
   performs at every device-row creation, so it also catches a host zone with no
   IANA mapping that no constant would reach. That helper degrades to `false`
   with an empty out-parameter rather than throwing, so it fails more quietly
   than the throw does.

**Unconditional across all three `DXSuite:Role` values.** Gating on `isWebRole`
fails: `CloudConnectorSyncService` is a `BackgroundService` holding two of the
four row-creation paths, `HeartbeatService` is both `IHostedService` and
injectable so its row creation lands in either role, and on-prem runs
`standalone` and needs both anyway. The only existing throw in the file is
likewise outside both role blocks.

**Why throwing, when decision 22 rejected it.** The two outcomes are not
symmetric, and both axes point the same way. A Host that will not start blanks
nothing: players fall back to a stored manifest and survive a reboot on it, so
the fleet freezes on **last-known-correct** content and the outage is visible. A
Host that starts without ICU serves **incorrect** content immediately,
fleet-wide, and invisibly, since there is no time-based content at go-live. The
authoring UI going dark costs nothing here, because every remedy is on the box
and none is reachable from the UI.

**Rejected:** logging only, indistinguishable from the present silence. And
self-writing `app_offline.htm`, which is operator-driven, out of process, and
whose page has no slot for a reason.

**No ADR.** Surprising and a genuine trade-off, but it is a one-line revert, so
it fails the hard-to-reverse test. The real risk is that it _looks_ trivially
reversible while not being, and the instrument for that is a comment where the
deleter's cursor already is, citing DXME-170.

### Four causes, four instruments

_Decision 26._ **No instrument substitutes for another**, so dropping one
silently uncovers its cause.

| Cause | Instrument |
| --- | --- |
| `InvariantGlobalization=true` on the Host or a publish profile | **CI guard** asserting the built Host's `runtimeconfig.json` carries no `System.Globalization.*` key, written against the `has-pending-model-changes` precedent rather than as a test |
| Machine or app-pool `DOTNET_SYSTEM_GLOBALIZATION_USENLS` | Decision 25's startup throw, **and nothing else, ever** |
| An OS below Windows Server 2019 | Decision 25, plus one documentation sentence |
| DbTool's own invariant mode | Decision 27 below |

### `InvariantGlobalization` comes out of DbTool

_Decision 27._ The property at
`installer/Host/dbtool/DXSuite.Host.DbTool.csproj:25` has no recorded
justification anywhere in the repo, was authored in the same commit (`2191320`)
as the wizard's `false` which **did** get a three-line comment, and costs nothing
to remove because `SelfContained=false` means ICU comes from the shared runtime.
Invariant mode there is a behaviour switch, not a payload switch. Its deletion
carries a comment in the wizard's style saying why it stays absent.

**The constraint matters more than the deletion and outlives it: no migration
computes a time zone identifier in C#.** DbTool touches no device-row-creation
path, but it **is** the process that executes every EF migration, run by the MSI
as a deferred custom action with `Return="check"`, so a throw fails the install
and rolls it back on a production host, and its log lands in the **SYSTEM** temp
folder rather than the technician's. Decisions 7 and 20 already make the one-shot
default a literal sentinel, so nothing breaks today. The constraint stops someone
later making it computed. It also holds on the hand-run SQL upgrade path, where
`TimeZoneInfo` does not exist at all.

### Nothing is checked in the installer

_Decision 28. Recorded so the absence is not read as an oversight._

An OS version check is a **proxy** for the capability and a poor one: it passes a
Server 2022 box with the NLS environment variable set, which is the most
invisible cause of the four, and it fails a box that would work. And the
installer reaches only one of two documented upgrade paths, while the runtime is
the only thing that certainly executes on the Director host under both.

So the IIS launch condition at `installer/Host/Package.wxs:37-38` stays the only
one. **No reopening condition**, because the runtime check strictly dominates.

For reference, the installer checks almost nothing today: zero hits for
`VersionNT`, `bal:Condition`, `MsiNTProductType` or `WixBundleVariable`, no
prerequisite script of any kind, and a failed SQL connection does not block
Finish (`MainWindow.xaml.cs:274` ignores the `bool` return). A ticket looking for
an install-time gate to hang something on will not find one.

### The DST rule, which has no consumer

_Decision 6, closed by DXME-166._ Nonexistent times shift forward, ambiguous
times take the first occurrence.

**Nothing on this map invokes it.** Window evaluation only ever converts instant
to local, which is total and cannot throw. The rule bites only going local to
instant, and the one such conversion (the next-boundary TTL arithmetic) was
removed by the cache split in section 5.

It is written into the glossary anyway, with the no-consumer fact attached,
because the first person to ask "when does this window next open" will need it
and will not re-derive it correctly.

---

## 7. Emission: `osTimeZone`

_Decisions 31 and 32. Settled by DXME-192 and DXME-195._

**A time zone is emitted to a device only when an operator set it.**
`TimeZoneSource = Operator` is the gate, and it is the only gate. Everything else
sends no zone field at all, which lands in the player's existing rule that a
missing value means nothing has told it yet.

### The wire

**One endpoint: `POST /api/Signage/PlayerContent`**, device-token only
(`SignageController.cs:365`). No API-key endpoint carries it, and `GetDisplays`
does **not** branch for it: decision 17's branch there is about which clock
resolves a window, not about this field.

**The heartbeat and the manifest are the same object.** The question DXME-195 was
charted with, "manifest body or heartbeat response", was a choice between two
names for one response.

**The field is `osTimeZone` on `PlayerContentResponse`, outside the
delivery-version hash.** It joins the family that response already has:
`ConsoleLoggingEnabled`, `ForceHtml5Video`, `AllowHttpFallback`, `WindowMode`,
`DeviceMetadata`. All per-device, none in `ComputeVersion`. The field costs no
query and no cache entry, because `GetPlayerContent` already holds the device
row.

**It must ride the `NotModified` path**, which is the normal path for almost
every poll. It joins that response's documented "command fields are still
populated and must be processed" set. Getting this wrong would deliver the zone
only to players whose content happened to be changing.

**The field carries the complete `SetTimeZone` argument**, `POSIX:MST7` and not
`MST7`, and the curated list's column holds the same string under the same name.
One value, no transformation at any hop, so a correction is one data edit.

### A result filter, not nine hand-copied literals

`GetPlayerContent` builds **nine** `PlayerContentResponse` literals: `:413`,
`:507`, `:541`, `:572`, `:686`, `:715`, `:839`, `:1050`, `:1071`. Eight have a
device row in hand.

`:541` is the "No location or display assigned" branch, which is the state of a
device just commissioned and not yet given content, and therefore **exactly the
device whose zone an operator has just set**. A hand-copied implementation would
miss it.

The codebase already solved this. `PlayerPollIntervalFilter` stamps
`NextPollSeconds` and `FullInventoryMinutes` on every branch, and its own doc
comment says it exists "so the many return sites in GetPlayerContent stay
untouched". Follow it: the action stashes the resolved value where the device row
is loaded, and the filter stamps it.

**The `Operator`-only gate is a pure predicate over `TimeZoneSource`, hoisted out
of the action.** _Constraint from DXME-200._ If it stays an inline branch in the
action it is untestable, for no reason.

### What was rejected

**A `tblDeviceCommand` type** alongside `cec`, `screenshot` and `flushCache`. **A
time zone is state, not an event.** A command is delivered once, expires, and is
lost for good if the player was offline past its window or its card is
re-imaged. State re-asserted on every poll self-heals.

**A flavour gate.** Both clients parse the response field-by-field off an untyped
value with no schema validation, inside a `try` that degrades to an empty result
(`assetSync.ts:171-307`), and `assetSync.ts` is **byte-identical** between the
two Engage repos, so an unknown field provably cannot break either one. A
`PlatformId` gate would also couple emission to an `EngageBrightSign` row that
does not exist on the dev database.

**An audit row per device.** The hazard DXME-195 was asked to weigh was already
closed by section 5: `ContentManifestLogService` is keyed per bucket and takes
the publication, so a per-device delivery version cannot reach it.

### Nothing is pushed when the value changes

No wake, no command, no forced reboot. `IPlayerWakeNotifier` fires from content
writes and from `DeviceCommandService.QueueAsync`, and an operator setting a time
zone is neither. The value lands within one poll and takes effect only at the
next boot, which the daily reboot timer supplies within 24 hours. Shaving minutes
off a 24-hour latency buys nothing.

### One consequence, stated rather than discovered

_From DXME-182._ A `TblDevice` row deleted and recreated by the Ink sync comes
back `Seeded`, so Director silently stops re-asserting the zone while the player
keeps the correct registry value. **That is the safe direction.**

---

## 8. Verification: the rules fingerprint

_Decision 33, settled by DXME-197._

**A pushed time zone is verified on the way back, by comparing the zone's rules
rather than its name.**

### What the app reports

On the existing heartbeat, inside `PropertiesJson`:

| Field | Meaning |
| --- | --- |
| Offset at 15 January | UTC offset in force at that instant |
| Offset at 15 July | UTC offset in force at that instant |
| Year | The year those two instants were taken in |
| POSIX string | The literal in force |
| `engage/tz_applied` | The literal the bootloader last applied |

DXSuite compares the two reported offsets against `TimeZoneInfo` for the intended
`tblDevice.TimeZone`, at the same two instants of the **reported** year.

### Why rules and not a name

**A name is not available to the app.** Under a `POSIX:` prefixed zone, which is
the only form section 7 sends,
`Intl.DateTimeFormat().resolvedOptions().timeZone` is **`undefined`**, and
`formatToParts` degrades to a bare `GMT-07:00`. ICU holds the offset rules and no
zone identity. The clock itself is correct throughout. [BENCH, DXME-197]

> **Trap.** DXME-161 measured that `Intl` returns an IANA name on a BrightSign
> player, and that is still true on an **unmodified** player: `MST` reports
> `America/Denver`, `EST` reports `America/New_York`. It stops being true on any
> player DXSuite has pushed a `POSIX:` literal to. A reader who takes DXME-161 at
> face value after commissioning will design against a surface that has gone
> dark.

### Why two instants and not one

| Zone | (January, July) |
| --- | --- |
| `America/Phoenix` | (-7, -7) |
| `America/Denver` | (-7, -6) |
| The wrong `MST1` | (-1, -1) |

**The January probe alone cannot separate Phoenix from Denver**, because both sit
at -7 in winter, so a single sample of the current offset passes the Arizona
error for four months a year. This is the same blind spot that rules out
comparing a bare UTC offset. Two instants escape it.

The check is **semantic rather than nominal**. It cannot separate two zones with
identical rules, which does not matter, because zones with identical rules
schedule identically.

> **Do not optimise this back down to one instant.** The test fixture carries a
> vector whose only job is to fail when somebody does (section 11, case 12).

### Why the year is reported rather than assumed

A hardcoded year rots as the server keeps computing past the app's release, and
deriving it independently on both sides races at the December boundary. A missing
year lands the device in the "nothing to compare against yet" state rather than
being judged against a guess.

### Pending versus mismatch

Distinguished by `engage/tz_applied`, **not by a timeout**.

* `tz_applied` **not equal** to the intended literal: the apply has not happened
  yet. Nothing is wrong. **Pending.**
* `tz_applied` **equal** to it, with the rules still disagreeing: the bootloader
  applied it and this is the result. **Mismatch.**

Decision 31 rejected a `TimeZoneConfirmedUtc` timestamp and `tblDevice` has no
change history, so DXSuite cannot date the change. The player already knows.

This requires the app to **read** a key in the bootloader-owned `engage` section,
so that section becomes bootloader-**write**-owned and app-readable.

### A mismatch is derived, never stored and never a trigger

The shape DXME-134 gave flavour mismatch. **Director has no device fault table at
all**, and `tblEventLog` has no UI reader.

The reported value is stored as an observation in a new `DeviceExtendedData`
member following `contentSync` end to end, and rendered as a **tri-state badge
with a fourth state**: verified, pending, mismatch, and not reported. The
dashboard card gets `contentSync`'s fleet counter, because the Devices grid
cannot be filtered to problem rows
([DXME-198](https://radianttechnology.atlassian.net/browse/DXME-198)).

> **Note the contradiction and resolve it this way.** Decision 31 said
> `TimeZoneSource` becomes a filterable column. DXME-197 then found the Devices
> grid cannot filter to problem rows at all. Until DXME-198 lands, **sort is the
> mechanism**, and the fleet counter on the dashboard card is what surfaces a
> problem that sort would not.

### It never re-pushes

Re-pushing the same literal cannot change the outcome, since the player applied
exactly what it was told and `tz_applied` will match. A re-push is a provable
no-op that costs a reboot. **The only fix is editing the curated list, which is a
human action.**

### No flavour gate on verification either

signageOS players discard `osTimeZone` and never report a fingerprint, so they
sit in the fourth state permanently. That is correct and costs nothing.

**Rejected: Sentinel's `BrightSignDwsDriver`.** The DWS surface does work, but the
driver does not poll `/time` today, Sentinel is not approved for sale, and it
would need to reach 90 players' DWS across customer networks.

The player half is
[DXME-199](https://radianttechnology.atlassian.net/browse/DXME-199), off-map.

---

## 9. Authoring and operator surfaces

_Decisions 17, 18, 19 and 31. Settled by DXME-167, amended by DXME-192._

### One sentence, four surfaces

**`Times are each player's local time.`** Nine words, the same string in all four
places, near the time inputs.

| Surface | Where |
| --- | --- |
| `MediaAssetUploader.razor` | The sidecar time inputs |
| `OverrideEditor.razor` | The override window inputs |
| `ScheduleEditor.razor` | The schedule event window inputs |
| `Playlist.razor` | The chips block at `:611` |

**No zone names, counts or values appear on any authoring surface.** The copy
states the rule; it never names a zone.

The other eleven editors the original ticket listed belong to the ticket-counter,
access and floor-plan paths, all already out of scope.

### The multi-zone rendering is dropped entirely

No `SELECT DISTINCT TimeZone` query, no banner, no per-chip resolved times, no
location summary line.

Per-chip resolved times would have been **actively harmful**: the times are
identical numbers in every zone, so three identical `9:00 AM` chips teach the
opposite of the rule.

### The "active now" badges do not exist

_This was ticket item 4 and it had no subject._ The chips at
`Playlist.razor:594-625` are static renderings of stored values. Nothing
evaluates whether a window is currently open.

What does exist is **expiry**. `Playlist.razor:886` computes `IsExpired` as a
date-only comparison on the server clock, driving the `CONTENT EXPIRED` overlay
and, silently, the loop-length figure at `:1303`. It is wrong by 1 hour for a
Mountain player under a Central Director and **2 hours for Arizona for most of
the year**.

**Left on the server clock** under minimum footprint: it is operator-facing only
and changes nothing about what plays. Filed as
[DXME-180](https://radianttechnology.atlassian.net/browse/DXME-180), off-map.

### The one place a time zone is visible

A **Time zone column on `DevicesTab.razor`**, on by default, edited through the
existing click-to-edit and save-on-blur cell pattern already used for Name at
`:569` and Description at `:625`.

**The control is a curated list, not free text and not all 400-plus IANA ids.**
US and Canada zones with explicit labels, roughly twenty rows, each carrying
three columns:

| Label | IANA id | POSIX literal |
| --- | --- | --- |
| Central Time (Chicago) | `America/Chicago` | `POSIX:`… |
| Mountain Time (Denver) | `America/Denver` | `POSIX:`… |
| Arizona (Phoenix, no daylight saving) | `America/Phoenix` | `POSIX:`… |

**This is the highest-risk operator action in the whole effort.** Someone who
reasons "Phoenix is Mountain" and picks Mountain Time gets `America/Denver`,
which tracks Denver's daylight saving and is **wrong for eight months a year
while looking correct all winter**. Free text is how a Denver value lands on a
Phoenix player.

The POSIX column is the literal section 7 emits, verbatim. **The literals
themselves are an open item**, see section 13. An off-list zone set by SQL has no
POSIX string and therefore emits nothing, which is section 6's shape:
server-side resolution is unaffected, because it reads the IANA value regardless.

A fourth "expected reported name" column was proposed and **removed**: section 8's
check is semantic, so no name is needed.

**The cell also renders provenance.** An unconfirmed value (`Seeded`) renders in a
muted or marked style. `TimeZoneSource` becomes a sortable column, off by
default.

**Commissioning relies on sort and filter only. No confirmed flag** as a
commissioning tracker: that would be decision 12's deleted unset branch returning
under another name. The weakness is real and stated: an integrator who does 88 of
90 is not told about the 2, and under decision 31 those 2 also never get their OS
time zone set.

**Existing users with a saved column view will not see the new column** until they
add it, because `LoadSavedView` at `:656` validates against the stored list.
Accepted rather than writing migration logic against everyone's stored
preference.

### Sites management

A new **`SitesTab.razor`** config tab following the StyleGuide `dx-panel`
structure, plus a site picker in the `DevicesTab` cell pattern. `Site`, `City`
and `State` become available `DevicesTab` columns, **off by default**.

Site assignment is one device at a time, no bulk UI. _Reopening: a second
deployment of this size._

**Naming.** `Site`, knowingly, despite Sentinel having a `SiteGateway.SiteName`
string attribute and a `site` policy scope. Sentinel's is an attribute, not an
entity, and Sentinel is not approved for sale. Rejected `ServiceCenter`, which
bakes one customer's vocabulary into a schema airports also use, the way
`HotelNo` did. Rejected `Facility`, which is correct and which nobody says.

### Calendars

`DisplayCalendar.razor` shows the authored numbers and **states no time zone**.
Those numbers resolve per device exactly as playlist windows do. This is a
decision that it does not change, not a scope exclusion: the display path's
_behaviour_ is in scope, its calendar's _presentation_ is unchanged.

`CounterCalendar.razor` is out with the ticket-counter path. It is at
`src/DXSuite.Director/Components/Schedulers/`, **not** where the survey said, and
it reads **no clock at all**: `IsDayActive` evaluates day-of-week against an
operator-chosen `SelectedDate`. It is not a badge and not a defect.

### Two metadata keys stop being suggested

`DeviceMetadataService.cs:44-50` offers `zipCode`, `propertyName`, `locale`,
`timezone` and `region` to operators.

**Remove `zipCode` and `propertyName`.** They are a shadow, per-device, free-text
version of the site data `tblSite` now owns. Existing rows are untouched and
still delivered, because the list is only a UI affordance and those values reach
the player as URL query parameters where something may read them.

> **`timezone` is a decoy and must stay one.** `TblDeviceMetadata` already has a
> key called `"timezone"`, the admin UI actively offers it, an operator can
> author a per-device value today, it reaches the player, and
> `displayManager.ts:98-124` appends it to web content URLs as a query
> parameter. **Nothing interprets it as a time zone.** No part of this work
> should read it, write it, or treat it as evidence, and it is **not** the column
> decision 11 is talking about. Whether it also stops being suggested is a
> judgement for the builder; removing it is consistent with the two above.

---

## 10. Migration and backfill

_Decisions 7, 20, 21, 22 and 23. Settled by DXME-168._

### Two migrations, one release

**Migration 1, the time zone.**

* Add `tblDevice.TimeZone`, `varchar(50)`, `[Unicode(false)]`, non-nullable.
* Add `tblDevice.TimeZoneSource`, non-nullable.
* Drop `tblDisplay.TimeZone`, and remove its only writer at
  `LocationEditModal.razor:979`.

**Migration 2, sites.** `tblSite` with the columns in section 2, plus nullable
`tblDevice.SiteId` and `IX_tblDevice_SiteId`.

**Two migrations, not one, in the same release.** `tblSite` is not time zone
work; it arrived by a destination widening and is the piece most likely to slip.
Splitting lets it be dropped without unpicking the time zone work, and the
on-prem script is generated over a range regardless, so it costs nothing.

### No persistent database default

Adding a non-nullable column to a populated table needs a default _for that
statement_, and that is separable from a persistent default constraint. The
migration uses a one-shot default in `AddColumn` and **drops the constraint in
the same migration**. The column ends non-nullable with no default.

This is house style, not a departure: `tblDevice` has **no column defaults at
all** except `CreateDate`.

**A persistent default is a second, silent acquisition path** that would mask a
missing code seed. Decision 3's fallback wearing a migration's hat.

`TimeZoneSource` follows the same shape with a one-shot `Seeded`, **on
consistency alone, and only half the reasoning transfers**: a persistent `Seeded`
default would not be a second silent acquisition path, it is the fail-closed
value, and the insert-throws-in-development property is carried by `TimeZone`
having no default regardless. Existing rows are correct by construction, because
the migration sentinel below is definitionally not an operator's choice. **The
sentinel earns no fourth `TimeZoneSource` value:** nothing behaves differently
for it.

### The seed lives at one choke point

**There are four row-creation paths, not the one decision 12 named:**

| Path | Assembly |
| --- | --- |
| `HeartbeatService.cs:290` | `DXSuite.Director` |
| `CloudConnectorsTab.razor:853-867` | `DXSuite.Director` |
| `CloudConnectorSyncService.cs:444-464` | `DXSuite.Sentinel` |
| `CloudConnectorSyncService.cs:555-569` | `DXSuite.Sentinel` |

Seeding inline in four places is four places to forget, and a fifth path would be
born wrong.

**One provisioning helper that all four call.** It writes **both** columns:
`TimeZone` from
`TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, …)`, and
`TimeZoneSource = Seeded`. The helper is the only writer of `Seeded`.

Not `TimeZoneInfo.Local.Id` directly, which on Windows yields
`Eastern Standard Time`. The 8 `tblDisplay` rows already holding a Windows id are
the evidence that this leak has happened once before.

**This is what makes "no persistent default" safe rather than reckless.** With no
default, a path that skips the helper throws on insert and surfaces in
development, instead of silently stamping a wrong zone on a real player.

The repo has **no** Windows-to-IANA conversion anywhere and no `TimeZoneConverter`
or `NodaTime` package. This is the framework call, ICU-backed, and the helper is
its first use. (`GuardrailService.cs:52-53` is _not_ a counter-example: it is a
UTC-to-local instant conversion on a Windows identifier from a deserialized
policy object, not an identifier-format conversion. The map's original survey line
implying otherwise is wrong.)

### The backfill is an operator-supplied literal

**T-SQL cannot produce the value.** Measured on the dev Director database, SQL
Server 15.0.4261.1:

* `SELECT SYSDATETIMEOFFSET() AT TIME ZONE 'America/Chicago'` **errors**. The
  same statement with `'Central Standard Time'` returns a value.
* `sys.time_zone_info` holds Windows names only. `America/Chicago` and
  `America/Phoenix` match **zero** rows.
* `CURRENT_TIMEZONE()` returns a **display name**,
  `(UTC-05:00) Eastern Time (US & Canada)`, which is not usable with
  `AT TIME ZONE` without parsing it first.
* Incidentally: Arizona is its own Windows identifier,
  `US Mountain Standard Time`, distinct from `Mountain Standard Time`.

Decision 4 makes IANA canonical, so no migration can derive a time zone from the
server. "The server" is ambiguous anyway, since an on-prem upgrade script may run
on a DBA workstation rather than the Director host.

**So the backfill is a documented step in the upgrade runbook, before the
application starts:**

```sql
UPDATE tblDevice SET TimeZone = '<zone>', TimeZoneSource = 'Operator';
```

`TimeZoneSource = 'Operator'` is not incidental. Without it, every upgraded
deployment stays permanently unconfirmed and never pushes a zone to any player.

**Rejected:** deriving it from `CURRENT_TIMEZONE()` through an embedded mapping
table, which reads the wrong machine while looking authoritative and ships a
table that will rot. And correcting it at application startup, which reads the
right clock but is a resolution-time fallback in everything but name.

**Also rejected:** a `sqlcmd` guard making the operator step non-skippable. It is
the better shape, and `db/schema-migration-strategy.md:98-99` forbids it:
generated scripts are derived artifacts to be regenerated, never hand-edited.
Worth revisiting if a missed step turns out to be common.

The mechanics for any migration-embedded SQL are an established pattern here:
`migrationBuilder.Sql("EXEC('UPDATE …')")` at `AirlineContentStates.cs:41-42`,
whose comment explains the `EXEC` wrapper exists so `migrations script` can emit
it in one batch.

### When the operator step is skipped: a sentinel, not a plausible zone

**This amends decision 7.** The upgrade is behaviourally inert **only when the
operator step runs**.

A real literal such as `America/New_York` keeps the inertness promise only for
deployments that happen to be Eastern. For every other one it produces
silently-wrong-but-plausible content from a value nobody chose, which is the
defect this whole effort exists to remove, reintroduced at the migration.

**The column holds a literal sentinel that does not resolve.** It trades
inertness for loudness: content plays that should not, within one poll interval,
and one `UPDATE` fixes it.

It adds **no new branch**. A sentinel is simply an identifier that fails to
resolve, and section 6 already handles that. The same sentinel is written when
the seed helper cannot compute an IANA id for the host, which is what happens
under invariant globalization. Throwing there would mean a player that cannot
register at all, which is far worse and much harder to diagnose from the field.

### Delivery is out of scope

`deploy/director-upgrade-2026.8.1.sql` covers **one** migration rather than a
release, is already stale, and **nothing in the repo documents how or when it is
regenerated**. `db/schema-migration-strategy.md` is marked _decided, not yet
implemented_, and the per-tenant fan-out migrator that would land this column on
every tenant Director database does not appear to exist in `src/`.

Rebuilding that pipeline is a different effort, filed as
[DXME-181](https://radianttechnology.atlassian.net/browse/DXME-181). **What this
work owes it is the requirement: the generated script must carry the backfill
`UPDATE`, and the operator step must be in the runbook.** Do not re-litigate
delivery here.

---

## 11. Test strategy

_Decision 34, settled by DXME-200._

**Two artifacts, not one.**

### Artifact 1: the JSON vector file

`tests/DXSuite.Director.Tests/TimeZoneVectors.json`, copied to output via the
csproj, read by `[Theory]` plus `MemberData`. **Three top-level arrays**, for the
three things that are pure functions.

Two rules go in a `$comment` at the head of the file, along with the fact that no
TypeScript consumer exists today:

1. **Expected offsets are pinned as literals.**
2. **Vector years are legislated years only, present and past.** No vector
   reaches into a future year. Section 8 reports the year precisely so a hardcode
   cannot rot; a vector asserting an offset pair for 2035 is a guess about
   legislation, one asserting 2026 is a fact. Parameterisation is proved by
   varying the year _inside_ that range.

Portability to a TypeScript runner is preserved because it costs nothing, not
because one is coming. **There is no TypeScript suite in either Engage repo and
there never has been**: no `test` script, devDependencies are webpack, ts-loader
and typescript, and the only gates are `tsc --noEmit` and, on signageOS,
`es-check --module es5`. DXME-164's shared-JSON vehicle assumed a consumer that
is not there.

#### Array 1: `windowRule`, against `DailyWindow.Contains`

| Start | End | Local time | Expected |
| --- | --- | --- | --- |
| 09:00 | 17:00 | 09:00:00 | true (start inclusive) |
| 09:00 | 17:00 | 12:00:00 | true |
| 09:00 | 17:00 | 17:00:00 | **false** (end exclusive) |
| 09:00 | 17:00 | 08:59:59 | false |
| 22:00 | 06:00 | 22:00:00 | true (wrap, start inclusive) |
| 22:00 | 06:00 | 23:30:00 | true (wrap, before midnight) |
| 22:00 | 06:00 | 05:59:59 | true (wrap, after midnight) |
| 22:00 | 06:00 | 06:00:00 | **false** (wrap, end exclusive) |
| 22:00 | 06:00 | 12:00:00 | false (wrap, outside) |
| 08:00 | 23:59 | 23:59:30 | **true** (23:59 means end of day) |
| 08:00 | 23:59 | 23:59:59 | true |
| 08:00 | 23:59 | 07:59:59 | false |
| 09:00 | _null_ | 08:59:59 | false (one-sided start) |
| 09:00 | _null_ | 23:59:59 | true |
| _null_ | 17:00 | 00:00:00 | true (one-sided end) |
| _null_ | 17:00 | 17:00:00 | false |
| _null_ | _null_ | 03:17:00 | true (always open) |

#### Array 2: `conversion`, against the instant-to-local helper

| Instant (UTC) | Zone | Expected local |
| --- | --- | --- |
| 2026-01-15T12:00:00Z | `America/Chicago` | 2026-01-15 06:00 |
| 2026-07-15T12:00:00Z | `America/Chicago` | 2026-07-15 07:00 |
| 2026-01-15T12:00:00Z | `America/Denver` | 2026-01-15 05:00 |
| 2026-07-15T12:00:00Z | `America/Denver` | 2026-07-15 06:00 |
| 2026-01-15T12:00:00Z | `America/Phoenix` | 2026-01-15 05:00 |
| 2026-07-15T12:00:00Z | `America/Phoenix` | 2026-07-15 05:00 |
| 2025-01-15T12:00:00Z | `America/Chicago` | 2025-01-15 06:00 |
| 2025-07-15T12:00:00Z | `America/Chicago` | 2025-07-15 07:00 |
| _(sentinel literal)_ | _(sentinel literal)_ | **unresolved, does not throw** |

**The DST vector, which replaces the gap-and-ambiguity vectors decision 6 has no
consumer for.** US spring forward 2026 is 2026-03-08, when 02:00 local becomes
03:00.

| Instant (UTC) | Zone | Expected local |
| --- | --- | --- |
| 2026-03-08T07:30:00Z | `America/Chicago` | 2026-03-08 01:30 |
| 2026-03-08T08:30:00Z | `America/Chicago` | 2026-03-08 **03:30** |
| 2026-03-08T07:30:00Z | `America/Phoenix` | 2026-03-08 00:30 |
| 2026-03-08T08:30:00Z | `America/Phoenix` | 2026-03-08 **01:30** |

**Two instants one hour apart give local times two hours apart in Chicago and one
hour apart in Phoenix.** That is Arizona's seasonal drift expressed as a vector.
The instant is an argument, so nothing needs faking, and no clock seam is needed
or wanted.

#### Array 3: `verificationBadge`, against the badge state function

| Intended zone | Reported (Jan, Jul) | Year | `tz_applied` matches intended | Expected |
| --- | --- | --- | --- | --- |
| `America/Phoenix` | (-7, -7) | 2026 | yes | Verified |
| `America/Denver` | (-7, -6) | 2026 | yes | Verified |
| `America/Phoenix` | **(-7, -6)** | 2026 | yes | **Mismatch** (Denver rules) |
| `America/Phoenix` | (-1, -1) | 2026 | yes | Mismatch (the wrong `MST1`) |
| `America/Phoenix` | (-7, -6) | 2026 | **no** | **Pending** |
| `America/Phoenix` | _none reported_ | n/a | n/a | Not reported |
| `America/Phoenix` | (-7, -7) | _missing_ | yes | Not reported |

> **Carry this comment into the file, next to the third row.** The third and
> second rows differ only in the intended zone and are not redundancy. **A
> January-only probe passes both**, because Phoenix and Denver are both -7 in
> winter. This pair is the only thing standing between decision 33's two-instant
> design and somebody halving the probe.

### Artifact 2: ordinary xUnit facts

Everything else, in `tests/DXSuite.Director.Tests`. A case about a controller, a
cache, a result filter or hash membership has no input row and no expected output
value, and stretching one JSON schema over both kinds produces a file with a
`kind` discriminator and fifteen shapes.

**The seam that makes the expensive cases cheap:**
`DisplayItem.GetActiveEvent(ICollection<TblScheduleEvent>, DateTime?)` at
`DisplayItem.cs:50` reads no instance state, queries nothing, and collaborates
only with the eight-line pure static at `ScheduleTime.cs:14`. Fully-constructed
events plus a local `DateTime` in, selected event out.

**The boundary is exact.** Asserting _which event is selected_ is free. Asserting
_which content comes out_ is not: `GetContentAssignmentAsync` hydrates assets
through EF, and `EffectiveContentResolver` takes `IDirectorDBContext` with
roughly 90 `DbSet` properties. **Nothing about time zones needs to cross that
line.** The existing suite has zero database machinery, no `DbContext`, no EF
in-memory provider, no `WebApplicationFactory` and no mocking library.

| Assertion | Where |
| --- | --- |
| Server `TimeSpan?` serialisation emits a string the strict `HH:MM:SS` parse accepts | Serialisation fact. The server's output format was always the hazard, not the regex. |
| **Publication-version invariance:** one publication, two `localNow` values either side of a boundary, identical version strings | Cache fact. This is what fails when somebody folds the two versions back together to save the hash. |
| The unresolved branch serves `DefaultContent` | `GetActiveEvent` |
| One display resolves to different content for two devices in different time zones | `GetActiveEvent` |
| A display-sourced layout zone resolves against device-local time; a playlist-sourced zone in the same layout does not | Holds section 5's line against the obvious later cleanup of routing both through one resolver |
| A `Seeded` device emits no zone field | The hoisted pure predicate over `TimeZoneSource` |
| Confirming a device's time zone does **not** move its delivery version | Hash-membership fact. Pins the surprise, since this is the opposite of what DXME-192 anticipated. |
| The result filter stamps `osTimeZone` on what it is handed | Filter fact |

> **A recorded hazard, not a test.** A filter test proves the filter stamps what
> it is handed. It **cannot** prove the action reached the filter on the
> `NotModified` branch or the "No location or display assigned" branch. There are
> currently **nine** return sites in `GetPlayerContent`. That is why section 7
> mandates a filter rather than hand-copied literals, and it is the residual risk
> if somebody ignores that.

### Two constraints this puts on the build

Both are the choke-point argument of sections 6 and 10 arriving from the test
side. Without them, these cases are untestable for no reason.

1. **Section 6's unresolved branch is decided before control enters EF-bound
   code.** Landing the catch inside `ResolveDisplayAsync` makes the caller half
   untestable.
2. **Section 7's `Operator`-only gate is a pure predicate, not an inline branch
   in the action.**

### Who writes the file

**DXME-158 authors `TimeZoneVectors.json`. This document carries the vectors
concretely enough to transcribe.** A fixture committed by the map would reference
types that do not compile, and none of the subject code exists yet:
`ConvertTimeFromUtc`, `FindSystemTimeZoneById` and `TryConvertIanaIdToWindowsId`
appear **zero** times under `src/DXSuite.Director`, and `tblDevice` has no
`TimeZone` column. (`TblDisplay.TimeZone` exists at `TblDisplay.cs:85` and is
referenced by no code, which independently confirms section 2's drop is safe.)

### Capacity is not a test subject, and not an argument either

_Decision 29, measured by DXME-177._ The per-request filter, meaning the actual
time zone work, is **3.4 microseconds** at 100 assets: 1% of a 358-microsecond
pass, and that pass is **0.32% of one core** at 90 players in the worst plausible
cadence. One core carries roughly 28,000 players at the fastest poll the system
allows.

**This is binding in one direction only. No part of this work may add a cache, a
memoised delivery or a shortened evaluation on capacity grounds.** The capacity is
not in question, and section 5 exists precisely to keep a TTL from becoming
load-bearing for correctness. Known optimizations are
[DXME-188](https://radianttechnology.atlassian.net/browse/DXME-188) and are
explicitly **not** prerequisites.

Two things inside the pass are worth carrying forward anyway, neither being time
zone work. The **delivery-version hash is 80%** of it, so the per-device delivery
version costs far more than the filter it was introduced to serve. And
`WeightedLoopExpander.Expand` is **O(n²)**, the only superlinear term anywhere in
the path, invisible at these sizes and the first thing to bite if a publication
grows.

Performance is verified by DXME-177's five instruments, led by delivery-pass
duration tagged by time zone and the `NotModified` ratio. Never by a test.

---

## 12. Code changes by file

New work is marked **new**; everything else is a change to an existing file. Line
numbers are as of `origin/dev` at `d48ac47` and will drift.

### `src/DXSuite.Director/Entities/`

| File | Change |
| --- | --- |
| `TblDevice.cs` | `TimeZone` (`varchar(50)`, `[Unicode(false)]`, non-nullable); `TimeZoneSource` (non-nullable); nullable `SiteId` and its navigation |
| `TblDisplay.cs` | **delete** `TimeZone` (`:85`) |
| **new** `TblSite.cs` | `SiteId`, `CustomerId`, required `SiteName`, nullable `Address1`, `Address2`, `City`, `StateProvince`, `PostalCode`, `Country` |
| `DirectorDbContext.cs` | Remove the `tblDisplay.TimeZone` model default (`:354`); configure `tblSite`, `DeleteBehavior.SetNull` on `tblDevice.SiteId`, `IX_tblDevice_SiteId`; `SiteName` **not** unique |
| **new** EF migration 1 | `tblDevice.TimeZone` + `TimeZoneSource`, one-shot defaults dropped in the same migration; drop `tblDisplay.TimeZone`. **Lossy `Down`, stated not papered over** |
| **new** EF migration 2 | `tblSite` + `tblDevice.SiteId`. Separate so it can be dropped from the release |

### `src/DXSuite.Director/Services/`: the window rule

| File | Change |
| --- | --- |
| **new** `DailyWindow.cs` | `readonly record struct DailyWindow(TimeOnly? Start, TimeOnly? End)` with `Contains(TimeOnly)`. Pure |
| `ScheduleTime.cs` | `:14` delegates to `DailyWindow`. Its wrap branch stops being inclusive; its 23:59 branch stops discarding the end bound |
| `ClusterResolutionService.cs` | `IsTimeActive` (`:205`) delegates. **Gains midnight wrap.** Check a customer database first |
| `StatusSelectorService.cs` | The inline check at `:412` delegates, so the class uses one semantic in both paths (`:327` already does). Also `:408-409`: compare `.Date`, not full `DateTime`, so the last day of a schedule fires |
| `AirlineStateResolver.cs` | `IsActiveAt` (`:40`) delegates. Move "missing or unparseable means inactive" out of the window rule into an explicit branch in `PickActive`, **and log it** (`:83-84` currently swallows a bad string into a default) |
| `ScheduleEvaluator.cs` | **No change here.** Deferred to DXME-176 |
| `TblAccessSchedule.cs`, `TblFloorPlanIconSchedule.cs`, `IScheduleWindow.cs` | `TimeSpan?` converts to `TimeOnly` at the edge, **rejecting out-of-range values loudly rather than clamping**. Retire the adapters at `PixeraLayerSyncService.cs:400-405` and `StatusSelectorService.cs:96-97`, `:361-362` |

### `src/DXSuite.Director/Services/`: resolution and seeding

| File | Change |
| --- | --- |
| **new** time zone conversion helper | Instant to device-local wall clock from a stored IANA id. Returns an **explicit unresolved result**, never throws. **This is where section 6's catch lives, and it is the only place** |
| **new** device provisioning helper | Writes `TimeZone` (via `TryConvertWindowsIdToIanaId` on `TimeZoneInfo.Local.Id`) and `TimeZoneSource = Seeded`. Sentinel literal when the conversion fails. The **only** writer of `Seeded` |
| **new** `TimeZoneSource` predicate | Pure, `Operator` only. Hoisted out of the action so section 7's gate is testable |
| `HeartbeatService.cs` | `:290` calls the provisioning helper |
| `CloudConnectorSyncService.cs` (**`DXSuite.Sentinel`**) | `:444-464` and `:555-569` call the provisioning helper |
| `ManifestCacheService.cs` | Cached artifact becomes the publication on all three paths. `ComputeVersion` (`:163`) becomes the **publication** version. Delete the dead `MatchesAnyVariant` (`:80`) and `InvalidateAll` (`:123-136`), or leave them, but do **not** revive `MatchesAnyVariant`: it returns true when any variant matches |
| `ContentManifestLogService.cs` | Consumes the **publication** version only. No change to its key |
| `OverrideService.cs` | `:542` resolves against the device's time zone. No cache work |
| `MetadataParser.cs` | `:36-43` continues to parse the four window fields. They are now consumed server-side rather than forwarded |
| `EffectiveContentResolver.cs` | `:218` passes `localNow` under the new name. Behaviour unchanged: preview-only, host clock by design |
| `PixeraAllocationService.cs` | `:111` and `:274` use the `atTime` parameter they already receive instead of hardcoding `DateTime.Now`. **No time zone, no device lookup, no new clock read.** Fixes future airline dayparting never being pre-derived at `BackgroundZoneDerivationService.cs:207` |
| `ScheduleExpiryNotificationService.cs` | `:61` uses the **host local** clock instead of `UtcNow`, with a comment saying the comparison is wall clock to wall clock by design. **Carve-out from minimum footprint**, and the time zone question is explicitly declined: it is a tick sweep with no device, and a display spans zones |
| `DeviceMetadataService.cs` | `:44-50` drops `zipCode` and `propertyName` from the suggested keys |

### `src/DXSuite.Director/Models/`

| File | Change |
| --- | --- |
| `DisplayItem.cs` | Rename `at` to `localNow` on `GetActiveEvent` (`:50`) and `GetContentAssignmentAsync` (`:89`), documented as the local wall-clock moment in whatever time zone the caller chose. `:53` stops defaulting to `DateTime.Now`. `DefaultContent` at `:81-85` becomes section 6's display-path default |
| `BrightSign/PlayerContentResponse.cs` | **new** `osTimeZone`, outside `ComputeVersion` |
| `ZoneContent/ZoneContentModels.cs` | No change here. `ZoneContentItem` carrying no time fields is DXME-191, off-map |

### `src/DXSuite.Director/Controllers/`

| File | Change |
| --- | --- |
| `SignageController.cs` | The bulk of the work. Playlist branch: cache boundary immediately before `IsDayActiveForPlaylist` (`:951`); day-of-week resolves in the device's time zone at `:1011`; **new** server-side window evaluation over the four sidecar fields, which are then nulled at `:925-931`; delete the misleading TTL comment at `:981`. Display branch: cache the publication at `:640`, resolve `GetActiveEvent` per request against device-local now. Layout branch: same split at `:756`/`:1230`, display-sourced zones only (`:1289-1322`); `:1300` is **in scope**. `GetDisplays` (`:110`) branches on authentication scheme. `GetPlayerContent`: stash the resolved `osTimeZone` where the device row is loaded; **do not** touch the nine response literals |
| **new** result filter | Stamps `osTimeZone` on every `GetPlayerContent` return site including `NotModified`, following `Filters/PlayerPollIntervalFilter.cs` exactly |

### `src/DXSuite.Host/`

| File | Change |
| --- | --- |
| `Program.cs` | **new** two startup assertions before `builder.Build()`, adjacent to the OIDC check at `:129-139`, reported as distinct failures, **outside all three role blocks**. Carry a comment citing DXME-170, because the throw looks trivially reversible and is not |
| `docs/README-customer-deployment.md:22`, `docs/README-NetJets-deployment.md:22` | One sentence each connecting the Windows Server 2019 floor to ICU. **Both files, accepting that this makes it three verbatim duplications instead of two** |

### `installer/`

| File | Change |
| --- | --- |
| `Host/dbtool/DXSuite.Host.DbTool.csproj` | **delete** `InvariantGlobalization` at `:25`, with a comment in the wizard's style saying why it stays absent |
| `Host/Package.wxs` | **No change.** The IIS launch condition at `:37-38` stays the only one. Read as a decision, not an omission |

### UI

| File | Change |
| --- | --- |
| `ConfigTabs/DevicesTab.razor` | **new** Time zone column, on by default, click-to-edit following Name (`:569`) and Description (`:625`), backed by the curated list. Renders `Seeded` in a muted or marked style. **new** `TimeZoneSource` column, sortable, off by default. **new** `Site`, `City`, `State` columns, off by default, plus a site picker. **new** verification badge, tri-state plus not-reported, following `contentSync` |
| **new** `ConfigTabs/SitesTab.razor` | `tblSite` CRUD. **StyleGuide `dx-panel` structure, `DevicesTab.razor` as the template** |
| **new** curated time zone list | Roughly twenty US and Canada rows: label, IANA id, POSIX literal. One source, read by the `DevicesTab` cell and by section 7's emission |
| `DirectorModule.cs` | Register `SitesTab` |
| `Components/Modals/MediaAssetUploader.razor` | `Times are each player's local time.` near the time inputs |
| `Pages/OverrideEditor.razor` | Same sentence |
| `Components/Schedulers/ScheduleEditor.razor` | Same sentence |
| `Components/LocationTypes/Playlist.razor` | Same sentence at the chips block (`:611`). `IsExpired` at `:886` **unchanged**, server clock, DXME-180 |
| `Components/Modals/LocationEditModal.razor` | **delete** the hardcoded `TimeZone = "UTC"` at `:979` |
| `Components/Home/DirectorDashboardCard.razor` | **new** fleet counter for the verification badge, following `contentSync` |
| `Components/Schedulers/Schedule.razor:705`, `Schedule_TVController.razor:526` | **delete** the time component of the Active badge. Date range and day-of-week stay |
| `StandardDisplay.razor:205-250`, `:281-322` | **No change.** Two near-identical copies of the selection rule, confirmed operator previews, **declined** for collapse under minimum footprint |

### The eleven `DateTime.Now` writes

_Carve-out from minimum footprint. One token each, no behavioural reach, no test
surface, no UI. Nothing reads `CreateDate` on any of the three tables today._

| Table | Sites |
| --- | --- |
| `TblConstraint` | `AssetLibrary.razor:220`, `CreateLayoutModal.razor:1732` and `:2862`, `LocationEditModal.razor:855` |
| `TblAsset` | `StreamAssetUploader.razor:247`, `TvSourceUploader.razor:308`, `WebContentUploader.razor:443`, `TemplateCreator.razor:973` |
| `TblScheduleEvent` | `StatusSelectorService.cs:350` and `:394`, `CBPMessages.razor:326` |

> **The existing rows cannot be corrected, ever.** They carry no marker recording
> which clock wrote them, and the two clocks coexisted across an unknown number of
> releases, so any `DATEADD` would corrupt as many rows as it repaired. A future
> reader finding a cluster offset by several hours should not go looking for a
> missing migration. Recorded in ADR 0002's consequences.

### Tests and CI

| File | Change |
| --- | --- |
| **new** `tests/DXSuite.Director.Tests/TimeZoneVectors.json` | Three arrays, section 11. Copied to output via the csproj |
| **new** xUnit facts in `tests/DXSuite.Director.Tests` | Section 11's second table |
| `.github/workflows/ci.yml` | **Precondition, see section 13.** Uncomment `dotnet test`, add `dev` to the triggers |

### Docs

| File | Change |
| --- | --- |
| `CONTEXT.md` | The Scheduling section is already on `dev` and is the vocabulary. A builder who settles a term writes it there |
| `docs/adr/0002-datetime-columns-instants-in-utc-and-wall-clock-bounds.md` | On branch `DXME-169-clock-storage-adr`, cut from `DXME-100-tracker-conventions`, commit `a30893c`. **Local and unpushed.** Lands with this work |

### Not in this work, and not accidentally

`ScheduleEvaluator` and the access path (DXME-176). Video walls and the whole
Pixera path. The SLC airline monitor. The ticket-counter and airline write path.
All five operator previews. `PanelService.cs:311`. The 81 SQL `(getdate())`
column defaults (DXME-183). `CBPMessages.razor:259`, which deletes content when
the authoring surface loads (DXME-184). `Playlist.razor:886`'s expiry badge
(DXME-180). Playlist-sourced layout zones ignoring schedules entirely
(DXME-191). Each carries its reasoning and, where it has one, its reopening
condition in the map's Out of scope section.

---

## 13. Open items

### One real precondition

**[DXME-185](https://radianttechnology.atlassian.net/browse/DXME-185): CI runs no
tests.**

The `dotnet test` step is commented out behind a stale "once test projects exist"
note while 12 test files exist, and the workflow fires only on `master`. **A
fixture merged while CI still skips `dotnet test` and still fires only on `dev`'s
absence from the triggers is a fixture that has never run.**

This is a named precondition of **DXME-158**, not a blocker on the map, so the
dependency sits one level down and a planning ticket is not parked behind an
execution ticket.

**Its price is measured and small.** `dotnet test` on `origin/dev` is green: 140
passed, 0 failed, 8 seconds. That spends DXME-185's "some may not currently pass"
caveat, so this is a YAML edit with no cleanup behind it.

### One item that blocks nothing but must be resolved before an operator uses the list

**Whether the POSIX literals in the curated list are correct.**
[DXME-196](https://radianttechnology.atlassian.net/browse/DXME-196), on the
DXME-120 map. Decision 32 deliberately did not decide this, along with the name
of the registry key the app writes, which no document anywhere states. Both are
player-half questions.

The emission mechanism (section 7) is unaffected either way: it carries whatever
string the list holds, verbatim, and a correction is one data edit.

### Two off-map tickets the decisions lean on

Neither blocks a build.

* **[DXME-198](https://radianttechnology.atlassian.net/browse/DXME-198):** the
  Devices grid cannot be filtered to problem rows. Section 8's badge works
  without it, on sort plus the dashboard counter, but decision 31's
  "filterable column" clause is not satisfiable until this lands.
* **[DXME-181](https://radianttechnology.atlassian.net/browse/DXME-181):** the
  upgrade-script pipeline. Section 10 states the requirement it owes; do not
  rebuild the pipeline here.

### The execution backlog

Nineteen tickets sit off-map in the DXME-174 to DXME-199 range, unsequenced,
several at P2. **Ordering them into a release is not this document's job.** Of
them, only DXME-185 and DXME-198 above are things a decision here leans on.
Everything else is a consequence of the model rather than a precondition of it,
and several (DXME-174, DXME-175) are repaired for free by the work in sections 4
and 5.

### Things measured once, not to be re-measured

* The existing suite is green: 140 passed, 0 failed, 8 seconds on `origin/dev`.
* Neither Engage repo has a test runner, and never has.
* None of the subject code exists yet. Zero `ConvertTimeFromUtc`,
  `FindSystemTimeZoneById` or `TryConvertIanaIdToWindowsId` under
  `src/DXSuite.Director`, and no `tblDevice.TimeZone` column.
* T-SQL cannot produce an IANA identifier. Measured on the dev Director database,
  SQL Server 15.0.4261.1.
* `America/Phoenix` resolves on the Host as shipped, with `SupportsDST=False` and
  zero adjustment rules. Arizona is sound at the framework level.
* A Director outage does not blank a screen. Players keep playing a stored
  manifest across a reboot.
* **Do not enumerate an object's prototype chain and invoke its members when
  probing a player.** Doing so wedged the bench HD1025 during DXME-161, past
  remote recovery, because a mutator-name denylist missed `reset`, `halt`,
  `shutdown` and `powerOff`.
