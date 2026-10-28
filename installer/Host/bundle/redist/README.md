# Bundle redistributables

Place the **.NET 10 ASP.NET Core Hosting Bundle** installer here as
`dotnet-hosting-win.exe` before building the bundle.

Download it from <https://dotnet.microsoft.com/download/dotnet/10.0> → **ASP.NET Core
Runtime** → **Hosting Bundle** (the Windows `dotnet-hosting-10.x.y-win.exe`). Rename it to
`dotnet-hosting-win.exe`, or point the build at it explicitly:

```powershell
dotnet build .\DXSuite.Host.Bundle.wixproj -c Release -p:HostingBundleExe=<abs path to exe>
```

Embedding it (rather than downloading at install time) keeps the installer usable on
air-gapped customer servers. This file is intentionally not committed to source control.
