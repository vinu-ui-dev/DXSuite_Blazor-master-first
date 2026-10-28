# Ticket Counter Management Architecture

## Overview

Airport ticket counter management solution enabling airline agents to control signage content and open/closed status at their assigned counters, with server-side integration for external schedule data and graceful outage handling.

The solution consists of two major components:
1. **Counter Agent App** — A Blazor WebAssembly application used by airline agents at ticket counters
2. **Server-Side Schedule Integration** — API-driven scheduling from an external data source (CoreAPI), with feed control and reconciliation

---

## Counter Agent App (Blazor WASM)

### Project

New Blazor WebAssembly project: `DXSuite.CounterApp`. Communicates with Director exclusively via HTTP API (no shared DbContext). Designed for hundreds of concurrent browser instances.

### Authentication

Shared credentials per airline (high-turnover positions make individual password management impractical). Uses the existing ControlClient `AccessCode` authentication model.

### Connection Identity

The app identifies which counter/desk an agent is operating from via a three-tier identity system:

| Priority | Mechanism | Purpose |
|----------|-----------|---------|
| 1 | `?desk=TC-101` URL parameter | Provisioning hint (pre-selects counter on first use) |
| 2 | `localStorage` token | Persistent identity across sessions |
| 3 | Desk picker (after login) | Manual fallback if no param or token |

#### First-Launch Flow (Launch Day)

1. IT distributes shortcuts with `?desk=TC-101` baked into the URL
2. Agent opens shortcut → app loads with desk param
3. No token in `localStorage` → airline login prompt (shared credentials)
4. Login succeeds → app auto-selects TC-101 from the desk param (skips picker)
5. Token + desk assignment written to `localStorage`
6. Subsequent launches (with or without the param) → token found → straight to counter view

#### Manual Navigation Flow (No URL Param)

1. Agent navigates to `https://director.local/counter` directly
2. No `localStorage` token → airline login prompt
3. Login succeeds → desk picker showing all counters assigned to their airline
4. Agent selects their desk → token + assignment written to `localStorage`
5. Subsequent launches → token found → straight to selected counter

#### Session Management

- **Switch Desk**: Clears `localStorage` desk assignment, returns to desk picker (no re-login)
- **Log Out**: Clears both session and `localStorage`, returns to login screen

After the initial launch day, shortcuts become optional — every machine carries its assignment in `localStorage`. If a machine is reimaged or storage is cleared, the agent logs in and picks their desk (~10 seconds, no IT ticket required).

### Agent Capabilities

- **Open/Close** their assigned counter (manual toggle)
- **Change content/status** on their counter's display via StatusSelector workflow
- **View and control** all other counters assigned to their airline
- **Cannot** reassign counters between airlines

### Reconnection Resilience

The WASM app does not need offline storage since the server is on-prem and handles all state. If HTTP calls fail (server restart, network blip), the app should display a connection status indicator and auto-retry with exponential backoff until the server is reachable again.

---

## Display Model

### Open/Closed State

Stored in `TblDisplay.ExtendedData` (JSON) rather than a new column, since this is customer-specific:

```json
{
  "counterState": "open",
  "counterStateChangedAt": "2026-04-09T14:30:00Z",
  "counterStateChangedBy": "AA-agent"
}
```

The StatusSelector service reads `counterState` when resolving content — if `"closed"`, it shows the closed asset regardless of what was last selected via the status buttons.

### Split-Screen Displays (1:2 Counter-to-Display)

Some physical displays serve two ticket counters. These are modeled as **two logical `TblDisplay` entities per physical device**, linked together:

- `SplitPartnerId` (int?, FK) on `TblDisplay` — links two logical displays as partners
- `SplitPosition` (string) — `"left"` or `"right"` (stored in ExtendedData or as a field)

#### Content Rendering Logic

The signage player checks whether both logical displays on a physical screen share the same airline AND the same active content:

| Left Counter | Right Counter | Rendering |
|-------------|--------------|-----------|
| Same airline, same status | Same airline, same status | Full-screen single asset |
| Same airline, different status | — | Left half + right half (per-counter content) |
| Different airlines | — | Left half + right half (per-counter content) |

#### Auto-Split on Upload

When an asset is uploaded at full-screen resolution (e.g., 1920x1080), the system automatically generates cropped left-half and right-half variants:

