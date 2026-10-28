using DXSuite.Connectors.Services.CoreApi;
using DXSuite.Connectors.Services.CoreApi.Hub;
using DXSuite.Connectors.Services.CoreApi.Models;
using DXSuite.Director.Services.DistributedLock;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Director.Services.CoreApi;

/// <summary>How often the watcher does each part of its work; only tests change these.</summary>
/// <param name="Poll">Between <c>FeedStatus</c> polls.</param>
/// <param name="LeaseRetry">Between attempts to take a tenant's lease, and to notice a tenant whose flag came on.</param>
/// <param name="LeaseTerm">How long one hold of the lease lasts before it is given up and taken again.</param>
/// <param name="HubRetry">Before reconnecting to a hub that is down.</param>
public sealed record CoreApiWatcherTiming(TimeSpan Poll, TimeSpan LeaseRetry, TimeSpan LeaseTerm, TimeSpan HubRetry)
{
    public static CoreApiWatcherTiming Default { get; } = new(
        CoreApiWatcherService.PollInterval, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(10), CoreApiServiceCollectionExtensions.DefaultHubRetryDelay);
}

/// <summary>
/// The CoreAPI watcher (CAPI-05): for each worker tenant with the Flight Data Editor on, exactly one Director
/// instance, the holder of the SQL lease <c>coreapi-watcher:{tenantId}</c>, holds the hub connection, polls
/// <c>FeedStatus</c> every 30 seconds as a safety net for missed hub events, writes the feed state to
/// <c>tblCoreApiFeedState</c> and logs <c>coreapi.feed-down</c>, <c>coreapi.feed-up</c>, <c>coreapi.unreachable</c>
/// and <c>coreapi.reachable</c> once per change. Pages never read the hub through the watcher: each instance's
/// read-only listener (<see cref="CoreApiHubListeners"/>) feeds its own pages.
/// </summary>
/// <remarks>
/// Each hold of the lease runs every registered <see cref="ICoreApiWatcherStartTask"/> once, after the first poll, and
/// every <see cref="ICoreApiWatcherSwitchTask"/> on each hub report of the flight Outage mode switch. The lease is held
/// for <see cref="CoreApiWatcherTiming.LeaseTerm"/> and then taken again at once (<see cref="TenantLeaseHolds"/>), so a
/// holder whose SQL session was lost without it noticing stops within one term; until then the feed state is replaced
/// only from the state its writer read, so two watchers still log each change once.
/// </remarks>
public sealed class CoreApiWatcherService : BackgroundService
{
    /// <summary>A fixed value, not a setting (1.1 section 3.10).</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    /// <summary>The actor name on the watcher's System events.</summary>
    public const string ServiceName = "CoreAPI watcher";

    public static string LeaseName(string tenantId) => $"coreapi-watcher:{tenantId}";

    private readonly IWorkerScopeFactory _workerScopes;
    private readonly ILogger<CoreApiWatcherService> _logger;
    private readonly CoreApiWatcherTiming _timing;
    private readonly TimeProvider _clock;
    private readonly TenantLeaseHolds _holds;

    public CoreApiWatcherService(IWorkerScopeFactory workerScopes, IDistributedLockService leases, ILogger<CoreApiWatcherService> logger)
        : this(workerScopes, leases, logger, CoreApiWatcherTiming.Default)
    {
    }

    internal CoreApiWatcherService(IWorkerScopeFactory workerScopes, IDistributedLockService leases, ILogger<CoreApiWatcherService> logger,
        CoreApiWatcherTiming timing, TimeProvider? clock = null)
    {
        _workerScopes = workerScopes;
        _logger = logger;
        _timing = timing;
        _clock = clock ?? TimeProvider.System;
        _holds = new TenantLeaseHolds(workerScopes, leases, _clock, logger, "The CoreAPI watcher", LeaseName, ShouldWatch,
            timing.LeaseRetry, timing.LeaseTerm);
    }

