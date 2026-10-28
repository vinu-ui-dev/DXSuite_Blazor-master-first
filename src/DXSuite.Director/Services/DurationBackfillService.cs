using System.Xml;
using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Shared.Multitenancy;
using static DXSuite.Director.Models.Types;

namespace DXSuite.Director.Services;

/// <summary>
/// Startup backfill that probes existing video files for their true runtime. Uploads made
/// before duration capture existed carry the 7-second placeholder in tblAsset and no
/// Duration sidecar field; loop-duration display and play-count estimation need the real
/// value. The sidecar field doubles as the per-file marker, so completed files are skipped
/// on later startups.
/// </summary>
public class DurationBackfillService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ThrottleDelay = TimeSpan.FromMilliseconds(250);

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".avi", ".mkv", ".m4v", ".wmv"
    };

    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ITenantStore _tenantStore;
    private readonly IVideoProcessingService _videoProcessing;
    private readonly IWorkerScopeFactory _workerScopeFactory;
    private readonly ILogger<DurationBackfillService> _logger;

    public DurationBackfillService(
        IWebHostEnvironment env,
        IConfiguration config,
        ITenantStore tenantStore,
        IVideoProcessingService videoProcessing,
        IWorkerScopeFactory workerScopeFactory,
        ILogger<DurationBackfillService> logger)
    {
        _env = env;
        _config = config;
        _tenantStore = tenantStore;
        _videoProcessing = videoProcessing;
        _workerScopeFactory = workerScopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            await RunSidecarBackfillAsync(stoppingToken);
            await RunDbBackfillAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Host shutting down
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Duration backfill aborted unexpectedly");
        }
    }

    private async Task RunSidecarBackfillAsync(CancellationToken ct)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(_env.WebRootPath)
        };
        foreach (var tenant in await _tenantStore.GetAllEnabledAsync(ct))
            roots.Add(Path.GetFullPath(TenantMediaPaths.GetRoot(tenant, _env, _config)));

        var probed = 0;
        var failed = 0;

        foreach (var root in roots)
        {
            var mediaRoot = Path.Combine(root, "MediaAssets");
            if (!Directory.Exists(mediaRoot)) continue;

            foreach (var file in Directory.EnumerateFiles(mediaRoot, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                if (!VideoExtensions.Contains(Path.GetExtension(file))) continue;
                // Only sidecar-bearing files are playlist/library content; the field is the done-marker
                if (!File.Exists(file + ".metadata") || SidecarHasDuration(file + ".metadata")) continue;

                try
                {
                    var probe = await _videoProcessing.AnalyzeAsync(file);
                    if (probe?.Duration > 0)
                    {
                        AssetIngestService.UpdateSidecarField(file, "Duration",
                            Math.Max(1, (int)Math.Round(probe.Duration)).ToString());
                        probed++;
                    }
                    else
                    {
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(ex, "Duration backfill probe failed for {Path}", file);
                }

                await Task.Delay(ThrottleDelay, ct);
            }
        }

        if (probed > 0 || failed > 0)
            _logger.LogInformation("Sidecar duration backfill complete: {Probed} probed, {Failed} failed", probed, failed);
    }

    private async Task RunDbBackfillAsync(CancellationToken ct)
    {
        var updated = 0;

        foreach (var subdomain in _workerScopeFactory.WorkerTenants)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var scope = await _workerScopeFactory.CreateTenantScopeAsync(subdomain, ct);
                var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
                var mediaPaths = scope.ServiceProvider.GetRequiredService<ITenantMediaPathResolver>();
                var root = mediaPaths.GetRoot();

                // 7.0 is the schema default no upload path ever overwrote; anything else was probed
                var candidates = await db.TblAssets
                    .Where(a => a.AssetTypeId == (int)AssetType.Video && !a.IsDeleted && a.Duration == 7.0)
                    .ToListAsync(ct);

                foreach (var asset in candidates)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var localPath = Path.Combine(root,
                            asset.AssetPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(localPath)) continue;

                        var probe = await _videoProcessing.AnalyzeAsync(localPath);
                        if (probe?.Duration > 0)
                        {
                            asset.Duration = probe.Duration;
                            if (asset.AssetWidth == 0) asset.AssetWidth = probe.Width;
                            if (asset.AssetHeight == 0) asset.AssetHeight = probe.Height;
                            updated++;
                        }
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        _logger.LogWarning(ex, "Duration backfill failed for asset {AssetId}", asset.AssetId);
                    }
                    await Task.Delay(ThrottleDelay, ct);
                }

                if (candidates.Count > 0)
                {
                    await db.SaveChangesAsync(ct);
                    scope.ServiceProvider.GetRequiredService<IPublicationInvalidator>()
                        .AssetsChanged(candidates.Select(a => a.LocationId));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Duration backfill skipped tenant {Tenant}", subdomain);
            }
        }

        if (updated > 0)
            _logger.LogInformation("Asset duration backfill complete: {Updated} assets updated", updated);
    }

    private static bool SidecarHasDuration(string metadataFilePath)
    {
        try
        {
            var doc = new XmlDocument();
            doc.Load(metadataFilePath);
            return doc.SelectSingleNode("usermetadata/field[@name='Duration']") != null;
        }
        catch
        {
            // Unreadable sidecar: let the probe pass attempt the rewrite
            return false;
        }
    }
}
