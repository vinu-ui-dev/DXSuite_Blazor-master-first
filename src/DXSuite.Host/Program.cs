using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using DXSuite.Connectors;
using DXSuite.CoreData;
using DXSuite.Director;
using DXSuite.Host;
using DXSuite.Host.Components;
using DXSuite.Host.Multitenancy;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Services.Identity;
using DXSuite.Shared.Module;
using DXSuite.Shared.Models;
using DXSuite.Host.Components.Platform;
using DXSuite.Host.Services.MockCoreApi;
#if MODULE_AVIATION
using DXSuite.Aviation;
#endif
#if MODULE_PULSE
using DXSuite.Pulse;
#endif
#if MODULE_SENTINEL
using DXSuite.Sentinel;
#endif
#if MODULE_DESIGN
using DXSuite.Design;
#endif
#if MODULE_STYLEGUIDE
using DXSuite.StyleGuide;
#endif
#if MODULE_NETJETS
using DXSuite.NetJets;
#endif
#if MODULE_SLC
using DXSuite.SLC;
#endif
#if MODULE_DEVICEPROXY
using DXSuite.DeviceProxy;
#endif
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;

var builder = WebApplication.CreateBuilder(args);

// Azure Key Vault. When KeyVault:Uri is set, layer the vault on top of the other configuration
// sources so secrets resolve from it. DefaultAzureCredential uses a managed identity in Azure and
// the developer's Visual Studio or az sign-in locally. If the vault is configured but unreachable
// (offline, or no Azure sign-in), fall back to the already-loaded sources (user secrets,
// appsettings) so local development is not blocked.
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    try
    {
        builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());

        // Positive confirmation. ConfigurationManager loads the vault synchronously above, so reaching
        // this point means it connected. Count the secrets the vault actually contributed so startup
        // logs show an affirmative signal, not just the absence of an error.
        var kvProvider = ((IConfigurationRoot)builder.Configuration).Providers
            .LastOrDefault(p => p.GetType().Name.Contains("AzureKeyVault", StringComparison.Ordinal));
        var kvCount = 0;
        if (kvProvider is not null)
        {
            var stack = new Stack<string?>();
            stack.Push(null);
            while (stack.Count > 0)
            {
                var parent = stack.Pop();
                foreach (var seg in kvProvider.GetChildKeys(Array.Empty<string>(), parent).Distinct())
                {
                    var path = parent is null ? seg : $"{parent}:{seg}";
                    if (kvProvider.TryGet(path, out _)) kvCount++;
                    stack.Push(path);
                }
            }
        }
        Console.WriteLine($"[KeyVault] Loaded {kvCount} secrets from {keyVaultUri}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            $"Key Vault at {keyVaultUri} could not be loaded; continuing with local configuration. {ex.Message}");
    }
}

// Deployment role — controls which services and middleware are registered.
//   "web"        Blazor UI + API controllers. No background services.
//   "worker"     Background services only. No web UI or controllers.
//   "standalone" Both (default; backward-compatible on-prem mode).
var role = builder.Configuration["DXSuite:Role"] ?? "standalone";
var isWebRole    = role is "web"    or "standalone";
var isWorkerRole = role is "worker" or "standalone";

// In worker mode, WorkerTenants must be set so background services know which tenants to process.
// Without it, every background service falls back to the top-level connection string — equivalent
// to single-tenant mode — and per-tenant isolation is silently absent.
// Phase 5 added per-tenant scope iteration via IWorkerScopeFactory; DXSuite:WorkerTenants governs which tenants each worker pod handles.
if (role is "worker")
{
    var workerTenants = builder.Configuration.GetSection("DXSuite:WorkerTenants").Get<string[]>();
    if (workerTenants is null or { Length: 0 })
    {
        var startupLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Startup");
        startupLogger.LogWarning(
            "DXSuite:Role=worker but DXSuite:WorkerTenants is not configured. " +
            "Background services will use the top-level connection string (single-tenant fallback). " +
            "Set DXSuite:WorkerTenants to the list of tenant subdomains this worker should process.");
    }
}

// Read config up-front so both web and pipeline sections can reference it.
var extIntegrationConfig = builder.Configuration.GetSection("ExternalIntegration")
    .Get<ExternalIntegrationConfig>() ?? new ExternalIntegrationConfig();
var authConfig = builder.Configuration.GetSection("Authentication")
    .Get<AuthenticationConfig>() ?? new AuthenticationConfig();

// Fail fast if an enabled OIDC provider has no ClientSecret. Without this, a missing secret
// surfaces as a cryptic MSAL "Confidential Client" error deep in the login callback. The usual
// cause locally is running outside the Development environment, so user-secrets are not loaded.
foreach (var (providerKey, providerCfg) in authConfig.Providers.Where(kvp => kvp.Value.Enabled))
{
    if (string.IsNullOrEmpty(providerCfg.ClientSecret))
    {
        throw new InvalidOperationException(
            $"OIDC provider '{providerKey}' is enabled but ClientSecret is empty " +
            $"(ASPNETCORE_ENVIRONMENT='{builder.Environment.EnvironmentName}'). " +
            "For local dev, launch the http/https profile so the environment is 'Development' and " +
            "user-secrets load; verify with: dotnet user-secrets list. See CONTRIBUTING.md.");
    }
}

