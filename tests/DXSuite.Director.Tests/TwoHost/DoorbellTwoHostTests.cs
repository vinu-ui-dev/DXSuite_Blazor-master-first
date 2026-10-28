using System.Collections.Concurrent;
using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using DXSuite.Director.Services.DistributedLock;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.CustomerDeviations;
using DXSuite.Shared.Multitenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Tests.TwoHost;

// Clause ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// The doorbell job on two Director instances (B-82, INK-09, HOST-04 items 1, 2 and 5): both run it against one
/// database, one holds the tenant's lease, and the script's listener (a stand-in on a loopback port) gets one nudge per
/// change, whichever host holds the lease and also after it moves.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "TwoHost")]
public sealed class DoorbellTwoHostTests
{
    private const int Gate = 101;
    private const string TenantId = "gtaa-tenant";

    // Shorter than the product's 1 and 5 seconds so the test runs quickly; the cadence itself is covered on a fake clock.
    private static readonly MessagingDoorbellTiming Timing = new(
        VersionCheck: TimeSpan.FromMilliseconds(200), HashCheck: TimeSpan.FromSeconds(1),
        LeaseRetry: TimeSpan.FromMilliseconds(200), LeaseTerm: TimeSpan.FromMinutes(10));

    // Long enough for several version checks and two hash checks by whichever host holds the lease.
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2.5);

    // Only stored on the display row; nothing reads it.
    private static readonly DateTime Created = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    [LocalDbFact]
    public async Task Two_hosts_ring_once_per_change_and_go_on_doing_so_after_the_lease_moves()
    {
        await using var listener = await StubDoorbell.StartAsync();
        await using var hosts = await TwoHosts.StartAsync(o => Configure(o, listener.Url));

        // A new database has never rung, so the first holder rings once.
        await TwoHosts.WaitForAsync(() => Holders(hosts).Count == 1 && listener.Nudges == 1);
        var first = Holders(hosts).Single();

        // A display added: a bump in the save's own transaction.
        await AddDisplayAsync(hosts, "Gate B1");
        await AssertNudgesAsync(listener, 2);

        // A Message whose start comes a moment later: no bump, the hash check catches the start.
        await AddStartingMessageAsync(hosts, HostNow(hosts).AddSeconds(1));
        await AssertNudgesAsync(listener, 3);

        await hosts.Hosts.Single(h => h.Name == first).StopAsync();
        await TwoHosts.WaitForAsync(() => Holders(hosts) is [var holder] && holder != first);

        // The new holder carries on from the state in SQL: nothing to ring for until something changes.
        await Task.Delay(Settle);
        Assert.Equal(3, listener.Nudges);

        await AddDisplayAsync(hosts, "Gate B2");
        await AssertNudgesAsync(listener, 4);
        Assert.All(listener.Requests, r => Assert.Equal(("POST", 0L), (r.Method, r.BodyLength)));
    }

    // INK-09, section 3.10: pull only on both hosts. Neither takes the lease, and a change sends nothing.
    [LocalDbFact]
    public async Task With_no_doorbell_URL_neither_host_takes_the_lease_or_rings()
    {
        await using var listener = await StubDoorbell.StartAsync();
        await using var hosts = await TwoHosts.StartAsync(o => Configure(o, url: ""));

        await AddDisplayAsync(hosts, "Gate B1");
        await Task.Delay(Settle);

        Assert.Empty(Holders(hosts));
        Assert.Equal(0, listener.Nudges);
        Assert.False(await LeaseIsHeldAsync(hosts));
    }

    private static async Task AssertNudgesAsync(StubDoorbell listener, int expected)
    {
        await TwoHosts.WaitForAsync(() => listener.Nudges >= expected);
        await Task.Delay(Settle);
        Assert.Equal(expected, listener.Nudges);
    }

    private static void Configure(TwoHostOptions o, string url)
    {
        o.SeedAsync = SeedDisplayAsync;
        o.ConfigureServices = (_, services, _) =>
        {
            services.AddLogging();
            services.AddHttpClient();
            services.AddSingleton(TimeProvider.System);
            var messaging = new MessagingOptions { DefaultTimeZone = "America/Toronto", Delivery = new MessagingDeliveryOptions { DoorbellUrl = url } };
            services.AddSingleton(Options.Create(messaging));
            services.AddSingleton(Options.Create(new D02GtaaMessagingOptions { Enabled = true }));
            services.AddSingleton<MessageArbitration>();
            services.AddSingleton<DisplayTimeZones>();
            services.AddScoped<MessageDelivery>();
            services.AddScoped<MessagingDoorbell>();
            services.AddSingleton<IDirectorSaveChangesInterceptor, DeliveryVersionInterceptor>();
            services.AddScoped<ITenantContext>(_ => new MessagingDoorbellTests.MessagingTenant());
            services.AddSingleton<IWorkerScopeFactory, OneTenantOnTheSharedDatabase>();
            services.AddSingleton(sp => new MessagingDoorbellService(
                sp.GetRequiredService<IWorkerScopeFactory>(),
                sp.GetRequiredService<IDistributedLockService>(),
                sp.GetRequiredService<IOptions<MessagingOptions>>(),
                TimeProvider.System,
                sp.GetRequiredService<ILogger<MessagingDoorbellService>>(),
                Timing));
            services.AddHostedService(sp => sp.GetRequiredService<MessagingDoorbellService>());
        };
    }

    private static List<string> Holders(TwoHosts hosts) =>
        hosts.Hosts.Where(h => h.IsRunning && h.Services.GetRequiredService<MessagingDoorbellService>().TenantsHeld.Count > 0)
            .Select(h => h.Name)
            .ToList();

    /// <summary>Whether some session holds the tenant's doorbell lease, asked of SQL Server from outside both hosts.</summary>
    private static async Task<bool> LeaseIsHeldAsync(TwoHosts hosts)
    {
        await using var db = hosts.CreateDbContext();
        var free = await db.Database.SqlQueryRaw<int>(
            "SELECT APPLOCK_TEST('public', {0}, 'Exclusive', 'Session') AS [Value]",
            MessagingDoorbellService.LeaseName(TenantId)).SingleAsync();
        return free == 0;
    }

    // Parents the delivery does not read (location types, display types, constraints) are left out.
    private static Task SeedDisplayAsync(DirectorDbContext db) => db.Database.ExecuteSqlRawAsync($"""
        ALTER TABLE tblLocation NOCHECK CONSTRAINT ALL;
        ALTER TABLE tblDisplay NOCHECK CONSTRAINT ALL;
        SET IDENTITY_INSERT tblLocation ON;
        INSERT INTO tblLocation (LocationId, LocationTypeId, LocationGroupId, LocationName, SortOrder, EnableShuffle, EnableFade, EnableContentApproval)
        VALUES (1, 1, 1, 'Terminal 1', 1, 0, 1, 0);
        SET IDENTITY_INSERT tblLocation OFF;
        SET IDENTITY_INSERT tblDisplay ON;
        INSERT INTO tblDisplay (DisplayId, LocationId, DisplayName, DisplayMode, DisplayTypeId, ConstraintId, UseClock, UseVideoMask, EnableFade, EnableContentApproval)
        VALUES ({Gate}, 1, 'Gate', 0, 1, 1, 0, 0, 1, 0);
        SET IDENTITY_INSERT tblDisplay OFF;
        """);

    // The hosts' own clock decides when a start is crossed, so a start time is set against it.
    private static DateTime HostNow(TwoHosts hosts) =>
        hosts.Hosts.First(h => h.IsRunning).Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

    /// <summary>
    /// A display added through a running host's own Director context, as the location and layout dialogs add one; the
    /// save itself moves the messaging delivery version, before any hash check could.
    /// </summary>
    private static async Task AddDisplayAsync(TwoHosts hosts, string name)
    {
        var before = await DeliveryVersionProbe.ReadAsync(hosts.CreateDbContext);
        using (var scope = hosts.Hosts.First(h => h.IsRunning).Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DirectorDbContext>();
            db.TblDisplays.Add(new TblDisplay
            {
                LocationId = 1, DisplayName = name, DisplayTypeId = 1, DisplayGroupId = 1, ConstraintId = 1, CreateDate = Created,
            });
            await db.SaveChangesAsync();
        }
        Assert.NotEqual(before, await DeliveryVersionProbe.ReadAsync(hosts.CreateDbContext));
    }

    /// <summary>An Emergency to the gate display starting at <paramref name="startUtc"/>, with its first shown version.</summary>
    private static async Task AddStartingMessageAsync(TwoHosts hosts, DateTime startUtc)
    {
        await using var db = hosts.CreateDbContext();
        db.TblMessageActivations.Add(new TblMessageActivation
        {
            Message = new TblMessage
            {
                MessageTypeId = 1, Name = "Evacuate", IsCanned = true, Status = MessageStatus.Approved, CreatedUtc = startUtc,
                Texts = [new TblMessageText { LanguageCode = "eng", Body = "Evacuate" }],
            },
            StartUtc = startUtc, ActivatedUtc = startUtc, Source = MessageActivationSource.Api,
            Targets = [new TblMessageActivationTarget { DisplayId = Gate, DisplayName = "Gate", LocationName = "Terminal 1" }],
            Versions =
            [
                new TblMessageActivationVersion
                {
                    Sequence = 1, Reason = ShownVersionReason.FirstSend, MessageTypeId = 1, MessageTypeCode = "EMERGENCY", EffectiveRank = 10,
                    Texts = """{"eng":"Evacuate"}""", AssetSlots = "[]", SentUtc = startUtc,
                },
            ],
        });
        await db.SaveChangesAsync();
    }

    /// <summary>The hosts have no tenant registry: one tenant whose scope is the host's own, on the shared database.</summary>
    private sealed class OneTenantOnTheSharedDatabase(IServiceScopeFactory scopes) : IWorkerScopeFactory
    {
        public IReadOnlyList<string> WorkerTenants => ["gtaa"];

        public Task<IServiceScope> CreateTenantScopeAsync(string subdomain, CancellationToken ct = default) =>
            Task.FromResult(scopes.CreateScope());
    }

    /// <summary>The Moxie script's doorbell listener on a loopback port: counts every request, whatever its method.</summary>
    private sealed class StubDoorbell : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly ConcurrentQueue<(string Method, long BodyLength)> _requests = new();

        private StubDoorbell(WebApplication app) => _app = app;

        public string Url => _app.Urls.First().TrimEnd('/') + "/doorbell/";

        public int Nudges => _requests.Count;

        public IReadOnlyList<(string Method, long BodyLength)> Requests => _requests.ToList();

        public static async Task<StubDoorbell> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Configuration.Sources.Clear();
            builder.Logging.ClearProviders();
            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            var stub = new StubDoorbell(app);
            app.Map("/doorbell/", async (HttpContext context) =>
            {
                using var body = new MemoryStream();
                await context.Request.Body.CopyToAsync(body);
                stub._requests.Enqueue((context.Request.Method, body.Length));
                return Results.Ok();
            });
            await app.StartAsync();
            return stub;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
