# Video Wall URL Enhancements

Feature branch: `feature/video-wall-url-enhancements`

## Problem

The Pixera layer URLs (`/pixera/layer?locationId=X&type=...`) were designed for
Pixera media servers, where each layer (background, mask, counter, logo) runs as
its own full-canvas CEF webzone and the player can map arbitrary canvas
resolutions (e.g. 44160×648) onto physical outputs.

Two new requirements break those assumptions:

1. **Granular re-rendering.** The shells rebuild their entire DOM whenever any
   allocation data changes. A mask re-upload reloads the background page's
   video; a single counter status change reloads every counter. Signatures are
   all-fields, and rendering is clear-and-rebuild.
2. **Moxie (and similar) players.** Omnivex Moxie cannot abstract a wrapped
   canvas: each player output is a fixed 4K 16:9 surface, and wide canvases are
   covered by *wrapping* the canvas row into horizontal strips stacked
   vertically within each output (e.g. 3 strips of 3840×648 per 4K output,
   continuing across outputs). Moxie players are also weaker than Pixera
   servers: one Chromium instance per layer per row (12–16 per wall) is too
   heavy, so layers must be consolidated into one page per output.

## Design

### URL contract (one endpoint, parameter-driven)

```
/videowall/layer?locationId=3214&layers=background,mask,counter,logo&left=11520&wrap=3840x648&outh=2160
```

`/videowall` (with `/api/videowall`) is the canonical prefix; `/pixera` and
`/api/pixera` are permanent legacy aliases for every route. Deployed players
carry hand-entered `/pixera` URLs in their Pixera project files / Moxie zone
configs, which Director cannot update remotely, so the aliases must never be
removed. The served shells self-reference whichever prefix loaded them (API
fetches, counter-content iframe srcs), so a legacy player's cached state never
mixes prefixes; the localStorage cache keys (`px-l-`/`px-c-`) are shared by
both prefixes.

- `layers=` — comma-separated whitelist, rendered stacked in fixed z-order
  (background zones < airline backgrounds < masks < counters < logos).
  `type=` remains as a single-layer alias; existing Pixera URLs keep working.
- `left=` — canvas X where this output/tile begins (unchanged meaning).
- `wrap=WxH` — strip width × strip height. Presence selects wrap placement.
- `outh=` — output height (default 2160). Strips per output =
  floor(outh / stripH). Explicit rather than derived from the viewport because
  embedded CEF viewports are unreliable (Moxie reported 1258×638 for a 4K zone);
  the shell compares declared vs actual viewport and applies a uniform CSS
  scale when they differ.
- `debug=1` — existing on-screen diagnostic overlay.

### Placement modes

Rendering is split into "what elements exist" (per layer, in canvas
coordinates) and "how a canvas rect becomes DOM":

- **Flat** (default, Pixera): one container per element at
  `(canvasX - left, canvasY)`. Today's behavior.
- **Wrap** (Moxie): each output covers `stripsPerOutput × stripW` canvas px.
  An element's canvas rect is intersected with each strip range; each
  intersection emits a clip container at `(canvasX mod stripW, strip × stripH)`
  with the inner content offset by the amount of the element that precedes the
  strip. Elements spanning a strip boundary render as two (rarely more) slices —
  the same iframe/img src instantiated per slice with different inner offsets.
  **Video slices are full decodes**: small videos (counters) crossing a boundary
  render per-slice copies, which is cheap but can drift; wide background zone
  videos spanning multiple strips log a debug warning and should use pre-wrapped
  per-output renditions (below) instead.

### Keyed reconciliation (fixes cross-type and whole-page re-renders)

Both shells replace clear-and-rebuild with keyed diffing. Each element carries
`data-key` (stable identity) and `data-sig` (content signature). On every poll:
unchanged elements are untouched (videos keep playing, iframes keep state),
changed ones are rebuilt in place, removed ones deleted, new ones appended.
Signatures contain only fields the element actually renders, which makes them
inherently type-scoped — mask versions no longer appear in any background
element's signature and vice versa.

Keys: background zone tile `bz:{zoneIdx}:{tile}`, cluster region
`cl:{startIndex}`, logo zone `logo:{idx}`, counter item `d:{displayId}`
(`CounterDisplayItem` gains `DisplayId`).

### Derived per-output renditions (wrap mode backgrounds)

Background videos are authored wall-native (e.g. four 11040×648 zone videos, or
a looping tile), never output-aligned. A derivation job converts the wall's
virtual background row into per-output stacked-strip videos:

