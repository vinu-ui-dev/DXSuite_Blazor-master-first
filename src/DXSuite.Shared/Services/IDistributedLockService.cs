namespace DXSuite.Shared.Services;

/// <summary>
/// Distributed locking service for coordinating operations across multiple application instances.
/// Returns null if lock cannot be acquired within the wait time.
/// </summary>
public interface IDistributedLockService
{
    Task<IDisposable?> TryAcquireLockAsync(string resource, TimeSpan expiry, CancellationToken cancellationToken = default);
}
