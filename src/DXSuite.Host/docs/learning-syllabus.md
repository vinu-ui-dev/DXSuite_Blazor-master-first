# DXSuite Learning Syllabus

A self-paced curriculum for going from "I built this with AI assistance" to "I understand every layer well enough to onboard another engineer and refactor with confidence."

The order is deliberate: each unit assumes the previous ones. Don't skip Tier 1 even if it feels basic — the gaps a classically-trained engineer has filled in are mostly there.

For each unit:
- **Read**: specific files in this repo to open
- **Internalize**: the concepts a senior engineer carries in their head
- **Exercise**: an active task that proves you've absorbed it
- **Refactor signals**: things to flag for later

---

## Tier 1 — Foundations You Should Be Able to Explain Cold

These are the mental models that every classically-trained .NET engineer has and that AI tends to skip over because it "just works." If you can't whiteboard these without notes, the rest of the syllabus rests on sand.

### Unit 1.1 — The ASP.NET Core Request Pipeline

**Read**: `src/DXSuite.Host/Program.cs` end-to-end (you've already touched this; now read it slowly).

**Internalize**:
- The difference between `builder.Services` (the DI container being *configured*) and `app.Use*` (middleware being *ordered*). They look similar; they aren't.
- Middleware is a **linked list of delegates**, not a list of plugins. Each one calls `await next()` to pass control. Anything that runs *before* `await next()` is request-phase; anything *after* is response-phase.
- Order matters and is not configurable later. `UseAuthentication` before `UseAuthorization` before `UseEndpoints` — change the order and silent breakage follows.
- `UseStaticFiles` short-circuits the pipeline. Anything after it never runs for `.css`/`.js` requests.

**Exercise**: Trace what happens, line by line, when a device POSTs to `/api/signage/heartbeat`:
1. Which middleware runs in order?
2. At what point is `ITenantContext` populated?
3. At what point is the device token validated?
4. What's the call stack inside `SignageController.Heartbeat`?

Write the answer out. If you get stuck, you've found the gap.

**Refactor signal**: `Program.cs` is ~590 lines. Convention in modern .NET is to extract `builder.Services.AddDXSuiteAuth(authConfig)`-style methods into `Host/Extensions/` so the entrypoint reads as a story. Note it; we'll revisit in Tier 5.

---

### Unit 1.2 — Dependency Injection Lifetimes

**Read**: every `AddSingleton`, `AddScoped`, `AddTransient` call in `Program.cs` and in each module's `RegisterServices`. Pay attention to which lifetime each one chose and why.

**Internalize**:
- **Singleton**: one instance for the lifetime of the process. Shared across every request, every tenant, every thread. Must be thread-safe. Cannot depend on scoped services.
- **Scoped**: one instance per "scope" — for HTTP, that's per request. For Blazor Server, that's **per circuit** (the entire user session), not per render. This bites people constantly.
- **Transient**: a new instance every time. Cheap for stateless services; a footgun when the service holds resources.
- **Captive dependency**: if a singleton holds a scoped service, that scoped service lives forever and serves every request. Memory leak and tenant-bleed in one.
- The container validates this on startup in dev (`ValidateScopes`); in prod, you can ship a captive dep undetected.

**Exercise**: 
1. `IMediaStore` is registered as Singleton. Is that safe? Walk through what state it holds.
2. `DirectorDbContext` is provided through a *scoped* `IDbContextFactory`, not pooled. Why was pooling rejected? (Hint: read the comment in `Program.cs` around line 243 and look at `src/DXSuite.Director/Services/Extensions.cs`.)

**Refactor signal**: any singleton that takes `IHttpContextAccessor` is a code smell — it usually means scoped state leaked into singleton land.

---

### Unit 1.3 — Async/Await Without Hand-Waving

**Internalize**:
- `async` doesn't make code parallel. It makes it **non-blocking**. The thread is released back to the pool while the I/O completes.
- `Task` represents a future value. Awaiting it suspends the method and resumes when the value is ready, **possibly on a different thread**.
- `ConfigureAwait(false)` — in library code, on .NET 8+ ASP.NET Core, you usually don't need it. In Blazor Server **you do need to be careful** about UI thread synchronization (the `SynchronizationContext` is the circuit's render queue).
- `_ = InvokeAsync(...)` in your CLAUDE.md memory is exactly this — Blazor demands you re-enter the circuit's sync context before touching state.
- `async void` is a banned construct except for event handlers. An exception in `async void` crashes the process.
- "Sync over async" (`.Result`, `.Wait()`) deadlocks under load. It's tempting; it's wrong.

