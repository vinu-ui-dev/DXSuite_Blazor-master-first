# Map: time zone correct scheduling across DXSuite

The body of the [DXME-159](https://radianttechnology.atlassian.net/browse/DXME-159)
wayfinder map. Moved out of the Jira epic on 2026-09-12, when that body hit
Jira's description size cap, following the same move made on the
[DXME-120](https://radianttechnology.atlassian.net/browse/DXME-120) map a day
earlier. `git pull` and read this before choosing a ticket.

**Nothing about the mechanics changed.** Tickets are still child issues of the
epic, blocking is still the native Jira link type, and a session still claims a
ticket by assigning it to the map owner before doing any work. The frontier is
still the open, unblocked, unassigned children of the epic, and it still renders
in Jira's own UI. Only the prose moved.

**Editing rule.** Edit this file and commit. Do not restore the body to Jira. A
decision lives in exactly one place, its ticket; this file only gists it and
links.

> **This map is closed, and it is no longer the thing to read first.** Every
> child ticket is resolved and the handoff spec is
> [`docs/spec/time-zone-scheduling.md`](../spec/time-zone-scheduling.md), written
> by [DXME-201](https://radianttechnology.atlassian.net/browse/DXME-201) on
> 2026-09-12. **Build from the spec.** Come back here only for a decision's
> reasoning, which the Decisions-so-far index below points at ticket by ticket.
> The fix itself is
> [DXME-158](https://radianttechnology.atlassian.net/browse/DXME-158).

## Destination

The time zone model for DXSuite scheduling is decided and written down: where a
device's authoritative time zone lives, who evaluates a schedule window and
against which clock, what an author is told, and how existing tenants upgrade
without a behaviour change. Every clock-dependent scheduling defect found in the
September 2026 survey is accounted for, either sequenced into an execution
ticket or explicitly ruled out of scope.

**No time zone is discovered from any device, on any flavour.** Every device's
time zone is set by a person in DXSuite Director.

**This is an interim measure, and device-driven time zones are the intended
destination.** Setting them in Director is the temporary and fallback path, not
the end state. That is a constraint on every modelling decision here: choose the
shape that device-driven discovery can later write into **without
re-modelling**, and do not build anything whose only justification is that a
person is typing the values today.

**The destination was widened once**, on 2026-09-12 by
[DXME-167](https://radianttechnology.atlassian.net/browse/DXME-167) at the user's
direction, to include a `tblSite` entity carrying a name and postal address. It
is not time zone work and it would not have arrived on its own, but a 90-device
list is unnavigable without it and the alternative homes were all wrong. See
decision 18, including the clause that a site carries no time zone.

This map plans. It writes no production code. The fix itself is
[DXME-158](https://radianttechnology.atlassian.net/browse/DXME-158).

## Notes

**Domain.** DXSuite Director (.NET 10 Blazor modular monolith,
`C:\CODE\DXSuite_Blazor`), plus the two Engage player flavours
(`C:\CODE\Engage_SignageOS`, `C:\CODE\Engage_BrightSign`), whose
`displayManager.ts` is byte-identical between them.

**Skills every session should consult:** `grilling` and `domain-modeling`.
`CONTEXT.md` carries a Scheduling section (Schedule, Schedule event, Daily
window, Local time, Display, Location, Device, Time zone, Request-scoped
evaluation, Tick-scoped evaluation, Operator preview, Published content,
Delivered content, Publication version, Delivery version, Daylight saving
transitions) on branch `DXME-164-window-glossary`. A session that settles a term
writes it there.

**Terminology, settled by [DXME-163](https://radianttechnology.atlassian.net/browse/DXME-163):
the term is "time zone", never the bare word "zone".** `Zone` is already taken in
DXSuite for a rectangular region of a video wall layout, in roughly 300
identifiers in `src/` (`ZoneNumber`, `LayoutZone`, `ZoneDefinition`,
`BackgroundZoneDerivationService`, `WallZoneController`) plus a `Zone Control`
location type. The column is `TimeZone` and prose never shortens it. Older
tickets on this map were written before this and still say "zone"; read them as
"time zone".

**The originating defect** is
[DXME-158](https://radianttechnology.atlassian.net/browse/DXME-158). It is
deliberately not a child of this map, so the frontier query stays clean.

### Standing preference: minimum footprint

Binding on every remaining ticket, set by the user during
[DXME-165](https://radianttechnology.atlassian.net/browse/DXME-165).

**A site changes only if a wrong answer there is visible on a screen.**
Everything else is documented as server-clock and left alone. No unneeded
changes to high-visibility surfaces, where the risk of touching working code
exceeds the value of consistency. Re-evaluate what is being touched rather than
assuming the survey's full inventory is the work.

The cost is that the codebase keeps two conventions for a while. That is
acceptable **only because the boundary is written down**: every out-of-scope
item on this map carries a reopening condition, so the next reader does not
mistake an untouched site for the intended pattern.

**Corollary, added by [DXME-166](https://radianttechnology.atlassian.net/browse/DXME-166):
an optimization is not a licence to widen scope.** DXME-166 found a worthwhile
cache saving, discovered it was gated on a cache-invalidation audit across three
write surfaces, and declined to take either. The optimization was filed as
[DXME-178](https://radianttechnology.atlassian.net/browse/DXME-178) rather than
ridden into a time zone ticket.

**Two deliberate carve-outs, both from [DXME-169](https://radianttechnology.atlassian.net/browse/DXME-169).**
Eleven `CreateDate = DateTime.Now` writes become `UtcNow`, and one `UtcNow` read
in `ScheduleExpiryNotificationService` becomes the host local clock, even though
neither produces a wrong answer on a screen. The test that let both through is
the same one: each is a single token in code with no behavioural reach, no test
surface and no UI. The preference exists to keep working high-visibility code
untouched, and a one-word change to a stamp is not that.

Read the preference as **do not touch a working surface for consistency**, not
as do not fix anything invisible. DXME-169 also declined the larger consistency
work by exactly this test, filing 81 SQL column defaults off-map rather than
migrating them.

### The deployment this map serves

Settled by [DXME-163](https://radianttechnology.atlassian.net/browse/DXME-163)
and binding. **One Director deployment**, serving a **single Playlist location**
that manages every screen of one type across every service center, spanning
Central, Mountain and Arizona simultaneously, at roughly **90 players**.
Requiring a display row per time zone, or a playlist per time zone, would
duplicate authoring effort per zone and is **not an acceptable outcome**. The
authoring surface stays single and each device's own time zone is respected.

**The hardware, identified by
[DXME-173](https://radianttechnology.atlassian.net/browse/DXME-173).** Those
players are **BrightSign HD225** units, Series 5, essentially an HD1025 minus
some I/O, on the `WINGSCAROUSEL` BSN.cloud network, running **Carousel** today
and standing as takeover candidates for Engage for BrightSign. **They are not
the 89 HD1025s** that `Engage_BrightSign/docs/map-facts.md:137` records in Pulse
Core: two different BrightSign fleets, and DXME-173 conflated them once already.
No HD225 has ever been on the bench, so every BrightSign measurement on this map
was taken on a sibling model and carries `[INFERRED for HD225]`. Verification
happens on a customer lab player and does not block this map.

`Playlist.razor` contains zero references to `TblDisplay`, `DisplayId` or
`TblScheduleEvent`, so this deployment does not use the display path at all.

## Settled before charting, and binding on every ticket

1. A time entered in Director means that wall-clock time in the **player's own
   local time zone**. Always player-local, never a reference zone.

2. **The time zone lives on `tblDevice`.** A plain, non-nullable `TimeZone`
   column, IANA, SQL-updatable. Settled by
   [DXME-163](https://radianttechnology.atlassian.net/browse/DXME-163), which
   **overturned** the original decision that it lived on the display. Neither
   Location nor Display is a place: a DXSuite Location is a content role
   (`Weather`, `Airline Logos`, `Center Content`) and a Display is a canvas
   format (`Landscape`, `Portrait 1`). `tblDevice` is the only row in the schema
   that is unambiguously somewhere. It is also exactly what device-driven
   discovery later writes into.

3. **No runtime fallback, anywhere.** A default may be copied into a row at
   create time. Nothing resolves a missing time zone at evaluation time. A
   silent resolution to the server's clock is the defect being fixed, so
   shipping a smaller one as the fix is not available.

   **One narrow carve-out, by [DXME-168](https://radianttechnology.atlassian.net/browse/DXME-168):**
   when a stored identifier does not resolve, the server logs loudly and serves
   the manifest **unfiltered**. That picks no clock, so it is not a fallback in
   the sense this decision forbids; it declines to filter, and fails toward a
   screen that is showing something. See decision 22.

4. **IANA is the canonical stored identity** (`America/Chicago`,
   `America/Phoenix`), translated at each edge. Confirmed workable by DXME-162
   on the Host as shipped, including Phoenix. Its silent-failure modes on the
   Director are [DXME-170](https://radianttechnology.atlassian.net/browse/DXME-170).
   The translation-at-each-edge clause is what makes device-driven time zones
   droppable in later without a format migration. Keep it.

5. **One consumer, not two.** Settled by
   [DXME-163](https://radianttechnology.atlassian.net/browse/DXME-163): **the
   server resolves every window** against the requesting device's stored time
   zone and ships only currently-active content, with the four window fields
   (`StartDate`, `EndDate`, `StartTime`, `EndTime`) set to **null**. Because
   `isAssetScheduled` returns true when all four are absent, that disables the
   player's own filter, so the device's OS clock stops affecting content
   selection entirely. This replaces the earlier position that two independent
   consumers evaluated and could silently disagree. It needs **no player release
   on either fleet**.

   **Qualified by [DXME-165](https://radianttechnology.atlassian.net/browse/DXME-165):**
   on the playlist path the server does not evaluate these fields **at all**
   today, it forwards them to the device. So implementing this is a _new_
   server-side evaluation, not a corrected one. Larger than this decision
   implied, but it also repairs
   [DXME-174](https://radianttechnology.atlassian.net/browse/DXME-174) for free.

   **Extended by [DXME-166](https://radianttechnology.atlassian.net/browse/DXME-166):**
   day-of-week resolves server-side too, in the device's time zone. See decision
   15.

6. **DST rule:** nonexistent times shift forward, ambiguous times take the first
   occurrence. Stated once, in the glossary. DXME-164 established that window
   evaluation never needs this rule, because it only ever converts instant to
   local, which is total and cannot throw. The rule bites only going local to
   instant.

   **Closed by [DXME-166](https://radianttechnology.atlassian.net/browse/DXME-166):
   it has no consumer on this map at all.** DXME-166 was given ownership because
   the one local-to-instant conversion was believed to be its TTL; the cache
   split (decision 16) removed that conversion. The rule is written into the
   glossary anyway, with the no-consumer fact attached, because the first person
   to ask "when does this window next open" will need it.

7. **The upgrade is behaviourally inert** *only when the operator step runs.*
   Every existing deployment is single-zone. The migration adds
   `tblDevice.TimeZone` and an operator-supplied `UPDATE` sets every device to
   that deployment's zone.

   **Amended by [DXME-168](https://radianttechnology.atlassian.net/browse/DXME-168).**
   T-SQL cannot compute an IANA identifier, so the migration cannot derive the
   value and inertness cannot be automatic. When the operator step is skipped
   the column holds a **sentinel that does not resolve**, not a plausible zone:
   a plausible literal would be inert only for a deployment that happens to
   match it, and silently wrong content is the defect this map exists to
   remove. The sentinel trades inertness for loudness.

   `tblDisplay.TimeZone` **is dropped**, in the same migration. See decision 20.

8. First deployment spans Central, Mountain and **Arizona**, all going live
   simultaneously, in one Director. Arizona does not observe DST, so it tracks
   Mountain in winter and Central in summer and no stored offset can be correct
   year-round. There is no time-based content at go-live, so this is not a
   launch blocker, but the seasonal-drift case has to be right by design rather
   than by luck.

9. **Acquisition versus resolution.** There is exactly **one** acquisition path:
   a person sets the time zone in Director, over a value seeded at device-row
   creation. The **resolution** path has none: the device has exactly one stored
   time zone and the evaluator reads only that. The distinction still matters,
   because "Director-set is the fallback" is a statement about acquisition and
   says nothing about resolution. Conflating the two reintroduces the defect
   this map exists to fix. When device-driven time zones arrive they are a
   **second acquisition path**, and decision 3 still forbids a resolution-time
   fallback.

10. **A daily window means one thing everywhere.** Start inclusive, end
    exclusive, a start later than the end wraps midnight, an end at or after
    23:59 means through the end of the day, a missing start means 00:00 and a
    missing end means end of day. Settled by DXME-164 and written into the
    glossary. The rule is a **pure** function of a local time and a window: it
    reads no clock, takes no time zone, and has no DST behaviour.

11. **The time zone is a plain column, updatable by SQL.** Not an EAV row, not a
    key in a JSON blob, not a separate mapping table. Bulk assignment is a
    script against `tblDevice.TimeZone`, so no bulk-edit UI is in scope. This is
    also what keeps a later discovery mechanism cheap: one column to write.

12. **Never unset.** `tblDevice` rows are created **automatically** on first
    full heartbeat (`HeartbeatService.cs:286-313`) with no operator present, so
    an unset state would be guaranteed. Settled by
    [DXME-163](https://radianttechnology.atlassian.net/browse/DXME-163): the
    column is seeded at row creation with the Director host's time zone,
    **converted to IANA** (not `TimeZoneInfo.Local.Id`, which on Windows yields
    `Eastern Standard Time`). Never null, so no evaluator carries an unset
    branch. A device that is never commissioned plays on the server's clock,
    which is accepted: unlike a runtime fallback the wrong value is queryable
    and one `UPDATE` fixes a whole service center.

    **Corrected by [DXME-168](https://radianttechnology.atlassian.net/browse/DXME-168):
    there are four row-creation paths, not one.** `HeartbeatService.cs:290`,
    `CloudConnectorsTab.razor:853-867`, `CloudConnectorSyncService.cs:444-464`
    and `CloudConnectorSyncService.cs:555-569`, the last two in a different
    assembly. The seed therefore lives at **one choke point every path calls**,
    not inline in each. See decision 21.

13. ~~**The manifest cache key carries the time zone.**~~ **OVERTURNED by
    [DXME-166](https://radianttechnology.atlassian.net/browse/DXME-166).** The
    key stays `(tenant, locationId, filter)` with no time zone, and there is one
    cached manifest per location, not three.

    Decision 13 was correct for a cache holding **resolved** content. Decision
    16 moves the cache boundary so the cached artifact is the **pre-filter**
    build, which no clock touches, so the time zone would be a key component
    that never changes the value it keys, tripling cache cardinality and rebuild
    cost for identical bytes.

    **Its overturn now holds on all three manifest paths, for the same reason
    each time.** [DXME-179](https://radianttechnology.atlassian.net/browse/DXME-179)
    put the display and layout caches through the same boundary move, which is
    decision 30, so their artifacts became clock-free as well. No manifest cache
    key anywhere gains a time zone. Read decision 13 as a statement about what a
    cache holds, not about one path.

14. **Four rules cover every clock-dependent site, and only five sites are in
    scope.** Settled by
    [DXME-165](https://radianttechnology.atlassian.net/browse/DXME-165).
    (a) _Request-scoped with a device in hand_ resolves against
    `tblDevice.TimeZone`. (b) _Tick-scoped sweeps_ stop evaluating windows; the
    decision moves to request time. (c) _Location-scoped with no device_ (video
    walls) is out of scope. (d) _Operator previews_ are out of scope. Rule (b)
    exists because a tick service asking "is it inside the window now?" is
    exactly the second consumer decision 5 abolished.

15. **Day-of-week stays server-side.** Settled by
    [DXME-166](https://radianttechnology.atlassian.net/browse/DXME-166),
    rejecting the map's earlier working position that it move to the player.
    Moving it would reinstate the second consumer decision 5 abolished; the
    player has **no timer that re-runs its schedule filter**
    ([DXME-174](https://radianttechnology.atlassian.net/browse/DXME-174)), so it
    could not self-correct at its own midnight, which was the entire motivation;
    and `Asset` in `types.ts` has no `days` field, so it would cost a schema
    change plus an applet release.

16. **Publication and delivery are separated, and the cache holds publication.**
    Settled by
    [DXME-166](https://radianttechnology.atlassian.net/browse/DXME-166). The
    manifest cache stores the **unfiltered** playlist build, which is zone-free
    and clock-free; the day-of-week and window filter runs **per request** in
    the requesting device's time zone, outside the cache. The expensive work
    (media folder walk, sidecar parsing, one awaited database query per file)
    stays cached; the filter, sort and weight expansion are per-request
    in-memory passes.

    Consequently the cache TTL is a pure freshness backstop and is load-bearing
    for nothing, window-boundary latency is one poll interval, no exact
    next-boundary arithmetic is needed anywhere (which is what closed decision
    6), and the time zone stays out of the cache key (which is what overturned
    decision 13).

    **Publication version** and **delivery version** become distinct concepts:
    publication identifies what a location publishes and is what the content
    manifest log records; delivery identifies what one device should be showing
    now and is what drives the player's `NotModified`. Both are in the glossary.
    Keeping them as one field would make every window boundary write a spurious
    audit row per time zone.

17. **The clock follows the authentication scheme.** Settled by
    [DXME-167](https://radianttechnology.atlassian.net/browse/DXME-167),
    sharpening decision 14's rules (a) and (c) from a per-site judgement into a
    mechanical test at the controller boundary. A caller holding a **device
    token** is a device, so its stored time zone decides. A caller holding an
    **API key** is an external integration with no device, so the server's clock
    decides, exactly as today. `GetPlayerContent`
    (`SignageController.cs:367`) is device-token only; `GetAirline`,
    `GetMessages` and `GetAirlineStatus` are API-key only; `GetDisplays`
    (`:110`) accepts both and is the one endpoint that must branch. This
    generalises rule (c) past video walls to every `signage:read` integration.

18. **Sites are a first-class entity, and they carry no time zone.** Settled by
    [DXME-167](https://radianttechnology.atlassian.net/browse/DXME-167), which
    **widened this map's destination** at the user's direction: `tblSite`
    (`SiteId`, `CustomerId`, required `SiteName`, nullable `Address1`,
    `Address2`, `City`, `StateProvince`, `PostalCode`, `Country`) with a
    nullable `tblDevice.SiteId`, many devices to one site, plus a `SitesTab`
    config tab. All of it optional, because an airport has no use for it.

    Two clauses are binding. **`tblSite` holds no time zone**: decision 2 stands
    and it lives on `tblDevice`, because a site entity is exactly what DXME-163
    examined and rejected as a time zone home, and moving it there would break
    device-driven discovery. **Nothing derives a time zone from the address**:
    no state or postal-code lookup, which decision 3 forbids and which Arizona
    would break anyway.

    Note the asymmetry with the time zone: a device will one day report its own
    time zone, but **no device will ever report its own street address**, so
    site assignment is manual on every deployment, permanently.

19. **A Display is not a place and has no time zone.** Settled by
    [DXME-167](https://radianttechnology.atlassian.net/browse/DXME-167), which
    considered and **rejected** an invariant that every device under one display
    shares a time zone. A Display is a collection of content set by the schedule
    editor. The devices attached to it each carry a time zone and each must be
    respected, so the display path needs per-device resolution exactly as the
    playlist path does. Because `SignageController.cs:558` caches the display
    manifest keyed by `displayId` with no device in the key, it cannot do that
    today, which is
    [DXME-179](https://radianttechnology.atlassian.net/browse/DXME-179).

    **Settled by [DXME-179](https://radianttechnology.atlassian.net/browse/DXME-179),
    as decision 30.** The case to design for is many devices under one display
    spread across several time zones, each respecting the schedule in its own.
    DXME-179 also found the live defect sitting underneath the multi-device one:
    a display whose devices all agree on a single time zone is still evaluated
    against the Director's clock, so this is wrong on a single-device display
    too. The multi-device case is real but latent, and the single-zone case is
    what earns the change under the minimum footprint preference.

20. **Two migrations, no persistent default, and `tblDisplay.TimeZone` is
    dropped.** Settled by
    [DXME-168](https://radianttechnology.atlassian.net/browse/DXME-168).
    Migration one adds `tblDevice.TimeZone` (`varchar(50)`, `Unicode(false)`,
    non-nullable) and drops `tblDisplay.TimeZone` with its only writer at
    `LocationEditModal.razor:979`. **It also carries `tblDevice.TimeZoneSource`,
    added by decision 31**, which follows the same no-persistent-default shape
    for consistency rather than for decision 20's own safety reasoning. Migration two adds `tblSite` and a nullable
    `tblDevice.SiteId` (`DeleteBehavior.SetNull`, `SiteName` not unique,
    `IX_tblDevice_SiteId`). They ship in one release but stay separate so the
    site work can be dropped without unpicking the time zone work.

    The column ends **non-nullable with no default**: `AddColumn` carries a
    one-shot default for existing rows and the migration drops the constraint.
    A persistent default is a second, silent acquisition path. This is house
    style rather than a departure, since `tblDevice` has no column defaults at
    all except `CreateDate`.

21. **The seed lives at one choke point.** Settled by
    [DXME-168](https://radianttechnology.atlassian.net/browse/DXME-168). One
    provisioning helper that all four creation paths (decision 12) call, using
    `TimeZoneInfo.TryConvertWindowsIdToIanaId`. **It writes two columns**, since
    decision 31 makes this helper the only writer of `TimeZoneSource = Seeded`. This is what makes decision 20's
    "no persistent default" safe: a path that skips the helper throws on insert
    and surfaces in development, instead of silently stamping a wrong zone on a
    real player.

22. **An identifier that does not resolve serves unfiltered content.** Settled
    by [DXME-168](https://radianttechnology.atlassian.net/browse/DXME-168),
    as the carve-out recorded on decision 3. Catch, log loudly, serve everything
    including out-of-window content. Rejected: throwing (a blank screen at a
    customer site is a truck roll) and serving an empty manifest. Safe only
    because of decision 5, which nulls the window fields so the player does not
    re-filter. This is also what the migration sentinel and the failed-seed
    sentinel land in, so neither adds a branch.

    **Qualified twice by [DXME-170](https://radianttechnology.atlassian.net/browse/DXME-170),
    and read it with both qualifications.** First, **the loud half does not
    work**: a log line is very nearly invisible on an on-prem install, so the
    only channel that survives is a passive Windows event log nobody reads until
    they already suspect a problem. The serve-unfiltered half works as written.
    Second, **this path is not the intended handler for fleet-wide loss.** A bad
    row is a data defect, one device and one cell, and decision 22 is right for
    it. A dead ICU is a deployment defect that routes every device through this
    catch at once, which no operator edit fixes, and at 90 players on the 180s
    poll floor it writes roughly 43,000 entries a day into a size-capped log,
    evicting other diagnostics while the incident is happening. That case is
    decision 25's, and it is detected once at startup instead.

    **Generalised by [DXME-179](https://radianttechnology.atlassian.net/browse/DXME-179):
    read it as decline to discriminate, and fail toward whatever that path's own
    default is.** "Unfiltered" means something only where content is a list being
    filtered. The display path selects one event instead, so its way of declining
    is to serve the display's `DefaultContent` event, which already exists at
    `DisplayItem.cs:81-85` and needs no new branch. Rejected the literal reading,
    taking the last non-default event regardless of its window, which would put a
    lunchtime promotion on screen at midnight. The guarantee is conditional here:
    a display authored with no default shows nothing, which is equally its
    behaviour whenever no event matches.

23. **Nothing new validates the identifier.** Settled by
    [DXME-168](https://radianttechnology.atlassian.net/browse/DXME-168). No
    CHECK constraint and no new attribute: the schema has **zero** CHECK
    constraints across all 13 migrations and `src/` has zero
    `[RegularExpression]` or `[AllowedValues]`, and a list of allowed IANA
    identifiers would rot as the IANA database changes. DXME-167's curated list
    covers the operator path; decision 11's bulk script is a trusted-operator
    tool by design. Decision 22 is what catches what gets through.

24. **Every persisted `DateTime` is an instant or a wall-clock bound, and the
    two are never compared.** Settled by
    [DXME-169](https://radianttechnology.atlassian.net/browse/DXME-169) and
    written up as ADR 0002. An **instant** is a single moment, the same moment
    everywhere, and is what every audit and lifecycle stamp is; it is stored as
    UTC. A **wall-clock bound** is a date or time marking the edge of a
    schedule, stored exactly as authored, carrying no time zone, and it does not
    identify a moment on its own. Code that needs to know whether a bound has
    passed resolves it against a particular device's time zone first, which
    yields an instant, and compares that.

    The never-compare clause is the load-bearing half. The map's earlier working
    position, that an audit stamp is UTC and a schedule window is zone-naive,
    would have left both of DXME-169's live defects untouched, because neither
    is a stamp.

    This decision governs **what is written to a column and what may be compared
    to what**. Which clock a decision is read against is decisions 14 and 17,
    and is deliberately not restated in the ADR.

25. **The Host throws at startup if IANA resolution is unavailable.** Settled by
    [DXME-170](https://radianttechnology.atlassian.net/browse/DXME-170). Before
    `builder.Build()`, adjacent to the OIDC check and following
    `Program.cs:129-139` in placement and message style, the Host asserts two
    capabilities and throws `InvalidOperationException` if either fails.

    **Two assertions, because the two APIs fail differently.**
    `FindSystemTimeZoneById("America/Chicago")` must resolve, using a
    **hardcoded** id so this is a capability test and not a data test; a stored
    or configured id would conflate a dead ICU with a mistyped zone, which is
    decision 22's job. And `TryConvertWindowsIdToIanaId` must return true for the
    host's **own** `TimeZoneInfo.Local.Id`, which is the exact conversion
    decision 21 performs at every device-row creation, so it also catches a host
    zone with no IANA mapping that no constant would reach. That helper degrades
    to `false` with an empty out-parameter rather than throwing, so it fails
    more quietly than the throw does. Reported as distinct failures.

    **Unconditional across all three `DXSuite:Role` values.** Gating on
    `isWebRole` fails because `CloudConnectorSyncService` is a
    `BackgroundService` holding two of decision 12's four row-creation paths, so
    a worker creates device rows and needs the conversion; `HeartbeatService` is
    both `IHostedService` and injectable, so its row creation lands in either
    role; and on-prem runs `standalone` and needs both anyway. The only existing
    throw in the file is likewise outside both role blocks.

    **Why throwing, when decision 22 rejected it.** Decision 22 rejected
    throwing for one bad row, where a blank screen is a truck roll, and that
    still holds. Fleet-wide it inverts, because the two outcomes are not
    symmetric and both axes point the same way. A Host that will not start blanks
    nothing: players fall back to a stored manifest and survive a reboot on it,
    so the fleet freezes on **last-known-correct** content and the outage is
    visible. A Host that starts without ICU serves **incorrect** content
    immediately, fleet-wide, and invisibly, since decision 8 records no
    time-based content at go-live. The authoring UI going dark costs nothing
    here, because every remedy is on the box and none is reachable from the UI.
    Rejected logging only (indistinguishable from the present silence) and
    self-writing `app_offline.htm` (operator-driven, out of process, and its page
    has no slot for a reason).

26. **Four causes, and each gets the instrument that actually covers it.**
    Settled by [DXME-170](https://radianttechnology.atlassian.net/browse/DXME-170),
    replacing "detect it somehow" with a named cause per instrument. A future
    `InvariantGlobalization=true` on the Host or a publish profile is caught by a
    **CI guard** asserting the built Host's `runtimeconfig.json` carries no
    `System.Globalization.*` key, written against the
    `has-pending-model-changes` precedent rather than as a test. A machine or
    app-pool `DOTNET_SYSTEM_GLOBALIZATION_USENLS` is caught by decision 25 and by
    nothing else, ever. An OS below Server 2019 is caught by decision 25 plus one
    documentation sentence. DbTool's own invariant mode is decision 27.

    The corollary is that **no instrument is a substitute for another**, so
    dropping one silently uncovers its cause.

27. **`InvariantGlobalization` comes out of DbTool, and no migration computes a
    time zone identifier in C#.** Settled by
    [DXME-170](https://radianttechnology.atlassian.net/browse/DXME-170). The
    property at `installer/Host/dbtool/DXSuite.Host.DbTool.csproj:25` has no
    recorded justification anywhere in the repo, was authored in the same commit
    (`2191320`) as the wizard's `false` which **did** get a three-line comment,
    and costs nothing to remove because `SelfContained=false` means ICU comes
    from the shared runtime, making invariant mode a behaviour switch and not a
    payload switch. Its deletion carries a comment in the wizard's style saying
    why it stays absent.

    The constraint matters more than the deletion and outlives it. DbTool touches
    **no** device-row-creation path, migrates one context and seeds only T-SQL,
    but it **is** the process that executes every EF migration. Decisions 7 and
    20 already make the one-shot default a literal sentinel, so nothing breaks
    today; the constraint stops someone later making it computed and finding out
    in a rolled-back install on a production host, where `Return="check"` fails
    the MSI and the DbTool log lands in the SYSTEM temp folder. It also holds on
    the hand-run SQL upgrade path, where `TimeZoneInfo` does not exist at all.

28. **Detection lives in the runtime, never in the installer.** Settled by
    [DXME-170](https://radianttechnology.atlassian.net/browse/DXME-170), on two
    independent arguments. An OS version check is a **proxy** for the capability
    and a poor one: it passes a Server 2022 box with the NLS environment variable
    set, which is the most invisible cause of the four, and fails a box that
    would work. And the installer reaches only one of two documented upgrade
    paths, while the runtime is the only thing that certainly executes on the
    Director host under both.

    So the IIS launch condition at `installer/Host/Package.wxs:37-38` stays the
    only one, and it is the odd one out for a good reason: IIS presence is
    machine state with no runtime equivalent, and ICU availability has one. Read
    this as a decision, not an omission.

29. **Per-request window resolution is affordable, and nothing on this map may
    be justified by its cost.** Settled by
    [DXME-177](https://radianttechnology.atlassian.net/browse/DXME-177), which
    measured decision 16's per-request pass rather than estimating it. The
    filter, meaning the actual time zone work, is **3.4 microseconds** at 100
    assets: 1% of a 358-microsecond pass, and that pass is **0.32% of one core**
    at 90 players in the worst plausible cadence. One core carries roughly 28,000
    players at the fastest poll the system allows.

    This is binding in one direction only. **No ticket may add a cache, a
    memoised delivery or a shortened evaluation on capacity grounds**, because
    the capacity is not in question and decision 16 exists precisely to keep a
    TTL from becoming load-bearing for correctness. The known optimizations are
    filed as [DXME-188](https://radianttechnology.atlassian.net/browse/DXME-188)
    and are explicitly not prerequisites.

    Two things inside the pass are worth carrying forward anyway, neither being
    time zone work. The **delivery-version hash is 80%** of it, so the per-device
    delivery version decision 16 introduced costs far more than the filter it
    was introduced to serve. And `WeightedLoopExpander.Expand` is **O(n
    squared)**, the only superlinear term anywhere in the path, invisible at
    these sizes and the first thing to bite if a publication grows.


30. **The display and layout manifests cache the publication and resolve the
    delivery per request.** Settled by
    [DXME-179](https://radianttechnology.atlassian.net/browse/DXME-179),
    extending decision 16 to the two remaining manifest paths so that all three
    now have one shape: cache the publication, resolve the delivery per request.
    The cached artifact becomes the display, its schedule events and the assets
    for all of them, none of which a clock touches; the active event is selected
    per request against the requesting device's local time. On the layout path
    only the display-sourced zones move, because playlist-sourced zones read no
    clock at all today, which is
    [DXME-191](https://radianttechnology.atlassian.net/browse/DXME-191).

    **The seam is a local wall-clock time, not an instant and not a time zone.**
    `DisplayItem`'s existing `at` parameter is renamed `localNow` and documented
    as the local wall-clock moment to evaluate against, in whatever time zone the
    caller chose. That is already its meaning at its only live caller,
    `EffectiveContentResolver.cs:218`. Rejected passing a time zone instead,
    which breaks the operator preview, since that wants to simulate a time rather
    than a place.

    **One conversion helper, and it is where decision 22's catch lives on this
    path.** It takes the device's stored identifier and returns either a local
    wall-clock time or an explicit unresolved result, never throwing, so the
    choice of what to serve stays with the caller that knows its own default.
    Decision 21's argument for a single choke point transfers exactly, and it
    matters more here because decision 22's loud half barely works, so a
    duplicated catch doubles the chance one copy is silent.

    **The publication version feeds the audit log and the delivery version is
    hashed per request**, exactly as decision 16 set out. Rejected composing the
    delivery version as publication version plus active event id: that pair is
    genuinely exact on this path, and it is still rejected because its only
    justification is cost, which decision 29 forbids.

    Two further rejections worth keeping. **Keying the cache by time zone** would
    have vindicated decision 13 rather than contradicted its overturn, since this
    artifact was not clock-free, but it scales cache entries with the number of
    time zones under one display and makes the 30 second TTL load-bearing for
    window boundaries again. **Dropping the display cache** would be correct by
    construction with the smallest change of the three, and loses because three
    read-only consumers render display status from those entries
    (`Pages/Location.razor:306`, `Components/Home/DirectorDashboardCard.razor:240`,
    `ConfigTabs/DevicesTab.razor:725`).

    Boundary latency is one poll interval, accepted on DXME-166's reasoning and
    an improvement on today regardless, since the boundary is currently gated by
    the TTL and evaluated against the wrong clock at the same time.

    Scope: `SignageController.cs:1300`, the layout zone build, is **in**.
    `PanelService.cs:311` is **out**, an operator surface under decision 14(d).
    **Reopening condition:** a panel becoming a playback surface rather than an
    operator view.

31. **A time zone is emitted to a device only when an operator set it, and
    provenance is what records that.** Settled by
    [DXME-192](https://radianttechnology.atlassian.net/browse/DXME-192). A second
    non-nullable column, `tblDevice.TimeZoneSource`, carrying `Seeded` or
    `Operator` today with `Discovered` reserved and deliberately not shipped.
    **Only `Operator` is emitted.** Everything else sends no zone field at all,
    which lands in the player's existing rule that a missing value means nothing
    has told it yet.

    This is not decision 12's unset branch returning, and the distinction is
    decision 9's. Confirmation is an **acquisition-path** fact; decision 12's
    never-unset is a **resolution-path** guarantee. **No evaluator reads this
    column**, so decision 5 is untouched and the server still resolves every
    window against `tblDevice.TimeZone` whatever its provenance. The column
    governs one thing only: whether the value is good enough to send out.

    **Provenance rather than a `TimeZoneConfirmedUtc` timestamp**, on the
    destination's own constraint. A timestamp cannot say what confirmed it, so
    discovery would have to stamp itself confirmed or earn a second column. Same
    one-column footprint, and it is the shape discovery writes into without
    re-modelling. No timestamp: both live consumers only ask which value it is.

    **Who writes what, stated per writer**, because a single-exception rule
    breaks the moment discovery exists. The decision 21 seed helper writes
    `Seeded`; discovery will write `Discovered`; **every other write path writes
    `Operator`**, which includes decision 11's bulk script and DXME-168's
    operator backfill. The backfill counting is load-bearing rather than
    incidental: those are upgraded single-zone deployments where a human did
    choose the value, and without it they would stay permanently unconfirmed and
    never push.

    **Migration one**, alongside `tblDevice.TimeZone`, non-nullable with no
    persistent default and a one-shot `Seeded` for existing rows. That follows
    decision 20's pattern **on consistency alone, and only half of decision 20's
    reasoning transfers**: a persistent `Seeded` default would not be a second
    silent acquisition path, it is the fail-closed value, and decision 21's
    insert-throws-in-development property is carried by `TimeZone` having no
    default regardless. Existing rows are correct by construction, since decision
    7 leaves the migration sentinel in `TimeZone` and a sentinel is definitionally
    not an operator's choice. The sentinel earns **no fourth value**: nothing
    behaves differently for it.

    **The operator sees it in one cell and nowhere else.** DXME-167's `DevicesTab`
    Time zone cell renders an unconfirmed value in a muted or marked style, and
    `TimeZoneSource` becomes a filterable column, off by default. No banner, no
    commissioning dashboard, no count. **DXME-167's no-confirmed-flag clause is
    amended, not overturned**: sort and filter remain the commissioning
    mechanism. What changed is that its stated cost, an integrator who does 88 of
    90 not being told about the 2, now also means those 2 never get their OS time
    zone set, so the state is load-bearing for a behaviour rather than for data
    quality alone.

    **The POSIX string ships here**, as a literal third field on DXME-167's
    curated list, roughly twenty rows. An off-list zone set by SQL has no POSIX
    string and therefore emits nothing, which is decision 22's shape: server-side
    resolution is unaffected because it reads the IANA value regardless.

    **The honest cost is decision 23's.** Nothing validates the column, so a
    careless script can write a real zone and leave `Seeded` behind. The symptom
    is a device that quietly never gets pushed, which is the safe direction and
    also the invisible one.

    Rejected: deriving the state by comparing the stored zone against the
    Director host's current zone, which is free and needs no schema change. It
    reads a device genuinely in the Director's own zone as unconfirmed forever,
    and it silently flips meaning the day someone moves the host. Also rejected
    on facts rather than judgement: an audit-derived answer (`tblDevice` has no
    change history, `tblPlatformAuditLog` is a different database and records
    elevation events, and `TblEventLog` has no field-edit writer and is purged at
    365 days), `ExtendedData` (the DXME-47 clobber race) and `tblDeviceMetadata`
    (EAV, already rejected by DXME-163).

    **Not generalised to `tblSite`.** `tblDevice.SiteId` is nullable, so site
    assignment has a natural unset state and needs no equivalent column. Only the
    time zone has this shape, and only because decision 12 made it non-nullable
    with a seed.

    The wire format is settled by
    [DXME-195](https://radianttechnology.atlassian.net/browse/DXME-195). See
    decision 32.

32. **The OS time zone rides `PlayerContentResponse` as `osTimeZone`, outside the
    delivery-version hash, stamped by a result filter.** Settled by
    [DXME-195](https://radianttechnology.atlassian.net/browse/DXME-195). One
    endpoint carries it, `POST /api/Signage/PlayerContent`, which is device-token
    only (`SignageController.cs:365`). No API-key endpoint carries it, and
    `GetDisplays` does not branch for it: decision 17's branch there is about
    which clock resolves a window, not about this field.

    **The heartbeat and the manifest are the same object**, so the question this
    ticket was charted with, manifest body or heartbeat response, was a choice
    between two names for one response. The real boundary inside it is the
    delivery-version hash, and `osTimeZone` sits **outside** it, in the family
    `PlayerContentResponse` already has: `ConsoleLoggingEnabled`,
    `ForceHtml5Video`, `AllowHttpFallback`, `WindowMode`, `DeviceMetadata`. All
    per-device, none in `ComputeVersion`.

    **A time zone is state, not an event**, which is what ruled out a
    `tblDeviceCommand` type alongside `cec`, `screenshot` and `flushCache`. A
    command is delivered once, expires, and is lost for good if the player was
    offline past its window or its card is re-imaged. State re-asserted on every
    poll self-heals. The field costs no query and no cache entry, because
    `GetPlayerContent` already holds the device row.

    **It must ride the `NotModified` path**, which is the normal path for almost
    every poll, and it joins that response's documented "command fields are still
    populated and must be processed" set. Getting this wrong would deliver the
    zone only to players whose content happened to be changing.

    **A result filter, not nine hand-copied object literals.**
    `GetPlayerContent` builds nine `PlayerContentResponse` literals (`:413`,
    `:507`, `:541`, `:572`, `:686`, `:715`, `:839`, `:1050`, `:1071`), eight with
    a device row in hand. `:541` is the "No location or display assigned" branch,
    which is the state of a device just commissioned and not yet given content,
    and therefore exactly the device whose zone an operator has just set. The
    codebase already solved this: `PlayerPollIntervalFilter` stamps
    `NextPollSeconds` and `FullInventoryMinutes` on every branch and its own doc
    comment says it exists "so the many return sites in GetPlayerContent stay
    untouched". The action stashes the resolved value where the device row is
    loaded; the filter stamps it.

    **The field carries the complete `SetTimeZone` argument**, `POSIX:MST7` and
    not `MST7`, and the curated list's column holds the same string under the same
    name. One value, no transformation at any hop, so a correction is one data
    edit. Same principle DXME-153 used to put the config-fingerprint computation
    on DXSuite rather than the player.

    **No flavour gate.** `TimeZoneSource = Operator` is the only gate and one gate
    is the right number. Both clients parse the response field-by-field off an
    untyped value with no schema validation, inside a `try` that degrades to an
    empty result (`assetSync.ts:171-307`), and `assetSync.ts` is **byte-identical**
    between the two Engage repos, so an unknown field provably cannot break either
    one. The ticket's "harmless or gated" trade had no subject. A `PlatformId`
    gate would also couple emission to DXME-124's `EngageBrightSign` row, which
    does not exist on the dev database.

    **The ticket's stated hazard was already closed by decision 16.**
    `ContentManifestLogService.RecordIfChangedAsync` is keyed
    `(tenant, bucketType, bucketId, filterValue)` and takes a `CachedManifest`, so
    it records the publication version per bucket and a per-device delivery
    version cannot reach it. There is no audit row per device to avoid.

    **Nothing is pushed when the value changes.** No wake, no command, no forced
    reboot. `IPlayerWakeNotifier` fires from content writes and from
    `DeviceCommandService.QueueAsync`, and an operator setting a time zone is
    neither. The value lands within one poll and takes effect only at the next
    boot, which the daily reboot timer supplies within 24 hours, so shaving
    minutes off a 24-hour latency buys nothing.

    **Consequence of DXME-182, stated rather than discovered:** a `TblDevice` row
    deleted and recreated by the Ink sync comes back `Seeded`, so Director
    silently stops re-asserting the zone while the player keeps the correct
    registry value. That is the safe direction.

    Two things this deliberately does **not** decide, both on the player half:
    the name of the registry key the app writes, which no document anywhere
    states, and whether the POSIX literals are correct. Both went to
    [DXME-196](https://radianttechnology.atlassian.net/browse/DXME-196) on the
    [DXME-120](https://radianttechnology.atlassian.net/browse/DXME-120) map.

33. **A pushed time zone is verified on the way back, by comparing the zone's
    rules rather than its name.** Settled by
    [DXME-197](https://radianttechnology.atlassian.net/browse/DXME-197). The app
    reports, on the existing heartbeat inside `PropertiesJson`: the UTC offset at
    two fixed instants, 15 January and 15 July; the **year** those instants were
    taken in; the POSIX string in force; and `engage/tz_applied`. DXSuite compares
    the two offsets against `TimeZoneInfo` for the intended `tblDevice.TimeZone`
    at the same two instants of the reported year.

    **It reports rules because a name is not available to it.** Under a `POSIX:`
    prefixed zone, which is the only form decision 32 sends,
    `Intl.DateTimeFormat().resolvedOptions().timeZone` is **`undefined`** and
    `formatToParts` degrades to a bare `GMT-07:00`. ICU holds the offset rules and
    no zone identity. The clock is correct throughout. \[BENCH, DXME-197\]

    **Two instants, not one.** Phoenix is `(-7, -7)`, Denver is `(-7, -6)`, the
    wrong `MST1` is `(-1, -1)`. The January probe alone cannot separate Phoenix
    from Denver, because both sit at -7 in winter, so a single sample of the
    current offset passes the Arizona error for four months a year. This is the
    same blind spot that rules out comparing a bare UTC offset, and two instants
    escape it. The check is **semantic rather than nominal**: it tests the
    behaviour that drives scheduling instead of a label, and it cannot separate
    two zones with identical rules, which does not matter because zones with
    identical rules schedule identically. **This removes the fourth "expected
    reported name" column** that was briefly proposed for DXME-167's curated list.

    **The year is reported rather than assumed**, because a hardcoded year rots as
    the server keeps computing past the app's release, and deriving it
    independently on both sides races at the December boundary. A missing year
    lands the device in the "nothing to compare against yet" state rather than
    being judged against a guess.

    **Pending is distinguished from mismatch by `engage/tz_applied`, not by a
    timeout.** `tz_applied` not equal to the intended literal means the apply has
    not happened yet and nothing is wrong; equal to it with the rules still
    disagreeing means the bootloader applied it and this is the result. Decision
    31 rejected a `TimeZoneConfirmedUtc` timestamp and `tblDevice` has no change
    history, so DXSuite cannot date the change; the player already knows. This
    requires the app to **read** a key in the bootloader-owned `engage` section,
    so that section is now bootloader-**write**-owned and app-readable.

    **A mismatch is derived, never stored and never a trigger**, the shape
    DXME-134 gave flavour mismatch. Director has no device fault table at all.
    The reported value is stored as an observation in a new `DeviceExtendedData`
    member following `contentSync` end to end, and rendered as a tri-state badge
    with a fourth state. The dashboard card gets `contentSync`'s fleet counter,
    because the Devices grid cannot be filtered to problem rows
    ([DXME-198](https://radianttechnology.atlassian.net/browse/DXME-198)).

    **It never re-pushes.** Re-pushing the same literal cannot change the outcome,
    since the player applied exactly what it was told and `tz_applied` will match,
    so a re-push is a provable no-op that costs a reboot. The only fix is editing
    the curated list, which is a human action.

    **No flavour gate on verification either.** signageOS players discard
    `osTimeZone` and never report a fingerprint, so they sit in the fourth state
    permanently, which is correct and costs nothing. Rejected Sentinel's
    `BrightSignDwsDriver`: the DWS surface does work, but the driver does not poll
    `/time` today, Sentinel is not approved for sale, and it would need to reach
    90 players' DWS across customer networks. Player half is
    [DXME-199](https://radianttechnology.atlassian.net/browse/DXME-199).

34. **Two test artifacts, not one, and the DST subject is instant-to-local.**
    Settled by
    [DXME-200](https://radianttechnology.atlassian.net/browse/DXME-200). A JSON
    vector file, `tests/DXSuite.Director.Tests/TimeZoneVectors.json`, carries the
    three things that are pure functions: the window rule, the conversion helper
    and the verification badge. Everything else is ordinary xUnit facts in
    `tests/DXSuite.Director.Tests`. DXME-164's shared-JSON vehicle was settled
    for the window rule and keeps that scope, because a case about a controller,
    a cache, a result filter or hash membership has no input row and no expected
    output value.

    **The clock-seam question dissolved rather than being answered.** Decision 6
    has no consumer, so gap and ambiguity vectors would test a rule no code
    invokes. The live DST assertion is that instant-to-local is correct *across*
    a transition: two instants an hour apart spanning the US spring-forward give
    local times two hours apart in `America/Chicago` and one hour apart in
    `America/Phoenix`, which is decision 8's Arizona drift expressed as a vector.
    The instant is an argument, so nothing needs faking.

    **Vector years are legislated years only**, present and past. Expected
    offsets are pinned as literals and no vector reaches into a future year,
    because decision 33 reports the year precisely to stop a hardcode rotting.
    The parameterisation is proved by varying the year inside that range.

    **There is no TypeScript suite, in either Engage repo, and there never has
    been.** The testing case has inverted since DXME-163: `displayManager.ts`'s
    window filter is dead once decision 5's nulling ships, while DXME-199's
    fingerprint is live new TypeScript. The JSON stays plain data so a runner
    could consume it, because that costs nothing, not because one is coming.

    **Two constraints this puts on
    [DXME-158](https://radianttechnology.atlassian.net/browse/DXME-158).**
    Decision 22's catch decides the unresolved branch **before** control enters
    EF-bound code, and decision 31's `Operator`-only gate is a pure predicate
    rather than an inline branch in the action. Both are the choke-point argument
    of decisions 21 and 30 arriving from the test side; without them those cases
    are untestable for no reason.

    **[DXME-185](https://radianttechnology.atlassian.net/browse/DXME-185) is a
    named precondition of DXME-158**, not a blocker on this map. A fixture merged
    while CI still skips `dotnet test` and still fires only on `master` is a
    fixture that has never run. Its price is now measured and small: all 140
    existing tests pass in 8 seconds, so it is a YAML edit with no cleanup behind
    it.

    **DXME-158 authors the file; DXME-201 carries the vectors concretely enough
    to transcribe.** This map writes no production code, and none of the subject
    code exists yet.

## Traps, all measured

- **The player's window filter is live today, and its semantic disagrees with
  decision 10.** Measured by DXME-200. Decision 5 as qualified by DXME-165: on
  the playlist path the server does not evaluate the window fields at all today,
  it forwards them, so `displayManager.ts` is what decides content on 90 players
  right now. `isAssetScheduled` is called from three sites (`:275`, `:1372`,
  `:1436`), uses `currentTime > endTime` so **end inclusive**, and has **no
  midnight-wrap branch**. Not fixed, because fixing it is a release on two fleets
  to correct code the same effort is about to make unreachable. **Reopening
  condition:** decision 5's server-side nulling being descoped or made
  conditional, at which point this divergence is a live defect.

- **DXME-161's "Intl returns an IANA name" has a shelf life, and it ends at the
  first push.** Measured by DXME-197. That finding was taken on an **unmodified**
  player, where the OS zone was a BrightSign short identifier, and it is still
  true there: `MST` reports `America/Denver` and `EST` reports
  `America/New_York`. It stops being true on any player DXSuite has pushed a
  `POSIX:` literal to, where `resolvedOptions().timeZone` is `undefined`. This is
  consistent with the plan, because discovery targets players that have **not**
  been pushed to and those still report a name, but a reader who takes DXME-161
  at face value after commissioning will design against a surface that has gone
  dark. The Out of scope entry's "cheapest route back" clause is scoped to
  un-pushed players for this reason. \[BENCH, DXME-197\]
- **"Log loudly" is not a mechanism this product has.** Measured by DXME-170.
  There is no Serilog, NLog, Application Insights, Seq, file sink or log shipping
  anywhere in `src/` or `installer/`; `web.config:32` sets
  `stdoutLogEnabled="false"` under `hostingModel="inprocess"`, so console output
  goes nowhere; `AppSettingsWriter.cs` writes no `Logging` section at all, so a
  customer runs the repo default of `Information`; and **`LogCritical` appears
  zero times in the entire codebase**. The one surviving channel is the
  framework's default Windows `EventLog` provider at a `Warning` floor, which is
  framework behaviour rather than anything this repo configures, and nothing
  alerts on it. **A thrown startup exception is the only mechanism that reliably
  reaches a human**, via ANCM's 500.30 page plus an event log entry. Any ticket
  that specifies "log loudly" as its failure response has specified silence.
- **The health endpoints are not a channel either.** `/health` and
  `/health/ready` exist (`Program.cs:492-534`, `:771-779`) but **nothing on a
  customer site is documented as polling them**, and they are registered inside
  the web-role block so a worker exposes none. The only `/health` polling in the
  codebase points outward at devices.
- **A Director outage does not blank a screen.** `offlineManager.ts` plus
  `directorClient.ts` `fallbackToStoredContent` (`:279`),
  `lastKnownAssetCollection` and boot recovery (`:220-263`) mean players keep
  playing a stored manifest across a reboot. Measured by DXME-170, and it is what
  made decision 25's throw cheap. The flip side, under decision 5: because the
  server now ships resolved content, a frozen fleet holds the **last delivered
  window** and can no longer self-correct at a boundary.
- **DbTool executes every EF migration, under `InvariantGlobalization=true`.**
  Measured by DXME-170. It is run by the MSI as a deferred custom action with
  `Return="check"`, so a throw fails the install and rolls it back on a
  production host, and its log goes to the **SYSTEM** temp folder rather than the
  technician's. Decision 27 removes the property and forbids computing an
  identifier in a migration. The seed helper is **not** the exposure: DbTool
  touches no device-row-creation path.
- **There are two documented upgrade paths and nobody recorded which customers
  use.** Measured by DXME-170. The installer runs DbTool on the Director host,
  but `db/schema-migration-strategy.md:14` says both modes are supported and
  `deploy/director-upgrade-2026.8.1.sql` proves the hand-run idempotent-SQL path
  has been exercised, on whatever machine the DBA is sitting at. That path
  validates **nothing** about the target host. `installer/Host/README.md:100-109`
  treats the first clean-VM install as the acceptance test, so the Burn bundle
  may never have run against a real customer host. Delivery stays with
  [DXME-181](https://radianttechnology.atlassian.net/browse/DXME-181); do not
  re-litigate it here.
- **The installer checks almost nothing, and the wizard has no preflight.**
  Measured by DXME-170: zero hits for `VersionNT`, `bal:Condition`,
  `MsiNTProductType` or `WixBundleVariable`, no prerequisite script of any kind,
  and exactly one `<Launch>` condition (IIS). The wizard is a three-tab
  `TabControl` with no checks page, and **a failed SQL connection does not block
  Finish** (`MainWindow.xaml.cs:274` ignores the `bool` return). A ticket looking
  for an install-time gate to hang something on will not find one.
- **There is no options-validation pattern to follow.** `ValidateOnStart`,
  `IValidateOptions` and `ValidateDataAnnotations` appear **zero** times in
  `src/` and `installer/`, despite `Configure<T>` being used throughout.
  Measured by DXME-170. The four existing refuse-to-start precedents are all
  hand-written throws: `Program.cs:133`, `ConfigTenantStore.cs:30` (forced eager
  at `Program.cs:597`), `SqlTenantStore.cs:47` (from `StartAsync`), and the WiX
  launch condition.
- `TblDeviceMetadata` already has a key called `"timezone"`, **and the admin UI
  actively offers it**: `DeviceMetadataService.cs:45-52` lists it among the
  suggested keys alongside `zipCode`, `propertyName`, `locale`, `region`. An
  operator can author a per-device `timezone` value today, it reaches the
  player, and `displayManager.ts:98-124` appends it to web content URLs as a
  query parameter. Nothing interprets it as a time zone. No ticket on this map
  should read it, write it, or treat it as evidence, and it is **not** the
  column decision 11 is talking about.
- **`zipCode` and `propertyName` are the same decoy as `timezone`**, and
  `DeviceMetadataService.cs:44-50` offers all three to operators in the admin
  UI. They are a shadow, per-device, free-text version of the site data decision
  18 gives `tblSite`. Measured by DXME-167, which removes the two address-ish
  keys from the suggested list but leaves existing rows untouched, because those
  values reach the player as URL query parameters and something may read them.
  No ticket should treat any of the three as evidence of anything.
- **Do not enumerate an object's prototype chain and invoke its members** when
  probing a player. Doing so wedged the bench HD1025 during DXME-161, past
  remote recovery, because a mutator-name denylist missed `reset`, `halt`,
  `shutdown` and `powerOff`. The HD1025 at 192.168.4.169 is still out of service
  until physically power-cycled.
- **The manifest cache TTL was never load-bearing for correctness**, despite a
  code comment at `SignageController.cs:981` saying it is. `PlaylistManifestTtl`
  is 5 minutes and the TTL is `min(untilMidnight, 5 min)`, so the midnight cap
  only bites in the last five minutes of the Director's day. Measured by
  DXME-166. Any ticket reasoning from that comment is reasoning from a false
  premise.
- **A `TblDevice` row can be deleted and recreated with no operator present.**
  `CloudConnectorSyncService.cs:575-579` deletes rows whose Ink folder
  disappears, and the sync recreates them at `:444-464` or `:555-569`. Every
  operator-set field goes with it, so a hand-set `America/Phoenix` comes back
  stamped with the host zone, silently. Measured by DXME-168 and filed off-map
  as [DXME-182](https://radianttechnology.atlassian.net/browse/DXME-182),
  because it destroys `Name`, `AutoHealEnabled`, `LocationId` and `DisplayId`
  too. Any ticket that assumes a per-device setting persists is assuming too
  much.
- **UTC already won, 120 to 11, and there is a third clock family nobody
  mentioned.** Measured on `dev` by DXME-169. `CreateDate = DateTime.UtcNow`
  appears at **120** sites against **11** for `DateTime.Now`, and every other
  stamp column is UTC-only: `LastEditDate` (67), `ModifiedDate` (12),
  `CreatedDate` (12), plus about thirty more. So the mixing is eleven outliers,
  not two competing conventions, and `CreateLayoutModal.razor` holds both.

  The third family is SQL `(getdate())`, the **database server's** local clock,
  on **81** column defaults: 60 in `DirectorDbContext`, 9 in `AviationDbContext`,
  7 in `IdentityDbContext`, 5 in `CoreDataDbContext`. Nine columns already use
  `(getutcdate())`, mixed into the same files. Latent rather than live, because
  the defaults fire only when a row is inserted without EF supplying the column
  and the two servers currently share a time zone. Filed off-map as
  [DXME-183](https://radianttechnology.atlassian.net/browse/DXME-183).
- **`EndDate` is never written from a clock.** Measured by DXME-169. Every
  writer parses a date an operator typed into a form field, so it is a
  wall-clock bound and not a stamp. Any ticket reading
  `ScheduleExpiryNotificationService.cs:67` as two clocks disagreeing is reading
  it wrongly: it is an instant compared against a bound, which is DXME-158's
  defect class with a UTC offset stacked on top.
- **Nothing reads `CreateDate` on `TblAsset`, `TblConstraint` or
  `TblScheduleEvent`.** Measured by DXME-169. The only `.CreateDate` reads in
  Director or SLC are `DevicePairing.razor:73,188`, against other tables, with no
  clock comparison. So the mixed-clock writes in those three tables produce no
  observable wrong answer today, and DXME-169 fixed them as a convention repair
  rather than as a defect fix.
- **The mixed `CreateDate` rows cannot be corrected, ever.** Measured by
  DXME-169: the rows carry no marker recording which clock wrote them and the two
  clocks coexisted across an unknown number of releases, so any `DATEADD` would
  corrupt as many rows as it repaired. A future reader finding a cluster offset
  by several hours should not go looking for the missing migration.
- **`CounterCalendar.razor` is not where the survey says and reads no clock at
  all.** It is at `src/DXSuite.Director/Components/Schedulers/`, not
  `Components/LocationTypes/Custom/`, and its `IsDayActive` evaluates day-of-week
  against an operator-chosen `SelectedDate`. Measured by DXME-169. It is not a
  badge and not a defect.
- **T-SQL cannot produce an IANA identifier.** Measured on the dev Director
  database (SQL Server 15.0.4261.1) by DXME-168, not inferred.
  `AT TIME ZONE 'America/Chicago'` **errors**, while
  `AT TIME ZONE 'Central Standard Time'` succeeds. `sys.time_zone_info` holds
  Windows names only, so `America/Chicago` and `America/Phoenix` match zero
  rows. And `CURRENT_TIMEZONE()` is worse than a Windows identifier: it returns
  a **display name**, `(UTC-05:00) Eastern Time (US & Canada)`, which is not
  usable with `AT TIME ZONE` without parsing it first.

  Decision 4 makes IANA canonical, so no migration can derive a time zone from
  the server. "The server" is ambiguous anyway, since an on-prem upgrade script
  may run on a DBA workstation rather than the Director host. Note also that
  Arizona is its own Windows identifier, `US Mountain Standard Time`, distinct
  from `Mountain Standard Time`.
- **The repo has no Windows-to-IANA conversion anywhere**, and no
  `TimeZoneConverter` or `NodaTime` package. Measured by DXME-168. The two
  `TimeZoneInfo` calls in `src/` are both at `GuardrailService.cs:52-53`, and
  they are a UTC-to-local **instant** conversion on a Windows identifier from a
  deserialized policy object, **not** an identifier-format conversion and not a
  database column. The map's survey line implying otherwise is wrong.
- **Nothing wakes a player at a window boundary.** `IPlayerWakeNotifier` fires
  from `ManifestCacheService.InvalidateLocation`, which is a content write. A
  boundary is not a write, so boundary latency is bounded by the poll interval,
  which is 30s base but floors at 180s whenever the wake channel is connected
  (`directorClient.ts:394`).

### Dormant, and live again when device-driven time zones return

Nothing on this map now reads or writes a device's reported time zone, so these
are recorded rather than active. They were measured by DXME-161 and would have
been settled by DXME-172. **Expect to need them**, since device-driven time
zones are the intended destination.

- `MST` is probably not Arizona. A BrightSign short identifier is a DST-aware
  region rather than a fixed offset: a player set to `EST` showed an applied
  offset of -4 in September. By **inference, never measured**, `MST` is
  DST-aware too and therefore tracks Denver rather than Phoenix. BrightSign
  documents `MST1` for Arizona. Treat `MST` for a Phoenix site as a live
  foot-gun.
- `timezone_abbr` is not DST-aware. It read `EST` while the offset in force was
  -4 and the renderer reported `EDT`, because it echoes the configured
  identifier. Read `timezone_name` instead.
- **A BrightSign player holds its time zone in four formats at once**, on one
  unmodified device: registry `networking/tz` and
  `@brightsign/systemtime.getTimeZone()` both give `EST`, the DWS gives the IANA
  name, and the renderer's `process.env.TZ` carries a full POSIX string. The
  registry key is flat `networking/tz`, **not** `networking/private/tz` as two
  older research documents record.
- **What an unset BrightSign player reports was never measured.** DXME-172
  closed before doing it. So a reported time zone cannot today be distinguished
  from a factory default, which is why
  [DXME-173](https://radianttechnology.atlassian.net/browse/DXME-173) was scoped
  to observation only and forbade adoption.
- **The fleet is provisioned from one setup carrying one time zone, and that is
  what killed DXME-173.** The local BrightAuthor:connected store holds
  `"network":"WINGSCAROUSEL"` and a single setup-level `"timeZone":"PST"`, a
  BrightSign short identifier rather than IANA and one value rather than one per
  player. **[LOCAL CLIENT STORE, not BSN server truth, possibly stale.]** So the
  reported values would all be identical, and discovery cannot become useful here
  until provisioning carries a per-player zone.
- **The players run Carousel today, which manages time zones in-app.** That is
  why the BrightSign OS-level zone was never maintained and nobody noticed. After
  takeover that in-app management goes away, so the starting state on every player
  is expected to be **wrong** rather than merely unknown. Nothing on this map
  depends on it, because decision 5 nulls the window fields, but the first thing
  on a player that reads its own clock will.
- **BrightAuthor:connected is not a backfill source for decision 7.** A short
  identifier at setup granularity is the `MST`-is-not-Arizona foot-gun and is
  worse than nothing. DXME-168's operator-supplied `UPDATE` stands.

## Survey of record (September 2026)

A full code survey found: roughly 30 schedule-deciding sites out of 107
local-clock reads; five mutually inconsistent copies of the window logic
(`ScheduleTime.cs:14`, `ScheduleEvaluator.cs:74`,
`ClusterResolutionService.cs:267`, `StatusSelectorService.cs:415`,
`AirlineStateResolver.cs:49`); zero reads of `tblDisplay.TimeZone`; no
`TimeProvider` or `IClock` seam anywhere in `src/`; no geography of any kind on
`tblLocation`; and exactly one timezone conversion in the whole repo
(`GuardrailService.cs:52`, in Sentinel, using a Windows id). Findings are
restated in the tickets that depend on them.

**DXME-164 corrected the window-logic count**: it is five distinct semantics
across nine call sites, the inline inclusive form is triplicated rather than
duplicated, and two of the four listed authoring sites were misread.

**DXME-163 corrected the reachability**: `StatusSelectorService` and
`ClusterResolutionService` are **not reachable from** `PlayerContent` at all,
since `SignageController` injects neither. `ClusterLifecycleService` touches the
player path only asynchronously, by creating and deleting transient
`TblLocation` rows. It also found that evaluation is currently split mid-window
across two clocks: the server decides day-of-week on `DateTime.Now`
(`SignageController.cs:951`, `:1011`) and the player decides date and
time-of-day on its own `new Date()` (`displayManager.ts:1301`).

**DXME-165 cut the 30 sites to 5.** The rest are out of scope under the four
rules, each with a written reopening condition. It also found that the playlist
path performs **no** server-side window evaluation today, and that
`ScheduleEvaluator` is unreachable from the player path.

**DXME-166 corrected the manifest-path mechanism.** The day-boundary defect is
caused solely by `DateTime.Now` in `IsDayActiveForPlaylist`, not by the TTL. It
also found that overrides never touch the manifest cache at all, so that
in-scope site needs the time zone swap and nothing else.

**DXME-167 corrected the authoring inventory.** Of the 14 schedule-authoring
surfaces the map listed, only four are in scope. It also found that **the
“active now” badges it was chartered to settle do not exist**: the `Playlist`
chips are static renderings of stored values and nothing evaluates whether a
window is open. What exists is a date-only expiry on the server clock. And it
established that the display manifest cache has the same defect DXME-166 found
in the playlist cache, on a cache nobody had looked at.

## Decisions so far

The index: one line per closed ticket, enough to judge relevance, then open the
ticket for the detail.

- [Does .NET resolve IANA zone ids on the Director deployment, and how is a POSIX TZ string derived?](https://radianttechnology.atlassian.net/browse/DXME-162):
  Yes on the Host as shipped, and `America/Phoenix` resolves with
  `SupportsDST=False` and zero adjustment rules, so decision 4 holds and Arizona
  is sound at the framework level. It fails identically under invariant
  globalization and NLS, only at first use, and the supported-OS floor has zero
  margin, all of which became
  [DXME-170](https://radianttechnology.atlassian.net/browse/DXME-170). POSIX
  turns out not to be needed for the first deployment.
- [Can a signageOS device report its own timezone, and can DXSuite set it?](https://radianttechnology.atlassian.net/browse/DXME-160):
  Read and write both exist server-side with **no applet change**, so reading a
  time zone may cost zero fleet releases. But the reported value is a bare
  `string` with no guaranteed format, an absent value has two indistinguishable
  causes (unset, or capability missing), signageOS models a Location that
  carries no time zone so granularity is per device only, and DXSuite has **no
  signageOS REST integration at all** today. Nothing was observed on hardware.
  Deferred by the scope narrowings, and directly relevant again when
  device-driven time zones return.
- [Can a BrightSign player report its own timezone, and in what format?](https://radianttechnology.atlassian.net/browse/DXME-161):
  **Yes, and as IANA.** The DWS `GET /api/v1/time` returns
  `timezone_name: "America/New_York"`, identically on Series 4 and Series 5, and
  `Intl` in the renderer returns the same. Reporting it on the heartbeat would
  be an **app-bundle change only**, since `PropertiesJson` already exists and is
  populated. Arizona already resolves correctly in the renderer with no
  configuration on both series, out of 428 zones. Deferred by the 2026-09-12
  narrowing; this is the cheapest route back to device-driven time zones and is
  what [DXME-173](https://radianttechnology.atlassian.net/browse/DXME-173) would
  spend. **Scoped by DXME-197 to players DXSuite has not pushed to**: the `Intl`
  half reads `undefined` once a `POSIX:` literal has been applied, so the route
  back exists only on a player that has not been commissioned yet.
- [Collapse the five window evaluators into one, and pick the semantic that survives](https://radianttechnology.atlassian.net/browse/DXME-164):
  One rule survives, as decision 10 above, expressed as a **pure**
  `DailyWindow.Contains(TimeOnly localTime)` that reads no clock and has no DST
  behaviour, which is why decision 6 moved to DXME-166 and why the
  `TimeProvider` question dissolved. All five implementations are in one
  assembly, so the collapse needs no new project reference. The ticket's own
  table was **wrong in six places**. The two largest deltas are
  `ScheduleEvaluator`'s one-sided bounds (four access-control sites, currently
  permanently open) and `ClusterResolutionService` gaining midnight wrap.
  **Authoring badges stop evaluating time.** Three incidental defects went to
  [DXME-169](https://radianttechnology.atlassian.net/browse/DXME-169); the
  device-versus-display premise went to
  [DXME-163](https://radianttechnology.atlassian.net/browse/DXME-163).
  **Amended by DXME-165:** four of the five collapse now, `ScheduleEvaluator`
  waits for [DXME-176](https://radianttechnology.atlassian.net/browse/DXME-176).
- [Where the authoritative time zone lives, and who evaluates against it](https://radianttechnology.atlassian.net/browse/DXME-163):
  **A plain non-nullable `tblDevice.TimeZone` column, IANA, seeded at row
  creation from the Director host's zone; the server resolves every window
  against it and ships resolved content with the window fields nulled; the
  manifest cache key gains the time zone.** Overturns decision 2 (it is not the
  display). Driven by the deployment shape: one Playlist location spanning three
  zones, and `Playlist.razor` never touches `TblDisplay`. Neither Location nor
  Display is a place; `tblDevice` is the only row that is. Needs **no player
  release on either fleet**, because nulling the window fields disables the
  player's filter. Rejected a Service Center entity and `tblDeviceMetadata`
  (EAV, and `"timezone"` is already an offered key doing something unrelated).
  Filed [DXME-174](https://radianttechnology.atlassian.net/browse/DXME-174) and
  [DXME-175](https://radianttechnology.atlassian.net/browse/DXME-175) off-map.
  **Its cache-key clause was later overturned by DXME-166.**
- [Which zone each of the ~30 evaluation sites uses, including the ones with no single display](https://radianttechnology.atlassian.net/browse/DXME-165):
  **Four rules, not a thirty-row table, and five sites in scope, not thirty.**
  The per-site table was an artifact of the overturned decision 2. Biggest
  finding: **the playlist path performs no server-side window evaluation
  today**, `SignageController.cs:950-951` filters day-of-week only and forwards
  the four window fields to the device unevaluated, so decision 5 is a _new_
  evaluation rather than a corrected one, and it repairs DXME-174 for free.
  Ruled video walls out (`PixeraController` is `[AllowAnonymous]` with **zero**
  device references; shells identify by location id alone and their URLs cannot
  be updated remotely), plus the SLC airline monitor, the ticket-counter write
  path, access and floor-plan schedules, and all five Blazor surfaces (confirmed
  operator previews, answering ticket item 5). Established that
  `ScheduleEvaluator` is **unreachable from the player path**, which is what let
  DXME-164's collapse be resequenced. Routed the discarded-`atTime` bug to
  DXME-169 and the cache TTL to DXME-166. **Sharpened by DXME-169:** ruling
  video walls out means they get no **device time zone** and stay on the server
  clock. It does not mean video wall scheduling code may not be touched, which
  is how the line reads and is broader than what was decided. Filed
  [DXME-176](https://radianttechnology.atlassian.net/browse/DXME-176) off-map and
  [DXME-177](https://radianttechnology.atlassian.net/browse/DXME-177) on it.
- [Manifest caching and the day boundary: stop the TTL being load-bearing for correctness](https://radianttechnology.atlassian.net/browse/DXME-166):
  **Move the cache boundary earlier: cache the unfiltered build, apply the
  day-of-week and window filter per request in the device's time zone.**
  Decisions 15 and 16 above. The ticket's title was right and its premise was
  wrong: `PlaylistManifestTtl` is 5 minutes, so the midnight cap only bites in
  the day's last five minutes and the TTL was load-bearing for nothing; the hour
  of wrong-day content comes entirely from `DateTime.Now` in
  `IsDayActiveForPlaylist`. **Decision 5 is what would have made the TTL
  load-bearing**, since a manifest of resolved content makes every boundary wait
  TTL plus one poll (measured at roughly 8 minutes, because a connected wake
  channel floors the poll at 180s and nothing wakes a player at a boundary).
  **Overturned decision 13** and **closed decision 6** with no consumer.
  Separated publication version from delivery version, which prevents a spurious
  audit row per time zone at every boundary. Rejected moving day-of-week to the
  player and rejected a 30-minute TTL, the latter because it is gated on
  [DXME-178](https://radianttechnology.atlassian.net/browse/DXME-178). Also:
  `MatchesAnyVariant` is dead code, `PlayerPollFlagCache` is a non-issue,
  overrides need no cache work, and DXME-175 confirmed by direct reading.

- [What an author is shown about time zones, and where an operator sets one](https://radianttechnology.atlassian.net/browse/DXME-167):
  **One sentence on four authoring surfaces, the time zone visible in exactly one
  place, and a new `tblSite` entity.** The copy is `Times are each player's local
  time.` on `MediaAssetUploader`, `OverrideEditor`, `ScheduleEditor` and the
  `Playlist` chips; no zone name or count appears on any authoring surface. The
  multi-zone rendering the ticket proposed is **dropped entirely**, and with it
  the prototype the ticket anticipated. A Time zone column joins `DevicesTab`,
  on by default, edited through the existing click-to-edit cell pattern and
  backed by a **curated list rather than free text**, because an operator
  reasoning “Phoenix is Mountain” picks `America/Denver` and is wrong for eight
  months a year while looking right all winter. Commissioning relies on sort and
  filter only; no confirmed flag, which would be decision 12's deleted unset
  branch returning under another name. **That clause was amended by DXME-192**,
  which needed the state as a gate on an outbound write rather than as a
  commissioning tracker: sort and filter stay the mechanism, and decision 31
  makes the state visible in the cell that already shows the value. Added
  decisions 17, 18 and 19.
  **Three of the ticket's five sub-questions rested on false premises**: the
  “active now” badges do not exist, the multi-zone rendering had no subject once
  decision 2 was overturned, and a Display cannot stand in as a unit of place.
  That last one is the big one and became
  [DXME-179](https://radianttechnology.atlassian.net/browse/DXME-179). Filed
  [DXME-180](https://radianttechnology.atlassian.net/browse/DXME-180) off-map for
  the server-clock expiry badge, and routed the `TblFloorPlanIconSchedule`
  missing-editor question to DXME-176.

- [Migration and backfill: adding tblDevice.TimeZone without changing behaviour](https://radianttechnology.atlassian.net/browse/DXME-168):
  **Two migrations, a non-nullable column with no persistent default, a seed at
  one choke point, an operator-supplied backfill, and a sentinel rather than a
  plausible zone when the operator step is skipped.** Decisions 20 to 23 above.
  Its title and three of its seven sub-questions had no subject once DXME-163
  moved the time zone off the display; item 7 was already answered by DXME-167.
  **T-SQL cannot compute an IANA identifier**, so the backfill cannot be
  automatic, which is what amended decision 7. **Decision 12 named one
  row-creation path and there are four**, two of them in Sentinel, which is what
  produced the choke point. Ruled delivery out of scope and filed
  [DXME-181](https://radianttechnology.atlassian.net/browse/DXME-181): the
  `deploy/` upgrade script covers one migration rather than a release, is
  already stale, has no documented provenance anywhere in the repo, and the
  per-tenant fan-out migrator that would land this column across the fleet does
  not appear to exist. Filed
  [DXME-182](https://radianttechnology.atlassian.net/browse/DXME-182) for the
  delete-and-recreate that destroys every per-device setting. Corrected the
  survey's `GuardrailService` line, and established that the Director database is
  per-tenant by connection string with no query filter anywhere.

- [The incidental clock defects: mixed Now and UtcNow writes, and the rest of the sweep](https://radianttechnology.atlassian.net/browse/DXME-169):
  **Two persisted kinds of `DateTime`, an instant and a wall-clock bound, and
  they are never compared.** Decision 24 above, written into the glossary and
  into ADR 0002. **UTC had already won 120 to 11**, so this finished a convention
  rather than choosing one, and the ticket's framing of two competing clock
  families was wrong: there are eleven outliers, and a **third** family nobody
  had mentioned, 81 SQL `(getdate())` column defaults, filed off-map as
  [DXME-183](https://radianttechnology.atlassian.net/browse/DXME-183).
  `EndDate` turns out **never to be written from a clock**, so finding 2 was an
  instant compared against a bound rather than an audit-stamp problem, and it is
  fixed here against the host clock with the time zone question explicitly
  declined, because a display spans zones (decision 19) and a notification has
  no device. Four findings in, one out. Nothing reads `CreateDate` on the three
  affected tables, so the eleven edits are a convention repair, and the existing
  rows **cannot** be corrected because they carry no marker saying which clock
  wrote them. Declined an analyzer (no infrastructure, and 40 legitimate
  `DateTime.Now` reads would need suppressing) and `DateTimeOffset` everywhere
  (it makes a wall-clock bound inexpressible). Confirmed DXME-164 took the three
  badges, and found `CounterCalendar` at a different path reading **no clock at
  all**. Filed [DXME-184](https://radianttechnology.atlassian.net/browse/DXME-184)
  off-map: `CBPMessages.razor:259` persists its own active-message filter and so
  **deletes content when the authoring surface loads**.

- [Detecting a deployment where IANA zone resolution is unavailable](https://radianttechnology.atlassian.net/browse/DXME-170):
  **The Host throws at startup, on two assertions, unconditionally; four causes
  each get their own instrument; the DbTool property goes and a constraint
  replaces it; and nothing is checked in the installer.** Decisions 25 to 28
  above. The load-bearing finding is that **"log loudly" is not a mechanism this
  product has**: no third-party logging provider anywhere, console discarded by
  `stdoutLogEnabled="false"` under in-process hosting, no `Logging` section
  written by the installer, and `LogCritical` used zero times, leaving a thrown
  startup exception as the only thing that reliably reaches a human. That
  **qualified decision 22 twice**, since its loud half does not work and its
  per-identifier path is the wrong handler for fleet-wide loss. The throw is
  cheap because a Director outage blanks no screen, so the fleet freezes on
  last-known-correct content while an ICU-less Host serves incorrect content
  immediately, invisibly at go-live: not symmetric, and pointing the same way on
  both axes. The DbTool archaeology came back **empty**, one commit with no
  mention of globalization while its sibling got a comment, and the real coupling
  turned out to be **migrations rather than the seed helper** this map had
  assumed. Rejected an OS version check as a proxy that passes the worst cause
  and fails working boxes, app-local ICU (measured at 177.0 MiB, pinning a 2023
  tzdata vintage, and useless against the environment-variable cause), shipping
  the probe's internal-API reflection, and an ADR. Filed
  [DXME-185](https://radianttechnology.atlassian.net/browse/DXME-185) off-map:
  CI runs no tests despite 12 existing test files and never fires on `dev`.

- [What server-side window resolution costs at fleet scale, and the hardware envelope it implies](https://radianttechnology.atlassian.net/browse/DXME-177):
  **The time zone work is 3.4 microseconds, 1% of a 358-microsecond pass that is
  0.32% of one core at 90 players, and there is no hardware recommendation
  anywhere in the repo to revise.** Decision 29 above. **Both of the ticket's own
  premises had died in DXME-166** and it was written before that resolved: the
  cache is not keyed by time zone (decision 13 overturned, so the first
  deployment holds one publication bucket rather than three, a 3x saving on
  rebuilds and database round-trips) and rebuild frequency is no longer the cost
  variable, because decision 16 moved the cache boundary and the cost is now a
  per-request pass the ticket did not anticipate. Inside that pass the
  **delivery-version hash is 80%** and the filter is 1%, and
  `WeightedLoopExpander.Expand` is the only superlinear term. Measured the
  rebuild at **120 to 420 ms**, dominated by **2N sequential database
  round-trips**, which answers DXME-178's request to size it both ways and shows
  its payoff is database load rather than CPU. Confirmed the applet's 180s wake
  floor (`Engage_SignageOS/src/config.ts:37`) and established the worst case is a
  fleet-wide wake outage at the 10s poll floor. Named five instruments, led by
  delivery-pass duration tagged by time zone and the NotModified ratio. Filed
  [DXME-186](https://radianttechnology.atlassian.net/browse/DXME-186),
  [DXME-187](https://radianttechnology.atlassian.net/browse/DXME-187) and
  [DXME-188](https://radianttechnology.atlassian.net/browse/DXME-188) off-map.


- [The display manifest cache, and resolving a window per device on the display path](https://radianttechnology.atlassian.net/browse/DXME-179):
  **The display and layout manifests take decision 16's boundary move, so all
  three manifest paths now cache the publication and resolve the delivery per
  request.** Decision 30 above. **Three of the ticket's premises were wrong**: it
  quoted `GetDisplays` while meaning the display branch of `PlayerContent`; "there
  is no clean pre-filter artifact to cache" was false, because `GetActiveEvent` is
  already a pure database-free function and asset hydration is a separate stage;
  and **decision 5 already held here by accident**, since the display projection
  never sets the four window fields. That last one reframed the ticket, because
  the live defect is the server clock on every display rather than the latent
  multi-device ambiguity, which in the dev database is one display carrying a
  duplicate row. **Generalised decision 22** to "fail toward the path's own
  default" and confirmed **decision 13's overturn holds on all three paths**.
  Named DXME-167's `DevicesTab` column as the instrument covering a sentinel, so
  nothing new was built for it. Ruled `PanelService` out and
  `SignageController.cs:1300` in. Filed
  [DXME-189](https://radianttechnology.atlassian.net/browse/DXME-189),
  [DXME-190](https://radianttechnology.atlassian.net/browse/DXME-190) and
  [DXME-191](https://radianttechnology.atlassian.net/browse/DXME-191) off-map.

- [How an operator-confirmed time zone is distinguished from the seeded default](https://radianttechnology.atlassian.net/browse/DXME-192):
  **A non-nullable `tblDevice.TimeZoneSource` recording provenance rather than
  confirmation, and only `Operator` is ever emitted to a device.** Decision 31
  above. Filed from DXME-157 on the DXME-120 map, and it **reopened a frontier
  that was empty**. The collision it had to clear first was DXME-167's explicit
  rejection of a confirmed flag: that rejection stands for what it rejected, a
  commissioning tracker, and decision 9 is what separates it from a gate on an
  outbound write, since no evaluator reads the new column and decision 5 is
  untouched. **Provenance beat a `ConfirmedUtc` timestamp on the destination's
  own constraint**, because a timestamp cannot say what confirmed it and
  discovery would have to lie or add a second column. Three of the ticket's four
  listed options died on facts rather than judgement: `tblDevice` has no change
  history at all, `tblPlatformAuditLog` is a different database recording
  elevation events, and `TblEventLog` has no field-edit writer and is purged at
  365 days. The free option, deriving the state from the Director host's own
  zone, was examined and rejected because it reads a device genuinely in that
  zone as unconfirmed forever. Established that **the DXME-168 backfill counts as
  confirming**, without which every upgraded deployment would stay permanently
  unconfirmed, and that the rule has to be stated **per writer** rather than as
  one exception, because discovery breaks a single-exception rule. Took the
  POSIX column onto the curated list and gave the off-list case decision 22's
  shape. Amended DXME-167's clause and decisions 20 and 21, rewrote the Out of
  scope entry on pushing a time zone to a player, and filed
  [DXME-195](https://radianttechnology.atlassian.net/browse/DXME-195) on this map
  for the wire format.

- [Which callers receive a confirmed time zone, and on which payload](https://radianttechnology.atlassian.net/browse/DXME-195):
  **One endpoint, one new field called `osTimeZone` on `PlayerContentResponse`,
  outside the delivery-version hash, stamped by a result filter, with no flavour
  gate.** Decision 32 above. **Three of the ticket's four questions rested on
  wrong facts.** There is no heartbeat endpoint: the heartbeat _is_
  `POST /api/Signage/PlayerContent`, so "manifest body or heartbeat response" was
  a choice between two names for one object. `DeviceTypeId` is a dead column read
  nowhere in C#, so the device-type gate had no subject, and the live
  discriminator is `PlatformId`. And the audit-row-per-device cost it asked to
  weigh had **already been closed by decision 16**, because the content log is
  keyed per bucket and takes the publication. The one option worth real argument
  was the durable command queue, rejected because a time zone is state rather than
  an event. Established that both Engage clients ignore unknown response fields by
  construction and that `assetSync.ts` is byte-identical between the repos, which
  is what removed the gate. Found the nine response construction sites and the
  `PlayerPollIntervalFilter` precedent that covers them. Deleted the stale
  `Time zone` row from the hub spec's provisioning package, a fourth carrier
  DXME-157 had killed three hours after it was written. Filed
  [DXME-196](https://radianttechnology.atlassian.net/browse/DXME-196) on the
  DXME-120 map for the two player-half gaps, and carried a correction to
  DXME-194: every Engage player on **both** flavours sends its device token in
  `X-API-KEY` on every poll, so that header is the fleet's normal authentication
  carrier and not the BrightSign-recovery artifact DXME-193 took it for.

- [Whether a pushed time zone is verified on the way back, and on which surface](https://radianttechnology.atlassian.net/browse/DXME-197):
  **Yes, by the app, reporting the zone's rules rather than its name.** Decision
  33 above. **The preferred carrier died on a measurement**: under a `POSIX:`
  prefixed zone, which is the only form decision 32 sends,
  `Intl.DateTimeFormat().resolvedOptions().timeZone` is `undefined` and
  `formatToParts` degrades to `GMT-07:00`, so the one form DXSuite must send is
  the one form under which the app cannot report a name. The ticket anticipated
  `Intl` returning the POSIX string or falling back to UTC; it returns nothing.
  What survives is the offset at a chosen instant, and **two** instants separate
  Phoenix `(-7, -7)` from Denver `(-7, -6)` and catch the wrong `MST1`
  `(-1, -1)` where one does not, because both Phoenix and Denver sit at -7 in
  winter. Rejected Sentinel on three counts the ticket had understated: its
  driver does not poll `/time` today, Sentinel is not approved for sale, and it
  would need to reach 90 players' DWS across customer networks. **This reopened
  the map's own Out of scope entry**, whose stated condition had fired, and the
  line held at verification rather than discovery. Established that Director has
  **no device fault table at all** and that `tblEventLog` has no UI reader, which
  is what made the derived-fault shape the only honest one, and that the Devices
  grid cannot filter to problem rows, which **contradicts decision 31's
  filterable-column clause**. Removed the fourth curated column DXME-197 itself
  had proposed an hour earlier. Recorded the DXME-161 shelf-life trap above.
  Filed [DXME-198](https://radianttechnology.atlassian.net/browse/DXME-198) and
  [DXME-199](https://radianttechnology.atlassian.net/browse/DXME-199) off-map.

- [The time zone test strategy: which cases go in the fixture, and how a DST transition is exercised](https://radianttechnology.atlassian.net/browse/DXME-200):
  **Two artifacts, and the seam question dissolved rather than being answered.**
  Decision 34 above. All fifteen routed cases survive in some form and one is
  added: two are transformed (case 2 inverted off the dead TypeScript regex onto
  the server's `TimeSpan?` serialisation, which was always the real hazard; case
  3 re-subjected from the cache key, which the compiler already enforces, to
  publication-version invariance across `localNow`), two are reduced to a filter
  assertion plus a recorded hazard (cases 10 and 11, because a filter test proves
  only that the filter stamps what it is handed and cannot prove the action
  reached it), one narrowed (case 7, to the display-sourced versus
  playlist-sourced boundary that holds decision 30's line) and one split (case
  4). **Three measured facts moved the answer.** The existing suite is green:
  **140 passed, 0 failed, 8 seconds** on `origin/dev`, which spends DXME-185's
  "some may not currently pass" caveat. **No TypeScript suite exists in either
  Engage repo and never has**, so DXME-164's shared-JSON vehicle assumed a
  consumer that is not there. And the suite has **zero database machinery**, no
  `DbContext`, no EF in-memory provider, no `WebApplicationFactory` and no
  mocking library. Found the seam that made the expensive cases free:
  `DisplayItem.GetActiveEvent(ICollection<TblScheduleEvent>, DateTime?)` at
  `DisplayItem.cs:50` reads no instance state and queries nothing, while anything
  routed through `EffectiveContentResolver` requires EF. Established that **none
  of the subject code exists yet**, zero
  `ConvertTimeFromUtc`/`FindSystemTimeZoneById`/`TryConvertIanaIdToWindowsId`
  under `src/DXSuite.Director` and no `tblDevice.TimeZone` column, which
  independently confirms decision 20's drop of the unreferenced
  `TblDisplay.TimeZone`. Recorded two further duplicate copies of the selection
  rule at `StandardDisplay.razor:205-250` and `:281-322`, not in DXME-164's count
  of five, confirmed to be operator previews and **declined** for collapse under
  the minimum footprint preference, with the consequence stated so it is not
  later filed as a bug: Director's own preview of a Mountain display will
  disagree with what that display is showing once the fleet spans three time
  zones. Recorded the live TypeScript window-filter divergence as a trap above.

- [Assemble the time zone scheduling handoff spec from the closed tickets](https://radianttechnology.atlassian.net/browse/DXME-201):
  **[`docs/spec/time-zone-scheduling.md`](../spec/time-zone-scheduling.md), and it
  is now the document a builder implements from.** Thirteen sections carrying all
  34 decisions with only the surviving version of each, the corrections noted so
  nobody reopens one from an older ticket, and every section citing the ticket
  that decided it. Section 12 is the **code-changes-by-file table** this map never
  had, covering 40-odd files across entities, migrations, services, controllers,
  models, UI, the installer and CI. Section 11 carries DXME-200's vectors
  **concretely enough to transcribe**, with literal expected offsets and the
  2026-03-08 spring-forward pair that expresses Arizona's drift. Section 13
  separates the one real precondition,
  [DXME-185](https://radianttechnology.atlassian.net/browse/DXME-185), from the
  items that block nothing. Two things the assembly surfaced and resolved in the
  document rather than reopening: decision 31's filterable-column clause is not
  satisfiable until
  [DXME-198](https://radianttechnology.atlassian.net/browse/DXME-198) lands, so
  sort plus the dashboard counter is the mechanism until then; and decision 32's
  POSIX literals are an open item on
  [DXME-196](https://radianttechnology.atlassian.net/browse/DXME-196), which the
  emission mechanism is indifferent to because it ships whatever string the
  curated list holds. Nothing was overturned and no new fog was surfaced.

## Not yet specified

**Empty, for the second time and for a better reason.** The one patch that ever
lived here, the test strategy for time zone behaviour, graduated into
[DXME-200](https://radianttechnology.atlassian.net/browse/DXME-200) on 2026-09-12
and closed the same day as decision 34. Nothing replaced it. DXME-200 surfaced no
new fog: the two things it found that were not on the map both landed on
[DXME-158](https://radianttechnology.atlassian.net/browse/DXME-158) as
constraints rather than as open questions, and the third, the duplicated
selection rule in the operator preview, was already inside decision 14(d).

**The write-up has landed and this map is done.**
[DXME-201](https://radianttechnology.atlassian.net/browse/DXME-201) closed on
2026-09-12, and
[`docs/spec/time-zone-scheduling.md`](../spec/time-zone-scheduling.md) is now the
document a builder implements from, including the code-changes-by-file table this
map never had. There are no open children and no fog. **Read the spec, not this
file, unless you need a decision's reasoning**, in which case this index points
at the ticket that holds it.

## Out of scope

- **Auto-discovery of a device's time zone, on every flavour including
  BrightSign.** Narrowed in two steps on 2026-09-12: the signageOS flavours
  first, under a time constraint
  ([DXME-171](https://radianttechnology.atlassian.net/browse/DXME-171) closed),
  then BrightSign, to cut complexity for the initial rollout
  ([DXME-172](https://radianttechnology.atlassian.net/browse/DXME-172) closed).
  **Deferred and expected to return**: device-driven time zones are the intended
  destination and Director-set is the interim path, so this is sequencing rather
  than a scope boundary. DXME-160's and DXME-161's work stands and does not need
  redoing. DXME-163 made the return cheap: discovery writes into
  `tblDevice.TimeZone`, which is the row a device reports about, with no
  re-modelling.

  **The one carve-out was examined and closed, so the boundary now holds
  whole.** [DXME-173](https://radianttechnology.atlassian.net/browse/DXME-173)
  asked whether to ship the **reporting half only**, where a BrightSign player
  reports its time zone and DXSuite records it as an observed value that nothing
  adopts and no evaluator reads. **Answered no**, and not on cost: the fleet has
  no per-player time zone to report. It is provisioned from a single
  BrightAuthor:connected setup carrying one `timeZone`, so a reported value would
  be a constant echo of the provisioning template rather than evidence of where a
  player is. The incumbent product manages time zones in-app, which is why the
  OS-level zone was never maintained and is expected to be uniformly wrong after
  takeover. Per-time-zone setups would have to be authored and do not exist.

  **The reopening condition fired on 2026-09-12, by DXSuite's own hand, and the
  reopening is narrow.** Decision 32 pushes an individually-correct zone to each
  player, which is exactly the stated condition, and
  [DXME-197](https://radianttechnology.atlassian.net/browse/DXME-197) is the
  reopening. It does **not** reopen discovery. The line is that **discovery asks
  a device what time zone it is in; verification asks a device whether it
  obeyed**: same wire, different authority. Nothing adopts the reported value, it
  is never written to `tblDevice.TimeZone`, decision 31's `Discovered` stays
  unshipped, and decision 5 is untouched.

  DXME-173's answer is not overturned, it is spent. Its reasoning was that the
  fleet had no per-player time zone to report, so a reported value would be a
  constant echo of the provisioning template. Once DXSuite pushes a per-player
  zone, that is no longer what a reported value is.

  Full discovery returns as its own effort. DXME-160's measurements stand
  unchanged. **DXME-161's stand only for players DXSuite has not pushed to**,
  because its `Intl` half goes dark on a pushed player. See the trap.

- **Video walls and the entire Pixera path.** Ruled out by
  [DXME-165](https://radianttechnology.atlassian.net/browse/DXME-165).
  `PixeraController` is `[AllowAnonymous]` and contains **zero** references to
  `DeviceId` or `TblDevice`; wall layer shells identify themselves by **location
  id alone**, and their hand-entered `/pixera` URLs live in Pixera project files
  and Moxie zone configs that Director cannot update remotely. So decision 5's
  "requesting device" does not exist on this path. Walls are an airport feature
  and an airport does not cross time zones, so no wall spans zones; they are
  also high-visibility surfaces where an unneeded change is pure risk. Rejected
  giving `VideoWall`-type locations geography, which would reintroduce "is
  _this_ location a place?" at every site. Covers `ClusterLifecycleService`,
  `ClusterResolutionService`, `PixeraLayerSyncService`,
  `PixeraAllocationService`, `BackgroundZoneDerivationService`,
  `PixeraController`, `AirlineStateResolver`, `UserGroupLocationDefaultService`,
  and dissolves the original ticket item 4.

  **Reopening condition:** one Director deployment serving airports in two
  different time zones. Each wall would still be correct in itself, but all
  would resolve against the server's clock. The honest fix then is wall shells
  becoming devices that heartbeat into `tblDevice`.

- **The SLC airline monitor.** `AirlineScheduleMonitorService` writes
  `TblAirline.CarrierStatus1/2` to a **separate Aviation database**, driven by
  two hardcoded display ids from config. Not an airline spanning airports: a
  single-site integration with two fixed screens, specific enough to Salt Lake
  City that it would not be reused. One place by construction, so no multi-zone
  problem, and bringing it in means taking on a cross-database integration for
  zero correctness gain.

- **The ticket-counter and airline write path.** `DefaultAssetActivatorService`
  (airline allocation blocks only, `UserGroupId != null`) and
  `StatusSelectorService` (StatusSelector, TicketCounterScheduler,
  FloorPlanDisplayModal, PanelApiController). Airport features, single-zone by
  construction, high-visibility. Same reopening condition as the walls. DXME-165
  records the mechanism in full, because this is the **stacked** case: the
  display path is three server clocks deep, and `ClearActiveContentAsync`
  deletes by matching `StartDate == today`, which silently misses once the
  server rolls over to a different calendar day than the operator.

- **Access and floor-plan schedules.** Split out at the user's direction and
  filed as [DXME-176](https://radianttechnology.atlassian.net/browse/DXME-176),
  deliberately off this map: that discussion is about access semantics, with
  time zones secondary. Collapsing `ScheduleEvaluator` flips four
  access-control sites from permanently-open to enforced, and bundling a
  fail-open to fail-closed change into a time zone fix means a rollback for a
  time zone bug also reopens the access windows. Not urgent: the initial client
  uses the partial view, unscheduled and always fully open. `ScheduleEvaluator`
  is unreachable from the player path, so deferring it costs nothing there.

- **Operator previews.** All five Blazor display surfaces
  (`StandardDisplay.razor`, `PlaylistDisplay.razor`,
  `PlaylistDisplay_ConcourseEntry.razor`, `CBPMessages.razor`, plus
  `VideoWall.razor` and `AirlineClusterView.razor`) are operator previews: none
  carries an `@page`, and every one is hosted by an authenticated admin page.
  `EffectiveContentResolver` is preview-only by design. They share no code path
  with any player, so a fix would have to be made twice. Showing the operator's
  "now" for a remote display is arguably correct for a preview.

- **A tenant-level or location-level time zone default.** Closed by DXME-163
  decision 12: the seed is the Director host's own time zone, applied at
  device-row creation. No new column on `tblLocation` or `tblTenant`, and no
  create-time default to configure.

- **A bulk-edit UI for assigning time zones.** Bulk assignment is a SQL script
  against the column in decision 11. Building an operator surface for a path
  that is itself temporary is not worth it.

- **A signageOS REST integration.** None exists today, and it existed on this
  map only to serve setting a device's time zone. Out with the discovery item,
  and it returns with it.

- **A player applying a time zone it has been sent.** Bootloader-owned, DXME-154
  on the [Engage for BrightSign map](https://radianttechnology.atlassian.net/browse/DXME-120):
  the app writes the value to a registry key, the bootloader reads it at INIT and
  calls `roSystemTime.SetTimeZone`, and a missing key means the OS zone is left
  alone. **No reopening condition**, because this is owned elsewhere rather than
  deferred, which makes it unlike every other entry in this section.

  **Read the boundary carefully, because this entry used to be wider and is no
  longer.** It said pushing a time zone to a player "was never on this map". Half
  of that is now false: **whether DXSuite emits a time zone is decision 31**, and
  the payload it rides on is
  [DXME-195](https://radianttechnology.atlassian.net/browse/DXME-195). Only the
  player's own application of the value stays out. Note this direction is the
  **opposite** of the device-driven destination, which is the player reporting
  its own, and DXME-173 does not touch it.

  The old entry also warned that pushing needed DXME-172 reopened first, because
  whether `MST` is DST-aware was an inference and setting it on a Phoenix site
  would silently track Denver. **That warning is deleted rather than annotated.**
  DXME-157 resolved it by choosing `POSIX:` strings over the short identifiers,
  and BrightSign documents `MST1` for Arizona.

- **Per-schedule reference-zone authoring**, where an author names a time zone
  so a window fires simultaneously across zones instead of as a rolling wave.
  The requirement is player-local. This would double both the authoring UI and
  the evaluator for a case nobody has asked for.

- **Changing the zone-naive storage model.** `TimeOnly` plus `DateTime` with no
  offset is arguably the correct representation of "respect the player's local
  time", so the defect is in who evaluates and against which clock, not in what
  is stored.

- **POSIX derivation from `TimeZoneInfo`.** Unnecessary and not fully possible.
  All three first-deployment zones have native BrightSign identifiers, a
  BrightSign player already carries a full POSIX string in `process.env.TZ`, and
  Windows supplies no POSIX abbreviation, so a derived Phoenix comes out as
  `UMST7` rather than `MST7`.

- **The on-screen clock widget** (`tblDisplay.UseClock`, `ClockHeight`,
  `ClockWidth`, rendered from the server clock at `StandardDisplay.razor:173`).
  A deprecated feature.

- **Sentinel's `GuardrailService` time zone convention**
  (`GuardrailService.cs:52`), which uses Windows ids and so diverges from
  decision 4 above. Knowingly left alone: it gates remediation business hours,
  not content playback.

- **Manifest cache invalidation coverage, and the TTL increase it would
  unlock.** Ruled out by
  [DXME-166](https://radianttechnology.atlassian.net/browse/DXME-166) and filed
  as [DXME-178](https://radianttechnology.atlassian.net/browse/DXME-178).
  Approval and access-rule edits are database writes that trip no file watcher,
  so nothing invalidates the manifest cache and the TTL is their only backstop;
  `MetadataCacheService` adds a second uninvalidated five-minute cache behind
  it, making approvals up to ten minutes stale today. Raising the TTL from five
  to thirty minutes is a real saving and would have offset the per-request cost
  decision 16 adds, but it would have stretched approval latency to roughly
  thirty-five minutes, and the fix spans three write surfaces with no seam to
  hook.

  **Not a time zone problem at all**, and it predates this map. Kept off it so a
  rollback of the time zone work does not revert a cache-correctness fix.

- **`CounterCalendar.razor` and the ticket-counter calendar.** Out with the
  ticket-counter path, but on a stronger reason than DXME-165 recorded: ticket
  counter video walls are always an on-premises deployment at a single location,
  so there is no time zone problem there by construction rather than by
  convention. Reopening condition: a ticket counter deployment served by a
  Director in another time zone.

- **`DisplayCalendar.razor` stating a time zone.** Not an exclusion but a
  decision, settled by DXME-167: the calendar shows the authored numbers, states
  no time zone, and those numbers resolve per device exactly as playlist windows
  do. The display path's **behaviour** is in scope (decision 19 and DXME-179);
  its calendar's presentation is unchanged.

- **The `TblFloorPlanIconSchedule` authoring gap.** DXME-167 found no editor for
  its time window anywhere in `src/` and could not settle whether it is
  create-only through the bundle importer or simply incomplete. It was designed
  primarily for airports, so it leaves this map with the rest of the access and
  floor-plan work, recorded as a comment on
  [DXME-176](https://radianttechnology.atlassian.net/browse/DXME-176).

- **The server-clock expiry badge on `Playlist.razor`.** `IsExpired` at `:886`
  compares dates on the server clock, driving the `CONTENT EXPIRED` overlay and,
  silently, the loop-length figure at `:1303`. Wrong by 1 hour for a Mountain
  player under a Central Director and **2 hours for Arizona for most of the
  year**. Left alone under the minimum-footprint preference because it is
  operator-facing only and changes nothing about what plays. Filed as
  [DXME-180](https://radianttechnology.atlassian.net/browse/DXME-180).

- **A bulk site-assignment UI.** One device at a time, matching the time zone
  answer. But the reasoning is weaker here and decision 18 records why: site
  assignment never becomes device-driven. Reopening condition: a second
  deployment of this size.

- **App-local ICU.** Ruled out by
  [DXME-170](https://radianttechnology.atlassian.net/browse/DXME-170). Measured
  rather than estimated at **177.0 MiB** in the Host's current RID-less publish
  shape, of which 142 MiB is inert weight `deps.json` never references, or
  **34.5 MiB** with `-r win-x64`, which is itself a change to the publish shape.
  Size is not the decisive part: the pinned build is 72.1.0.3 from October 2023,
  so its CLDR and tzdata vintage becomes ours to maintain, and app-local ICU sits
  **after** NLS in the Windows probe order, so it does not rescue the
  environment-variable cause at all.

  **Reopening condition:** a customer contractually stuck below Windows Server
  2019. It stays the correct answer for that one case, which is why it carries a
  condition rather than a flat rejection, and taking it would earn an ADR, since
  it is a pinned dependency plus a publish-shape change.

- **An OS version check in the installer, and a wizard preflight page.** Ruled
  out by [DXME-170](https://radianttechnology.atlassian.net/browse/DXME-170) as
  decision 28. Recorded here so the absence is not read as an oversight: the
  check is a proxy for the capability, and detection belongs in the runtime,
  which is the only thing that certainly runs on the Director host under both
  upgrade paths. No reopening condition, because the runtime check strictly
  dominates.

- **A canonical system-requirements page.** The OS floor lives in exactly two
  files, `README-customer-deployment.md:22` and `README-NetJets-deployment.md:22`,
  duplicated verbatim, neither in `installer/`, and neither connecting it to ICU.
  DXME-170 adds one sentence to **both**, accepting the honest cost that this
  makes it three verbatim duplications instead of two. De-duplicating them into a
  single page is a docs job with no wrong answer on a screen, so the
  minimum-footprint preference covers it.

- **An ADR for the startup throw.** Ruled out by
  [DXME-170](https://radianttechnology.atlassian.net/browse/DXME-170), which is a
  judgement worth recording because it is surprising: the decision is genuinely
  surprising without context and genuinely a trade-off, but it is a one-line
  revert, so it fails the hard-to-reverse test. The real risk is that it **looks**
  trivially reversible while not being, and the instrument for that is a comment
  where the deleter's cursor already is, citing the ticket, not an ADR they are
  not reading. Note the asymmetry with ADR 0002: decision 24 governs a convention
  every author applies to every persisted `DateTime`, while this governs one check
  in one file.