// Fail fast if the runtime cannot resolve IANA time zones. Every schedule window in the product
// resolves against an IANA identifier stored on the device row, and a runtime without ICU cannot
// resolve any of them. That is a deployment defect rather than a data defect: it routes the whole
// fleet through the per-device catch at once, which no operator edit fixes, and at ninety players
// on the poll floor it writes tens of thousands of log entries a day into a size-capped log,
// evicting other diagnostics while the incident is happening.
//
// Throwing is deliberate and is NOT a trivially reversible line, despite looking like one. A Host
// that will not start blanks nothing: players keep serving their stored manifest, so the fleet
// freezes on last-known-correct content and the outage is visible. A Host that starts without ICU
// serves incorrect content immediately, fleet-wide, and invisibly. Deleting this trades a visible
// outage for a silent one. See DXME-170.
//
// Unconditional across every DXSuite:Role value: two of the four device-row creation paths live in
// a BackgroundService, and on-prem runs standalone and needs both checks anyway.
try
{
    TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
}
catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
{
    throw new InvalidOperationException(
        "This runtime cannot resolve IANA time zone identifiers, so no schedule window can be " +
        "evaluated against a device's local time. The usual cause is InvariantGlobalization=true " +
        "on the Host or a publish profile, or DOTNET_SYSTEM_GLOBALIZATION_USENLS on the machine or " +
        "app pool. Windows Server 2019 or later is required. See DXME-170.", ex);
}

// The exact conversion the device seed helper performs at every row creation, so it also catches a
// host zone with no IANA mapping that a hardcoded identifier would never reach. The helper itself
// degrades to false rather than throwing, so it fails more quietly than this does.
if (!TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out _))
{
    throw new InvalidOperationException(
        $"The host time zone '{TimeZoneInfo.Local.Id}' has no IANA equivalent on this runtime, so " +
        "newly created device rows cannot be seeded with a usable time zone. See DXME-170.");
}

