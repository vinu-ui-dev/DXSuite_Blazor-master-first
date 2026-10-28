# DXSuite

DXSuite is a multi-tenant digital signage platform built on .NET 10 and Blazor Server. A single application deployment serves multiple customers (tenants); each tenant is resolved from the request subdomain and backed by its own databases.

## Modules

The solution is a host application plus feature modules, toggled by build flags and runtime configuration:

- Director: core signage management (locations, content, playlists, scheduling)
- CoreData and Aviation: shared and aviation-specific data services
- Sentinel: R&D device health and cloud monitoring
- Design: design and layout tooling
- Pulse: monitoring integration
- NetJets and SLC: customer-specific modules
- StyleGuide: shared UI components
- Connectors: integrations with external platforms (Ink, Q-SYS Reflect, and others)

## Prerequisites

- .NET 10 SDK
- Access to the development SQL Server (DCMDEV07) or a local equivalent
- Visual Studio 2022, Rider, or VS Code with the C# Dev Kit
- Azure CLI (az login) once secrets move to Azure Key Vault (planned)

## Getting started

1. Clone the repository:

   ```
   git clone https://github.com/Radiant-Technology-Group/DXSuite_Blazor.git
   ```

2. Configure local secrets (see "Secrets and configuration"). The committed appsettings.json is sterile; the application will not authenticate or reach a database until you provide secrets.

3. Run using the http or https launch profile (this is required; see "Local run notes"):

   ```
   dotnet run --project src/DXSuite.Host --launch-profile https
   ```

   or press F5 in Visual Studio with the "https" profile selected.

## Secrets and configuration

This repository contains no secrets. appsettings.json defines the configuration shape with empty values. Real values are supplied per developer through .NET user secrets in your own profile, and through Azure Key Vault in hosted environments (planned).

Required user-secret keys:

- ConnectionStrings: Director, CoreData, Aviation, Sentinel, Platform, Design, Pulse
- Tenants:Default:ConnectionStrings: Director, CoreData, Aviation, Sentinel, Design, Pulse
- Authentication:Providers:Microsoft:ClientSecret, Google:ClientSecret, Okta:ClientSecret

Set a value with:

```
dotnet user-secrets set "Authentication:Providers:Microsoft:ClientSecret" "<value>" --project src/DXSuite.Host
```

Obtain the actual values from the team password manager or another developer. Do not commit them.

User secrets are per developer and per machine. They are keyed by the UserSecretsId in DXSuite.Host.csproj, so every clone on your machine shares the same set.

## Local run notes (read this first)

These are the two things most likely to trip you up:

1. You must run in the Development environment. User secrets only load when ASPNETCORE_ENVIRONMENT is Development, and the http and https launch profiles set that for you. If you launch any other way (for example, running the built binary directly), the environment defaults to Production, user secrets do not load, and login fails. The application now fails fast at startup with a clear message if an enabled identity provider has an empty client secret.

2. On localhost there is no subdomain to resolve, so the request falls back to the default tenant, which is configured as "dev" in appsettings. Your platform account already has access to that tenant.

## Build

From the solution root:

```
dotnet build
```

## Deployment models

DXSuite runs in two shapes from the same codebase. The mode is determined entirely by configuration, not by separate builds.

- Standalone (on-premises): a single customer, single deployment. One application process serves one tenant backed by that customer's own databases. This is the default and is backward compatible with the original single-tenant product.
- Hosted (cloud, multi-tenant): one deployment serves many customers. Each request is resolved to a tenant by its subdomain, and each tenant has its own databases.

Configuration differences:

| Concern                  | Standalone (on-prem)                              | Hosted (cloud)                                  |
| ------------------------ | ------------------------------------------------- | ----------------------------------------------- |
| DXSuite:Role             | standalone (web + worker in one process)          | web and worker split for scale-out              |
| Tenant registry          | ConfigTenantStore (no TenantRegistry conn string) | SqlTenantStore (TenantRegistry conn string set) |
| Tenant resolution        | Falls back to DXSuite:DefaultTenant               | Resolved from the request subdomain             |
| Platform / JIT elevation | Off (no Platform DB; middleware no-ops)           | On (Platform DB; JIT elevation required)        |
| Secrets                  | User secrets, env vars, or IIS env vars           | Azure Key Vault via managed identity (planned)  |
| Media storage            | Local disk or network share (FilePaths)           | Azure Blob via IMediaStore (planned)            |
| Identity providers       | Microsoft, Google, Okta, LDAP, local accounts     | Microsoft, Google, Okta (per tenant)            |

Minimum to run standalone on-prem: set DXSuite:Role to standalone, define a single tenant under Tenants with DXSuite:DefaultTenant pointing at it, supply that tenant's connection strings, and leave ConnectionStrings:TenantRegistry and ConnectionStrings:Platform unset.

## Module composition (per-customer builds)

A core goal of DXSuite is modularity: a given customer or industry vertical can be shipped a build that contains only the modules they need, with nothing else compiled or deployed. This keeps customer-specific workflows isolated and prevents one customer's modules from appearing in another customer's environment.

- Always included (core): Shared, Connectors, Director, CoreData.
- Optional (included by default, strippable at publish time): Aviation, Pulse, StyleGuide, Sentinel, Design, NetJets, SLC.

There are two independent gates:

- Build-time gate (IncludeX MSBuild properties): set -p:IncludeX=false at publish, or set the property in a .pubxml publish profile. This removes the module's project reference and DLL from the output entirely, and removes its MODULE_X compile constant so any #if MODULE_X code is excluded. The module is not shipped at all.
- Runtime gate (Modules:X:Enabled in appsettings): hides a module at runtime without removing its code. Use this to toggle a module that is still present in the build.

Example: the NetJets build. NetJets receives a build that keeps Director plus NetJets and strips everything else:

```
dotnet publish src/DXSuite.Host -c Release -p:IncludeAviation=false -p:IncludePulse=false -p:IncludeSentinel=false -p:IncludeDesign=false -p:IncludeStyleGuide=false -p:IncludeSLC=false
```

The output contains the core projects plus DXSuite.NetJets only. The same set of flags can be expressed in a .pubxml publish profile so they do not have to be passed by hand.

Publish profiles (.pubxml) are intentionally not tracked in this repository (they are gitignored). Each customer profile is maintained by the team outside source control. When adding a new optional module, add its IncludeX property, its MODULE_X define constant, and its conditional project reference in src/DXSuite.Host/DXSuite.Host.csproj, following the existing pattern.

## Project structure

- src/: the host application and feature module projects
- db/: database scripts
  - db/provisioning: per-database baseline scripts, run when standing up a new database
  - db/migrations: incremental schema changes and their rollbacks
  - db/seed: seed data
- src/DXSuite.Host/docs/: architecture and feature documentation
- Directory.Build.props: shared build properties, including assembly versioning

## Documentation

- Architecture: src/DXSuite.Host/docs (start with multi-tenant-cloud-architecture.md)
- Database setup: db/provisioning
- Operations and compliance documents are maintained in Confluence, not in this repository

## Contributing

- Work on short-lived feature branches and open a pull request for review
- Keep code comments concise and focused
- Never commit secrets; use user secrets locally