    /// <summary>The tenant ids this instance holds the watcher lease for right now.</summary>
    public IReadOnlyCollection<string> TenantsWatched => _holds.TenantsHeld;

    /// <summary>
    /// Whether the tenant needs a watcher: one with the Flight Data Editor on. The rule lives here alone, so that a
    /// second CoreAPI feed needing a watcher widens it in one place.
    /// </summary>
    internal static bool ShouldWatch(ITenantContext tenant) =>
        tenant.IsPopulated && tenant.Features.FlightDataEditor;

    // Each tenant's watch gives up its lease and closes its hub connection before the host stops.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => _holds.RunAsync(WatchTenantAsync, stoppingToken);

    private async Task WatchTenantAsync(string subdomain, string tenantId, CancellationToken ct)
    {
        _logger.LogInformation("Watching CoreAPI for tenant {Tenant}", tenantId);
        await using var watch = new TenantWatch(this, subdomain);
        await watch.RunAsync(ct);
    }

    /// <summary>One hold of one tenant's lease: the hub connection, the poll and the feed state it keeps.</summary>
    private sealed class TenantWatch(CoreApiWatcherService owner, string subdomain) : ICoreApiHubObserver, ICoreApiWatcherHold, IAsyncDisposable
    {
        private readonly SemaphoreSlim _observing = new(1, 1);
        private CoreApiHubConnection? _hub;
        private CancellationToken _ct;

        public async Task RunAsync(CancellationToken ct)
        {
            _ct = ct;
            var first = true;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    using (var scope = await owner._workerScopes.CreateTenantScopeAsync(subdomain, ct))
                    {
                        if (!ShouldWatch(scope.ServiceProvider.GetRequiredService<ITenantContext>())) return;

                        await PollAsync(scope.ServiceProvider, ct);
                        if (first)
                        {
                            first = false;
                            await RunStartTasksAsync(scope.ServiceProvider, ct);
                        }
                    }

                    await Task.Delay(owner._timing.Poll, owner._clock, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_hub is not null) await _hub.DisposeAsync();
        }

        public Task OnFeedStatusAsync(FeedStatus status) =>
            ObserveAsync(s => s with { Status = status.Status }, serversTried: null, _ct);

