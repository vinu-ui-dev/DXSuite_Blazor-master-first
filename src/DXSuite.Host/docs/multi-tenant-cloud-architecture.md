# Multi-Tenant Cloud Architecture for DXSuite

## Context

DXSuite is currently a single-tenant on-premises application. The goal is to convert it into a cloud-hostable, multi-tenant platform where:
- A **single application deployment** serves multiple customers
- Each customer gets a **unique subdomain** (e.g., `acme.dxsuite.cloud`)
- Each customer has their own **isolated SQL database** (not shared schema)
- **Services run per-tenant** to isolate performance and state
- **Authentication is configurable per tenant** (unique Azure AD/OIDC app registrations per customer)

This is an architecture plan — not an implementation plan. It defines the layers, patterns, and migration path.

---

## Architecture Overview

```
                    ┌─────────────────────────────────────┐
                    │         DNS / Reverse Proxy         │
                    │   acme.dxsuite.cloud → App Service  │
                    │   beta.dxsuite.cloud → App Service  │
                    └──────────────┬──────────────────────┘
                                   │
                    ┌──────────────▼──────────────────────┐
                    │      DXSuite.Host (single deploy)   │
                    │                                     │
                    │  ┌──────────────────────────────┐   │
                    │  │  Tenant Resolution Middleware│   │
                    │  │  (subdomain → TenantContext) │   │
                    │  └──────────────┬───────────────┘   │
                    │                 │                   │
                    │  ┌──────────────▼───────────────┐   │
                    │  │     TenantContext (scoped)   │   │
                    │  │  - TenantId, CustomerCode    │   │
                    │  │  - ConnectionStrings         │   │
                    │  │  - AuthConfig (OIDC settings)│   │
                    │  │  - FeatureFlags, Modules     │   │
                    │  └──────────────┬───────────────┘   │
                    │                 │                   │
                    │  ┌──────────────▼───────────────┐   │
                    │  │   Tenant-Scoped DbContexts   │   │
                    │  │   (connection per tenant)    │   │
                    │  └──────────────────────────────┘   │
                    │                                     │
                    │  ┌──────────────────────────────┐   │
                    │  │   Per-Tenant Background Svcs │   │
                    │  │   (isolated execution loops) │   │
                    │  └──────────────────────────────┘   │
                    └─────────────────────────────────────┘
                          │              │             │
                    ┌─────▼────┐  ┌─────▼────┐  ┌────▼─────┐
                    │ Acme DB  │  │ Beta DB  │  │ Gamma DB │
                    │(SQL Srv) │  │(SQL Srv) │  │(SQL Srv) │
                    └──────────┘  └──────────┘  └──────────┘
```

---

## Layer 1: Tenant Registry (New — Central Metadata Store)

A lightweight **central database** (or config file for dev) that maps tenants to their configuration. This is the only shared database.

### Option A: SQL Table (recommended for production)
```
DXSuite_TenantRegistry database
├── TblTenant
│   ├── TenantId (GUID, PK)
│   ├── Subdomain (varchar, unique) — "acme", "beta"
│   ├── CustomerCode (varchar) — "ACME", "BETA"
│   ├── DisplayName — "Acme Corp"
│   ├── IsEnabled (bit)
│   ├── ConnectionString_Director (encrypted)
│   ├── ConnectionString_Sentinel (encrypted, nullable)
│   ├── ConnectionString_CoreData (encrypted, nullable)
│   ├── AuthProvider — "microsoft", "google", "okta", "local"
│   ├── AuthClientId — OIDC client ID
│   ├── AuthClientSecret (encrypted)
│   ├── AuthTenantId — Azure AD tenant ID (nullable)
│   ├── AuthAuthority — OIDC authority URL
│   ├── EnabledModules (JSON) — ["Director", "CoreData", "Aviation"]
│   ├── FeatureFlags (JSON) — {"Engage": true, "Wayfinding": false}
│   ├── FilePaths_MediaRoot — tenant-specific media storage path
│   ├── SmtpConfig (JSON, encrypted)
│   ├── CreatedDate, LastEditDate
│   └── MaxUsers (int, nullable) — license limit
```

### Option B: appsettings.json (dev/small scale)
```json
"Tenants": {
  "acme": {
    "CustomerCode": "ACME",
    "DisplayName": "Acme Corp",
    "ConnectionStrings": {
      "Director": "data source=...;initial catalog=DXSuite_ACME;..."
    },
    "Auth": {
      "Provider": "microsoft",
      "ClientId": "...",
      "TenantId": "..."
    },
    "Modules": { "Director": true, "Aviation": true },
    "Features": { "Engage": true }
  }
}
```

**Recommendation:** Start with Option B for dev, build toward Option A for production. Abstract behind an `ITenantStore` interface so the backing store is swappable.

---

## Layer 2: Tenant Resolution Middleware

### New: `TenantResolutionMiddleware`

Runs early in the pipeline (before auth, before routing). Extracts the subdomain from the Host header and resolves tenant config.

```
Request: https://acme.dxsuite.cloud/api/signage/PlayerContent
  → Host: acme.dxsuite.cloud
  → Subdomain: "acme"
  → Lookup in ITenantStore → TenantContext { TenantId, ConnectionStrings, AuthConfig, ... }
  → Store in HttpContext.Items["TenantContext"]
```

**Fallback for dev:** If no subdomain match (e.g., `localhost:5298`), fall back to a configured default tenant.

**API key requests** (device heartbeats, content delivery): Devices don't use subdomains — they use `X-API-KEY`. The API key table gains a `TenantId` column, and the middleware resolves tenant from the key.

### New: `ITenantContext` (scoped service)

```csharp
public interface ITenantContext
{
    string TenantId { get; }
    string CustomerCode { get; }
    string DirectorConnectionString { get; }
    string? SentinelConnectionString { get; }
    TenantAuthConfig AuthConfig { get; }
    TenantModuleConfig Modules { get; }
    TenantFeatureFlags Features { get; }
    string MediaRootPath { get; }
}
```

Populated by middleware, consumed by everything downstream.

---

## Layer 3: Tenant-Scoped Database Access

### Current Pattern (single-tenant)
```csharp
services.AddPooledDbContextFactory<DirectorDbContext>(options =>
    options.UseSqlServer(config.GetConnectionString("Director")));
```

### New Pattern (per-tenant connection)

Replace static connection string with a **tenant-aware factory**:

```csharp
services.AddDbContextFactory<DirectorDbContext>((sp, options) =>
{
    var tenantContext = sp.GetRequiredService<ITenantContext>();
    options.UseSqlServer(tenantContext.DirectorConnectionString);
});
```

