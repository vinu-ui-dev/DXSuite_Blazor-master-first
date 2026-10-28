using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DXSuite.Sentinel.Entities;
using DXSuite.Sentinel.Services.Automation;
using DXSuite.Sentinel.Services.Drivers;
using DXSuite.Sentinel.Services.Engine;
using DXSuite.Sentinel.Services.Gateway;
using DXSuite.Sentinel.Services.Mqtt;
using DXSuite.Sentinel.Services.Pulse;
using DXSuite.Sentinel.Services.Reporter;
using DXSuite.Sentinel.Services.Routing;
using DXSuite.Sentinel.Services.Tasks;
using DXSuite.Connectors.Services.Itsm;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Services;
using Microsoft.AspNetCore.Http;

namespace DXSuite.Sentinel.Services;

public static class Extensions
{
    public static IServiceCollection AddSentinelModule(this IServiceCollection services, IConfiguration config)
    {
        // "web" role hosts the Blazor/API front-end only; background workers run in "worker" or "standalone".
        var runBackgroundServices = config["DXSuite:Role"] is not "web";
        // Scoped factory: options delegate runs once per DI scope, giving each request/circuit
        // its own connection string from ITenantContext. AddPooledDbContextFactory would bake
        // DbContextOptions as a singleton, making per-tenant routing unreachable.
        // Resolve ITenantContext from the current scope first: worker scopes created by
        // IWorkerScopeFactory carry a populated tenant but no HttpContext.
        services.AddDbContextFactory<SentinelDBContext>((sp, options) =>
        {
            var tenantCtx = sp.GetService<ITenantContext>();

            // Circuit scopes hold an unpopulated TenantContext until TenantCircuitInitializer runs.
            if (tenantCtx?.IsPopulated != true)
            {
                var httpCtx = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
                var httpTenantCtx = httpCtx?.RequestServices.GetService<ITenantContext>();
                tenantCtx = httpTenantCtx?.IsPopulated == true ? httpTenantCtx : null;
            }

            var connStr = tenantCtx != null
                ? tenantCtx.SentinelConnectionString
                  ?? throw new InvalidOperationException(
                      $"Tenant '{tenantCtx.Subdomain}' does not license the Sentinel module.")
                : config.GetConnectionString("Sentinel")
                  ?? throw new InvalidOperationException(
                      "No Sentinel connection string in app config (background service path).");
            options.UseConfiguredProvider(config, "Sentinel", connStr);
        }, ServiceLifetime.Scoped);
        services.AddScoped<SentinelDBContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<SentinelDBContext>>().CreateDbContext());
        services.AddScoped<ISentinelDBContext>(sp =>
            sp.GetRequiredService<SentinelDBContext>());

        // ──────────────────────────────────────────────────────────────────
        // Sentinel availability
        // ──────────────────────────────────────────────────────────────────
        // Availability service — hosted on ALL roles: every replica must run the probe loop so that
        // Blazor pages (SentinelDashboard, Automation, PulseDashboard) see IsAvailable=true on web
        // replicas. The probe is a single read-only query; concurrent probes across replicas are safe.
        services.AddSingleton<SentinelAvailabilityService>();
        services.AddSingleton<ISentinelAvailabilityService>(sp => sp.GetRequiredService<SentinelAvailabilityService>());
        services.AddHostedService(sp => sp.GetRequiredService<SentinelAvailabilityService>());

        // ──────────────────────────────────────────────────────────────────
        // MQTT control plane
        // ──────────────────────────────────────────────────────────────────
        // Single shared broker connection — hosted on all roles (self-gates on
        // Sentinel:Mqtt:Enabled, no-op when disabled). Gateway transport and device
        // telemetry ingestion publish/subscribe through it.
        var mqttOptions = config.GetSection(SentinelMqttOptions.SectionName).Get<SentinelMqttOptions>()
            ?? new SentinelMqttOptions();
        services.AddSingleton(mqttOptions);
        services.AddSingleton<MqttConnectionService>();
        services.AddSingleton<ISentinelMessageBus>(sp => sp.GetRequiredService<MqttConnectionService>());
        services.AddHostedService(sp => sp.GetRequiredService<MqttConnectionService>());

        // ──────────────────────────────────────────────────────────────────
        // ITSM connection provider (Sentinel owns ITSM config)
        // ──────────────────────────────────────────────────────────────────
        services.AddSingleton<IItsmConnectionProvider, SentinelItsmConnectionProvider>();

