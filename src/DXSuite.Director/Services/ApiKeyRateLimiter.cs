using System.Collections.Concurrent;

namespace DXSuite.Director.Services;

/// <summary>
/// In-memory rate limiter for API key authentication attempts (IS-LACS2026).
/// Tracks failed attempts per IP address using a sliding window. After exceeding
/// the max attempts threshold, the IP is locked out for a configurable duration.
/// </summary>
public class ApiKeyRateLimiter : IDisposable
{
    private readonly ConcurrentDictionary<string, ClientRecord> _clients = new();
    private readonly IConfiguration _configuration;
    private readonly Timer _purgeTimer;

    public ApiKeyRateLimiter(IConfiguration configuration)
    {
        _configuration = configuration;
        // Purge expired entries every 5 minutes
        _purgeTimer = new Timer(_ => PurgeExpired(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    private int MaxAttempts => _configuration.GetValue<int>("Security:ApiKeyLockout:MaxAttempts", 5);
    private int WindowMinutes => _configuration.GetValue<int>("Security:ApiKeyLockout:WindowMinutes", 15);
    private int LockoutMinutes => _configuration.GetValue<int>("Security:ApiKeyLockout:LockoutMinutes", 30);

    /// <summary>
    /// Checks whether the given IP is currently locked out.
    /// Returns the lockout expiry time if locked out, or null if allowed.
    /// </summary>
    public DateTime? GetLockoutExpiry(string ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress)) return null;

        if (!_clients.TryGetValue(ipAddress, out var record))
            return null;

        if (record.LockedUntil.HasValue && record.LockedUntil.Value > DateTime.UtcNow)
            return record.LockedUntil;

        return null;
    }

    /// <summary>
    /// Records a failed authentication attempt for the given IP.
    /// If the failure count exceeds MaxAttempts within the sliding window, triggers a lockout.
    /// </summary>
    public void RecordFailure(string ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress)) return;

        var now = DateTime.UtcNow;
        var windowStart = now.AddMinutes(-WindowMinutes);

        var record = _clients.GetOrAdd(ipAddress, _ => new ClientRecord());

        lock (record)
        {
            // Remove attempts outside the sliding window
            record.FailedAttempts.RemoveAll(t => t < windowStart);
            record.FailedAttempts.Add(now);

            if (record.FailedAttempts.Count >= MaxAttempts)
            {
                record.LockedUntil = now.AddMinutes(LockoutMinutes);
            }
        }
    }

    /// <summary>
    /// Clears tracked failures for an IP after successful authentication.
    /// </summary>
    public void ClearFailures(string ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress)) return;
        _clients.TryRemove(ipAddress, out _);
    }

    private void PurgeExpired()
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddMinutes(-WindowMinutes);

        foreach (var kvp in _clients)
        {
            var record = kvp.Value;
            lock (record)
            {
                // Remove if lockout has expired and no recent failures
                if (record.LockedUntil.HasValue && record.LockedUntil.Value <= now)
                    record.LockedUntil = null;

                record.FailedAttempts.RemoveAll(t => t < windowStart);

                if (!record.LockedUntil.HasValue && record.FailedAttempts.Count == 0)
                    _clients.TryRemove(kvp.Key, out _);
            }
        }
    }

    public void Dispose()
    {
        _purgeTimer.Dispose();
    }

    private class ClientRecord
    {
        public List<DateTime> FailedAttempts { get; } = new();
        public DateTime? LockedUntil { get; set; }
    }
}