if (isWebRole)
{
    // ── External integration (JWT launch tokens) ────────────────────────────
    builder.Services.Configure<ExternalIntegrationConfig>(builder.Configuration.GetSection("ExternalIntegration"));

    if (extIntegrationConfig.Enabled && !string.IsNullOrEmpty(extIntegrationConfig.Secret))
    {
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(30);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
        });

        builder.Services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IOptions<ExternalIntegrationConfig>>().Value;
            return new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = config.Issuer,
                ValidateAudience = true,
                ValidAudience = config.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                    System.Text.Encoding.UTF8.GetBytes(config.Secret)),
                ClockSkew = TimeSpan.FromMinutes(5)
            };
        });
    }

    // ── Authentication — multi-provider support ──────────────────────────────
    // All providers (Microsoft, Google, Okta) are configured in Authentication:Providers.
    // Legacy: if an "AzureAd" section exists without any Providers, auto-migrate it.
    builder.Services.Configure<AuthenticationConfig>(builder.Configuration.GetSection("Authentication"));

    var legacyAzureAd = builder.Configuration.GetSection("AzureAd");
    if (legacyAzureAd.Exists() && !string.IsNullOrEmpty(legacyAzureAd["ClientId"]) && !authConfig.Providers.Any(p => p.Value.Enabled))
    {
        authConfig.Providers["Microsoft"] = new OidcProviderConfig
        {
            Enabled = true,
            DisplayName = "Microsoft",
            Icon = "bi-microsoft",
            Instance = legacyAzureAd["Instance"] ?? "https://login.microsoftonline.com/",
            TenantId = legacyAzureAd["TenantId"] ?? "common",
            ClientId = legacyAzureAd["ClientId"]!,
            ClientSecret = legacyAzureAd["ClientSecret"] ?? "",
            CallbackPath = legacyAzureAd["CallbackPath"] ?? "/signin-oidc",
            Domain = legacyAzureAd["Domain"],
        };
    }

    var enabledProviders = authConfig.Providers.Where(p => p.Value.Enabled).ToList();

    if (enabledProviders.Any() || authConfig.LocalAccounts.Enabled || authConfig.Ldap?.Enabled == true)
    {
        var hasMicrosoft = enabledProviders.Any(p => p.Key == "Microsoft");

        if (hasMicrosoft)
        {
            // Microsoft.Identity.Web registers its own "Cookies" scheme internally.
            // Other OIDC providers sign into that same cookie scheme via SignInScheme.
            var msProvider = enabledProviders.First(p => p.Key == "Microsoft").Value;

            builder.Services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
                    // Sign-out is local (drop the cookie), never a federated end-session redirect.
                    // Without this there is no default sign-out scheme at all and any scheme-less
                    // SignOutAsync throws; callers wanting federated sign-out pass schemes explicitly.
                    options.DefaultSignOutScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                })
                .AddMicrosoftIdentityWebApp(options =>
                {
                    options.Instance = msProvider.Instance ?? "https://login.microsoftonline.com/";
                    options.TenantId = msProvider.TenantId ?? "common";
                    options.ClientId = msProvider.ClientId;
                    options.ClientSecret = msProvider.ClientSecret;
                    options.CallbackPath = msProvider.CallbackPath ?? "/signin-oidc";
                    options.Domain = msProvider.Domain;
                })
                .EnableTokenAcquisitionToCallDownstreamApi()
                .AddMicrosoftGraph()
                .AddInMemoryTokenCaches();
        }
        else
        {
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie();
        }

        // Cookie security hardening (applies to both Microsoft Identity Web and standalone cookie schemes)
        builder.Services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(builder.Configuration.GetValue<int>("Security:SessionTimeoutMinutes", 30));
        });

        // Register generic OIDC providers (Google, Okta, etc.)
        // When Microsoft is primary, its cookie scheme is "Cookies" — other providers sign into it.
        foreach (var (name, provider) in enabledProviders.Where(p => p.Key != "Microsoft"))
        {
            var providerName = name; // capture for closure
            builder.Services.AddAuthentication().AddOpenIdConnect(providerName, displayName: provider.DisplayName, options =>
            {
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.Authority = provider.Authority ?? $"{provider.Instance}{provider.TenantId}";
                options.ClientId = provider.ClientId;
                options.ClientSecret = provider.ClientSecret;
                options.CallbackPath = provider.CallbackPath;
                options.ResponseType = "code";
                if (!string.IsNullOrWhiteSpace(provider.ResponseMode))
                    options.ResponseMode = provider.ResponseMode;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.Events = new Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectEvents
                {
                    OnTokenValidated = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("OidcAuth");
                        var email = ctx.Principal?.FindFirst("email")?.Value ?? ctx.Principal?.Identity?.Name;
                        var actualSignIn = ctx.Options.SignInScheme;
                        logger.LogInformation("OIDC TokenValidated for {Provider}: Email={Email}, ActualSignInScheme={SignIn}, ChallengeSchemeName={Scheme}",
                            providerName, email, actualSignIn, ctx.Scheme.Name);
                        ctx.Options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("OidcAuth");
                        logger.LogError(ctx.Exception, "OIDC AuthenticationFailed for {Provider}", name);
                        return Task.CompletedTask;
                    },
                    OnRemoteFailure = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("OidcAuth");
                        logger.LogError(ctx.Failure, "OIDC RemoteFailure for {Provider}", name);
                        ctx.Response.Redirect("/?error=remote-failure");
                        ctx.HandleResponse();
                        return Task.CompletedTask;
                    },
                    OnTicketReceived = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("OidcAuth");
                        var identity = ctx.Principal?.Identity;
                        logger.LogInformation("OIDC TicketReceived for {Provider}: Authenticated={IsAuth}, Name={Name}, Claims={Count}",
                            providerName, identity?.IsAuthenticated, identity?.Name, ctx.Principal?.Claims.Count());
                        foreach (var claim in ctx.Principal?.Claims ?? Enumerable.Empty<System.Security.Claims.Claim>())
                            logger.LogInformation("  Claim: {Type} = {Value}", claim.Type, claim.Value);
                        return Task.CompletedTask;
                    }
                };
            });

            // Force SignInScheme for non-Microsoft providers after all configuration runs.
            builder.Services.PostConfigure<OpenIdConnectOptions>(providerName, opts =>
            {
                opts.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            });
        }

        builder.Services.AddAuthorization();
    }

    // ── Per-tenant OIDC scheme registration (Phase 3) ───────────────────────
    // For each tenant with explicit Auth:Providers, register a named scheme
    // "{subdomain}_{providerKey}" with a unique callback path. Tenants using
    // Provider="multi" with no explicit Providers use the top-level schemes above.
    //
    // IConfigureNamedOptions<OpenIdConnectOptions> is NOT used — its callbacks
    // feed into IOptionsMonitor which caches as a process-lifetime singleton
    // (same flaw as AddPooledDbContextFactory). Per-scheme registration is correct.
    foreach (var tenantSection in builder.Configuration.GetSection("Tenants").GetChildren())
    {
        var subdomain = tenantSection.Key;
        if (!tenantSection.GetValue("IsEnabled", true)) continue;

        foreach (var providerSection in tenantSection.GetSection("Auth:Providers").GetChildren())
        {
            var providerKey = providerSection.Key;
            var providerConfig = providerSection.Get<OidcProviderConfig>();
            if (providerConfig == null || !providerConfig.Enabled) continue;

            var schemeName = $"{subdomain}_{providerKey}";
            var capturedConfig = providerConfig;
            var capturedCallback = $"/signin-oidc-{subdomain.ToLower()}-{providerKey.ToLower()}";

            builder.Services.AddAuthentication().AddOpenIdConnect(schemeName, options =>
            {
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.Authority = capturedConfig.Authority
                    ?? $"{capturedConfig.Instance ?? "https://login.microsoftonline.com/"}{capturedConfig.TenantId ?? "common"}";
                options.ClientId = capturedConfig.ClientId;
                options.ClientSecret = capturedConfig.ClientSecret;
                options.CallbackPath = capturedCallback;
                options.ResponseType = "code";
                if (!string.IsNullOrWhiteSpace(capturedConfig.ResponseMode))
                    options.ResponseMode = capturedConfig.ResponseMode;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
            });

            builder.Services.PostConfigure<OpenIdConnectOptions>(schemeName, opts =>
            {
                opts.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            });
        }
    }

    // ── SignalR (player wake channel) ────────────────────────────────────────
    builder.Services.AddSignalR();

    // ── Razor + Blazor Server ────────────────────────────────────────────────
    var sessionTimeoutMinutes = builder.Configuration.GetValue<int>("Security:SessionTimeoutMinutes", 15);
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents(options =>
        {
            options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(sessionTimeoutMinutes);
        });

    builder.Services.AddControllersWithViews()
        .AddMicrosoftIdentityUI();

    // The solution's only request throttle, deliberately scoped to the Engage recovery route
    // rather than applied globally: it is a first-of-its-kind change and its blast radius stays
    // inside a feature nobody is using yet.
    builder.Services.AddRateLimiter(DXSuite.Director.Services.RecoveryRateLimiting.Configure);

    // Upload size limit (500 MB)
    builder.Services.Configure<FormOptions>(options =>
    {
        options.MultipartBodyLengthLimit = 524_288_000;
    });
} // end isWebRole

