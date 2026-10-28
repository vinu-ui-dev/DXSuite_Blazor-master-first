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
        yield return new NavItem("Dashboard", "/sentinel", "bi-activity", 50, "Sentinel");
        yield return new NavItem("Automation", "/automation", "bi-robot", 51, "Sentinel");
    }

    public IEnumerable<ConfigTab> GetConfigTabs()
    {
        yield return new ConfigTab("gateways", "Agents", typeof(Components.SiteGatewaysTab), 55);
        yield return new ConfigTab("sentinel-devices", "Monitored Devices", typeof(Components.SentinelDevicesTab), 58);
        yield return new ConfigTab("sentinel-pulse-reporting", "Pulse Reporting", typeof(Components.PulseReportingTab), 59);
        yield return new ConfigTab("device-models", "Device Models", typeof(Components.DeviceModelsTab), 60);
        yield return new ConfigTab("probe-profiles", "Probe Profiles", typeof(Components.SentinelProbeProfilesTab), 65);
        yield return new ConfigTab("tasks", "Tasks", typeof(Components.TasksTab), 68);
        yield return new ConfigTab("itsm", "ITSM", typeof(Components.SentinelItsmTab), 70);
        yield return new ConfigTab("guardrail-policies", "Guardrail Policies", typeof(Components.SentinelPoliciesTab), 80);
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
