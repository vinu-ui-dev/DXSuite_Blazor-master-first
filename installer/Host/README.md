# DXSuite Host — on-premises install wizard

Builds a single setup `.exe` (`DXSuiteHost-Setup.exe`) that automates the on-prem IIS
deployment that is otherwise done by hand (create site/app pool, copy published files, set
`appsettings`, set up the database). The technician runs one file and is walked through it.

## What it does

The Burn bundle runs three steps in order:

1. **Setup wizard** — collects database, authentication, file-path, and module/feature
   configuration, with a live **Test & detect state** button. Writes a config JSON.
2. **.NET 10 ASP.NET Core Hosting Bundle** — installed silently if not already present
   (required for IIS to host the app).
3. **Host MSI** — lays the published app into the install folder, creates the IIS
   application pool + website, writes `appsettings.Production.json`, and brings the
   **Director** database to the current schema.

## Database: EF migrations only (the contract)

On-prem provisions **only the Director database**. The schema is applied with
`DirectorDbContext.Database.Migrate()` — the migrations are compiled into the
`DXSuite.Director` assembly, so no .NET SDK is needed on the target box. The installer
**never** calls `EnsureCreated()` and **never** runs the old full-schema provisioning SQL.
See `db/schema-migration-strategy.md`.

The DB tool detects state from `__EFMigrationsHistory` + user tables and acts accordingly:

| State | Meaning | Action |
|-------|---------|--------|
| Fresh | empty / no database | `Migrate()` builds all tables and records the baseline |
| ExistingUntracked | live pre-migrations schema (the existing on-prem DBs) | **operator must confirm** it matches the baseline → stamp baseline id → `Migrate()` |
| Tracked | migration history present | `Migrate()` applies only pending migrations |

Two SQL credential sets are collected: an **admin** account (CREATE DATABASE + DDL for
migrations) and the **application** account (db_datareader + db_datawriter) that goes into
the runtime connection string.

## Architecture (why it's shaped this way)

A plain MSI can't chain another installer (the Hosting Bundle), so the outer artifact is a
**WiX Burn bundle**. The wizard is a **self-contained .NET 10 WPF exe** (carries its own
runtime; references only `Microsoft.Data.SqlClient`) so it runs before any runtime is
guaranteed. The **DB tool** is a separate framework-dependent console exe that references
`DXSuite.Director` for the compiled-in migrations; it runs as the MSI's deferred custom
action after files are laid down and the Hosting Bundle is present. Keeping the migrate code
in the DB tool (not the web host) preserves the rule that the runtime app never migrates.

```
DXSuiteHost-Setup.exe                      bundle/  (WiX Burn)
├── ExePackage  SetupWizard                wizard/  (self-contained WPF) → writes config JSON
├── ExePackage  AspNetCoreHostingBundle    bundle/redist/dotnet-hosting-win.exe
└── MsiPackage  DXSuiteHost.msi            ./       (WiX MSI)
    ├── files        harvested from the Host publish output
    ├── IIS          app pool + website + binding   (WixToolset.Iis.wixext)
    ├── db tool      dbtool/  (references DXSuite.Director)
    ├── seed         db/scripts/director-seed.sql
    └── deferred CA  DXSuite.Host.DbTool.exe install → appsettings + Migrate + seed
```

| Path | What it is |
|------|------------|
| `shared/` | The wizard↔install-time JSON contract + connection-string + DB-state helpers (linked into both exes). |
| `wizard/` | `DXSuite.Host.SetupWizard` — the WPF wizard. |
| `dbtool/` | `DXSuite.Host.DbTool` — headless provisioner (`detect`/`test-connection`/`apply-settings`/`provision`/`install`). |
| `Package.wxs`, `DXSuite.Host.Installer.wixproj` | The MSI. |
| `bundle/` | The Burn bundle (+ `redist/` for the Hosting Bundle payload). |

## Build

```powershell
# 1. Publish the Host app (framework-dependent; the bundle installs the runtime).
dotnet publish ..\..\src\DXSuite.Host -c Release

# 2. Publish the DB tool (framework-dependent) and the wizard (self-contained, single file).
dotnet publish .\dbtool\DXSuite.Host.DbTool.csproj -c Release
dotnet publish .\wizard\DXSuite.Host.SetupWizard.csproj -c Release

# 3. Build the MSI.
dotnet build .\DXSuite.Host.Installer.wixproj -c Release        # → bin\Release\en-US\DXSuiteHost.msi

# 4. Supply the Hosting Bundle (see bundle\redist\README.md), then build the bundle.
dotnet build .\bundle\DXSuite.Host.Bundle.wixproj -c Release    # → bundle\bin\Release\DXSuiteHost-Setup.exe
```

## Install / uninstall

```powershell
# Install (elevated): bundle shows license + install-folder options, runs the wizard,
# installs the Hosting Bundle, then the MSI.
DXSuiteHost-Setup.exe

# Silent install
DXSuiteHost-Setup.exe /quiet

# Uninstall (removes the site + files; does NOT drop the database)
DXSuiteHost-Setup.exe /uninstall
```

## Validate on a real target

Everything here builds clean, but these behaviours can only be verified on a Windows Server
with IIS + SQL Server — treat the first install on a clean VM as the acceptance test:

- IIS app pool / website creation and ASP.NET Core Module (ANCM) hosting.
- The Hosting Bundle install step (currently runs unconditionally; the official installer is
  idempotent — adjust `DetectCondition` in `bundle/Bundle.wxs` if you prefer registry-gated
  detection for a specific patch level).
- Live `Migrate()` against the Director database and the three state paths above.
- Optional: switch the app pool to **No Managed Code** post-install (the MSI leaves it at the
  default v4.0; ASP.NET Core is hosted by ANCM either way).

`appsettings.Production.json` is written by the deferred custom action and is **not** removed
on uninstall (it can contain secrets) — delete it by hand if required.
