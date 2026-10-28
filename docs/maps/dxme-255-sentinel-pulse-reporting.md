# Map: Sentinel health reporting into Pulse

The body of the [DXME-255](https://radianttechnology.atlassian.net/browse/DXME-255)
wayfinder map. It lives in the repo from the start, following the DXME-159,
DXME-120 and DXME-204 maps, so Jira's description size cap never forces
compaction. `git pull` and read this before choosing a ticket.

**Mechanics.** Tickets are child issues of the epic, blocking is the native Jira
"Blocks" link, and a session claims a ticket by assigning it to the map owner
before doing any work. The frontier is the open, unblocked, unassigned children
of the epic. Tracker conventions: `docs/agents/issue-tracker.md` on
`DXME-100-tracker-conventions`.

**Editing rule.** Edit this file and commit. Do not move the body into Jira. A
decision lives in exactly one place, its ticket; this file only gists it and
links. Decisions settled at charting have no ticket and are recorded here in
full.

## Destination

An implementation-ready spec at `docs/spec/sentinel-pulse-reporting.md` on `dev`,
with a code-changes-by-file section a build session can execute without making
further design decisions. **The destination is the spec, not the built feature.**

What the spec delivers:

1. A way to enable syncing Sentinel devices and their health into a customer's
   existing Pulse database.
2. A configurable ErrorLevel per issue type.

Sentinel may eventually replace Pulse. This is deliberately not a deep
integration: it gets Sentinel's view of device health into Pulse without
disturbing the Pulse databases and apps customers already run. The arrival is
[DXME-263](https://radianttechnology.atlassian.net/browse/DXME-263), blocked by
every other open child.

## Notes

**Repo**: `C:\CODE\DXSuite_Blazor`, branch `dev`. See `CONTEXT.md` for
**Pulse**, **Pulse link**, **Issue type** and **Snooze**.

**Skills**: every ticket calls `grilling` and `domain-modeling` unless its type
says otherwise. Prototype tickets also call `prototype`; research tickets call
`research`.

**Standing preferences**: no em dashes in UI copy or doc deliverables. New
config UI uses the StyleGuide `dx-panel` structure. No back-compat shims. Match
surrounding comment density.

**Databases.**

- **SQL02 `Pulse`** is the live, authoritative schema, reachable read-only as
  MCP server `mssql-pulseV2-SQL02`. Never a test target.
- **DCMDEV07 `Pulse`** (`mssql-pulseV2`, writes permitted) was rebuilt on SQL02's
  schema and is the Dev environment's Pulse indefinitely, test customer `RAD`.
- **DCMDEV07 `PulseCore`** (`mssql-pulseCore`, read-only) is live and differs
  again. Never a test target.

**Never copy credential values** out of `tblCustomer`, `tblParameter` or
`tblCustomerIntegration`; those tables hold plaintext secrets.

### Charting facts (PulseCore and DCMDEV07, to be verified on SQL02)

- **Writing ErrorLevel to `tblPulse` raises no alert by itself.** Device down is
  `tblDevice.Heartbeat` older than 6 minutes with `Monitor = 1` and `Snooze = 0`.
- `tblAlert` is written by another app.
- ErrorLevel is varchar: 0 OK, 1 Information, 2 Warning, 3 Error, 4 Mission
  Critical; `-1` means not yet evaluated.
- `tblPulse` key is (DeviceID, Heartbeat, MonitorItemID); CustomerID is not in it.
- `SnoozeTime` is the end of a snooze stored in **Eastern local time**, not UTC.
  `sp_ResetSnoozedPlayers` compares it against UTC, so snoozes end 4 hours
  early, then clears it and emails a hard-coded address. It still runs on SQL02
  ([DXME-256](https://radianttechnology.atlassian.net/browse/DXME-256)).
- Pulse device ids are host names; Director device ids are serials. No overlap
  found in a sample.
- Sentinel has no issue code catalogue, no edge detection anywhere, and its
  device drivers are registered but never poll. The unused
  `DeviceInstance.PulseDeviceId` and `PulseCustomerId` columns exist.
- The SLC `PulseAlertSink` auto-creates Pulse devices it never heartbeats, so
  nothing it creates can ever alert.

## Decisions so far

### Settled at charting (2026-09-17)

1. **The destination is a spec**, as above.
2. **Sentinel is the only DXSuite writer into Pulse.** The SLC `PulseAlertSink`
   is retired; Director alerts reach Pulse through Sentinel's publisher.
3. **What Sentinel may write**: append `tblPulse` readings; keep
   `tblDevice.Heartbeat` current on linked devices; create a Pulse device only on
   explicit request; set and clear snooze. Nothing else Pulse owns.
4. **The Pulse customer ID is per tenant**, in the tenant's config section. For
   single-tenant deployments this is one value per deployment. The per-device
   `PulseCustomerId` column goes unused.
5. **The enable switch and customer ID live in appsettings.** The ErrorLevel
   table lives in a Sentinel config tab, editable without a redeploy.
6. **Pulse means the legacy monitoring system and its database** in `CONTEXT.md`.
   The `DXSuite.Pulse` module is a placeholder with no role here.
7. **Every Pulse control lives on Sentinel's Monitored Devices tab**: linking,
   Create in Pulse, bulk add, snooze. Not on Director's `DevicesTab`. Operators
   never go to two places.
8. **Pulse links are explicit.** A device without a link reports nothing.
   **Create in Pulse** is an explicit per-device action, never automatic, and a
   picker links to existing Pulse devices. Bulk add is wanted.
9. **Issue types are keyed by monitor point id and severity**, with severity-only
   rows as defaults; each row may also choose the Pulse `MonitorItemID` its
   reading is filed under, defaulting to 0 (Device). *Settled by
   [DXME-260](https://radianttechnology.atlassian.net/browse/DXME-260): readings
   aggregate per device and item to the worst open issue.*
10. **Heartbeat every cycle, readings on change.** `tblPulse` gets a reading only
    when an issue opens, changes ErrorLevel or clears (clear at ErrorLevel 0),
    tracked by a per-device last-published record in Sentinel's DB. *Settled by
    [DXME-261](https://radianttechnology.atlassian.net/browse/DXME-261): change is
    `(ErrorLevel, open issue codes)`, and open issues are re-asserted every 24
    hours so `PruneDays` cannot erase them.*
11. **Only Healthy, Warning or Critical advance the heartbeat.** Unknown does
    not, so a device Sentinel cannot vouch for shows down in Pulse; the link UI
    warns about it. A false down is visible, a false up hides an outage.
    *Refined by [DXME-261](https://radianttechnology.atlassian.net/browse/DXME-261):
    the heartbeat carries `max(LastSeenUtc)` across fresh sources and is written
    only when that is within 5 minutes, so evidence older than that stops the
    heartbeat instead of asserting a stale liveness.*
12. **Snooze is "snooze now until"**, against Pulse's own `Snooze` and
    `SnoozeTime`. Readings keep flowing while snoozed. *Settled by
    [DXME-259](https://radianttechnology.atlassian.net/browse/DXME-259):
    `SnoozeTime` is written in UTC, with no `tblDeviceHistory` row.*
13. **Testing uses DCMDEV07 `Pulse` rebuilt on SQL02's schema**, after a backup.
    Never live SQL02, never PulseCore.
14. **Director URL monitors get their own Pulse link for now.** The goal is for
    them to report through a linked device's Pulse link; it may come into scope
    if the lift is light. *Settled by
    [DXME-262](https://radianttechnology.atlassian.net/browse/DXME-262): it comes
    into scope now, and a URL gets no Pulse link of its own. It attaches to a
    Sentinel **reporting device** and publishes through that device's Pulse
    link.*
15. **Driver polling is not this effort.** "All of Sentinel's device health"
    means what Sentinel produces today: Engage heartbeat telemetry,
    cloud-connector probes and agent hosts.

### From tickets

<!-- one line per closed ticket: [title](link): gist -->

- [Survey what SQL02's Pulse schema requires of an outside writer](https://radianttechnology.atlassian.net/browse/DXME-256):
  a writer checklist, and the DCMDEV07 rebuild diff. Write CustomerID and an
  explicit ErrorLevel; UTC heartbeat within 6 minutes; readings purged after
  `PruneDays`; latest reading per device and item hides the rest. Reopens
  decisions 9, 10 and 12.
- [Find whether a Director URL monitor can attach to a Sentinel device, and at what cost](https://radianttechnology.atlassian.net/browse/DXME-257):
  yes for content-synced Pixera, via the existing `LinkedDeviceId`; about 1 to 2
  days on top of the publisher, no schema change; issues keyed per URL; loose
  keys fail silently.
- [Rebuild DCMDEV07 Pulse on SQL02's schema for testing](https://radianttechnology.atlassian.net/browse/DXME-258):
  done. DCMDEV07 `Pulse` now matches SQL02's tables, keys, indexes and lookups;
  view and procedure bodies are still DCMDEV07's and no Agent jobs run. Test as
  customer `SNT`. Script: `docs/research/dxme-258-rebuild-dcmdev07-pulse.sql`.
- [Prototype Pulse linking, Create in Pulse, bulk add and snooze on Monitored Devices](https://radianttechnology.atlassian.net/browse/DXME-259):
  variant D. A sortable grid (Device, Pulse status, Last heartbeat) sits beside
  a Pulse inspector, and ticking rows turns the inspector into bulk add and bulk
  snooze. `SnoozeTime` is written in UTC and shown in local time. There is no
  audit row, and Sentinel never clears an expired snooze.
- [Decide what the issue type ErrorLevel table holds and how it is edited](https://radianttechnology.atlassian.net/browse/DXME-260):
  a code catalogue of 8 monitor points, 2 Director kinds and Source offline;
  whole-device Offline is not an issue type. Defaults Warning 2, Critical 3,
  with Do not report per row. Every issue type defaults to item 0, and the
  publisher writes the worst open issue per device and item. Overrides only, in
  `SentinelPulseIssueLevel`, edited on a new Pulse Reporting `dx-panel` tab;
  saved changes apply on the next cycle. *Refined by
  [DXME-266](https://radianttechnology.atlassian.net/browse/DXME-266):
  `DeviceProbeFailed` is deleted from the catalogue, leaving `UrlMissing` alone in
  the Director group, and Source offline is keyed per source type.*
- [Decide how the Pulse publisher runs](https://radianttechnology.atlassian.net/browse/DXME-261):
  a state reconciler, not an event shipper. `PulsePublisherEngine` on a 60 second
  cycle beside `ServiceTechEngine`, iterating tenants via `IWorkerScopeFactory`.
  Open issues are re-rolled from `DeviceSourceState` each cycle, because
  `LastStateJson` never decays; an open issue is an `ActiveAlert` and nothing
  else. Two new tables hold last-published state. Failures log and drop, never
  queue. `PulseAlertSink` is deleted and Director issues reach the publisher by
  polling, which left connector probes with no durable state to poll
  ([DXME-266](https://radianttechnology.atlassian.net/browse/DXME-266)).

- [Decide where a URL monitor's Pulse link lives, and whether device linking replaces it now](https://radianttechnology.atlassian.net/browse/DXME-262):
  link-through now, and a URL never gets a Pulse link of its own. A new nullable
  `DeviceInstanceId` on `tblDirectorUrl` names the **reporting device**, attached
  on Monitored Devices; Director's `LinkedDeviceId` is untouched. This corrects
  DXME-257: `monitoring-only` is the default `ContentMode` and it does create a
  Sentinel device, just no Director `tblDevice` row, so the gap was the picker,
  not monitorability. Open issues are keyed `UrlMissing:{MonitorId}` with the
  qualifier stripped for the ErrorLevel lookup, because DXME-261's change tuple
  would otherwise miss a second URL dropping. Item 0 by default, overridable.
  Snooze is inherited from the device, `IsMonitored` is the only switch, silence
  is shown only on Monitored Devices, and the retired sink's auto-created Pulse
  devices are identified for an admin, never deleted.

- [Decide how Director connector-probe issues reach Pulse, given their state is in-memory](https://radianttechnology.atlassian.net/browse/DXME-266):
  they do not, as their own issue type. `DeviceProbeFailed` leaves the Pulse
  catalogue: an unreachable Pixera already surfaces through the live systems
  under it, because the connector sync stops advancing their `cloud-connector`
  source rows on the same 60 second cadence as Director's probe. Either the
  heartbeat stalls and Pulse raises its own down alert, or the gateway angle keeps
  the device fresh and the stale angle is Source offline. No connector column, no
  reporting-device picker, and nothing else in `DirectorMonitoringService` needs
  durable state. `ValueVarchar` **names** the source that went offline, and
  Source offline is keyed `SourceOffline:{SourceType}` so a second source loss
  cannot hide behind the first. Director's bell keeps both probe kinds.

- [Assemble the Sentinel Pulse reporting spec](https://radianttechnology.atlassian.net/browse/DXME-263):
  the spec is at `docs/spec/sentinel-pulse-reporting.md`. It settled the last fog.
  Upgrade: two plain EF migrations, a first `sentinel-upgrade-<version>.sql`
  beside the Director script, and least-privilege grants the customer's DBA
  applies. Day one is a runbook with no auto-linking. Tests: a pure planner
  unit-tested in a new `DXSuite.Sentinel.Tests`, plus a manual rehearsal on
  DCMDEV07. DCMDEV07 `Pulse` is the Dev environment's Pulse indefinitely, and
  test customer `RAD` replaces `SNT`. The Pulse connection string resolves from
  config per tenant, not `TenantRecord`. Suggestions drop host-name matching.
  Found a precondition: Sentinel's DbContext factory ignores a worker scope's
  tenant.

## Not yet specified

Empty. The map has arrived: the spec is
[`docs/spec/sentinel-pulse-reporting.md`](../spec/sentinel-pulse-reporting.md).

## Out of scope

- **Writing `tblAlert`**, or anything else another app owns. Another app handles
  alerts.
- **Scheduled or recurring snooze windows.** A wanted future state, but Pulse is
  designed around "snooze until".
- **Sentinel becoming the authority over Pulse**, or replacing it. Long-term
  direction, not this effort.
- **Reading from Pulse back into Sentinel's health or alerting.** The Monitored
  Devices tab does read `tblDevice` rows (Monitor, Snooze, SnoozeTime, Heartbeat)
  to show them and to pick links. That is display only.
- **Fixing the Pulse web UI writing `SnoozeTime` in Eastern local time** while
  its unsnooze job reads it as UTC. Found in
  [DXME-259](https://radianttechnology.atlassian.net/browse/DXME-259); it belongs
  to the Pulse app, as a separate fix.
- **Managing Pulse customers, recipients, templates or emails.**
- **Sentinel taking over URL or Pixera monitoring** from Director.
- **Making `content-only` cloud connectors monitorable.** They deposit a Director
  `tblDevice` row but no Sentinel device, so they can never carry a Pulse link.
  Ruled out by [DXME-262](https://radianttechnology.atlassian.net/browse/DXME-262).
- **Restoring per-connector `BuildDeviceState`.** The cloud-connector source angle
  hard-codes `OverallHealth = Unknown` (`CloudConnectorSyncService.cs:1074`, with a
  TODO); real health comes from probe angles when `GatewayMode != "none"`. Like
  decision 15, this is about acquiring health, not reporting it.
- **Connector-probe issues as their own Pulse issue type**, and any durable open
  state for them. Ruled out by
  [DXME-266](https://radianttechnology.atlassian.net/browse/DXME-266); the
  connector's own live systems carry the outage. Director's bell is untouched.
- **A connector with no synced devices being visible in Pulse.** A Pixera that has
  never been reachable has no live system to report through, and Director's bell
  never fires for it either because `wasOnline` is null on first sight. A
  provisioning failure an installer sees on the connector page, not a monitoring
  event. It goes in the spec as a known limit, and the first-sight gap is a
  Director bug in its own right.
- **Per-source-type freshness windows.** One global `StalenessWindow` makes
  Source offline up to 10 minutes late; the Pulse down path does not depend on it.
  Choosing freshness per source is about acquiring health, like decision 15.
  Ruled out by [DXME-263](https://radianttechnology.atlassian.net/browse/DXME-263).
- **Making Sentinel's device drivers poll.**
- **Any role for the `DXSuite.Pulse` module.**

## Children at charting

| Ticket | Type | Blocked by |
| ------ | ---- | ---------- |
| [Survey what SQL02's Pulse schema requires of an outside writer](https://radianttechnology.atlassian.net/browse/DXME-256) | research | |
| [Find whether a Director URL monitor can attach to a Sentinel device, and at what cost](https://radianttechnology.atlassian.net/browse/DXME-257) | research | |
| [Rebuild DCMDEV07 Pulse on SQL02's schema for testing](https://radianttechnology.atlassian.net/browse/DXME-258) | task | DXME-256 |
| [Prototype Pulse linking, Create in Pulse, bulk add and snooze on Monitored Devices](https://radianttechnology.atlassian.net/browse/DXME-259) | prototype | DXME-256 |
| [Decide what the issue type ErrorLevel table holds and how it is edited](https://radianttechnology.atlassian.net/browse/DXME-260) | grilling | DXME-256 |
| [Decide how the Pulse publisher runs](https://radianttechnology.atlassian.net/browse/DXME-261) | grilling | DXME-256 |
| [Decide where a URL monitor's Pulse link lives, and whether device linking replaces it now](https://radianttechnology.atlassian.net/browse/DXME-262) | grilling | DXME-257 |
| [Assemble the Sentinel Pulse reporting spec](https://radianttechnology.atlassian.net/browse/DXME-263) | task | all of the above |

Added since charting:

| Ticket | Type | Blocked by |
| ------ | ---- | ---------- |
| [Decide how Director connector-probe issues reach Pulse, given their state is in-memory](https://radianttechnology.atlassian.net/browse/DXME-266) | grilling | |

Both research tickets were fired at charting.
