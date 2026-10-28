# Sentinel health reporting into Pulse

The handoff spec for reporting Sentinel's view of device health into a
customer's existing Pulse database. Assembled under
[DXME-263](https://radianttechnology.atlassian.net/browse/DXME-263) from the
closed tickets on the
[DXME-255](https://radianttechnology.atlassian.net/browse/DXME-255) wayfinder
map, the same way the time zone scheduling spec was assembled from DXME-159.

**This is the document a builder implements from.** The map at
[`docs/maps/dxme-255-sentinel-pulse-reporting.md`](../maps/dxme-255-sentinel-pulse-reporting.md)
is an index: 15 charting decisions and 8 closed tickets, several of which refine
or correct earlier ones. This document holds only the surviving decisions.
**Every section cites the ticket that decided it; the ticket's resolution comment
holds the reasoning.** Where a later ticket corrected an earlier one, the
correction is noted so nobody reopens it from the older ticket.

Vocabulary is [`CONTEXT.md`](../../CONTEXT.md): **Pulse**, **Pulse link**,
**Reporting device**, **Issue type**, **Health source** and **Snooze**. The
distinctions that trip people up:

- A **Pulse link** belongs to a Sentinel device. A Director URL monitor has no
  Pulse link; it has a **reporting device**, and reaches Pulse through that
  device's link. A URL's **linked device** (`LinkedDeviceId`) is Director's own
  loose reference and plays no part here.
- An **issue type** is what an ErrorLevel is configured against. A Pulse
  **monitor item** is where the resulting reading is filed. They are not the
  same thing and never share an id space.
- "Down" in Pulse is a stale `tblDevice.Heartbeat`, and nothing else. Writing an
  ErrorLevel raises no alert on its own. The heartbeat is the only real alerting
  path this effort has.

**Section 13 lists known limits and what is open.** Nothing there blocks the
build.

---

## 1. The shape of the work

Sentinel gains one output adapter, the **Pulse publisher**, and the operator
controls to feed it. When a tenant switches reporting on:

1. The operator **links** Sentinel devices to Pulse devices on Monitored
   Devices, or creates the Pulse device from there. A device without a link
   reports nothing.
2. Every 60 seconds the publisher, for each linked device, **re-derives current
   health** from Sentinel's health sources and writes to Pulse only what differs
   from what it last wrote: a fresh `tblDevice.Heartbeat` while Sentinel can
   vouch for the device, and a `tblPulse` reading per monitor item when that
   item's open issues change.
3. The ErrorLevel each issue type reports at, and the monitor item it is filed
   under, are editable on a new **Pulse Reporting** config tab without a
   redeploy.
4. The operator can **snooze** a linked device from Monitored Devices, against
   Pulse's own `Snooze` and `SnoozeTime`.

This is deliberately shallow. Sentinel may eventually replace Pulse, so this
gets Sentinel's view into Pulse without disturbing the Pulse databases and apps
customers already run. Sentinel is the only DXSuite writer into Pulse: the SLC
`PulseAlertSink` is deleted (charting decision 2).

What Sentinel reports is what it produces today: Engage heartbeat telemetry,
cloud connector sync, site gateway probes and agent hosts. Making device drivers
poll is not this work (charting decision 15).

---

## 2. The model

### Pulse link

A Pulse link is `SentinelDeviceInstance.PulseDeviceID` (varchar 50, already
present and unused) naming a `tblDevice.DeviceID` under the tenant's configured
Pulse customer. The customer is **per tenant, not per device**: it comes from
configuration (section 7), and `SentinelDeviceInstance.PulseCustomerID` stays
unused (charting decision 4). Links are explicit and never inferred from
matching ids (charting decision 8).

### Reporting device

`tblDirectorUrl.DeviceInstanceId` (new, nullable int, no foreign key because it
names a row in another database) names the Sentinel device a URL monitor
publishes through. It is edited on Monitored Devices, never on Director's URLs
tab: Director cannot reference Sentinel, so that tab can never list Sentinel
devices
([DXME-262](https://radianttechnology.atlassian.net/browse/DXME-262)).

A URL reaches Pulse only when **all** of these hold: `IsMonitored` is true, a
reporting device is set, and that device has a Pulse link. `IsMonitored` is the
only switch; there is no Sentinel-side toggle.

This covers `monitoring-only` (the default `ContentMode`) and
`content-and-monitoring` connectors, both of which create a Sentinel device.
`content-only` connectors create no Sentinel device and cannot report.

> **Correction to DXME-257.** That research called monitoring-only Pixera
> connectors unmonitorable. They are monitorable: `CloudConnectorSyncService`
> creates the `DeviceInstance` for them (`:209`) and only skips the Director
> `tblDevice` row, which is the content path (`:317`). The gap was the URL
> picker. Settled by DXME-262.

### Open issues

An **open issue** is one of three things, derived fresh every cycle. Nothing
else is an open issue.

| Kind | Where it comes from | Open issue code | Lookup key |
|---|---|---|---|
| Monitor point | an `ActiveAlert` in the device's re-rolled `DeviceState` | `{MonitorPointId}/{severity}`, e.g. `cpu-percent/critical` | (MonitorPointId, severity) |
| Source offline | a health source row gone stale while the device's heartbeat is still being written | `SourceOffline:{SourceType}`, e.g. `SourceOffline:cloud-connector` | `SourceOffline` |
| Director alert | a `tblDirectorUrl` row with `AlertState = "missing"` whose reporting device is this device | `UrlMissing:{MonitorId}` | `UrlMissing` |

- **An open issue is an `ActiveAlert` and nothing else**, for the monitor point
  kind. A driver that sets a non-healthy `MonitorReading.Status` without adding
  an `ActiveAlert` is a bug. Both producers (`EngageTelemetryParser`,
  `AgentHostRegistry`) agree today; nothing enforces it
  ([DXME-261](https://radianttechnology.atlassian.net/browse/DXME-261)).
- `ActiveAlert.Severity` is a lowercase string. `warning` and `critical` map to
  themselves; any other value is treated as `warning`.
- The **qualifier** after `:` in `SourceOffline:` and `UrlMissing:` codes exists
  so a second source or URL loss on the same device changes the code set. The
  ErrorLevel and monitor item lookup **strips it**, so the config tab shows one
  row per kind
  ([DXME-262](https://radianttechnology.atlassian.net/browse/DXME-262),
  [DXME-266](https://radianttechnology.atlassian.net/browse/DXME-266)).
- Source offline is qualified by `SourceType`, not `SourceKey`: a device holds
  at most one angle per type, and the full key would churn the code whenever a
  device moves between gateways.
- **Not issue types:** whole-device Offline or Unknown (the stalled heartbeat
  covers them), the Director recovery kinds (they are clears), and
  `DeviceProbeFailed`, which is deleted from the catalogue
  ([DXME-266](https://radianttechnology.atlassian.net/browse/DXME-266)).

### Why connector probes need no issue type

An unreachable Pixera stops `SyncSingleConnectorAsync` advancing the
`cloud-connector` source rows of its live systems
(`SourceKey = "{ConnectorId}:{PlatformDeviceId}"`, `CloudConnectorSyncService.cs:299`),
on the same 60 second cadence as Director's probe. Either every source goes stale
and the heartbeat stops, so Pulse raises its own down alert; or the gateway angle
(`:716`) keeps the device fresh and the stale connector angle is Source offline.
Director's bell keeps receiving `DeviceProbeFailed` and `DeviceProbeRecovered`
unchanged ([DXME-266](https://radianttechnology.atlassian.net/browse/DXME-266)).

### Monitor items and the worst-of rule

Each issue type resolves to a Pulse `MonitorItemID`, default **0 (Device)**,
which is defined on SQL02 and carries no readings from anything else. The
publisher writes **one reading per (device, monitor item)** at the **highest
ErrorLevel among that item's open issues**. The item clears (ErrorLevel `'0'`)
only when no open issue maps to it. This stops two issues on one item hiding
each other in `View_LastMonitor`, which shows only the latest reading per
(device, item)
([DXME-260](https://radianttechnology.atlassian.net/browse/DXME-260)).

---

## 3. The Pulse write contract

The full survey is
[`docs/research/sql02-pulse-writer-contract.md`](../research/sql02-pulse-writer-contract.md)
on `research/dxme-256-sql02-pulse-schema`
([DXME-256](https://radianttechnology.atlassian.net/browse/DXME-256)). SQL02 is
the authoritative schema. This section is what the build must honour.

### What Sentinel writes, and nothing else

(Charting decision 3.)

| Write | Table | When |
|---|---|---|
| Insert a reading | `tblPulse` | publisher, on change or re-assert |
| Update `Heartbeat` | `tblDevice` | publisher, linked devices only |
| Insert a device | `tblDevice` | operator presses Create in Pulse |
| Update `Snooze`, `SnoozeTime` | `tblDevice` | operator snoozes or unsnoozes |
| Update `Monitor` (with `Heartbeat` when starting) | `tblDevice` | operator presses Start monitoring or Stop monitoring |

**Never:** `tblAlert`, `tblMonitoritem`, templates, `tblParameter`,
`tblDeviceHistory`, or any delete. The **publisher** never writes `Monitor`:
that is the operator's statement about alerting, not about whether Sentinel
reports. Linked devices with `Monitor = 0` still get heartbeats.

Operators can change `Monitor` on a linked device from the inspector
([DXME-284](https://radianttechnology.atlassian.net/browse/DXME-284)). Before
this, Sentinel set it only at Create in Pulse, and changing it later needed a
Pulse admin. Operators need to start and stop Pulse alerting without one. It is
an operator action like Snooze, not publisher behaviour, so invariant 2 still
holds.

### Readings (`tblPulse`)

```sql
INSERT INTO dbo.tblPulse
    (DeviceID, CustomerID, Heartbeat, MonitorItemID,
     ValueVarchar, ValueInteger, ValueDateTime, ErrorLevel, LastEditUser)
VALUES
    (@DeviceId, @CustomerId, @StampUtc, @MonitorItemId,
     @Summary, @OpenIssueCount, @OldestTriggeredUtc, @ErrorLevel, 'Sentinel')
```

- `CustomerID` is **never NULL**. A NULL hides the reading from every current
  state view.
- `ErrorLevel` is always explicit, the string `'0'` to `'4'`. Omitting it
  defaults to `'-1'`, which a Pulse job may overwrite.
- `Heartbeat` is the cycle stamp in **UTC**, truncated to the second (see
  section 4, Key collisions). Never in the future.
- `ValueVarchar` is a plain one-line summary, semicolon separated, capped at 500
  characters with a trailing `…` when cut. Example:
  `Disk used 96% (critical); Source offline: Pixera 'SLC Ticket Wall'`.
  On a clear it is `OK`.
- `ValueInteger` is the count of open issues mapped to the item (0 on a clear).
  `ValueDateTime` is the oldest `TriggeredAt` among them (NULL on a clear).
- `CreateDate` is left to its default. `LastEditUser = 'Sentinel'` identifies
  the writer; every live row leaves it NULL.

The summary per open issue kind:

| Kind | Text |
|---|---|
| Monitor point | the `ActiveAlert.Message`, then ` (warning)` or ` (critical)` |
| Source offline, `cloud-connector` | `Source offline: Pixera '{tblCloudPlatformConnector.DisplayName}'`, the `ConnectorId` taken from `SourceKey` before the first `:` |
| Source offline, `gateway` | `Source offline: gateway '{SiteGateway name}'`, or `direct probe` when the key is `direct` |
| Source offline, `heartbeat` | `Source offline: Engage heartbeat ({SourceKey})` |
| Source offline, `agent-host` | `Source offline: agent ({SourceKey})` |
| `UrlMissing` | `URL missing: {tblDirectorUrl.UrlKey}` |

The summary text **never triggers a write** (section 4, What counts as a change).

### Heartbeat (`tblDevice`)

```sql
UPDATE dbo.tblDevice SET Heartbeat = @HeartbeatUtc
WHERE DeviceID = @DeviceId AND CustomerID = @CustomerId
```

The value and the rule for writing it are in section 4. Pulse's down job runs
every 2 minutes and flags `Monitor = 1`, `Snooze = 0` devices whose heartbeat is
more than 6 minutes old or more than 6 minutes in the future.

### Create in Pulse

```sql
INSERT INTO dbo.tblDevice
    (DeviceID, CustomerID, Description, DeviceTypeID, MonitorTemplateID,
     Monitor, Debug, Snooze, IsMaster, Heartbeat)
VALUES
    (@DeviceId, @CustomerId, @Description, @DeviceTypeId, 1,
     @Monitor, 0, 0, 0, @HeartbeatUtc)
```

- Every NOT NULL column is set explicitly. `Monitor` defaults to 1 in Pulse, and
  a monitored device with no heartbeat raises a Mission Critical alert within
  about 8 minutes.
- `Monitor` is the operator's choice, **Monitor** (1) or **Don't monitor yet**
  (0). **Monitor is disabled** unless the device currently has an eligible
  heartbeat (section 4). When it is chosen, `Heartbeat` is set to that value;
  otherwise `Heartbeat` is NULL.
- `DeviceTypeID` is picked from the customer's `tblDeviceType`, default 1
  (Player PC). `MonitorTemplateID` is always 1; Sentinel devices run no Pulse
  agent, so the template collects nothing.
- `Description` defaults to the Sentinel `DisplayName`, truncated to the column.
- The DeviceID must not exist under this customer (refuse). If it exists under
  **another** customer, warn: `tblPulse`'s key omits `CustomerID`, so the two
  can collide.
- On success the Pulse link is set in the same action.

### Snooze

```sql
-- snooze now until
UPDATE dbo.tblDevice SET Snooze = 1, SnoozeTime = @UntilUtc
WHERE DeviceID = @DeviceId AND CustomerID = @CustomerId
-- unsnooze
UPDATE dbo.tblDevice SET Snooze = 0, SnoozeTime = NULL
WHERE DeviceID = @DeviceId AND CustomerID = @CustomerId
```

- `SnoozeTime` is written in **UTC**. Pulse's unsnooze job compares it with
  `GETUTCDATE()`, so UTC is the contract. The Pulse web UI writes Eastern local
  time; that is a Pulse bug, out of scope. Until it is fixed, the Pulse UI shows
  Sentinel-set snoozes 4 hours off (5 in winter)
  ([DXME-259](https://radianttechnology.atlassian.net/browse/DXME-259)).
- No `tblDeviceHistory` row.
- Sentinel **never clears an expired snooze**. Pulse's own job does.
- Readings keep flowing while snoozed (charting decision 12). The publisher has
  no snooze duties.

### Start and stop monitoring

```sql
-- start monitoring
UPDATE dbo.tblDevice SET Monitor = 1, Heartbeat = @HeartbeatUtc
WHERE DeviceID = @DeviceId AND CustomerID = @CustomerId
-- stop monitoring
UPDATE dbo.tblDevice SET Monitor = 0
WHERE DeviceID = @DeviceId AND CustomerID = @CustomerId
```

- **Start** follows Create in Pulse's rule: it is allowed only while the device
  has an eligible heartbeat (section 4), and refused with the suppression reason
  (Offline, Unknown or Stale evidence) otherwise. The heartbeat is written in
  the same UPDATE, so Pulse never sees the device monitored with a stale
  heartbeat and raises no instant down alert.
- **Stop** is always allowed. It silences Pulse's down alerts; readings and
  heartbeats keep flowing.
- No `tblDeviceHistory` or `tblAlert` row, matching Snooze.
- A zero-row update means the Pulse device is gone (`DeviceMissing`). SQL error
  229 (object) or 230 (column) comes back as `PermissionDenied`, and the
  inspector tells the operator to ask the DBA to re-apply the grants below. A
  login holding only the earlier column grant gets 230, not 229.
- The change is logged with the operator's user name.

### Permissions the Pulse login needs

The customer's DBA applies these; the deployment never runs them. Reference
block for the upgrade notes:

```sql
GRANT SELECT ON dbo.tblDevice       TO [<sentinel login>];
GRANT SELECT ON dbo.tblDeviceType   TO [<sentinel login>];
GRANT SELECT ON dbo.tblMonitoritem  TO [<sentinel login>];
GRANT SELECT ON dbo.tblCustomer     TO [<sentinel login>];
GRANT INSERT ON dbo.tblPulse        TO [<sentinel login>];
GRANT INSERT ON dbo.tblDevice       TO [<sentinel login>];
GRANT UPDATE (Heartbeat, Snooze, SnoozeTime, Monitor) ON dbo.tblDevice TO [<sentinel login>];
```

Table names follow SQL02's spelling (`tblMonitoritem`, lower-case `i`).
`Monitor` was added to the UPDATE grant by DXME-284. A customer whose DBA
applied the earlier grant must re-apply it before Start and Stop monitoring
work.

---

## 4. The publisher

Settled by [DXME-261](https://radianttechnology.atlassian.net/browse/DXME-261).

**It is a state reconciler, not an event shipper.** Every cycle it re-derives
current truth and writes only what differs from what it last published. Nothing
is queued, replayed or retried across cycles.

### Where it runs

- `PulsePublisherEngine : BackgroundService` in `src/DXSuite.Sentinel/Services/Engine/`,
  registered in the `runBackgroundServices` block of
  `src/DXSuite.Sentinel/Services/Extensions.cs` beside `ServiceTechEngine`.
- A 60 second cycle (`CycleSeconds`), 15 second initial delay, lock
  `sentinel-pulse-publisher-lock` for 5 minutes through `IDistributedLockService`
  (the NoOp implementation today, as for every Sentinel engine).
- Skips the cycle when `ISentinelAvailabilityService.IsAvailable` is false.
- Not a step inside the evaluator cycle: one bad remediation run must not stop
  heartbeats fleet-wide.
- **It iterates tenants** through `IWorkerScopeFactory.WorkerTenants` and
  `CreateTenantScopeAsync`, the first Sentinel service to do so. The pattern to
  copy is Director's `AssetCleanupService` (`:46` to `:63`): per tenant, open a
  scope, skip when the tenant's reporting is off or it has no Sentinel
  connection string, catch and log per tenant. Tenants run **sequentially**.

> **Precondition in this repo.** Sentinel's `DbContextFactory` registration
> (`Services/Extensions.cs:31`) resolves the tenant only from `HttpContext`, and
> falls back to the top-level `ConnectionStrings:Sentinel` otherwise. Inside a
> worker tenant scope that silently reads the default tenant's Sentinel DB for
> every tenant. It must adopt Director's scope-first resolution
> (`Director/Services/Extensions.cs:40`): the scope's populated
> `ITenantContext`, then `HttpContext`, then config.

### One cycle, per tenant

1. Read the tenant's `PulseReporting` settings and Pulse connection string
   (section 7). Skip the tenant if not enabled.
2. Stamp `nowUtc` once, truncated to the second. This is the `Heartbeat` of
   every reading this tenant writes this cycle.
3. Load, in one query each: linked devices (`PulseDeviceID` not null,
   `IsEnabled`), their `DeviceSourceState` rows, their `SentinelPulseDeviceState`
   and `SentinelPulseItemState` rows, the `SentinelPulseIssueLevel` overrides,
   the Director URL rows with a reporting device among them, and the connector
   and gateway names needed for summaries.
4. For each linked device, skipping any marked **link broken**:
   1. **Relink check.** If `SentinelPulseDeviceState` names a different
      `PulseDeviceId` or `PulseCustomerId` than the current link and
      configuration, delete its item state rows and treat the device as first
      run. Nothing is written to the old Pulse device: no farewell clear.
   2. **Re-roll** the device's `DeviceState` from its `DeviceSourceState` rows
      through the new `DeviceHealthRollup.BuildState` overload.
      `LastStateJson` is **not** the source: it is recomputed only when a
      source reports, so it never decays, and a dead device would publish
      Healthy forever.
   3. **Heartbeat eligibility.** Let `fresh` be the sources with
      `LastSeenUtc` within `StalenessWindow` (10 minutes). The device is
      eligible when the rolled `OverallHealth` is Healthy, Warning or Critical
      **and** `max(fresh.LastSeenUtc)` is within **5 minutes** of `nowUtc`.
      Otherwise record a suppression reason (`Offline`, `Unknown`, or
      `Stale evidence`) and write no heartbeat (charting decision 11, as
      refined).
   4. **Derive open issues** (section 2). Source offline issues are raised only
      when the device is heartbeat-eligible: a stale source on a device whose
      heartbeat has stopped is already covered by Pulse's down alert.
   5. **Resolve** each open issue's ErrorLevel and monitor item (section 5).
      Drop issues resolving to Do not report.
   6. **Group by monitor item**, taking the worst ErrorLevel and the sorted set
      of open issue codes per item.
   7. **Heartbeat first.** If eligible, write
      `Heartbeat = min(max(fresh.LastSeenUtc), nowUtc)` unless it equals
      `LastHeartbeatSentUtc`.
   8. **Then readings,** per item, when the item changed, or when it is above
      `'0'` and its last publish is older than `ReassertHours`.
   9. Update `SentinelPulseDeviceState` and `SentinelPulseItemState`. Item state
      advances only for writes that succeeded.

Heartbeat before readings: if the heartbeat lands and readings fail, Pulse shows
the device up with slightly stale readings and self-corrects next cycle. The
reverse order files a Critical reading against a device Pulse already thinks is
down, which reads as two faults.

The heartbeat carries the **real** evidence timestamp, not `GETUTCDATE()`, and
only within 5 minutes: this lets Pulse's own 6 minute threshold do the
detecting, and cuts heartbeat-path detection from about 16 minutes to about 6
without touching `StalenessWindow`.

### What counts as a change

Per (device, item), compare `(ErrorLevel, sorted open issue codes)` with the
stored `SentinelPulseItemState`. Different means write. The summary text rides
along but **never triggers a write**: a disk drifting from 96% to 97% must not
write a row every cycle (charting decision 10).

This comparison covers configuration changes with no extra machinery. A saved
level change moves the ErrorLevel; Do not report removes the code and, if it was
the last, clears the item; a monitor item change clears the old item and opens
the new one. Each takes effect on the next cycle
([DXME-260](https://radianttechnology.atlassian.net/browse/DXME-260)).

An item that has state but no longer has open issues is written once as a clear
(`'0'`), and its state row keeps `ErrorLevel = 0` with an empty code set.

### First run and new links

Publish the heartbeat and **only items with an open issue**. No ErrorLevel `'0'`
baseline for items that never fired: that would invent rows Pulse never held.

### Surviving retention

Pulse purges readings older than `PruneDays` nightly (15 days for RAD on SQL02)
with no archive. Items above `'0'` are **re-asserted every `ReassertHours`**
(default 24), which costs one row per open item per day. Cleared items are never
re-asserted.

### Key collisions

`tblPulse`'s primary key is (DeviceID, Heartbeat, MonitorItemID) without
`CustomerID`, and `DeviceID` is unique only per customer. One stamp per tenant
per cycle, at most one row per (device, item) per cycle, and tenants taking their
own stamp in sequence keep collisions rare. On error 2627, retry once with the
stamp **minus** one second, then log and drop; the next cycle re-derives.

> DXME-261 said plus one second. Minus keeps invariant 3 exact: the stamp is
> already truncated to the second, so plus one can land in the future. The
> previous cycle's row is 60 seconds older, so minus one cannot reorder it.

### Failures

- **Pulse unreachable or rejecting:** log and drop. State is not advanced, so
  the next good cycle publishes current truth. No backlog.
- Per-device outcome goes to `SentinelPulseDeviceState`: `LastAttemptUtc`
  always, `LastSuccessUtc` and `ConsecutiveFailures = 0` on success,
  `ConsecutiveFailures + 1` and `LastError` on failure.
- **Dangling link** (error 547 on `FK_tblPulse_tblDevice`, or the heartbeat
  UPDATE touching zero rows): set `LinkBrokenUtc`, stop attempting that device,
  show **Link broken** on Monitored Devices. Never auto-recreate. It clears when
  the operator changes the link, unlinks, or presses **Retry** in the inspector.
- **Error 547 on `FK_tblPulse_tblMonitoritem`:** rewrite that reading at item 0
  and log a warning naming the issue type. The config tab validates items on
  save, so this is a backstop.
- No new alerting. Pulse already detects the stopped heartbeat.

### Startup validation

When `Enabled` is true for a tenant and `CustomerId` is blank, or no Pulse
connection string resolves, the engine **throws at startup** naming the tenant.
A NULL `CustomerID` hides every reading, so a blank must fail loudly.

---

## 5. Issue types and ErrorLevels

Settled by [DXME-260](https://radianttechnology.atlassian.net/browse/DXME-260),
with `DeviceProbeFailed` removed by
[DXME-266](https://radianttechnology.atlassian.net/browse/DXME-266).

### The shipped catalogue

A fixed list in code, `PulseIssueCatalogue`:

| Group | Issue type | Severities | Shipped level |
|---|---|---|---|
| Engage player | `web-content-status` | Warning, Critical | 2, 3 |
| Engage player | `web-content-js-errors` | Warning | 2 |
| Engage player | `input-status` | Warning | 2 |
| Engage player | `console-errors` | Warning | 2 |
| Engage player | `screenshot-age` | Warning | 2 |
| Agent host | `cpu-percent` | Warning, Critical | 2, 3 |
| Agent host | `memory-used-percent` | Warning, Critical | 2, 3 |
| Agent host | `disk-used-percent` | Warning, Critical | 2, 3 |
| Director | `UrlMissing` | | 3 |
| Source offline | `SourceOffline` | | 2 |
| Defaults | severity default Warning | | 2 |
| Defaults | severity default Critical | | 3 |

Sources: `EngageTelemetryParser.cs:60` to `:127`, `AgentHostRegistry.cs:28` to
`:48`. A monitor point not in the catalogue still reports, through its severity
default, so a new point needs no table change.

### Resolution chain

For a monitor point issue: its (MonitorPointId, severity) override row, then the
severity default override row, then the shipped value. For `SourceOffline` and
`UrlMissing`: their override row, then the shipped value. The **level** and the
**monitor item** resolve independently along the same chain; the item ends at 0.
A NULL in either column inherits, so a row that only moves the item keeps
following later changes to the severity default. **Do not report** is its own
flag, and ends the level chain wherever it is set.

Levels are 1 to 4 or **Do not report**. Level 0 is never offered; 0 is a clear.

### `SentinelPulseIssueLevel`

Overrides only; shipped values live in code, and "Reset to default" deletes the
row. Tenant-scoped by living in the tenant's Sentinel DB, with no customer
column.

| Column | Type | Notes |
|---|---|---|
| `SentinelPulseIssueLevelID` | int identity | PK |
| `Kind` | varchar(20) | `SeverityDefault`, `MonitorPoint`, `SourceOffline`, `DirectorAlert` |
| `MonitorPointId` | varchar(100) null | set for `MonitorPoint` |
| `Severity` | varchar(20) null | `warning` or `critical`; set for `SeverityDefault` and `MonitorPoint` |
| `DirectorAlertKind` | varchar(30) null | `UrlMissing`; set for `DirectorAlert` |
| `ErrorLevel` | tinyint null | 1 to 4; NULL means inherit |
| `DoNotReport` | bit, default 0 | 1 means Do not report; `ErrorLevel` is then NULL |
| `MonitorItemID` | int null | NULL means inherit |
| `LastEditUser` | varchar(100) null | |
| `LastEditDate` | datetime2 | UTC |

Unique index on (`Kind`, `MonitorPointId`, `Severity`, `DirectorAlertKind`).
`DirectorAlertKind` stays a column although the Director group holds one row: the
enum can grow again.

### The Pulse Reporting tab

`ConfigTab("sentinel-pulse-reporting", "Pulse Reporting", typeof(Components.PulseReportingTab), 59)`,
after Monitored Devices (58). Its permission is the generic
`config-tab:sentinel-pulse-reporting`, default role Architect, like every config
tab. StyleGuide `dx-panel` structure.

- When reporting is off for the tenant, the tab still loads and edits, under a
  notice: "Pulse reporting is off. Changes apply once it is switched on."
- One grid, grouped **Defaults**, **Engage player**, **Agent host**,
  **Director**, **Source offline**. One row per catalogue entry and severity.
- **Level** select: `Default (Warning, 2)` style first option, then 1 to 4, then
  `Do not report`.
- **Monitor item** select: `Default (0 Device)`, then the customer's live
  `tblMonitoritem` rows, **excluding** those with a NULL `ItemTypeID` (65, 66, 67
  and 77 on SQL02). Items Pulse agents write (1 to 4, 10 and 13 to 25) carry a
  warning that a live agent's newer reading will mask Sentinel's.
- A saved item id that no longer exists in `tblMonitoritem` shows a warning on
  its row; the publisher treats it as 0.
- Save **validates every chosen item against the customer's Pulse** before
  writing, and refuses the save naming the bad rows.
- Overridden rows are marked and carry a reset link. Save and Discard sit in
  `dx-panel-controls`. Nothing autosaves.
- If Pulse is unreachable, the item select shows only the saved id, disabled,
  with a warning. Level edits still save.

---

## 6. Monitored Devices

Layout is **variant D** of the prototype on `prototype/sentinel-pulse-controls`
(04b2131, `src/DXSuite.Sentinel/Components/prototype/pulse-controls.html?variant=D`)
([DXME-259](https://radianttechnology.atlassian.net/browse/DXME-259)). Every Pulse
control lives here, never on Director's `DevicesTab` (charting decision 7). The
Pulse parts render only when reporting is enabled for the tenant; the tab is
otherwise unchanged.

### Status line

At the top: last successful publish (`max(LastSuccessUtc)` across the tenant's
device state), and the number of linked devices currently failing
(`ConsecutiveFailures > 0`), or "Pulse unreachable" when the tab's own read of
Pulse fails. Read from Sentinel's tables, so it works in a web-only role.

### Grid

| Column | Content |
|---|---|
| checkbox | selection for bulk actions |
| Device | name, health badge, source list |
| Pulse | DeviceID and a status chip |
| Last heartbeat | `LastHeartbeatSentUtc`, in the viewer's local time |

Status chips, derived for display only from Pulse's `tblDevice` row and
Sentinel's device state:

| Chip | Condition |
|---|---|
| Not linked | no `PulseDeviceID` |
| Link broken | `LinkBrokenUtc` set |
| Not monitored | Pulse `Monitor <> 1` |
| Snoozed until {local time} | Pulse `Snooze = 1` |
| Down (Sentinel cannot vouch) | Pulse heartbeat older than 6 minutes and a suppression reason is set |
| Down | Pulse heartbeat older than 6 minutes, no suppression reason |
| Up | otherwise |

All three headers sort. Default sort is Last heartbeat, oldest first, with
unlinked devices always last. The tab reads Pulse with one query per load:
`SELECT DeviceID, Description, Monitor, Snooze, SnoozeTime, Heartbeat FROM dbo.tblDevice WHERE CustomerID = @CustomerId`.
This is display only; nothing read from Pulse feeds Sentinel's health or
alerting.

### Inspector, one device

Clicking a row opens the Pulse inspector beside the grid.

- **Tiles:** Pulse link; In Pulse (status and whether it is monitored); last heartbeat sent,
  with the suppression reason when none is being sent (for example "not sent
  while Offline"); Snooze.
- **Unlinked:** a picker of the customer's Pulse devices with suggestions
  first, and **Create in Pulse…** beside it. A DeviceID that also exists under
  another customer is marked.
- **Linked:** a Pulse monitoring card with **Start monitoring** or **Stop
  monitoring** (section 3). Start is disabled, with the reason, while Sentinel
  cannot vouch for the device; Stop asks for confirmation ("Pulse will stop
  raising down alerts for this device"). Then Snooze ("snooze now until", a date
  and time in the viewer's local time), Unsnooze, and a Link section with Change
  link and Unlink. Unlink
  leaves the Pulse device in place and deletes the Sentinel-side state rows.
- **Link broken:** the chip, the error, and **Retry**.
- **Warnings** for a device that is Unknown or Offline: Pulse will show it down.
- **URL monitors reporting through this device:** the attached URLs, each with
  inline state for "not monitored" (`IsMonitored` false) and, on the list header,
  "device has no Pulse link". Attach and Detach set or clear
  `tblDirectorUrl.DeviceInstanceId`. The attach picker lists the tenant's
  `tblDirectorUrl` rows.

**Suggestions** are strong matches only, case-insensitive: `DirectorDeviceID`
equals the Pulse `DeviceID`, or `DisplayName` equals the Pulse `DeviceID` or
`Description`. There is no host name column to match on.

**Create in Pulse** defaults: DeviceID is `DirectorDeviceID` when it is present
and not a GUID; otherwise an upper-case slug of `DisplayName` (letters, digits
and `-`, capped at 50). Monitor defaults to **Don't monitor yet**, and
**Monitor** is disabled while the device is not heartbeat-eligible.

### Inspector, several devices

Ticking rows turns the inspector into a bulk inspector.

- **Unlinked rows:** each gets Link to suggestion, Create as `<default id>`, or
  Skip, then one **Apply**. Rows run in order and each reports its own result;
  one failure does not stop the rest. Created devices use Don't monitor yet
  unless the row is heartbeat-eligible and the operator chose Monitor.
- **Linked rows:** bulk Snooze until, and bulk Unsnooze.

### Unattached URL monitors

Above the grid, a collapsible list of `IsMonitored` URL monitors with no
reporting device, so they are not invisible for want of a device to appear
under. Nothing is added to Director's URLs tab
([DXME-262](https://radianttechnology.atlassian.net/browse/DXME-262)).

---

## 7. Configuration

```json
"Sentinel": {
  "PulseReporting": {
    "Enabled": false,
    "CustomerId": "",
    "CycleSeconds": 60,
    "ReassertHours": 24
  }
}
```

- Read per tenant from `Tenants:<subdomain>:Sentinel:PulseReporting`, key by key,
  falling back to top-level `Sentinel:PulseReporting` (charting decision 5).
- The Pulse connection string resolves from
  `Tenants:<subdomain>:ConnectionStrings:Pulse`, falling back to top-level
  `ConnectionStrings:Pulse`. It is **not** added to `TenantRecord` or
  `ITenantContext`; one resolver, `PulseReportingSettingsResolver`, serves the
  publisher and both tabs. DXME-261's `ConnectionStringName` setting is dropped:
  the name is always `Pulse`.
- `Enabled` defaults **false**, so an upgrade never starts writing into a
  customer's live Pulse unattended.
- The ErrorLevel table is **not** configuration; it is data in the tenant's
  Sentinel DB, edited on the Pulse Reporting tab.

### The Dev environment

DCMDEV07 `Pulse`, rebuilt on SQL02's schema
([DXME-258](https://radianttechnology.atlassian.net/browse/DXME-258)), is the
**Dev environment's Pulse database indefinitely**. `src/DXSuite.Host/appsettings.json`
already points `ConnectionStrings:Pulse` there, top level and under
`Tenants:Default`. WEB02's deployed `appsettings.json` did not have it; the
top-level entry was added there by hand at enablement. dev.dxsuite.ai (WEB02) ships with `Enabled: true` and
`CustomerId: "RAD"` once the build lands.

RAD on DCMDEV07 already holds 37 legacy devices (10 with `Monitor = 1`, most
last heartbeated 2019 to 2022), 8 readings and 9 parameter rows. They stay as a
realistic fixture: they exercise the link picker, both `Monitor` cases and the
retired-sink SELECT (`TEST-WALL-01` matches it). No Agent jobs run on DCMDEV07,
so the stale monitored rows raise nothing. The `SNT` customer from DXME-258 is
left dormant, not deleted.

---

## 8. Invariants

A reviewer checks these. Each is testable or greppable.

1. An open issue is an `ActiveAlert`, a stale source on a heartbeat-eligible
   device, or a missing URL with a reporting device. Nothing else.
2. The publisher never writes `tblAlert`, `tblMonitoritem`, `tblDeviceHistory`,
   `tblDevice.Monitor`, or any delete. This covers the publisher only: an
   operator may change `Monitor` from the inspector (section 3).
3. No `Heartbeat`, in either table, is ever later than `nowUtc`.
4. `ErrorLevel` is always an explicit `'0'` to `'4'`; `CustomerID` is never
   NULL.
5. The summary text never decides whether a reading is written.
6. `Enabled` true with a blank `CustomerId` or no connection string fails at
   startup.
7. A device without a Pulse link causes no Pulse I/O at all.
8. Unlink and relink write nothing to the old Pulse device.
9. Pulse failures never queue; state advances only on success.

---

## 9. Upgrade and day one

### Schema

- **Sentinel:** two EF migrations. `AddPulseReporting` creates
  `SentinelPulseIssueLevel`, `SentinelPulseDeviceState` and
  `SentinelPulseItemState`, and `AddPulseIssueLevelDoNotReport` adds the
  `DoNotReport` column. Empty tables, no backfill. The migration before them
  is `20260625141809_AddDeviceSourceState`.
- **Director:** one EF migration, `AddDirectorUrlReportingDevice`, adding
  nullable `tblDirectorUrl.DeviceInstanceId` with a non-unique index. The
  migration before it is `20260918125913_AddMessaging` (Toronto messaging),
  so `director-upgrade-2026.9.0.sql` carries both.
- Both ship in the next release's customer upgrade scripts in `deploy/`, in
  the existing idempotent pattern (every step guarded on
  `__EFMigrationsHistory`, stamp before running). The Director change goes in
  the next `director-upgrade-<version>.sql`. Sentinel has no customer upgrade
  script yet; this adds the first, `sentinel-upgrade-<version>.sql`, in the same
  pattern.
- Generate migrations with an explicit `--connection`: Sentinel's design-time
  factory hard-codes a localdb string.
- Nothing is written to Pulse during the upgrade.

### Day one, for an operator

The runbook goes in the release notes and in
`src/DXSuite.Host/docs/sentinel-pulse-reporting.md`:

1. Have the customer's DBA apply the grants in section 3 to the login in the
   Pulse connection string.
2. Set `Sentinel:PulseReporting:CustomerId`, the Pulse connection string and
   `Enabled: true` for the tenant, then restart.
3. Open Monitored Devices, tick the devices to report, and use the bulk
   inspector: Link to suggestion or Create, then Apply.
4. Run the retired-sink SELECT below and give the result to a Pulse admin. Do
   not delete anything from Sentinel.
5. Attach URL monitors to their reporting devices from each device's inspector,
   working down the Unattached URL monitors list.
6. Adjust levels and items on Pulse Reporting if the defaults do not suit.

### Devices the retired sink auto-created

`PulseAlertSink` (shipped 2026-08-04) inserted Pulse devices with `Monitor = 1`
that it never heartbeated. Sentinel cannot tell them from real devices sharing
the id, so it leaves them alone and a Pulse admin decides
([DXME-262](https://radianttechnology.atlassian.net/browse/DXME-262)):

```sql
SELECT d.DeviceID, d.Description, d.CreateDate, d.Monitor,
       (SELECT COUNT(*) FROM dbo.tblPulse p
         WHERE p.DeviceID = d.DeviceID AND p.CustomerID = d.CustomerID
           AND p.MonitorItemID = 0) AS DeviceItemReadings
FROM dbo.tblDevice d
WHERE d.CustomerID = @CustomerId
  AND d.Heartbeat IS NULL
  AND d.Monitor = 1
  AND d.MonitorTemplateID = 1
  AND d.CreateDate >= '2026-08-04'
ORDER BY d.CreateDate;
```

### What is removed

- `src/DXSuite.SLC/Services/PulseAlertSink.cs` and its registration
  (`SLCModule.cs:47` to `:51`).
- The `DXSuite:PulseAlerts` section from
  `src/DXSuite.Host/docs/director-url-monitoring.md:96` to `:104`. It appears in
  no appsettings file.
- `SlcPulseDbContext` **stays**: the read-only screenshot views
  (`ScreenshotViewer.razor`, `ScreenshotModal.razor`) use it.

---

## 10. Test strategy

### Unit tests in CI

A new project, `tests/DXSuite.Sentinel.Tests` (xUnit 2.9.3, net10.0, matching
`tests/DXSuite.Director.Tests`), referencing `DXSuite.Sentinel`, added to
`DXSuite.sln` so CI's `dotnet test DXSuite.sln` runs it. There are no Sentinel
tests today.

The subject is a **pure planner**, `PulsePublishPlanner.Plan(...)`: it takes the
device's source states, the Director URL rows, the resolved overrides, the
stored device and item state, the current link and customer, and `nowUtc`, and
returns the intended writes (heartbeat value or suppression reason, readings,
state updates). No database, no clock. The engine and the SQL gateway stay thin
and are not unit tested.

The facts to cover:

- A dead device (every source stale) writes no heartbeat and no Source offline
  issue, even though `LastStateJson` would still say Healthy.
- Fresh evidence older than 5 minutes suppresses the heartbeat with
  `Stale evidence`; the heartbeat value is `max(fresh.LastSeenUtc)`, never
  `nowUtc`, and never later than `nowUtc`.
- Unknown health suppresses the heartbeat.
- Two issues on one item publish once at the worse level; clearing one writes
  the survivor, not a clear.
- A second URL missing on the same device changes the code set and writes; the
  same for a second source going offline.
- A summary-only change writes nothing.
- A level override change, Do not report, and a monitor item move each produce
  the right writes.
- First run writes only items with open issues. Relink discards item state and
  writes nothing to the old device.
- An open item older than `ReassertHours` is re-asserted; a cleared one is not.
- A device marked link broken produces no writes.
- Level and item resolution along the chain, including an unknown monitor point
  falling to its severity default and the qualifier being stripped.

### Rehearsal on DCMDEV07

A manual checklist against DCMDEV07 `Pulse` as customer `RAD`, run once before
merge and recorded on the build ticket. It proves the write contract:

1. A linked, healthy device's `tblDevice.Heartbeat` advances every cycle and it
   stays out of `View_Devices_Down`.
2. Stopping its sources removes it from the heartbeat within 5 minutes, and it
   appears in `View_Devices_Down` within about 8.
3. An induced issue (an agent host disk threshold, or a URL marked missing)
   appears in `View_LastMonitor` at item 0 with the explicit ErrorLevel,
   `LastEditUser = 'Sentinel'` and the summary; clearing it writes `'0'`.
4. A level change on Pulse Reporting writes a new reading on the next cycle.
5. Snooze sets `Snooze = 1` and a UTC `SnoozeTime`; unsnooze clears both.
6. Create in Pulse inserts a row with every NOT NULL column explicit and links
   it.
7. Deleting a linked test device's Pulse row (DCMDEV07 only) shows Link broken
   and stops attempts; Retry after recreating it recovers.
8. The retired-sink SELECT lists `TEST-WALL-01`.

**What it cannot prove:** that Pulse raises its `tblAlert` down alert, because no
Agent jobs run on DCMDEV07. Step 2 proves the view the job reads.

---

## 11. Code changes by file

### `src/DXSuite.Sentinel/Entities/`

- **New** `SentinelPulseIssueLevel.cs`, table and columns in section 5.
- **New** `SentinelPulseDeviceState.cs`, table `SentinelPulseDeviceState`, PK
  `DeviceInstanceID`: `PulseCustomerID` varchar(20), `PulseDeviceID`
  varchar(50), `LastHeartbeatSentUtc` datetime2 null, `LastAttemptUtc`
  datetime2 null, `LastSuccessUtc` datetime2 null, `ConsecutiveFailures` int,
  `SuppressionReason` varchar(30) null, `LastError` nvarchar(500) null,
  `LinkBrokenUtc` datetime2 null.
- **New** `SentinelPulseItemState.cs`, table `SentinelPulseItemState`, PK
  (`DeviceInstanceID`, `MonitorItemID`): `ErrorLevel` tinyint,
  `OpenIssueCodes` varchar(1000), `Summary` varchar(500), `LastPublishedUtc`
  datetime2.
- `SentinelDBContext.cs`, `ISentinelDBContext.cs`: the three DbSets.
- `DeviceInstance.cs`: no change. `PulseDeviceID` is the link;
  `PulseCustomerID` stays unused.

### `src/DXSuite.Sentinel/Migrations/`

- **New** `<timestamp>_AddPulseReporting.cs` and
  `<timestamp>_AddPulseIssueLevelDoNotReport.cs`, with designers, plus the
  model snapshot update.

### `src/DXSuite.Sentinel/Services/`

- `Extensions.cs`:
  - The `SentinelDBContext` factory adopts Director's scope-first tenant
    resolution (section 4, Precondition).
  - Register `PulsePublisherEngine` in the `runBackgroundServices` block.
  - Register `PulseReportingSettingsResolver` (singleton), `PulseGateway`
    (scoped) and `PulseIssueLevelService` (scoped) on all roles, since the tabs
    need them on web.
- `DeviceHealthRollup.cs`: **new** `public static DeviceState BuildState(IReadOnlyCollection<DeviceSourceState> sources, DateTime nowUtc)`,
  and `BuildCanonicalStateJson` becomes a serialization of it. No change to
  `StalenessWindow` or `Roll`.
- **New** `Pulse/PulseReportingSettings.cs`: the options record (section 7) and
  `PulseReportingSettingsResolver.Resolve(string subdomain)`, returning settings
  plus connection string, with the startup validation.
- **New** `Pulse/PulseIssueCatalogue.cs`: the shipped catalogue and defaults
  (section 5).
- **New** `Pulse/OpenIssue.cs`: record (`Code`, `LookupKind`, `MonitorPointId`,
  `Severity`, `Summary`, `TriggeredAtUtc`) and the code format helpers,
  including stripping the qualifier.
- **New** `Pulse/PulseIssueLevelService.cs`: loads overrides, resolves level and
  item along the chain, saves and resets overrides for the tab.
- **New** `Pulse/PulsePublishPlanner.cs`: the pure planner (section 10). All
  decision logic lives here.
- **New** `Pulse/PulseGateway.cs`: the only code that touches a Pulse database.
  Parameterized ADO.NET through `Microsoft.Data.SqlClient` on the resolved
  connection string, no EF model of Pulse's schema. Methods: write heartbeat,
  insert reading (with the 2627 retry), read the customer's devices, device
  types and monitor items, create device, snooze, unsnooze, set monitor. It
  returns typed outcomes for 547 on each foreign key, for a zero-row update and
  for 229 and 230 (`PermissionDenied`).
- **New** `Engine/PulsePublisherEngine.cs`: the `BackgroundService` (section 4).
- **New** `Pulse/PulseLinkService.cs`: link, change link, unlink, retry, create
  in Pulse, bulk apply, suggestions and default DeviceIDs, and attaching URL
  monitors (writes `tblDirectorUrl.DeviceInstanceId` through
  `IDirectorDBContext`). Unlink and change link delete the device's state rows.

### `src/DXSuite.Sentinel/Components/`

- `SentinelDevicesTab.razor`: rework to variant D (section 6): grid with
  checkboxes and sortable headers, status line, Pulse inspector and bulk
  inspector, unattached URL list. Existing Detail, Merge and Split stay
  reachable from the inspector. Pulse parts render only when reporting is
  enabled.
- **New** `PulseInspector.razor` and `PulseBulkInspector.razor`, split out so the
  tab stays readable.
- **New** `PulseReportingTab.razor`: section 5.
- Delete `prototype/pulse-controls.html` if it was merged; it lives only on the
  prototype branch today.

### `src/DXSuite.Sentinel/SentinelModule.cs`

- Add `ConfigTab("sentinel-pulse-reporting", "Pulse Reporting", typeof(Components.PulseReportingTab), 59)`.

### `src/DXSuite.Director/`

- `Entities/TblDirectorUrl.cs`: `public int? DeviceInstanceId { get; set; }`,
  indexed.
- **New** migration `<timestamp>_AddDirectorUrlReportingDevice`.
- No change to `DirectorMonitoringService`, `NotificationAlertSink`,
  `DirectorUrlsTab.razor` or `DirectorAlertKind`. `IDirectorAlertSink` keeps its
  bell consumer.

### `src/DXSuite.SLC/`

- Delete `Services/PulseAlertSink.cs`.
- `SLCModule.cs`: remove the sink registration and its config checks; keep the
  `SlcPulseDbContext` registration.

### `src/DXSuite.Host/`

- `appsettings.json`: add `Sentinel:PulseReporting` with `Enabled: false`.
  WEB02's deployed settings set `Enabled: true`, `CustomerId: "RAD"`.
- `docs/director-url-monitoring.md`: remove the `PulseAlerts` section and point
  at the new doc.
- **New** `docs/sentinel-pulse-reporting.md`: the operator runbook (section 9),
  the grants, and the retired-sink SELECT.

### `deploy/`

- Next `director-upgrade-<version>.sql`: the Director migration.
- **New** `sentinel-upgrade-<version>.sql`: the Sentinel migration, in the
  Director script's guarded pattern.

### `tests/`

- **New** `DXSuite.Sentinel.Tests/` with `PulsePublishPlannerTests.cs` and
  `PulseIssueLevelResolutionTests.cs` (section 10). Add to `DXSuite.sln`.

### Not in this work, and not accidentally

- `DeviceHealthRollup.StalenessWindow` and `Roll` are unchanged.
- `CloudConnectorSyncService` is unchanged, including the hard-coded
  `OverallHealth = Unknown` at `:1074`.
- `DirectorMonitoringService._connectorOnline` stays in memory.
- No change to the `DXSuite.Pulse` module.
- No change to `TenantRecord`, `ITenantContext` or any tenant store.

---

## 12. Out of scope

From the map, restated so a builder does not drift into them:

- Writing `tblAlert`, or anything else another app owns.
- Scheduled or recurring snooze windows.
- Sentinel becoming the authority over Pulse, or replacing it.
- Reading Pulse back into Sentinel's health or alerting. Monitored Devices reads
  `tblDevice` for display and link picking only.
- Fixing the Pulse web UI's Eastern-time `SnoozeTime`.
- Managing Pulse customers, recipients, templates or emails.
- Sentinel taking over URL or Pixera monitoring from Director.
- Making `content-only` connectors monitorable.
- Restoring per-connector `BuildDeviceState`.
- Connector-probe issues as their own Pulse issue type.
- Making device drivers poll.
- **Per-source-type freshness windows.** One global `StalenessWindow` of 10
  minutes means a Source offline reading lands up to 10 minutes after a fast
  source stops. The Pulse down path does not depend on it (section 4). Choosing
  freshness per source is about acquiring health, like decision 15. Ruled out
  by DXME-263.

---

## 13. Known limits and open items

### Known limits, accepted

- **A connector with no synced devices is invisible to Pulse.** A Pixera never
  reachable since it was added has no live systems, so nothing for an outage to
  surface through. Director's bell does not fire either, because `wasOnline` is
  null on first sight. A provisioning failure an installer sees on the connector
  page. The first-sight gap is a Director bug in its own right
  ([DXME-266](https://radianttechnology.atlassian.net/browse/DXME-266)).
- **Source offline is late by up to `StalenessWindow`** (section 12).
- **The Pulse UI shows Sentinel snoozes 4 or 5 hours off** until Pulse's own
  write is fixed (section 3).
- **A live Pulse agent writing the same item on the same device masks
  Sentinel's reading.** Item 0 has no agent writer on SQL02; the tab warns on
  agent items.
- **SQL02 view, procedure and job bodies were never read.** The contract relies
  on DCMDEV07's bodies and SQL02 data patterns (research, Open unknowns). Reading
  them needs a login with VIEW DEFINITION and `msdb` access. Nothing here
  changes if they differ, but the rehearsal's view checks are DCMDEV07's views.

### Open items

None block the build. Ticketing the build as an epic is the next step.

### Facts measured once, not to be re-measured

- `View_Current_Alerts` returns zero rows across live SQL02, and no `tblPulse`
  row there has ever carried ErrorLevel above 0. This publisher is its first
  producer.
- `MonitorItemID` 0 (Device) exists on SQL02 with no readings at all.
- `ValueVarchar` is `varchar(max)`. `tblPulse` has foreign keys to both
  `tblDevice` and `tblMonitoritem`.
- `DeviceHealthRollup.BuildCanonicalStateJson` sets `LastHeartbeat = nowUtc` at
  build time, so that field carries no liveness information.
- SQL02 stores `SnoozeTime` in Eastern local time from its own UI, and its
  unsnooze job compares against UTC.
- DCMDEV07 `Pulse` matches SQL02's tables, keys, indexes and lookups; view and
  procedure bodies are still DCMDEV07's. Backup:
  `D:\SQLBackups\Pulse_pre-DXME-258_2026-09-17.bak`.
