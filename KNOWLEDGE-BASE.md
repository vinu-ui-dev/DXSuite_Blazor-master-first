# DXSuite local-dev knowledge base

Fast-reference notes for running this repo locally and fixing the errors that come up
repeatedly. Read the relevant section before re-debugging something from scratch.

## 1. Running DXSuite.Host locally

```
dotnet run --project src/DXSuite.Host --launch-profile https
```

- Launch profiles (`src/DXSuite.Host/Properties/launchSettings.json`): `http` (port 5298 only),
  `https` (7112 https + 5298 http), `Local` (5298 http, `ASPNETCORE_ENVIRONMENT=Local`). Always use
  `https` for normal dev — user secrets and many auth flows assume `ASPNETCORE_ENVIRONMENT=Development`.
- Default local tenant: `dev` (see `Tenants:Default` in `appsettings.json`), Aviation/CoreData/
  Sentinel/Design/NetJets/SLC all enabled for it.
- User secrets hold the real connection strings / auth secrets (`ConnectionStrings`, `Tenants`,
  `Authentication`, `Modules`, `Features`). Check with
  `dotnet user-secrets list --project src/DXSuite.Host` (don't print values in chat).

### Symptom → cause
| Symptom | Cause / fix |
|---|---|
| `https://localhost:7112/` doesn't load | A DXSuite.Host process is already running but only bound to 5298 (started via plain `dotnet run` or the exe directly). Check real bound ports with `Get-NetTCPConnection -OwningProcess <pid>` — **launchSettings.json is not authoritative**, the actual listening process is. Kill it, restart with `--launch-profile https`. |
| Browser cert warnings | Usually not the real problem — `dotnet dev-certs https --check --trust` already passes here. The real issue is almost always "nothing listening on that port". |
| `Invoke-WebRequest` fails with "underlying connection was closed" against `https://localhost:7112` | Known-unreliable in this PowerShell 5.1 terminal for the self-signed dev cert. Use `curl.exe -k` instead. |

## 2. Build

```
dotnet build DXSuite.sln
```

- Full solution build from cold cache takes several minutes (Director/Aviation/SLC are Razor-heavy).
  `DXSuite.Director`'s `CoreCompile` alone can take 60–100s.
- Gotcha: stale incremental build cache can show misleading cascading errors (e.g. "namespace Audit
  does not exist in DXSuite.Shared" even though DXSuite.Shared built fine) — rerun with
  `--no-incremental` to confirm before treating an error as real.
- Prefer `execution_subagent` (or a backgrounded `run_in_terminal`) for builds/`dotnet run` — this
  terminal has been flaky with basic cmdlets (`Get-Content`, `Test-Path`, even `Get-Location`
  sometimes "not recognized") right after a long-running foreground command in the same shell.

## 3. Editor Bridge (Director ⇄ DXSuite SSO) — cross-repo integration

This is the most error-prone area because it spans **three separate processes/repos**, only one
of which is in this workspace. `grep_search` / `file_search` / `semantic_search` /
`vscode_listCodeUsages` **do not work outside this workspace folder** — use `list_dir` + `read_file`
manually for the other two.

| Component | Location | Runs on |
|---|---|---|
| DXSuite.Host (this repo) | `src/DXSuite.Host` | `https://localhost:7112` |
| RadiantCoreWebApi ("Director" backend) | `C:\Users\vineethes\Desktop\new git\DxSuite-BE\RadiantCoreWebApi` | `http://localhost:5290` (http profile — matches Angular's `api_url`) or `https://localhost:7001` (https profile) |
| DxSuite-UI (Director's Angular SPA) | `D:\Radiant\DxSuite-UI` | dev server; calls `environment.ts: api_url` |

Flow: Angular's `EditorBridgeService.launch(customerId, targetPath)` → POST
`RadiantCoreWebApi /api/v1/editor-auth/launch` (needs Director JWT, PKCE challenge) → full-page nav
to DXSuite `/bridge/dxsuite-callback?code=...&returnTo=...#verifier=...` → relay page POSTs to
`/bridge/dxsuite-exchange` → DXSuite calls `RadiantCoreWebApi /api/v1/editor-auth/redeem`
server-to-server → DXSuite signs the user in and sets the `dx-bridge-tenant` cookie.

RadiantCoreWebApi's EditorAuth feature lives in the **sibling project**
`RadiantCoreWebApi.Applications\EditorAuth\` (minimal-API endpoints mapped via
`app.MapEditorBridge()` in `Program.cs`, registered under `/api/v1/editor-auth/*`) — **not** in
`RadiantCoreWebApi\Controllers`.

### Required config (dev-only, in `appsettings.Development.json` on both sides — never the base `appsettings.json`)

**DXSuite.Host** (`src/DXSuite.Host/appsettings.Development.json`):
```json
"EditorBridgeClient": {
  "Enabled": true,
  "AllowLoopbackHttp": true,
  "DirectorBaseUrl": "http://localhost:5290",
  "ClientId": "director-editor",
  "ClientSecret": "<32+ char secret, must match RadiantCoreWebApi byte-for-byte>"
},
"DXSuite": { "TenantResolution": { "AllowEditorBridgeTenantOverride": true } }
```

**RadiantCoreWebApi** (`appsettings.Development.json`):
```json
"EditorBridge": {
  "Enabled": true,
  "AllowLoopbackHttp": true,
  "AllowIdentityStoreReads": true,
  "Clients": {
    "director-editor": {
      "Secret": "<same secret as DXSuite.Host ClientSecret>",
      "RedirectUri": "https://localhost:7112/bridge/dxsuite-callback",
      "CustomerIds": [ "<Customer.Customers.CustomerId guid>" ]
    }
  },
  "Customers": {
    "<same CustomerId guid>": {
      "TenantId": "dev",
      "RoleGrants": {
        "<real Identity.Roles.Name for the test account>": {
          "PermissionKeys": [ "page:flight-data-editor" ],
          "Operations": [ "flight.read" ]
        }
      }
    }
  }
}
```

### ⚠️ Every one of these is independently required — missing any one of them silently breaks the flow with no 500/exception, just a 404 or 403:

1. **`EditorBridge:Enabled` missing/false** (RadiantCoreWebApi) → `MapEditorBridge()` registers
   **zero routes**, silently. Every call to `/api/v1/editor-auth/*` → **404**.
2. **`EditorBridge:AllowIdentityStoreReads` missing/false** — this is read directly via
   `IConfiguration.GetValue<bool>("EditorBridge:AllowIdentityStoreReads")` in
   `EditorAccessResolver.cs`, it is **not** part of the `EditorBridgeOptions` class, easy to miss.
   If false, `ResolveAsync` always returns `null` regardless of CustomerId/role correctness →
   **403** on `/launch`, no matter how correct the rest of the config is.
3. **`Secret` mismatch or `RedirectUri` mismatch** between the two sides → `/redeem` or `/launch`
   fails → **401/403**. `RedirectUri` is a byte-for-byte string equality check server-side, must be
   exactly `https://<dxsuite-host>/bridge/dxsuite-callback`.
4. **Wrong `CustomerId`** — `Customer.Customers.CustomerId` is a **GUID**, not a short code like
   `"RAD"`. `Clients.<id>.CustomerIds` and `Customers` dict keys are compared **case-sensitively**
   (ordinal) — must be the lowercase-hyphenated GUID exactly as stored in SQL.
5. **Wrong `RoleGrants` key** — must be the exact `Identity.Roles.Name` the signed-in test account
   actually holds (e.g. `CustomerAdmin`, `CustomerUser`, `DIRECTOR_ADMIN`), not a guess.
6. **`DXSuite:TenantResolution:AllowEditorBridgeTenantOverride` missing/false** (DXSuite.Host) — the
   bridge sign-in can fully succeed (no 403) but every *subsequent* page on localhost (no
   subdomain) ignores the `dx-bridge-tenant` cookie and falls back to `DXSuite:DefaultTenant`
   instead. Symptom: you land on DXSuite's generic Home page instead of
   `/Flight-Data-Editor`/`/cupps-agent-screens`, and the "Active Modules" list is missing Aviation
   (and any other non-default-tenant module) even though the tenant *does* have it enabled.
7. **`IOptions<T>.Value` is cached at first access** on both sides (`MapEditorBridge()` reads it
   once, at startup) — **any config change requires a full process restart**, not a config reload.

### How to look up the real CustomerId/role instead of guessing
`sqlcmd` is installed and works against the (shared, remote!) dev DB:
```
sqlcmd -S 13.72.87.154 -U sa -P '<pwd from RadiantCoreWebApi/appsettings.json ConnectionStrings>' -d DXSuite -C -W -Q "..."
```
Schemas: `Customer.Customers`, `[Identity].[Users]`, `[Identity].[Roles]`, `[Identity].[UserRoles]`
(the `Identity` schema name needs brackets in T-SQL — it's a reserved word unquoted). Ask the user
which username/email they're logged into the Angular app as, then join `Users`→`UserRoles`→`Roles`
and `Users.CustomerId`→`Customer.Customers` to get the exact values — don't guess twice.

### Diagnostics added for next time
`EditorBridgeStore.LaunchAsync` and `EditorAccessResolver.ResolveAsync` (in RadiantCoreWebApi) now
log a `Warning` with the specific rejection reason instead of silently returning `null` → 403. Check
the RadiantCoreWebApi console output first when the bridge 403s again — it will usually name the
exact mismatched field (ClientId, CustomerId, RedirectUri, RoleGrants, etc.).

### Process ownership gotcha
RadiantCoreWebApi is often already running externally (started by the user outside the agent's own
terminals, e.g. via Visual Studio or a separate console) — find it with
`Get-NetTCPConnection -State Listen -LocalPort 5290,7001`, then either ask the user to restart it,
or (if you need live log visibility to diagnose) `Stop-Process -Id <pid> -Force` and run it yourself
with `dotnet run --launch-profile http --no-build` from `RadiantCoreWebApi/` — that gives direct
visibility into its console output instead of guessing blind.

## 4. Known pre-existing issues in this repo (not caused by any of the above)

- `RadiantCoreWebApi/appsettings.json` has a **plaintext SQL password committed**
  (`RadiantCoreWebApiContextConnection`), and its `appsettings.Development.json` does **not**
  override `ConnectionStrings`, so "local dev" RadiantCoreWebApi actually reads/writes a **shared
  remote SQL Server** (`13.72.87.154`), not a local DB. Treat any write-path testing there as
  touching shared/real customer data.
- Large pre-existing uncommitted WIP in this repo (messaging/approvals/CoreApi feature) has its own
  unrelated build errors in `DXSuite.Connectors` (missing `Microsoft.AspNetCore.SignalR.Client`
  package ref, malformed `bind-Value` razor attrs, missing usings). Confirmed unrelated to any
  NuGet/package version bump via `git diff --stat` + isolated `git stash` test — don't assume a
  fresh error here is something you caused.

## 5. NuGet security patch notes

- `System.Security.Cryptography.Xml` 10.0.10 → `11.0.0-rc.3.26522.148` across all 9 csproj files
  that referenced it (Aviation, Connectors, CoreData, Director, Host, Pulse, Sentinel, Shared,
  StyleGuide). Also fixes the transitive `System.Security.Cryptography.Pkcs` NU1903 flag.
- `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 (transitive via `Microsoft.EntityFrameworkCore.Sqlite` in
  `DXSuite.Shared`) fixed with a direct `PackageReference` override to `3.53.4.1-pre1`.
- `mcp_nuget_mcp_ser_update_package_version` errored in this env ("NuGet.Frameworks ... manifest
  definition does not match") — edit the `.csproj` files directly instead.
