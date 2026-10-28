using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FFMpegCore;
using Newtonsoft.Json;
using RedLockNet;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;
using DXSuite.Director.Authorization;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Director.Services.Atrius;
using DXSuite.Director.Services.DistributedLock;
using DXSuite.Director.Services.PtzCameras;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Services;
using DXSuite.Shared.Services.Identity;
using Microsoft.AspNetCore.Http;

namespace DXSuite.Director.Services;

public static class Extensions
{
    public static IServiceCollection AddDirectorModule(this IServiceCollection services, IConfiguration config)
    {
        // "web" role hosts the Blazor/API front-end only; background workers run in "worker" or "standalone".
        var runBackgroundServices = config["DXSuite:Role"] is not "web";
        // Scoped factory (not pooled): options delegate runs once per DI scope, giving each
        // HTTP request and Blazor circuit its own connection string from ITenantContext.
        // AddPooledDbContextFactory would bake DbContextOptions as a process-lifetime singleton,
        // making tenant routing in the delegate unreachable — all callers would share whichever
        // connection string was resolved at startup.
        // Resolve ITenantContext directly from the current scope first. Worker scopes created by
        // IWorkerScopeFactory have ITenantContext pre-populated but no HttpContext, so checking
        // HttpContextAccessor alone would fall through to the top-level connection string and
        // route every worker iteration to the wrong tenant DB.
        services.AddDbContextFactory<DirectorDbContext>((sp, options) =>
        {
            var tenantCtx = sp.GetService<ITenantContext>();

            // A scoped TenantContext exists in every DI scope but may not yet be populated
            // (e.g. Blazor Server circuit scopes before TenantCircuitInitializer runs).
            // Fall through to HttpContext and then config in that case.
            if (tenantCtx?.IsPopulated != true)
            {
                var httpCtx = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
                var httpTenantCtx = httpCtx?.RequestServices.GetService<ITenantContext>();
                if (httpTenantCtx?.IsPopulated == true)
                    tenantCtx = httpTenantCtx;
                else
                    tenantCtx = null;
            }

            // When a tenant context is present, its connection string is authoritative.
            // Falling back to app config when the tenant IS present but has no Director
            // license would silently connect to a different tenant's DB.
            var connStr = tenantCtx != null
                ? tenantCtx.DirectorConnectionString
                  ?? throw new InvalidOperationException(
                      $"Tenant '{tenantCtx.Subdomain}' does not license the Director module.")
                : config.GetConnectionString("Director")
                  ?? throw new InvalidOperationException(
                      "No Director connection string in app config (background service path).");
            options.UseSqlServer(connStr);
        }, ServiceLifetime.Scoped);
        services.AddScoped<DirectorDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<DirectorDbContext>>().CreateDbContext());
        services.AddScoped<IDirectorDBContext>(sp =>
            sp.GetRequiredService<DirectorDbContext>());

        // Options
        services.Configure<FfmpegOptions>(config.GetSection("Ffmpeg"));
        var ffmpegOptions = config.GetSection("Ffmpeg").Get<FfmpegOptions>() ?? new FfmpegOptions();
        GlobalFFOptions.Configure(new FFOptions
        {
            // Resolve relative paths against the content root; CurrentDirectory is the content
            // root at startup for both Kestrel and the in-process IIS module. Empty => PATH lookup.
            BinaryFolder = ffmpegOptions.ResolveFolder(Directory.GetCurrentDirectory()),
            TemporaryFilesFolder = Path.GetTempPath()
        });

        // Identity services (users, groups, metadata, session state)
        services.AddScoped<IIdentityDbContext>(sp => sp.GetRequiredService<DirectorDbContext>());
        services.AddIdentityServices();

        // Memory cache for SignageController and weather endpoints
        services.AddMemoryCache();