        // ──────────────────────────────────────────────────────────────────
        // Pulse reporting
        // ──────────────────────────────────────────────────────────────────
        // All roles: the publisher runs on workers, but the config tabs on web replicas read the same settings.
        services.AddSingleton<PulseReportingSettingsResolver>();
        services.AddScoped<PulseGateway>();
        services.AddScoped<PulseIssueLevelService>();
        services.AddScoped<PulseLinkService>();

        // ──────────────────────────────────────────────────────────────────
        // Core services
        // ──────────────────────────────────────────────────────────────────
        services.AddSingleton<DeviceDescriptorSchema>();
        services.AddSingleton<DeviceDescriptorValidator>();
        services.AddSingleton<ProbeExecutionService>();

        // Represents self-monitoring agent hosts as DeviceInstances (shared by ingestion + gateway manager).
        services.AddSingleton<AgentHostRegistry>();

        // Distributed lock — background services serialize their cycles through this.
        // NoOp is correct for single-instance (standalone) hosting; multi-instance
        // deployments must swap in a SQL-backed lock so replicas don't run the engine
        // concurrently. Mirrors the Failover:Enabled pattern in the Director module.
        services.AddSingleton<IDistributedLockService, NoOpDistributedLockService>();

        // ──────────────────────────────────────────────────────────────────
        // Engine (evaluation pipeline)
        // ──────────────────────────────────────────────────────────────────
        services.AddSingleton<IDeviceEvaluator, DeviceEvaluator>();
        services.AddSingleton<IGuardrailService, GuardrailService>();

        // ──────────────────────────────────────────────────────────────────
        // Automation
        // ──────────────────────────────────────────────────────────────────
        services.AddSingleton<IAutomationExecutor, AutomationExecutor>();
        // AutomationContext is created per-execution by AutomationExecutor with the
        // device-specific context (device id, type, dry-run); it is not DI-resolvable.

        // ──────────────────────────────────────────────────────────────────
        // Drivers
        // ──────────────────────────────────────────────────────────────────
        services.AddSingleton<DeviceDriverRegistry>();
        services.AddSingleton<IDeviceDriver, LgDisplayDriver>();
        services.AddSingleton<IDeviceDriver, HttpHeartbeatDriver>();
        services.AddSingleton<IDeviceDriver, IcmpPingDriver>();
        services.AddSingleton<IDeviceDriver, BrightSignDwsDriver>();

        // ──────────────────────────────────────────────────────────────────
        // Gateway
        // ──────────────────────────────────────────────────────────────────
        // Transport-selected: MQTT-backed manager when MQTT is enabled and configured for
        // the gateway; otherwise the legacy WebSocket acceptor. Both satisfy the same
        // IGatewayConnectionManager contract consumed by CommandRouter / ProbeExecutionService.
        // Agent self-enrollment (bulk unattended). Scoped: runs in the caller's tenant request.
        services.AddScoped<IGatewayEnrollmentService, GatewayEnrollmentService>();

        if (mqttOptions.Enabled && mqttOptions.GatewayOverMqtt)
        {
            services.AddSingleton<MqttGatewayConnectionManager>();
            services.AddSingleton<IGatewayConnectionManager>(sp => sp.GetRequiredService<MqttGatewayConnectionManager>());
            services.AddHostedService(sp => sp.GetRequiredService<MqttGatewayConnectionManager>());
        }
        else
        {
            services.AddSingleton<IGatewayConnectionManager, GatewayConnectionManager>();
        }

        // ──────────────────────────────────────────────────────────────────
        // Routing & Reporter
        // ──────────────────────────────────────────────────────────────────
        services.AddSingleton<ICommandRouter, CommandRouter>();
        services.AddSingleton<IReportProcessor, ReportProcessor>();
        services.AddSingleton<ReporterEngine>();

        // ──────────────────────────────────────────────────────────────────
        // Tasks (scripting engine)
        // ──────────────────────────────────────────────────────────────────
        services.AddSingleton<ITaskExecutionService, TaskExecutionService>();

        // ──────────────────────────────────────────────────────────────────
        // Background services
        // ──────────────────────────────────────────────────────────────────
        if (runBackgroundServices)
        {
            services.AddHostedService<ServiceTechEngine>();
            services.AddHostedService<PulsePublisherEngine>();
            services.AddHostedService<CloudConnectorSyncService>();
            services.AddHostedService<ProbeHistoryCleanupService>();
            services.AddHostedService<ReporterEngine>(sp => sp.GetRequiredService<ReporterEngine>());
            services.AddHostedService<SentinelIngestionService>();
            services.AddHostedService<SentinelTaskSchedulerService>();
        }

        return services;
    }
}