**Important:** Use `AddDbContextFactory<T>(..., ServiceLifetime.Scoped)`, **not** `AddPooledDbContextFactory`. The pooled variant registers `DbContextOptions<T>` as a process-lifetime singleton — the options delegate runs once at first resolution (from the root provider, where `HttpContext` is null) and the resulting connection string is frozen forever. All tenants would share that first-resolved connection string, defeating isolation entirely. The scoped factory rebuilds options once per DI scope (per HTTP request or Blazor circuit), which is the correct granularity for per-tenant routing. ADO.NET connection pooling at the SQL Client level still applies, so there is no meaningful performance regression.

The same pattern applies to `SentinelDBContext`, `CoreDataDbContext`, `AviationDbContext`.

---

## Layer 4: Per-Tenant Authentication

### Current State
Authentication is configured once at startup in `Program.cs`:
```csharp
.AddMicrosoftIdentityWebApp(config.GetSection("AzureAd"))
.AddOpenIdConnect("Google", ...)
```

### Challenge
OIDC middleware is registered at startup — it can't change per request. Each tenant may have a different Azure AD app registration.

### Solution: Per-Tenant Named OIDC Schemes

At startup, iterate all tenants from the registry and register a named OIDC scheme for each enabled provider per tenant. Scheme name: `{subdomain}_{providerKey}` (e.g., `acme_Microsoft`). Callback path: `/signin-oidc-{subdomain}-{providerKey}`.

```csharp
foreach (var tenantSection in config.GetSection("Tenants").GetChildren())
{
    foreach (var providerSection in tenantSection.GetSection("Auth:Providers").GetChildren())
    {
        var schemeName = $"{tenantSection.Key}_{providerSection.Key}";
        services.AddAuthentication().AddOpenIdConnect(schemeName, options =>
        {
            options.Authority = providerConfig.Authority;
            options.ClientId = providerConfig.ClientId;
            options.ClientSecret = providerConfig.ClientSecret;
            options.CallbackPath = $"/signin-oidc-{tenantSection.Key}-{providerSection.Key.ToLower()}";
        });
    }
}
```

`AuthController.Challenge(provider)` resolves `ITenantContext` and challenges `{tenantSubdomain}_{provider}` when the tenant has an explicit provider configured. Tenants using `Provider="multi"` fall back to the top-level scheme registration.

**Why not `IConfigureNamedOptions<OpenIdConnectOptions>`?** Its `Configure()` callbacks feed into `IOptionsMonitor<OpenIdConnectOptions>`, which caches results as a **process-lifetime singleton** (resolved from the root provider, where `HttpContext` is null). This is the same structural flaw as `AddPooledDbContextFactory` — the callback runs once at startup, not per request. Per-tenant routing in the callback would be permanently unreachable. Per-scheme registration at startup is the correct mechanism for static tenant configurations.

**Local accounts:** These already work per-DB since `TblLocalAccount` lives in each tenant's Director database.

---

## Layer 5: Web/Worker Tier Split

### Problem
Background services (HeartbeatService, InkContentSyncService, etc.) are singletons. In multi-tenant mode with load-balanced web instances, every instance would run every tenant's services — wasteful and requires complex distributed coordination.

### Strategy: Separate Deployment Tiers

Split the application into two deployment modes sharing the same codebase:

```
┌────────────────────────────────────┐
│  WEB TIER (stateless, N instances) │
│  - Blazor Server UI                │
│  - API Controllers                 │
│  - SignalR hubs                    │
│  - NO background services          │
│  - Horizontally scaled via LB      │
└────────────────────────────────────┘

┌────────────────────────────────────┐
│  WORKER TIER (per-tenant/group)    │
│  - Background services only        │
│  - HeartbeatService, SyncService   │
│  - AssetCleanup, OverrideSchedule  │
│  - 1 worker per tenant (or group)  │
│  - Independently scaled            │
└────────────────────────────────────┘
```

### Implementation

**Startup mode flag** — `DXSuite:Role` config value:
- `"web"` — Registers Blazor, controllers, auth. Does NOT register `AddHostedService<>()`.
- `"worker"` — Registers background services and DB access. Does NOT register Blazor or controllers.
- `"standalone"` — Registers everything (backward-compatible, on-prem single-server mode).

```csharp
var role = config["DXSuite:Role"] ?? "standalone";

if (role is "web" or "standalone")
{
    // Register Blazor, controllers, auth, static files
}

if (role is "worker" or "standalone")
{
    // Register background services
    // Worker is configured with specific tenant(s) via config
}
```

**Worker tenant assignment:**
```json
// Worker instance for Acme
{
  "DXSuite:Role": "worker",
  "DXSuite:WorkerTenants": ["acme"]
}

// Worker instance for multiple small tenants
{
  "DXSuite:Role": "worker",
  "DXSuite:WorkerTenants": ["beta", "gamma", "delta"]
}
```

Each worker's `ExecuteAsync` loop only processes its assigned tenants. High-volume tenants get dedicated workers; small tenants can share.

### Scaling Model

| Scenario | Web Instances | Worker Instances |
|----------|--------------|-----------------|
| Dev/on-prem | 1 (standalone) | 0 (built-in) |
| Small cloud (5 tenants) | 2 (load-balanced) | 1 (all tenants) |
| Medium cloud (20 tenants) | 3-4 | 3-4 (5 tenants each) |
| Large cloud (100+ tenants) | Auto-scale | 1 per high-volume tenant, shared for rest |

### Singleton Caches (Web Tier)

In-memory caches are still needed on the web tier for request performance. They gain a tenant dimension:

```csharp
// Current: ConcurrentDictionary<string, ResolvedAccess?>
// New:     prefix all cache keys with {tenantId}:
//          e.g., "acme:userId:locationId" → ResolvedAccess
```

Cache refresh is timer-based per web instance (already the pattern today with `AccessCacheRefreshService`).

### Distributed Locking

**Pre-requisite:** Port RIADirector's `SqlDistributedLockService` to DXSuite (replaces the current Redis-based implementation). Uses `sp_getapplock`/`sp_releaseapplock` — zero external infrastructure.

In multi-tenant mode, since each tenant has its own SQL database, locks are **inherently tenant-isolated** — `sp_getapplock` operates at the connection/database level. A lock named `"asset-cleanup"` in Acme's DB doesn't conflict with the same lock name in Beta's DB. No tenant-prefixing needed for lock keys.

---

## Layer 6: Tenant-Scoped Static Files & Media

### Current State
Media assets are served from `wwwroot/MediaAssets/{locationId}/` — no tenant isolation.

### New Structure
```
wwwroot/
├── _shared/          ← branding, CSS, scripts (shared across tenants)
├── tenants/
│   ├── acme/
│   │   ├── MediaAssets/
│   │   ├── DeviceScreenshots/
│   │   ├── OverrideContent/
│   │   └── images/
│   ├── beta/
│   │   ├── MediaAssets/
│   │   └── ...
```

