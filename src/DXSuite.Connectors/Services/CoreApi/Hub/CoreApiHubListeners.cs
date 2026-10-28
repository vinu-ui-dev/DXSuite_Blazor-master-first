using DXSuite.Connectors.Services.CoreApi.Models;
using DXSuite.Shared.Multitenancy;
using Microsoft.Extensions.Logging;

namespace DXSuite.Connectors.Services.CoreApi.Hub;

/// <summary>
/// A page's live CoreAPI updates, fed by this Director instance's one read-only hub listener for the page's tenant
/// (CAPI-05, A-35). Resolve it from the page's circuit scope.
/// </summary>
public interface ICoreApiLiveFeed
{
    /// <summary>
    /// Feeds <paramref name="page"/> every hub event until the returned subscription is disposed; dispose it when
    /// the page goes. When the tenant has no hub address or no readable key, nothing is fed.
    /// </summary>
    Task<IAsyncDisposable> SubscribeAsync(ICoreApiHubObserver page, CancellationToken ct = default);
}

internal sealed class CoreApiLiveFeed(CoreApiHubListeners listeners, ICoreApiSettingsStore settings, ITenantContext tenant) : ICoreApiLiveFeed
{
    public async Task<IAsyncDisposable> SubscribeAsync(ICoreApiHubObserver page, CancellationToken ct = default)
    {
        var current = await settings.ReadAsync(ct);
        var tenantKey = tenant.IsPopulated ? tenant.TenantId : "";
        return await listeners.SubscribeAsync(tenantKey, current, page);
    }
}

/// <summary>
/// This instance's read-only hub listeners, one per tenant with a page open, so CoreAPI sees one connection per
/// instance however many browser tabs are open (CAPI-05, A-35). A listener starts with its tenant's first page,
/// closes with its last, and fans every event out to that tenant's pages. Listeners write nothing: shared state
/// is the leased watcher's to write.
/// </summary>
public sealed class CoreApiHubListeners : IAsyncDisposable
{
    private readonly ILogger<CoreApiHubListeners> _logger;
    private readonly TimeSpan _retryDelay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Listener> _listeners = new(StringComparer.OrdinalIgnoreCase);

    public CoreApiHubListeners(ILogger<CoreApiHubListeners> logger, TimeSpan retryDelay)
    {
        _logger = logger;
        _retryDelay = retryDelay;
    }

    /// <summary>
    /// Adds a page to the tenant's listener, starting it if it is the first. When the tenant's hub address or key
    /// changed since the listener started, the listener reconnects with the new ones.
    /// </summary>
    public async Task<IAsyncDisposable> SubscribeAsync(string tenantKey, CoreApiSettings? settings, ICoreApiHubObserver page)
    {
        if (settings is not { IsConfigured: true, HubUrl: not null }) return NoFeed.Instance;

        await _gate.WaitAsync();
        try
        {
            if (!_listeners.TryGetValue(tenantKey, out var listener))
            {
                listener = new Listener();
                _listeners[tenantKey] = listener;
            }

            if (listener.Connection is null || !listener.Connection.Uses(settings))
            {
                var old = listener.Connection;
                listener.Connection = new CoreApiHubConnection(settings.HubUrl, settings.DirectorKey!, listener, _logger, _retryDelay);
                listener.Connection.Start();
                if (old is not null) await old.DisposeAsync();
            }

            listener.Add(page);
            return new Subscription(this, tenantKey, listener, page);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            foreach (var listener in _listeners.Values)
            {
                if (listener.Connection is not null) await listener.Connection.DisposeAsync();
            }
            _listeners.Clear();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task UnsubscribeAsync(string tenantKey, Listener listener, ICoreApiHubObserver page)
    {
        await _gate.WaitAsync();
        try
        {
            if (!listener.Remove(page) || !_listeners.TryGetValue(tenantKey, out var current) || current != listener) return;

            _listeners.Remove(tenantKey);
            if (listener.Connection is not null) await listener.Connection.DisposeAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>One tenant's listener: the hub connection and the pages it feeds.</summary>
    private sealed class Listener : ICoreApiHubObserver
    {
        private ICoreApiHubObserver[] _pages = [];

        public CoreApiHubConnection? Connection { get; set; }

        public void Add(ICoreApiHubObserver page) => _pages = [.. _pages, page];

        /// <returns>True when that was the listener's last page.</returns>
        public bool Remove(ICoreApiHubObserver page)
        {
            _pages = _pages.Where(p => !ReferenceEquals(p, page)).ToArray();
            return _pages.Length == 0;
        }

        public Task OnFeedStatusAsync(FeedStatus status) => Each(p => p.OnFeedStatusAsync(status));

        public Task OnOutageSwitchAsync(bool on) => Each(p => p.OnOutageSwitchAsync(on));

        public Task OnDeparturesAsync(IReadOnlyList<FEDepartureFlight> flights) => Each(p => p.OnDeparturesAsync(flights));

        public Task OnArrivalsAsync(IReadOnlyList<FEArrivalFlight> flights) => Each(p => p.OnArrivalsAsync(flights));

        public Task OnFieldSettingsAsync(string apiKeyId, UserSettings settings) => Each(p => p.OnFieldSettingsAsync(apiKeyId, settings));

        // Every page is called even when one throws, so one broken page cannot stop the others updating.
        private async Task Each(Func<ICoreApiHubObserver, Task> call)
        {
            List<Exception>? failures = null;
            foreach (var page in _pages)
            {
                try { await call(page); }
                catch (Exception ex) { (failures ??= []).Add(ex); }
            }
            if (failures is not null) throw new AggregateException(failures);
        }
    }

    private sealed class Subscription(CoreApiHubListeners owner, string tenantKey, Listener listener, ICoreApiHubObserver page) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                await owner.UnsubscribeAsync(tenantKey, listener, page);
        }
    }

    private sealed class NoFeed : IAsyncDisposable
    {
        public static readonly NoFeed Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
