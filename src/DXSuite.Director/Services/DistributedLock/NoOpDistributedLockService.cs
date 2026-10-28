using DXSuite.Shared.Services;

namespace DXSuite.Director.Services.DistributedLock
{
    /// <summary>
    /// No-op implementation for single-instance deployments where distributed locking is not needed.
    /// Always returns a lock immediately (no actual coordination).
    /// </summary>
    public class NoOpDistributedLockService : IDistributedLockService
    {
        public Task<IDisposable?> TryAcquireLockAsync(string resource, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IDisposable?>(new NoOpLock());
        }

        private class NoOpLock : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
