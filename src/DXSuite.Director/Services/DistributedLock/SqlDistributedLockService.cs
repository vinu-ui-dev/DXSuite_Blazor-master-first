using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Services.DistributedLock
{
    /// <summary>
    /// Distributed locking via SQL Server sp_getapplock / sp_releaseapplock.
    /// Each lock holds a dedicated DbContext (and its underlying connection) open
    /// for the lifetime of the lock. No tables or schema changes required.
    /// </summary>
    public class SqlDistributedLockService : IDistributedLockService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SqlDistributedLockService> _logger;

        public SqlDistributedLockService(
            IServiceScopeFactory scopeFactory,
            ILogger<SqlDistributedLockService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<IDisposable?> TryAcquireLockAsync(string resource, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            DirectorDbContext? context = null;
            try
            {
                // Create a short-lived scope to resolve the scoped factory; the scope is
                // disposed immediately after context creation — the context outlives the scope
                // because SqlLock owns and disposes it independently.
                using var scope = _scopeFactory.CreateScope();
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DirectorDbContext>>();
                context = await factory.CreateDbContextAsync(cancellationToken);
                var connection = context.Database.GetDbConnection();
                await connection.OpenAsync(cancellationToken);

                // sp_getapplock return codes: 0 = granted, 1 = granted after wait, <0 = failed
                var returnCode = new SqlParameter("@ReturnCode", System.Data.SqlDbType.Int)
                {
                    Direction = System.Data.ParameterDirection.Output
                };

                await context.Database.ExecuteSqlRawAsync(
                    "EXEC @ReturnCode = sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 5000",
                    [resource, returnCode],
                    cancellationToken);

                var result = (int)returnCode.Value;

                if (result >= 0)
                {
                    _logger.LogDebug("Acquired distributed lock for resource: {Resource} (result: {Result})", resource, result);
                    return new SqlLock(context, resource, expiry, _logger);
                }

                _logger.LogDebug("Failed to acquire distributed lock for resource: {Resource} (result: {Result})", resource, result);
                await context.DisposeAsync();
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error acquiring distributed lock for resource: {Resource}", resource);
                if (context != null)
                    await context.DisposeAsync();
                return null;
            }
        }

        private class SqlLock : IDisposable
        {
            private readonly DirectorDbContext _context;
            private readonly string _resource;
            private readonly ILogger _logger;
            private readonly Timer _expiryTimer;
            private int _disposed;

            public SqlLock(DirectorDbContext context, string resource, TimeSpan expiry, ILogger logger)
            {
                _context = context;
                _resource = resource;
                _logger = logger;

                // Safety net: auto-release after the requested expiry
                _expiryTimer = new Timer(_ => Dispose(), null, expiry, Timeout.InfiniteTimeSpan);
            }

            public void Dispose()
            {
                if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                    return;

                _expiryTimer.Dispose();

                try
                {
                    _context.Database.ExecuteSqlRaw(
                        "EXEC sp_releaseapplock @Resource = {0}, @LockOwner = 'Session'", _resource);
                }
                catch (Exception ex)
                {
                    // Connection may already be closed (app shutdown, expiry race) — SQL Server
                    // releases session-scoped locks automatically when the connection drops
                    _logger.LogDebug(ex, "Could not explicitly release lock {Resource} (connection may be closed)", _resource);
                }

                _context.Dispose();
            }
        }
    }
}
