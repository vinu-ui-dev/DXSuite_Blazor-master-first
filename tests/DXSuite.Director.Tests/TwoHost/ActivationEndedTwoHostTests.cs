using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using DXSuite.Director.Services.DistributedLock;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DXSuite.Director.Tests.TwoHost;

// Clause ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// B-53's <c>activation.ended</c> on two instances (HOST-04 items 2 and 5): both Director instances run the job
/// against one database, and each Activation's end is still logged once.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "TwoHost")]
public sealed class ActivationEndedTwoHostTests
{
    private const int EndedEventTypeId = 116;
    private const int EndedAlready = 6;

    [LocalDbFact]
    public async Task Two_hosts_log_one_activation_ended_per_Activation()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));
        var now = clock.GetUtcNow().UtcDateTime;
        await using var hosts = await TwoHosts.StartAsync(o =>
        {
            o.SeedAsync = db => SeedAsync(db, now);
            o.ConfigureServices = (_, services, _) =>
            {
                services.AddScoped<IDirectorDBContext>(sp => sp.GetRequiredService<DirectorDbContext>());
                services.AddUserEventLog();
                services.AddSingleton<IWorkerScopeFactory, OneTenantOnTheSharedDatabase>();
                services.AddScoped<ActivationEndedLog>();
                // Both hosts start at once and keep polling, as two nodes restarted together would.
                services.AddSingleton(sp => new ActivationEndedService(
                    sp.GetRequiredService<IWorkerScopeFactory>(),
                    sp.GetRequiredService<IDistributedLockService>(),
                    clock,
                    sp.GetRequiredService<ILogger<ActivationEndedService>>(),
                    startupDelay: TimeSpan.Zero,
                    pollInterval: TimeSpan.FromMilliseconds(100)));
                services.AddHostedService(sp => sp.GetRequiredService<ActivationEndedService>());
            };
        });

        await TwoHosts.WaitForAsync(() => EndedEventsAsync(hosts).GetAwaiter().GetResult().Count >= EndedAlready);
        await BothHostsRunACycleAsync(hosts);
        var first = await EndedEventsAsync(hosts);
        Assert.Equal(EndedAlready, first.Count);
        Assert.Equal(EndedAlready, first.Select(e => e.TargetId).Distinct().Count());

        // The last Activation ends an hour on; its end is logged once too.
        clock.Advance(TimeSpan.FromHours(1));
        await TwoHosts.WaitForAsync(() => EndedEventsAsync(hosts).GetAwaiter().GetResult().Count > EndedAlready);
        await BothHostsRunACycleAsync(hosts);
        var all = await EndedEventsAsync(hosts);
        Assert.Equal(EndedAlready + 1, all.Count);
        Assert.Equal(EndedAlready + 1, all.Select(e => e.TargetId).Distinct().Count());
        Assert.All(all, e => Assert.Equal((UserEventActorKind.System, ActivationEndedLog.ServiceName), (e.ActorKind, e.ActorName)));
    }

    // Another cycle on both hosts at the same moment, on top of the ones their timers keep running.
    private static Task BothHostsRunACycleAsync(TwoHosts hosts) =>
        Task.WhenAll(hosts.Hosts.Select(h => h.Services.GetRequiredService<ActivationEndedService>().RunCycleAsync(CancellationToken.None)));

    private static async Task<List<TblEventLog>> EndedEventsAsync(TwoHosts hosts)
    {
        await using var db = hosts.CreateDbContext();
        return await db.TblEventLogs.AsNoTracking().Where(e => e.EventTypeId == EndedEventTypeId).ToListAsync();
    }

    /// <summary>A Message with six Activations that have ended and one that ends in an hour.</summary>
    private static async Task SeedAsync(DirectorDbContext db, DateTime now)
    {
        // director-seed.sql owns the severities, which the log still references.
        await db.Database.ExecuteSqlRawAsync("""
            SET IDENTITY_INSERT tblEventSeverity ON;
            INSERT INTO tblEventSeverity (EventSeverityId, SeverityName) VALUES (1, 'INFO'), (2, 'WARNING'), (3, 'ERROR'), (4, 'CRITICAL');
            SET IDENTITY_INSERT tblEventSeverity OFF;
            """);
        var message = new TblMessage
        {
            MessageTypeId = await db.TblMessageTypes.Select(t => t.MessageTypeId).FirstAsync(), Name = "Gate change", IsCanned = true,
            Status = MessageStatus.Approved, CreatedUtc = now,
            Activations = Enumerable.Range(1, EndedAlready)
                .Select(i => new TblMessageActivation
                {
                    StartUtc = now.AddHours(-2), EndUtc = now.AddMinutes(-i), ActivatedUtc = now.AddHours(-2),
                    Source = MessageActivationSource.DirectorAdmin,
                })
                .Append(new TblMessageActivation
                {
                    StartUtc = now.AddHours(-2), EndUtc = now.AddMinutes(30), ActivatedUtc = now.AddHours(-2),
                    Source = MessageActivationSource.DirectorAdmin,
                })
                .ToList(),
        };
        db.TblMessages.Add(message);
        await db.SaveChangesAsync();
    }

    /// <summary>The hosts have no tenant registry: one tenant whose scope is the host's own, on the shared database.</summary>
    private sealed class OneTenantOnTheSharedDatabase(IServiceScopeFactory scopes) : IWorkerScopeFactory
    {
        public IReadOnlyList<string> WorkerTenants => ["gtaa"];

        public Task<IServiceScope> CreateTenantScopeAsync(string subdomain, CancellationToken ct = default) =>
            Task.FromResult(scopes.CreateScope());
    }
}
