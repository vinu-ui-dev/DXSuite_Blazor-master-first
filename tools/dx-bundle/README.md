# dx-bundle

Zips a location bundle folder and pushes it to a running DXSuite instance's
`/api/location-bundles/import` endpoint. Companion tool for the Location Bundles
feature — see `src/DXSuite.Host/docs/location-bundles.md` for the full authoring guide.

## Setup

```bash
cd tools/dx-bundle
npm install
```

Create an admin API key in DXSuite (Settings > API Keys) and keep it handy, or set it
once as `DX_API_KEY` in your environment.

## Usage

One-shot push (new immutable version per manifest version):

```bash
node dx-bundle.mjs push --dir ./sample-bundle --url https://dxsuite.example --key <admin-api-key>
```

Watch mode — re-pushes on every file change, auto-bumping a `-dev.N` prerelease
suffix so each push is a new version:

```bash
node dx-bundle.mjs push --dir ./my-bundle --url https://dev.dxsuite.example --key <key> --watch
```

Locations tracking the bundle's current version pick each push up on reload.

## Local live-reload alternative (dev-override proxy)

When your DXSuite dev instance can reach your machine, skip pushing entirely:

1. Set `LocationBundles:AllowDevOverride: true` in the instance's appsettings.
2. Serve your bundle folder locally: `npx serve ./my-bundle -p 5500`.
3. As an Admin, open Configuration > Locations > Configure on your custom location and
   set Dev override URL to `http://localhost:5500`.

Admins then see the location served live from your folder (plain refresh after each
edit); all other users still get the last uploaded version. Clear the override and do
a real push before handing off.

## sample-bundle

`sample-bundle/` is a working reference that exercises the entire `dx-location.js`
SDK surface: context/settings, asset list + upload/delete, displays + schedules,
per-location storage, host navigation, override-section events, and iframe resize.