        // JSON serializer settings (Newtonsoft)
        services.AddSingleton(new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        });


        // ──────────────────────────────────────────────────────────────────
        // Singleton services
        // ──────────────────────────────────────────────────────────────────

        // Heartbeat — hosted on ALL roles: the flush timer must drain each replica's in-process queue.
        // Device payloads arrive at the web tier via SignageController; without the flush loop running
        // there, QueueHeartbeat accumulates indefinitely and heartbeats are silently lost.
        // Concurrent replica flushes are safe because each flush is a per-device UPSERT — lost writes
        // are benign for heartbeats (last-writer-wins on the heartbeat timestamp).
        services.AddSingleton<HeartbeatService>();
        services.AddSingleton<IHeartbeatService>(sp => sp.GetRequiredService<HeartbeatService>());
        services.AddHostedService(sp => sp.GetRequiredService<HeartbeatService>());

        services.AddSingleton<IDirectorService, DirectorService>();
        services.AddSingleton<IOverrideService, OverrideService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IBatchDeploymentService, BatchDeploymentService>();
        services.AddSingleton<IContentHealthService, ContentHealthService>();
        services.AddSingleton<ILgDisplayControlService, LgDisplayControlService>();
        // Durable device command queue; the singleton ICecCommandService bridges Sentinel callers into it
        services.AddScoped<IDeviceCommandService, DeviceCommandService>();
        services.AddSingleton<ICecCommandService, CecCommandService>();
        // Push-to-poll wake channel (hub mapped in the Host)
        services.AddSingleton<PlayerWakeConnectionTracker>();
        services.AddSingleton<NowPlayingStore>();
        services.AddSingleton<ContentManifestVersionTracker>();
        services.AddScoped<IContentManifestLogService, ContentManifestLogService>();
        services.AddSingleton<IPlayerWakeNotifier, PlayerWakeNotifier>();
        services.AddSingleton<IApiKeyService, ApiKeyService>();
        services.AddSingleton<IDeviceMetadataService, DeviceMetadataService>();
        services.AddSingleton<IDirectorLocationAccessService, LocationAccessService>();
        services.AddSingleton<ILocationAccessService>(sp => sp.GetRequiredService<IDirectorLocationAccessService>());
        services.AddScoped<IContentMetadataService, ContentMetadataService>();
        // Player sync cadence: scoped (tenant-bound DbContext) with a short per-tenant cache
        services.AddScoped<IPlayerSyncSettingsService, PlayerSyncSettingsService>();
        services.AddScoped<Filters.PlayerPollIntervalFilter>();
        services.AddScoped<Filters.PlayerOsTimeZoneFilter>();
        services.AddScoped<Filters.PlayerOrientationFilter>();
        services.AddScoped<Filters.PlayerVideoPathFilter>();
        // Poll read-path amortization: per-device flag cache + tenant override gate
        services.AddScoped<IPlayerPollFlagCache, PlayerPollFlagCache>();
        services.AddSingleton<IActiveOverrideGate, ActiveOverrideGate>();
        services.AddSingleton<IScreenshotService, ScreenshotService>();
        services.AddSingleton<ThumbnailService>();
        services.Configure<WebPreviewOptions>(config.GetSection("WebPreview"));
        services.AddSingleton<OutboundUrlGuard>();
        // One listener shared by all captures, started on first use
        services.AddSingleton<EgressFilteringProxy>();
        // Scoped: thumbnail paths are keyed by the request's resolved tenant
        services.AddScoped<WebPreviewService>();
        services.AddScoped<AssetThumbnailCleanupService>();
        services.AddHostedService<ThumbnailBackfillService>();
        services.AddHostedService<DurationBackfillService>();
        services.AddSingleton<IVideoProcessingService, VideoProcessingService>();
        services.AddSingleton<IFontService, FontService>();
        services.AddSingleton<IEmailService, SmtpEmailService>();
        services.AddSingleton<IWeatherService, WeatherService>();

        // Floor Plan and StatusSelector
        services.AddSingleton<ScheduleEvaluator>();
        services.AddSingleton<IFloorPlanService, FloorPlanService>();
        services.AddScoped<IStatusSelectorService, StatusSelectorService>();
        services.AddScoped<IPublicationInvalidator, PublicationInvalidator>();
        services.AddScoped<IDisplayOverrideService, DisplayOverrideService>();
        services.AddScoped<IScheduleEventService, ScheduleEventService>();
        services.AddScoped<ILocationPlaybackService, LocationPlaybackService>();
        services.AddScoped<IAssetSoftDeleteService, AssetSoftDeleteService>();

        // Control Client (Engage) services
        services.AddSingleton<IControlClientService, ControlClientService>();
        services.AddSingleton<IControlClientPairingService, ControlClientPairingService>();
        services.AddSingleton<IControlClientTokenService, ControlClientTokenService>();
        services.AddSingleton<IDevicePairingService, DevicePairingService>();
        services.AddSingleton<DeviceTokenCacheInvalidator>();
        services.AddScoped<IPlayerArtifactStore, PlayerArtifactStore>();
        services.AddScoped<IPlayerCallbackUrlResolver, PlayerCallbackUrlResolver>();
        services.AddScoped<IProvisioningAuthorizationService, ProvisioningAuthorizationService>();
        services.AddScoped<IProvisioningPackageService, ProvisioningPackageService>();
        services.AddScoped<IOffOriginHostReportService, OffOriginHostReportService>();
        services.AddScoped<IPairingGrantService, PairingGrantService>();
        services.AddScoped<IRecoveryHandlerService, RecoveryHandlerService>();
        services.AddSingleton<IDeviceDwsCredentialService, DeviceDwsCredentialService>();
        services.AddSingleton<IMediaAccessTokenService, MediaAccessTokenService>();
        services.AddSingleton<IDeviceTokenService, DeviceTokenService>();

        // Access control services
        services.AddSingleton<IAccessCacheStore, AccessCacheStore>();
        services.AddSingleton<IDisplayScheduleResolver, DisplayScheduleResolver>();

        // API security
        services.AddSingleton<ApiKeyRateLimiter>();

        // Authentication schemes for device tokens and admin API keys
        services.AddAuthentication()
            .AddScheme<DeviceTokenAuthOptions, DeviceTokenAuthHandler>(AuthSchemes.DeviceToken, _ => { })
            .AddScheme<AdminApiKeyAuthOptions, AdminApiKeyAuthHandler>(AuthSchemes.AdminApiKey, _ => { });

        // Email/credential services
        services.AddSingleton<SmtpCredentialProtector>();
        services.AddSingleton<ProvisioningSecretProtector>();
        // EmailTemplateBuilder is a static helper class — no DI registration needed

        // Cross-module provider implementations
        services.AddSingleton<IDeviceDataProvider, DirectorDeviceDataProvider>();
        services.AddSingleton<DXSuite.Connectors.Services.Connectors.IConnectorConfigProvider, DirectorConnectorConfigProvider>();
        services.AddSingleton<DXSuite.Connectors.Services.Llm.ILlmProviderConfigSource, DirectorLlmProviderConfigSource>();
        services.AddSingleton<DXSuite.Connectors.Services.Llm.ILlmInteractionSink, DirectorLlmInteractionSink>();

        // Video Wall — airline default asset libraries, cluster resolution, and platform abstraction
        services.AddSingleton<IUserGroupLocationDefaultService, UserGroupLocationDefaultService>();
        services.AddSingleton<IClusterResolutionService, ClusterResolutionService>();
        services.AddSingleton<IPixeraAllocationService, PixeraAllocationService>();
        services.AddSingleton<IWallContentBroadcastService, WallContentBroadcastService>();
        // Register each wall platform as IWallLayoutPlatform; the registry resolves all via IEnumerable.
        services.AddSingleton<WallPlatforms.IWallLayoutPlatform, WallPlatforms.PixeraWallLayoutPlatform>();
        services.AddSingleton<WallPlatforms.IWallLayoutPlatformRegistry, WallPlatforms.WallLayoutPlatformRegistry>();

        // Performance caching for SignageController
        services.AddSingleton<IManifestCacheService, ManifestCacheService>();
        services.AddSingleton<IMediaFolderCacheService, MediaFolderCacheService>();
        services.AddSingleton<IMetadataCacheService, MetadataCacheService>();

        // PTZ cameras
        services.AddSingleton<PtzCameraFactory>();

        // Pixera integration
        services.AddHttpClient<PixeraApiClient>();
        services.AddSingleton<Func<string, PixeraApiClient>>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            return ipAddress =>
            {
                var httpClient = httpClientFactory.CreateClient();
                var rpcUrl = $"http://{ipAddress}:1400/jsonrpc";
                return new PixeraApiClient(rpcUrl, httpClient);
            };
        });
        services.AddSingleton<PixeraService>();
        // Pixera Cloud Connector — registered in Director (not DXSuite.Connectors) because it
        // references PixeraApiClient and the FilePaths:BaseUNC pattern, both Director-specific.
        services.AddSingleton<ICloudPlatformConnector, Pixera.PixeraConnector>();

        // ──────────────────────────────────────────────────────────────────
        // Scoped services
        // ──────────────────────────────────────────────────────────────────

        services.AddScoped<CanvasImageGenerationService>();
        services.AddScoped<IUploadStateService, UploadStateService>();
        services.AddScoped<IBatchDeploymentStateService, BatchDeploymentStateService>();
        services.AddScoped<IConsoleLoggingService, ConsoleLoggingService>();
        services.AddScoped<IAutoHealService, AutoHealService>();
        services.AddScoped<IAssetApprovalService, AssetApprovalService>();
        services.AddScoped<IAssetIngestService, AssetIngestService>();
        services.AddScoped<IUploadIdentityService, UploadIdentityService>();
        services.AddScoped<IContentOverwriteService, ContentOverwriteService>();
        services.AddScoped<IEffectiveContentResolver, EffectiveContentResolver>();
        services.AddSingleton<DeliveryMetrics>();
        services.AddScoped<IMetadataSubjectResolver, MetadataSubjectResolver>();
        services.AddScoped<ILocationMetadataRuleService, LocationMetadataRuleService>();
        services.AddScoped<IContentScopeService, ContentScopeService>();
        services.AddScoped<IContentMetadataRuleEditor, ContentMetadataRuleEditor>();
        services.AddScoped<DXSuite.Shared.Services.Identity.IMetadataUsageContributor, DirectorMetadataUsageContributor>();
        services.AddSingleton<IContentModerationProvider, LlmModerationProvider>();
        services.AddScoped<IContentModerationService, ContentModerationService>();
        services.AddScoped<IDirectorUserPreferenceService, UserPreferenceService>();
        services.AddScoped<DXSuite.Shared.Services.IUserPreferenceService>(sp => sp.GetRequiredService<IDirectorUserPreferenceService>());
        services.AddScoped<IApprovalChainService, ApprovalChainService>();
        services.AddScoped<IDirectorNotificationService, AppNotificationService>();
        services.AddScoped<INotificationService>(sp => sp.GetRequiredService<IDirectorNotificationService>());
        services.AddScoped<DisplayScaleService>();

        // Content Package subsystem (Features:ContentPackage gated). Services are always registered so
        // the controller and UI can resolve them; behavior is no-op when the flag is off.
        services.AddScoped<ContentPackage.IContentPackageDependencyResolver, ContentPackage.ContentPackageDependencyResolver>();
        services.AddScoped<ContentPackage.IContentPackageBundleBuilder, ContentPackage.ContentPackageBundleBuilder>();
        services.AddScoped<ContentPackage.IContentPackageUserResolver, ContentPackage.ContentPackageUserResolver>();
        services.AddScoped<ContentPackage.IContentPackageBundleImporter, ContentPackage.ContentPackageBundleImporter>();
        services.AddScoped<ContentPackage.IContentPackageHistoryService, ContentPackage.ContentPackageHistoryService>();

        // Location Bundle subsystem (Features:LocationBundles gated). Services are always
        // registered so the controllers and UI can resolve them; endpoints 404 when the flag is off.
        services.Configure<LocationBundles.LocationBundleOptions>(config.GetSection("LocationBundles"));
        services.AddScoped<LocationBundles.ILocationBundleService, LocationBundles.LocationBundleService>();
        services.AddScoped<LocationBundles.ILocationBundleImporter, LocationBundles.LocationBundleImporter>();
        services.AddScoped<LocationBundles.ILocationAccessResolver, LocationBundles.LocationAccessResolver>();
        services.AddHttpClient(Controllers.LocationBundleContentController.DevProxyClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        // Panels (Features:Panels gated). Reuses bundle storage for its files but not the
        // bundle runtime, so it registers independently of the flag above.
        services.AddScoped<Panels.IPanelService, Panels.PanelService>();

        // Web-preview fetches target admin-entered URLs; the guard validates the resolved
        // address of every connection (including redirect hops) at socket-connect time.
        services.AddHttpClient(WebPreviewService.OutboundHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
            {
                ConnectCallback = sp.GetRequiredService<OutboundUrlGuard>().ConnectAsync,
                // With a proxy in play the guard would validate the proxy's address rather than
                // the target's, so the connect-time check must dial the destination directly.
                UseProxy = false
            });

        // ──────────────────────────────────────────────────────────────────
        // Transient services
        // ──────────────────────────────────────────────────────────────────

        services.AddTransient<AverCameraClient>();
        services.AddTransient<IAtriusControl, AtriusControl>();

        // ──────────────────────────────────────────────────────────────────
        // HttpClient factories
        // ──────────────────────────────────────────────────────────────────

        services.AddHttpClient<InkService>();

        // ──────────────────────────────────────────────────────────────────
        // Hosted (background) services
        // ──────────────────────────────────────────────────────────────────

        // Access cache refresh runs on ALL roles: the in-memory IAccessCacheStore is per-instance,
        // so every web replica must populate its own cache. Without this, DisplayScheduleResolver
        // reads an empty cache and all authorization decisions fall back to the uncached path.
        services.AddHostedService<AccessCacheRefreshService>();

        // Rendition lookup is registered on ALL roles (it only reads derived files
        // from disk; used by the allocation endpoint and the layout editor). The
        // encoding sweep itself runs with the other background services below.
        services.AddSingleton<BackgroundZoneDerivationService>();
        services.AddSingleton<IBackgroundRenditionService>(sp => sp.GetRequiredService<BackgroundZoneDerivationService>());

        if (runBackgroundServices)
        {
            services.AddHostedService(sp => sp.GetRequiredService<BackgroundZoneDerivationService>());
            services.AddHostedService<TempFileCleanupService>();
            services.AddHostedService<AssetCleanupService>();
            services.AddHostedService<OverrideScheduleService>();
            services.AddHostedService<BatchDeploymentBackgroundService>();
            services.AddHostedService<AutoHealBackgroundService>();
            services.AddHostedService<DeclinedContentCleanupService>();
            services.AddHostedService<ScheduleExpiryNotificationService>();
            services.AddHostedService<PairingCleanupService>();
            services.AddHostedService<ScreenshotRefreshService>();
            services.AddHostedService<EventLogRetentionService>();
            services.AddHostedService<InkContentSyncService>();
            services.AddHostedService<Pixera.PixeraContentSyncService>();
            services.AddHostedService<Pixera.PixeraLayerSyncService>();
            services.AddHostedService<Pixera.PixeraTimelineWatchdogService>();
            services.AddHostedService<AirlineScheduleMonitorService>();
            services.AddHostedService<ClusterLifecycleService>();
            services.AddHostedService<DefaultAssetActivatorService>();
            services.AddHostedService<CredentialExpiryNotificationService>();
            services.AddHostedService<ApiKeyExpiryNotificationService>();
            services.AddHostedService<DirectorMonitoringService>();
        }

        // URL heartbeat capture is request-driven (middleware) and runs on all
        // roles; only the sweeper above is background-role-gated.
        services.AddSingleton<UrlHeartbeatService>();
        services.AddSingleton<DXSuite.Shared.Services.IDirectorAlertSink, NotificationAlertSink>();

        // ──────────────────────────────────────────────────────────────────
        // Distributed locking (SQL-based or NoOp)
        // ──────────────────────────────────────────────────────────────────

        var failoverEnabled = config.GetValue<bool>("Failover:Enabled");
        if (failoverEnabled)
        {
            services.AddSingleton<DistributedLock.IDistributedLockService, DistributedLock.SqlDistributedLockService>();
        }
        else
        {
            services.AddSingleton<DistributedLock.IDistributedLockService, DistributedLock.NoOpDistributedLockService>();
        }

        return services;
    }
}
