using DXSuite.Shared.Services;
using RedLockNet;

namespace DXSuite.Director.Services.DistributedLock
{
    public class RedisDistributedLockService : IDistributedLockService
    {
        private readonly IDistributedLockFactory _lockFactory;
        private readonly ILogger<RedisDistributedLockService> _logger;

        public RedisDistributedLockService(IDistributedLockFactory lockFactory, ILogger<RedisDistributedLockService> logger)
        {
            _lockFactory = lockFactory;
            _logger = logger;
        }

        public async Task<IDisposable?> TryAcquireLockAsync(string resource, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            var redLock = await _lockFactory.CreateLockAsync(
                resource,
                expiry,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(500),
                cancellationToken
            );

            if (redLock.IsAcquired)
            {
                _logger.LogDebug("Acquired distributed lock for resource: {Resource}", resource);
                return redLock;
            }

            _logger.LogDebug("Failed to acquire distributed lock for resource: {Resource}", resource);
            redLock.Dispose();
            return null;
        }
    }
}
