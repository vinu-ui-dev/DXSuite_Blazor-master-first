using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DXSuite.Sentinel.Entities;
using DXSuite.Sentinel.Services.Gateway;

namespace DXSuite.Sentinel.Middleware;

/// <summary>
/// Accepts WebSocket connections from Site Gateway agents at /ws/gateway.
/// Authenticates via API key (header or query param), registers with GatewayConnectionManager,
/// and keeps the connection open for bidirectional command/event messaging.
/// </summary>
public class GatewayWebSocketMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GatewayWebSocketMiddleware> _logger;

    public GatewayWebSocketMiddleware(RequestDelegate next, ILogger<GatewayWebSocketMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IServiceScopeFactory scopeFactory, IGatewayConnectionManager gatewayManager)
    {
        if (!context.Request.Path.StartsWithSegments("/ws/gateway"))
        {
            await _next(context);
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("WebSocket connections only");
            return;
        }

        // Extract API key from header or query string
        var apiKey = context.Request.Headers["X-Gateway-ApiKey"].FirstOrDefault()
            ?? context.Request.Query["apiKey"].FirstOrDefault();

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Gateway WebSocket rejected: no API key provided from {Ip}",
                context.Connection.RemoteIpAddress);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("API key required");
            return;
        }

        // Hash the key and look up the gateway
        var keyHash = ComputeHash(apiKey);
        SiteGateway? gateway;

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();
            gateway = await db.SiteGateways
                .FirstOrDefaultAsync(g => g.ApiKeyHash == keyHash && g.IsEnabled);
        }

        if (gateway == null)
        {
            _logger.LogWarning("Gateway WebSocket rejected: invalid or disabled API key from {Ip}",
                context.Connection.RemoteIpAddress);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Invalid API key");
            return;
        }

        var remoteIp = context.Connection.RemoteIpAddress?.ToString();

        // Accept the WebSocket connection
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();

        _logger.LogInformation("Gateway {GatewayId} ({SiteName}) WebSocket accepted from {Ip}",
            gateway.GatewayId, gateway.SiteName, remoteIp);

        // Update gateway status in DB
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();
            var entity = await db.SiteGateways.FindAsync(gateway.GatewayId);
            if (entity != null)
            {
                entity.Status = "connected";
                entity.LastHeartbeat = DateTime.UtcNow;
                entity.IpAddress = remoteIp;
                await db.SaveChangesAsync();
            }
        }

        var connectionInfo = new GatewayConnectionInfo
        {
            GatewayId = gateway.GatewayId,
            SiteName = gateway.SiteName,
            CustomerId = gateway.CustomerId,
            ConnectedSince = DateTime.UtcNow,
            LastHeartbeat = DateTime.UtcNow,
            RemoteIpAddress = remoteIp
        };

        // Register with the connection manager — its receive loop handles messages
        // This call blocks until the WebSocket closes
        await gatewayManager.RegisterGatewayAsync(gateway.GatewayId, webSocket, connectionInfo, context.RequestAborted);

        // Connection ended — update DB status
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();
            var entity = await db.SiteGateways.FindAsync(gateway.GatewayId);
            if (entity != null)
            {
                entity.Status = "disconnected";
                await db.SaveChangesAsync();
            }
        }

        _logger.LogInformation("Gateway {GatewayId} ({SiteName}) WebSocket disconnected",
            gateway.GatewayId, gateway.SiteName);
    }

    private static string ComputeHash(string apiKey)
    {
        var bytes = Encoding.UTF8.GetBytes(apiKey);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