- Input: the ordered tile sequence from the same model
  `BuildBackgroundZonesAsync` computes (zone lefts/widths, loop counts, source
  assets). For each output strip, crop the intersecting tile segment(s) —
  loop tiles reuse the same input file at wrapped offsets ("fill from the
  beginning of the video, continue on the next") — `hstack` multi-source
  strips, `vstack` the strips.
- Output: `Derived/{basename}_w{stripW}x{stripH}_out{N}.webm` beside the source
  (same convention as `Thumbnails/`). Geometry in the filename keys the cache;
  different walls/geometries never collide.
- Cache key: (source asset ids + mtimes, geometry). Derivation is idempotent —
  it skips when current — and runs once per uploaded source set, never at
  schedule-switch time. Day-parting (day/night events on the background
  display) just resolves a different asset whose renditions already exist.
- Trigger: assignment-driven (scheduling a video onto a background-role display
  of a wrap-enabled wall enqueues missing renditions) plus a periodic
  self-heal sweep. VP9/WebM output (Moxie's Chromium has no H.264).
- Until renditions exist, wrap-mode backgrounds render nothing (debug overlay
  reports "derivation pending"); deliberately no fallback to slicing the
  source video.

### UI (CreateLayoutModal, reached from LocationsTab → Configure)

A "Wrapped outputs" section on the VideoWall configuration
(`VideoWallConfiguration` in `TblLocation.ExtendedData`):

- Enable toggle + output resolution (default 3840×2160). Strip height comes
  from the background row's display height; output count from canvas width.
- Computed per-output URLs with copy buttons (paste into Moxie zone config).
- Rendition status per background asset per output (pending/ready/failed).

## Phases

1. **Shell refactor** — keyed reconciliation in both shells, type-scoped
   signatures, `DisplayId` on counter items. Fixes the cross-type and
   whole-page re-render bugs on its own.
2. **`layers=` consolidation** — multi-layer rendering in one page.
3. **Wrap placement engine** — strip math, element slicing, viewport scale
   guard, video-slice refusal.
4. **Derivation service + UI** — ffmpeg strip stitching, derived-file cache,
   wrapped-outputs section in CreateLayoutModal.

## Offline resilience (DXME-83)

The wall shells were always outage-tolerant mid-run (poll failures are ignored;
allocation/counter data persists in localStorage), but a player that (re)booted
while the server was down failed its initial navigation and composited a browser
error page until something re-navigated it. A service worker closes that window.

- Served at `/videowall/sw.js` and `/pixera/sw.js` (one prefix-neutral script;
  scope is bound to the script URL directory, hence both routes). Registered by
  the layer and counter shells via `PAGE_BASE`. Requires a secure origin (HTTPS
  or localhost) and a persistent browser profile — both already true for
  deployed players.
- Pages, html-asset iframes, and API polls: network-first with an 8 s
  time-to-first-byte timeout, falling back to the last cached copy on network
  failure or 5xx (4xx is returned as-is so a deleted wall doesn't play forever).
  Normal operation is unchanged.
- `/servertime` is never intercepted: a cached clock sample would silently skew
  the video phase-lock. Offline, shells fall back to the client clock as before.
- Media: after every successful poll the shells post the full asset-URL list for
  their output to the worker (`dx-precache`), which downloads missing files
  sequentially into CacheStorage and evicts anything no longer referenced by any
  page's manifest. Offline playback slices cached files into 206 responses
  because video elements request byte ranges.
- The worker also caches its own client page on the first message it receives.
  The navigation that installs a worker finishes before that worker controls the
  page, so without this the shell HTML is absent from the cache until some later
  reload — meaning a player that boots once and then runs for weeks would still
  error-page on its next boot into an outage. One healthy load is now enough.
- Known gap: constraint-slideshow media inside counter-content iframes is not
  precached (the iframe page itself is). A player whose profile never loaded the
  wall at all (new machine, wiped cache) still errors on first boot during an
  outage — healed after the fact by DXME-84's heartbeat-gated URL re-assertion.

### Player support

Nothing here is Pixera-specific: the worker is registered by the shell, so any
player whose renderer supports service workers gets it. Two conditions decide it:

- **Secure origin.** Service workers require HTTPS (or localhost). A wall URL
  entered as plain `http://director/...` silently gets no offline protection on
  any platform. This is the single most common reason it will not engage.
- **Chromium-era renderer.** Pixera CEF, Moxie, SignageOS/Engage, and Windows
  players hosting WebView2 or CEF all qualify. A legacy IE/Trident embedded
  browser does not, and degrades to the pre-existing behavior (localStorage
  cold-start rendering, no cached shell).

Load a wall URL with `&debug=1` on the player to see which case applies: the
overlay reports `sw registered — offline cache active`, or `sw unsupported`
with the reason. DXME-84's healing is Pixera-only by contrast — it drives the
Pixera JSON-RPC API, and has no equivalent for other player platforms.