`ITenantContext.MediaRootPath` resolves to the tenant's subfolder. The existing relative path configs (`MediaAssets`, `Screenshots`) are combined with this root.

**Media abstraction:** `IMediaStore` interface with local disk implementation initially, swappable to Azure Blob Storage later without code changes.

**Static file middleware** serves from the full wwwroot; tenant isolation is at the path level. A custom `IFileProvider` could enforce tenant boundaries.

### Web Preview Thumbnails (implemented)

Web-asset preview thumbnails are **not** under `wwwroot` at all. They live at
`{ContentRoot}/App_Data/WebThumbnails/{tenantSubdomain}/{assetId}.png`, out of reach of the
anonymous static-files middleware, and are read only through `WebThumbnailController`
(`[Authorize]`, route `/WebThumbnails/{assetId:int}.png`). The tenant segment is deliberately
absent from the URL — the controller supplies it from the caller's resolved `ITenantContext` —
so one tenant cannot address another's captures even by guessing.

`WebPreviewService` is scoped for this reason. Background services must resolve it from a tenant
scope (`IWorkerScopeFactory.CreateTenantScopeAsync`), never from the root provider.

**Outbound fetches** of admin-entered URLs go through `OutboundUrlGuard`, which rejects loopback,
link-local (cloud metadata), private, CGNAT, and IPv6 transition ranges. Enforcement is at
socket-connect time in `OutboundUrlGuard.DialAsync` — the socket is dialled at a specific validated
address rather than by name, so redirect hops and DNS rebinding are covered. Our own fetches reach
it through `SocketsHttpHandler.ConnectCallback`, with proxies disabled on that client so the guard
always sees the real destination.

**Headless capture is filtered, not disabled.** Chromium fetches a page's subresources and follows
its redirects inside its own network stack, so validating the top-level URL constrains nothing:
a page that passes the check can embed `<iframe src="http://169.254.169.254/...">` and have the
response rendered into a thumbnail the tenant admin reads back. `EgressFilteringProxy` closes that
by making the browser's loads pass through us — it listens on loopback, applies the same
`IsAllowedAddress`/`IsHostAllowListed` policy to every request, handles CONNECT for HTTPS by
tunnelling (the decision is which address may be dialled, so there is no reason to terminate TLS),
and answers refusals with `403`. The browser is launched with `--proxy-server` pointing at it, and
capture fails closed to metadata thumbnails if the filter cannot start.

Two Chromium flags carry weight here and should not be dropped:

| Flag | Why |
|------|-----|
| `--proxy-bypass-list=<-loopback>` | Chromium bypasses the proxy for loopback and link-local targets **by default** — exactly the addresses the filter exists to deny. `BrowserEgressIntegrationTests` fails without it. |
| `--force-webrtc-ip-handling-policy=disable_non_proxied_udp` | WebRTC opens UDP outside the proxy, letting a page probe internal hosts and render the answers into the thumbnail. |

The one refused configuration is `WebPreview:AllowPrivateNetworks` in a shared deployment: it
disarms the address filter, so `IsBrowserCaptureAllowedAsync` returns false when it is set
alongside more than one enabled tenant. It remains supported for single-tenant on-prem installs.
Narrower exemptions belong in `WebPreview:OutboundAllowList`.

---

## Layer 7: API Key Tenant Binding

### Current State
`TblApiKey` has no tenant column. API keys are global.

