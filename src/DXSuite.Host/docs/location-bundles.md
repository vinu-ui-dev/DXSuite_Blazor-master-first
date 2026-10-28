# Location Bundles

Location bundles let engineers ship new location experiences into a **running** DXSuite
instance — on-prem or cloud — without rebuilding or redeploying the app. A bundle is a
zip of static HTML/JS/CSS plus a `manifest.json`. Locations of the seeded **Custom** or
**Custom Display** types render their assigned bundle inside `Location.razor`'s page
chrome as a same-origin iframe; the bundle's JavaScript talks to the host through the
`dx-location.js` SDK and the cookie-authenticated Location Content API.

Bundles are **trusted first-party code**: they run same-origin with the user's session,
so uploads are restricted to Admin/Architect users and admin API keys. This is a
professional-services delivery mechanism, not a third-party plugin system.

## Feature flag and configuration

```jsonc
"Features": { "LocationBundles": true },
"LocationBundles": {
  "AllowDevOverride": false,   // never true in production
  "MaxZipBytes": 104857600,
  "MaxUncompressedBytes": 314572800,
  "MaxFileCount": 2000
}
```

With the flag off, the config tab is hidden and every bundle endpoint returns 404.
Services stay registered; behavior is inert (same pattern as ContentPackage).

## Lifecycle

| Action | How |
|---|---|
| Install / update | Configuration > Location Bundles > Upload, or `tools/dx-bundle` push (admin API key) |
| Assign to a location | Create wizard (Custom / Custom Display tile) or Locations > Configure |
| Roll back | Location Bundles tab > Set Current on an older version (or pin a location to a version) |
| Delete | Location Bundles tab **only** — blocked while any location references the bundle. API keys cannot delete (see below); use the UI or a direct database change. |

**API keys can install but never delete.** Every management action accepts an admin API key
except `DELETE` of a bundle or a version, which requires an interactive Admin/Architect
session and returns `403` to a key. Keys are deployed to unattended players and build
agents, so a leaked one must not be able to destroy content; an import can only ever add,
because versions are immutable.

Uploaded versions are **immutable**: a re-upload requires a version bump, which is what
makes the serving endpoint's `immutable` cache headers safe. Files live in the tenant's
media store under `location-bundles/{key}/{version}/`. Open tabs pinned to a replaced
version receive `409` from the API and self-reload onto the new version.

The two location type rows are seeded by the `AddLocationBundles` EF migration and are
recognized by `ComponentTypeName = "bundle:host"` — never by `LocationTypeId`, which is
identity-generated and differs per environment.

## manifest.json

```jsonc
{
  "id": "wayfinder",              // ^[a-z0-9][a-z0-9\-_]{1,63}$ — becomes the bundle key
  "name": "Wayfinder",
  "version": "1.4.0",             // semver; -prerelease allowed (watch mode uses -dev.N)
  "entry": "index.html",          // relative path inside the zip
  "description": "Interactive wayfinding location",
  "capabilities": [               // documentation + import-time warnings only;
    "assets:read", "assets:write",//   the server enforces from the caller's real access
    "schedule:read", "schedule:write", "storage", "overrides"
  ],
  "settingsSchema": {             // renders the generic Configure form
    "fields": [
      { "key": "theme", "label": "Theme", "type": "select", "options": ["light","dark"], "default": "light" },
      { "key": "refreshSeconds", "label": "Refresh interval", "type": "number", "default": 30 }
    ]
  }
}
```

Settings field types: `text`, `number`, `boolean`, `select`, `json`. Per-location
setting values are stored on the location (`ExtendedData.bundle.settings`) and handed
to the bundle via the context endpoint — one bundle, many differently-configured
locations.

## Writing a bundle

Start from `tools/dx-bundle/sample-bundle/` — it exercises the entire SDK. Load the SDK
from the entry HTML:

```html
<script src="/_content/DXSuite.Director/scripts/dx-location.js"></script>
```

The host injects `?dxLocation={id}&dxVersion={version}` onto the iframe URL; the SDK
reads them. All API calls are same-origin, so the user's session cookie authenticates
them automatically — a bundle writes **zero** auth code, and nothing client-side is
trusted: the server re-checks location access on every request.

```js
const dx = await DX.init();
dx.context            // { location, access: {level, viewScope, upload}, user, bundle, settings }

await dx.api.getDisplays();                    // displays + constraints
await dx.api.getAssets();                      // visibility-filtered; items carry canEdit/canDelete
await dx.api.getSchedule(displayId);
await dx.api.saveSchedule(displayId, events);  // full-sync; asset ids validated server-side
await dx.api.uploadAsset(file, { constraintId }); // approval chain applies automatically
await dx.api.deleteAsset(assetId);

await dx.storage.set('key', anyJsonValue);     // per-location KV — persist state with no schema changes
await dx.storage.get('key');                   // (256 KB/value, 200 keys/location)

dx.navigate('/location/42');                   // host performs the Blazor navigation
dx.navigate('/schedule/' + displayId);         // deep-link to the built-in schedule editor
dx.resize(heightPx);                           // grow the iframe to fit content
dx.onOverrideAssetSelected(assetId => { ... }); // Custom Display: host override-section picks
dx.raw('/assets', { method: 'GET' });          // escape hatch under /api/location-content/{id}
```

