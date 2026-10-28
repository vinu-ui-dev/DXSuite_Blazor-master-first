using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DXSuite.Director.Tests.TwoHost;

/// <summary>
/// A stand-in for CoreAPI on a loopback port, so no test reaches a real CoreAPI: <c>GET Override/FeedStatus</c>
/// answers with <see cref="FeedStatus"/> (or drops the connection while <see cref="Refusing"/>),
/// <c>POST Override/OverrideToggle?state=</c> records each <see cref="ToggleCalls">call</see>, every other
/// <c>Override/*</c> call is <see cref="OtherOverrideCalls">recorded</see> and refused, and a SignalR hub at
/// <see cref="HubUrl"/> records who is connected and sends whatever event a test asks it to.
/// </summary>
internal sealed class StubCoreApi : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly Connections _connections;
    private int _feedStatusCalls;

    private StubCoreApi(WebApplication app, Connections connections)
    {
        _app = app;
        _connections = connections;
    }

    public string BaseUrl => _app.Urls.First().TrimEnd('/');

    public string HubUrl => BaseUrl + "/pulse";

    public volatile string FeedStatus = "OK";

    /// <summary>While true, every REST call has its connection dropped, as an unreachable server would.</summary>
    public volatile bool Refusing;

    public int FeedStatusCalls => Volatile.Read(ref _feedStatusCalls);

    /// <summary>The <c>state</c> of every <c>OverrideToggle</c> call, in order.</summary>
    public IReadOnlyList<bool> ToggleCalls => _toggleCalls.ToList();

    /// <summary>Every other <c>Override/*</c> call (method and path), such as the review's Preserve and Clear.</summary>
    public IReadOnlyList<string> OtherOverrideCalls => _otherOverrideCalls.ToList();

    private readonly ConcurrentQueue<bool> _toggleCalls = new();
    private readonly ConcurrentQueue<string> _otherOverrideCalls = new();

    /// <summary>The hub connections open now, with the headers each one connected with.</summary>
    public IReadOnlyList<HubClient> Clients => _connections.Open.Values.ToList();

    public static async Task<StubCoreApi> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        var connections = new Connections();
        builder.Services.AddSingleton(connections);
        builder.Services.AddSignalR();

        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        var stub = new StubCoreApi(app, connections);
        app.MapGet("/Override/FeedStatus", (HttpContext context) =>
        {
            Interlocked.Increment(ref stub._feedStatusCalls);
            if (stub.Refusing)
            {
                context.Abort();
                return Results.Empty;
            }
            return Results.Json(new { Status = stub.FeedStatus, ArrivalsStatus = "OK", DeparturesStatus = "OK" });
        });
        app.MapPost("/Override/OverrideToggle", (bool state) =>
        {
            stub._toggleCalls.Enqueue(state);
            return Results.Json(new { feedOverride = state, arrivalsFeedStatus = "OK", departuresFeedStatus = "OK" });
        });
        app.Map("/Override/{**rest}", (HttpContext context) =>
        {
            stub._otherOverrideCalls.Enqueue($"{context.Request.Method} {context.Request.Path}");
            return Results.NotFound();
        });
        app.MapHub<Hub>("/pulse");
        await app.StartAsync();
        return stub;
    }

    /// <summary>Sends one hub event to every connected client, as CoreAPI broadcasts its changes.</summary>
    public Task SendAsync(string method, params object?[] args) =>
        _app.Services.GetRequiredService<IHubContext<Hub>>().Clients.All.SendCoreAsync(method, args);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    public sealed record HubClient(string ConnectionId, string? ApiKey, string? Username, bool HasDeviceHeaders);

    internal sealed class Connections
    {
        public ConcurrentDictionary<string, HubClient> Open { get; } = new();
    }

    internal sealed class Hub(Connections connections) : Microsoft.AspNetCore.SignalR.Hub
    {
        public override Task OnConnectedAsync()
        {
            var headers = Context.GetHttpContext()!.Request.Headers;
            connections.Open[Context.ConnectionId] = new HubClient(
                Context.ConnectionId,
                headers["X-API-KEY"].FirstOrDefault(),
                headers["Username"].FirstOrDefault(),
                headers.ContainsKey("DeviceId") || headers.ContainsKey("CustomerId"));
            return Task.CompletedTask;
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            connections.Open.TryRemove(Context.ConnectionId, out _);
            return Task.CompletedTask;
        }
    }
}