// ── Always registered (both web and worker) ──────────────────────────────────

// ── Data Protection key persistence ─────────────────────────────────────────
// Without explicit key persistence, ASP.NET Core stores keys in a per-process
// temp directory. On IIS, each app pool recycle starts a fresh process, which
// generates a new key ring and makes every previously-encrypted credential
// (SMTP, LLM API keys, cloud connector configs, ITSM credentials) unreadable.
//
// Keys are stored on the file system so they survive restarts, then encrypted
// with Windows DPAPI bound to the local machine. Machine-binding means the keys
// are readable only on this server — copying the key directory to another machine
// does not expose the protected values.
//
// ProtectKeysWithDpapi is Windows-only; this deployment targets IIS on Windows.
// If the application is ever run on Linux, swap for ProtectKeysWithCertificate.
var dpKeyPath = builder.Configuration["DataProtection:KeyPath"]
    ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "DXSuite", "DataProtection-Keys");

Directory.CreateDirectory(dpKeyPath);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dpKeyPath))
    .ProtectKeysWithDpapi(protectToLocalMachine: true)
    .SetApplicationName("DXSuite");

// IHttpContextAccessor is needed by scoped DbContext factories (Phase 2) in both tiers.
builder.Services.AddHttpContextAccessor();

// CORS for signage device API endpoints
builder.Services.AddCors(options =>
{
    options.AddPolicy("SignageDevices", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

// Options
builder.Services.Configure<CustomerInfo>(builder.Configuration.GetSection("CustomerInfo"));
builder.Services.Configure<FeatureFlags>(builder.Configuration.GetSection("Features"));

// Multi-tenant resolution — must be registered before module services.
// ConfigTenantStore (Option B, appsettings) is the default. SqlTenantStore (Option A, SQL)
// overrides it when ConnectionStrings:TenantRegistry is present and is also registered as a
// hosted service so it can eagerly load and background-refresh the tenant cache.
builder.Services.AddTenantResolution(builder.Configuration);

var registryConnStr = builder.Configuration.GetConnectionString("TenantRegistry");
if (!string.IsNullOrEmpty(registryConnStr))
{
    builder.Services.AddDbContext<DXSuite.Shared.Entities.Registry.TenantRegistryDbContext>(opts =>
        opts.UseSqlServer(registryConnStr));
    builder.Services.AddScoped<DXSuite.Shared.Entities.Registry.ITenantRegistryDbContext>(sp =>
        sp.GetRequiredService<DXSuite.Shared.Entities.Registry.TenantRegistryDbContext>());
    // SqlTenantStore replaces ConfigTenantStore (last registration wins in ASP.NET Core DI).
    builder.Services.AddSingleton<DXSuite.Host.Multitenancy.SqlTenantStore>();
    builder.Services.AddSingleton<ITenantStore>(sp =>
        sp.GetRequiredService<DXSuite.Host.Multitenancy.SqlTenantStore>());
    builder.Services.AddHostedService(sp =>
        sp.GetRequiredService<DXSuite.Host.Multitenancy.SqlTenantStore>());
    builder.Services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();
}
else
{
    builder.Services.AddScoped<ITenantProvisioningService, NullTenantProvisioningService>();
}

// Central platform identity DB — optional; registered only when the Platform connection string is
// configured. When absent (single-tenant or dev), IPlatformIdentityService resolves to null and
// SessionState skips bootstrap silently. No Platform DB means no tenant switcher.
var platformConnStr = builder.Configuration.GetConnectionString("Platform");
if (!string.IsNullOrEmpty(platformConnStr))
{
    builder.Services.AddDbContext<DXSuite.Shared.Entities.Identity.PlatformDbContext>(opts =>
        opts.UseSqlServer(platformConnStr));
    builder.Services.AddScoped<DXSuite.Shared.Entities.Identity.IPlatformDbContext>(sp =>
        sp.GetRequiredService<DXSuite.Shared.Entities.Identity.PlatformDbContext>());
    builder.Services.AddScoped<DXSuite.Shared.Services.Identity.IPlatformIdentityService,
        DXSuite.Shared.Services.Identity.PlatformIdentityService>();
    builder.Services.AddScoped<DXSuite.Shared.Services.Identity.IPlatformElevationService,
        DXSuite.Shared.Services.Identity.PlatformElevationService>();
    builder.Services.AddScoped<DXSuite.Shared.Services.Identity.IPlatformAuditService,
        DXSuite.Shared.Services.Identity.PlatformAuditService>();
    builder.Services.AddMemoryCache();
}

// Re-authentication confirmation — scoped so each circuit gets its own state machine.
builder.Services.AddScoped<DXSuite.Shared.Services.ReAuthConfirmationService>();

// Media store. When Storage:BlobEndpoint is configured, use Azure Blob (one container per tenant)
// authenticated with the same DefaultAzureCredential as Key Vault; otherwise the local-disk store.
// All file I/O goes through IMediaStore, so no consumer code changes either way.
var blobEndpoint = builder.Configuration["Storage:BlobEndpoint"];
if (!string.IsNullOrWhiteSpace(blobEndpoint))
{
    builder.Services.AddSingleton(new Azure.Storage.Blobs.BlobServiceClient(
        new Uri(blobEndpoint), new Azure.Identity.DefaultAzureCredential()));
    builder.Services.AddSingleton<DXSuite.Shared.Storage.IMediaStore,
        DXSuite.Host.Storage.AzureBlobMediaStore>();
}
else
{
    builder.Services.AddSingleton<DXSuite.Shared.Storage.IMediaStore,
        DXSuite.Host.Storage.LocalDiskMediaStore>();
}

// Health checks — liveness (/health) and readiness (/health/ready).
// Readiness probes each configured tenant's Director DB and the Platform DB (when present).
var healthBuilder = builder.Services.AddHealthChecks();
healthBuilder.AddCheck<DXSuite.Host.Health.TenantDbHealthCheck>(
    "tenant-dbs", tags: ["ready"]);
if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString("Platform")))
{
    var platformConnStrForHealth = builder.Configuration.GetConnectionString("Platform")!;
    healthBuilder.AddCheck("platform-db",
        () =>
        {
            try
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(platformConnStrForHealth);
                conn.Open();
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy();
            }
            catch (Exception ex)
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy(ex.Message);
            }
        },
        tags: ["ready"]);
}
if (!string.IsNullOrEmpty(registryConnStr))
{
    var registryConnStrForHealth = registryConnStr;
    healthBuilder.AddCheck("registry-db",
        () =>
        {
            try
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(registryConnStrForHealth);
                conn.Open();
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy();
            }
            catch (Exception ex)
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy(ex.Message);
            }
        },
        tags: ["ready"]);
}

