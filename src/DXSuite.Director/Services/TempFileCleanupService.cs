using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DXSuite.Director.Services;

/// <summary>
/// Sweeps the TempFiles directory every 30 minutes and removes subdirectories/files older
/// than the configured threshold. This handles abandoned uploads from page refreshes,
/// browser closes, or other interruptions.
/// </summary>
public class TempFileCleanupService : BackgroundService
{
    private static readonly TimeSpan _interval = TimeSpan.FromMinutes(30);

    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly DXSuite.Shared.Multitenancy.ITenantStore _tenantStore;
    private readonly ILogger<TempFileCleanupService> _logger;

    public TempFileCleanupService(
        IWebHostEnvironment env,
        IConfiguration config,
        DXSuite.Shared.Multitenancy.ITenantStore tenantStore,
        ILogger<TempFileCleanupService> logger)
    {
        _env = env;
        _config = config;
        _tenantStore = tenantStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                SweepTempFiles();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during TempFiles sweep");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void SweepTempFiles()
    {
        var maxAgeHours = _config.GetValue<int>("TempFiles:MaxAgeHours", 2);
        var threshold = DateTime.UtcNow.AddHours(-maxAgeHours);

        // Sweep the local web root, not FilePaths:BaseUNC. BaseUNC is the network share Pixera
        // reads media from; it is published over this same wwwroot, so the local path targets the
        // exact files uploads and content-package imports write. Reaching the UNC from this host
        // instead goes over the SMB loopback as the app-pool/machine identity, which lacks delete
        // rights on those folders and fails with "Access denied". Matches MediaAssetUploader, which
        // likewise writes only to the local web root.
        // Uploads land under each tenant's media root, so sweep every distinct root (they all
        // collapse to the global web root when tenant media isolation is disabled).
        foreach (var baseRoot in ResolveTempRoots())
            SweepRoot(baseRoot, threshold);
    }

    private IEnumerable<string> ResolveTempRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(_env.WebRootPath))
            roots.Add(Path.GetFullPath(_env.WebRootPath));

        foreach (var tenant in _tenantStore.GetAllEnabledAsync().GetAwaiter().GetResult())
            roots.Add(Path.GetFullPath(DXSuite.Shared.Multitenancy.TenantMediaPaths.GetRoot(tenant, _env, _config)));

        return roots;
    }

    private void SweepRoot(string baseRoot, DateTime threshold)
    {
        var tempRoot = Path.Combine(baseRoot, "TempFiles");
        if (!Directory.Exists(tempRoot)) return;

        // Delete orphaned top-level files (validation copies)
        foreach (var file in Directory.GetFiles(tempRoot))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < threshold)
                {
                    File.Delete(file);
                    _logger.LogInformation("Deleted orphaned temp file: {File}", file);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete temp file: {File}", file);
            }
        }

        // Delete subdirectories older than the threshold (location-scoped temp uploads)
        foreach (var dir in Directory.GetDirectories(tempRoot))
        {
            try
            {
                var lastWrite = Directory.GetLastWriteTimeUtc(dir);
                if (lastWrite < threshold)
                {
                    Directory.Delete(dir, recursive: true);
                    _logger.LogInformation("Deleted orphaned temp directory: {Dir}", dir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete temp directory: {Dir}", dir);
            }
        }
    }
}