- Stored as separate assets in the same library
- Tagged with metadata: `splitSource: {originalAssetId}`, `splitSide: "left"` / `"right"`
- The player selects full-screen or half-screen variants based on the rendering logic above

### Open/Closed Content Ownership (Deferred)

Three options are documented for the customer to decide:

1. **Airport decides**: Open/closed assets live in a shared/global asset library. All airlines show the same visuals. Simpler to manage.
2. **Airline decides**: Each airline's asset library contains their own branded open/closed content. The StatusSelector pulls from the airline's library based on the active access schedule constraint. More flexible.
3. **Hybrid**: Airport provides default open/closed assets; airlines can optionally override with branded versions. StatusSelector falls back to defaults if airline hasn't uploaded their own.

The hybrid approach works with existing constraints — the airport's defaults live in a "common" asset group, and the constraint system can include those alongside airline-specific assets.

---

## Schedule Integration (CoreAPI → Director)

### Communication Model

**Push with heartbeat.** CoreAPI pushes schedule updates to Director using a Director-generated API key (`AdminApiKey` auth scheme). Director tracks `lastReceivedAt` to detect data source outages.

No pull mechanism — Director does not need CoreAPI credentials.

### Airline ↔ Group Linking

Airlines are linked to Director user groups via metadata. A `TblUserGroupMetadata` entry with key `airlineCode` and value `"AA"` maps the group to American Airlines. The schedule import service resolves incoming `airlineCode` values through this metadata lookup.

This keeps the mapping soft/configurable — adding a new airline means creating the group and setting the metadata. No code changes.

### Schedule Import API

#### Data Shape (from CoreAPI)

```json
{
  "schedules": [
    {
      "counterIdentifier": "TC-101",
      "airlineCode": "AA",
      "startDate": "2026-04-10",
      "endDate": "2026-04-10",
      "startTime": "06:00",
      "endTime": "14:00",
      "daysOfWeek": ["Mon", "Tue", "Wed", "Thu", "Fri"]
    }
  ]
}
```

#### Import Model: Overwrite

The external system is the source of truth for schedules when the feed is active. Each import replaces the schedule set for the affected counters/date range:

1. Receive batch of schedule records
2. For each counter identifier, resolve to `DisplayId` (via counter mapping in configuration)
3. Delete existing `TblAccessSchedule` records that overlap the incoming date range
4. Insert new records
5. Trigger `AccessCacheRefreshService` force-refresh

This is idempotent — CoreAPI can re-send the same data without side effects. Updates are handled the same way: send the updated schedule block, Director replaces the old overlapping records.

#### Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/scheduling/import` | POST | Bulk schedule import (overwrite model) |
| `/api/scheduling/export` | GET | Current schedule state (for reconciliation) |
| `/api/scheduling/{id}` | DELETE | Individual record removal (edge cases) |

### Schedule Expiry Cleanup

A background service (`ScheduleExpiryCleanupService`) runs on a configurable interval (e.g., hourly):

- Deletes `TblAccessSchedule` records where `EndDate < now - retentionPeriod`
- Retention period is configurable in the settings tab
- Prevents expired schedules from accumulating

### Counter Identifier Mapping

A configuration mapping links external counter identifiers (e.g., `"TC-101"`) to internal `DisplayId` values. This is managed in the configuration tab and used by the import service for resolution.

---

## Feed Control & Reconciliation

### Feed States

The external schedule feed has three operational states, controlled from the configuration tab:

| State | Incoming Pushes | Manual Edits | Behavior |
|-------|----------------|-------------|----------|
| **On** | Applied immediately (overwrite) | Allowed (will be overwritten on next push) | Normal operation |
| **Off** | Queued to `TblScheduleImportQueue` | Authoritative | Manual control mode |
| **Resuming (raw)** | Queue drained, overwrites everything | Discarded | Fast recovery |
| **Resuming (reconcile)** | Queue held for review | Preserved until resolved | Careful recovery |

### Queue Table: `TblScheduleImportQueue`

When the feed is off, incoming pushes from CoreAPI are stored rather than discarded:

- `QueueEntryId` (int, PK)
- `ReceivedAt` (datetime) — when the push arrived
- `Payload` (nvarchar(max)) — raw JSON payload
- `Status` (string) — `"pending"` / `"applied"` / `"discarded"`

