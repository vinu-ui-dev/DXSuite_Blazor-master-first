using System.Collections.Concurrent;
using System.Net;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.DistributedLock;
using DXSuite.Director.Services.Messaging;
using DXSuite.Director.Tests.TwoHost;
using DXSuite.Shared.CustomerDeviations;
using DXSuite.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static DXSuite.Director.Tests.MessageAccessDatabase;

namespace DXSuite.Director.Tests;

// Clause ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// The doorbell (B-82, INK-09): one check reads the delivery version and, when asked, the delivery body's hash, and
/// on a change posts an empty body to <c>Messaging:Delivery:DoorbellUrl</c>; the job runs the checks every second and
/// the hash every 5 seconds on its clock. Over a Director database on LocalDB, with the doorbell URL answered in
/// memory. Two instances are covered in <see cref="TwoHost.DoorbellTwoHostTests"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessagingDoorbellTests : IClassFixture<MessageAccessDatabase>, IAsyncLifetime
{
    private const string Url = "http://moxie.gtaa.test:8091/doorbell/";

    // Tuesday 20 Oct 2026, 10:00 in Toronto.
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 14, 0, 0, TimeSpan.Zero);

    private readonly MessageAccessDatabase _db;
    private readonly ManualClock _clock = new(Start);
    private readonly DoorbellEndpoint _endpoint = new();

    public MessagingDoorbellTests(MessageAccessDatabase db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // The first check of a new database has nothing to compare with, so it rings once; the script pulls at start anyway.
    [LocalDbFact]
    public async Task Once_rung_a_check_that_finds_nothing_changed_sends_nothing()
    {
        var doorbell = Doorbell();
        Assert.True(await doorbell.CheckAsync(recomputeHash: true, default));
        await _endpoint.WaitForAsync(1);

        Assert.False(await doorbell.CheckAsync(recomputeHash: false, default));
        Assert.False(await doorbell.CheckAsync(recomputeHash: true, default));

        var nudge = Assert.Single(_endpoint.Nudges);
        Assert.Equal((HttpMethod.Post, Url, 0L), (nudge.Method, nudge.Url, nudge.BodyLength));
    }

    // INK-09: a change that bumps the version is seen on the next 1-second check, without the hash.
    [LocalDbFact]
    public async Task A_bumped_version_rings_once_on_the_next_check()
    {
        var doorbell = await RungDoorbellAsync();

        await BumpAsync();

        Assert.True(await doorbell.CheckAsync(recomputeHash: false, default));
        Assert.False(await doorbell.CheckAsync(recomputeHash: false, default));
        Assert.False(await doorbell.CheckAsync(recomputeHash: true, default));
        await _endpoint.WaitForAsync(2);
        await Task.Delay(100);
        Assert.Equal(2, _endpoint.Nudges.Count);
    }

    // INK-09, MSG-17: a time boundary changes the body with no change to the version; only the hash sees it.
    [LocalDbFact]
    public async Task A_body_that_changes_with_time_alone_rings_on_the_next_hash_check_only()
    {
        var message = await _db.AddMessageAsync(Paging);
        var activation = await _db.AddActivationAsync(message, Start.UtcDateTime.AddSeconds(3), null, [GateA1]);
        await _db.AddShownVersionAsync(activation, 1, Start.UtcDateTime, new Dictionary<string, string> { ["eng"] = "Gate change" });
        var doorbell = await RungDoorbellAsync();

        _clock.Advance(TimeSpan.FromSeconds(4));

        Assert.False(await doorbell.CheckAsync(recomputeHash: false, default));
        Assert.True(await doorbell.CheckAsync(recomputeHash: true, default));
        Assert.False(await doorbell.CheckAsync(recomputeHash: true, default));
    }

    // INK-09, section 3.10: pull only. Nothing is sent and the doorbell state is left alone.
    [LocalDbFact]
    public async Task An_empty_doorbell_URL_sends_nothing()
    {
        var doorbell = Doorbell(url: "");

        await BumpAsync();

        Assert.False(await doorbell.CheckAsync(recomputeHash: true, default));
        await Task.Delay(100);
        Assert.Empty(_endpoint.Nudges);
        Assert.Null((await StateAsync()).NudgedVersion);
    }

    // INK-09: the doorbell carries nothing the script needs, so a nudge that fails or hangs is dropped and not retried;
    // the script's safety-net pull covers it.
    [LocalDbFact]
    public async Task A_nudge_that_fails_is_dropped_and_the_check_carries_on()
    {
        var doorbell = await RungDoorbellAsync();
        _endpoint.Failing = true;

        await BumpAsync();

        Assert.True(await doorbell.CheckAsync(recomputeHash: false, default));
        Assert.False(await doorbell.CheckAsync(recomputeHash: true, default));
        await _endpoint.WaitForAttemptsAsync(2);
    }

    // HOST-04 item 1: two instances that see the same change ring once between them, from the state in SQL.
    [LocalDbFact]
    public async Task Two_doorbells_checking_the_same_change_at_once_ring_once_between_them()
    {
        await RungDoorbellAsync();
        await BumpAsync();

        var rang = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Doorbell().CheckAsync(recomputeHash: true, default)));

        Assert.Equal(1, rang.Count(r => r));
    }

    // INK-09, MSG-17: the job reads the version every second and the hash every 5 seconds, so a scheduled start
    // nudges within 5 seconds of its time.
    [LocalDbFact]
    public async Task The_job_rings_for_a_scheduled_start_within_5_seconds_of_its_time()
    {
        await using var services = Services();
        var job = Job(services);
        await job.StartAsync(default);
        try
        {
            await _endpoint.WaitForAsync(1);
            var firstRung = (await StateAsync()).NudgedUtc;

            var startsAt = Start.UtcDateTime.AddSeconds(2.5);
            var message = await _db.AddMessageAsync(Paging);
            var activation = await _db.AddActivationAsync(message, startsAt, null, [GateA1]);
            await _db.AddShownVersionAsync(activation, 1, Start.UtcDateTime, new Dictionary<string, string> { ["eng"] = "Boarding" });

            DateTime? rungAt = null;
            for (var second = 1; second <= 10 && rungAt is null; second++)
            {
                await AdvanceOneTickAsync();
                var state = await StateAsync();
                if (state.NudgedUtc != firstRung) rungAt = state.NudgedUtc;
            }

            Assert.NotNull(rungAt);
            Assert.InRange(rungAt.Value, startsAt, startsAt.AddSeconds(5));
            await _endpoint.WaitForAsync(2);
        }
        finally
        {
            await job.StopAsync(default);
        }
    }

    // INK-09: a change that bumps the version is nudged on the next 1-second check, well before the hash check.
    [LocalDbFact]
    public async Task The_job_rings_for_a_bumped_version_on_its_next_one_second_check()
    {
        await using var services = Services();
        var job = Job(services);
        await job.StartAsync(default);
        try
        {
            await _endpoint.WaitForAsync(1);
            await AdvanceOneTickAsync();

            await BumpAsync();
            await AdvanceOneTickAsync();

            await _endpoint.WaitForAsync(2);
            Assert.Equal(Start.UtcDateTime.AddSeconds(2), (await StateAsync()).NudgedUtc);
        }
        finally
        {
            await job.StopAsync(default);
        }
    }

    // INK-09 "every second", HOST-04: a hold of the lease ends a little before its term so the instance never checks
    // without it, and is taken again at once, so the checks carry on across the end of a term.
    [LocalDbFact]
    public async Task The_checks_carry_on_every_second_across_the_end_of_a_lease_term()
    {
        var timing = new MessagingDoorbellTiming(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(20));
        await using var services = Services();
        var job = Job(services, timing);
        await job.StartAsync(default);
        try
        {
            await _endpoint.WaitForAsync(1);
            // The first hold ends at 18 s, a tenth of the term before the lease's own expiry.
            for (var second = 1; second <= 19; second++) await AdvanceOneTickAsync();

            await BumpAsync();
            await AdvanceOneTickAsync();

            await _endpoint.WaitForAsync(2);
            Assert.Equal(Start.UtcDateTime.AddSeconds(20), (await StateAsync()).NudgedUtc);
        }
        finally
        {
            await job.StopAsync(default);
        }
    }

    [Fact]
    public void The_lease_is_named_per_tenant_and_the_checks_run_every_second_and_every_5_seconds()
    {
        Assert.Equal("messaging-doorbell:gtaa-tenant", MessagingDoorbellService.LeaseName("gtaa-tenant"));
        Assert.Equal((TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)),
            (MessagingDoorbellTiming.Default.VersionCheck, MessagingDoorbellTiming.Default.HashCheck));
        Assert.Equal(TimeSpan.FromSeconds(2), MessagingDoorbell.NudgeTimeout);
    }

    // The job's two timers (lease retry, lease term) plus its one-second wait.
    private const int JobTimers = 3;

    /// <summary>Moves the clock one second and waits until the job has run its check and is waiting again.</summary>
    private async Task AdvanceOneTickAsync()
    {
        await TwoHosts.WaitForAsync(() => _clock.PendingTimers == JobTimers);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await TwoHosts.WaitForAsync(() => _clock.PendingTimers == JobTimers);
    }

    private async Task<MessagingDoorbell> RungDoorbellAsync()
    {
        var doorbell = Doorbell();
        Assert.True(await doorbell.CheckAsync(recomputeHash: true, default));
        await _endpoint.WaitForAsync(1);
        return doorbell;
    }

    private async Task BumpAsync()
    {
        await using var db = _db.Director();
        await DeliveryVersion.BumpAsync(db, default);
        await db.SaveChangesAsync();
    }

    private async Task<TblMessagingDeliveryState> StateAsync()
    {
        await using var db = _db.Director();
        return await db.TblMessagingDeliveryStates.AsNoTracking().SingleAsync();
    }

    private MessagingDoorbell Doorbell(string url = Url) =>
        new(_db.Factory, Delivery(), _endpoint, Options.Create(Messaging(url)), _clock, NullLogger<MessagingDoorbell>.Instance);

    private MessageDelivery Delivery() =>
        new(_db.Factory, new MessageArbitration(Options.Create(Messaging(Url))),
            new DisplayTimeZones(Options.Create(Messaging(Url)), NullLogger<DisplayTimeZones>.Instance),
            Options.Create(new D02GtaaMessagingOptions()), _clock, NullLogger<MessageDelivery>.Instance);

    private static MessagingOptions Messaging(string url) =>
        new() { DefaultTimeZone = "America/Toronto", Delivery = new MessagingDeliveryOptions { DoorbellUrl = url } };

    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_db.Factory);
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<IHttpClientFactory>(_endpoint);
        services.AddSingleton(Options.Create(Messaging(Url)));
        services.AddSingleton(Options.Create(new D02GtaaMessagingOptions()));
        services.AddSingleton<MessageArbitration>();
        services.AddSingleton<DisplayTimeZones>();
        services.AddScoped<MessageDelivery>();
        services.AddScoped<MessagingDoorbell>();
        services.AddScoped<ITenantContext>(_ => new MessagingTenant());
        services.AddSingleton<IWorkerScopeFactory, OneTenant>();
        services.AddSingleton<IDistributedLockService, NoOpDistributedLockService>();
        return services.BuildServiceProvider();
    }

    private MessagingDoorbellService Job(IServiceProvider services, MessagingDoorbellTiming? timing = null) => new(
        services.GetRequiredService<IWorkerScopeFactory>(), services.GetRequiredService<IDistributedLockService>(),
        Options.Create(Messaging(Url)), _clock, NullLogger<MessagingDoorbellService>.Instance, timing ?? MessagingDoorbellTiming.Default);

    /// <summary>The tenant, with Messaging on.</summary>
    internal sealed class MessagingTenant : ITenantContext
    {
        public bool IsPopulated => true;
        public string TenantId => "gtaa-tenant";
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
        public TenantFeatureFlags Features => new() { Messaging = true };
        public string MediaRootPath => "";
        public string? SiteUrl => null;
    }

    private sealed class OneTenant(IServiceScopeFactory scopes) : IWorkerScopeFactory
    {
        public IReadOnlyList<string> WorkerTenants => ["gtaa"];

        public Task<IServiceScope> CreateTenantScopeAsync(string subdomain, CancellationToken ct = default) =>
            Task.FromResult(scopes.CreateScope());
    }

    internal sealed record Nudge(HttpMethod Method, string Url, long BodyLength);

    /// <summary>The script's doorbell listener in memory: records each nudge, or fails every one while <see cref="Failing"/>.</summary>
    internal sealed class DoorbellEndpoint : HttpMessageHandler, IHttpClientFactory
    {
        private readonly ConcurrentQueue<Nudge> _nudges = new();
        private int _attempts;

        public volatile bool Failing;

        public IReadOnlyList<Nudge> Nudges => _nudges.ToList();

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        public Task WaitForAsync(int nudges) => TwoHosts.WaitForAsync(() => _nudges.Count >= nudges);

        public Task WaitForAttemptsAsync(int attempts) => TwoHosts.WaitForAsync(() => Volatile.Read(ref _attempts) >= attempts);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _attempts);
            if (Failing) throw new HttpRequestException("No listener");
            var body = request.Content is null ? 0 : (await request.Content.ReadAsByteArrayAsync(ct)).LongLength;
            _nudges.Enqueue(new Nudge(request.Method, request.RequestUri!.ToString(), body));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