**Exercise**: Find an instance of `Task.Run` in the codebase. Justify whether it should be there. (`Task.Run` for CPU-bound work is fine; for I/O-bound work it's actively harmful because it burns a thread.)

**Read**: Stephen Cleary's "There Is No Thread" blog post once. It's the single best explanation written.

---

### Unit 1.4 — IDisposable, `using`, and Native Resources

**Internalize**:
- Managed memory is collected automatically. **Unmanaged** resources (file handles, sockets, DB connections, GDI handles, SkiaSharp surfaces, FFmpeg process handles) are not.
- `IDisposable.Dispose()` is the explicit release hook. `using` is syntactic sugar for `try { } finally { x.Dispose(); }`.
- `IAsyncDisposable` is for resources that need awaitable cleanup (network connections that flush, EF DbContext from .NET 6+).
- Finalizers exist as a backstop, but relying on them is a bug — the GC may not run for minutes.

**Exercise**: Open `src/DXSuite.Director/Services/CanvasImageGenerationService.cs` and find every `SKBitmap`, `SKSurface`, `SKImage` allocation. Verify each is in a `using`. SkiaSharp objects wrap native memory — leaks here are invisible to the .NET memory profiler but visible to the OS.

**Refactor signal**: SkiaSharp leaks are a class of bug worth a dedicated review pass. Add to refactor backlog.

---

### Unit 1.5 — Configuration and Options

**Internalize**:
- `IConfiguration` is a layered key/value store. JSON, env vars, command line, Azure Key Vault — they layer with last-wins semantics.
- `IOptions<T>` is a singleton, captured at startup. Doesn't reload.
- `IOptionsSnapshot<T>` is scoped — re-reads per request. Use this for tenant-specific config that can change.
- `IOptionsMonitor<T>` is singleton with change notifications — and **caches by name**, which is the trap your `Program.cs` comment calls out around per-tenant OIDC.
- Strongly-typed binding (`Configure<T>` + `services.GetSection().Get<T>()`) is preferable to string-keyed lookups everywhere.

**Exercise**: Find `FeatureFlags` and trace how a single flag flows from `appsettings.json` to a runtime check in a Blazor component.

---

## Tier 2 — The DXSuite Core

Now into your actual architecture. You wrote (or co-wrote) all of this, but you may not have an integrated mental model of *how it composes*.

### Unit 2.1 — The Module Plugin Pattern

**Read**:
- `src/DXSuite.Shared/Module/IBlazorModule.cs`
- Any one module's implementation, e.g. `src/DXSuite.Director/DirectorModule.cs`
- `src/DXSuite.Host/Program.cs` lines 415-445 and 558-587

**Internalize**:
- This is a **composition root with manual registration** — not dynamic plugin loading. Modules are referenced as project dependencies and added to the registry by name.
- The pattern gives you: per-module service registration, per-module endpoint registration, per-module Razor assembly discovery for Blazor.
- Trade-off: adding a new module requires touching `Program.cs`. Benefit: no runtime assembly scanning, fast startup, no plugin-version-mismatch nightmares.
- Compare against the alternative — `Microsoft.Extensions.DependencyInjection.Abstractions.IHostedService` discovery via reflection, or MEF. You picked the simpler model. Know why.

**Exercise**: Add a hypothetical "Reports" module. Write the minimum scaffolding (no real code) — interface, registration, endpoint mapping. Diff it against `DirectorModule` and explain every line.

---

### Unit 2.2 — Multi-Tenancy: The Spine of the System

This is the single most important subsystem in the codebase. If you only deeply understand one thing, make it this.

**Read** in order:
1. `src/DXSuite.Host/docs/multi-tenant-cloud-architecture.md` — full read, no skimming
2. `src/DXSuite.Shared/Multitenancy/ITenantContext.cs`
3. `src/DXSuite.Shared/Multitenancy/ITenantStore.cs`
4. `src/DXSuite.Host/Multitenancy/ConfigTenantStore.cs`
5. `src/DXSuite.Host/Multitenancy/SqlTenantStore.cs`
6. `src/DXSuite.Host/Multitenancy/TenantResolutionMiddleware.cs` (find it via grep)
7. `src/DXSuite.Host/Multitenancy/TenantCircuitInitializer.cs` (find it via grep)
8. `src/DXSuite.Shared/Multitenancy/IWorkerScopeFactory.cs`
9. `src/DXSuite.Director/Services/Extensions.cs` — DbContext factory wiring

**Internalize**:
- **The tenant boundary is the security boundary.** Every cross-tenant data leak is a security incident. Internalize what guarantees this — it is *not* automatic.
- `ITenantContext` is **scoped**. It's populated by middleware (HTTP) or by `CircuitHandler` (Blazor). If you ever resolve it from a singleton, you've broken isolation.
- DbContexts are **factories, not contexts**. Each unit of work asks the factory for a fresh DbContext, and the factory injects the current tenant's connection string at construction time. If you cached a DbContext in a singleton, you've broken isolation.
- Background services don't have an HTTP request, so they don't have an `ITenantContext` populated by middleware. They use `IWorkerScopeFactory` to *create* a scope per tenant manually, with the context populated explicitly. This is why `DXSuite:WorkerTenants` exists.
- Two-tier identity: each tenant DB has its own users (`IdentityDbContext`), and there's a **Platform** identity that grants cross-tenant operator access via `TblPlatformElevation`.

**Exercise**:
1. Draw a sequence diagram of an HTTP request from `tenant-a.dxsuite.com/devices`: DNS → Kestrel → middleware → tenant resolution → controller → DbContext factory → connection string → SQL.
2. Now draw the same for a Blazor circuit (long-lived WebSocket).
3. Now for a `BackgroundService` iterating tenants.
4. Identify, for each, the place where "tenant identity" is materialized and the place where it's consumed.

**Refactor signal**: any code that hardcodes a connection string, or that resolves `IConfiguration["ConnectionStrings:..."]` directly inside a request-scoped service, is a tenant-isolation bug waiting to be noticed.

---

### Unit 2.3 — Authentication, End to End

You called this out specifically — and rightly. There's a lot here.

**Read**:
1. `src/DXSuite.Host/Program.cs` lines 94-280 (cookie scheme, Microsoft Identity Web, generic OIDC providers, per-tenant OIDC schemes)
2. `src/DXSuite.Host/Controllers/AuthController.cs`
3. `src/DXSuite.Director/Authorization/DeviceTokenAuthHandler.cs`
4. `src/DXSuite.Director/Authorization/AdminApiKeyAuthHandler.cs`
5. `src/DXSuite.Shared/Services/Identity/PlatformElevationService.cs`
6. `src/DXSuite.Host/Multitenancy/PlatformElevationMiddleware.cs`

**Internalize the concepts** (not just the code):
- **Authentication scheme**: a named handler that can answer "who is this caller?" and "how do I challenge them?" You have several: `Cookies`, `OpenIdConnect`, per-tenant OIDC, device token, admin API key.
- **DefaultScheme vs DefaultChallenge vs DefaultSignIn**: these are *different*. DefaultScheme is "who do I ask by default"; DefaultChallenge is "where do I redirect to for login"; DefaultSignIn is "where do I drop the resulting identity."
- **Cookie scheme** stores the authentication ticket in an encrypted cookie. The cookie *is* the session.
- **OIDC** is the protocol: browser is redirected to the IdP, IdP redirects back with a code, server exchanges code for an ID token, server validates the token, server **signs in** to the cookie scheme. The cookie is what carries forward; the OIDC scheme is only used during login.
- **Per-tenant OIDC**: each tenant can configure its own IdP. Because OIDC options are cached by scheme name in `IOptionsMonitor`, you cannot mutate them per-request — you must register a *distinct named scheme per tenant*. That's what those lines 244-280 do.
- **JWT bearer tokens** are stateless: the token itself carries the identity, signed. No cookie, no session. Your external integration uses this.
- **API keys** for devices: simpler than JWT, validated against a DB record (with caching). Read `DeviceTokenAuthHandler` to see the pattern.
- **Re-authentication for sensitive operations**: `ReAuthConfirmationService` + Platform elevation = "you logged in two hours ago, but to delete this tenant you must prove it's still you right now."

**Exercise**:
1. Read the OAuth 2.0 RFC 6749 §4.1 ("Authorization Code Grant"). It's 6 pages. Don't skip it.
2. Read the OIDC core spec §3.1 (the same flow with identity tokens added). Also short.
3. Now re-read your `Program.cs` auth section. Most of the magic becomes legible.
4. Write a 10-line answer to: "If a user from tenant-a logs in via Microsoft, then visits tenant-b.dxsuite.com, what happens?" — explain whether the cookie carries over, whether they have access, and what middleware enforces this.

**Refactor signal**: the OIDC event handlers inline-define identical logger creation. That's a five-line extension method waiting to be born. Note it for Tier 5.

---

### Unit 2.4 — Blazor Server: What's Actually Happening

You write a lot of Blazor. Most engineers using it never internalize what the runtime is doing.

**Internalize**:
- A Blazor Server "circuit" is a **WebSocket** held open between browser and server, plus a server-side **render tree** kept in memory.
- Every interactive component lives in server RAM. Every event (`@onclick`, `@onchange`) is a message over the wire. The browser is a thin terminal.
- This means: state is in RAM (good — no round-trip to DB for UI state), latency is real (bad — every click is a network round-trip), and **scale is bounded by RAM** because each user holds a circuit.
- Disconnection retention: when the WebSocket drops, the server holds the circuit for some retention period (you set this to `Security:SessionTimeoutMinutes`) so a reconnect resumes seamlessly.
- `StateHasChanged()` from a background thread will throw. You must marshal back to the circuit's sync context via `InvokeAsync(StateHasChanged)`. Your CLAUDE.md memory already mentions this — make sure you understand *why*.
- `OnInitializedAsync` runs twice in Blazor Server in some configurations (prerender then interactive). It does **not** run twice in pure Server mode without prerendering. Know your config.

**Read**:
- `src/DXSuite.Host/Components/App.razor`, `Routes.razor`, `MainLayout.razor` (find via Glob)
- Any one heavy interactive component, e.g. a configuration page in Director

**Exercise**: Open the dev tools network tab while clicking through an interactive page. Watch the WebSocket frames. Convince yourself this is what's happening.

---

### Unit 2.5 — Database Access at the EF Core Level

**Internalize**:
- EF Core builds an **expression tree** from your LINQ; the provider translates it to SQL. Anything that can't translate falls back to client-side evaluation — which silently materializes everything to memory first. This is the single most common EF performance bug.
- **Change tracking** is on by default. Every entity read into the DbContext is "tracked" and will be saved on `SaveChangesAsync()`. For read-only queries this is wasted CPU and memory. `AsNoTracking()` opts out.
- **N+1 queries**: `var devices = db.Devices.ToList(); foreach(d in devices) Console.Write(d.Location.Name);` runs one query for devices and one per device for `Location`. `Include(d => d.Location)` joins it once.
- **Transactions**: SaveChangesAsync wraps changes in a transaction *per call*. If you need atomicity across multiple SaveChanges, you need an explicit `db.Database.BeginTransactionAsync()`.
- **Connection pooling** is at the ADO.NET level (`SqlConnection`), independent of DbContext pooling. You always have connection pooling. You opted *out* of DbContext pooling for tenant isolation reasons.
- **Migrations** vs **`.sql` files**: this repo uses raw SQL migration scripts (`docs/sql-migration-*.sql`), not EF Migrations. Understand why — likely because per-tenant DB shape evolution is easier to reason about with scripts than EF's snapshot model.

**Read**:
- `src/DXSuite.Director/Entities/DirectorDbContext.cs` — the model configuration
- Any 2-3 LINQ queries in services. Crack open Profiler/Activity Tracking to see the SQL each generates.

**Exercise**: Take the heaviest query in a Director service. Use EF's `.ToQueryString()` or SQL Profiler to capture the generated SQL. Read the execution plan in SSMS. Decide if any indexes are missing.

**Refactor signal**: this is likely where measurable perf wins live. Note it.

---

## Tier 3 — Subsystems You Use But Maybe Don't Internalize

### Unit 3.1 — FFmpeg and the World of Subprocess Management

**Read**:
- `src/DXSuite.Director/Services/VideoProcessingService.cs`
- `src/DXSuite.Director/Services/VideoThumbnailService.cs`
- The `FFMpegCore` configuration block in `src/DXSuite.Director/Services/Extensions.cs`

**Internalize**:
- FFmpeg is **a separate executable**. `FFMpegCore` is a thin .NET wrapper that spawns `ffmpeg.exe` and pipes data to it. Every transcode is a `Process.Start`.
- Subprocesses have: a PID, stdin/stdout/stderr streams, a working directory, an environment, and they don't die when your app dies unless you actively kill them.
- If you spawn ffmpeg and your service is restarted mid-encode, you have an orphan ffmpeg process holding a file handle. Cleanup matters.
- FFmpeg's output goes to **stderr** by default (progress info). You parse stderr to read progress. Stdout is for the data stream.
- Codecs, containers, bitrates, keyframes, B-frames: these are the vocabulary of video. Skim the FFmpeg docs for `-c:v`, `-c:a`, `-b:v`, `-preset`, `-crf`.
- Common pitfalls: encoding everything to h264 high profile when the device can only play baseline; not setting `-movflags +faststart` for web-served MP4 (player can't begin playback until the moov atom is read).

**Exercise**:
1. Run `ffmpeg -h encoder=libx264` once and read the options. Find which ones your code uses and why.
2. Run one of your video pipelines locally with `--loglevel debug` (or however your service exposes it). Read the ffmpeg invocation it produces.
3. Re-encode a sample video manually with that command. Compare bitrate, file size, and visual quality if you change `-crf 23` to `-crf 18` to `-crf 28`.

**Refactor signals**:
- Are encoded outputs deterministic (same input → same output bytes)? If not, idempotency suffers.
- Is there a kill-orphan-ffmpeg cleanup path on startup?

---

### Unit 3.2 — SkiaSharp and the World of Native Graphics

**Read**:
- `src/DXSuite.Director/Services/CanvasImageGenerationService.cs`
- `docs/logo-layer-feature.md`
- `docs/ticket-counter-management-architecture.md`

**Internalize**:
- SkiaSharp is a .NET binding to Skia, the C++ graphics library Chrome uses. It wraps native memory; .NET GC doesn't see most of it.
- Core types:
  - `SKBitmap`: a pixel buffer (raster data).
  - `SKImage`: an immutable image, possibly GPU-backed.
  - `SKSurface`: a drawable canvas; calling `surface.Canvas` gives you the painter.
  - `SKCanvas`: the API surface for `DrawText`, `DrawRect`, `DrawBitmap`, etc.
  - `SKPaint`: the "brush + pen + font" — every draw call takes one.
  - `SKTypeface`/`SKFont`: text rendering.
- Compositing model: alpha blending via `SKPaint.BlendMode`. Layers via `canvas.SaveLayer` (expensive; uses an offscreen surface).
- DPI/scale: everything is in pixels. If you want crisp logos on a 4K signage display, you must render at native resolution.
- Color spaces: sRGB by default. If you ever see washed-out output, color space mismatch is the suspect.

**Exercise**: Write a 50-line standalone Skia program that loads a JPEG, draws "HELLO" in 72pt white text over it, and saves a PNG. Don't copy from your service — write it from scratch. Internalizing the API surface is the goal.

**Refactor signal** (carried from Unit 1.4): every Skia object in a `using` — verify.

---

### Unit 3.3 — Storage Abstraction

**Read**:
- `src/DXSuite.Shared/Storage/IMediaStore.cs`
- `src/DXSuite.Host/Storage/LocalDiskMediaStore.cs`

**Internalize**:
- The pattern: define an interface that supports both local-disk and cloud (Azure Blob, S3). Local implementation today; swap to cloud later without touching consumers.
- Tenant scoping: every path should be prefixed by tenant. A bug here is a tenant data leak.
- Streaming vs buffering: `Stream`-based APIs scale to large files; byte-array APIs blow up RAM for 4GB video uploads.
- Idempotency of writes, atomic moves, partial-write recovery.

**Exercise**: Stub out `AzureBlobMediaStore`. Don't implement it — just sketch the interface implementation and identify the friction points (auth, retries, ranged reads).

---

### Unit 3.4 — Background Services and Scheduling

**Read**:
- Pick three `BackgroundService` subclasses with different shapes: `PixeraContentSyncService` (polling integration), `AssetCleanupService` (housekeeping), `BatchDeploymentBackgroundService` (orchestration).

**Internalize**:
- `BackgroundService.ExecuteAsync` runs once for the process lifetime. If it returns or throws, the service is **dead** until restart. Defensive wrapping in `try/catch` inside a loop is mandatory.
- Distributed locking (RedLock / Redis) prevents two pods running the same job. Without it, every replica of your worker doubles the work.
- Cancellation tokens: every `await` in a long-running loop should accept the stopping token, so graceful shutdown actually shuts down.
- Cron-like scheduling can be done with timers, Quartz.NET, or Hangfire. You use timers + locks — know the trade-offs vs Hangfire.

**Exercise**: Write a one-paragraph "incident retrospective" for the hypothetical case: "ServiceTechEngine crashed at 2am, nobody noticed for 9 hours." What's your detection story? What's your auto-restart story? (If the answer is "Kubernetes will restart the pod" — does that actually work for a `BackgroundService` exception that doesn't crash the process?)

---

### Unit 3.5 — WebSockets and the Sentinel Gateway

**Read**:
- `src/DXSuite.Sentinel/Middleware/GatewayWebSocketMiddleware.cs`
- `src/DXSuite.Sentinel/Services/Gateway/GatewayConnectionManager.cs`

**Internalize**:
- WebSocket is a TCP-derived protocol. After an HTTP upgrade handshake, the connection becomes a bidirectional frame stream. No HTTP semantics after that.
- You receive frames in a `while` loop; you send frames by writing to the socket. Both sides can send at any time.
- Connection lifecycle: TCP can die without telling you. Heartbeats (`Ping`/`Pong`) detect dead peers. Without them, you'll accumulate zombie connections.
- Backpressure: if the gateway sends faster than your handler processes, the receive buffer fills and the kernel drops the connection. You manage this with bounded channels or explicit reads.
- Compare against SignalR: SignalR is a higher-level abstraction over WebSocket/SSE/long-poll with auto-reconnect, hubs, and JSON-RPC. You chose raw WebSocket — know why (probably: gateways are not browsers, and you want protocol control).

**Exercise**: Pull up a connected gateway in dev tools (or a packet capture). Watch the frames. Identify ping cadence, message envelope shape, max frame size.

---

## Tier 4 — Operational Concerns

These are the things that separate "the code works on my machine" from "we run this in production for paying customers."

### Unit 4.1 — Logging, Tracing, and Observability

**Internalize**:
- **Structured logging**: `logger.LogInformation("User {UserId} logged in", userId)` is *not* string interpolation. The placeholder names become **fields** the log aggregator can query on. Never use `$"User {userId} logged in"` — that defeats the system.
- Log levels exist for filtering. `Debug` and `Trace` should be safe to leave in code; aggregators filter them out unless asked. `Error` should be reserved for "something is wrong."
- **OpenTelemetry / distributed tracing** is the modern story for "follow this request across services." Even without it, `Activity` and `Activity.Current` exist in .NET and give you correlation IDs.
- **Health checks** (you have these) are the dumbest, most important observability primitive. `/health` for liveness; `/health/ready` for readiness. Kubernetes uses them to know when to restart and when to route traffic.

**Read**: `src/DXSuite.Host/Health/TenantDbHealthCheck.cs`

**Exercise**: Find a log line in a hot path. Convince yourself it's structured (uses placeholders, not interpolation). Find one that isn't and fix it.

---

### Unit 4.2 — Deployment Roles and Process Topology

**Read**: `src/DXSuite.Host/Program.cs` lines 28-53 (role split logic).

**Internalize**:
- Three roles: `web`, `worker`, `standalone`. The same binary can take any of three shapes based on config. That's a powerful pattern but also a footgun — code conditionalized on role is brittle.
- In Kubernetes (or any orchestrator), you typically run **multiple replicas** of `web` (stateless, behind a load balancer) and a **single replica** (or few) of `worker` per tenant group.
- The `WorkerTenants` config is how you shard tenants across worker pods. Without it, every worker processes every tenant — fine for a few tenants, terrible at 100.

**Exercise**: Sketch a Kubernetes deployment for a 30-tenant production: how many web pods? how many worker pods? how do you shard tenants? what's the rollout story?

---

### Unit 4.3 — Secrets, Configuration, and the SOC 2 Story

**Read**: `src/DXSuite.Host/docs/soc2-readiness.md` and `src/DXSuite.Host/docs/platform-operations.md`.

**Internalize**:
- Secrets in `appsettings.json` are **wrong** in production. Use environment variables, Azure Key Vault, AWS Secrets Manager, or dotnet user-secrets for dev.
- Connection strings, OIDC client secrets, JWT signing keys — every one of these in source control is a credential rotation event waiting to happen.
- SOC 2 cares about: access control (who can do what), audit trail (proof of who did what), change management (proof that changes are reviewed), incident response (proof that you have a plan).
- The Platform Elevation pattern (`TblPlatformElevation`) is your audit-trail story for cross-tenant operator actions. Make sure you can explain it to an auditor.

**Exercise**: Grep the repo for hardcoded secrets. Find at least one. Decide whether it's truly a secret or a placeholder.

---

### Unit 4.4 — The Build Pipeline

**Read**:
- `DXSuite.sln`
- `Directory.Build.props`
- Each `.csproj` (skim, don't read every line)

**Internalize**:
- `.sln` is a flat list of project references. Visual Studio cares about it; the CLI mostly doesn't.
- `Directory.Build.props` is auto-imported into every `.csproj` under it. Use it for shared settings (language version, nullable, treat-warnings-as-errors).
- `dotnet build` walks the project graph, compiles in dependency order, parallelizes where possible.
- `<PackageReference>` brings in NuGet packages. Transitive references are resolved automatically; conflicts are warnings (or errors if you set them so).
- Trim warnings, AOT warnings: ignore unless you compile to native.

**Exercise**: Pick the biggest `.csproj` and audit its `<PackageReference>` list. Are there packages you don't recognize? Find them on NuGet and read their summary.

---

## Tier 5 — Refactor Backlog (Things to Look at While Learning)

Each of these is a candidate for follow-up work. Don't do them now — collect them, prioritize, then attack one at a time.

1. **Program.cs is 590 lines.** Extract `AddDXSuiteAuth`, `AddDXSuiteMultitenancy`, `AddDXSuiteHealthChecks`, `UseDXSuiteSecurityHeaders` into extension methods. The entrypoint should read as a five-stanza story.

2. **OIDC event handlers duplicate logger creation.** Five lines of `var logger = ctx.HttpContext.RequestServices.GetRequiredService<...>` in every event. Extract.

3. **Per-tenant OIDC scheme registration is identical to the global path, modulo the scheme name.** Both blocks can share a helper.

4. **Inline lambda health checks for `platform-db` and `registry-db`** are byte-for-byte identical. Extract `AddSqlServerConnectivityCheck(name, connectionString)`.

5. **SkiaSharp lifetime audit.** Tier 1.4 / 3.2 — verify every native handle is disposed.

6. **Sync-over-async grep.** Search for `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` outside of `Main`. Each one is a deadlock risk.

7. **EF query plan audit.** For the top 5 heaviest queries by frequency or row count, capture the SQL and execution plan, and decide if indexes are missing.

8. **N+1 sweep.** Grep for `foreach` loops over EF query results that access navigation properties.

9. **Background service crash handling.** Audit every `BackgroundService` subclass in `src/DXSuite.Director/Services/` and `src/DXSuite.Sentinel/Services/` (22+ classes — full list in Unit 3.4) for try/catch placement.

   **What to look for:** an unhandled exception inside `ExecuteAsync` unwinds the method, the host marks the service as stopped, and **that one service is dead until process restart** — while the web tier and other background services keep running. Silent service death is hard to detect (no crash, no alert, just missing work) and easy to ship.

   **The pattern that fails:**
   ```csharp
   protected override async Task ExecuteAsync(CancellationToken ct)
   {
       while (!ct.IsCancellationRequested)
       {
           var result = await DoWorkAsync();   // throws → service dies forever
           await Task.Delay(60_000, ct);
       }
   }
   ```

   **The pattern that survives:**
   ```csharp
   protected override async Task ExecuteAsync(CancellationToken ct)
   {
       while (!ct.IsCancellationRequested)
       {
           try { await DoWorkAsync(); }
           catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
           catch (Exception ex) { _logger.LogError(ex, "{Service} iteration failed; will retry next cycle", nameof(MyService)); }
           await Task.Delay(60_000, ct);
       }
   }
   ```

   Key invariants: try/catch lives **inside the loop**, not around it (around-the-loop catches once then exits the loop forever); `OperationCanceledException` from the stopping token is rethrown so graceful shutdown still works; the log uses structured fields so the aggregator can alert on `iteration failed` patterns. Add a counter or health-check metric per service so a service that keeps catching can be detected separately from a service that's quietly succeeding.

10. **Test coverage gaps.** Tier 4 implicitly asks: do you have integration tests for tenant isolation? For auth? For background services? If not, what's the smallest investment to add them?

11. **Secret hygiene.** Grep for connection strings, client secrets, JWT keys in source. Document the remediation plan.

12. **API surface review.** Device-facing controllers (`SignageController`, `UpdateController`) are your public API. Are they versioned? Documented? Rate-limited?

13. **Fail-fast on missing `WorkerTenants` in worker role.** `Program.cs` lines 42-53 currently `LogWarning` and fall back to the top-level connection string when `DXSuite:Role=worker` but `DXSuite:WorkerTenants` is unset or empty. This is a silent-misconfiguration footgun in cloud deployments: a typo'd env var (`DXSuite__WorkerTenant` vs `DXSuite__WorkerTenants`) yields a pod that *starts successfully* but silently processes the wrong tenant — potentially for days before discovery. Consider throwing `InvalidOperationException` so the pod refuses to start, Kubernetes restarts it, CrashLoopBackOff trips an alert, and an operator sees the error within minutes. May need an opt-out for explicit on-prem single-tenant worker deployments that genuinely want the fallback.

14. **Swap `AddDistributedMemoryCache()` to a real distributed backend before scaling out web pods.** `Program.cs` line 68 registers the in-memory `IDistributedCache` implementation, which despite its name is **per-process**. Sessions stored in pod A are invisible to pod B. The moment DXSuite runs on more than one web replica, the external-integration JWT-launch flow breaks (user validates token on pod A, next request lands on pod B, session not found, redirect loop). Mitigation when scaling out: register `AddStackExchangeRedisCache()` (or SQL Server distributed cache) with the same `IDistributedCache` interface — consumer code doesn't change. Track this as a prerequisite to horizontal scaling, not a tech-debt item.

15. **Persist data protection keys for horizontal scaling.** Auth cookies are encrypted with keys managed by `IDataProtector`. By default, keys are stored in the local filesystem of the pod that minted them. When DXSuite runs as more than one web pod, pod A's cookies can't be decrypted by pod B → users get logged out on every load-balancer reroute. Mitigation: configure `services.AddDataProtection().PersistKeysToAzureBlobStorage(...)` (or `PersistKeysToStackExchangeRedis(...)`, or a shared filesystem) so all pods share the same key ring. Same horizontal-scaling prerequisite as #14 — pair them.

16. **Retire the legacy `AzureAd`-section migration shim.** `Program.cs` lines 99-114 contain a one-time compatibility shim that synthesizes a `Microsoft` provider from the old RIADirector-style `AzureAd` config section when no modern `Authentication:Providers` exist. Once every tenant has been migrated to the new `Authentication:Providers` shape, this block is dead weight that obscures the auth wiring for new readers. Plan: add a startup `LogWarning` when the legacy path actually fires ("Tenant using deprecated AzureAd config section; migrate to Authentication:Providers"), monitor for the warning's absence over a release cycle, then delete the block. Without telemetry the shim will live forever.

17. **Consider a default `ClockSkew` in `TokenValidationParameters`.** Was set to `TimeSpan.Zero` originally (now `FromMinutes(5)` after a learning-driven edit). Worth a code review pass to confirm no other JWT-validation code paths in the repo still default to zero. Grep for `ClockSkew` and `TokenValidationParameters` across the codebase to verify consistency.

19. **Change the Microsoft Identity Web `TenantId` default from `"common"` to `"organizations"`.** `Program.cs` line 136 currently defaults `options.TenantId` to `"common"` when no tenant ID is configured. `"common"` is the **most permissive** Azure AD authority — it accepts any work/school Microsoft account *and* any personal Microsoft account (Outlook.com, Xbox, Hotmail, etc.). DXSuite is a B2B application: there is essentially no legitimate use case for a personal Microsoft account to authenticate against it. The current default lets personal accounts through the auth endpoint, where they're only stopped by the post-authentication authorization layer (which has been verified to be separately enforced and not conflated with authentication — so no immediate vulnerability today).

   Defense-in-depth says the auth endpoint should fail closed, not open. The fix is a one-line change: `options.TenantId = msProvider.TenantId ?? "organizations";`. `"organizations"` accepts any Azure AD work/school account (preserving multi-tenant SaaS behavior — every customer's Azure AD still works) while rejecting personal Microsoft accounts at the authentication layer, before they ever reach DXSuite's authorization checks. This narrows the surface area, makes audit logs cleaner (no stray personal-account login attempts to investigate), and aligns the default with the application's actual identity model.

   The authority is part of the auth scheme registration, which is built once at startup — changing the default has no per-tenant overrides to worry about. Operators who genuinely need `"common"` (or a specific tenant GUID, or `"consumers"`) can still set it explicitly in config; only the silent fallback changes.

20. **Consolidate cookie security configuration into a single `PostConfigure` source of truth.** `Program.cs` lines 148-156 (standalone-path `AddCookie` lambda) and lines 160-167 (cross-path `PostConfigure<CookieAuthenticationOptions>`) currently set the **same five cookie properties** (`HttpOnly`, `SecurePolicy`, `SameSite`, `SlidingExpiration`, `ExpireTimeSpan`) with the same values. The `PostConfigure` block runs in both auth paths (Microsoft Identity Web and standalone) and overwrites whatever `AddCookie` set, so the lambda in the standalone path is functionally redundant — `PostConfigure` is the actual source of truth.

    The duplication carries two real costs: (a) **drift risk** — when `Security:SessionTimeoutMinutes` is changed in only one block, the standalone and Microsoft auth paths quietly diverge, surfacing only on whichever path isn't being actively tested; (b) **reader confusion** — every future engineer reading the file pauses on the question "are these doing the same thing?" and pays a minute to figure it out.

    **The refactor:** replace `AddCookie(options => { ... })` in the standalone path with bare `AddCookie()` (just scheme registration), and let `PostConfigure<CookieAuthenticationOptions>` be the single, unambiguous place that defines cookie security policy. Add a comment above `PostConfigure` explaining that it intentionally applies to **both** the standalone-registered cookie scheme and the Microsoft.Identity.Web-registered cookie scheme — which is *why* `PostConfigure` is the right tool (you cannot reach into Microsoft.Identity.Web's internal `AddCookie` call to harden it; `PostConfigure` runs after every `Configure` callback regardless of who registered them).

    Worth using this refactor as the canonical example for any onboarding doc: it cleanly illustrates the **`Configure` vs `PostConfigure` lifecycle** in the .NET Options pattern, which is one of the most-misunderstood corners of ASP.NET Core configuration.

21. **(Deferred — capture for the future) DB-driven per-tenant OIDC providers.** Currently per-tenant OIDC providers must be defined in `appsettings.json` under `Tenants:{subdomain}:Auth:Providers` (`Program.cs` lines 244-280), because ASP.NET Core auth schemes are registered at startup in DI and are immutable after `app.Build()`. The `SqlTenantStore` (lines 332-338) loads tenant rows from the DB and supports adding tenants at runtime, but **only tenants that use the global Microsoft/Google providers** can be onboarded without a pod restart. A tenant with a custom OIDC IdP (e.g., their own Okta) requires editing config and restarting today. Two escape hatches when this becomes a real constraint: (a) implement a custom `IAuthenticationSchemeProvider` that lazily loads schemes from the DB on demand, or (b) register a single "proxy" OIDC scheme that reads tenant-specific client ID/secret/authority from `ITenantContext` at the moment of token exchange. Don't build this preemptively — wait until a customer commit makes it concrete. Capture this analysis now so it doesn't need to be rediscovered cold.

---

## Suggested Pace

- **Tier 1**: one unit per evening. ~1 week.
- **Tier 2**: one unit per weekend (these are deeper). ~5 weeks.
- **Tier 3**: one per week. ~5 weeks.
- **Tier 4**: one per week, in parallel with refactor work. ~4 weeks.
- **Tier 5**: rolling backlog — pick one per week to actually fix.

Total: ~3-4 months to "I could onboard another engineer to this codebase confidently." After that, the syllabus becomes a reference document for the next person.

---

## Onboarding Cheatsheet (for future engineers)

Once you've worked through this, distill the top of it into a `docs/onboarding.md` for new hires. Their first day should be:
1. Read `multi-tenant-cloud-architecture.md`.
2. Read this syllabus's Tier 2 (skip Tier 1 — they should already have it).
3. Pair with you on one trace-the-request exercise per subsystem.
4. Pick a Tier 5 refactor item as their first PR.

That's a real, defensible onboarding path. It's also the artifact this syllabus is implicitly building toward.
