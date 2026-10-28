using DXSuite.Aviation.Services.FlightDataEditor;
using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Connectors.Services.CoreApi.Hub;
using DXSuite.Connectors.Services.CoreApi.Models;
using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using DXSuite.Director.Services.CoreApi;
using DXSuite.Director.Services.DistributedLock;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Multitenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DXSuite.Director.Tests.TwoHost;

/// <summary>
/// The CoreAPI watcher on two Director instances (CAPI-05, FDE-02, HOST-04): one leased watcher per tenant writes
/// the feed state to SQL and logs each change once, and each instance keeps one read-only hub listener per tenant
/// for its own pages. CoreAPI is <see cref="StubCoreApi"/> on a loopback port.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "TwoHost")]
public sealed class CoreApiWatcherTwoHostTests
{
    private const string TenantId = "gtaa-tenant";
    private const string DirectorKey = "director-key";

    [Fact]
    public void The_poll_is_the_fixed_30_seconds_and_the_lease_is_named_per_tenant()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), CoreApiWatcherService.PollInterval);
        Assert.Equal("coreapi-watcher:gtaa-tenant", CoreApiWatcherService.LeaseName("gtaa-tenant"));
    }

    [LocalDbFact]
    public async Task Exactly_one_host_holds_the_watcher_lease_and_it_moves_when_that_host_stops()
    {
        await using var coreApi = await StubCoreApi.StartAsync();
        var tenant = new WatchedTenant();
        await using var hosts = await StartHostsAsync(coreApi, tenant, watcher: true);

        await TwoHosts.WaitForAsync(() => Holders(hosts).Count == 1 && coreApi.Clients.Count == 1);
        var first = Holders(hosts).Single();
        Assert.True(await LeaseIsHeldAsync(hosts));

        // The watcher's hub connection carries the tenant's Director key and the service name, nothing hard-coded.
        var connection = Assert.Single(coreApi.Clients);
        Assert.Equal(DirectorKey, connection.ApiKey);
        Assert.Equal(CoreApiClient.ServiceUsername, connection.Username);
        Assert.False(connection.HasDeviceHeaders);

        // The other host keeps trying for the lease and must not get it while the holder runs.
        await Task.Delay(TimeSpan.FromSeconds(6));
        Assert.Equal([first], Holders(hosts));
        Assert.Single(coreApi.Clients);

        await hosts.Hosts.Single(h => h.Name == first.Host).StopAsync();

        var second = first.Host == "A" ? "B" : "A";
        await TwoHosts.WaitForAsync(() => Holders(hosts) is [{ Host: var holder }] && holder == second && coreApi.Clients.Count == 1);
        Assert.True(await LeaseIsHeldAsync(hosts));
    }

    [LocalDbFact]
    public async Task Each_feed_change_is_written_to_SQL_and_logged_once_as_a_System_event()
    {
        await using var coreApi = await StubCoreApi.StartAsync();
        var tenant = new WatchedTenant();
        await using var hosts = await StartHostsAsync(coreApi, tenant, watcher: true);

        // The first answer only records the state: an up, reachable feed is the state nothing has to report.
        var state = await WaitForStateAsync(hosts, s => s is { Status: "OK", Reachable: true });
        Assert.True(state.FeedUp);
        Assert.Empty(await CoreApiEventsAsync(hosts));

        coreApi.FeedStatus = "DOWN";
        state = await WaitForStateAsync(hosts, s => s is { Status: "DOWN" });
        Assert.False(state.FeedUp);

        coreApi.Refusing = true;
        state = await WaitForStateAsync(hosts, s => s is { Reachable: false });
        Assert.Equal("DOWN", state.Status);

        coreApi.FeedStatus = "OK";
        coreApi.Refusing = false;
        await WaitForStateAsync(hosts, s => s is { Status: "OK", Reachable: true });

        // Several more polls on both hosts change nothing, so nothing more is logged.
        var calls = coreApi.FeedStatusCalls;
        await TwoHosts.WaitForAsync(() => coreApi.FeedStatusCalls >= calls + 5);

        var events = await CoreApiEventsAsync(hosts);
        var servers = $$"""{"servers":["{{coreApi.BaseUrl}}"]}""";
        // In the order they happened; when CoreAPI comes back, its reachability is logged before the feed it reports.
        Assert.Equal(
            [
                (UserEventActionKeys.CoreApiFeedDown, UserEventTargetTypes.FlightFeed, """{"status":"DOWN"}"""),
                (UserEventActionKeys.CoreApiUnreachable, UserEventTargetTypes.CoreApi, servers),
                (UserEventActionKeys.CoreApiReachable, UserEventTargetTypes.CoreApi, servers),
                (UserEventActionKeys.CoreApiFeedUp, UserEventTargetTypes.FlightFeed, """{"status":"OK"}"""),
            ],
            events.Select(e => (e.EventTitle, e.TargetType, e.Details)));
        Assert.All(events, e =>
        {
            Assert.Equal(UserEventActorKind.System, e.ActorKind);
            Assert.Equal(CoreApiWatcherService.ServiceName, e.ActorName);
            Assert.Null(e.UserId);
        });
    }

    [LocalDbFact]
    public async Task Hub_events_update_the_feed_state_and_the_ignored_events_change_nothing()
    {
        await using var coreApi = await StubCoreApi.StartAsync();
        var tenant = new WatchedTenant();
        // A long poll, so every change below comes from the hub.
        await using var hosts = await StartHostsAsync(coreApi, tenant, watcher: true, poll: TimeSpan.FromMinutes(10));
        await WaitForStateAsync(hosts, s => s is { Status: "OK" });
        await TwoHosts.WaitForAsync(() => coreApi.Clients.Count == 1);

        await coreApi.SendAsync("WindowUpdate", new FeedWindow { WindowStart = DateTime.Now, WindowEnd = DateTime.Now.AddHours(1) });
        await coreApi.SendAsync("ActiveServerUpdate", "http://elsewhere.test");
        await coreApi.SendAsync("FeedUpdate", new FeedStatus { Status = "DOWN" }, "http://elsewhere.test", new[] { "http://elsewhere.test" });
        await coreApi.SendAsync("ToggleOverride", true);

        var state = await WaitForStateAsync(hosts, s => s is { Status: "DOWN", SwitchOn: true });
        Assert.True(state.Reachable);
        Assert.Equal([UserEventActionKeys.CoreApiFeedDown], (await CoreApiEventsAsync(hosts)).Select(e => e.EventTitle));

        // The hub's server list and active server never reach the tenant's own list.
        await using var scope = hosts.A.Services.CreateAsyncScope();
        var settings = await scope.ServiceProvider.GetRequiredService<ICoreApiSettingsStore>().ReadAsync(default);
        Assert.Equal([coreApi.BaseUrl], settings!.Servers);
    }

    [LocalDbFact]
    public async Task The_watcher_runs_only_while_the_tenant_has_the_Flight_Data_Editor_on()
    {
        await using var coreApi = await StubCoreApi.StartAsync();
        var tenant = new WatchedTenant { FlightDataEditor = false };
        await using var hosts = await StartHostsAsync(coreApi, tenant, watcher: true);

        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Empty(Holders(hosts));
        Assert.Equal(0, coreApi.FeedStatusCalls);
        Assert.Empty(coreApi.Clients);
        Assert.Null(await ReadStateAsync(hosts));

        tenant.FlightDataEditor = true;
        await WaitForStateAsync(hosts, s => s is { Status: "OK" });
        Assert.Single(Holders(hosts));

        tenant.FlightDataEditor = false;
        await TwoHosts.WaitForAsync(() => Holders(hosts).Count == 0 && coreApi.Clients.Count == 0);
    }

    [LocalDbFact]
    public async Task Each_host_feeds_its_pages_from_one_read_only_listener_per_tenant()
    {
        await using var coreApi = await StubCoreApi.StartAsync();
        var tenant = new WatchedTenant();
        await using var hosts = await StartHostsAsync(coreApi, tenant, watcher: false);

        // Two pages (two circuits) on each host.
        var pages = new List<(string Host, RecordingPage Page, IAsyncDisposable Subscription, AsyncServiceScope Circuit)>();
        foreach (var host in hosts.Hosts)
        {
            for (var i = 0; i < 2; i++)
            {
                var circuit = host.Services.CreateAsyncScope();
                var page = new RecordingPage();
                var subscription = await circuit.ServiceProvider.GetRequiredService<ICoreApiLiveFeed>().SubscribeAsync(page);
                pages.Add((host.Name, page, subscription, circuit));
            }
        }

        await TwoHosts.WaitForAsync(() => coreApi.Clients.Count == 2);
        Assert.All(coreApi.Clients, c => Assert.Equal(DirectorKey, c.ApiKey));

        await coreApi.SendAsync("FlightDepartureUpdate", new FEDepartureFlight { RecordId = 7, Type = "D", FLT = "AC101" });
        await coreApi.SendAsync("FlightArrivalListUpdate",
            System.Text.Json.JsonSerializer.Serialize(new[] { new FEArrivalFlight { RecordId = 8, Type = "A" } }));
        await coreApi.SendAsync("FeedUpdate", new FeedStatus { Status = "DOWN" }, null, null);
        await coreApi.SendAsync("ToggleOverride", true);

        await TwoHosts.WaitForAsync(() => pages.All(p => p.Page.Departures.Contains(7) && p.Page.Arrivals.Contains(8)
            && p.Page.Statuses.Contains("DOWN") && p.Page.Switch.Contains(true)));

        // Listeners write nothing: no feed state and no event, although the feed went down.
        Assert.Null(await ReadStateAsync(hosts));
        Assert.Empty(await CoreApiEventsAsync(hosts));

        // A host's listener closes when its last page goes.
        foreach (var (_, _, subscription, circuit) in pages.Where(p => p.Host == "A"))
        {
            await subscription.DisposeAsync();
            await circuit.DisposeAsync();
        }
        await TwoHosts.WaitForAsync(() => coreApi.Clients.Count == 1);

        foreach (var (_, _, subscription, circuit) in pages.Where(p => p.Host == "B"))
        {
            await subscription.DisposeAsync();
            await circuit.DisposeAsync();
        }
        await TwoHosts.WaitForAsync(() => coreApi.Clients.Count == 0);
    }

    // ── Composition ──────────────────────────────────────────────────────

    /// <param name="switchOff">Also registers the startup switch-off (FDE-03), with the switch allowed or not.</param>
    /// <param name="feedState">A feed state row the watcher finds when it starts, as an earlier run left it.</param>
    /// <param name="leaseTerm">How long each hold of the watcher lease lasts before it is taken again.</param>
    internal static async Task<TwoHosts> StartHostsAsync(StubCoreApi coreApi, WatchedTenant tenant, bool watcher, TimeSpan? poll = null,
        SwitchOff? switchOff = null, CoreApiFeedState? feedState = null, TimeSpan? leaseTerm = null)
    {
        // One key ring for both hosts, as the shared ring (HOST-04 item 3) gives them.
        var protector = new CoreApiKeyProtector(new EphemeralDataProtectionProvider());
        return await TwoHosts.StartAsync(o =>
        {
            o.SeedAsync = async db =>
            {
                await db.Database.ExecuteSqlRawAsync("""
                    SET IDENTITY_INSERT tblEventSeverity ON;
                    INSERT INTO tblEventSeverity (EventSeverityId, SeverityName) VALUES (1, 'INFO'), (2, 'WARNING'), (3, 'ERROR'), (4, 'CRITICAL');
                    SET IDENTITY_INSERT tblEventSeverity OFF;
                    """);
                db.TblCoreApiConnections.Add(new TblCoreApiConnection
                {
                    DirectorKeyProtected = protector.Protect(DirectorKey),
                    HubUrl = coreApi.HubUrl,
                    UpdatedUtc = DateTime.UtcNow,
                    Servers = [new TblCoreApiServer { Url = coreApi.BaseUrl, SortOrder = 0 }],
                });
                if (feedState is not null)
                {
                    db.TblCoreApiFeedStates.Add(new TblCoreApiFeedState
                    {
                        CoreApiFeedStateId = TblCoreApiFeedState.SingleRowId,
                        Status = feedState.Status,
                        Reachable = feedState.Reachable,
                        SwitchOn = feedState.SwitchOn,
                        ChangedUtc = feedState.ChangedUtc,
                    });
                }
                await db.SaveChangesAsync();
            };
            o.ConfigureServices = (_, services, _) =>
            {
                services.AddLogging();
                services.AddSingleton(protector);
                services.AddScoped<ITenantContext>(_ => tenant);
                services.AddSingleton<IWorkerScopeFactory, OneTenantOnTheSharedDatabase>();
                services.AddScoped<ISessionState, CoreApiClientTests.FakeSession>();
                services.AddUserEventLog();
                services.AddCoreApiStores();
                services.AddCoreApiClient();
                services.AddCoreApiLiveFeed(hubRetryDelay: TimeSpan.FromMilliseconds(500));
                if (switchOff is not null)
                {
                    services.Configure<FlightDataEditorOptions>(o => o.AllowOutageModeSwitch = switchOff.Allowed);
                    services.AddOutageSwitchOff();
                }
                if (watcher)
                {
                    services.AddSingleton(sp => new CoreApiWatcherService(
                        sp.GetRequiredService<IWorkerScopeFactory>(),
                        sp.GetRequiredService<IDistributedLockService>(),
                        sp.GetRequiredService<ILogger<CoreApiWatcherService>>(),
                        new CoreApiWatcherTiming(
                            Poll: poll ?? TimeSpan.FromMilliseconds(200),
                            LeaseRetry: TimeSpan.FromMilliseconds(200),
                            LeaseTerm: leaseTerm ?? TimeSpan.FromMinutes(10),
                            HubRetry: TimeSpan.FromMilliseconds(500))));
                    services.AddHostedService(sp => sp.GetRequiredService<CoreApiWatcherService>());
                }
            };
        });
    }

    internal static List<(string Host, string Tenant)> Holders(TwoHosts hosts) =>
        hosts.Hosts.Where(h => h.IsRunning)
            .SelectMany(h => h.Services.GetService<CoreApiWatcherService>()?.TenantsWatched.Select(t => (h.Name, t)) ?? [])
            .ToList();

    /// <summary>Whether some session holds the tenant's lease, asked of SQL Server from outside both hosts.</summary>
    private static async Task<bool> LeaseIsHeldAsync(TwoHosts hosts)
    {
        await using var db = hosts.CreateDbContext();
        var free = await db.Database.SqlQueryRaw<int>(
            "SELECT APPLOCK_TEST('public', {0}, 'Exclusive', 'Session') AS [Value]",
            CoreApiWatcherService.LeaseName(TenantId)).SingleAsync();
        return free == 0;
    }

    internal static async Task<CoreApiFeedState?> ReadStateAsync(TwoHosts hosts)
    {
        var host = hosts.Hosts.First(h => h.IsRunning);
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICoreApiFeedStateReader>().ReadAsync(default);
    }

    internal static async Task<CoreApiFeedState> WaitForStateAsync(TwoHosts hosts, Func<CoreApiFeedState, bool> condition)
    {
        CoreApiFeedState? state = null;
        await TwoHosts.WaitForAsync(() =>
        {
            state = ReadStateAsync(hosts).GetAwaiter().GetResult();
            return state is not null && condition(state);
        });
        return state!;
    }

    private static async Task<List<TblEventLog>> CoreApiEventsAsync(TwoHosts hosts)
    {
        await using var db = hosts.CreateDbContext();
        return await db.TblEventLogs.AsNoTracking()
            .Where(e => e.EventTitle != null && e.EventTitle.StartsWith("coreapi."))
            .OrderBy(e => e.RecordId)
            .ToListAsync();
    }

    /// <summary>A page that records what its instance's listener fed it.</summary>
    private sealed class RecordingPage : ICoreApiHubObserver
    {
        public List<int> Departures { get; } = new();
        public List<int> Arrivals { get; } = new();
        public List<string?> Statuses { get; } = new();
        public List<bool> Switch { get; } = new();

        public Task OnDeparturesAsync(IReadOnlyList<FEDepartureFlight> flights)
        {
            lock (Departures) Departures.AddRange(flights.Select(f => f.RecordId));
            return Task.CompletedTask;
        }

        public Task OnArrivalsAsync(IReadOnlyList<FEArrivalFlight> flights)
        {
            lock (Arrivals) Arrivals.AddRange(flights.Select(f => f.RecordId));
            return Task.CompletedTask;
        }

        public Task OnFeedStatusAsync(FeedStatus status)
        {
            lock (Statuses) Statuses.Add(status.Status);
            return Task.CompletedTask;
        }

        public Task OnOutageSwitchAsync(bool on)
        {
            lock (Switch) Switch.Add(on);
            return Task.CompletedTask;
        }
    }

    /// <summary>The instance setting <c>Aviation:FlightDataEditor:AllowOutageModeSwitch</c> both hosts carry.</summary>
    internal sealed record SwitchOff(bool Allowed);

    /// <summary>The GTAA tenant, whose Flight Data Editor flag a test can turn on and off.</summary>
    internal sealed class WatchedTenant : ITenantContext
    {
        public volatile bool FlightDataEditor = true;

        public bool IsPopulated => true;
        public string TenantId => CoreApiWatcherTwoHostTests.TenantId;
        public string Subdomain => "gtaa";
        public string CustomerCode => "GTAA";
        public string DisplayName => "GTAA";
        public string? DirectorConnectionString => null;
        public string? SentinelConnectionString => null;
        public string? CoreDataConnectionString => null;
        public string? AviationConnectionString => null;
        public string? DesignConnectionString => null;
        public TenantAuthConfig AuthConfig => new();
        public TenantModuleConfig Modules => new();
        public TenantFeatureFlags Features => new() { Aviation = new AviationFeatureFlags { FlightDataEditor = FlightDataEditor } };
        public string MediaRootPath => "";
        public string? SiteUrl => null;
    }

    /// <summary>The hosts have no tenant registry: one tenant whose scope is the host's own, on the shared database.</summary>
    private sealed class OneTenantOnTheSharedDatabase(IServiceScopeFactory scopes) : IWorkerScopeFactory
    {
        public IReadOnlyList<string> WorkerTenants => ["gtaa"];

        public Task<IServiceScope> CreateTenantScopeAsync(string subdomain, CancellationToken ct = default) =>
            Task.FromResult(scopes.CreateScope());
    }
}
