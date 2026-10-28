# IANA timezone resolution on the Director runtime

Research findings for [DXME-162](https://radianttechnology.atlassian.net/browse/DXME-162),
under the map [DXME-159](https://radianttechnology.atlassian.net/browse/DXME-159)
"Timezone-correct scheduling across DXSuite". Facts only. This document does not
recommend a canonical stored format; DXME-159 decision 4 already chose IANA and
the job here is to confirm or invalidate that the choice is workable.

**Verdict: decision 4 is confirmed for the Director web host, and it is confirmed
conditionally, not unconditionally.** IANA ids resolve because the Host is built
with globalization left at its defaults and the runtime therefore loads ICU. Two
build properties and one environment variable can each silently withdraw that
capability, and one of the three already appears in this repo on a sibling
executable.

---

## 1. How the evidence was produced

A throwaway console probe lives at **`tools/tz-probe/`** in this branch. It is not
in `DXSuite.sln`, nothing references it, and it ships nowhere. It reports the
effective globalization mode by reflecting on the internal
`System.Globalization.GlobalizationMode` type rather than inferring the mode from
the switches that were requested, then calls
`TimeZoneInfo.FindSystemTimeZoneById` and the two IANA conversion helpers and
prints the exact exception type and message on failure.

It was run in four configurations on this machine. Every "Observed" line below is
probe output, not a documentation claim.

| Run | How it was produced | `GlobalizationMode.Invariant` | `GlobalizationMode.UseNls` |
|---|---|---|---|
| A. ICU (default) | `dotnet run -c Release` | False | False |
| B. Invariant | `dotnet run -c Release -p:InvariantGlobalization=true` | True | False |
| C. NLS | `DOTNET_SYSTEM_GLOBALIZATION_USENLS=1` against the default build | False | True |
| D. App-local ICU | `System.Globalization.AppLocalIcu` = `72.1` plus `Microsoft.ICU.ICU4C.Runtime` 72.1.0.3 | False | False |

Host and platform for every run:

```
RuntimeInformation.FrameworkDescription : .NET 10.0.1
Environment.Version                     : 10.0.1
OSDescription                           : Microsoft Windows 10.0.26200
SDK                                     : 10.0.101
C:\Windows\System32\icu.dll             : present
Registry "Time Zones" subkey count      : 141
```

Run A is the configuration the Director Host ships with. Section 3 sets out why.

### Caveat on what a local run can and cannot settle

This machine is Windows 11 build 26200, not a Windows Server. The runs above
settle what the **runtime configuration** does. They do not settle what a
particular server's **operating system** supplies. Section 4 separates the two
and names what would settle the part that is left.

---

## 2. Sub-question 1: does `FindSystemTimeZoneById("America/Chicago")` succeed?

**Yes in run A, and it is run A that the Host ships.** No in runs B and C.

Observed, run A (ICU):

```
OK   "America/Chicago"
       -> Id=America/Chicago  Base=-06:00:00  SupportsDST=True  Rules=2
       -> Jan offset=-06:00:00 (DST=False)  Jul offset=-05:00:00 (DST=True)
```

Observed, run B (`InvariantGlobalization=true`):

```
FAIL "America/Chicago"
       -> System.TimeZoneNotFoundException: The time zone ID 'America/Chicago' was not found on the local computer.
```

Observed, run C (`DOTNET_SYSTEM_GLOBALIZATION_USENLS=1`):

```
FAIL "America/Chicago"
       -> System.TimeZoneNotFoundException: The time zone ID 'America/Chicago' was not found on the local computer.
```

The exact failure is `System.TimeZoneNotFoundException`, message
`The time zone ID 'America/Chicago' was not found on the local computer.`, with no
inner exception. It is identical in invariant mode and in NLS mode, so the message
alone does not tell an operator which of the two conditions they are in.

Windows ids keep working in every failing configuration. In runs B and C,
`Central Standard Time`, `Mountain Standard Time`, `US Mountain Standard Time` and
`Eastern Standard Time` all resolved with correct offsets and DST rules, and
`GetSystemTimeZones().Count` stayed at 141. **The zone database is never the thing
that goes missing. Only the ability to address it by IANA id goes missing.**

`PredefinedCulturesOnly` was tested separately and is **not** a factor. Built with
`-p:PredefinedCulturesOnly=true` and ICU active, the probe reported
`GlobalizationMode.PredefinedCulturesOnly: True` and all four IANA ids still
resolved. The property appears in run B's output as True only because
`InvariantGlobalization=true` implies it.

### Property audit across the repo

Searched every `.csproj`, `Directory.Build.props`, `runtimeconfig` template,
publish profile, `.wxs`, `.ps1`, `appsettings*.json` and `web.config` on `dev` for
`InvariantGlobalization`, `PredefinedCulturesOnly`, `UseNls`,
`RuntimeHostConfigurationOption`, `AppLocalIcu`,
`DOTNET_SYSTEM_GLOBALIZATION_*` and `CLR_ICU_*`.

| File | Setting |
|---|---|
| `Directory.Build.props` | version properties only. No globalization property of any kind. |
| `src/DXSuite.Host/DXSuite.Host.csproj` | `net10.0`. No globalization property, no `RuntimeIdentifier`, no `SelfContained`, no `PublishTrimmed`. |
| the other 15 `src/` and `tests/` projects | no globalization property |
| `src/DXSuite.Host/web.config` | ANCM in-process handler, header and WebDAV removals. No `environmentVariables` block, so nothing is injected into the worker process. |
| `installer/Host/dbtool/DXSuite.Host.DbTool.csproj:25` | **`<InvariantGlobalization>true</InvariantGlobalization>`** |
| `installer/Host/wizard/DXSuite.Host.SetupWizard.csproj:28` | `<InvariantGlobalization>false</InvariantGlobalization>`, with a comment explaining that WPF crashes under invariant mode |

There are **no `.pubxml` publish profiles in the repository at all** and **no
`runtimeconfig.template.json`**. The generated `runtimeconfig.json` for a default
build therefore carries no `System.Globalization.*` key whatsoever, which is what
the probe's own default build shows:

```json
"configProperties": {
  "System.Reflection.Metadata.MetadataUpdater.IsSupported": false,
  "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization": false
}
```

Absent key means default, and the default on Windows is to try ICU first.

### The live hazard this audit turned up

`installer/Host/dbtool` runs with `InvariantGlobalization=true`. It is not the web
host, so it does not change the answer for the Director runtime. It matters for a
different reason: the DbTool is the executable that applies EF migrations and
**runs the Director seed**, and it references `DXSuite.Director`. Any timezone
resolution that ever ends up on a code path the DbTool executes will throw
`TimeZoneNotFoundException` on an IANA id, on a technician's machine, mid-install.
Nothing does that today. The property carries no comment explaining why it is
there, unlike the wizard's, which does.

---

## 3. Sub-question 2: on-prem Windows Server, DCMDEV07, and however Host is published

All three consume the **same publish output produced by the same command with no
globalization override**, so the runtime configuration is identical in all three.
What can differ between them is the operating system underneath.

**How Host is published.** Two places document it and they agree:

- `installer/Host/README.md:73` runs `dotnet publish ..\..\src\DXSuite.Host -c Release`,
  annotated "framework-dependent; the bundle installs the runtime".
- `README.md:115` has the same command plus `-p:IncludeX=false` module-stripping flags.

No `-r`, no `--self-contained`, no publish profile, no trimming. The module
inclusion flags gate `ProjectReference` items and `DefineConstants` only; none of
them touches globalization.

**On-prem Windows Server.** The Burn bundle installs the .NET 10 ASP.NET Core
Hosting Bundle and the MSI creates an IIS app pool and site, hosted in-process by
ANCM (`web.config`, `hostingModel="inprocess"`). The framework-dependent app runs
on the shared runtime the bundle installed. The stated system requirement is
**"Windows Server 2019 or later, or any Linux distribution supported by .NET 10"**
(`src/DXSuite.Host/docs/README-customer-deployment.md:22`, identical at
`README-NetJets-deployment.md:22`).

Microsoft's table of which .NET versions can load ICU on which Windows versions
gives, verbatim, ".NET 7 or later | Windows Server 2019 or later"
([Globalization and ICU](https://learn.microsoft.com/en-us/dotnet/core/extensions/globalization-icu)).
**The documented minimum OS and the ICU floor are the same line, with no margin.**
A customer on Server 2016 or 2012 R2 is outside the documented requirement but the
app will still install and run, and on that box .NET falls back to NLS, which is
run C. Confirmed reasoning, not executed here: see the caveat below.

The same page states the Windows probe order: invariant mode first, then NLS mode,
then app-local ICU, then `icu.dll` from the system directory, then `icuuc.dll` plus
`icuin.dll`, and "If any of these libraries aren't found, the runtime falls back to
using NLS APIs for globalization support." The fallback is silent. There is no
startup warning and no log line.

**DCMDEV07 (the dev box).** Same publish, same runtime, same conclusion. Its OS
build is not recorded anywhere in this repository and I could not execute on it
from here.

**Linux.** Documented as supported. On Linux `TimeZoneInfo` reads the system tzdata
directly rather than going through ICU for zone lookup, so IANA ids are the native
form there. Not executed, and not a shipped deployment today.

**What would settle the part configuration cannot.** On each target box, run the
probe in `tools/tz-probe/`, or the two-line equivalent:

```powershell
[System.Environment]::OSVersion.Version
Test-Path C:\Windows\System32\icu.dll
```

A present `icu.dll` plus a default `runtimeconfig.json` is sufficient. Reading the
mode from inside the worker process is stronger still, because it also catches a
machine-level or app-pool `DOTNET_SYSTEM_GLOBALIZATION_USENLS`, which no file in
this repository would reveal.

---

## 4. Sub-question 3: do the three deployment zones resolve, Phoenix included?

**Yes, all three, in run A.** Phoenix resolves and it is correct: no DST, no
adjustment rules, and the same offset in January and July.

Observed, run A:

```
OK   "America/Chicago"
       -> Base=-06:00:00  SupportsDST=True   Rules=2
       -> Jan offset=-06:00:00 (DST=False)  Jul offset=-05:00:00 (DST=True)
       -> Display="(UTC-06:00) Central Time (US & Canada)"
OK   "America/Denver"
       -> Base=-07:00:00  SupportsDST=True   Rules=2
       -> Jan offset=-07:00:00 (DST=False)  Jul offset=-06:00:00 (DST=True)
       -> Display="(UTC-07:00) Mountain Time (US & Canada)"
OK   "America/Phoenix"
       -> Base=-07:00:00  SupportsDST=False  Rules=0
       -> Jan offset=-07:00:00 (DST=False)  Jul offset=-07:00:00 (DST=False)
       -> Display="(UTC-07:00) Arizona"
```

`America/Phoenix` maps onto the Windows zone `US Mountain Standard Time`, which is
a distinct registry entry from `Mountain Standard Time` and carries zero adjustment
rules. The feared surprise, a zone database with no no-DST US zone, does not
occur. Arizona is representable.

Two facts worth carrying forward, both from the same output.

**The returned `Id` preserves the id that was asked for.** `FindSystemTimeZoneById("America/Chicago").Id`
is `America/Chicago`, not `Central Standard Time`. A round trip through
`TimeZoneInfo` does not quietly rewrite a stored IANA id into a Windows one.

**Phoenix's `StandardName` is `"US Mountain Standard Time"`, and its `DaylightName`
is `"US Mountain Daylight Time"` even though it has no DST.** Windows fills in a
daylight name for a zone that never uses one. Any code that builds an abbreviation
or a label from `DaylightName` will produce a daylight name for Arizona.

---

## 5. Sub-question 4: deriving a POSIX TZ string

**Who needs POSIX.** The BrightSign player. `roSystemTime.SetTimeZone` takes
BrightSign's own identifiers or a `POSIX:` prefix plus a POSIX TZ string, and it
does not take IANA names. Recorded on the map at
`Engage_BrightSign/docs/map-facts.md:440` and in the handoff specs at
`docs/spec/engage-for-brightsign.md:1083` and
`src/DXSuite.Host/docs/engage-for-brightsign-hub.md:1861`. BrightSign's own
documentation shows the prefixed form
`t.SetTimeZone("POSIX:GMT-0BST-1,M3.5.0/1:00,M10.5.0/2:00")`
([roSystemTime](https://docs.brightsign.biz/develop/rosystemtime)). That example is
Europe/London and it confirms the dialect is ordinary POSIX `TZ`, offsets
west-positive, so UTC+1 is written `-1`.

**The three strings.** Derived from the IANA source, which is the database that
owns them ([eggert/tz `northamerica`](https://raw.githubusercontent.com/eggert/tz/main/northamerica)):

```
Rule  US  2007  max  -  Mar  Sun>=8  2:00  1:00  D
Rule  US  2007  max  -  Nov  Sun>=1  2:00  0     S

Zone America/Chicago   ...  -6:00  US  C%sT
Zone America/Denver    ...  -7:00  US  M%sT
Zone America/Phoenix   ...  -7:00  -   MST
```

`Mar Sun>=8` is the second Sunday of March, so `M3.2.0`. `Nov Sun>=1` is the first
Sunday of November, so `M11.1.0`. Both at 02:00 local, which is the POSIX default
and may be omitted.

| IANA id | POSIX TZ | Fully explicit equivalent |
|---|---|---|
| `America/Chicago` | `CST6CDT,M3.2.0,M11.1.0` | `CST6CDT5,M3.2.0/2:00,M11.1.0/2:00` |
| `America/Denver` | `MST7MDT,M3.2.0,M11.1.0` | `MST7MDT6,M3.2.0/2:00,M11.1.0/2:00` |
| `America/Phoenix` | `MST7` | `MST7` |

Sent to a player these become `POSIX:CST6CDT,M3.2.0,M11.1.0`,
`POSIX:MST7MDT,M3.2.0,M11.1.0` and `POSIX:MST7`.

**These three zones do not actually need POSIX.** BrightSign's named list covers
all three, and Arizona has its own entry
([roSystemTime](https://docs.brightsign.biz/develop/rosystemtime)):

| BrightSign identifier | Documented description | Zone |
|---|---|---|
| `CST` | "US Central Time" | America/Chicago |
| `MST` | "US Mountain Time" | America/Denver |
| `MST1` | "US MT without Daylight Saving Time (Arizona)" | America/Phoenix |
| `EST` | "US Eastern Time" | America/New_York |
| `EST1` | "US ET without Daylight Saving Time (East Indiana)" | no current IANA equivalent; Indiana has observed DST since 2006 |

POSIX is the escape hatch for zones the named list does not cover, not the route
for the first deployment. Note also, from the map, that **a zone change costs a
reboot**: `SetTimeZone` returns the new identifier at once and `timezone_abbr`
follows, but the UTC offset only moves on the next boot
(`Engage_BrightSign/docs/map-facts.md:442`).

### Deriving POSIX from `TimeZoneInfo` is possible, but it cannot produce the abbreviation

The probe includes a derivation from `TimeZoneInfo.GetAdjustmentRules()`. The
transition rules and the offsets come out exactly right. The abbreviation does not.
Observed, run A:

```
America/Chicago      -> CST6CDT5,M3.2.0,M11.1.0
America/Denver       -> MST7MDT6,M3.2.0,M11.1.0
America/Phoenix      -> UMST7
```

Chicago and Denver match the canonical strings (the explicit DST offset `5` and `6`
is redundant but valid POSIX). Phoenix produced `UMST7` instead of `MST7`, because
the only name `TimeZoneInfo` offers is `StandardName`, which for Phoenix is
`"US Mountain Standard Time"`, and initialling it gives `UMST`.

**`TimeZoneInfo` on Windows carries no POSIX abbreviation.** `StandardName` and
`DaylightName` are localised prose, not `CST` and `CDT`, and they are localised, so
the initialling trick also breaks under a non-English server locale. A POSIX
emitter therefore needs an explicit abbreviation table keyed by zone, or it can
emit a synthetic placeholder and accept that BrightSign's `timezone_abbr` will read
back as nonsense. Unverified: whether a player rejects a POSIX string whose
abbreviation is not a real one. POSIX itself only requires three or more
characters, and the offsets and rules are what drive the clock.

---

## 6. Sub-question 5: are the IANA conversion helpers available without a package?

**Yes. Both exist in the framework, with no package reference of any kind.**

The probe declares zero `PackageReference` items and compiled and ran against both
methods. Observed, run A:

```
(declaring assembly: System.Private.CoreLib 10.0.0.0)
```

Microsoft's reference page states the methods apply to net-6.0 through net-11.0,
live in `System.Runtime.dll` and `netstandard.dll`, and that the API "is
exclusively supported on .NET 6 and later versions, and only when the application
utilizes the ICU library"
([TryConvertIanaIdToWindowsId](https://learn.microsoft.com/en-us/dotnet/api/system.timezoneinfo.tryconvertianaidtowindowsid)).
The same page names the two conditions under which ICU is not used: an OS without
ICU and no app-local ICU, or invariant mode or NLS mode on Windows. The
globalization page lists both methods explicitly as "ICU-dependent APIs".

Observed behaviour, run A:

```
"America/Chicago"            Iana->Windows = True "Central Standard Time"
"America/Denver"             Iana->Windows = True "Mountain Standard Time"
"America/Phoenix"            Iana->Windows = True "US Mountain Standard Time"
"America/New_York"           Iana->Windows = True "Eastern Standard Time"
"Central Standard Time"      Windows->Iana = True "America/Chicago"
"Mountain Standard Time"     Windows->Iana = True "America/Denver"
"US Mountain Standard Time"  Windows->Iana = True "America/Phoenix"
"Eastern Standard Time"      Windows->Iana = True "America/New_York"
```

Three properties of this worth recording.

**They are strictly directional.** `TryConvertIanaIdToWindowsId("Central Standard Time")`
returns `false`, and `TryConvertWindowsIdToIanaId("America/Chicago")` returns
`false`. Neither silently passes through an id that is already in the target
format. That makes the pair a usable discriminator for classifying a stored string
of unknown provenance, which is directly relevant to `tblDisplay.TimeZone` holding
three incompatible formats.

**Phoenix round-trips exactly.** `America/Phoenix` maps to `US Mountain Standard Time`
and back to `America/Phoenix`. The Arizona identity survives the round trip.

**They degrade to `false`, not to an exception.** In runs B and C every call
returned `false` with an empty out-parameter. A converter built on these helpers
fails quietly on a misconfigured box, where `FindSystemTimeZoneById` at least
throws. Whichever of the two a caller uses, it needs its own explicit handling.

There is no timezone package anywhere in the dependency set. Confirmed: no
NodaTime, no TimeZoneConverter, no `Directory.Packages.props` at all.

---

## 7. Sub-question 6: if IANA resolution were unavailable, the smallest change

Two separate cases, because two separate things can remove it.

**Case one: a build or host setting removed it.** The change is to delete the
setting. `InvariantGlobalization` and `UseNls` are the only levers, and the audit
in section 2 shows neither is set on the Host today. Cost: nothing. The risk is
not the current state, it is that a future publish profile, container image or
`-p:InvariantGlobalization=true` added for startup time would withdraw IANA
resolution with no build error and no test failure, because nothing in the repo
resolves an IANA id yet.

**Case two: the operating system has no ICU.** The change is the app-local ICU
feature, and its cost was measured, not estimated. Add to
`src/DXSuite.Host/DXSuite.Host.csproj`:

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="System.Globalization.AppLocalIcu" Value="72.1" />
</ItemGroup>
<ItemGroup>
  <PackageReference Include="Microsoft.ICU.ICU4C.Runtime" Version="72.1.0.3" />
</ItemGroup>
```

Measured on run D. It works: all four IANA ids resolved, with
`GlobalizationMode.UseNls` False and `Invariant` False.

Measured cost, from the publish output:

| Publish | ICU native payload |
|---|---|
| no `RuntimeIdentifier` (**what the Host does today**) | **177.0 MiB** across `win-x64`, `win-x86`, `win-arm64`, `linux-x64`, `linux-arm64` |
| `-r win-x64 --self-contained false` | **34.5 MiB** (`icudt72.dll` 29.7 MiB, `icuin72.dll` 2.8 MiB, `icuuc72.dll` 2.1 MiB) |

`deps.json` lists only the three `win-x64` natives, so the other 142 MiB is inert
weight in the MSI and in every deployment. Adding a `RuntimeIdentifier` to the Host
to trim it is a change to the publish shape, not a free one.

Two further costs that are not size. The pinned ICU version becomes ours to
maintain, including its CLDR and tzdata vintage, and 72.1.0.3 was built in
October 2023. And app-local ICU is checked **after** NLS mode in the Windows probe
order, so it does not rescue a box where `DOTNET_SYSTEM_GLOBALIZATION_USENLS` is
set.

**A third option, and the cheapest by far, is neither of the above.** Because
`FindSystemTimeZoneById` throws only at the moment of first use, a Director that
resolves zones lazily will fail at an arbitrary later point rather than at startup.
A single startup probe that resolves one known IANA id and fails fast, or logs
loudly, converts a silent wrong-time-forever failure into a visible one. That
costs a few lines and no bytes. It does not make IANA resolution available; it
makes its absence detectable.

---

## 8. Facts this ticket established

1. `FindSystemTimeZoneById` with an IANA id succeeds under the Host's shipped
   configuration on .NET 10, and throws
   `System.TimeZoneNotFoundException: The time zone ID 'X' was not found on the local computer.`
   under both `InvariantGlobalization=true` and `DOTNET_SYSTEM_GLOBALIZATION_USENLS=1`.
   The message is identical in both, so it does not diagnose which.
2. No `.csproj`, `Directory.Build.props`, `runtimeconfig` template, publish profile,
   `web.config` or deployment script in the repo sets `InvariantGlobalization`,
   `PredefinedCulturesOnly`, `UseNls` or any ICU switch for the Host. There are no
   `.pubxml` files in the repository.
3. `installer/Host/dbtool` does set `InvariantGlobalization=true`. It is not the web
   host, but it is the executable that runs migrations and the seed, and it
   references `DXSuite.Director`.
4. `PredefinedCulturesOnly=true` does not affect `TimeZoneInfo`. Verified directly.
5. Windows ids and the 141-entry zone database keep working in every failing
   configuration. Only IANA addressing is lost.
6. `America/Chicago`, `America/Denver` and `America/Phoenix` all resolve, with
   correct DST behaviour including Arizona's absence of it. Phoenix maps to the
   Windows zone `US Mountain Standard Time` with zero adjustment rules.
7. `FindSystemTimeZoneById` returns a `TimeZoneInfo` whose `Id` is the id that was
   requested, so a stored IANA id is not rewritten by a round trip.
8. Phoenix reports a `DaylightName` of `"US Mountain Daylight Time"` despite having
   no DST.
9. POSIX strings: `CST6CDT,M3.2.0,M11.1.0`, `MST7MDT,M3.2.0,M11.1.0`, `MST7`.
   Derived from the IANA `northamerica` source. BrightSign takes them behind a
   `POSIX:` prefix.
10. All three deployment zones also have native BrightSign identifiers: `CST`,
    `MST`, and `MST1` for Arizona. POSIX is not required for the first deployment.
11. A POSIX string cannot be fully derived from `TimeZoneInfo`, because Windows
    supplies no POSIX abbreviation. The probe's derivation produced `UMST7` for
    Phoenix instead of `MST7`.
12. `TryConvertIanaIdToWindowsId` and `TryConvertWindowsIdToIanaId` are in
    `System.Private.CoreLib`, need no package, are strictly directional, and return
    `false` rather than throwing when ICU is absent.
13. The documented minimum OS, Windows Server 2019, is exactly the ICU floor for
    .NET 7 and later. There is no margin. A box below it runs the app and falls
    back to NLS silently.
14. App-local ICU costs a measured 177.0 MiB in the Host's current publish shape,
    or 34.5 MiB with a `win-x64` RID.

## 9. Left open

- The OS build of the on-prem target, DCMDEV07, and the demo box. Not recorded in
  this repository and not reachable from here. `tools/tz-probe/` or the two
  PowerShell lines in section 3 settle it per box.
- Whether any machine-level or app-pool environment variable sets
  `DOTNET_SYSTEM_GLOBALIZATION_USENLS` on a deployed box. No file in this
  repository could show this. Only reading the mode from inside the worker process
  settles it.
- Whether a BrightSign player accepts a POSIX string carrying a synthetic
  abbreviation.
- Linux behaviour was reasoned from documentation, not executed. It is a documented
  supported platform with no deployment on it today.
