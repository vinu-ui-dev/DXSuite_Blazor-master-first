using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Shared.Multitenancy;
using static DXSuite.Director.Models.Types;

namespace DXSuite.Director.Services;

/// <summary>
/// Startup backfill that brings existing media assets up to the current thumbnail convention
/// (normalized PNGs in a Thumbnails subfolder beside the media). A version marker file per folder
/// prevents reprocessing on later startups; bump ThumbnailVersion to force a global regeneration.
/// Folders with failures are not stamped, so they retry on the next startup.
/// </summary>
public class ThumbnailBackfillService : BackgroundService
{
    private const string ThumbnailVersion = "v1";
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ThrottleDelay = TimeSpan.FromMilliseconds(250);

    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ITenantStore _tenantStore;
    private readonly ThumbnailService _thumbnailService;
    private readonly IWorkerScopeFactory _workerScopeFactory;
    private readonly ILogger<ThumbnailBackfillService> _logger;

    public ThumbnailBackfillService(
        IWebHostEnvironment env,
        IConfiguration config,
        ITenantStore tenantStore,
        ThumbnailService thumbnailService,
        IWorkerScopeFactory workerScopeFactory,
        ILogger<ThumbnailBackfillService> logger)
    {
        _env = env;
        _config = config;
        _tenantStore = tenantStore;
        _thumbnailService = thumbnailService;
        _workerScopeFactory = workerScopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            await RunBackfillAsync(stoppingToken);
            await RunWebBackfillAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Host shutting down
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Thumbnail backfill aborted unexpectedly");
        }
    }

    private async Task RunBackfillAsync(CancellationToken ct)
    {
        // One pass per distinct media root: the global web root plus each tenant root
        // (all identical when tenant media isolation is disabled, so this stays one pass).
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(_env.WebRootPath)
        };
        foreach (var tenant in await _tenantStore.GetAllEnabledAsync(ct))
            roots.Add(Path.GetFullPath(TenantMediaPaths.GetRoot(tenant, _env, _config)));

        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            await BackfillMediaRootAsync(Path.Combine(root, "MediaAssets"), ct);
        }
    }

    private async Task BackfillMediaRootAsync(string mediaRoot, CancellationToken ct)
    {
        if (!Directory.Exists(mediaRoot)) return;

        var generated = 0;
        var failed = 0;

        var folders = Directory.EnumerateDirectories(mediaRoot, "*", SearchOption.AllDirectories).Prepend(mediaRoot);
        foreach (var folder in folders)
        {
            ct.ThrowIfCancellationRequested();
            if (Path.GetFileName(folder).Equals("Thumbnails", StringComparison.OrdinalIgnoreCase)) continue;

            var markerPath = Path.Combine(folder, "Thumbnails", $".{ThumbnailVersion}");
            var regenerateAll = !File.Exists(markerPath);
            var folderHadMedia = false;
            var folderFailed = false;

            foreach (var file in Directory.EnumerateFiles(folder))
            {
                ct.ThrowIfCancellationRequested();
                if (!ThumbnailService.SupportsThumbnail(file)) continue;
                // Pixera alpha masks are generated artifacts, not content
                if (file.EndsWith("_mask.png", StringComparison.OrdinalIgnoreCase)) continue;

                folderHadMedia = true;
                if (!regenerateAll && File.Exists(ThumbnailService.GetThumbnailPath(file))) continue;

                try
                {
                    if (await _thumbnailService.GenerateThumbnailAsync(file) != null) generated++;
                    else { failed++; folderFailed = true; }
                }
                catch (Exception ex)
                {
                    failed++;
                    folderFailed = true;
                    _logger.LogWarning(ex, "Thumbnail backfill failed for {Path}", file);
                }

                await Task.Delay(ThrottleDelay, ct);
            }

            if (folderHadMedia && !folderFailed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
                if (!File.Exists(markerPath)) File.Create(markerPath).Dispose();
            }
        }

        if (generated > 0 || failed > 0)
            _logger.LogInformation("Thumbnail backfill complete: {Generated} generated, {Failed} failed", generated, failed);
    }

    /// <summary>
    /// Generates missing thumbnails for web assets across all worker tenants. Unlike the media pass
    /// this is retried every startup for assets without a thumbnail file, since unreachable sites
    /// may come back later. WebPreviewService is scoped (thumbnail paths are tenant-keyed), so it
    /// must come from the tenant scope and the scope must outlive the generation loop.
    /// </summary>
    private async Task RunWebBackfillAsync(CancellationToken ct)
    {
        var generated = 0;
        foreach (var subdomain in _workerScopeFactory.WorkerTenants)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var scope = await _workerScopeFactory.CreateTenantScopeAsync(subdomain, ct);
                var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
                var webPreview = scope.ServiceProvider.GetRequiredService<WebPreviewService>();

                var webAssets = await db.TblAssets.AsNoTracking()
                    .Where(a => a.AssetTypeId == (int)AssetType.Web && !a.IsDeleted)
                    .Select(a => new { a.AssetId, a.AssetPath, a.AssetWidth, a.AssetHeight })
                    .ToListAsync(ct);

                foreach (var asset in webAssets)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if (File.Exists(webPreview.GetThumbnailLocalPath(asset.AssetId))) continue;

                        if (await webPreview.GenerateWebThumbnailAsync(asset.AssetId, asset.AssetPath, asset.AssetWidth, asset.AssetHeight))
                            generated++;
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        // One bad asset must not abandon the rest of the tenant's backfill
                        _logger.LogWarning(ex, "Web thumbnail backfill failed for asset {AssetId}", asset.AssetId);
                    }
                    await Task.Delay(ThrottleDelay, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Web thumbnail backfill skipped tenant {Tenant}", subdomain);
            }
        }

        if (generated > 0)
            _logger.LogInformation("Web thumbnail backfill complete: {Generated} generated", generated);
    }

}