        public async Task OnOutageSwitchAsync(bool on)
        {
            await ObserveAsync(s => s with { SwitchOn = on }, serversTried: null, _ct);

            using var scope = await owner._workerScopes.CreateTenantScopeAsync(subdomain, _ct);
            foreach (var task in scope.ServiceProvider.GetServices<ICoreApiWatcherSwitchTask>())
            {
                try
                {
                    await task.OnSwitchReportedAsync(on, this, _ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    owner._logger.LogError(ex, "CoreAPI watcher switch task {Task} failed for tenant {Tenant}", task.GetType().Name, subdomain);
                }
            }
        }

        public Task RecordSwitchAsync(bool on, CancellationToken ct) =>
            ObserveAsync(s => s with { SwitchOn = on }, serversTried: null, ct);

        private async Task PollAsync(IServiceProvider services, CancellationToken ct)
        {
            var settings = await services.GetRequiredService<ICoreApiSettingsStore>().ReadAsync(ct);
            await KeepHubAsync(settings);

            var result = await services.GetRequiredService<CoreApiClient>().GetFeedStatusAsync(ct);
            Func<CoreApiFeedState, CoreApiFeedState>? apply = result.Outcome switch
            {
                CoreApiOutcome.Succeeded => s => s with { Reachable = true, Status = result.Value!.Status },
                CoreApiOutcome.Failed => s => s with { Reachable = true },
                CoreApiOutcome.Unreachable => s => s with { Reachable = false },
                _ => null,
            };
            if (apply is not null) await ObserveAsync(apply, result.ServersTried, ct);
        }

        // The hub connection follows the tenant's settings, so a new key or hub address takes effect on the next poll.
        private async Task KeepHubAsync(CoreApiSettings? settings)
        {
            if (_hub is not null && settings is { IsConfigured: true, HubUrl: not null } && _hub.Uses(settings)) return;

            if (_hub is not null)
            {
                await _hub.DisposeAsync();
                _hub = null;
            }

            if (settings is { IsConfigured: true, HubUrl: not null })
            {
                _hub = new CoreApiHubConnection(settings.HubUrl, settings.DirectorKey!, this, owner._logger, owner._timing.HubRetry);
                _hub.Start();
            }
        }

        private async Task RunStartTasksAsync(IServiceProvider services, CancellationToken ct)
        {
            var state = await services.GetRequiredService<ICoreApiFeedStateReader>().ReadAsync(ct);
            foreach (var task in services.GetServices<ICoreApiWatcherStartTask>())
            {
                try
                {
                    await task.RunAsync(state, this, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    owner._logger.LogError(ex, "CoreAPI watcher start task {Task} failed for tenant {Tenant}", task.GetType().Name, subdomain);
                }
            }
        }

        /// <summary>
        /// Applies one observation to the stored state and logs what changed. A missing row stands for an up,
        /// reachable feed, so the first observation logs only a feed that is down or a CoreAPI that is unreachable.
        /// </summary>
        private async Task ObserveAsync(Func<CoreApiFeedState, CoreApiFeedState> apply, IReadOnlyList<string>? serversTried, CancellationToken ct)
        {
            await _observing.WaitAsync(ct);
            try
            {
                // Another writer that got in first has recorded and logged the change; the re-read then sees it.
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    using var scope = await owner._workerScopes.CreateTenantScopeAsync(subdomain, ct);
                    var store = scope.ServiceProvider.GetRequiredService<CoreApiFeedStateStore>();
                    var now = owner._clock.GetUtcNow().UtcDateTime;

                    var current = await store.ReadAsync(ct);
                    var before = current ?? new CoreApiFeedState(CoreApiFeedState.UpStatus, Reachable: true, SwitchOn: null, now);
                    var next = apply(before) with { ChangedUtc = now };
                    if (current is not null && SameState(current, next)) return;

                    if (!await store.TryReplaceAsync(current, next, ct)) continue;

                    await LogChangesAsync(scope.ServiceProvider, before, next, serversTried, ct);
                    return;
                }
            }
            finally
            {
                _observing.Release();
            }
        }

        private static bool SameState(CoreApiFeedState a, CoreApiFeedState b) =>
            a.Status == b.Status && a.Reachable == b.Reachable && a.SwitchOn == b.SwitchOn;

        // Reachability first: when CoreAPI comes back, the feed it reports follows from it being reached.
        private async Task LogChangesAsync(IServiceProvider services, CoreApiFeedState before, CoreApiFeedState next,
            IReadOnlyList<string>? serversTried, CancellationToken ct)
        {
            var log = services.GetRequiredService<IUserEventLog>();
            var actor = UserEventActor.System(ServiceName);

            if (before.Reachable != next.Reachable)
            {
                await log.TryWriteAsync(owner._logger, "CoreAPI reachability change", (l, c) => l.WriteAsync(actor,
                    new UserEvent(next.Reachable ? UserEventActionKeys.CoreApiReachable : UserEventActionKeys.CoreApiUnreachable,
                        new UserEventTarget(UserEventTargetTypes.CoreApi, null, "CoreAPI"),
                        Details: new { servers = serversTried ?? [] }), c), ct);
            }

            if (before.FeedUp != next.FeedUp)
            {
                await log.TryWriteAsync(owner._logger, "flight feed change", (l, c) => l.WriteAsync(actor,
                    new UserEvent(next.FeedUp ? UserEventActionKeys.CoreApiFeedUp : UserEventActionKeys.CoreApiFeedDown,
                        new UserEventTarget(UserEventTargetTypes.FlightFeed, null, "Flight feed"),
                        Details: new { status = next.Status }), c), ct);
            }
        }
    }
}
