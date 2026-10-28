using Microsoft.Extensions.Primitives;

namespace DXSuite.Director.Services;

/// <summary>
/// Lets a revoked device token be evicted from the auth handler's cache immediately.
///
/// The handler caches a validation result for five minutes keyed on a hash of the plaintext token,
/// which nothing else in the system ever holds, so revocation cannot find the entry to remove it.
/// A revoked token therefore kept working for up to five minutes. The handler registers its entry
/// against a change token for the token's id, and revocation cancels it.
///
/// No grace period is intended: a grace period only ever helps an attacker, because a legitimate
/// player re-pairs deliberately.
/// </summary>
public class DeviceTokenCacheInvalidator
{
    private readonly Dictionary<int, CancellationTokenSource> _sources = new();
    private readonly Lock _gate = new();

    /// <summary>
    /// A change token that fires when the given device token is revoked or deleted. Register it on
    /// the cache entry; the entry is dropped the moment <see cref="Invalidate"/> runs.
    /// </summary>
    public IChangeToken GetChangeToken(int deviceTokenId)
    {
        lock (_gate)
        {
            if (!_sources.TryGetValue(deviceTokenId, out var source))
            {
                source = new CancellationTokenSource();
                _sources[deviceTokenId] = source;
            }

            return new CancellationChangeToken(source.Token);
        }
    }

    public void Invalidate(int deviceTokenId)
    {
        CancellationTokenSource? source;
        lock (_gate)
        {
            if (!_sources.Remove(deviceTokenId, out source))
                return;
        }

        source.Cancel();
        source.Dispose();
    }
}
