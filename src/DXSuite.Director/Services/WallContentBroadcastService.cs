using System.Collections.Concurrent;

namespace DXSuite.Director.Services;

/// <summary>
/// One wall-content change notice. Null ActivateAtUtc means "refresh immediately";
/// a value means "apply the refresh exactly at that instant" so every output of a
/// wall cuts together (subscribers wait out the delay before refreshing).
/// </summary>
public sealed record WallContentChange(DateTime? ActivateAtUtc);

/// <summary>
/// In-process pub/sub for video wall content changes, keyed by (tenant, wall location).
/// Wall render surfaces subscribe so they learn about edits and schedule boundaries the
/// moment they happen instead of on their own polling timers, which is what let a
/// four-output wall transition raggedly over tens of seconds.
/// </summary>
public interface IWallContentBroadcastService
{
    IDisposable Subscribe(string tenantId, int wallLocationId, Func<WallContentChange, Task> handler);
    Task PublishAsync(string tenantId, int wallLocationId, WallContentChange change);
}

public sealed class WallContentBroadcastService : IWallContentBroadcastService
{
    private readonly ConcurrentDictionary<(string TenantId, int LocationId),
        ConcurrentDictionary<Guid, Func<WallContentChange, Task>>> _subscribers = new();
    private readonly ILogger<WallContentBroadcastService> _logger;

    public WallContentBroadcastService(ILogger<WallContentBroadcastService> logger)
    {
        _logger = logger;
    }

    public IDisposable Subscribe(string tenantId, int wallLocationId, Func<WallContentChange, Task> handler)
    {
        var key = (tenantId, wallLocationId);
        var handlers = _subscribers.GetOrAdd(key, _ => new ConcurrentDictionary<Guid, Func<WallContentChange, Task>>());
        var id = Guid.NewGuid();
        handlers[id] = handler;
        return new Subscription(() =>
        {
            handlers.TryRemove(id, out _);
            if (handlers.IsEmpty)
                _subscribers.TryRemove(KeyValuePair.Create(key, handlers));
        });
    }

    public Task PublishAsync(string tenantId, int wallLocationId, WallContentChange change)
    {
        if (!_subscribers.TryGetValue((tenantId, wallLocationId), out var handlers) || handlers.IsEmpty)
        {
            _logger.LogInformation(
                "Wall content change for location {LocationId} had no subscribers", wallLocationId);
            return Task.CompletedTask;
        }

        _logger.LogInformation(
            "Publishing wall content change for location {LocationId} to {Count} subscriber(s) (activate at {ActivateAt})",
            wallLocationId, handlers.Count, change.ActivateAtUtc?.ToString("o") ?? "immediately");

        // Handlers run detached: a subscriber honoring ActivateAtUtc sleeps until the
        // activation instant, and the publisher must not wait on that.
        foreach (var handler in handlers.Values)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await handler(change);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Wall content subscriber failed for location {LocationId}", wallLocationId);
                }
            });
        }

        return Task.CompletedTask;
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _dispose;
        public Subscription(Action dispose) => _dispose = dispose;
        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }
}
