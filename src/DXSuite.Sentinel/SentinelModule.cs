using System.Reflection;
using DXSuite.Sentinel.Contracts;
using DXSuite.Sentinel.Services;
using DXSuite.Sentinel.Services.Gateway;
using DXSuite.Shared.Module;
using DXSuite.Shared.Multitenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Sentinel;

public class SentinelModule : IBlazorModule
{
    public string Name => "Sentinel";

    public string Version => "2026.05";

    public Assembly Assembly => typeof(SentinelModule).Assembly;

    public bool IsAvailableForTenant(ITenantContext tenant) => tenant.Modules.Sentinel;

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSentinelModule(config);
    }

    public IEnumerable<NavItem> GetNavItems()
    {
        const string navFeature = nameof(DXSuite.Shared.Multitenancy.TenantFeatureFlags.ShowSentinelNav);
        yield return new NavItem("Dashboard", "/sentinel", "bi-activity", 50, "Sentinel", navFeature);
        yield return new NavItem("Automation", "/automation", "bi-robot", 51, "Sentinel", navFeature);
    }

    public IEnumerable<ConfigTab> GetConfigTabs()
    {
        const string opsFeature = nameof(DXSuite.Shared.Models.FeatureFlags.SentinelOpsConfigTabs);
        yield return new ConfigTab("gateways", "Agents", typeof(Components.SiteGatewaysTab), 55, opsFeature);
        yield return new ConfigTab("sentinel-devices", "Monitored Devices", typeof(Components.SentinelDevicesTab), 58, opsFeature);
        // Pulse Reporting is deliberately not gated by opsFeature: it configures Sentinel's own
        // issue reporting and has no dependency on the device-ops tabs above.
        yield return new ConfigTab("sentinel-pulse-reporting", "Pulse Reporting", typeof(Components.PulseReportingTab), 59);
        yield return new ConfigTab("device-models", "Device Models", typeof(Components.DeviceModelsTab), 60, opsFeature);
        yield return new ConfigTab("probe-profiles", "Probe Profiles", typeof(Components.SentinelProbeProfilesTab), 65, opsFeature);
        yield return new ConfigTab("tasks", "Tasks", typeof(Components.TasksTab), 68, opsFeature);
        yield return new ConfigTab("itsm", "ITSM", typeof(Components.SentinelItsmTab), 70, opsFeature);
        yield return new ConfigTab("guardrail-policies", "Guardrail Policies", typeof(Components.SentinelPoliciesTab), 80, opsFeature);
    }

    public IEnumerable<SettingsTab> GetSettingsTabs()
    {
        yield break; // No settings tabs initially
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IApplicationBuilder app)
    {
        // Gateway WebSocket middleware at /ws/gateway
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments("/ws/gateway"),
            branch => branch.UseMiddleware<Middleware.GatewayWebSocketMiddleware>());

        // Agent self-enrollment (anonymous; gated by the hashed enrollment token in the request body).
        endpoints.MapPost("/api/sentinel/gateway/enroll",
            async (GatewayEnrollRequest req, IGatewayEnrollmentService svc, CancellationToken ct) =>
            {
                var resp = await svc.EnrollAsync(req, ct);
                return resp.Success ? Results.Ok(resp) : Results.BadRequest(resp);
            })
            .AllowAnonymous();
    }
}
