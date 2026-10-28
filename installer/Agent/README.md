# DXSuite Sentinel Agent — installer

Builds an MSI that installs the Sentinel agent to Program Files and registers it as an
auto-start Windows Service (`DXSuiteSentinelAgent`). Configuration is done after install
through the agent's local page at `http://localhost:5099`.

## Build

Two steps — publish the agent, then build the installer that packages it.

```powershell
# 1. Publish a self-contained, single-folder build (no .NET runtime needed on the target)
dotnet publish ..\..\src\DXSuite.SentinelGateway -c Release -r win-x64 --self-contained true

# 2. Build the MSI (restores the WiX toolset from NuGet automatically)
dotnet build .\DXSuite.SentinelAgent.Installer.wixproj -c Release
```

The MSI is produced at `installer\Agent\bin\Release\DXSuiteSentinelAgent-Setup.msi`.

To package a different publish output, override the path:
`dotnet build .\DXSuite.SentinelAgent.Installer.wixproj -p:PublishDir=<abs path ending in \>`

## Install / configure / uninstall

```powershell
# Install (elevated). The wizard installs to Program Files and starts the service.
msiexec /i DXSuiteSentinelAgent-Setup.msi

# Update in place: just install the newer MSI over the existing one — the upgrade
# stops the service, swaps the files, and restarts it. Same-version reinstalls are
# allowed (AllowSameVersionUpgrades), so dev rebuilds replace cleanly without an uninstall.
msiexec /i DXSuiteSentinelAgent-Setup.msi

# Configure: open the local page, set Broker / Tenant ID / Gateway ID (from the
# cloud Agents tab), Save, then restart the service to apply.
start http://localhost:5099
sc.exe stop DXSuiteSentinelAgent && sc.exe start DXSuiteSentinelAgent

# Uninstall (stops + removes the service and files)
msiexec /x DXSuiteSentinelAgent-Setup.msi
```

## Bulk / unattended enrollment

Generate a token in the cloud **Agents → Bulk Enroll** panel, then push the MSI silently with the
token and your tenant URL. Each machine claims its own gateway identity + key on first run (idempotent
by machine fingerprint, so re-installs reclaim the same gateway):

```powershell
msiexec /i DXSuiteSentinelAgent-Setup.msi /qn `
  ENROLLTOKEN=<token-from-bulk-enroll-panel> `
  ENROLLURL=https://<your-tenant>.dxsuite.app
```

`ENROLLTOKEN`/`ENROLLURL` are passed to the service as the `Gateway:EnrollmentToken` and
`Gateway:EnrollUrl` settings. Roles and broker come from the token/enroll response, so they need not be
specified. The token is consumed once locally (cleared from `appsettings.json` after a successful
enroll); it remains on the service command line, so prefer short-lived, revocable tokens.

## Notes

- The service runs as `LocalSystem` and binds its config page to `localhost:5099` only
  (never exposed off the box).
- Per-gateway broker credentials / mTLS (the production-hardening step) are not wired yet;
  in dev the broker is anonymous and the agent is identified by its tenant + gateway id.
- `appsettings.json` lives next to the exe in Program Files; the config page writes to it.
  Moving config to `ProgramData` is a future refinement.
