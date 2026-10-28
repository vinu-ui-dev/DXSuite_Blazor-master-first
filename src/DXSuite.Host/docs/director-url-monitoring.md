# Director URLs Monitoring

Detects silently-dead players consuming Director-hosted URLs (video walls, future
bundle pages) and unreachable Pixera servers, and delivers alerts to admin
notification bells. Missing URLs can also reach Pulse through Sentinel (see
[Pulse](#pulse)).

## How it works

```
player URL (&id=...) ──> heartbeat middleware ──> tblDirectorUrl (Director DB)
                                                        │
Pixera connectors  ──> API probe (60s) ──┐              │  sweeper (30s)
                                         ▼              ▼
                              DirectorMonitoringService state machine
                                         │ one alert per transition
                                         ▼
                              NotificationAlertSink (bell)
```

## Tagging a player URL

Append `&id=<your-id>` to any hand-entered player URL (letters, digits, `._-`,
max 64 chars). Example:

```
http://director/videowall/layer?locationId=3214&layers=background,mask,counter,logo&left=0&id=MOXIE-01-OUT2
```

The shell threads the id into its polling fetches, so every 30-second poll counts
as a heartbeat. **One id is one row** — the page document and all of its API
polls beat the same entry (the displayed URL is the page URL you entered), so
use a distinct id for each output/URL you want independently monitored. The row
appears under **Configuration → Director URLs** after its first poll; enable
**Monitored** there and optionally link it to a device from the device list so
alerts name hardware. `Interval` is the absence threshold (default 120 s ≈ 4
missed polls; minimum 60 s).

Monitored route prefixes: `/videowall`, `/pixera`, `/api/videowall`,
`/api/pixera` (extend `UrlHeartbeatMiddleware.MonitoredPrefixes` for future URL
surfaces — no other wiring is needed).

## Pixera probing

Enabled Pixera cloud connectors are probed every 60 s (`getApiRevision`
liveness + `Pixera.LiveSystems` per-system states). Successful probes stamp the
synced devices' heartbeats in the device list; reachability transitions raise
device alerts. Run **Sync Devices** on the Pixera connector once so its
LiveSystems machines exist as devices. Per-system performance JSON is logged
(`Pixera[...] perf`) roughly every 10 minutes for shape discovery.

Alert timing notes: the sweeper starts 60 s after app start and suppresses
*missing* transitions for a further 180 s so a deploy doesn't alert-storm
(recoveries are never suppressed). Connector reachability state is in-memory, so
a Pixera that is still down after a Director restart re-alerts once.

## Web layer healing (DXME-84)

A Pixera web layer that navigates to a wall URL while Director is down sits on
a browser error page forever — CEF never retries on its own. Each successful
connector probe therefore runs a healing pass:

1. Collect monitored URLs currently in the **missing** state. Anything alive
   resumes heartbeating within one poll interval of Director recovering, so a
   still-missing monitor is, by elimination, a page that failed its navigation.
2. Walk the Pixera project's resources (`Pixera.Resources`), keeping web
   resources whose URL is a wall path (`/videowall/` or `/pixera/`) carrying an
   `id=` monitor parameter. URLs without an `id=` are skipped, never guessed at.
3. For matches whose id is in the missing set, re-assert the resource's own URL
   via `Resource.setUrl`, forcing a fresh navigation. Kicks are rate-limited to
   one per 15 minutes per connector + monitor.

Healthy pages are never re-navigated, so live walls see no reload. For this to
cover a wall, every Pixera web layer URL must carry an `id=` (see "Tagging a
player URL"). Complements DXME-83's service worker: the worker keeps rebooting
players rendering cached content during an outage; healing catches players with
no installed worker (first boot, wiped CEF cache). Requires a real Pixera to
verify end-to-end — the qualification rule is unit-pinned, the RPC path is not.

## Pulse

Director monitoring no longer writes to Pulse itself. Missing URLs reach Pulse
through Sentinel's Pulse reporting, once a URL monitor is attached to a
reporting device on Sentinel's Monitored Devices tab. See
[Sentinel Pulse reporting](sentinel-pulse-reporting.md) for setup, the grants
the Pulse login needs, and how to review Pulse devices the retired
`PulseAlertSink` created.