CoreAPI receives a `202 Accepted` response when the feed is off, indicating the data was received but not yet applied.

### Reconciliation Flow

When an admin chooses "Resume with reconciliation":

1. Load all `"pending"` entries from `TblScheduleImportQueue`
2. Load current `TblAccessSchedule` records (including any manual changes made during outage)
3. Present a **side-by-side diff modal** showing per-record differences:
   - Left side: "Current in Director" (what's live now, including manual changes)
   - Right side: "Incoming from Data Source" (what CoreAPI sent)
4. Per-record actions:
   - **Keep** — retain the Director version
   - **Take** — accept the incoming version
   - **Edit** — modify the incoming record before accepting (merge)
5. Bulk actions:
   - **Keep All** — retain all Director versions, then review individual records to selectively take
   - **Take All** — accept all incoming versions, then review individual records to selectively keep
6. On confirm:
   - Apply the reconciled schedule state to `TblAccessSchedule`
   - Push the reconciled result back upstream to CoreAPI (via the export endpoint or a callback) so both systems agree
   - Clear the queue (mark entries as `"applied"` or `"discarded"`)
   - Resume normal feed operation

### Heartbeat Monitoring

Director tracks `lastReceivedAt` from CoreAPI. If no push is received within a configurable window (e.g., 10 minutes), the configuration tab displays a warning indicating the data source may be unavailable. The admin can then decide whether to proactively pause the feed.

---

## Configuration Tab

A new tab in Configuration (customer-specific to this deployment):

### Sections

1. **Data Source Settings**
   - Connection status indicator (healthy / stale / offline)
   - Last received timestamp
   - Heartbeat window configuration
   - Feed state toggle (On / Off)
   - Resume mode selection (Raw / Reconcile)

2. **Counter Mapping**
   - Table linking external counter identifiers to internal display entities
   - Supports 1:1 and 1:2 (split-screen) mappings
   - Used by the import service for resolution

3. **Schedule Cleanup**
   - Retention period setting
   - Manual purge button
   - Last cleanup timestamp

4. **Reconciliation Panel** (visible when resuming with reconciliation)
   - Side-by-side diff view
   - Per-record keep/take/edit controls
   - Bulk Keep All / Take All buttons
   - Confirm and resume button

---

## Existing Infrastructure Reuse

| Existing Component | How It's Used |
|-------------------|---------------|
| `StatusSelectorService` | Content switching per display (open/close, status changes) |
| `TblAccessSchedule` | Time-windowed airline-to-counter assignments |
| `AccessCacheRefreshService` | 60s evaluation of active schedule windows |
| `ScheduleEvaluator` | Date/time/day-of-week logic for schedule windows |
| `ControlClientController` | Pairing and token auth pattern for WASM app |
| `TblDisplay.ExtendedData` | Customer-specific counter state (open/closed) |
| `TblUserGroupMetadata` | Airline code → Director group mapping |
| `AdminApiKey` auth scheme | CoreAPI authentication for schedule pushes |
| Asset libraries + constraints | Per-airline content with schedule-based filtering |
| `DisplayScheduleResolver` | Active constraint resolution for content filtering |

## New Components Required

| Component | Type | Purpose |
|-----------|------|---------|
| `DXSuite.CounterApp` | Blazor WASM project | Agent-facing counter control application |
| `/api/scheduling/import` | API endpoint | Bulk schedule import (overwrite model) |
| `/api/scheduling/export` | API endpoint | Schedule state export (for reconciliation) |
| `/api/scheduling/{id}` | API endpoint | Individual schedule record deletion |
| `TblScheduleImportQueue` | Entity | Queue for incoming pushes when feed is off |
| `ScheduleImportService` | Service | Processes incoming schedule data, manages feed state |
| `ScheduleExpiryCleanupService` | Background service | Removes expired schedule records |
| `ScheduleFeedMonitorService` | Background service | Heartbeat tracking, stale data source detection |
| Counter Mapping config | Configuration UI section | Links external counter IDs to display entities |
| Feed Control config | Configuration UI section | Feed state, reconciliation panel |
| Auto-split asset processing | Upload hook / service | Generates half-screen variants from full-screen uploads |
| `SplitPartnerId` on TblDisplay | Schema (or ExtendedData) | Links two logical displays sharing a physical screen |