// Connectors module is always enabled (infrastructure for LLM, ITSM, cloud connectors)
var connectorsModule = new ConnectorsModule();

// Build module registry from config
var moduleRegistry = new List<IBlazorModule>();
moduleRegistry.Add(connectorsModule);

if (builder.Configuration.GetValue<bool>("Modules:Director:Enabled"))
    moduleRegistry.Add(new DirectorModule());
if (builder.Configuration.GetValue<bool>("Modules:CoreData:Enabled"))
    moduleRegistry.Add(new CoreDataModule());
#if MODULE_AVIATION
if (builder.Configuration.GetValue<bool>("Modules:Aviation:Enabled"))
    moduleRegistry.Add(new AviationModule());
#endif
#if MODULE_PULSE
if (builder.Configuration.GetValue<bool>("Modules:Pulse:Enabled"))
    moduleRegistry.Add(new PulseModule());
#endif
#if MODULE_STYLEGUIDE
if (builder.Configuration.GetValue<bool>("Modules:StyleGuide:Enabled"))
    moduleRegistry.Add(new StyleGuideModule());
#endif
#if MODULE_SENTINEL
if (builder.Configuration.GetValue<bool>("Modules:Sentinel:Enabled"))
    moduleRegistry.Add(new SentinelModule());
#endif
#if MODULE_DESIGN
if (builder.Configuration.GetValue<bool>("Modules:Design:Enabled"))
    moduleRegistry.Add(new DesignModule());
#endif
#if MODULE_NETJETS
if (builder.Configuration.GetValue<bool>("Modules:NetJets:Enabled"))
    moduleRegistry.Add(new NetJetsModule());
