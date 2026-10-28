using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using DXSuite.Director.Models;
using System.IO;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.DistributedLock;
using System.Data;

namespace DXSuite.Director.Services
{
    public class AssetCleanupService : BackgroundService
    {
        private readonly DXSuite.Shared.Multitenancy.IWorkerScopeFactory _workerScopeFactory;
        private readonly IConfiguration _config;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(2);//TimeSpan.FromHours(1);
        private readonly IDistributedLockService _lockService;
        private readonly ILogger<AssetCleanupService> _logger;
        private readonly IWebHostEnvironment _env;

        public AssetCleanupService(DXSuite.Shared.Multitenancy.IWorkerScopeFactory workerScopeFactory, IConfiguration config, IDistributedLockService lockService, ILogger<AssetCleanupService> logger, IWebHostEnvironment env)
        {
            _workerScopeFactory = workerScopeFactory;
            _config = config;
            _lockService = lockService;
            _logger = logger;
            _env = env;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var distLock = await _lockService.TryAcquireLockAsync("asset-cleanup-service-lock", TimeSpan.FromMinutes(30), stoppingToken);

                    if (distLock != null)
                    {
                        _logger.LogInformation("Lock acquired for asset cleanup, running background task.");

                        // Per-tenant scopes pair each tenant's Director DB with that tenant's
                        // media root, so a deletion decision from one tenant's DB can never
                        // remove another tenant's file.
                        foreach (var tenantSubdomain in _workerScopeFactory.WorkerTenants)
                        {
                            if (stoppingToken.IsCancellationRequested) break;
                            try
                            {
                                using var scope = await _workerScopeFactory.CreateTenantScopeAsync(tenantSubdomain, stoppingToken);
                                var tenantCtx = scope.ServiceProvider.GetRequiredService<DXSuite.Shared.Multitenancy.ITenantContext>();
                                if (tenantCtx.DirectorConnectionString == null) continue;

                                var dbContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
                                var baseDirectory = DXSuite.Shared.Multitenancy.TenantMediaPaths.GetRoot(tenantCtx, _env, _config);
                                await CleanupTenantAsync(dbContext, baseDirectory, stoppingToken);
                            }
                            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error cleaning up assets for tenant {Tenant}", tenantSubdomain);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Another instance has the lock, skipping run.");
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Normal shutdown, don't log as error
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in AssetCleanupService, will retry on next interval");
                }

                try { await Task.Delay(_interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task CleanupTenantAsync(IDirectorDBContext dbContext, string baseDirectory, CancellationToken stoppingToken)
        {
            var deletedAssets = await dbContext.TblAssets
                .Where(a => a.IsDeleted)
                .ToListAsync(stoppingToken);

            foreach (var asset in deletedAssets)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    bool isReferencedInDisplay =
                        await dbContext.TblDisplays.AnyAsync(d => d.OverrideAssetId == asset.AssetId, stoppingToken);

                    // Search for asset ID reference in the JSON using database-side pattern matching
                    // Pattern matches "AssetId":N where N is followed by comma or closing brace
                    // Use NOLOCK hint via raw SQL to avoid blocking user operations
                    var assetIdPattern = $"%\"AssetId\":{asset.AssetId}[,}}]%";
                    var db = (DbContext)dbContext;
                    var isReferencedInSchedule = await db.Database
                        .SqlQueryRaw<bool>(
                            @"SELECT CAST(CASE WHEN EXISTS (
                                SELECT 1 FROM [tblScheduleEvent] WITH (NOLOCK)
                                WHERE [ContentAssignment] LIKE {0}
                            ) THEN 1 ELSE 0 END AS BIT) AS [Value]",
                            assetIdPattern)
                        .FirstOrDefaultAsync(stoppingToken);

                    bool isReferenced = isReferencedInDisplay || isReferencedInSchedule;

                    if (!isReferenced)
                    {
                        bool assetRemoved = false;

                        if (!string.IsNullOrWhiteSpace(asset.AssetPath))
                        {
                            var path = Path.Combine(baseDirectory, asset.AssetPath.TrimStart('\\', '/'));

                            if (File.Exists(path))
                            {
                                File.Delete(path);
                                assetRemoved = true;
                            }

                            // Delete the Omnivex .metadata sidecar written alongside the asset.
                            // Checked independently of the main file so it is removed even if the
                            // main file was already cleaned up by a previous run.
                            var metaPath = path + ".metadata";
                            if (File.Exists(metaPath))
                                File.Delete(metaPath);
                        }

                        if (!string.IsNullOrWhiteSpace(asset.VideoMaskPath))
                        {
                            var maskPath = Path.Combine(baseDirectory, asset.VideoMaskPath.TrimStart('\\', '/'));
                            if (File.Exists(maskPath))
                            {
                                File.Delete(maskPath);
                            }
                        }

                        if (assetRemoved)
                        {
                            dbContext.TblAssets.Remove(asset);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking references for asset {AssetId}, skipping", asset.AssetId);
                }
            }

            await dbContext.SaveChangesAsync(stoppingToken);
        }
    }
}
