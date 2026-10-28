# DXSuite as the Engage for BrightSign hub

The DXSuite half of the handoff spec for the Engage for BrightSign flavour: what
Director has to become so that it is the update, provisioning and enrollment hub
for a BrightSign player running natively, with no signageOS and no BSN.cloud at
runtime.

The player half is
[`docs/spec/engage-for-brightsign.md`](https://github.com/Radiant-Technology-Group/Engage_BrightSign/blob/dev/docs/spec/engage-for-brightsign.md)
in `Engage_BrightSign`. Both were assembled under
[DXME-132](https://radianttechnology.atlassian.net/browse/DXME-132) from the
closed tickets on
[DXME-120](https://radianttechnology.atlassian.net/browse/DXME-120).

**Every section cites the ticket that decided it. The ticket's resolution comment
holds the reasoning; this document holds the decision.** Where a later ticket
overturned an earlier one, only the surviving decision appears here, with the
correction noted so nobody reopens it from the older ticket.

Vocabulary is [`CONTEXT.md`](../../../CONTEXT.md), sections "Engage player
flavours" and "Provisioning, enrollment and updates". Terms in _italics_ on first
use are defined there. The distinctions that trip people up: _flavour_ is which
player build a device is enrolled as and is the app platform row; _app bundle_
and _bootloader_ are two independently versioned halves of one repository,
distinguished on a version row by _artifact kind_; _provisioning_ precedes
_enrollment_; _takeover_ converts a player running someone else's CMS and
_reclaim_ is the separate later deletion of what they left; _enrolled with BSN_
and _released_ are about the incumbent's console and not about our pairing flow.

The standing facts behind the code citations below, with a ticket pointer on
every line, are in
[`Engage_BrightSign/docs/map-facts.md`](https://github.com/Radiant-Technology-Group/Engage_BrightSign/blob/dev/docs/map-facts.md).

**Section 15 lists what is open**, including the three tickets filed while this
document was written because a closed ticket left something ambiguous. Only one
of them touches this half: [DXME-154](https://radianttechnology.atlassian.net/browse/DXME-154),
the time route's response contract.

---

## 1. The shape of the work

Six things change on the server, and one thing deliberately does not.

1. **Flavour identity** becomes correct, at the cost of one server-side property
   and zero applet releases (section 2).
2. **`tblAppPlatform`** is left in the state the new flavour needs (section 3).
3. **The update pipeline** gains a second artifact kind, a second version line per
   device, and authenticated artifact storage (section 4).
4. **Provisioning** is new: a generated card package, a persisted package record,
   and a third settings tab (section 5).
5. **The recovery-URL handler** is new, and is the only thing that can rescue a
   player whose bootloader will not start (section 6).
6. **Takeover** is new as an operator workflow over the remote DWS (section 7).
7. **Sentinel stays as it is**, with one correction to where a credential is read
   from (section 12).

Two constraints shape all of it.

**There is no production fleet on this schema.** No production database runs
either Engage flavour or the legacy `DirectorClient_BrightSign`. The 112
BrightSign players in Pulse Core are not rows in `tblDevice`, so they are
_takeover_ candidates rather than migration candidates. One customer runs an old
pre-Engage BrightSign build; that environment does not use these tables and is a
different application being replaced, not migrated. **Nothing here needs a
customer data migration, and any work reaching for one should stop and re-read
this paragraph.** _DXME-133._

**The first deployment is on-prem Director**, with the players moved onto a
network that can reach it. Proxying, TLS interception and constrained bandwidth
are possible rather than expected. This matters for more than topology: on-prem
is precisely the deployment shape that triggers the tenant-resolution
fallthrough in section 9, so every new route carries the tenant in its path.
_DXME-136 §0._

**The common engagement is a takeover, not a greenfield install.** Radiant
usually inherits an existing BrightSign deployment, strongly prefers no
technician on site, and can usually obtain BrightAuthor:connected access to the
customer's BSN.cloud organisation. Treat "no technician on site" as the default
constraint on anything touching installation.

All new UI is built to the `DXSuite.StyleGuide` `dx-panel` structure, with
`DevicesTab.razor` as the template. `DevicePairing.razor` uses `dx-card` plus a
page-local `<style>` block and raw HTML tables, and is **not** the pattern to
copy.

---

## 2. Flavour identity

_DXME-124._

**Flavour is the app platform row. Engage for BrightSign takes a new one,
`EngageBrightSign`. It is written from the approved pairing request, and the
heartbeat's own claim is validated against it rather than trusted.**

### The finding that reframed the problem

The applet already declares its flavour on every heartbeat and the server throws
it away. `deviceInfo.ts:125` sets `platform: 'SignageOS'` at the top level of the
payload; `PlayerHeartbeatModel` has one platform property, `ClientPlatform`
(line 47), with no `JsonPropertyName` alias, so `platform` does not bind and
System.Text.Json discards it silently. `HeartbeatService.cs:279-281` then guesses
from `Manufacturer`, which the applet sets to `brand || 'SignageOS'`, and on
BrightSign hardware `brand` is `"BrightSign"`, which is the name of the native
platform row.

So the two flavours have been landing on the same row and the signal needed to
separate them has been on the wire the whole time. **Correct identification costs
one server-side property and zero applet releases.**

### Decisions

1. **Flavour is the app platform row.** No new discriminator column. The pipeline
   already keys updates on `tblDevice.PlatformId`, and a parallel field would
   give two identity values that can disagree.
2. **A new row, not row 1.** `PlatformName` and `FolderName` both
   `EngageBrightSign`, and the device declares exactly that string. PascalCase
   follows the seeded rows; the lowercase `avalonia` is a self-registration
   artifact, not a precedent.
3. **Bind `platform` as an alias onto `ClientPlatform`, and delete the
   `Manufacturer` fallback.** The fallback is the defect. Deleting it is safe:
   `PlatformId = null` is already the state of every LG and Samsung applet
   player, and `UpdateService.cs:284` guards ALL-scope resolution behind
   `if (device.PlatformId.HasValue)`, so a null platform means no deployment
   reaches the device. The LG and Samsung fleet has been correctly excluded by
   accident; only BrightSign-applet escaped, through the fallback.
4. **Flavour is written from the approved pairing request.** Add platform to
   `PairingRequestBody`. The device row is still created by the heartbeat, but
   registration copies flavour from the approved request rather than from the
   heartbeat body, so a device cannot choose its own update stream. Note
   `ApprovePairingAsync` writes nothing to `TblDevice` today: it mints a token and
   marks the request approved, so this is new code in that method.
5. **A heartbeat that contradicts the stored flavour is a _flavour mismatch_, not
   a silent no-op.** `PlatformId` must stop being silently write-once
   (`HeartbeatService.cs:277` assigns only when null and never clears).
6. **Auto-registration is refused for this flavour, and platform-row
   auto-creation is deleted outright.** The auto-create block
   (`HeartbeatService.cs:160-183`) is how `avalonia` appeared, and under decision
   1 it would let an unenrolled player invent itself a flavour. The refusal is
   informative: the existing `RequestFullInventory = true` path for unknown
   devices (`SignageController.cs:408-421`) is the hook for a "not enrolled, pair
   me" signal, at the cost of one more field on `PlayerContentResponse`. Legacy
   auto-registration survives for existing platforms.
7. **Screenshots use a device-token-authenticated binary upload.** Not the
   anonymous `sos/{requestId}` routes and not the query-param `apiKey` on
   `Screenshot/Native`. Those exist because the signageOS SDK forced anonymity
   and a native screenshot API could not set headers; neither constraint applies
   to a client we write on Chromium 87 or newer. Route shape is in section 9.
8. **The device list shows real flavour and shows mismatch state.** Decision 5 is
   only worth having if a human can see a mismatch, and this is the only surface
   where that happens. Note for whoever builds it: `DevicesTab.razor:745`
   currently renders the platform column as the literal string `"Engage"` derived
   from `LocationId.HasValue`, and the real platform name is loaded at `:746` and
   never displayed; a second gate at `:243` is dead code (`@if (false)`).
9. **Sentinel driver selection is out of scope.** Flavour does not reach Sentinel
   by any path today. See section 12.

### Flavour mismatch is derived, and null is not a mismatch

_DXME-134 decisions 8 and 16, DXME-133 clarification._

Mismatch is **a comparison of the enrolled flavour against the last declared one,
evaluated on read.** Nothing to clear after a reflavour fixes the cause, nothing
to go stale, and no way for the flag and the facts to disagree.

**`PlatformId = null` must read as "no flavour enrolled", never as a conflicting
value.** LG and Samsung applet players have no enrolled flavour at all, and once
`platform` binds as an alias they all declare `SignageOS` on every heartbeat. If
the mismatch check treats "declares `SignageOS`, enrolled nothing" as a
contradiction, all 233 null rows light up as mismatched on their next heartbeat.

**Mismatch is a fault, never a trigger.** It is surfaced and never acted on. A
device declaring a flavour it was not enrolled with is either mis-provisioned or
not ours, and both want a human.

A silent BSN re-provision is a **distinct fault from flavour mismatch.** It
overwrites the card's autorun and installs the incumbent app, so our app stops
and there is no heartbeat at all. It presents as an offline device,
indistinguishable from unplugged, with no local trace but an `activation_token`.
_DXME-147 decision 4._

### The operator-facing word is Flavour

"Platform" already means three things: the `TblAppPlatform` row this ticket made
the flavour, the `DeviceRow.Platform` field that is actually
`LocationId.HasValue ? "Engage" : null` and does not mean platform at all, and
the applet's wire field. **The schema keeps `PlatformId`; the UI says Flavour;
`DeviceRow.Platform` is renamed.** _DXME-134 decision 13._

---

## 3. Platform table end state

_DXME-133._

| Row | Action | Why |
| --- | --- | --- |
| 1 `BrightSign` | `IsActive = 0`, rename `PlatformName` to `DirectorClientBrightSign`, leave `FolderName` at `BrightSign` | Deactivation removes the row from `platformLookup` and kills the collision magnet. The rename is for legibility, because `BrightSign` beside `EngageBrightSign` is ambiguous to every future reader. `FolderName` is untouched so the existing 1.0.7 and 1.0.8 zip paths stay valid. Not deleted: those two version rows are DXSuite's only record of what the legacy fleet ran |
| 3 `SignageOS` | `IsActive = 0` | Once `platform` binds as an alias, every applet player declares `SignageOS`. An active row 3 does not merely permit the pollution, it schedules it across all 233 null rows |
| 8 `avalonia` | left alone | One device, no versions, harmless, possibly a genuine future flavour |
| new | **seed `EngageBrightSign`** by EF migration | |

**Applet strays go to `PlatformId = null`, not row 3.** Null and row 3 are
equivalent for ALL scope, which is guarded by `HasValue`. They are **not**
equivalent for DEVICE and LOCATION scope, guarded by
`AppVersion.PlatformId == device.PlatformId`: that comparison is never true
against null, but perfectly true when both sides are row 3. **Row 3 would make
applet players targetable.** Null is the only value that structurally cannot be
deployed to.

**The new row is seeded by EF migration, not by hand.** Deleting the auto-create
loop removes the **only** mechanism by which a `tblAppPlatform` row has ever come
into existence: `PlayerUpdates.razor` reads platforms and never creates one, and
there is no admin screen for it. Row 8 `avalonia` is the proof it has fired
exactly once. A hand-written insert repeated per tenant database is precisely the
failure a seed migration prevents.

### Ordering, and it is the reverse of the obvious one

**Code first, data second.** `PlatformId` is write-once, so the strays are
already stranded and deleting the fallback first strands nothing. The constraint
runs the other way: **if the data step runs before the fallback deletion ships,
the next heartbeat re-resolves `Manufacturer = 'BrightSign'` and puts the row
straight back on row 1.** The applet stray beats every thirty seconds.

A sharper dependency inside that: `platformLookup` is built from **active rows
only** (`HeartbeatService.cs:160-162`), and the auto-create loop fires when the
lookup lacks a name. So **deactivating row 3 while the auto-create loop still
exists makes the next applet heartbeat insert a fresh, active `SignageOS` row**,
reintroducing the magnet on a new row id. **The auto-create loop deletion must
ship before or with the row 3 deactivation, never after.**

### What the applet actually sends, and what it does to the ordering above

**The applet sends no `clientPlatform` field at all.** `Engage_SignageOS`
`src/deviceInfo.ts:94-102` builds its device info with `manufacturer: brand ||
'SignageOS'` and no platform member, and on a BrightSign player signageOS's
`brand` is the string `BrightSign`. **That is the whole mechanism by which the
dev HD1025 reached row 1**: not a declared platform, but the Manufacturer
fallback arm.

Two consequences for the ordering rule above, both measured against the dev
database on 2026-09-13:

- **The auto-create loop cannot fire for any applet player**, because it filters
  on a non-empty `ClientPlatform`. Row 8 `avalonia` came from the Engage player,
  which does declare one. So "deactivating row 3 while the auto-create loop still
  exists mints a fresh active `SignageOS` row" is a hazard only where something
  declares that name, and on dev nothing does.
- **Deactivating row 1 disarms the Manufacturer fallback by itself**, since
  `platformLookup` is built from active rows and the fallback then simply misses.

So the data step is safe ahead of the code deletions **provided the rows are
deactivated before any device is nulled**, which is the order it was applied in.
The code-first rule still stands as written for any database where an applet
player declares a platform name; it is the general case, and this is the dev one.

### Three deletions in `HeartbeatService.cs`

- the update-path `Manufacturer` fallback arm (`:277-281`)
- the separate `??` fallback on the auto-create path (`:303`)
- the platform-row auto-creation loop (`:164-183`)

### What does not happen

**No deduplication tooling, and no handoff.** Duplicate rows arise because
`DeviceId` is client-chosen and the legacy client chose the hostname, so a rename
spawned a second row. Both 1.0.1 and 1.0.8 used the identical
`deviceId: hostStatus.hostName`, verified by diffing the shipped zips: the scheme
never changed, the player's hostname did. That hazard retires with the legacy
client rather than needing tooling, and the `cloud-{connectorId}-` mirror rows
are a deliberate second identity rather than duplicates to merge.

**No customer-facing runbook step and no upgrade script**, because no production
database is affected. Dev data work only: null the dev HD1025's `PlatformId`,
deactivate and rename row 1, deactivate row 3, delete the two XT1145 ghost rows.

### Count it by `PlatformId`, never by `Manufacturer`

Dev has eleven devices with `Manufacturer = 'BrightSign'` and three on row 1.
Eight are `cloud-3-*` rows created in one batch by
`CloudConnectorSyncService.cs:444`; they carry BrightSign manufacturer and model,
never heartbeat, and are created with `PlatformId` null. **There are four
`TblDevice` creation paths, not two**: heartbeat auto-registration plus three
cloud-connector syncs. Any cleanup written against `Manufacturer` sweeps in
mirror rows of the applet fleet.

---

## 4. Update pipeline

_DXME-129, amended by DXME-131 §6 and §1._

**The bootloader is a second artifact kind inside the one platform row, not a
platform of its own, and the two artifacts are never offered in the same
response.** Everything else follows from those two sentences.

### 4.1 Where the bootloader lives

A new **`ArtifactKind`** column on `tblAppVersion`, `App` or `Bootloader`, under
the single `EngageBrightSign` row.

**A bootloader platform row is not merely awkward, it is structurally
impossible.** `TblDevice.PlatformId` is one nullable int (`TblDevice.cs:90`), and
all three scope queries in `GetTargetVersionForDeviceAsync` compare
`AppVersion.PlatformId == device.PlatformId` (`UpdateService.cs:257`, `:272`,
`:288`). A device enrolled as `EngageBrightSign` could never match a bootloader
platform row in any of them. Taking that route means forking the resolver anyway,
which is the artifact-kind model with a junk row attached.

**Keeping the bootloader outside these tables entirely was rejected**, even
though section 6 gives it two pins that are not deployments. It cannot express
"roll the bootloader to these 40 players, ten at a time", and the bootloader is
the artifact whose update costs a 46 second reboot, so batching matters more
there than for app bundles, not less.

`PlayerUpdates.razor` stays one screen with a kind filter rather than splitting.

### 4.2 The second version line reaches the device row

- **`TblDevice.ClientBootloaderVersion varchar(20)`**, refreshed on every
  heartbeat alongside its seven neighbours at `HeartbeatService.cs:190-199`.
- **`TblDevice.TargetBootloaderVersionId int?`** beside `TargetAppVersionId`,
  same shape and nullability.

_This is a stated correction to DXME-127, which put the bootloader version in
`DeviceExtendedData` to avoid a migration "while the shape settles". Three
consumers now **query** it: section 6's conversion check, section 4.3's
`MinBootloaderVersion` gate, and "which two versions is this device on" on the
Player Updates screen. It is a version string of exactly the shape of the column
beside it, and the moment a resolver compares against it a JSON blob is the wrong
home. DXME-127's argument still holds for `resources` and the CEC mode; this
field is not telemetry._

### 4.3 The artifact and its metadata

**`manifest.json` inside the zip is the version of record.** Upload reads it and
the operator no longer types a version.

**A typed version is deleted, not validated.** A typed value that disagrees with
the manifest produces a device reporting 2.0.1 forever while the pipeline
believes 2.0.2, and no comparison in `UpdateService` would ever converge. There
is one authoritative source and it is inside the artifact. Upload **refuses a zip
with no manifest**, and **refuses a duplicate
`(PlatformId, ArtifactKind, VersionNumber)`**.

New metadata on `tblAppVersion`:

- `ArtifactKind` on every row.
- `MinBootloaderVersion varchar(20)` on app-kind rows only, **as a string, not a
  foreign key**, because the bootloader that satisfies it need not be a row in
  this tenant's table.
- `MinPreviousVersion` already exists, is unused, and is left alone rather than
  repurposed.

**The gate is enforced on both sides and the server is the decision point.** The
server does not offer an app bundle whose `MinBootloaderVersion` exceeds the
device's `ClientBootloaderVersion`, and offers the satisfying bootloader update
instead. The device refuses as a last line, because a card can be moved between
players and arrive under an older bootloader than the one it left.

_Build note: the device side had no data until DXME-129's player half was built._
The floor lived only on `TblAppVersion` and was read only by the server's own
resolver, so "the device refuses as a last line" was unimplementable. It now
rides `UpdateInfo` and `UpdateCheckResult`, populated for app-kind offers only.
The device mirrors `VersionOrdering.Satisfies`, including its asymmetry: an
unreadable candidate satisfies nothing.

**A blocked device is a visible state, not silence.** Today an unresolvable
target yields `UpdateAvailable = false`, which `GetDeviceUpdateStatusesAsync`
renders as "up to date". A device held behind a bootloader requirement is not up
to date, and reporting that it is would hide a stuck fleet. **New `BLOCKED`
status with the reason.** Where **no** bootloader version in the tenant satisfies
the requirement, surface it loudly: someone uploaded an app bundle nothing here
can run.

### 4.4 Two artifacts on the wire

`PlayerContentResponse` gains **`BootloaderUpdate` beside `Update`**, both
`UpdateInfo`. **Not an array.** An array breaks the applet flavour's parser for
zero gain, and it invites "loop over the updates", which is precisely wrong: an
app update is a sub-second slot swap and a bootloader update is an autorun
replacement plus a 46 second reboot, and the player branches on them regardless.

**The server never populates both in one response.** Bootloader first, then
silence until the device reports the new `ClientBootloaderVersion`, then the app
bundle. **Sequencing lives on the server next to `MinBootloaderVersion`, not in
the player**, so a bootloader requirement and its satisfying artifact are
reasoned about in one place.

- **`RequiresReboot` stops being hardcoded `true`** (`UpdateService.cs:231`) and
  follows the artifact kind.
- **`DownloadUrl` is absolute and follows the artifact kind.** An app bundle gets
  the authenticated `api/Update/versions/{id}/artifact`; a bootloader gets the
  anonymous `api/Recovery/bootloader/{tenantToken}/{id}`, the same URL the
  recovery courier is handed. _Built as one URL per kind in `Offer()`, which
  originally emitted the authenticated route for both. The tenant token is read
  off the request rather than injected, because `UpdateService` is a singleton
  and `ITenantContext` is scoped; with no request in flight a bootloader offer
  falls back to the authenticated route rather than emitting a URL with no tenant
  in it._ Built from one shared resolver rather than
  inline. `ResolveBaseUrl()` is already what `CheckForUpdateAsync` uses, and five
  call sites rebuild this from `Request.Scheme/Host`. Consolidate them. Note the
  `DownloadUrl` is baked into a per-device cache entry
  (`PlayerPollCache.cs:73-103`), so the absolute URL a player receives reflects
  whichever request populated that entry: harmless on a single-host deployment,
  worth knowing on a multi-hostname one.
- **`FileHash` carries a SHA-256 for both kinds.** `TblAppVersion.ZipFileHash`
  and `PlayerContentResponse.FileHash` already do this for app bundles; the
  bootloader artifact kind needs the same. The digest rides the payload and not
  the artifact, because `manifest.json` is an identity claim and a digest inside
  an artifact cannot attest to that artifact.

### 4.5 Artifacts leave `wwwroot`

_DXME-131 §6, changing DXME-129 §5.2._

**Both artifact kinds move to a tenant-keyed root outside the web root. Nothing
is served by static-file middleware.**

- **App bundles** stream from a new
  `GET api/Update/versions/{id}/artifact` under `AuthSchemes.DeviceToken`.
- **The bootloader** streams from an **anonymous** action carrying the tenant in
  the path.

**The bootloader's anonymity is deliberate and load-bearing.** The recovery
courier reaches a player with a broken bootloader, no running app and therefore
no device token. "Everything behind the device token" and "the courier can fetch
the bootloader" cannot both hold. **One URL, not two**: a normal bootloader
update has a live token and could use the authenticated route, but two paths to
the same bytes is a second thing to keep correct on the one path that has to work
when everything else is broken.

What makes it safe is an invariant rather than a property: **the bootloader
artifact is tenant-neutral by construction.** No endpoint, no credentials, no
CA, no proxy, no tenant identifier in the bytes; it reads all of those from the
registry at boot. That is precisely the thing a future change would quietly
break. A modest rate limiter with `Retry-After` keeps it from serving as a
bandwidth amplifier.

**Build note: the route landed, the storage root has not.** `GET
api/Update/versions/{id}/artifact` exists under `AuthSchemes.DeviceToken` and is
what `DownloadUrl` now points at, so no player is handed a static path any more.
The files still sit under `wwwroot/PlayerContent`, so **the anonymous static path
is still reachable by anyone who guesses it**, and the cross-tenant overwrite
below is still live. Both close with the artifact store over a tenant-keyed root,
which is its own section 14 item. Bootloaders nest under
`{FolderName}/bootloader/{version}` because an app bundle and a bootloader can
share a version number under the one platform row; app paths are unchanged, so no
existing row needs rewriting. **The bootloader's anonymous route has since
landed** as `RecoveryController.GetBootloader`, which refuses anything that is
not a Bootloader-kind row, and a bootloader update offer now points at it (4.4).

**Moving off `wwwroot` kills a live cross-tenant defect** that predates this
flavour. `UpdateController.cs:206-214` builds the path as
`{env.WebRootPath}/PlayerContent/{platform.FolderName}/{VersionNumber}`, which is
global, while the duplicate-version check runs against the tenant's own Director
database. **On a hosted Director, tenant B uploading a version number tenant A
already has silently overwrites tenant A's zip**, after which tenant A's
`ZipFileHash` no longer matches the bytes on disk. Adding `/PlayerContent` to
`TenantContentPrefixes` would also have fixed it, but leaving an anonymous static
path alive beside an authenticated one is the thing worth removing.

### 4.6 The legacy `app.js` rewriting is deleted

Both routes go: `GET api/Update/versions/{id}/download`'s regex rewriting of
`const Endpoint` and `const ApiKey` (`UpdateController.cs:315-401`), and
`GET api/Update/versions/{id}/config`, which reads them back to prefill the UI
(`:405-465`).

Three reasons stack and any one would be enough. Section 3 deactivated row 1 and
established there is no production fleet on it, so the workflow has no live
consumer. Section 5 replaces hand-imaging with the provisioning package, which
puts the endpoint in `config.json` at the card root rather than inside a bundle.
And with pairing-only enrollment nothing in a bundle is per-tenant, so there is
nothing left to rewrite.

**These were never a device path.** `/download` builds a customised zip for an
operator to hand-image onto a card; `/config` is UI prefill. Both are
`AdminApiKey` routes. Reading the ticket it is easy to assume the rewriting sat on
the update path; it did not.

**Deletion disposes of a live defect rather than patching it.**
`PlayerUpdates.razor:766` calls `/config` with a bare `new HttpClient()` and no
`X-API-KEY`, and `:802` opens `/download` in a browser tab. Both should 401, and
the `/config` failure is swallowed at `:781`, so the operator silently sees empty
Server URL and API Key defaults rather than an error. The fix is deletion, not an
added header.

**It also removes a credential leak**: `GetVersionConfig`
(`UpdateController.cs:445-453`) reads `app.js` out of the zip and returns the
embedded `serverUrl` and `apiKey` in plaintext, so one admin key reads another.

What deletion costs: the ability to hand-build a legacy zip from the UI. A person
with a card reader and a text editor can still do it.

### 4.7 Status reporting

**`UPDATE_ROLLED_BACK` is a fifth status, reported explicitly by the surviving
bundle, never inferred.** The new bundle cannot report its own rollback: rollback
takes about six minutes across four unhealthy boots, during which the new bundle
never becomes healthy enough to say anything. Inference from "a heartbeat
reporting the old version after an `UPDATE_STARTED`" was rejected, because the
channel is silent for six minutes and the inference cannot distinguish a rollback
from a power cut followed by a manual downgrade.

**Every update report is stamped with generation and slot** alongside the
`AppVersionId`, `PreviousVersion` and `Version` that `UpdateStatusReport` already
carries. **A single app update is reported by two different bundles.** The running
app owns `UPDATE_STARTED` and `DOWNLOAD_COMPLETE`; the swap takes under a second,
so the bundle posting `UPDATE_COMPLETE` is the **new** one with no memory of
having started anything, and on a rollback it is the **old** one, back again.

Without the stamp the failure is concrete rather than theoretical: a
`LastUpdateStatus` of `UPDATE_COMPLETE` sitting next to a `ClientAppVersion`
holding the old version, with nothing to say whether that is a lagging report or
a rollback in flight. The bench caught exactly this shape on heartbeats, where a
released widget credited a heartbeat to the bundle replacing it.

Reporting only the bootloader-adjacent stages was rejected: it throws away the
only signal separating a download failure from an install failure, which are
different operator actions.

### 4.8 Batching

**No new mechanism and no new columns.** `EnableBatching`, `BatchSize`,
`BatchStatus`, `CurrentBatchNumber`, `TotalBatches` and the
`PENDING`/`TRIGGERED`/`UPDATING`/`COMPLETED`/`FAILED` progress vocabulary all
carry forward untouched. Batching is also **the only bandwidth lever this map
ships**, alongside `Retry-After` on the recovery handler.

- **`UPDATE_ROLLED_BACK` maps onto `FAILED`** in `tblUpdateDeploymentProgress`,
  with `ErrorMessage` recording the version it fell back to. The existing
  partial-failure auto-pause at `BatchDeploymentService.cs:584` then fires
  unchanged, and whole-batch failure still marks the deployment `FAILED` at
  `:574`. **A rollback is the system working, and also the strongest possible
  evidence the bundle is bad.** The point of batching is to stop before batch
  two, and a self-healed batch one is exactly when to stop.
- **One new rule, for bootloader-kind deployments only: the batch advances on the
  device reporting healthy, not on `UPDATE_COMPLETE`.** Health is three
  heartbeats plus 45 seconds of uptime, and a bootloader that installs and then
  fails to boot is precisely the case batching exists to contain.

  **How the device says it, settled at build time**: a `HEALTHY` status on the
  existing `POST api/Update/status`, alongside the four statuses already there
  and `UPDATE_ROLLED_BACK`. A status value on a channel that exists is neither a
  new mechanism nor a new column, which is what this section rules out. The
  server does not compute health, because it cannot: `TblDevice.Heartbeat` is one
  overwritten timestamp with no history, so counting three of anything would need
  the column this section forbids. The player already reaches HEALTHY locally to
  clear `boot_count`, so it is the side that knows.

  **A bootloader row parks in `UPDATING` between the install and the health
  report**, which is what the existing vocabulary already means. The completion
  check treats a parked row as pending, so the batch simply does not advance.

- **Nothing called `HandleDeviceVersionUpdateAsync`.** A fifth defect, found
  while building this: the method existed and no caller anywhere invoked it, so a
  batched deployment could only ever advance through its failure path, which one
  admin route drove. A batch that started could not finish on success. It is now
  driven from `UpdateController.ReportUpdateStatus`, where the device already
  reports, which is also where the rolled-back and healthy statuses arrive.

### 4.9 The known-good bootloader pin

**A `tblSystemSetting` row holding the blessed `AppVersionId`.** The Director
database is per-tenant, so a tenant-level setting is literally a row in this
database, and `tblSystemSetting` (`SettingKey` / `SettingValue` /
`LastEditDate` / `LastEditUser`) exists for exactly this.

**It must stay distinct from the rollout target**, or a rollout candidate becomes
the rescue image. Note there are now **two unrelated pins** in the model: this
one answers "what rescues a broken player"; the version pin on a _provisioning
authorisation_ (section 6) answers "what does this conversion install".

An `IsKnownGood` flag on `tblAppVersion` was rejected as a single-valued choice
modelled as a boolean kept unique by convention. Section 3 spent an entire ticket
on what happens when a table's invariants live in convention rather than
structure.

**Player Updates renders the badge by reading the setting**, which also makes
"nothing is blessed yet" a visible state rather than an empty query.

**Deletion guards extend to it.** `DeleteVersionAsync` refuses to delete a
version that is deployed, pinned as a device target, or running somewhere
(`UpdateService.cs:154-170`). Two corrections: the running-device check compares
`ClientAppVersion`, which is wrong for bootloader-kind rows and must compare
`ClientBootloaderVersion`; and **the blessed version must not be deletable at
all.**

### 4.10 Four pipeline defects fixed alongside

All four predate this flavour. All four were found by tickets on this map and
routed here.

1. **A pinned target version is platform-blind.**
   `GetTargetVersionForDeviceAsync` returns `device.TargetAppVersion` at
   `UpdateService.cs:249` before any platform comparison, so an admin can pin any
   device to any version of any flavour. **Fixed at the point the pin is set, not
   the point it is read**, because the pin is a deliberate operator override and
   silently ignoring it at read time is worse than refusing it at write time. The
   setter validates both `PlatformId` and `ArtifactKind`, and the picker at
   `PlayerUpdates.razor:220` filters accordingly. The comparison is **also**
   added at `:249`, but as a guard that **logs and surfaces**, never a silent
   skip, so a pin made before this ships appears rather than vanishing.
   _DXME-133, DXME-134._
2. **The resolver never checks whether a platform is active.**
   `GetAllPlatformsAsync` and the `HeartbeatService` lookup filter on `IsActive`;
   none of the three scope queries do, so a deployment under a deactivated row
   keeps resolving and keeps shipping. **`IsActive` joins all three.** This is
   what makes deactivation a safe idiom, and section 3 leaned on it when it
   deactivated rows 1 and 3 instead of deleting them, so it is a stated
   dependency rather than a tidy-up. _DXME-133._
3. **`Manufacturer` and `Model` never refresh.** Written only in the auto-create
   branch at `HeartbeatService.cs:294`, never in the per-beat block at
   `:190-199`, so a row born from a slim first beat or a connector sync carries a
   wrong model permanently. **Both refresh every beat**, like their seven
   neighbours. Three lines, and it removes a permanently-wrong-field hazard on
   the two columns designated as `tblDeviceModelConfig`'s future home. The three
   cloud-connector creation paths write these from cloud inventory and never
   heartbeat, so they are unaffected either way. _DXME-137._
4. **`DevicePairingService._pendingTokenDelivery`** is a `static
   ConcurrentDictionary` (`:22`) holding minted plaintext device tokens in
   process, written at `:171` and read at `:117`. It is not tenant-keyed, does not
   survive a restart and does not exist on a second replica. **Persisted on the
   pairing request row instead**, cleared on delivery or expiry, with at-rest
   protection per section 9. Fine today for one operator approving one player; a
   hundred-player takeover mints a hundred of these with nobody on site to retry,
   and an app-pool recycle mid-takeover currently strands every uncollected one
   silently, with re-arming a hundred serials by hand as the recovery. A
   distributed cache was rejected as an infrastructure dependency DXSuite has
   nowhere else, to solve a problem the row it already writes can solve.
   _DXME-139 §11._

### 4.11 Two telemetry members, nothing to decide

Recorded so they reach a builder. _DXME-127 items 2 and 3._

- **`DeviceExtendedData` gains a `resources` member** for memory, CPU,
  temperature, network interfaces and the engine facts (`widget_type`,
  `navigator.userAgent`, `process.version`). Needed because `propertiesJson` is
  accepted and then parsed for `browserUserAgent` only
  (`HeartbeatService.cs:249-274`), so months of applet telemetry has been
  discarded. This is the one stream that would have caught the August 2026
  failure on this hardware, where an HD1025 died of renderer memory growth over
  eight hours with the fleet view showing nothing wrong.
- **A CEC polling-mode field**, so a player that has silently fallen back to
  passive detection via the legacy self-disabling heuristic is visible. Note the
  existing HDMI block is gated on `HdmiConnected.HasValue` at `:202`, so a
  partial display update is discarded whole.

**Report raw resource numbers, never a percentage of budget.** Series 4 has fixed
pre-allocated graphics and JS splits, Series 5 has none, and the LS424 has no
published budget row at all, so any budget-relative figure would be meaningless
for 8 of the 19 Series 4 players and all 93 Series 5 players. _DXME-137 finding
A._

---

## 5. Provisioning package and the operator surface

_DXME-130, DXME-121._

**Two workflows, not one**, sharing one artifact and almost nothing else:
greenfield provisioning from a card, and remote fleet takeover (section 7).

Also worth stating because the original framing inherited the wrong premise:
**there is no LG-style "type an application URL on the player" field on BOS 9.**
On-player setup was cut to Wi-Fi only from 9.0.218 and to activation code plus
Wi-Fi from 9.1.92. **"One-touch" means one physical touch on site, not one field
typed.** For a takeover it means zero.

### 5.1 Routes

**R1 is the only supported provisioning route**: a DXSuite-generated card applied
before first power-on.

- **R2** (DHCP Option 43 pointing at the recovery-URL handler) is **documented as
  a site option** riding on an endpoint we build anyway, not a supported path
  with its own runbook. Note it loses zero-touch under section 6's arming rule,
  which was already declined here.
- **R3, R4 and R5 are out of scope.** R3 (well-known LAN host or mDNS) rests on a
  host spelling research could not confirm. R4 (a BrightSign Control "CMS URL"
  setup saved to a card) and R5 (B-Deploy by serial) put a BSN.cloud account on
  the critical path of every greenfield install while gaining nothing the card
  does not already do. Not to be confused with the **takeover** routes, which use
  BSN.cloud deliberately and are in scope.
- **Pre-imaged SD is R1 performed in the office**, not a fifth route. Radiant
  writes cards from the same package record and ships them. Two consequences: the
  bootstrap DWS password travels in the post, which promotes rotation from
  nice-to-have to load-bearing; and the endpoint must be known before the site is
  stood up, which is fine for a takeover and awkward for a greenfield site whose
  DXSuite instance does not exist yet.

### 5.2 What the package is

**A single `autorun.zip` containing `autozip.brs`.** One artifact, one SHA-256,
one thing a technician can mis-copy instead of five, and the form BrightSign's
own URL channels expect. Inside: `autozip.brs`, the bootloader `autorun.brs`, and
the first app bundle laid down in slot A.

**It binds to a tenant and a hardware family, never to a location or a device.**
With no enrollment secret, what actually varies is the endpoint URL, the network
config, the model family and the firmware pin. **This settles how a tenant's site
URL enters a package: `TblTenant.SiteUrl` _is_ the endpoint in `config.json`**,
nothing more. Note package generation has no HTTP request to lean on, which is
the other reason section 4.4's shared callback-URL resolver has to exist.

**One package is one family; the operator picks.** A package record has one
fingerprint, one SHA-256 and one bootstrap password, and a package "set" quietly
breaks all three.

**Every package carries a `.bsfw`** in its `_newer` form, family chosen by the
operator. A BOS 8 player cannot run the widget and therefore cannot self-upgrade
after enrollment, so the card is the only way in. On a fresh 9.1 player the file
is an inert no-op. 9.1.151 is a single unified firmware across Series 3 through
6, so there is no per-series version branching, but **the `.bsfw` file is still
per-family** (`hd4` for LS424 and HD1024, `xt4` for XT1144, `xt5` for HD1025 and
XT1145), so a card is still family-specific. `GET /api/v1/info` to `family` is
the image selector. A wrong image is safe: the player renames it
`*_incompatible` and carries on.

**Unguardable hazard, recorded rather than solved.** It is unconfirmed whether a
root `.bsfw` is applied **before** the card's `autorun.brs` ever runs. If it is, a
wrong-family card cannot be caught in software at all. Mitigation is operational:
family in the package filename, on the card label, and on the player's unpaired
status overlay.

**The download is a wrapper zip copied to the card root verbatim**: `autorun.zip`
alongside `<family>-<version>-update_newer.bsfw`. The OS scans the card root for
a `.bsfw` and cannot see inside a zip, so the firmware cannot live in it. The
technician instruction stays one line: unzip to the root of a blank FAT32 or
exFAT card.

**The firmware pin is a DXSuite-wide default per family, overridable per
package**, with the override recorded on the record. The `.bsfw` is **uploaded**
into a small firmware library keyed by family plus version, not mirrored from
BrightSign: an on-prem DXSuite may have no route to `firmware.bsn.cloud`, and a
provisioning feature that silently depends on outbound internet repeats the
mistake the original network premise kept punishing.

### 5.3 Package fields

| Field | Notes |
| --- | --- |
| Endpoint URL | `TblTenant.SiteUrl` |
| Hardware family | one per package |
| Firmware pin | default per family, overridable, recorded |
| Time source | a DXSuite route at the player-API origin, with a site NTP override. See section 11 and [DXME-154](https://radianttechnology.atlassian.net/browse/DXME-154) |
| Custom root CA | optional. Installed by the bootloader per boot |
| Explicit proxy | optional, a literal `host:port`. **PAC files refused.** Installed by the bootloader at provisioning |
| Wi-Fi SSID and PSK | optional, encrypted at rest, blank for wired sites |
| Local DWS position | on always / on at park / off entirely. See section 9 |
| Bootstrap DWS password | generated, shown once, stored on the record |
| Web inspector | **off in every shipped package.** See section 9 |
| Fingerprint | hash of the config payload only, never the whole zip. **DXSuite computes it and writes it into `config.json` as a literal string**; the player only string-compares. _DXME-153._ |

**The package carries no time zone, deliberately.** This table listed one until
[DXME-195](https://radianttechnology.atlassian.net/browse/DXME-195) removed it.
The card binds to tenant plus hardware family and never to a device, so a
package-level zone has the wrong granularity and is correct only for a
single-zone tenant, which is the assumption the DXME-159 map exists to stop
making. A device's zone reaches it on the player content response as
`osTimeZone`, per device and gated on an operator having set it. Keeping both
carriers would let them disagree per device. _DXME-157, DXME-192 decision 31,
DXME-195 decision 32._

**"Network profile" does not become an entity.** No such concept exists in
DXSuite today and the profile is mostly empty. Fields live inline on the package
record, with **clone-from-existing** as the reuse mechanism. The trigger that
would promote it later: the first customer whose sites need genuinely different
profiles edited centrally and re-applied.

**Wi-Fi credentials are optional and the exposure is inherited rather than new.**
Where a site is wireless-only the player cannot reach DXSuite until Wi-Fi is
configured, so deferring is not available; every BrightAuthor card setup has
always carried the PSK the same way. A wired site with DHCP generates a package
containing no secret at all, and wired is the expected case. **The runbook treats
a provisioned card as a credential-bearing item**, and a lost card as requiring
PSK rotation.

**DXME-153 withdrew the "reformat or destroy the card after enrollment" half of
that, which was unexecutable**: the player boots from that card and the takeover
section forbids reformatting it. **The bootloader instead replaces `config.json` with a
fingerprint-only stub once it has applied the config**, so the PSK stops living
on the card. The residual is the package zip DXSuite holds and the window between
writing the card and its first boot.

**Provisioning must set the web inspector registry key deliberately or no player
in the fleet is debuggable.** The Chromium inspector needs registry
`html`/`enable_web_inspector = 1` **in addition** to the widget's
`inspector_server` port, mandatory since BOS 8.5.31. The legacy autorun sets only
the port. The dev HD1025 was debuggable in August 2026 only because that key had
been set by hand.

### 5.4 The DWS password is two-stage

The package carries a generated **bootstrap password**, shared by every player
made from that package, shown once to the operator and stored on the record.
**After enrollment the app rotates to a per-device secret** and reports it to
DXSuite against the device row, so `BrightSignDwsDriver` can fetch it and a
leaked card does not open the fleet.

**Never port 80 with a blank password.** The legacy autorun did exactly that. A
bootstrap password is scoped per package and never blank, and re-issuing a
package mints a new one, bounding a leak to a package rather than a tenant.

**A device that has enrolled but not rotated is a visible device state**, the
same shape as flavour mismatch, rather than a silent condition nobody can query.

**A config re-apply clobbers the rotation and the app repairs it.** DWS policy is
inside the gated config payload, so re-provisioning a player writes the bootstrap
password back while the device row still holds the rotated one, and
`BrightSignDwsDriver` would silently stop reaching that player. The re-apply
clears the rotation marker and the app rotates and re-reports on its next start,
so the device passes back through the "enrolled but not rotated" state above
rather than getting stuck. Excluding the DWS password from the applied set was
rejected: a re-provisioning card could then no longer fix a player whose password
is lost, which is one of the few reasons to re-provision. _DXME-153._

Two things a builder must know, because the obvious verification confirms a lie:

- **Writing the `networking/nlwsu` and `nlwsp` registry keys does nothing.** They
  accept a write, read back the value, and survive both a reboot and a firmware
  upgrade while the DWS goes on serving every route unauthenticated. The live
  store is `PUT /api/v1/control/dws-password` with
  `{previous_password, password}`, which takes effect with no reboot, and the
  matching `GET` reports the truth as `password.isBlank`. **Any provisioning
  package or rotation that sets a DWS password must verify through
  `control/dws-password`.** _DXME-142._
- **Whether `@brightsign/dwsconfiguration` writes the live store or the decoy
  registry keys is not recorded anywhere.** The rotation mechanism names that
  module. Verify it before building against it.

### 5.5 Enrollment

**Pairing only. No tenant API key baked into a provisioning package.** A stolen
package cannot receive content: it carries no token, tokens are minted only in
`ApprovePairingAsync`, and `POST api/Signage/PlayerContent` requires the
`DeviceToken` scheme. A denied pairing blocks the device via the blocklist.

**That is priced rather than overturned for takeover.** Pairing needs a human to
read a six-digit code, and a no-technician takeover has no such human; at 100
players enrollment becomes the only step in the operation that does not divide.
**So DXSuite mints a short-lived, single-use, per-serial _pairing grant_** at the
moment the operator triggers that player's conversion, carried by the bootstrap
the recovery-URL handler serves. It is a different object from a baked tenant key
(per-serial, single-use, minutes-long) and it fails safe in every direction a
leaked key would not. Reading the code off a remote screen snapshot stays
available as the manual fallback.

**DXSuite records which package provisioned each device.** The app reports the
package fingerprint on its first heartbeat and it is stored in the
`DeviceExtendedData` telemetry member, no schema change. Without it, "superseded"
and "revoked" are labels with no teeth, and nobody can answer "which players in
the field came off the leaked card".

### 5.6 The operator surface

**A third sibling settings tab, `playerprovisioning`, at order 57**, between
Device Pairing (55) and Player Updates (60), gated on the same server-wide
`Engage` flag and registered in `DirectorModule.cs` alongside its siblings. The
tab order then reads as the real lifecycle: provision, pair, update.

**A generated package is a persisted record, not a fire-and-forget download**:
tenant, family, firmware pin, network config, bootstrap password, fingerprint,
created-by, created-date, with the zip on disk plus size and SHA-256, following
the existing update-pipeline shape. A technician calling six weeks later with a
corrupt card needs **that** package reissued, not a new one with a new DWS
password. Download uses the house pattern (token stash, controller GET,
`dxDownloadUrl`), not `window.open`.

**Regeneration creates a new record; the old one is marked superseded but stays
downloadable until explicitly revoked**, because cards in the field still
reference it. Revocation is the escape hatch when a package leaks.

The tab also hosts the _provisioning authorisation_ list (section 6), the
takeover operator flow (section 7) and the off-origin host report (section 10),
because all four are site prep.

**The location hint was proposed and withdrawn.** Pairing approval has no
location field today (the operator enters only the six-digit code) and
`TblDevice.LocationId` is written by exactly one screen, the Content Channels
tab, with the Devices tab deliberately not exposing it. Carrying a hint would
drag a device-to-location assignment redesign into a provisioning feature. **How
a device acquires its Location is a pre-existing gap**, on the map's fog.

### 5.7 Reflavouring

_DXME-134._ **It is a correction, not an adoption path.**

Open by saying what it is not, because the ticket that created it got this wrong
and the next reader will too. It was written believing 110 applet BrightSign
players would reach this flavour through it. Section 3 established there is no
production fleet, so the real population is **one device**, the dev HD1025 kept
deliberately as the fixture. Anything reaching for fleet-scale reflavouring
tooling should re-read section 3: there is no fleet to reflavour, and a reflavour
is physically a greenfield card install per player, so bulk tooling would
automate the step that is not the bottleneck.

**The operation is re-pairing.**

- **The device keeps its row, and that is not a choice.** Both flavours identify
  a BrightSign player by its serial, so the row is reused by construction,
  keeping `LocationId`, `Name`, `DisplayId`, the event log, device metadata and
  the Sentinel link.
- **Approval overwrites an existing flavour and tells the operator it is doing
  so.** Silent overwrite was rejected because it is exactly how a device that
  lies about its flavour reflavours itself. Approval is already a human gate, so
  the warning is a line of text on `DevicePairing.razor`.
- **Approval clears `TargetAppVersionId` and `ExtendedData`.** The pin is the only
  field that actively corrupts, since `UpdateService.cs:249` returns the pinned
  target before any platform check. `ClientAppVersion` is left alone, because the
  next heartbeat overwrites it. `tblUpdateDeploymentDevice` rows are left alone,
  because they are platform-guarded and go inert on their own. This is belt and
  braces on top of section 4.10 item 1, not a substitute for it.
- **Approval revokes other active tokens bound to that `DeviceId`.** A device
  holds exactly one active token. Pre-existing rather than introduced here: the
  only `RevokeDeviceTokenAsync` call site in the solution is a button at
  `DevicePairing.razor:364`, so every re-pairing on every platform leaves the
  previous token live.
- **The reverse direction is an operator action that clears the flavour, not a
  pairing.** Going back to the applet means clearing `PlatformId` to null. It
  cannot ride approval, because the applet's pairing request body carries no
  flavour field and the zero-applet-release property came from binding `platform`
  on the heartbeat only. Clearing is safe where setting is not, since null already
  means "no flavour enrolled". The action goes in the per-device settings modal on
  the Devices tab.
- **Clearing is refused while the device still declares the native flavour.**
  Null is never a mismatch, which is right for the 233 applet rows but means an
  operator who clears the flavour on a player still running Engage for BrightSign
  gets a device that raises no flag and falls out of the update pipeline
  entirely, since ALL scope is guarded by `HasValue`. The device declares what it
  is running on every heartbeat, so this is a check rather than a caution
  somebody has to read. **Clearing is the last step of moving a player back to
  the applet, not the first**, and doing it out of order fails loudly instead of
  stranding the device.
- **A reflavour writes a `TblEventLog` row**: old flavour, new flavour, approving
  user, what was cleared, and the Sentinel note below. Approved pairing requests
  are retained only 30 days (`PairingCleanupService.ResolvedRetention`), so the
  event log is the only durable record that the device ever ran the other
  flavour. `TblEventLog` already carries `DeviceId` and a JSON `Details` column
  and is the house pattern.
- **No new screens and no new tab.** Both surfaces already exist.

---

## 6. The recovery-URL handler

_DXME-139, refined by DXME-143._

**One handler, one response contract, two URL shapes, and the thing that
discriminates is server-side arming state, never a request header.**

A BrightSign player fetches and runs a BrightScript from the URL in its
`networking/ru` registry key. The handler serves three moments in a device's
life: installing Engage, converting a player during a takeover, and rescuing one
whose bootloader will not start. **It is the only recovery path that exists**,
because nothing on the card recovers a broken bootloader.

### 6.1 Shape

The OS contract is identical for all three callers: an HTTP GET carrying
`devicemodel`, `devicefamily`, `deviceid`, `devicefwversion`, `deviceuptime`,
`recoverymode`, `crashdump` and `storagestatus`, answered with 200 plus a
BrightScript body or 204. Three route registrations over one contract would be
one contract with three call sites. **What genuinely differs is trust, and trust
is carried in the URL.**

- **Tenant-scoped URL**, anonymous. What a provisioned player carries for life.
  Serves the recovery courier, and serves an install bootstrap with **no** pairing
  grant to an armed serial.
- **Per-player URL**, carrying a token bound to one serial. Written by the remote
  DWS at takeover trigger only. **The only response that carries a secret.**

The greenfield card forces that split: a package binds to a tenant and a family
and never to a device, so a provisioned player's permanent `ru` can only be
tenant-scoped. It is workable because only one of the three responses carries a
secret.

**The precedent is already in the codebase.** `PanelController.cs:16-120` is
`[AllowAnonymous]`, takes `/panel/{panelKey}/{token}` as path segments, streams
tenant content through `IMediaStore`, and returns an identical `NotFound()`
whether the key is wrong, the token is wrong or the panel is disabled. Its doc
comment at `:22-28` already argues the case for a permanent path token in a URL
typed once into a player config. **Copy its shape and its anti-enumeration
discipline.**

**The bootloader owns rewriting `ru`.** The per-player URL is temporary; the
bootloader replaces it with the tenant-scoped URL on the first healthy boot after
enrollment, and turns `rdwse` off once the player is _released_. This joins the
root CA and the proxy as a bootloader job for the same reason: JavaScript cannot
reach any of the three.

### 6.2 Trust

**`deviceid` is an input to selection and never to authorisation.** All eight
primary headers are forged by anyone who can reach the endpoint and read a serial
off a sticker. Per-player tokens are bound to a serial, and a mismatch is a
**logged rejection**, not a quiet 204.

**`networking/p` and the `Password` header are rejected**, tempting though `p` is
(documented as "password to the recovery handler"). It is unconfirmed that BOS
sends `Password` to a non-BSN handler at all, and the `a`/`u`/`g`/`p` key family
is BSN's, so writing it risks re-engaging exactly the cloud behaviour map
decision 2 exists to silence. A path token does the same job with none of that.

**Every install response requires an armed serial, for every caller.** This
reduces the endpoint to one sentence: _it only ever acts on serials an operator
has named, and everything else gets 204._ The cost is that R2 loses zero-touch
for unregistered players, which section 5.1 had already declined to support. The
threat this closes is thin, since an attacker would need the segment's DHCP
server, but the asymmetry is not: relaxing it later is one boolean, and walking
it back after a customer builds on it is not.

### 6.3 Branching

**Conversion state first, `recoverymode` only to refine.** Conversion state means
a `TblDevice` row exists for this serial with `PlatformId = EngageBrightSign`.

`recoverymode` alone gets the important case backwards, because a player whose
install **failed** reports `last-resort` while what it needs is the install
bootstrap, not the recovery courier. Within a converted player, `recoverymode`
then does its one useful job: `override` or `periodic` means healthy, answer 204;
`last-resort` means the on-card autorun is broken, serve the courier.

Three measured reasons this must not branch on headers:

- **The override GET cannot see that the player is broken.** Through an entire
  runtime-fault reboot loop it reported `sd=autorun`, the same value a healthy
  player sends. Only the last-resort GET carried `autorun-runtime-error`.
- **`CrashDump: yes` arrives on every request**, including clean reboots of a
  fully healthy player whose card holds no dump files. It cannot be used as a
  crash signal.
- Five headers arrive that are not in the documented set of eight
  (`DeviceBoard`, `DeviceOEM`, `timezone`, `verified`, `DestinationScriptFile`).
  None disturbs the rule above, which already treats every header as forgeable.

### 6.4 Exactly-once means "until it worked"

**Consume on confirmation, not on serve, with an expiry as backstop.** A GET is
not a transaction: the body may never arrive, the install may fail, the card may
be replaced. Consume-on-serve throws away the self-healing property of a
repeating periodic GET.

**The confirmation needs no new state**: an `EngageBrightSign` row for that
serial is the unambiguous "it converted".

_That holds for the endpoint, which derives conversion from the device row, and
it left the operator's view empty: nothing called `RecordConversionAsync`, so an
authorisation stayed `Armed` through a takeover that worked, and "how many armed
serials have converted", the one question this table exists to answer, read zero.
`HeartbeatService` now closes the authorisation at the moment a serial resolves to
the native platform, which is the only observable conversion event: a grant is an
ordinary device token with no redemption step, so there is no earlier hook. Found
on the bench 2026-09-13._

**A served install bootstrap holds off the next one for 20 minutes, and that
hold-off is what makes a takeover converge.** The OS checks the network recovery
override _before_ it runs the card's autorun. So an armed, not-yet-converted
serial that is answered on every boot is handed a bootstrap, rewrites its card,
reboots, and is handed another: the autorun it just installed never starts, and
conversion, the only thing that ends the serving, can never happen. This is
section 6.3's ~66 second loop reached from the other side, and the 204 for a
healthy converted player does not prevent it, because the player is not converted
yet. `TblProvisioningAuthorization.BootstrapServedDate` is stamped on serve and
`RecoveryHandlerService.BootstrapHoldOff` turns it into a 204 with a `Retry-After`
covering the remainder. The grace lapses rather than latching, so an install that
genuinely failed is retried and consume-on-confirmation still holds. Re-arming
clears it by construction, because re-arming inserts a new row.

_Deviation, found on the bench 2026-09-13, after the zip-versus-autorun fix
above. The courier committed the card perfectly and the player still rebooted
into the courier every 66 seconds, which is indistinguishable from a courier that
never ran._

**The conversion signal is re-asserted by the running app, not just by enrolment.**
Every heartbeat carries `declaredFlavour`, which sets `PlatformId` back to the
native platform. So clearing `PlatformId` on a player whose app is still running
un-converts it for as long as it takes the next heartbeat to arrive, and the
player then reads as "converted and healthy" and gets the load-bearing 204
forever. Re-testing a takeover means stopping the app first and clearing
`PlatformId` after. This is the same shape as the pairing-code finding in section
7: conversion is a derived fact that several paths re-derive, not a latch.

_Noted on the bench 2026-09-13, where it cost an evening of a courier that
appeared not to run and was never being served._

**Each 200 mints a fresh short-lived pairing grant** rather than re-serving a
stale one, so a bootstrap sitting on a card is worthless on its own.

**"Needs it twice" has one answer for all three cases** (failed install,
reflavour, replaced card): **re-arm**, an explicit operator action.

### 6.5 Payload: always a courier, never a redirect

**Always serve the `.brs` inline.** The served script is a **courier**, not the
payload: small, self-contained, and its first job is to fetch the real artifact,
write the card and reboot. Two variants, an **install bootstrap** and a
**recovery courier**.

**The bench proved this is necessary rather than prudent. The OS never writes the
served body to the card.** Every request carries an undocumented
`DestinationScriptFile` header naming `tmp:/recovery1.brs` or
`tmp:/recover2.brs`, and the broken autorun was found untouched at its exact byte
count from inside the recovery context. So **a handler that served the real
bootloader as the response body would rescue nothing**: the player would run it
from `tmp:` once and boot straight back into the broken card. A redirect would
add an untested OS behaviour to the one path that has to work when everything
else on the player is broken.

**A handler that always returns a body reboot-loops a healthy player.** A 200
with a body on an **override** GET means "use this as the autorun", so a handler
returning a body unconditionally is served on every boot, runs instead of the
card's autorun and reboots: a ~66 second cycle that never converges, on healthy
players. **Section 6.3's rule that a healthy converted player gets a 204 is
load-bearing, not housekeeping.**

**The install bootstrap fetches TWO artifacts; the recovery courier fetches one.**
A bootloader on its own converts a player into a parked one: it reboots into a
card with nothing in either slot, stays reachable and idle, and has no widget to
consume the grant it was just handed. So the install bootstrap also fetches the
app bundle, to `sd:/a.zip`, the card-root path `InstallSlot` accepts when
`sd:/staging/` does not exist yet, and `a` because a virgin registry selects that
slot. An armed serial with no bundle to send is a **refusal**, not a half
install: the authorisation's `PinnedAppVersionId` is used when set, otherwise the
newest `App` row on the native platform.

**The bundle is fetched with the grant, not anonymously, and that is the point of
the grant being an ordinary device token.** The authenticated artifact route
already serves it, so no second anonymous path to tenant bundles has to exist.
The bootloader's anonymous route stays anonymous because a player with a broken
bootloader has no token at all, and that is safe only because a bootloader is
tenant-neutral by construction, an invariant that says nothing about a bundle.

**The bootloader has two routes, and which one you get depends on whether you can
unpack.** `api/Recovery/bootloader/{tenant}/{id}` serves the artifact and is what
the ordinary update pipeline offers: the app downloads it to
`sd:/staging/bootloader.zip` and the running bootloader unpacks it.
`api/Recovery/bootloader/{tenant}/{id}/autorun` serves the `autorun.brs` inside
that artifact, and is what the courier is handed. Hashes follow the bytes: the
offer carries the recorded `ZipFileHash`, the courier carries
`BootloaderScriptHash`.

_The comment on `ResolveArtifactUrl` used to say "one URL rather than two", and it
was right for as long as both consumers wanted the same bytes. They do not, and
collapsing them cost a bootloader update that could never install: the app
downloaded a `.brs` to a path named `.zip`, checked it against the zip's hash, and
stopped. Measured on the HD1025 on 2026-09-14, which sat on a valid offer doing
nothing for ten minutes. Two consumers, two routes, each with the hash of what it
actually receives._

**The autorun route serves the script, not the artifact that contains it, and
its hash is computed over what is served.** A bootloader artifact is a zip,
because the ordinary update path downloads it and the running bootloader unpacks
it. The courier cannot do that: it runs from `tmp:` on a player where nothing
works, and its whole safety property is that it commits by a single rename, so
handing it an archive would trade the atomic commit for unpack logic in the one
script that has to survive everything. `RecoveryController.GetBootloaderScript`
therefore extracts the root `autorun.brs` and streams it as `text/plain`, and
`BootloaderScriptHash` hashes those same bytes rather than passing the recorded
`ZipFileHash`, which covers the zip and would make every courier reject a
bootloader that arrived perfectly intact. An artifact with no root `autorun.brs`
is a 404: the caller can retry a 404 and cannot undo a card it has written.

_Deviation, found on the bench 2026-09-13. The courier was originally handed the
zip route. The player fetched 14,824 bytes, renamed them onto `sd:/autorun.brs`,
rebooted into a file the OS will not run, and landed back in recovery, which
looks exactly like a courier that never ran._

**Nothing reaches the card until every fetch has arrived and every recorded hash
has matched**, and `autorun.brs` is committed last, because it is the file that
makes the card ours on the next boot. A courier that fails partway therefore
leaves the player exactly as it found it and the next scheduled GET tries again,
and a power cut between the two moves leaves a card that boots as it did before
rather than a parked player.

**Recovery serves the tenant's blessed known-good bootloader and leaves the A/B
slots alone.** No per-device bootloader history. A broken bootloader damages
nothing else (`config.json` is at the card root, the slots are under
`sd:/app/`), so recovery is a single-file replacement. A per-device history would
be state to keep correct across card swaps, reflavours and connector-mirrored
rows, in service of a path whose only required outcome is "a bootloader that
boots", and it would be **actively wrong** in the scenario being guarded against,
a bad bootloader rolled out fleet-wide, where "the last version this device ran"
is the broken one on some players.

### 6.6 Tenancy

**The tenant is carried in the path and is authoritative regardless of the host.**
Bound manually via `WorkerScopeFactory.CreateTenantScopeAsync(subdomain)`
(`:46-57`), which already exists for background work. **The token is the
subdomain string**, because `FindBySubdomainAsync` is the only key `ITenantStore`
exposes (`ITenantStore.cs:6-27`): no by-id and no by-URL lookup. Inventing a
second identifier means a second index and a second thing to keep in sync.
`TblTenant.SiteUrl` is not a candidate key: nullable, `nvarchar(500)`, no unique
constraint and no index, read by one consumer. It is a URL generator.

**This is a workaround for a real hazard, not a preference.**
`TenantResolutionMiddleware.cs:15-71` resolves the tenant from the Host header
alone; `ExtractSubdomain` (`:79-99`) returns null for a bare IP and for any host
with fewer than three dot-separated parts, and `:40-48` falls through to
`DXSuite:DefaultTenant`. **An on-prem Director reached at
`director.customer.local` or by IP would silently serve one tenant's bootloader
to another tenant's player.** An unknown subdomain hard-404s at `:31-35`, so the
safe case is loud and the dangerous case is silent. See section 9 for the
fallthrough itself, which is out of scope here.

**An unknown tenant token, an unarmed serial and a disabled tenant all produce
exactly the same 204.** Nothing tells a prober which one it was.

**Because of that, unresolved requests are logged at host level**, tenant token
and serial included, so the provisioning tab can say "four players are calling a
URL that resolves to nothing" instead of leaving an operator to guess at silence.
Host level rather than a tenant database, since by definition we could not pick
one.

### Build note on 6.1 to 6.5

Built as `RecoveryController` over `IRecoveryHandlerService`, with the couriers in
`RecoveryCourier`. Two narrowings worth knowing:

- **The anonymous bootloader route carries the tenant in its path too**
  (`api/Recovery/bootloader/{tenantToken}/{appVersionId}`) and serves
  `Bootloader`-kind rows only. An app bundle is not tenant-neutral, so it must not
  be reachable from an anonymous route even by guessing an id.
- **The bootloader's `ru` rewrite is the player's job and is not built here.**
  This half serves both URL shapes; replacing the per-player URL with the
  tenant-scoped one on the first healthy boot, and turning `rdwse` off on release,
  stay with the bootloader, because JavaScript cannot reach any of it.

### 6.7 Rate limiting

**This endpoint gets the solution's first `AddRateLimiter`**, scoped to this
route and partitioned by tenant token plus serial. There is no ASP.NET rate
limiting anywhere in DXSuite today. `ApiKeyRateLimiter.cs:10-111` is a per-IP
failed-auth lockout consumed by the auth handlers, not a request throttle, and
bending it was rejected because a lockout and a throttle want different behaviour
on the same request. Keeping it route-scoped keeps the blast radius of a
first-of-its-kind change inside a feature nobody is using yet.

### 6.8 `Retry-After`, and the hazard in it

**Return an explicit `Retry-After` on every response.** Long for converted
healthy players (24 hours), short for armed-but-unconverted ones. A converted
player already has a heartbeat channel, so its periodic recovery GET is pure
redundancy; an armed player is mid-operation and should convert on the next
cycle. This is the map's second bandwidth lever after batching, and it costs
nothing on the path that matters, because last-resort recovery is immediate
rather than scheduled.

**It is honoured exactly, and that cuts both ways.** Cadence moved from 300.0 s
to 120.0 s the moment the handler began sending `Retry-After: 120` and held at
exactly 120.0 s for four consecutive intervals. **But it is honoured in
last-resort too**, not only on the periodic healthy poll. Serving a 24-hour value
to a player in last-resort would park it broken for a day. Section 6.3's rule
mostly avoids this by serving the courier to a converted player in last-resort,
but every fallback path (no known-good pinned, unarmed serial, unresolved tenant,
section 6.6's deliberately indistinguishable 204) ends in a 204 a broken player
can reach. **Any 204 reachable in last-resort must carry a short `Retry-After` or
none.**

### 6.9 Provisioning authorisation

**A new per-serial record**: tenant, serial, armed-by, armed-at, expiry, state,
version pin. Verbs are **arm** and **disarm**.

There was nowhere to put it, because the 112 Pulse Core BrightSign players are
not rows in `tblDevice`. **Reusing `TblDevicePairingRequest` was rejected as an
inversion**: a pairing request is player-initiated, keyed on a six-digit code,
and expires in ten minutes, where arming is operator-initiated and lives until
conversion. It is also the record the provisioning tab needs, since "63 of 100
armed serials have converted" is a query against this table and nothing else.

**Arming pins a version**, recording which bootloader and app bundle that
conversion installs. A hundred-player takeover straddles a release, and a soak
period in which Monday's and Friday's cohorts run different bundles tells you
nothing. Re-arming picks up current. **This is a separate pin from section 4.9's
known-good bootloader**: that one answers "what rescues a broken player", this
one answers "what does this conversion install". Conflating them would let a
rollout candidate become the rescue image.

**Disarming is the kill switch for a takeover in flight.**

### 6.10 Two operational facts for whoever sizes this

- **The boot-delay cost is an outage penalty, not a feature tax.** A handler that
  answers costs nothing measurable. A black hole and a **refused connection each
  cost +20.5 s on every boot, identically**, because the OS observes a fixed
  window rather than waiting on a socket. There is no "make Director refuse
  quickly" mitigation: an unreachable Director adds 20 seconds to every boot of
  every player until the outage ends.
- **A bootloader that faults at runtime is meaningfully worse for the fleet than
  one that fails to compile.** A compile failure parks the player politely and
  polls every 300 s honouring `Retry-After`; a runtime fault **reboots every ~76
  seconds regardless of a responding handler**, offering ~21 seconds of recovery
  window per cycle. **So a compile check gates any bootloader artifact entering
  the pipeline.** It is cheap and it converts the worse failure mode into the
  gentler one.

---

## 7. Takeover and reclaim

_DXME-138, DXME-130 section B, reranked by DXME-147, measured by DXME-150._

**Takeover works with no technician, and it requires BSN console access held
through removal.** An engagement with no console access gets an R1 card and a
technician, not a remote takeover.

### 7.1 The channel

The remote DWS at `https://ws.bsn.cloud/rest/v1/`, authorised by an OAuth2 bearer
token scoped to the customer's network. **The rDWS is not a cut-down read-only
mirror of the local DWS**: it carries file upload, arbitrary registry writes,
`PUT /registry/recovery_url/`, firmware download, storage reformat, reboot,
screenshot, and a `GET /re-provision/` the local API lacks. **This is why remote
takeover is possible at all.** `rdwse` defaults to true, so the channel is
already open on a normally deployed fleet and nothing has to be enabled first.

**Reaching it is three hosts, and the middle one is the trap.** Mint a token at
`https://auth.bsn.cloud/realms/bsncloud/protocol/openid-connect/token` with
`grant_type=client_credentials` and a Basic `clientId:clientSecret` header;
**pin a network** with `PUT /Self/Session/Network` at `api.bsn.cloud`; then
address the player at `ws.bsn.cloud`. **Omit the pin and every call fails exactly
as an unreachable player would.** Tokens live 5 minutes 30 seconds with no
refresh token, so the pin repeats on every re-mint, and client secrets expire
every 180 days. An ordinary console-granted Application credential suffices.

**An rDWS `403` means the serial is not in the pinned network. It does not mean
the credential lacks scope.** Measured both ways on one player in one evening:
200 while enrolled, 403 seven seconds after console removal, with a
never-enrolled serial returning 403 throughout. **A run with no positive control
cannot tell these apart**, which is how
[DXME-152](https://radianttechnology.atlassian.net/browse/DXME-152) came to be
filed and withdrawn the same night.

**Almost nothing is bulk.** The rDWS is strictly one player per call. Only two
bulk operations exist on the entire surface: enumerate the fleet, and delete
provisioning records in batch.

### 7.2 Routes

**T1 is the supported takeover route.** Upload our file set with the rDWS, verify
each write by read-back, rename the incumbent `autorun.brs` to `autorun.backup`,
rename ours in, reboot. Roughly 6 to 10 calls per player. Chosen because it is
the only route whose failure mode is "player still runs the old thing" and whose
rollback is two API calls, which is exactly the insurance the no-technician
premise requires. **All three operations were executed against real hardware.**

**The rename call takes a JSON body and says otherwise.**
`POST /files/{path}` with `Content-Type: application/json` and
`{"name":"newname"}` renames. A multipart form carrying the same `name` field is
refused with `400 A 'name' parameter was not specified in the body`, which reads
as though the parameter were missing rather than the encoding wrong. **This is the
call T1 depends on.**

**T2 is documented and not supported.** Reassign the B-Deploy setup, then
`GET /re-provision/`. It clears the card, a failure leaves a blank player nobody
can reach, and it deepens the customer's cloud authority over the fleet rather
than loosening it.

**T3 is dropped as a route.** Repointing `networking/ru` over the rDWS was
previously ranked as the strategic target, with a promise to promote it above T1.
**That promotion is withdrawn and so is the route.** Its window is close to empty
in both directions: before release the repoint may be undone from the server side
and the player stays console-reachable, and after release there is no rDWS left
to write the key with. **Read this as the route being out, not the write**: the
same write survives as a post-takeover hardening step the bootloader already owns
under section 6.1.

**T4, remote firmware for the BOS 8 half, is a mandatory separate maintenance
window**, sequenced first, alone, never combined with an install. It carries the
only failure mode that can strand a player, because rescue mode needs the SVC
button. An 8.5.64 to 9.1.151 upgrade took 148 seconds and left `bootVersion`
untouched at 8.0.15; the DWS fetches the firmware itself via
`GET /api/v1/download-firmware?url=<url>`, so there is no need to push the image
through the file API.

**The R1 card stays costed in every takeover proposal** as the per-player
fallback. At 100 players some will fail T1, and a proposal that assumes otherwise
is wrong on day one.

### 7.3 The sequence, and why it cannot be reordered

**Install, verify heartbeat, release, soak, reclaim.**

**Un-enrolment cannot be a precondition, because it removes the channel.** T1
runs through the rDWS, addressing a player by serial within the customer's
network, and removing the player at the console removes the handle. Measured:
the same rDWS call returned 200 seven seconds before the console click and 403
seven seconds after.

**An install placed before release survives release.** Console removal is purely
server-side. Measured on a card carrying all three layers at once (our rig, the
preserved incumbent autorun, and BSN's own provisioned partner app): **95 files
in 50 directories before, 95 files in 50 directories after, with zero failed
listings on both sides**, and not one registry key removed, added or changed. The
player kept `access_token`, `refresh_token`, `registered_with_bsn`, `ru` and
`bsnrt`.

**Writing the runbook defensively instead of measuring was rejected.** Treating
removal as potentially destructive means sequencing it before install, which
kills the rDWS channel before anything is installed, which leaves no remote
takeover at all. The defensive posture is not conservative here, it is
self-defeating.

**A four-call pre-takeover snapshot is taken before the first write to any
player**: full registry dump (**minus the `ru` credential, which must not leave
the tenant**), a recursive file manifest of names rather than bytes, a screen
snapshot, and the B-Deploy provision record. This is what makes the operation
reversible rather than hopeful.

**Takeover is delivered as a plain file set plus an explicit rename, not as
`autorun.zip`.** The zip form overwrites same-named files on unzip, and in a
takeover the incumbent deployment is the safety net. Both forms build from one
artifact: zip for provisioning, file set for takeover.

### 7.4 Release, and the dual-homed window

**A player is standalone only once _released_, meaning removed at the incumbent's
BSN console. Until then it is dual-homed and the incumbent retains
re-provisioning authority over it.** This is a scope statement on map decision 2
rather than a retreat from it: DXSuite is the sole runtime hub for **our
software**, which was never a guarantee about the player.

**The site prerequisites page gains a line: the customer must release the players
at their BSN console, and the engagement does not complete until they do.** The
ask is smaller than it looks, because remote takeover already presupposes
customer BSN credentials; the new requirement is only that the access persists
through removal rather than ending at install.

**The dual-homed window gets a tracked state, not a detector.** DXSuite records
BSN registration state on the device row as `enrolled` / `released` / `unknown`,
set by the operator at removal, as a third `DeviceExtendedData` member alongside
the package fingerprint. No schema change. **Every existing row defaults to
`unknown`, because we genuinely do not know, and `unknown` must never read as
`released`.**

**There is no detector to build.** Removal is invisible to the player: the log
gained nothing across the removal, there was no disconnect and no event, and the
player still believes it is enrolled because only BSN's routing was withdrawn. A
released player and an enrolled one are indistinguishable from the player side. A
polling detector would also cost a per-player rDWS call and only works while the
player is still enrolled, which is the state we are trying to leave.

**The bootloader turns `rdwse` off after release**, one registry write on a path
it already owns, removing a remote-execution surface whose only remaining
authoriser is a cloud we have just told the customer we do not use. Not an
operator toggle, and not a seventh bootloader job: it belongs to the existing
`ru` rewrite step, which fires at the same moment.

### 7.5 Reclaim

**Incumbent content is cleared by a separate reclaim operation, never during
takeover.** Deleting what you would roll back to converts a reversible operation
into a one-way door on an unreachable player.

- **`released` is a hard gate.** A soak period measures whether our software is
  stable and says nothing about whether the incumbent can still push. Reclaiming
  an enrolled player deletes the rollback target while the console retains
  authority to re-provision: we lose the safety net and they keep the lever.
  **Reclaim refuses on `enrolled` and on `unknown`**, which is why `unknown` must
  fail closed.
- **It deletes exactly what the pre-takeover manifest recorded**, not "everything
  that isn't ours", with **every delete verified by read-back**. The local DWS
  reports success for deletions it never performed, and the remote docs make the
  same unreliable claims.
- **This is not housekeeping.** The player has one storage tier with no degraded
  mode, so on a nearly-full card leftover content is a boot-time hard fault.
- **A card too full to install at all inverts the order**, and that is the one
  case where the safety net is surrendered knowingly. It requires **explicit
  operator acknowledgement**, never a silent decision. Free space is visible via
  `StorageSize` / `StorageHealth` and the new telemetry member.
- **Never reformat the storage.** A player booted with an empty card enters BOS
  recovery, rolls its firmware back and restores its BSN identity. On an enrolled
  player a reformat is the exact gesture that undoes the takeover.

### 7.6 Re-enrolling a deployed player costs its card

**B-Deploy re-provisioning needs four conditions, and the last one is decisive.**
A player re-provisions only with no valid `activation_token` in the registry, no
local presentation on the card, a device record naming a setup that resolves,
**and an empty card**. With the first three satisfied and a populated card,
`bacon-runner` created `pool/` and `feedPool/` and then fetched nothing for three
minutes; wiping the card produced full enrollment in 160 seconds.

Read together with section 7.3's measurement: **release is close to a one-way
door in practice.** That belongs in the takeover runbook next to the release
step.

**The same gate is protective in deployment**, which is the happier reading. Once
Engage's `autorun.brs` is on the card the player stops asking BSN for a
provisioning package, and the bench rig survived two reboots as a fully enrolled
player without BSN reinstating the partner application over it.

### 7.7 Two prohibitions, and one non-goal

- **Never scrub BSN registry keys on the player. Scrubbing does not un-enroll, it
  re-provisions.** A player whose registry had been verifiably scrubbed of all 22
  BSN keys, and whose card had been wiped, minted a fresh `activation_token` on
  reboot and pulled a 455 KB provisioning zip whose `setup.json` carried back the
  **exact** `recoveryHandler` and `bsnRegistrationToken` values that had been
  deleted, plus the account, group and `partnerUrl`. BSN holds them server side,
  keyed on the serial. **Only removal at the console stops it.**
- **Never reformat an enrolled player's storage** (section 7.5).
- **Engage must never implement the settings handler at
  `/api/v1/system/supervisor/capabilities`.** A player running a partner
  application refuses cloud property pushes **because** that handler is absent.
  True by accident today; it is an explicit non-goal, because a "sync settings
  from the cloud" convenience would hand the customer's console authority over
  our runtime. This is a player-side rule recorded here because the takeover
  runbook is where its value becomes visible.

---

## 8. Every DWS write is verified by read-back

Not a section so much as a standing rule, placed here because sections 5, 7 and
12 all write through this surface and the failure mode is always the same.

**The BrightSign DWS reports success for work it did not do. Nine cases are
known.** The catalogue with a ticket pointer on each is in
[`map-facts.md`](https://github.com/Radiant-Technology-Group/Engage_BrightSign/blob/dev/docs/map-facts.md);
the ones that bite hardest:

1. A plain `GET` on a file returns stat rather than bytes; use `?contents`.
2. An upload into a directory that does not exist is silently discarded. **There
   is no mkdir: `POST` on a path is rename.**
3. `DELETE` succeeds on paths that never existed.
4. Unencoded paths silently skip directories with spaces.
5. The multipart `name` field is ignored and the file is named after the file
   part, so stage a copy under the target name.
6. A listing taken straight after an upload can be stale, and the DWS drops
   requests outright while the player is rebooting.
7. **Writing the DWS password registry keys does nothing while reading back
   correctly.** The read-back returns exactly what was written, so the natural
   verification confirms a lie. Use `control/dws-password`.
8. **A failed directory listing is indistinguishable from an empty directory.** On
   an LS424 the file service returns a bare connection failure with no HTTP
   status on roughly four calls in five while the player is busy. A card walk
   recorded 50 files and 6.2 MB where the card held **42,212 files and 310 MB**,
   and the backup, the wipe and the wipe's "card already empty" verdict were all
   wrong in the same direction. **A DWS client must distinguish "no answer" from
   "answered with nothing" and must never treat a transport-level failure as an
   empty list.**
9. The rename call takes JSON and its error message says otherwise (section 7.2).

The console is in the same family: **it reported an error on removal and removed
the player anyway.**

Assume the rDWS shares these defects. The remote docs repeat the exact
folder-creation claim already falsified locally.

---

## 9. Security

_DXME-131, with DXME-149 and DXME-140 corrections._

### 9.1 The device token's binding check is the decision

**A device token is not scoped to its device, so a leaked token reads the whole
tenant's content.** `DeviceTokenService.ValidateDeviceTokenAsync` (`:24-73`)
tests exactly two things, that the hash exists and that `IsActive` is true.
`DeviceId` is returned as a claim and **never compared** against the `DeviceId`
in the request body, which is where `GetPlayerContent` takes it from; binding at
`SignageController.cs:386-392` is fire-and-forget and nothing reads it back.
`Screenshot/Native` is worse: `deviceId` is a free query parameter, and
`UploadNativeScreenshot` (`:1668`) validates the token and then takes `deviceId`
from the query string without comparing the two.

Decisions:

- **Enforce the binding.** Once a token is bound, the `DeviceId` in the request
  must match it. Cheap: the column exists and is already populated.
- **No TTL.** There is no expiry column and none is wanted. A player off for
  three months must come back without a truck roll.
- **One live token per device**, so approving a pairing request revokes the others
  bound to that `DeviceId`.
- **Revocation is immediate.** Evicting `DeviceTokenAuthHandler`'s five-minute
  `IMemoryCache` entry (`:79-93`) is part of the fix, not a separate defect: a
  revoked token currently keeps working for up to five minutes. **No grace
  period**, which only ever helps an attacker, since a legitimate player
  re-pairs deliberately.
- **Record `LastUsedAt`** before revoking, and add enough to make a stolen token
  detectable at all.
- Documented recovery is revoke and re-pair.

For reference: the token is `dt_` plus 43 base64url characters from 256 bits of
CSPRNG, stored as an unsalted single-round SHA-256, with the first 8 characters
of the secret kept in cleartext as `TokenPrefix`. There is no scope column and
`Permissions` is a hardcoded constant.

### 9.2 Media asset authentication: tokenised URLs, not header auth

**A tenant-scoped path token embedded in the manifest URLs, validated by
middleware in front of the media root, with an identical `NotFound()` on every
failure.** The `PanelController` pattern applied to content.

This looked like a binary: leave `/MediaAssets` anonymous, or turn on header auth
and break every applet player until they ship a release, spending exactly the
zero-applet-releases section 2 bought. **There is a third option, because the
player fetches whatever URL the manifest hands it.** The server emits tokenised
URLs and both flavours authenticate with no client change at all.

Necessary because **there is no per-flavour switch to throw.** `/MediaAssets` is
`UseStaticFiles` with a tenant-path rewrite (`Program.cs:675-690`), one pipeline
serving one path to every flavour, anonymously.

**Cost, accepted: the token must be stable per tenant**, because the player caches
assets by URL and a rotating token re-downloads the whole library. A leaked
manifest therefore leaks that tenant's content library. Strictly better than
today's nothing. The header-token route stays available later if per-device scope
is ever worth an applet release.

**Build note: emission is unconditional, enforcement is a switch.** The token is
minted per tenant into `tblSystemSetting` on first use, and every manifest URL
carries it from the moment this ships, at `/MediaAssets/t/{token}/...`. **Refusing
an untokenised URL is gated on `DXSuite:MediaAssets:RequireToken`, default
false**, matching `DXSuite:TenantMedia:Enabled`. A player caches assets by URL and
holds a manifest it was served earlier, so enforcing on the same deploy would 404
every already-cached asset until each player happened to re-poll. Emit first,
enforce after one manifest cycle, and the window this section accepts does not
have to exist. **A wrong token is refused either way**, because presenting one is
a claim.

Worse than it first appeared, which argues harder for this:
**`DXSuite:TenantMedia:Enabled` defaults to `false`**
(`ITenantMediaPathResolver.cs:64-65`) and is absent from `appsettings.json`, so
outside the demo deploy every tenant is served from one shared
`wwwroot/MediaAssets` tree partitioned only by location id. Eight services also
write to `_env.WebRootPath` directly and bypass the resolver, the Pixera ones
deliberately and documented, the rest not.

### 9.3 Download integrity: hash, no signature

**SHA-256 only, carried in the update payload, verified by the bootloader before
`Unpack()` and before the slot switch. No code signing at launch.**

Half built already: `TblAppVersion.ZipFileHash` exists as `varchar(64)` lowercase
hex from a streaming SHA-256 (`UpdateService.cs:511-517`) and already reaches
devices as `FileHash`. **Nothing in DXSuite ever re-reads a file and compares**,
so this is a client-side decision with no new server column and no new field.

A real signature was **rejected on mechanism, not appetite.** BrightScript offers
`roEVPDigest` and `roHMAC` and no public-key verifier. An HMAC means a symmetric
secret on an SD card that anyone with physical access reads, which is not a trust
anchor; a verifier in the app bundle is useless because the app bundle is the
thing being replaced; and real crypto in the bootloader runs against the rule
that the bootloader's API surface must stay small.

**Residual, stated rather than implied: whoever owns the DXSuite host owns the
fleet.** That is already true because the host is also the content source, so
signing would buy less than it appears to.

### 9.4 The binary screenshot route

**New route, `DeviceToken` scheme through the real handler, header only, raw bytes
with a content-type allowlist and a 5 MB cap, and the device taken from the
token's bound `DeviceId` rather than from the request.** That last clause is
section 9.1 applied at birth rather than retrofitted.

The 5 MB cap replaces the base64 route's effective ~750 KB ceiling, which was
rejected for failing unpredictably on 1080p captures: unpredictable failure is
worse than consistent failure.

**Delete `Screenshot/Native`'s `?apiKey=` acceptance.** It hand-rolls its own
validation, so it bypasses the `ApiKeyRateLimiter` IP lockout entirely, and it
puts a bearer secret into access logs and referrers. The header path may stay for
whoever still uses it. The three `sos/{requestId}/upload/*` routes are left
alone; they belong to the applet.

### 9.5 Publishing and deploying are different privileges

**Two new `feature:` permission keys, both defaulting to Admin only, both
grantable to a custom role.**

Today there is one gate, the `playerupdates` settings tab at `MinimumRole` Admin
(`DirectorModule.cs:64`), and holding it means uploading a zip, creating a
deployment, arming it and starting the batch. **Publishing introduces new code to
the fleet; deploying chooses who receives code that already exists.** A customer
staging their own rollout should not need the right to introduce a binary, and
that is the common case. The house pattern already supports the split: content
carries `feature:content-upload`, `-edit`, `-delete` and `-approval-bypass` as
distinct keys.

**Not gated on the platform-operator plane.** `PlatformElevationMiddleware` is a
no-op when the Platform DB is unconfigured (`:71-76`), which is exactly the
on-prem shape this flavour ships into first.

Two defects fixed alongside: `CreatedBy` is null on the Blazor upload path
(`PlayerUpdates.razor:563` omits it), and nothing records who put code on a
fleet, so **publishing writes a `TblEventLog` row.**

Also note the one existing gate is weaker than it looks: `PlayerUpdates.razor`
has no page-level authorisation attribute, no `AuthorizeView` and no permission
check in the component. Reachability is `DirectorModule.cs:64` filtered in
`Settings.razor:79-98`.

### 9.6 Scope enforcement on the update API

**Two new API-key scopes mirroring the two permissions above, enforced on every
admin action in `UpdateController`. `BatchDeploymentController` gets the
`AdminApiKey` scheme plus the deploy scope.**

**No `UpdateController` action calls `CheckScope`**, so any active admin key of
any scope can upload a version, create a deployment, arm it and delete versions.
**`BatchDeploymentController` carries no `[Authorize]` at all**, so deployment
start, pause, resume and stop are anonymous to anyone who can reach the host.
Wildcard `*` keys keep working, so nothing deliberate breaks; keys scoped to
`signage:heartbeat` or `signage:read` lose an access they were never meant to
have.

On the precedent: [DXME-60](https://radianttechnology.atlassian.net/browse/DXME-60)
genuinely fixed this, but **point-locally.** `f8c6789` blocks bundle deletion for
API-key callers outright and `PanelApiController.cs:22-25` records the reasoning,
but `CallerIsAdminOrArchitectAsync()` still returns `true` unconditionally for
any API-key caller, so the pattern is still in the codebase waiting to be copied,
and `UpdateController` is where it was copied to.

### 9.7 Pairing

**Rate-limit `POST api/signage/Pairing/request`, per IP and per tenant, plus a
cap on outstanding unapproved requests per tenant.** Deliberately boring, because
this is a denial-of-service and database-noise problem rather than an
authentication one: approval is still a human act. The pairing-grant path gets
the same limiter, keyed on the armed serial.

Pairing is unthrottled today: the controller never touches `ApiKeyRateLimiter`,
so an unauthenticated caller can create unbounded `TblDevicePairingRequest` rows
in any tenant's Director database. `MaxFailedAttempts` guards the operator-side
approve, not this.

Three further holes in the flow, all owned here:

- **The status poll is not one-time.** `GetPairingStatusAsync` returns the
  plaintext token on every poll for 15 minutes keyed only on the request GUID,
  and **knowing the six-digit code is not required to collect it.** The code
  itself is 6 decimal digits, about 19.8 bits, with a 10-minute default lifetime.
- **One operator typo can deny every pending enrollment in the tenant.** A wrong
  code on the approval screen increments `FailedAttempts` on **every** pending
  request (`DevicePairingService.cs:142-164`) and auto-denies each one crossing
  5.
- **The blocklist is poisonable.** Denied rows are retained forever as the
  blocklist and match on `DeviceId` **or** `MacAddress`, both attacker-supplied
  in an anonymous request body. Submitting a request carrying a real player's MAC
  and getting it denied permanently blocklists that player.

**Build status of the three.** The first is fixed: the status poll is now
one-time, the plaintext is dropped from the row on collection or on expiry of the
fifteen-minute window, and what is stored in between is protected at rest. **The
other two are still open**, deliberately. Both are anti-enumeration behaviour
whose replacement this section describes but does not specify, and changing
either one quietly is how a denial-of-service defence becomes a denial-of-service.
The typo case wants failure counted per operator or per source rather than per
pending request; the blocklist wants a key the requester does not supply. Neither
is this flavour's to invent, and neither blocks a build.

### 9.8 Secrets at rest, and package revocation

Protected columns following the `SmtpCredentialProtector` pattern, in a solution
that already protects five other integration credentials:

- the **per-device DWS secret** on the device row (section 12)
- the **bootstrap DWS password** on the package record
- the **Wi-Fi PSK** on the package record
- the **persisted pairing tokens** from section 4.10 item 4
- **provisioning authorisations and their version pins**

**Package downloads are permission-gated and audit-logged.** A superseded package
stays downloadable because cards in the field reference it; **explicit revocation
is the escape hatch when one leaks**, and the package fingerprint on each device
row is what answers "which players came off the leaked card". **The runbook
treats a lost card as requiring PSK rotation.**

The package's genuinely sensitive content is not the endpoint but the Wi-Fi PSK:
a stolen card is a customer network-credential incident. Wired sites, the
expected case, carry no PSK at all.

### 9.9 Tenant identification, and the hazard left out of scope

**The tenant rides the path and is authoritative on every route this flavour adds.
Never inferred from the Host header.** No exception.

**"Authoritative" has a mechanism, settled by
[DXME-193](https://radianttechnology.atlassian.net/browse/DXME-193): the route
repopulates `ITenantContext` from its path segment as its first act, before it
touches any collaborator.** Reading the path and then calling a service is not
enough. Every service a route calls resolves tenant from ambient context, not
from the caller: `PanelController.cs:113` passes `_tenant.TenantId` straight into
`IMediaStore`, and the tenant-scoped `DbContextFactory` behaves the same way.
Threading the path tenant explicitly through every signature was considered and
rejected: it is more honest, it fights every service signature in the codebase,
and one missed call site fails silently against the wrong tenant.

Note also that `PanelController`, cited elsewhere as the house example for
anonymous device-facing routes, **does not carry a tenant in its path at all**.
It is the example for token-in-path and anonymous access, not for this rule.

The hazard is worse than first recorded, in three ways beyond section 6.6:

- **On the no-subdomain path only, an `X-API-KEY` header can override the
  resolved tenant** via `FindTenantByApiKeyHashAsync`
  (`TenantResolutionMiddleware.cs:50-60`). So the header selects the tenant on
  exactly the deployment shape on-prem takes.
- **That override is live on the default store**, contrary to the note this
  section previously carried. `TblApiKeyMapping` having no writer is true of
  `SqlTenantStore` (`:111`), which is **opt-in** (`Program.cs:440-445`).
  `ConfigTenantStore` is the **default registration**
  (`TenantServiceExtensions.cs:10`) and its `FindTenantByApiKeyHashAsync`
  (`:57-72`) ignores that table entirely, opening a `DirectorDbContext` against
  every configured tenant in turn and querying `TblApiKeys`.
- **`GetDefaultAsync` does not simply return the configured default**; it falls
  back to `_tenants.FirstOrDefault()` when `DXSuite:DefaultTenant` does not
  match, so the default tenant is whichever happens to be first.

**No route can defend against the override at its own edge.** The middleware runs
at `Program.cs:666`, before `UseAuthentication` (`:720`), `UseAuthorization`
(`:727`) and `MapControllers` (`:782`). By the time a handler exists to ignore
the header, the hash and the lookup have already happened. The recovery handler
receives that header on every boot of every player
(section 5.6 fact 6 of the player spec), so **gating the override off is a build
precondition of this flavour**, tracked as
[DXME-194](https://radianttechnology.atlassian.net/browse/DXME-194).
**That gate is now on `dev`** (2026-09-12, merge `fd15692`), so a builder reading
this section is describing the code as it stands rather than as it needs to
become. Section 15 has the shape of what landed.

**The fallthrough fix remains out of scope here** and is raised separately; see
section 9.10. It still affects `PanelController`, `PixeraController`,
`OverrideController` and the anonymous screenshot upload routes.

### 9.10 Three platform defects ruled out of scope

All pre-existing, none specific to this flavour, all worse than anything this
flavour adds. **Burying cross-cutting platform work in a BrightSign spec is how
it ships unreviewed**, so each is its own ticket.

1. [DXME-144](https://radianttechnology.atlassian.net/browse/DXME-144) tenant
   resolution: **the default-tenant fallthrough only**, since
   [DXME-193](https://radianttechnology.atlassian.net/browse/DXME-193) sliced the
   `X-API-KEY` override out as
   [DXME-194](https://radianttechnology.atlassian.net/browse/DXME-194). The
   fallthrough is genuinely out of scope; **DXME-194 is not**, and is a build
   precondition per section 9.9.
2. [DXME-145](https://radianttechnology.atlassian.net/browse/DXME-145)
   `OverrideController.ValidateApiKey` is `return !string.IsNullOrEmpty(apiKey)`
   (`:21-25`) on anonymous routes that activate fleet-wide content overrides.
   **Out of scope and genuinely unreachable from this flavour**, which earlier
   drafts got wrong: `ValidateApiKey` is a private method called only by that
   controller's own four actions, so no route this map adds can trip it and no
   player calls `api/Override/*`. It is as serious as it reads; it is simply not
   a precondition of anything here.
3. [DXME-146](https://radianttechnology.atlassian.net/browse/DXME-146)
   `PlatformElevationMiddleware` skips `/_blazor` (`:46-56`), so its write gate
   never sees the Blazor circuit, which is how almost every write in the product
   travels; plus the global device CORS policy of
   `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()` (`Program.cs:417-420`).

**New routes on this map still carry the tenant in the path whatever DXME-144
decides**, and repopulate `ITenantContext` from it per section 9.9.

### 9.11 The web inspector

**Off in every shipped package.** Enabling is a per-device operator action from
Director, audit-logged, writing the registry flag and triggering a widget
rebuild. The app owns `@brightsign/registry`, so this costs no bootloader work
and no reboot, given a sub-second slot switch. **It auto-expires after 24 hours**,
cleared by the app, so the failure mode is a debuggable player rather than a
fleet left open. **Never a tenant-wide or package-level default.**

### 9.12 The chain that makes a local web server password matter

Stated because it is what makes "just a local web server password" feel
unimportant to whoever implements it:

> **DWS access reads the registry, the registry holds the device token, and the
> device token is that player's content credential.**

### 9.13 The player hands no credential to the content it displays

_DXME-149._ The player-side deletion is in the player half. Two consequences land
here:

1. **The spec does not acquire a disclosure section**, and section 10's host
   inventory stays an operational allowlisting list rather than becoming a
   disclosure list with a second audience. **DXSuite states plainly that an
   Engage player hands no credential to the content it displays.**
2. **Enforcing the device token's binding (section 9.1) does not reach this path
   and is not sufficient for it.** `ValidateTokenRequest` carries only
   `{ Token }`, so there is no second `DeviceId` to compare and the endpoint is a
   token-introspection oracle. More generally, binding scopes what a token can
   **read**; that defect was what a token can **assert**. The two are unrelated
   and both are necessary.

The consumer was Radiant's own internal, undocumented `WebContent` display pages.
**`FlightHub.OnConnectedAsync` (`WebContent/Hubs/FlightHub.cs:29`) calls Director
`POST /api/signage/ValidateToken` with its own admin API key**, and that check is
**conditional**: `:31` enforces only where `Director:BaseUrl` is configured, and
otherwise `NoOpDeviceTokenValidator` returns valid for everything, including no
token at all. **That default is wrong independently of this map**, because the
data is contractually restricted. The replacement credential is tracked off this
map as [DXME-151](https://radianttechnology.atlassian.net/browse/DXME-151), with
two of its six constraints actionable now regardless of instrument: nothing in a
query string, and fail closed.

---

## 10. The off-origin content host report

_DXME-140._

**An operator-facing report of the off-origin hosts DXSuite will direct a player
to.** Advisory, never enforcing. **It has no player-side consumer.**

**Requiring same-origin content for this flavour was rejected**: it would remove
a capability the applet flavour has today for reasons that are only about one
customer's firewall. A web asset on an external HTTPS host assigned to an Engage
for BrightSign player plays, and nothing filters by origin at either end.

### Derived from the authored library, never from the rendered manifest

`POST api/Signage/PlayerContent` renders what is scheduled **now**, and both
file-based web content and database assets carry `StartDate`, `EndDate` and
`StartTime`. **A host a scheduler pointed at next month is invisible to a
manifest-derived report today**, so the survey passes, the firewall request is
written, and the site breaks the day the schedule turns over. **A
manifest-derived report is a report of what already works.**

Stated explicitly because "run the manifest generator and collect the URLs" is
the obvious cheap implementation and it is the wrong one.

The derivation walks the four authored sources without the schedule filter. These
are also the four routes by which an off-origin host enters a manifest, with no
origin check at either end:

1. `TblAsset` rows of type Web (4) and Stream (5) reachable through the location's
   content channels. `AssetPath` is returned verbatim
   (`AssetContentKind.cs:33-39`).
2. `TblLocation.ExtendedData["layoutUrl"]` (`SignageController.cs:653-698`).
3. The `.json` web-content definitions on disk (`:897-915`, zone variant
   `:1422-1441`).
4. Override `ContentPath` values starting `http`, which pass through unchanged
   (`:1416-1440`).

### Off-origin is measured against a declared set, not one string

**Three site-URL notions disagree and the test must name which one it means.**
`SignageController.SiteUrl` is request-derived (`:56`), `TblTenant.SiteUrl` is
configured per tenant and authoritative outside a request, and
`FilePaths:SiteUrl` is the global fallback.

The test compares against the **tenant's** `SiteUrl`, the global fallback, and
any additional origins the tenant declares as its own. **An IP-versus-hostname
mismatch against the same server counts as off-origin and is reported**, because
on-prem that mismatch is itself something a site survey should surface rather
than normalise away.

### Scope: derive per device, aggregate to location, roll up to tenant

A firewall request is written per site, so **location is the reporting unit.** But
derivation cannot start there: **override content is assigned per display**
(`DisplayMode.Override`, `SignageController.cs:1416-1440`), so one device can
carry a host none of its neighbours do. A per-location derivation that skips
device overrides is silently incomplete in exactly the case where one screen was
hand-set.

### Standing, not point-in-time, and it costs no alerting infrastructure

A pre-survey report catches nothing after go-live, which is the failure the
prerequisites page exists to prevent. A watcher that alerts needs a notification
channel this map does not have and should not invent.

**Authoring-time warning plus first-seen dates.** When an operator saves a web
asset, a zone layout URL or an override whose host is not already in that
location's known set, **warn at the point of saving**, which catches the host the
moment it is introduced, by the person introducing it. The report then carries a
first-seen date per host so a site can diff against its last firewall request.

**Build status.** The derivation is built and walks all four sources.
**First-seen is derived rather than recorded**: it is the earliest creation date
among the rows referencing a host, which is as close as the authored data gets
without a table this section never asked for. It moves backwards if an older row
is added later, which is the honest answer and not a regression.
**The authoring-time warning is not built**, because its hooks live on the
content screens rather than in a service, and it belongs with the operator
surfaces. Until it exists the report is the only notice, and the report is pulled
rather than pushed.

### Where it lives, and what it promises

**The `playerprovisioning` tab, plus a CSV export.** That tab is already the
site-prep surface and this is site prep. The export matters because a firewall
request gets emailed to somebody with no DXSuite login.

**The prerequisites page's promise is reworded.** It currently promises "every
off-origin host the customer's own content references", and that is unkeepable:
DXSuite can enumerate the hosts it hands the player and can never enumerate what
those pages then fetch. A customer dashboard pulling fonts, map tiles and its own
API leaves DXSuite seeing one host and the firewall needing four. **The page
states that DXSuite reports every host it will _direct_ the player to, and that
each of those hosts is the customer's responsibility to trace for its own
dependencies.** **Chasing the transitive set means a crawler or a proxy log, both
far larger than this map.**

One qualification for video: **Chromium refuses a media load from an `http://`
origin inside a `file://` document** even where `fetch` to the same URL returns
200, so off-origin video must be downloaded to the card first.

---

## 11. Site prerequisites, and the time route

_DXME-136._

### The deliverable

**A prerequisites page in two variants, on-prem and hosted, plus a
technician-run check script** the field team runs from a laptop on the target
VLAN, proving each requirement in turn and printing a pass/fail sheet. The
on-prem variant is validated first, because that is the first deployment.

**A player-side self-test reporting to DXSuite on first boot was rejected as
circular**: the channel it would report over is the thing being tested, and its
only local output is the bootloader's on-card log, which nothing collects. A
laptop on the VLAN is the same network with a human attached, and this runs at
survey time, before the zero-touch install, so it does not violate the
no-technician constraint.

### The governing principle, written above the list

> **Accommodate a static misconfiguration of trust or routing. Refuse a network
> that requires a human.**

A custom root CA and an explicit proxy are set once, never change, and the
customer cannot remove them, so we accommodate. Captive portals, rotating
credentials, client isolation and idle timeouts all require somebody present,
which is the one thing this flavour is built on not having. **This sits above the
list so the next unanticipated network gets sorted without reopening a ticket.**

### What the site must provide

| | Requirement | Note |
| --- | --- | --- |
| 1 | **A route from the player network to the DXSuite host**, 443 plus 80 for the time route | For on-prem this is very nearly the whole ask. Defensible because everything the player initiates is genuinely one origin: heartbeat, pairing, the wake channel and screenshot upload all concatenate onto `CONFIG.ENDPOINT`, and the update `DownloadUrl` is built from the live request host, so it is the same host by construction |
| 2 | **No captive portal**, with MAC allowlisting named as the usual remedy | A prerequisite rather than something we cope with, because the player cannot detect a portal and cannot represent one. **Never a scripted click-through**: brittle, changes without notice, and the most alarming thing in the product to a bank's security review |
| 3 | **A dedicated SSID or wired, not guest wifi** | Guest networks apply client isolation, which removes inbound reach to the player's local DWS. A rotating PSK is worse: an unattended player cannot be handed a new one, so a routine rotation becomes a truck roll to every site simultaneously. Second independent reason: the DWS password crosses the wire in the clear on every Sentinel probe |
| 4 | **WPA2-PSK where wireless is used** | 802.1X unsupported at launch. See section 15 |
| 5 | **The customer's root CA, before install**, if they intercept TLS | Installed by the bootloader per boot |
| 6 | **A literal `host:port` if they use an explicit proxy** | **PAC files refused**: a fetch that can fail, from a host that is itself another firewall line item, evaluated by an engine we cannot inspect |
| 7 | **A local DWS position** | Three positions, section 11 below |
| 8 | **Release the players at the BSN console**, for a takeover | Section 7.4 |
| 9 | Awareness of the off-origin hosts their content references | Section 10, and it is a floor |

### What the product deliberately does not ask for

- **No WebSocket exception.** All three SignalR transports are live with
  negotiation always on and `skipNegotiation` nowhere, so a proxy refusing the
  upgrade degrades to SSE or long polling by itself. **The spec states the
  consequence rather than asking for the permission: where the socket cannot
  hold, content changes land within three minutes instead of near-instantly.**
  "Permit WebSocket upgrades" is among the most commonly refused asks a bank
  network group receives, and it is traded away once here instead of argued per
  site.
- **No bandwidth guarantee and no per-player rate cap.** The fleet-level lever is
  deployment batching. Rate-limiting one player is the wrong lever; a hundred
  players pulling a new bundle simultaneously is the actual problem, and
  staggering which players update is what solves it. The page documents the
  behaviour instead: downloads are serialised one asset at a time, retried, and
  abandoned if throughput drops below the 512 bytes/sec stall floor.
- **One plain-HTTP call to `time.brightsignnetwork.com` on a player's very first
  boot** is stated as harmless to block. BOS sets the clock from it about 16
  seconds into boot, before the autorun runs, so cloud silence is broken exactly
  once per player before our configuration lands.

### The three local DWS positions

_DXME-131 §10, softening DXME-136's hard requirement._ **Default: off in steady
state, enabled by the bootloader when it enters park.**

| Position | What it costs |
| --- | --- |
| **On always** | Nothing technical. Fastest recovery, no network dependency at the moment of failure, `BrightSignDwsDriver` works, park is visible. Offered, not required |
| **On at park** (default) | Sentinel loses local control for that device and sees it only through the heartbeat |
| **Off entirely** | Recovery is `ru`-only, so it needs the player to reach DXSuite **and** a failure mode that triggers the OS recovery flow. Anything else is a truck roll and a card swap. No Sentinel local control at all |

**Three facts made this safe to soften.** Park is exactly the state where a human
needs a way in, and it is a state the bootloader knows it has entered, and the
bootloader owns the registry. **The takeover routes do not use the local DWS at
all**: they run over the rDWS, a different service behind a different flag,
authorised by a BSN OAuth token. So the customers most likely to object, the ones
handing over an existing BSN estate, have a conversion path that never touches
it, and a player remaining enrolled with BSN keeps the rDWS as a third recovery
channel afterwards. And most of the objection is probably to the **legacy shape**
rather than the service, since the legacy autorun opened it on port 80 with no
password.

**Both preconditions of the "off entirely" position have now been measured and
the position survives them**, closed by
[DXME-155](https://radianttechnology.atlassian.net/browse/DXME-155) on
2026-09-12. Writing `networking/dwse` does not take effect without a reboot, so
the DWS decision moves to the moment the fault budget is spent rather than to
arrival at park. An `autorun.brs` absent from a populated card does reach the OS
recovery flow, so a self-demolished player keeps its safety net and parks
politely rather than reboot-cycling. Section 15 has the detail.

### The time route

**DXSuite has no player time endpoint today.** The only server-time routes are
`PixeraController.cs:71-74`, which declares both `/api/videowall/servertime` and
`/api/pixera/servertime` on one anonymous action returning `{ unixMs }` for
Pixera phase-lock, referenced by no applet. **This is entirely net-new work.**

What is decided:

- **The route lives at the same origin as the player API.** The BrightSign default
  `http://time.brightsignnetwork.com` is an outbound cloud call on every player
  and contradicts the cloud-silence requirement, and collapsing the product to
  one host to allowlist is worth real effort.
- **It answers over plain HTTP as well as HTTPS**, because **the clock and TLS are
  circular**: a player whose RTC has reset cannot validate any certificate until
  its clock is right, and cannot fetch the time over HTTPS until its clock is
  right. The payload is a timestamp and there is nothing to protect, and a fleet
  that cannot bootstrap TLS is a far worse outcome than an unauthenticated
  timestamp. Relying on the RTC alone was rejected as the status quo dressed up
  as a decision.
- **It is anonymous**, and belongs in section 9's inventory of unauthenticated
  surfaces.
- A site NTP server reachable at `ntp://` is the documented alternative where one
  already exists.
- **DXSuite must map time zones.** `roSystemTime.SetTimeZone` does not take IANA
  names; it wants BrightSign's own identifiers (`EST`, `PST`, `GMTBST`, `GMT-12`
  to `GMT+14`) or a `POSIX:` prefix plus a POSIX TZ string. **The mapping is a
  literal authored column on DXME-167's curated zone list, never derived**, and
  it holds the complete `SetTimeZone` argument including the `POSIX:` prefix, so
  nothing builds a string at either end. It does not ride this route: the time
  route is anonymous and has no device, while the zone is per device and goes out
  on the player content response as `osTimeZone`. _DXME-195 decision 32 on the
  [DXME-159](https://radianttechnology.atlassian.net/browse/DXME-159) map._
  **The three deployment literals are verified on hardware** by
  [DXME-196](https://radianttechnology.atlassian.net/browse/DXME-196):
  `POSIX:CST6CDT,M3.2.0,M11.1.0` resolves to `America/Chicago`,
  `POSIX:MST7MDT,M3.2.0,M11.1.0` to `America/Denver`, and `POSIX:MST7` to
  `America/Phoenix`. Both alternatives to the Arizona value are wrong: the short
  identifier `MST` is DST-aware and lands on Denver, and BrightSign's documented
  `POSIX:MST1` lands on `Atlantic/Cape_Verde`, six hours out.

**What the route returns is settled** and the endpoint is buildable, closed by
[DXME-154](https://radianttechnology.atlassian.net/browse/DXME-154) on
2026-09-11. There was no body format to design.

- **A 200 whose `Date` header carries the time.** The mechanism is HTTP Time
  Protocol and the body is never parsed, so serve a small body and do not attempt
  a timestamp payload. Granularity is whole seconds and `Date` is always GMT, so
  the route never carries a zone.
- **`Cache-Control: no-cache` plus `Pragma: no-cache` are mandatory.** A cached
  response keeps its original `Date`, which would pin every player behind that
  cache to a fixed past moment.
- **A path and a non-default port are both honoured**, so the route lives at a
  normal API path such as `/api/player/time` rather than occupying the origin
  root, where DXSuite's login redirect would break time sync silently.
- **A broken time route is invisible at boot.** The player's boot sync ignores
  this configuration entirely, so any health signal has to come from the periodic
  sync.

One consequence lands on the player half: only `SetTimeServer` plus `Apply()`
reaches a player's live time configuration, DWS registry writes being inert, so
the bootloader owns that step.

---

## 12. Sentinel touchpoints

These stay as they are, with one correction. **Nothing on this map takes Sentinel
work.**

**Driver selection is out of scope.** Flavour does not reach Sentinel by any path
today: `CommandRouter.cs:111` reads `descriptor.Communication.Protocol` from
Sentinel's own descriptor JSON, a value a human picks in
`DeviceModelsTab.razor:129`. _DXME-124 decision 9._

**Reflavouring does not touch Sentinel, but it says so out loud.**
`DeviceInstance.DirectorDeviceId` is a soft cross-database string keyed on the
serial, not a foreign key, so nothing enforces it, nothing cascades, and nothing
orphans when a device row is reused. But the descriptor's communication protocol
was chosen for the old flavour, and the DWS password is now per-device after
enrollment, so both may need revisiting. **That goes in the reflavour event log
as a note**, not into code. _DXME-134 decision 9._

**The one correction: Director's device row becomes the authoritative store for
the DWS credential**, in a new protected column following the
`SmtpCredentialProtector` pattern. **Sentinel's driver resolves it from Director
rather than from `CredentialsJson`**, which stays as-is for every other device
type.

This is a build decision rather than a confirmation, **because the plumbing does
not exist.** `SentinelDeviceInstance.CredentialsJson` is `varchar(max)`
deserialized straight to a plaintext `Dictionary<string,string>`
(`CommandRouter.cs:423-452`) with **no `IDataProtector`**, in a solution that
protects five other integration credentials, and **there is no write path for
that column anywhere in the repo**; rows are populated out of band. So nothing
today could store or rotate the per-device secret section 5.4 promises.
Enrollment is a Director event, so the secret is born where it is generated
rather than copied across a link that nothing enforces.

**Build note.** Built as `IDeviceDwsCredentialService` in Director, with
`CommandRouter.MapToDriverInfoAsync` overlaying Director's password onto whatever
`CredentialsJson` holds whenever `DirectorDeviceId` is set. A miss is ordinary
rather than an error, since that column is a soft cross-database string: it means
the instance is not a paired Engage player, and `CredentialsJson` stands for every
other device type, untouched.

The write path is `POST api/Update/dws-password` under the device-token scheme and
binding-checked, so a player reports only its own. The clear path is the
heartbeat: a **changed provisioning fingerprint means the card wrote its bootstrap
password back**, so the recorded rotation is now a password the player does not
have, and it is dropped rather than left for Sentinel to fail quietly against. The
device then passes back through "enrolled but not rotated", which the Devices tab
shows as a badge beside the flavour.

One deviation from section 5.5: the fingerprint is a `TblDevice` column rather
than a `DeviceExtendedData` member. Section 9.8 already described it as being "on
each device row", three consumers read it, and the re-provision check compares it
on every beat.

**To be written into the runbook rather than designed around: the DWS password
crosses the wire in the clear on every Sentinel probe.**
`BrightSignDwsDriver.cs:185-189` builds `http://{ip}:{port}`. It stops casual
access and nothing more, which gives "a dedicated SSID or wired" a second
independent reason to be a hard prerequisite.

**One unresolved interoperability question, recorded rather than solved.**
`BrightSignDwsDriver.cs:191-211` sends HTTP **Basic** with a comment claiming the
DWS accepts Basic on newer firmware, while the HD1025 on 9.1.151 was observed
answering with **digest**. Those do not interoperate, so the driver may not be
authenticating against any current player today, and nobody would necessarily
notice because an unauthenticated DWS answers anyway. It was handed to the bench
and the bench run did not report on it.

**A parked player is invisible to Director.** The bootloader idles with the log
and the update channel alive and **the app not running**, so there is no
heartbeat and a parked player is indistinguishable from an unplugged one on the
only channel Director has. That is a Sentinel problem reached over the DWS, which
is one of the reasons the DWS position matters operationally. **What Sentinel
does when the surface is absent is on the map's fog**, not decided here.

---

## 13. Runbooks

_DXME-130 E1._ **Two runbooks, drafted into this document's siblings and
graduating to the MkDocs help site when `feature/help-docs-site` lands**, because
a field technician should not need repo access.

### Runbook 1: greenfield R1 card

1. Generate the package on the `playerprovisioning` tab: tenant, family, firmware
   pin, network, time source, DWS position. Record the bootstrap DWS password,
   shown once.
2. Download the wrapper zip. **Unzip to the root of a blank FAT32 or exFAT card**,
   giving `autorun.zip` and `<family>-<version>-update_newer.bsfw` at the root.
   Label the card with the family.
3. Insert and power on. **The BOS 8 firmware path is folded in here rather than
   split out**: on a BOS 8 player the `.bsfw` applies and the player reboots; on a
   9.1 player it is an inert no-op.
4. Read the six-digit code off the player's status overlay, which also shows
   serial, model, BOS version, both versions, IP, endpoint and family.
5. Approve the pairing request in Director, choosing the flavour. Approval writes
   the flavour, revokes any other token bound to that serial, and clears a stale
   version pin.
6. **Confirm the DWS password rotated.** A device still holding the bootstrap
   password has not finished enrolling, and that is a visible device state.
   Verify through `control/dws-password`, never the registry keys.
7. **Treat the card as a credential-bearing item**, but do not reformat or
   destroy it: the player boots from it. The bootloader strips `config.json` to
   its fingerprint on the first successful apply, which is what removes the PSK.
   _DXME-153._
8. Appended section: **reflavouring an existing device.** Re-pair, accept the
   overwrite warning, and note what was cleared. Going the other way is a guarded
   clear from the Devices tab, performed last.

### Runbook 2: remote takeover

1. Obtain the customer's BSN Application credential. Confirm the access **will
   persist through removal**; if it will not, this is an R1 engagement.
2. Mint a token, **pin the network**, and confirm an rDWS `GET /info/` returns 200
   for a subject serial **and** that a serial you know is absent returns 403.
   Without both, a 403 is uninterpretable.
3. **T4 first, alone**, for any BOS 8 player. Never combined with an install. One
   player before any batch.
4. **Pre-takeover snapshot per player**: registry dump minus the `ru` credential,
   recursive file manifest, screen snapshot, B-Deploy provision record. **Treat a
   failed listing as a failure, never as an empty directory.**
5. Arm the serial in Director, which pins the versions this conversion installs.
6. **T1**: upload the file set, verify every write by read-back with retries,
   rename the incumbent `autorun.brs` to `autorun.backup` using
   `POST /files/{path}` with a **JSON** body, rename ours in, reboot.
7. **Verify the heartbeat.** Pairing is automatic where the bootstrap carried a
   pairing grant; otherwise read the code off a `POST /snapshot/` thumbnail.
8. **Release**: the customer removes the players at their BSN console. Set the
   device's BSN state to `released` in Director. Removal is invisible to the
   player, so there is nothing to check on the device.
9. **Soak.**
10. **Reclaim**, which refuses unless the device is `released`. Delete exactly
    what the manifest recorded, verifying every delete by read-back. **Never
    reformat.**
11. Rollback at any point before reclaim is two calls: rename ours aside, rename
    the incumbent back.

**Standing warnings on both runbooks:** never scrub BSN registry keys on a player;
never reformat an enrolled player's storage; re-enrolling a deployed player
destroys its card.

---

## 14. Code changes by file

_DXME-129 §12, amended by DXME-131._ New work is marked **new**; everything else
is a change to an existing file.

**Line references in this section were re-verified against `dev` at `dd5a291` on
2026-09-12.** Treat them as a convenience, not a contract: DXME-159 moved two of
them and the symbol names are what actually identify the code.

### `DXSuite.Director/Entities/`

| File | Change |
| --- | --- |
| `TblAppVersion.cs` | `ArtifactKind`, `MinBootloaderVersion` |
| `TblDevice.cs` | `ClientBootloaderVersion`, `TargetBootloaderVersionId` and its navigation |
| `TblDevicePairingRequest.cs` | persisted token-delivery fields, protected at rest |
| **new** | provisioning-package record; provisioning-authorisation record; firmware library row |
| EF migrations | all of the above, plus the `EngageBrightSign` seed. **Sequence the seed behind section 3's code deletions** |

### `DXSuite.Director/Services/`

| File | Change |
| --- | --- |
| `UpdateService.cs` | `GetTargetVersionForDeviceAsync`: `IsActive` on all three scope queries, a logging guard on the pinned branch at `:249`, kind-aware resolution, the `MinBootloaderVersion` gate, the `BLOCKED` outcome. `CheckForUpdateAsync`: both kinds with the never-both rule, `RequiresReboot` from kind, absolute `DownloadUrl` from the shared resolver. `UpdateDeviceVersionAsync`: generation and slot stamp, `UPDATE_ROLLED_BACK`. `DeleteVersionAsync`: kind-aware running-device check, blessed-version guard. `GetDeviceUpdateStatusesAsync` / `GetUpdateSummaryAsync`: `BLOCKED` and both version lines. `CreateVersionAsync`: read `manifest.json`, reject a missing manifest and a duplicate `(PlatformId, ArtifactKind, VersionNumber)` |
| `IUpdateService.cs` | `UpdateCheckResult` gains kind; known-good pin accessor |
| `BatchDeploymentService.cs` | rolled-back maps to `FAILED`; bootloader batches advance on healthy |
| `HeartbeatService.cs` | **three deletions** (fallback arm `:278-283`, `??` fallback `:305`, auto-create loop `:164-183`); `ClientBootloaderVersion` in the per-beat block; `Manufacturer` and `Model` move into it; `resources` and CEC mode into `DeviceExtendedData`; flavour-mismatch derivation |
| `DevicePairingService.cs` | replace `_pendingTokenDelivery` with row persistence; write flavour on approval; revoke other tokens bound to the serial; clear `TargetAppVersionId` and `ExtendedData` on a reflavour; write the `TblEventLog` row |
| `DeviceTokenService.cs` | enforce the `DeviceId` binding; record `LastUsedAt`; revocation evicts the auth handler's cache |
| **new** | provisioning-package generation and reissue; provisioning-authorisation arm and disarm; the recovery-handler bootstrap and courier builders; the off-origin host derivation; the artifact store over a tenant-keyed root |
| **new** | a shared player-callback URL resolver, replacing the five inline `Request.Scheme/Host` rebuilds |

### `DXSuite.Director/Controllers/`

| File | Change |
| --- | --- |
| `UpdateController.cs` | **delete** `DownloadVersion`'s rewriting and `GetVersionConfig` entirely; **new** `GET versions/{id}/artifact` under `AuthSchemes.DeviceToken`; `UploadVersion` drops the typed `VersionNumber`, takes `ArtifactKind` and reads the manifest; `ReportUpdateStatus` / `UpdateStatusReport` gain generation and slot; `CheckScope` on every admin action |
| `BatchDeploymentController.cs` | `AdminApiKey` scheme plus the deploy scope. It has no `[Authorize]` today |
| `SignageController.cs` | **new** binary screenshot route under `DeviceToken`, device from the token's binding, content-type allowlist, 5 MB cap; **delete** `Screenshot/Native`'s `?apiKey=` acceptance; `RequestFullInventory` gains the "not enrolled, pair me" field |
| `PairingController.cs` | rate limiter; `PairingRequestBody` gains platform |
| `TenantResolutionMiddleware.cs` | **precondition, not this flavour's change, and already LANDED on `dev`**: the `X-API-KEY` override block is gated on `DXSuite:TenantResolution:AllowApiKeyTenantOverride`, default false. [DXME-194](https://radianttechnology.atlassian.net/browse/DXME-194) owns it; nothing is left to do here. See section 15 |
| **new** | the recovery-URL handler, tenant in the path and repopulating `ITenantContext` from it as its first act (section 9.9), ignoring `X-API-KEY`, anonymous, route-scoped rate limiter, `Retry-After` on every response; the anonymous bootloader-artifact action; the package download; the plain-HTTP time route (section 11) |

### `DXSuite.Director/Models/`

| File | Change |
| --- | --- |
| `BrightSign/PlayerContentResponse.cs` | `BootloaderUpdate` beside `Update`; `UpdateInfo` gains kind |
| `PlayerHeartbeatModel.cs` | bind `platform` as an alias onto `ClientPlatform` |
| `PlayerPollCache.cs` | carry the second update slot |

### UI

| File | Change |
| --- | --- |
| `Components/Settings/PlayerUpdates.razor` | kind filter; both version lines per device; `BLOCKED` state; known-good badge; kind-and-platform-filtered pin picker at `:220`; `CreatedBy` on the upload path at `:563`. The AdminApiKey header defect at `:766` and `:802` disappears with section 4.6 |
| `Components/Settings/DevicePairing.razor` | flavour on the approval form; the overwrite warning |
| `Components/.../DevicesTab.razor` | render the real flavour and the mismatch badge (`:926` currently renders the literal `"Engage"`; the real name is loaded at `:927` and never displayed; `:243` is dead code); the guarded clear-flavour action in the per-device modal; rename `DeviceRow.Platform` |
| **new** `PlayerProvisioning.razor` | package form and record table, provisioning-authorisation list, takeover flow, off-origin host report with CSV export. Order 57, `Engage` flag, registered in `DirectorModule.cs`. **`dx-panel` structure, `DevicesTab.razor` as the template**. **NOT BUILT: every panel on it needs a service that does not exist yet** (package generation and reissue, authorisation arm and disarm, the takeover routes, the off-origin derivation). The tab registration lands with the screen, not before, so the sidebar never offers an entry that opens onto nothing |
| `DirectorModule.cs` | the new tab; two new `feature:` permission keys. **The keys are in** (`PermissionKeys.PlayerUpdatePublish` and `PlayerUpdateDeploy`, both Admin-only until granted, which is where the single existing gate already sat); the tab waits on the screen |

### Cross-cutting

**All four are built.**

- **The solution's first `AddRateLimiter`**, route-scoped to the recovery handler
  and the pairing routes. Pairing partitions per tenant and per IP, since a
  pairing request is anonymous and carries no identity that is not
  attacker-supplied. The **cap on outstanding unapproved requests** is separate
  and lives in `CreatePairingRequestAsync`, because a limiter bounds how fast rows
  arrive and the cap bounds how many can exist, which is the half that matters
  when the rows are what an attacker is after.
- **Protected columns** per section 9.8, following `SmtpCredentialProtector`.
  Audited by test rather than by checklist: a column holding a secret ends in
  `Protected`, and `ProtectedColumnsTests` fails on a new one that does not, so
  the next unprotected password is visible in review rather than in a breach. The
  version pins that section also lists are integer foreign keys and are stored as
  such.
- **Tokenised `/MediaAssets` URLs** emitted in the manifest, with validating
  middleware in front of the media root. See the build note in section 9.2 for
  the emit-then-enforce split.
- `Sentinel`: `BrightSignDwsDriver` resolves its credential from Director's
  device row rather than `CredentialsJson`. See the build note in section 12.

---

## 15. Open items

### The one build precondition on this half is SATISFIED

**[DXME-194](https://radianttechnology.atlassian.net/browse/DXME-194): gate the
`X-API-KEY` tenant override behind explicit configuration, default off. BUILT and
merged to `dev` on 2026-09-12**, commit `e57ab3b` under merge `fd15692`. **Nothing
on this half blocks a build any more.**

Why it existed. Not a question, a decided change that had to land **before the
recovery route goes live**, settled by
[DXME-193](https://radianttechnology.atlassian.net/browse/DXME-193) on
2026-09-12. BrightSign OS puts a 128-character `X-API-KEY` on every recovery GET,
on every boot of every player, and `TenantResolutionMiddleware` acts on that
header before any route code runs, so the recovery handler cannot decline it for
itself. Section 9.9 has the mechanism and the ordering. **Sliced out of DXME-144
so it could ship without waiting on the default-tenant redesign**, which stays out
of scope.

**What landed, in the shape a builder needs:**

- The override block requires `DXSuite:TenantResolution:AllowApiKeyTenantOverride`,
  bound from a new `TenantResolutionOptions`. **Absent means off**, so an existing
  deployment that never adds the key never reaches
  `FindTenantByApiKeyHashAsync` and never scans tenant databases.
- **Device tokens are skipped even when a deployment opts in.** A `dt_` prefix
  returns before the hash, matching `AdminApiKeyAuthHandler:51`. This matters
  because the entire Engage fleet on **both** flavours sends its device token in
  this same header on every content poll, so without the skip an opted-in
  deployment would scan every tenant database on every heartbeat.
- **An override that actually fires logs at Information**, naming the default
  tenant it displaced and the request. The off path logs nothing, because the
  whole fleet would be in it. This is how the caller list becomes observable.
- Four regression tests in
  `tests/DXSuite.Director.Tests/TenantApiKeyOverrideTests.cs`. Reaching them
  required the test project to reference `DXSuite.Host` and Host to carry
  `InternalsVisibleTo`, which is new: **Host-internal middleware was untestable
  before this**, and anything else in `DXSuite.Host/Multitenancy/` can now be
  covered the same way.

**The one thing the ticket asked for that code cannot answer.** DXME-194 wanted
the current caller list established before the gate flipped.
`FindTenantByApiKeyHashAsync` has exactly one caller in the repo, the middleware
itself, so nothing in the codebase depends on the override. Who *sends* a
matching key is a deployment fact. **The only shape that regresses is an
admin-API integration reaching a multi-tenant host with no subdomain (apex domain
or direct IP) using a non-default tenant's key**; it would now resolve to the
default tenant and fail authentication. **Single-tenant on-prem cannot regress**,
since the default tenant is the only tenant. Confirm that against real
deployments; the remedy if one exists is to set the key true for that deployment,
not to revert.

**Note on the two entries below: both have since closed.** DXME-154 closed and
settled the time route (see section 11); DXME-155 closed on 2026-09-12 and the
"off entirely" position stands. They are left here because their own resolutions
also touched this document and the detail is worth keeping; **neither blocks a
build.**

### One ticket on this half

**[DXME-154](https://radianttechnology.atlassian.net/browse/DXME-154): what
response body a DXSuite HTTP time route must serve.**

DXME-130 A7 flagged "the wire format `SetTimeServer` expects from an HTTP time
source" as unconfirmed. DXME-136 §6 recorded it resolved, but what it established
is the format of the **argument** (a scheme-prefixed string) and that the
mechanism is not BrightSign-proprietary. **What a player accepts as a response
body is recorded nowhere**, and DXSuite has no player time endpoint today, so the
route is entirely net-new with no stated contract. **Do not build the endpoint
before this closes.** Everything else about the route (section 11) is decided and
unaffected.

### One ticket on the player half

[DXME-155](https://radianttechnology.atlassian.net/browse/DXME-155), the two
"DWS off entirely" preconditions that were handed to the bench and not measured.
It could change this document: if either answers no, the "off entirely" position
in section 11 is withdrawn from the prerequisites page rather than shipped.

[DXME-153](https://radianttechnology.atlassian.net/browse/DXME-153) has closed
and confirmed the player half's call. Two things landed on this document: DXSuite
computes the config fingerprint into `config.json` rather than the player hashing
it (section 5.3), and the card-destruction mitigation in section 5.3 and runbook
1 is withdrawn as unexecutable.

### Recorded as open by the tickets themselves

Not calls, and none blocks a build.

- **How a device acquires its Location.** `TblDevice.LocationId` is written by
  exactly one screen, the Content Channels tab; pairing approval has no location
  field and the Devices tab deliberately hides it. A pre-existing gap that
  predates this map. _DXME-130 D5._
- **Whether a `.bsfw` at the card root is applied before that card's
  `autorun.brs` first runs.** If it is, a wrong-family card cannot be caught in
  software at all, and the mitigation stays operational. _DXME-121, DXME-130 A5._
- **Whether a healthy enrolled-with-BSN player's `networking/ru` is rewritten by
  BSN.** Only the bootloader's `ru` rewrite leans on it now. The bench instrument
  is spent; the trigger is the first engagement with a player live in some BSN
  organisation. Note the window was briefly open and not used: the bench LS424
  was healthy and enrolled for about twenty minutes and rebooted twice with `ru`
  present and unchanged, but nobody rewrote `ru` to see whether BSN put its own
  value back. The experiment costs one registry write and one reboot. BSP-01
  `42D987001485` is still enrolled in the Radiant network and would serve.
  _DXME-147, DXME-150._
- **Whether the DWS answers Basic or digest** (section 12). Handed to the bench
  and not reported on.
- **Whether `@brightsign/dwsconfiguration` writes the live DWS password store or
  the decoy registry keys** (section 5.4).
- **A local control surface for Sentinel**, and **park visibility** with it. On
  the map's fog as one item.
- **Collecting the bootloader's on-card log.** Rollback now self-reports over the
  normal channel, so what lives only in that log is self-demolition.
- **802.1X.** Unsupported at launch, and the platform supports EAP-TLS both in
  the registry and in a BSN `setup.json`, so "PSK only" is a product decision
  rather than a platform limit. **Trigger: the first site that answers 802.1X**,
  at which point "PSK only" quietly becomes "wired at every site". The cheapest
  instrument for "what does your wireless authenticate with" is the prerequisites
  page itself. _DXME-136, DXME-142._
- **USB-delivered updates** for players that cannot reach DXSuite.

### The map-level restatement is done

**Map decision 7 was amended on 2026-09-12** and now reads "parity with the
applet's single-display feature set plus CEC display power control on Series 5".
It adopted what the player half had said since DXME-132, so no spec text changed.
CEC does not work on Series 4 at all; the cause stays scoped out as
[DXME-148](https://radianttechnology.atlassian.net/browse/DXME-148),
non-blocking. **CEC control is in the build**, on the player half.

### One ticket adopted onto the map

**[DXME-199](https://radianttechnology.atlassian.net/browse/DXME-199): the player
reports its time zone fingerprint on the heartbeat.** Filed off-map by DXME-197
on the DXME-159 map, it had no parent and would never have reached this map's
frontier; it is now a child of
[DXME-120](https://radianttechnology.atlassian.net/browse/DXME-120).

**Nothing on this half is left to build for it.** DXME-159 already shipped the
consumer: `HeartbeatService.CaptureTimeZoneFingerprint` reads five fields out of
the heartbeat's `PropertiesJson`, `DeviceExtendedData.TimeZoneFingerprintData`
stores them, and `TimeZoneVerification.Evaluate` derives NotReported, Pending,
Verified or Mismatch on read. It is named here so that a builder working section
14 does not build it twice, and so the wire contract has one statement on each
half. The player half carries it in its section 6.

The trap worth repeating on this side: the server compares against
`TimeZoneInfo.GetUtcOffset(...).TotalHours`, **signed hours east of UTC**, while
the player's `getTimezoneOffset()` yields minutes west. The conversion is the
app's, and a sign error reads as Mismatch on every correctly configured player.