#endif
#if MODULE_SLC
if (builder.Configuration.GetValue<bool>("Modules:SLC:Enabled"))
    moduleRegistry.Add(new SLCModule());
#endif
#if MODULE_DEVICEPROXY
// Defaults off: the proxy forwards to internal addresses, so a deployment opts in rather than
// inheriting routes it never asked for.
if (builder.Configuration.GetValue<bool>("Modules:DeviceProxy:Enabled"))
    moduleRegistry.Add(new DeviceProxyModule());
#endif
if (isWebRole)
    moduleRegistry.Add(new PlatformModule(builder.Configuration));

// Register each module's services
foreach (var module in moduleRegistry)
    module.RegisterServices(builder.Services, builder.Configuration);

// Make module list available via DI
builder.Services.AddSingleton<IReadOnlyList<IBlazorModule>>(moduleRegistry);

builder.Services.AddScoped<PostLoginNavigationState>();

// Local/offline escape hatch's mock flight feed (see the Database:UseSqlite block below) — cheap to
// register unconditionally; it only becomes reachable when its endpoints are mapped under that flag.
builder.Services.AddSingleton<DXSuite.Host.Services.MockCoreApi.MockCoreApiStore>();

var app = builder.Build();

// Eager resolution of ITenantStore ensures the fail-fast (no tenants configured)
// fires at process start rather than on the first inbound request.
_ = app.Services.GetRequiredService<DXSuite.Shared.Multitenancy.ITenantStore>();

// Local/offline escape hatch (Database:UseSqlite): this product's schema is normally owned by SQL
// Server EF Core migrations, applied by hand (see CONTRIBUTING.md). A fresh SQLite file has no
// migration history to apply, so build each module's schema straight from the current model
// instead. Safe to run every time — EnsureCreatedAsync is a no-op once the file has tables.
if (DXSuite.Shared.Services.LocalSqliteProvider.IsEnabled(builder.Configuration))
{
    using var sqliteInitScope = app.Services.CreateScope();
    var sqliteInitServices = sqliteInitScope.ServiceProvider;

    async Task EnsureCreatedAsync<TContext>() where TContext : DbContext
    {
        var factory = sqliteInitServices.GetService<IDbContextFactory<TContext>>();
        if (factory is null) return; // module not registered (disabled or not built in)
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }

    await EnsureCreatedAsync<DXSuite.Director.Entities.DirectorDbContext>();
    await EnsureCreatedAsync<DXSuite.CoreData.Entities.CoreDataDbContext>();
#if MODULE_AVIATION
    await EnsureCreatedAsync<DXSuite.Aviation.Entities.AviationDbContext>();
#endif
#if MODULE_SENTINEL
    await EnsureCreatedAsync<DXSuite.Sentinel.Entities.SentinelDBContext>();
#endif
#if MODULE_DESIGN
    await EnsureCreatedAsync<DXSuite.Design.Entities.DesignDbContext>();
#endif
#if MODULE_SLC
    await EnsureCreatedAsync<DXSuite.SLC.Data.SlcPulseDbContext>();
#endif
    // DesignDbContext's sibling IdentityDbContext maps a subset of Director's own tables onto the
    // same SQLite file (see DXSuite.Design/Services/Extensions.cs) — Director's EnsureCreatedAsync
    // above already created them; running it a second time from the narrower model would conflict.

    // There is no real CoreAPI/RMS server in local mode, so Flight Management and Counter Allocation
    // would otherwise show nothing. Mount a minimal in-process stand-in and point the tenant's CoreAPI
    // connection at it automatically, the first time only — a real connection saved later is left alone.
    app.MapMockCoreApiEndpoints();

    var coreApiSettings = sqliteInitServices.GetService<DXSuite.Connectors.Services.CoreApi.ICoreApiSettingsStore>();
    if (coreApiSettings is not null && await coreApiSettings.ReadAsync(default) is null)
    {
        var selfUrl = (builder.Configuration["urls"] ?? builder.Configuration["ASPNETCORE_URLS"] ?? "http://localhost:5298")
            .Split(';')[0].TrimEnd('/');
        await coreApiSettings.SaveAsync("local-mock-key", [selfUrl + DXSuite.Host.Services.MockCoreApi.MockCoreApiEndpoints.BasePath], null, default);
    }

    // A fresh SQLite file has no users at all. Seed one ready-made Admin account so this mode is
    // usable immediately — only when the file is genuinely empty; anyone who has already registered
    // keeps whatever role they have.
    var directorFactory = sqliteInitServices.GetService<IDbContextFactory<DXSuite.Director.Entities.DirectorDbContext>>();
    if (directorFactory is not null)
    {
        await using var usersDb = await directorFactory.CreateDbContextAsync();
        if (!await usersDb.TblUsers.AnyAsync())
        {
            // The four built-in roles (tblUserRole RoleId 1-4) are rows this product's real SQL Server
            // database has always had, never captured as EF Core seed data (HasData) or a migration — on
            // SQL Server they simply already exist. EnsureCreatedAsync builds the table from the model but
            // inserts nothing, so a fresh SQLite file needs them written by hand before any TblUser.RoleId
            // foreign key can point at one.
            if (!await usersDb.TblUserRoles.AnyAsync())
            {
                usersDb.TblUserRoles.AddRange(
                    new TblUserRole { RoleId = (int)DXSuite.Shared.Authorization.Roles.Role.Admin, RoleName = "Admin", IsSystem = true },
                    new TblUserRole { RoleId = (int)DXSuite.Shared.Authorization.Roles.Role.Architect, RoleName = "Architect", IsSystem = true },
                    new TblUserRole { RoleId = (int)DXSuite.Shared.Authorization.Roles.Role.Contributor, RoleName = "Contributor", IsSystem = true },
                    new TblUserRole { RoleId = (int)DXSuite.Shared.Authorization.Roles.Role.NewUser, RoleName = "New User", IsSystem = true });
                await usersDb.SaveChangesAsync();
            }

            // RolePermissionService lazily seeds each role's permission grants the first time any role's
            // permissions are asked for; do that now so the Admin role seeded above actually holds every
            // known permission once RolePermissionService.EnsureDefaultsSyncedAsync runs.
            var rolePermissions = sqliteInitServices.GetService<DXSuite.Shared.Services.Identity.IRolePermissionService>();
            if (rolePermissions is not null)
                await rolePermissions.GetPermissionsForRoleAsync((int)DXSuite.Shared.Authorization.Roles.Role.Admin);

            var localAccounts = sqliteInitServices.GetRequiredService<DXSuite.Shared.Services.Identity.ILocalAccountService>();
            var seededAdmin = await localAccounts.CreateAccountAsync("admin@local.test", "LocalAdmin#2026");
            await usersDb.TblUsers.Where(u => u.UserId == seededAdmin.UserId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.RoleId, (int)DXSuite.Shared.Authorization.Roles.Role.Admin));
        }
    }
}