### Change
Add `TenantId` to `TblApiKey` (in each tenant's DB) or maintain a central API key → tenant mapping in the registry.

**Recommendation:** Central mapping table in the tenant registry DB. When a device sends `X-API-KEY`, the middleware:
1. Looks up key in central table → gets `TenantId`
2. Resolves tenant config → sets `ITenantContext`
3. Validates key against tenant's Director DB

This avoids scanning every tenant's DB for key validation.

---

## Layer 8: Cross-Tenant Identity & Tenant Switching

### Problem

A user may have access to more than one tenant (e.g., a corporate group with multiple business units, or platform support staff). Each tenant may use a different OIDC provider (Acme uses Azure AD, Beta uses Okta, Gamma uses local accounts). The user needs:

1. A stable identity that exists across tenants
2. A way to discover which tenants they can access
3. A way to switch between tenants without managing N independent accounts

### Two-Level User Model

Identity is split into **platform identity** (who you are across the cloud) and **tenant identity** (your row in a specific tenant's database with roles, permissions, location access).

```
TblPlatformUser (central)              TblLocalAccount (per tenant)
─────────────────────────              ─────────────────────────────
PlatformUserId (GUID, PK)              UserId (int, PK)
PrimaryEmail (unique)                  Email
DisplayName                            FirstName, LastName
IsPlatformOperator (bit)               Roles, Permissions, etc.
CreatedDate, LastLoginDate             ...

         │                                       ▲
         │                                       │
         └──── TblPlatformUserTenantMembership ──┘
               ────────────────────────────────
               PlatformUserId (FK)
               TenantId (FK)
               TenantUserId (int) — FK to TblLocalAccount.UserId
               IsActive (bit)
               GrantedDate, GrantedByPlatformUserId
               UNIQUE(PlatformUserId, TenantId)
```

The platform user is the durable identity. Tenant memberships are an M:N join. Most users have exactly one membership and never notice the platform layer exists.

### Authentication Pattern: Hybrid (Pattern C)

Three classes of users, each with a different auth path:

| User class | Auth path | Switching UX |
|---|---|---|
| **Single-tenant business user** (the 95% case) | Tenant's own IdP via subdomain | N/A — no switcher shown |
| **Cross-tenant business user** (rare) | Re-auth against each tenant's IdP on switch | Full OIDC flow per switch |
| **Platform operator / support staff** | Central platform IdP (e.g., Azure AD operator tenant) with `*.dxsuite.cloud` cookie | Seamless within elevation window (see Layer 9) |

The tenant's own IdP remains authoritative for tenant-business users. The platform IdP is *additional*, not replacing.

### Tenant Switcher UI

After login, the app queries `TblPlatformUserTenantMembership` for the current platform identity and shows a tenant picker in the header *only if* `count > 1` or the user is a platform operator.

Selecting a tenant performs a full-page navigation to that tenant's subdomain — cookies cannot silently transfer across origins. The destination subdomain decides:

- **Platform operator with active elevation** → honor `*.dxsuite.cloud` cookie, issue tenant-local session
- **Cross-tenant business user** → trigger destination tenant's OIDC flow
- **No membership** → 403

### Membership Bootstrapping

When a tenant-local user logs in for the first time:

1. Tenant OIDC flow completes, returns email + claims
2. App finds-or-creates `TblLocalAccount` in tenant DB (existing behavior)
3. App finds-or-creates `TblPlatformUser` in central DB by `PrimaryEmail`
4. App finds-or-creates `TblPlatformUserTenantMembership` linking the two

This is automatic and invisible. The platform identity emerges as a side effect of normal logins. Manual provisioning is only needed for platform operators (who exist in the central IdP, not in any tenant).

### Email as Federation Key

`PrimaryEmail` is the only stable identifier shared across heterogeneous IdPs. This is fragile: email changes break the link, and email collisions across IdPs are a security risk. Mitigations:

- Email match is **only** a lookup hint; actual access requires a `TblPlatformUserTenantMembership` row that was created via a controlled flow (first login from a verified tenant IdP, or platform-operator provisioning).
- `TblPlatformUser.PrimaryEmail` is editable only by platform operators with audit logging; users cannot self-merge identities.
- Optional: store `IdpSubject` (the OIDC `sub` claim) per membership as a tamper-evident binding to the original IdP account.

---

## Layer 9: Privileged Access & Audit (SOC 2 Controls)

Platform operators can reach any tenant's data. This is the highest-risk surface in the system. The controls in this layer are designed to satisfy **SOC 2 CC6 (Logical Access)** and **CC7 (System Operations)** with auditable evidence per access event.

### Principle: No Standing Access

A platform operator cookie alone grants nothing inside any tenant. To enter a tenant, the operator must hold a **live elevation token** scoped to that tenant. Elevations are time-bound, ticketed, and (for write access) dual-approved. Expiry is automatic.

### Role Decomposition

Replace a single `IsPlatformAdmin` flag with purpose-bound roles:

| Role | Capability | Example |
|---|---|---|
| `PlatformOperator` | Create/disable tenants, manage other operators, manage billing. **Cannot read tenant business data.** | Provisioning new customer |
| `TenantSupport` | Time-bound, ticketed **read** access into a specific tenant | Investigating heartbeat failures |
| `TenantSupportWrite` | Same as `TenantSupport` + write. Requires second approver. | Manual data correction |
| `Auditor` | Read-only access to platform audit logs across all tenants. No tenant data access. | Compliance review |

Stored in `TblPlatformRole` and `TblPlatformUserRole` (M:N).

### Just-In-Time Elevation Flow

```
1. Operator opens elevation request:
   - Target tenant: "acme"
   - Role requested: TenantSupport
   - Ticket reference: SUPPORT-1234
   - Reason: "Investigating heartbeat failures reported 14:00 UTC"
   - Duration: 4 hours

2. Approval (one of):
   - Second operator approves (manual)
   - Auto-approve rule matches (e.g., on-call engineer + active P1 incident)
   - High-trust tenant requires tenant-side approval also

3. App writes TblPlatformElevation row:
   - ElevationId, PlatformUserId, TenantId, Role
   - GrantedBy, ApprovedBy, TicketRef, Reason
   - IssuedAt, ExpiresAt
   - RevokedAt (nullable)

4. Cross-tenant cookie + live elevation row → access granted
5. ExpiresAt passes → access vanishes, no manual revocation needed
```

### Database Additions

```
DXSuite_Platform additions
├── TblPlatformRole
│   ├── RoleId, RoleName, Description
├── TblPlatformUserRole
│   ├── PlatformUserId (FK), RoleId (FK)
│   ├── GrantedBy, GrantedDate
├── TblPlatformElevation
│   ├── ElevationId (GUID, PK)
│   ├── PlatformUserId (FK)
│   ├── TenantId (FK)
│   ├── Role — TenantSupport | TenantSupportWrite
│   ├── TicketRef, Reason
│   ├── IssuedAt, ExpiresAt
│   ├── ApprovedByPlatformUserId
│   ├── RevokedAt, RevokedByPlatformUserId (nullable)
│   └── INDEX(PlatformUserId, TenantId, ExpiresAt)
├── TblPlatformAuditLog (append-only, ledger-enabled)
│   ├── AuditId (GUID, PK)
│   ├── EventTime, PlatformUserId, TenantId (nullable)
│   ├── EventType — TenantCreated, ElevationRequested, ElevationApproved,
│   │              ElevationUsed, TenantDataRead, TenantDataModified,
│   │              MembershipGranted, MembershipRevoked, RoleChanged
│   ├── ResourcePath — e.g., "/tenant/acme/configuration/devices"
│   ├── TicketRef, Reason
│   └── DetailsJson
└── TblTenantAccessNotice (tenant-visible projection)
    ├── Mirrors elevation + audit events, scoped to tenant
    └── Surfaced in tenant admin UI as "Vendor Access Log"
```

### Middleware: `PlatformElevationMiddleware`

Runs after `TenantResolutionMiddleware`. For requests where the resolved tenant differs from the user's "home" tenant memberships:

1. Confirm caller has a platform identity cookie (`*.dxsuite.cloud`)
2. Query `TblPlatformElevation` for an unexpired row matching `(PlatformUserId, TenantId)`
3. If write request, confirm role is `TenantSupportWrite`
4. If no live elevation → 403 with link to elevation request UI
5. Write `TblPlatformAuditLog` row for the access (event type derived from HTTP verb + route)

### Audit Log Integrity

Append-only at the storage layer, not by convention:

- **Preferred**: Azure SQL Ledger Tables on `TblPlatformAuditLog` — tamper-evident hash chain, queryable via standard T-SQL, generates ledger digest for periodic verification.
- **Alternative**: Stream events to an external write-only sink (Azure Monitor, Splunk, Datadog) in addition to the local table. Local table for query, external sink for tamper resistance.
- App database role for the platform DB has `INSERT` only on `TblPlatformAuditLog` — no `UPDATE` or `DELETE`.

### Tenant Visibility

Each tenant's admin UI surfaces a **Vendor Access Log** populated from `TblTenantAccessNotice`:

- Every elevation issued for their tenant
- Every access event during the elevation window
- Stated reason and ticket reference
- Operator's display name (not full identity, depending on contract)

Transparency is itself a control. Customers who can see access tend to trust it more, and the existence of the log deters misuse.

### Session Controls

| Setting | Platform identity cookie |
|---|---|
| Absolute lifetime | 8 hours |
| Idle timeout | 30 minutes |
| Re-auth required for | Granting elevations to others, exporting tenant data, deleting tenants, modifying roles |
| Cookie scope | `*.dxsuite.cloud` (subdomain-wide, HTTPS-only, HttpOnly, SameSite=Lax) |
| MFA | Enforced at IdP, not the app — non-bypassable |

### Conditional Access (delegated to IdP)

The platform IdP (Azure AD operator tenant or equivalent) enforces:

- MFA mandatory for all platform identities, no exceptions
- Device compliance for `PlatformOperator` and `TenantSupportWrite` roles
- Geo-restrictions if applicable
- Risk-based sign-in policies

Inheriting these from the IdP avoids re-implementing them in the app and provides cleaner audit evidence (the IdP's own logs).

### Tenant-Side Opt-In Controls

High-trust tenants (banking, healthcare) can configure stricter rules per-tenant in `TblTenant`:

- `RequireTenantApprovalForElevation` (bit) — every elevation request notifies tenant admin and requires their approval before token issues
- `BlockedRegions` (JSON) — geo-restrictions on operator access to this tenant
- `ElevationMaxDurationMinutes` (int) — caps the duration field on elevation requests
- `NotifyOnElevation` (email list) — tenant contacts notified per elevation event

### Lifecycle Controls

- **Joiner/Mover/Leaver**: platform identities are in the central IdP, which is in turn fed by HR. When an employee leaves, IdP deprovisioning cascades within minutes — operator can no longer obtain a platform cookie. Existing elevations naturally expire.
- **Quarterly access review**: scheduled report of all `PlatformUserRole` assignments, owner signs off, evidence retained for audit.
- **Annual penetration test**: scoped to include cross-tenant escalation attempts.
- **Break-glass procedure**: documented out-of-band path for when the platform IdP is down (e.g., locally-stored secrets in a sealed envelope, dual-control to open). Auditors will ask.

### Separation of Duties

`TblPlatformElevation.GrantedBy` and `ApprovedByPlatformUserId` must differ. A single operator cannot self-approve. `PlatformOperator` (provisioning) and `TenantSupport*` (data access) are separate roles — being one does not grant the other. Auditor (read of audit logs) is its own role and cannot be combined with `PlatformOperator`.

---

## Layer 10: Operational Concerns

The architecture is unimplementable without answers to migration, secrets, observability, and local dev. These cut across every phase and must be designed early — not deferred to "production readiness."

### Database Migrations Across Tenants

EF Core migrations are designed for a single connection string. With N tenant DBs, the migration model must change.

**Migration runner pattern:**

A standalone console app (`DXSuite.MigrationRunner`) reads the central tenant registry and applies migrations to each tenant DB:

```
1. Read TblTenant WHERE IsEnabled = 1
2. For each tenant:
   a. Open connection using tenant's connection string
   b. Check current state via __EFMigrationsHistory
   c. Apply pending migrations in order
   d. Record success/failure to MigrationRunLog (in central DB)
3. Report summary: succeeded, failed, skipped
```

**Operational rules:**

- Migrations run as a **separate pipeline job before app deploy**, never at app startup. App startup must not modify schema.
- **Partial failure isolation**: one tenant failing does not roll back others. Failed tenants flagged in `MigrationRunLog`; deploy pipeline halts; ops investigate before app deploy proceeds.
- **Backward-compatible changes only**, deployed in additive-then-cleanup pattern (add column nullable → deploy reading both → backfill → deploy writing new only → drop old). This decouples schema changes from app changes.
- **Ordering**: central DB first (may add tenant config columns the runner needs), then tenants.
- **Schema drift detection**: separate `DXSuite.SchemaCheck` tool runs on schedule, compares each tenant's actual schema against EF model snapshot. Drift = alert.
- **Per-tenant smoke test** post-migration: trivial query confirms DB is reachable before marking that tenant migrated.

### Secrets Management

`TblTenant` columns marked `(encrypted)` in earlier sections are vague. Production should not store secret values in the DB at all.

**Pattern: Key Vault references**

```
TblTenant
├── ...
├── ConnectionStringSecretRef  → "https://dxsuite-kv.vault.azure.net/secrets/tenant-acme-director-cs"
├── OidcClientSecretRef        → "https://dxsuite-kv.vault.azure.net/secrets/tenant-acme-oidc-secret"
├── SmtpPasswordSecretRef      → "https://dxsuite-kv.vault.azure.net/secrets/tenant-acme-smtp-pwd"
```

The DB stores **references**, not values. App reads secrets from Key Vault on tenant-context resolution, with a short-lived in-memory cache (default 5 min TTL).

**Authentication to Key Vault:**

- App authenticates via **Managed Identity** — no static credentials anywhere
- Web tier MI: read access to its tenants' secret namespace
- Worker tier MI: read access to assigned tenants' secrets only
- Migration runner MI: read access to all tenant secrets (broader scope, locked-down execution context)

**Rotation:**

- Connection string rotation: write new value to Key Vault → app cache TTL expires → new connections use new value. No deploy needed.
- OIDC secret rotation: write new secret in IdP and Key Vault simultaneously, app picks up on cache expiry.
- Documented runbook with frequency targets (90 days for OIDC secrets, 365 days for SQL passwords).

**Local dev:** developer secrets in User Secrets / `.env`, Key Vault references skipped if a literal value is configured. `ITenantSecretResolver` abstracts the lookup.

### Observability

Every log entry, metric, and trace must carry `TenantId`. Without this, multi-tenant troubleshooting devolves into grep-and-pray.

**Structured logging:**

```csharp
services.AddScoped<ILogEventEnricher, TenantContextEnricher>();

// Enricher pulls ITenantContext from current scope,
// adds TenantId + TenantSubdomain properties to every log event
```

Worker tier creates the same scope per tenant per cycle; logs inherit the tenant tag.

**Metrics:**

- All counters/histograms tagged with `tenant_id` dimension
- Standard set: HTTP request rate/latency/errors, DB query duration, background-service iteration time, queue depth, cache hit ratio
- Per-tenant dashboards templated, instantiated for high-value tenants
- Aggregate dashboards for the long tail (top N by error rate)

**Distributed tracing:**

- OpenTelemetry with `TenantId` in baggage, propagated across HTTP and worker boundaries
- Trace context flows: web request → DB write to work queue → worker picks up → trace continues with same root
- Critical for tying "user clicked X" to "worker did Y minutes later"

**Correlation IDs:**

Every request gets a W3C Trace Context `CorrelationId`. When a request enqueues background work via DB, the correlation ID is persisted on the work row and re-attached when the worker picks it up. User-action → background-effect chains become traceable end-to-end.

**Cost attribution:**

- Tag every Azure resource with `TenantId` where the platform supports tagging
- Per-tenant DB DTU/CU consumption is directly billable from Azure Cost Management API
- Shared-resource costs (web tier, central DB) allocated by request volume per tenant
- Monthly cost-per-tenant report fed by Cost Management + tenant request metrics

**Per-tenant alerting:**

- High-value tenants get dedicated alert rules (latency, error rate, background-service lag)
- Long-tail tenants share aggregate alerts ("any tenant with >5% error rate over 5 min")
- Page differentiation: aggregate to general on-call; named-tenant alerts to a tenant-specific channel if contracted

### Local Developer Experience

If running multi-tenant locally is undocumented, every developer reinvents it badly.

**Hostname pattern:** use `localtest.me` (auto-resolves to `127.0.0.1`, no hosts file editing):

- `acme.localtest.me:5298` → resolves locally, hits dev app
- `beta.localtest.me:5298` → same app, different tenant context
- Wildcard cert for `*.localtest.me` checked into repo (or generated by setup script) for HTTPS

**Tenant seeding:** `scripts/dev-setup.ps1`:

1. Provision two SQL DBs (`DXSuite_Acme_Dev`, `DXSuite_Beta_Dev`) on local SQL or `mcr.microsoft.com/mssql/server` container
2. Run migrations against both
3. Seed each with test users, locations, devices
4. Write `appsettings.Development.json` with both tenant entries
5. Output the URLs to hit

**Compose profile:** `docker-compose.dev.yml` stands up 2 SQL containers + central registry + web app + worker. Single command brings up the full multi-tenant local stack.

**OIDC in dev:** per-tenant OIDC against real IdPs is impractical locally. Provide a `LocalAccountsOnly` mode for dev tenants (`AuthProvider = "local"`), login via `TblLocalAccount` rows seeded by setup script. Skip dynamic-OIDC complexity locally.

**Documentation:**

- `docs/local-dev-setup.md` — first-day onboarding
- `docs/multi-tenant-debugging.md` — attaching debugger, switching active tenant, manually triggering worker for one tenant

---

## Migration Path (Phased)

### Implementation Protocol (READ FIRST)

This migration is executed phase-by-phase. **Each phase is a review checkpoint.** The following protocol is mandatory for any agent or developer implementing these phases:

1. **Before starting a phase**: read this entire document, plus the specific phase's section below and its DoD entry in the Deployment Playbooks appendix.
2. **During the phase**: implement only what the phase scope describes. If you discover a need that crosses phase boundaries, stop and surface it — do not silently expand scope.
3. **At the end of the phase**, before moving to the next phase or declaring the work complete, you **MUST** invoke the `phase-reviewer` agent with:
   - The path to this architecture document
   - The phase number under review
   - The git diff range or list of modified files
   - Any phase-specific notes (deviations, deferred items, open questions)
4. **Act on the review verdict**:
   - `PASS` — proceed to next phase
   - `PASS WITH CAVEATS` — log the caveats as follow-up items, then proceed
   - `FAIL` — fix the Required-Before-Merge items, then re-invoke `phase-reviewer` until PASS
5. **Do not skip the review** because the change "feels small" or "looks obviously correct." The review exists precisely because familiarity with the code blinds the implementer to integration risks. Phases that touch security boundaries (Phase 3, Phase 7) and tenant isolation (Phase 2, Phase 5) are non-negotiable review gates.

This protocol applies whether the implementer is a human developer or a Claude agent (Sonnet, Opus, or otherwise). The `phase-reviewer` agent runs on Opus 4.7 and is defined at `.claude/agents/phase-reviewer.md`.

---

### Phase 1: Foundation (No user-visible changes)
- Create `ITenantContext`, `ITenantStore` interfaces
- Create `TenantResolutionMiddleware` (subdomain + API key)
- Create config-based `ITenantStore` (appsettings.json, Option B)
- Wire `ITenantContext` into DI as scoped
- Require at least one tenant entry in config (no silent fallback)

### Phase 2: Database Isolation
- Change DbContext factories to read connection string from `ITenantContext`
- Add tenant media root path support
- Test with two tenants on the same app instance, different DBs

### Phase 3: Auth Per-Tenant
- Implement dynamic OIDC configuration via `IOptionsMonitor`
- Store per-tenant auth config in tenant registry
- Support Microsoft, Google, Okta, Local per tenant

### Phase 4: Web/Worker Tier Split
- Add `DXSuite:Role` config flag (`web`, `worker`, `standalone`)
- Gate background service registration behind role check
- Gate Blazor/controller registration behind role check
- Worker reads `DXSuite:WorkerTenants` to know which tenants to process
- Existing on-prem deployments use `standalone` mode (default) — no behavior change

### Phase 4.5: Port SQL Distributed Lock
- Port RIADirector's `SqlDistributedLockService` to DXSuite
- Replace Redis-based lock service
- Remove Redis config section

### Phase 5: Service Isolation
- Prefix all cache keys with tenant ID
- Add `TenantId` to API key resolution
- Worker services create tenant-scoped DI scopes per cycle

### Phase 6: Cross-Tenant Identity (Layer 8)
- Create central `DXSuite_Platform` database with `TblPlatformUser` and `TblPlatformUserTenantMembership`
- Auto-bootstrap platform identity on tenant login (find-or-create by email)
- Tenant switcher UI (header dropdown, shown only when `count > 1` or platform operator)
- Cross-subdomain navigation flow (full-page navigate, no silent cookie transfer)
- Re-auth on switch for cross-tenant business users (Pattern A within Pattern C)

### Phase 7: Privileged Access & Audit (Layer 9)
- Stand up the central platform IdP (Azure AD operator tenant or equivalent) with MFA + Conditional Access
- Add `TblPlatformRole`, `TblPlatformUserRole`, `TblPlatformElevation`, `TblPlatformAuditLog`, `TblTenantAccessNotice`
- Implement JIT elevation flow: request → approve → issue → expire
- `PlatformElevationMiddleware` gates cross-tenant access on live elevation row
- Enable Azure SQL Ledger Tables on `TblPlatformAuditLog`
- Tenant-facing Vendor Access Log UI
- Tenant-side opt-in controls (`RequireTenantApprovalForElevation`, etc.)
- Document break-glass procedure, quarterly access review, JML process

### Phase 8: Production Readiness
- Migrate tenant registry to SQL (Option A)
- Tenant provisioning SQL script
- Azure Blob Storage `IMediaStore` implementation (optional)
- Monitoring/metrics per tenant
- Tenant admin portal (future)
- SOC 2 Type 1 readiness assessment, then Type 2 observation period

---

## Key Files Reference

### Current Architecture (to understand)
- `DXSuite.Host\Program.cs` — Service registration, middleware pipeline, auth setup
- `DXSuite.Director\Services\Extensions.cs` — Director service/DbContext registration
- `DXSuite.Shared\Authorization\SessionState.cs` — User session resolution
- `DXSuite.Director\Services\DistributedLock\*` — Current Redis-based locking
- `RIADirector\Services\DistributedLock\SqlDistributedLockService.cs` — SQL lock implementation to port

### Background Services (13 total, all need tenant-scoping)
- HeartbeatService, AssetCleanupService, OverrideScheduleService
- BatchDeploymentBackgroundService, AutoHealBackgroundService
- DeclinedContentCleanupService, ScheduleExpiryNotificationService
- PairingCleanupService, AccessCacheRefreshService, EventLogRetentionService
- InkContentSyncService, AirlineScheduleMonitorService
- CredentialExpiryNotificationService, ApiKeyExpiryNotificationService

### Singleton Caches (need tenant-prefix)
- AccessCacheStore, ManifestCacheService, MediaFolderCacheService
- MetadataCacheService, DeviceCacheService

---

## Design Decisions (Confirmed)

1. **Backward compat:** All deployments require at least one tenant entry in config. No silent fallback to legacy mode — cleaner code, easier to reason about.
2. **Tenant provisioning:** Manual DB creation + SQL provisioning script. Admin adds tenant config entry. No automated API initially.
3. **Sentinel:** Per-tenant Sentinel DB, same as Director. Full isolation.
4. **Media storage:** `IMediaStore` abstraction layer. Initial implementation is local disk (tenant subfolder). Swappable to Azure Blob Storage later without code changes.
5. **Distributed locking:** SQL Server `sp_getapplock` — same as RIADirector. No Redis needed. Locks are inherently per-database, so in a per-tenant-DB model, lock isolation is automatic.
6. **In-memory caches:** Tenant-prefixed keys in `ConcurrentDictionary`. No Redis cache layer needed.
7. **Service isolation:** Web/Worker tier split. Web tier is stateless and horizontally scalable. Worker tier runs background services, assigned to specific tenants via config. `standalone` mode for on-prem backward compatibility.
8. **Cross-tenant identity:** Two-level user model — `TblPlatformUser` (central, durable identity) and `TblLocalAccount` (per-tenant, with roles/permissions), joined via `TblPlatformUserTenantMembership`. Platform identity is bootstrapped automatically from first tenant login.
9. **Tenant switching:** Hybrid (Pattern C). Single-tenant users never see a switcher. Cross-tenant business users re-auth against each tenant's IdP on switch. Platform operators use a central platform IdP with `*.dxsuite.cloud` cookie, gated by Layer 9 elevation.
10. **Privileged access:** No standing access to tenant data for platform operators. JIT elevation only — time-bound, ticketed, dual-approved (for write), tenant-visible. `TblPlatformElevation` is the access boundary, not the platform identity cookie.
11. **Audit integrity:** Platform audit log is append-only via Azure SQL Ledger Tables. App database role has `INSERT` only — no `UPDATE`/`DELETE` on audit rows.
12. **Compliance posture:** Architecture targets SOC 2 CC6/CC7 controls from day one. Per-tenant DBs make data-isolation evidence trivial; JIT elevation + audit log generates per-event evidence for privileged access.

---

## Appendix: Deployment Playbooks

These are operational procedures referenced from the migration phases. They become production runbooks but should exist in draft form before Phase 8 so implementation does not paint into a corner.

### Tenant Provisioning Runbook

Adding a new customer tenant.

**Prerequisites:**
- Customer signed contract; legal/billing record exists
- Subdomain agreed (e.g., `acme`)
- OIDC provider chosen with customer; their IT will create app registration

**Steps:**

1. **Reserve subdomain** in central registry (`TblTenant` row with `IsEnabled = 0`, `Status = 'Provisioning'`)
2. **Create tenant DB** (`DXSuite_ACME`) on assigned SQL server, run schema migration
3. **Create Sentinel DB** (if licensed): `Sentinel_ACME`
4. **Create Key Vault secrets**: connection strings, OIDC client secret, SMTP credentials. Record URIs.
5. **Update `TblTenant`** with secret references, OIDC config, module flags, feature flags
6. **Configure DNS**: `acme.dxsuite.cloud` CNAME to platform load balancer
7. **TLS**: confirm wildcard cert covers new subdomain (or provision per-tenant cert)
8. **OIDC app registration**: customer's IT registers app in their IdP, provides ClientId, redirect URI configured to `https://acme.dxsuite.cloud/signin-oidc`
9. **Smoke test**:
   - Hit `https://acme.dxsuite.cloud/health` — returns tenant-scoped health
   - Run synthetic login flow against tenant OIDC
   - Confirm tenant DB reachable, basic query returns
10. **Worker assignment**: update worker pod config to include new tenant in `WorkerTenants`, rolling restart of worker tier
11. **Enable**: set `TblTenant.IsEnabled = 1`, `Status = 'Active'`
12. **Customer handoff**: send admin user the platform identity link to bootstrap their first login
13. **Audit log**: provisioning recorded in `TblPlatformAuditLog` with operator, timestamp, ticket reference

**Idempotency:** rerunning the script with existing tenant detects existing state and skips completed steps. Failed mid-provisioning leaves `Status = 'Provisioning'` for manual recovery.

### Tenant Decommissioning Runbook

GDPR right-to-portability + right-to-erasure compliance.

**Prerequisites:**
- Customer notice received (contract end, off-boarding)
- Decommission ticket with grace period agreed (typical: 30 days)

**Steps:**

1. **Notify**: `TblTenant.Status = 'Notified'`, customer receives confirmation email with timeline
2. **Disable new logins** at end of grace period: `TblTenant.IsEnabled = 0`, existing sessions expire
3. **Data export**: generate complete export package (DB dump + media assets) → encrypted archive → secure delivery to customer (signed URL with 7-day expiry)
4. **Customer confirmation**: customer signs off on export receipt
5. **Cold storage**: move full DB backup + media to long-term archive (Azure Archive tier) for compliance retention period (typically 7 years)
6. **Drop active resources**:
   - Drop tenant DB
   - Drop Sentinel DB
   - Delete tenant media folders
   - Remove tenant from worker assignments (rolling restart)
   - Revoke Key Vault secrets
   - Remove DNS CNAME
7. **Archive registry row**: `TblTenant.Status = 'Decommissioned'`, retain for audit/billing history (do not delete)
8. **Audit log**: decommissioning recorded with all step timestamps, operator, customer sign-off reference

**Recovery window:** for the first 30 days post-decommission, a documented "undelete" path exists (restore from backup, re-register). After cold storage transition, recovery requires multi-day SLA.

### Per-Phase Acceptance Criteria & Rollback Triggers

Every Migration Path phase needs DoD and rollback documented before implementation begins.

**Template:**

```
Phase N — <name>

DoD:
- [ ] Implementation complete per design
- [ ] Unit tests pass
- [ ] Integration tests confirm <phase-specific isolation invariant>
- [ ] Observability: new dimensions appear in dashboards
- [ ] Documentation updated
- [ ] Rollback rehearsed in staging
- [ ] phase-reviewer agent invoked, returned PASS or PASS WITH CAVEATS
      (see Implementation Protocol at top of Migration Path)

Rollback Trigger (revert immediately if):
- <phase-specific failure mode>

Rollback Procedure:
- <steps to revert>
```

**The phase-reviewer DoD item is non-optional.** A phase is not complete until the review has been run and its verdict acted on. This applies equally to human developers and Claude agents executing the phases.

**Worked example — Phase 2 (Database Isolation):**

```
DoD:
- DbContext factories read connection string from ITenantContext
- Two test tenants in config, each pointing to separate DB
- Integration test: query "all locations" while in Acme context returns
  zero rows from Beta DB (proves isolation). Manual procedure: provision
  DXSuite_Beta_Director on the dev SQL server; start the app with both
  tenants enabled; browse beta.localhost:<port> and confirm the
  DbContext options log shows the Beta connection string; browse
  default.localhost and confirm Director data is not visible under beta.
- EF Core scoped factory confirmed: verify logs show separate connection
  strings per tenant subdomain (no pooled singleton; each scope logs the
  IHttpContextAccessor-resolved tenant)
- TenantId present in DB query log entries
- phase-reviewer agent invoked with phase=2, returned PASS or
  PASS WITH CAVEATS

Rollback Trigger:
- Any production query observed reading from the wrong tenant DB
- Connection pool exhaustion on shared SQL server
- DbContext factory throwing during request handling

Rollback Procedure:
- Revert DbContext registration to static connection string from config
- Disable second tenant in registry
- Notify affected tenant if any cross-tenant exposure occurred
```

**Worked example — Phase 7 (Privileged Access & Audit):**

```
DoD:
- TblPlatformElevation, TblPlatformAuditLog (ledger-enabled), and roles
  tables in central DB
- PlatformElevationMiddleware blocks cross-tenant access without live
  elevation row
- JIT elevation request → approve → issue → expire flow works end-to-end
- Tenant Vendor Access Log UI shows test elevations
- Penetration test: operator without elevation cannot read tenant data
- Audit log ledger digest verifiable
- phase-reviewer agent invoked with phase=7, returned PASS
  (PASS WITH CAVEATS not acceptable for security-boundary phase —
  must be PASS before merging)

Rollback Trigger:
- Any operator confirmed to have accessed a tenant without an elevation row
- Audit log integrity verification fails
- Elevation expiry not enforced (operator retains access past ExpiresAt)

Rollback Procedure:
- Disable platform identity cookie acceptance on all tenants
- Revoke all active platform sessions
- Force re-auth via tenant-local IdPs only until fix verified
```

### Disaster Recovery & Backup Strategy

**Per-tenant backups:**

- Azure SQL automated backup with point-in-time restore (default 7-day retention, extended to 35 days for paid tiers)
- Long-term retention: weekly full retained 1 year, monthly retained 7 years
- Backup encryption with customer-specific key (Bring Your Own Key) for high-trust tenants

**Central registry backup:**

- Most critical DB in the system — losing it means no tenant routing works
- Geo-redundant backup with active geo-replication to secondary region
- Daily backup verification: restore to staging, run smoke test, alert on failure

**Recovery objectives:**

| Tenant tier | RPO | RTO |
|---|---|---|
| Standard | 1 hour | 4 hours |
| Premium | 5 minutes | 1 hour |
| Enterprise | 1 minute (geo-replication) | 15 minutes |

Tier stored in `TblTenant.ServiceTier`. Drives which backup configuration is applied per tenant.

**Restore procedure:**

1. Identify scope: single tenant, multiple tenants, central registry, or region-wide
2. Single tenant: point-in-time restore from Azure SQL backup → connection string in `TblTenant` flipped to restored DB → smoke test → mark active
3. Region failure: failover to secondary region (DNS update + warm standby app instances + secondary geo-replicated SQL becomes primary)
4. Post-restore: data loss assessment, customer notification, audit log entry

**Tested annually** in scheduled DR exercise. Untested restore procedures are not restore procedures.

### Network & Data Residency

**Topology:**

```
Internet
   │
   ▼
Azure Front Door / Traffic Manager   (multi-region routing)
   │
   ▼
Application Gateway / NGINX          (TLS termination, WAF)
   │
   ▼
VNet: Web Tier subnet  ←→  VNet: Worker Tier subnet
                                     │
                                     ▼
                          VNet: Data subnet
                          - Private endpoint → Azure SQL (per region)
                          - Private endpoint → Key Vault
                          - Private endpoint → Storage (Blob)
```

- No tenant DB has public internet access — private endpoints only
- Web and worker tiers communicate to SQL via VNet, never public IP
- App Service / AKS tier in dedicated VNet, private endpoint to data tier

**Region affinity:**

- `TblTenant.PrimaryRegion` = "us-east", "eu-west", "ap-southeast", etc.
- Tenant DB provisioned in that region
- Web tier deployed multi-region; routes request to tenant's primary region based on registry lookup
- Cross-region routing adds latency; documented per-region SLA

**Data residency for compliance:**

- EU tenants: DB and backups in EU region, no data egress to US
- Healthcare tenants: HIPAA-eligible Azure regions only
- Public sector: GovCloud equivalent if required
- `TblTenant.DataResidency` constraint validated at provisioning

**Connection limits:**

- Per-tenant DB on shared SQL server: max ~30 connections per app instance per tenant DB
- Or: dedicated SQL elastic pool per tier, sized for aggregate connection limit
- Worker tier connection limit lower than web (background work tolerates queueing)

### CI/CD Pipeline Changes

**Build:** single artifact (container image or zip) for all roles, tagged with version + git SHA, published to artifact registry.

**Deploy stages:**

```
1. Migration Job
   - DXSuite.MigrationRunner against staging tenants
   - Halt pipeline on any tenant failure
   - On success, run against production tenants

2. Web Tier Deploy
   - Rolling deploy across web instances
   - Per-instance health check: hit /health on canary tenant
   - Halt on health check failure

3. Worker Tier Deploy
   - Rolling restart of worker pods
   - Each pod re-reads tenant assignments on startup
   - Health check: confirm worker reports heartbeat within 60s

4. Smoke Test
   - Synthetic transaction against canary tenant
   - End-to-end: login, list devices, trigger background sync
   - Halt and alert on failure

5. Canary Promotion
   - New version runs on canary tenants only for N hours
   - Auto-promote to all tenants if metrics within tolerance
```

**Configuration deploy:** tenant assignment changes (new tenant added to `WorkerTenants`) deploy as config update + rolling restart of worker tier only — no app redeploy. Feature flag changes in `TblTenant.FeatureFlags` are live-effective with cache TTL — no deploy needed.

**Per-environment topology:**

| Env | Tenants | Roles | Notes |
|---|---|---|---|
| Local dev | 2 seeded | standalone | Developer machine |
| CI | Ephemeral | standalone | Per-PR, torn down after tests |
| Staging | 2 fixed (`acme-staging`, `beta-staging`) | web + worker | Mirrors prod topology, smaller scale |
| Production | Real customers + canary tenant | web + worker | Multi-region |

**Canary tenant:** synthetic tenant in production used for pre-promotion validation. Real DB, real auth flow, no real customer data. Receives every new deploy first; metrics observed before broader promotion.
