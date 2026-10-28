using System.Text.Json;
using System.Text.Json.Nodes;
using DXSuite.SentinelGateway;

var builder = WebApplication.CreateBuilder(args);

// Run as a Windows Service when installed (no-op when launched from a console in dev).
builder.Host.UseWindowsService(o => o.ServiceName = "DXSuite Sentinel Agent");

var options = builder.Configuration.GetSection(GatewayAgentOptions.SectionName).Get<GatewayAgentOptions>()
    ?? new GatewayAgentOptions();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(new GatewayAgentState
{
    Broker = $"{options.BrokerHost}:{options.BrokerPort}",
    TenantId = options.TenantId,
    GatewayId = options.GatewayId,
    Version = options.Version,
    Roles = string.Join(", ", options.EffectiveRoles)
});
builder.Services.AddHostedService<GatewayAgentWorker>();

// Local-only operator UI. Binds to loopback so the config page is never exposed off the box.
builder.WebHost.UseUrls($"http://localhost:{options.UiPort}");

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Live status for the UI to poll.
app.MapGet("/api/status", (GatewayAgentState s) => Results.Json(s));

// Current persisted config, for prefilling the form.
app.MapGet("/api/config", (GatewayAgentOptions o) =>
    Results.Json(new { o.BrokerHost, o.BrokerPort, o.TenantId, o.GatewayId, o.HeartbeatSeconds }));

// Persist config edits back to appsettings.json (applied on next restart).
app.MapPost("/api/config", async (GatewayConfigUpdate dto, IHostEnvironment env) =>
{
    var path = Path.Combine(env.ContentRootPath, "appsettings.json");
    if (!File.Exists(path))
        return Results.Problem("appsettings.json not found next to the agent.");

    var root = JsonNode.Parse(await File.ReadAllTextAsync(path)) as JsonObject ?? new JsonObject();
    var gw = root["Gateway"] as JsonObject ?? new JsonObject();

    if (!string.IsNullOrWhiteSpace(dto.BrokerHost)) gw["BrokerHost"] = dto.BrokerHost;
    if (dto.BrokerPort is > 0) gw["BrokerPort"] = dto.BrokerPort;
    if (!string.IsNullOrWhiteSpace(dto.TenantId)) gw["TenantId"] = dto.TenantId;
    if (dto.GatewayId is > 0) gw["GatewayId"] = dto.GatewayId;
    if (dto.HeartbeatSeconds is > 0) gw["HeartbeatSeconds"] = dto.HeartbeatSeconds;

    root["Gateway"] = gw;
    await File.WriteAllTextAsync(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    return Results.Ok(new { saved = true, message = "Saved. Restart the gateway to apply." });
});

app.Run();

internal record GatewayConfigUpdate(string? BrokerHost, int? BrokerPort, string? TenantId, int? GatewayId, int? HeartbeatSeconds);