// Absorb truncated-request exceptions from Kestrel. Devices in the field often have
// a heartbeat POST in-flight when DXSuite restarts; the OS delivers the half-arrived
// request and Kestrel throws BadHttpRequestException before any controller runs.
// This is harmless — the device retries on its next sync cycle.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Microsoft.AspNetCore.Server.Kestrel.Core.BadHttpRequestException)
    {
        if (!context.Response.HasStarted)
            context.Response.StatusCode = 400;
    }
});

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Re-execution replays the request against /not-found while keeping the original method, and
// that page is a Razor component, which only allows GET and POST. A failing PUT or DELETE
// therefore comes back as 405 "GET, POST" instead of its real status — the underlying error is
// discarded and the response actively misleads. Browsers get the friendly page; API callers get
// the status their client can act on.
app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();

// Security headers + version stamp
var appVersion = System.Reflection.CustomAttributeExtensions
    .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(System.Reflection.Assembly.GetExecutingAssembly())
    ?.InformationalVersion ?? "dev";
app.Use(async (context, next) =>
{
    // Suppress X-Frame-Options for HTML files under /MediaAssets so airline mask templates
    // uploaded via the Ticket Counter Scheduler can be loaded in Pixera's CEF iframes.
    // All other responses keep the deny policy.
    bool isHtmlMaskAsset =
        context.Request.Path.StartsWithSegments("/MediaAssets") &&
        context.Request.Path.Value?.EndsWith(".html", StringComparison.OrdinalIgnoreCase) == true;

    // Location bundle content renders inside Location.razor's same-origin iframe;
    // its controller sets SAMEORIGIN + frame-ancestors 'self' on HTML responses itself.
    bool isLocationBundleContent =
        context.Request.Path.StartsWithSegments("/api/location-bundles/content");

    if (!isHtmlMaskAsset && !isLocationBundleContent)
        context.Response.Headers["X-Frame-Options"] = "DENY";

    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["X-App-Version"] = appVersion;
    await next();
});

// WebSocket support (used by Sentinel gateway in both tiers)
app.UseWebSockets();

// Tenant resolution — populates ITenantContext for all downstream middleware.
// Runs before auth so tenant config is available when OIDC and cookie schemes run.
app.UseTenantResolution();