### Access enforcement (server-side, always)

The Location Content API resolves the caller per request with the same rules the Blazor
UI uses (including the admin implicit-all fallback):

- Not assigned to the location → `403` on everything, including the iframe's files.
- `view` level → reads only; writes are rejected regardless of what the UI shows.
- `partial` level → uploads are auto-tagged into the user's metadata scope; edits and
  deletes outside that scope are rejected; asset listings are scope-filtered.
- Content approval on the location → API uploads enter the approval chain as Pending,
  exactly like UI uploads.

Writes additionally require the `X-DX-Bundle: 1` header (the SDK sets it) as CSRF
defense-in-depth on top of `SameSite=Lax` cookies.

## Publishing to a player

Everything above is cookie-authenticated and gated on the *user's* location access, so an
external player — a signage device, an Omnivex Moxie script, an integration — gets `401`.
Bundles that need to drive a player publish through the **publish channel** instead.

A bundle publishes by writing to a storage key prefixed **`public.`**:

```js
await dx.storage.set('public.wall', resolvedDocument);   // player-readable
await dx.storage.set('presets', workingDrafts);          // stays private
```

Publication is opt-in by the writer. There is no manifest declaration and no host change
per bundle: a new bundle publishes to a new `public.*` key and the endpoint below serves it
immediately. The host never interprets the document — its shape is entirely the bundle's.

> **Anything written under a `public.` key is readable by any API key holding
> `signage:read`.** Treat those keys as published, not merely exposed. Keep credentials,
> internal identifiers, and draft state in ordinary (private) keys.

The prefix uses a dot rather than a slash so a key stays inside a single route segment —
`storage/{key}` is one segment and the SDK URL-encodes the key, so a `/` would round-trip
inconsistently.

### Reading it

```
GET /api/player/bundle-state/{locationId}/{key}
Headers: X-API-KEY: <key>          # scope: signage:read
```

Returns the stored JSON verbatim with a content-hash `ETag`; send `If-None-Match` on
subsequent polls and a `304` costs one indexed row read. Non-public keys return `404`
rather than `403`, so a private key's existence is never confirmed. Keys restricted to a
location (`tblApiKey.LocationId`) may only read that location; keys created through
Settings > API Keys are unrestricted.

### Wall zones (rotator shell)

For wall-controller bundles specifically, a player can also frame a ready-made shell per
zone instead of driving the geometry itself:

```
GET /player/wall-zone/{locationId}/{zoneIndex}       → HTML shell for one zone
GET /api/player/wall-zone/{locationId}/{zoneIndex}   → the same payload as JSON
```

The shell reads `public.wall`, renders that zone's dashboard URLs, rotates them on the
zone's `dwellSeconds`, and polls every 30 s — re-rendering only when the document's
`revision` changes. A webzone URL is therefore set **once**: preset activation, dashboard
edits, and dwell changes all arrive through the poll with no player-side work. The last
payload is cached in `localStorage` so a player that reboots during an outage resumes on
last-known content.

Three properties of the shell are deliberate and worth knowing before you rely on it:

- **Both routes are anonymous.** A CEF webzone performs a plain browser navigation and
  cannot attach a header, which is why the existing `/videowall/*` player routes are
  anonymous too. Anyone who can reach the server and knows the location id can read that
  wall's published URLs — the same exposure as `/api/videowall/allocation/{id}`. Where that
  is unacceptable, use the API-key endpoint above and drive the zones from a script.
- **Every dashboard in a zone stays mounted**; rotation only toggles opacity. Rebuilding
  the iframe each cycle would re-run each dashboard's login redirect, so SSO-protected
  dashboards would re-authenticate on every rotation. The cost is N dashboards live at
  once — keep zones to a handful.
- **The iframes are not sandboxed.** These are customer-authored trusted URLs, and
  sandboxing breaks the scripting and storage most real dashboards need.

Note that many third-party dashboards refuse to be framed at all (`X-Frame-Options: DENY`
or a `frame-ancestors` CSP), and SSO redirects inside a frame frequently fail. Test the
real URLs before committing a zone to the shell; anything that refuses must be driven by a
script against `/api/player/bundle-state`.

## Development loop

Two supported inner loops — see `tools/dx-bundle/README.md` for details:

1. **Dev-override proxy** (instance can reach your machine): serve the bundle folder
   locally, set the location's Dev override URL (Configure modal, Admin-only, requires
   `LocationBundles:AllowDevOverride`). Admins get your live files on every refresh;
   everyone else keeps the stored version. Triple-gated so it cannot engage in prod.
2. **`dx-bundle push --watch`** (works against any instance): auto-zips and re-uploads
   on save, bumping a `-dev.N` prerelease version each push.

## Known limitations (v1)

- Content-package export of a custom location carries the `bundle` assignment by value;
  the target environment must have the bundle uploaded separately or the location shows
  its "No bundle assigned" panel until it does.
- Asset ingest mirrors the current `MediaAssets/{locationId}` disk layout (shared with
  the Blazor uploader via `AssetIngestService`); when media storage migrates fully to
  `IMediaStore`, only that service changes.
- `MediaAssetUploader.razor` still contains its original inline persistence path;
  re-pointing it at `AssetIngestService` is planned once the upload UI regression pass
  can run against a migrated dev database.