if (isWebRole)
{
    // Heartbeat capture for player-consumed URLs carrying an id= parameter
    // (Director URLs monitoring). After tenant resolution so recording is
    // tenant-correct; before endpoints so both page loads and API polls count.
    app.UseMiddleware<DXSuite.Director.Services.UrlHeartbeatMiddleware>();

    // Validates the tenant path token on /MediaAssets and strips it, so the rewrite and the
    // static handler below need no knowledge of it. Stands in front of the media root rather
    // than beside it, so it must precede UseTenantMedia.
    app.UseMiddleware<DXSuite.Director.Services.MediaAssetTokenMiddleware>();

    // Rewrites tenant-content URLs (/MediaAssets/..., etc.) to the resolved tenant's media
    // root and blocks direct /tenants/** requests. Must precede UseStaticFiles.
    app.UseTenantMedia();

    // Static files with support for unknown file types (.ipk, etc.)
    app.UseStaticFiles(new StaticFileOptions
    {
        ServeUnknownFileTypes = true,
        OnPrepareResponse = ctx =>
        {
            // HTML mask files under /MediaAssets are loaded by Pixera inside iframes.
            // The global X-Frame-Options: DENY header (set in the security middleware above)
            // prevents them from rendering, so remove it selectively for these files only.
            // Contains rather than StartsWithSegments: the tenant media middleware may have
            // rewritten the path to /tenants/{subdomain}/MediaAssets/....
            if (ctx.Context.Request.Path.Value?.Contains("/MediaAssets/", StringComparison.OrdinalIgnoreCase) == true &&
                ctx.Context.Request.Path.Value.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Context.Response.Headers.Remove("X-Frame-Options");
            }

            // Branding images are replaced at runtime, so prevent stale browser caching.
            if (ctx.Context.Request.Path.StartsWithSegments(DXSuite.Shared.Storage.BrandingStorage.RequestPath))
                ctx.Context.Response.Headers.CacheControl = "no-cache";
        }
    });

    // Branding images live in wwwroot/images/branding (served by the middleware above) and are
    // seeded from images/branding-defaults so a fresh install shows defaults until customized.
    DXSuite.Shared.Storage.BrandingStorage.EnsureSeeded(app.Environment);

    // Runs here rather than in ThumbnailBackfillService so the pre-tenant-scoping folder is gone
    // before the server accepts requests — while it exists the static middleware serves it anonymously.
    DXSuite.Director.Services.WebPreviewService.RemoveLegacyThumbnails(
        app.Environment, app.Services.GetRequiredService<ILogger<DXSuite.Director.Services.WebPreviewService>>());

    app.UseCors("SignageDevices");

    // Session (used by external integration launch tokens)
    if (extIntegrationConfig.Enabled)
        app.UseSession();

    // Authentication & Authorization
    // Must run unconditionally so device/admin API key schemes work even when no
    // interactive login providers are configured (e.g. SSO-only environments).
    app.UseAuthentication();

    // JIT elevation enforcement — operators must hold a live TblPlatformElevation row
    // for the resolved tenant before any tenant-scoped request proceeds.
    // No-op when Platform DB is not configured (IPlatformDbContext resolves to null).
    app.UsePlatformElevation();

    app.UseAuthorization();

    app.UseRateLimiter();

    // Request localization (en, fr)
    var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("fr") };
    app.UseRequestLocalization(new RequestLocalizationOptions
    {
        DefaultRequestCulture = new RequestCulture("en"),
        SupportedCultures = supportedCultures,
        SupportedUICultures = supportedCultures
    });

    app.UseAntiforgery();

    // Module endpoint registration (controllers, middleware)
    foreach (var module in moduleRegistry)
        module.MapEndpoints(app, app);

    // Idle timeout redirect — signs out and returns to login.
    // The scheme is explicit because the Microsoft branch above configures only
    // DefaultAuthenticateScheme and DefaultChallengeScheme. With no DefaultScheme,
    // DefaultSignInScheme or DefaultSignOutScheme set, a bare SignOutAsync() resolves no
    // scheme and throws, which the exception handler re-executes in place — leaving the user
    // on /session-timeout looking at a dead login page. Matches AuthController.Logout.
    app.MapGet("/session-timeout", async (HttpContext context) =>
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        context.Response.Redirect("/");
    }).AllowAnonymous();

    // Session keepalive for Blazor circuits. Circuit traffic runs over a WebSocket and never
    // reaches the cookie middleware, so without this ping the sliding auth cookie expires
    // under an actively working user and the UI keeps rendering a session the server has
    // already dropped. Anonymous so an expired cookie yields a plain 401 the client can act
    // on rather than an OIDC challenge redirect.
    app.MapGet("/auth/keepalive", (HttpContext context, IOptions<AuthenticationConfig> authOptions) =>
    {
        context.Response.Headers.CacheControl = "no-store";

        // Demo mode has no cookie to expire; reporting it dead would bounce the user through
        // /session-timeout straight back into an authorized session.
        var live = authOptions.Value.DemoMode || context.User.Identity?.IsAuthenticated == true;
        return live ? Results.NoContent() : Results.Unauthorized();
    }).AllowAnonymous();

    // Health endpoints — always allow anonymous so load balancers can probe without auth.
    app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => true
    }).AllowAnonymous();
    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = hc => hc.Tags.Contains("ready")
    }).AllowAnonymous();

    app.MapStaticAssets();
    app.MapControllers();
    app.MapHub<DXSuite.Director.Hubs.PlayerWakeHub>("/hubs/player-wake");
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode()
        .AddAdditionalAssemblies(moduleRegistry
            .Select(m => m.Assembly)
            .Where(a => a != typeof(App).Assembly)
            .Distinct()
            .ToArray());
} // end isWebRole

app.Run();
