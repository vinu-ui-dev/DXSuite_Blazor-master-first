using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Services;

/// <summary>Status of one derived rendition set, surfaced in the layout editor.</summary>
public record RenditionSetStatus(
    string Hash,
    string Status,          // pending | encoding | done | failed
    string? Error,
    DateTime UpdatedUtc,
    IReadOnlyList<string> Inputs,
    int OutputsReady,
    int OutputsTotal);

public interface IBackgroundRenditionService
{
    /// <summary>
    /// Returns the web path + version of the derived per-output rendition for the
    /// given zone set and geometry, or null when it has not been generated yet.
    /// </summary>
    (string Path, long Version)? TryGetRendition(
        int locationId, IReadOnlyList<BackgroundZone> zones,
        int stripW, int stripH, int outh, int outputIndex);

    /// <summary>
    /// Returns the derived per-output rendition of the airline cluster background
    /// segment row (side-by-side tblUserGroupLocationLayerContent sequences), or null
    /// when no eligible set exists or it has not been generated yet. Cluster renditions
    /// carry VP9 alpha with transparent padding so wall-level zones stay visible where
    /// no airline cluster covers the canvas.
    /// </summary>
    (string Path, long Version)? TryGetClusterBackgroundRendition(
        int locationId, IReadOnlyList<DisplayCluster> clusters,
        int stripW, int stripH, int outh, int outputIndex);

    /// <summary>Rendition set statuses for a wall, for the layout editor UI.</summary>
    IReadOnlyList<RenditionSetStatus> GetStatuses(int locationId);
}

/// <summary>
/// Generates per-output "pre-wrapped" background video renditions for walls with
/// WrappedOutputsEnabled: the wall's virtual background row (ordered zone tiles,
/// including horizontalImageLoop repeats) is cut into output-width strips and
/// stacked vertically, producing one video per player output with the wrap baked
/// in — so the wrap-mode layer shell plays a single decode instead of slicing a
/// canvas-wide video.
///
/// Renditions are cached on disk keyed by (source files + mtimes, geometry): a
/// set is encoded once per uploaded content, never at schedule-switch time.
/// Day-parting resolves a different asset whose set already exists. The sweep
/// resolves the zone set at each schedule boundary (plus now) so upcoming
/// day-parts are derived before they activate.
///
/// Only sets where every contented zone is a video are derived — image/HTML
/// zones slice cheaply client-side, and a mixed row cannot be stitched into a
/// single video without occluding the non-video zones.
/// </summary>
public class BackgroundZoneDerivationService : BackgroundService, IBackgroundRenditionService
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };
    private static readonly string[] VideoExtensions = [".mp4", ".webm", ".mov", ".m4v", ".avi"];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPixeraAllocationService _allocationService;
    private readonly IWebHostEnvironment _env;
    private readonly FfmpegOptions _ffmpeg;
    private readonly ILogger<BackgroundZoneDerivationService> _logger;

    public BackgroundZoneDerivationService(
        IServiceScopeFactory scopeFactory,
        IPixeraAllocationService allocationService,
        IWebHostEnvironment env,
        IOptions<FfmpegOptions> ffmpegOptions,
        ILogger<BackgroundZoneDerivationService> logger)
    {
        _scopeFactory      = scopeFactory;
        _allocationService = allocationService;
        _env               = env;
        _ffmpeg            = ffmpegOptions.Value;
        _logger            = logger;
    }

    // ── Lookup (called from the allocation endpoint per request) ─────────────

    public (string Path, long Version)? TryGetRendition(
        int locationId, IReadOnlyList<BackgroundZone> zones,
        int stripW, int stripH, int outh, int outputIndex)
    {
        var set = BuildSetDescriptor(locationId, zones, stripW, stripH, outh);
        if (set == null) return null;

        var file = Path.Combine(DerivedFolder(locationId), OutputFileName(set.Hash, outputIndex));
        if (!File.Exists(file)) return null;

        var version = new DateTimeOffset(File.GetLastWriteTimeUtc(file)).ToUnixTimeSeconds();
        return ($"/MediaAssets/{locationId}/Derived/{OutputFileName(set.Hash, outputIndex)}", version);
    }

    public (string Path, long Version)? TryGetClusterBackgroundRendition(
        int locationId, IReadOnlyList<DisplayCluster> clusters,
        int stripW, int stripH, int outh, int outputIndex)
    {
        var set = BuildClusterSetDescriptor(clusters, stripW, stripH, outh);
        if (set == null) return null;

        var file = Path.Combine(DerivedFolder(locationId), OutputFileName(set.Hash, outputIndex));
        if (!File.Exists(file)) return null;

        var version = new DateTimeOffset(File.GetLastWriteTimeUtc(file)).ToUnixTimeSeconds();
        return ($"/MediaAssets/{locationId}/Derived/{OutputFileName(set.Hash, outputIndex)}", version);
    }

    public IReadOnlyList<RenditionSetStatus> GetStatuses(int locationId)
    {
        var folder = DerivedFolder(locationId);
        if (!Directory.Exists(folder)) return [];

        var statuses = new List<RenditionSetStatus>();
        foreach (var manifestPath in Directory.GetFiles(folder, "bg_*.json"))
        {
            try
            {
                var m = JsonSerializer.Deserialize<SetManifest>(File.ReadAllText(manifestPath));
                if (m == null) continue;
                int ready = Enumerable.Range(0, m.OutputCount)
                    .Count(n => File.Exists(Path.Combine(folder, OutputFileName(m.Hash, n))));
                statuses.Add(new RenditionSetStatus(
                    m.Hash, m.Status, m.Error, m.UpdatedUtc, m.Inputs, ready, m.OutputCount));
            }
            catch { }
        }
        return statuses.OrderByDescending(s => s.UpdatedUtc).ToList();
    }

    // ── Sweep loop ────────────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish startup before the first sweep.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(stoppingToken); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogError(ex, "Background rendition sweep failed"); }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        List<(int LocationId, VideoWallConfiguration Config)> walls = [];

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
            var candidates = await db.TblLocations.AsNoTracking()
                .Where(l => l.ExtendedData != null && l.ExtendedData.Contains("wrappedOutputsEnabled"))
                .Select(l => new { l.LocationId, l.ExtendedData })
                .ToListAsync(ct);

            foreach (var c in candidates)
            {
                try
                {
                    var cfg = JsonSerializer.Deserialize<VideoWallConfiguration>(c.ExtendedData!,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (cfg?.WrappedOutputsEnabled == true)
                        walls.Add((c.LocationId, cfg));
                }
                catch { }
            }
        }

        foreach (var (locationId, config) in walls)
        {
            if (ct.IsCancellationRequested) return;
            await DeriveWallAsync(locationId, config, ct);
        }
    }

    private async Task DeriveWallAsync(int locationId, VideoWallConfiguration config, CancellationToken ct)
    {
        int stripW = config.OutputWidth;
        int outh   = config.OutputHeight;
        int stripH = await GetBackgroundRowHeightAsync(locationId, config);
        if (stripW <= 0 || stripH <= 0 || outh < stripH) return;

        // Resolve the zone set now and at every schedule boundary of the wall's
        // background displays, so a day-parted set is derived before it activates.
        var candidateTimes = await GetCandidateTimesAsync(locationId, config);

        var seenHashes = new HashSet<string>();
        foreach (var t in candidateTimes)
        {
            if (ct.IsCancellationRequested) return;

            var alloc = await _allocationService.GetCurrentAllocationAsync(locationId, t);
            var zones = alloc.BackgroundZones ?? [];

            var zoneSet    = BuildSetDescriptor(locationId, zones, stripW, stripH, outh);
            var clusterSet = BuildClusterSetDescriptor(alloc.Clusters, stripW, stripH, outh);
            foreach (var set in new[] { zoneSet, clusterSet })
            {
                if (set == null || !seenHashes.Add(set.Hash)) continue;

                var folder = DerivedFolder(locationId);
                Directory.CreateDirectory(folder);

                bool complete = Enumerable.Range(0, set.OutputCount)
                    .All(n => File.Exists(Path.Combine(folder, OutputFileName(set.Hash, n))));
                if (complete) continue;

                await EncodeSetAsync(locationId, set, ct);
            }
        }
    }

    /// <summary>Strip height = the background row's display height from the layout.</summary>
    private async Task<int> GetBackgroundRowHeightAsync(int locationId, VideoWallConfiguration config)
    {
        var bgGroup = config.Layout?.Groups.FirstOrDefault(g =>
            !g.AllowAirlineAllocation &&
            string.Equals(g.Role, "background", StringComparison.OrdinalIgnoreCase));
        if (bgGroup == null || bgGroup.DisplayIds.Count == 0) return 0;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
        var display = await db.TblDisplays.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DisplayId == bgGroup.DisplayIds[0]);
        return (int)(display?.DisplayHeight ?? 0);
    }

    private async Task<List<DateTime>> GetCandidateTimesAsync(int locationId, VideoWallConfiguration config)
    {
        var times = new List<DateTime> { DateTime.Now };

        var bgDisplayIds = config.Layout?.Groups
            .Where(g => !g.AllowAirlineAllocation &&
                        string.Equals(g.Role, "background", StringComparison.OrdinalIgnoreCase))
            .SelectMany(g => g.DisplayIds)
            .Distinct().ToList() ?? [];
        if (bgDisplayIds.Count == 0) return times;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
        var events = await db.TblScheduleEvents.AsNoTracking()
            .Where(e => bgDisplayIds.Contains(e.DisplayId))
            .Select(e => new { e.StartTime, e.EndTime })
            .ToListAsync();

        var today = DateTime.Today;
        foreach (var e in events)
        {
            // One minute inside each boundary so the resolution lands within the window.
            if (e.StartTime.HasValue) times.Add(today.Add(e.StartTime.Value.ToTimeSpan()).AddMinutes(1));
            if (e.EndTime.HasValue)   times.Add(today.Add(e.EndTime.Value.ToTimeSpan()).AddMinutes(1));
        }
        return times;
    }

    // ── Set descriptor / hashing ──────────────────────────────────────────────

    private sealed record TileRef(string WebPath, string FullPath, long Mtime, double CanvasX, double Width);

    private sealed class SetDescriptor
    {
        public required string Hash;
        public required List<TileRef> Tiles;     // ordered by CanvasX
        public required int StripW;
        public required int StripH;
        public required int StripsPerOutput;
        public required int OutputCount;
        public required double RowWidth;
        /// <summary>
        /// Encode with VP9 alpha and transparent padding. Cluster-background sets
        /// require it so uncovered canvas keeps showing wall-level zones beneath;
        /// wall-zone sets stay opaque (cheaper decode).
        /// </summary>
        public required bool Alpha;
    }

    private sealed class SetManifest
    {
        public string Hash { get; set; } = "";
        public string Status { get; set; } = "pending";
        public string? Error { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public List<string> Inputs { get; set; } = [];
        public int OutputCount { get; set; }
        public string Geometry { get; set; } = "";
    }

    /// <summary>
    /// Expands zones into the ordered tile row and computes the cache key. Returns
    /// null when the row has no content or contains any non-video content (mixed
    /// rows are sliced client-side instead).
    /// </summary>
    private SetDescriptor? BuildSetDescriptor(
        int locationId, IReadOnlyList<BackgroundZone> zones, int stripW, int stripH, int outh)
    {
        var contented = zones.Where(z => !string.IsNullOrEmpty(z.ContentPath) && !z.Prewrapped).ToList();
        if (contented.Count == 0) return null;
        if (!contented.All(z => VideoExtensions.Contains(
                Path.GetExtension(z.ContentPath!).ToLowerInvariant())))
            return null;

        var tiles = new List<TileRef>();
        foreach (var z in contented.OrderBy(z => z.LeftPx))
        {
            var resolved = TryResolveTile(z.ContentPath!);
            if (resolved == null) return null;

            int loops = Math.Max(1, z.LoopCount);
            for (int t = 0; t < loops; t++)
                tiles.Add(new TileRef(z.ContentPath!, resolved.Value.FullPath, resolved.Value.Mtime,
                    z.LeftPx + t * z.WidthPx, z.WidthPx));
        }
        if (tiles.Count == 0) return null;

        return BuildSetFromTiles(tiles, stripW, stripH, outh, alpha: false);
    }

    /// <summary>
    /// Expands the airline cluster background segment sequences into an ordered tile
    /// row (each segment at native width, wrapping back to the first until its cluster
    /// is covered — the same walk the layer shell performs). Returns null when no
    /// cluster carries segments, or any segment is non-video, missing, or of unknown
    /// width — those sets render client-side instead.
    /// </summary>
    private SetDescriptor? BuildClusterSetDescriptor(
        IReadOnlyList<DisplayCluster> clusters, int stripW, int stripH, int outh)
    {
        var withSegments = clusters
            .Where(c => c.BackgroundSegments is { Count: > 0 })
            .OrderBy(c => c.LeftPx)
            .ToList();
        if (withSegments.Count == 0) return null;

        // Layer opacity cannot ride a baked rendition (the substitution would render
        // fully opaque); translucent rows render their segments client-side instead.
        if (withSegments.Any(c => c.BackgroundOpacityPct < 100)) return null;

        var tiles = new List<TileRef>();
        foreach (var c in withSegments)
        {
            double x = c.LeftPx, end = c.LeftPx + c.WidthPx;
            int idx = 0, guard = 128;
            while (x < end && guard-- > 0)
            {
                var seg = c.BackgroundSegments![idx % c.BackgroundSegments.Count];
                if (seg.WidthPx <= 0) return null;
                if (!VideoExtensions.Contains(Path.GetExtension(seg.Path).ToLowerInvariant()))
                    return null;
                var resolved = TryResolveTile(seg.Path);
                if (resolved == null) return null;

                double w = Math.Min(seg.WidthPx, end - x);
                tiles.Add(new TileRef(seg.Path, resolved.Value.FullPath, resolved.Value.Mtime, x, w));
                x += seg.WidthPx;
                idx++;
            }
        }
        return tiles.Count > 0 ? BuildSetFromTiles(tiles, stripW, stripH, outh, alpha: true) : null;
    }

    private (string FullPath, long Mtime)? TryResolveTile(string webPath)
    {
        var relative = webPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(_env.WebRootPath, relative));
        if (!full.StartsWith(Path.GetFullPath(_env.WebRootPath), StringComparison.OrdinalIgnoreCase))
            return null;
        if (!File.Exists(full)) return null;
        return (full, new DateTimeOffset(File.GetLastWriteTimeUtc(full)).ToUnixTimeSeconds());
    }

    private static SetDescriptor BuildSetFromTiles(
        List<TileRef> tiles, int stripW, int stripH, int outh, bool alpha)
    {
        double rowWidth = tiles.Max(t => t.CanvasX + t.Width);
        int strips  = Math.Max(1, outh / stripH);
        double span = (double)strips * stripW;
        int outputs = (int)Math.Ceiling(rowWidth / span);

        // The alpha marker is appended only for alpha sets so pre-existing opaque
        // zone renditions keep their hashes (and cached files) across this change.
        var keyPayload = string.Join("|", tiles.Select(t =>
            $"{t.WebPath}:{t.Mtime}:{Math.Round(t.CanvasX)}:{Math.Round(t.Width)}")) +
            $"|g:{stripW}x{stripH}x{outh}" + (alpha ? "|a:1" : "");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keyPayload)))[..16].ToLowerInvariant();

        return new SetDescriptor
        {
            Hash = hash, Tiles = tiles, StripW = stripW, StripH = stripH,
            StripsPerOutput = strips, OutputCount = outputs, RowWidth = rowWidth,
            Alpha = alpha
        };
    }

    // ── Encoding ──────────────────────────────────────────────────────────────

    private async Task EncodeSetAsync(int locationId, SetDescriptor set, CancellationToken ct)
    {
        var folder = DerivedFolder(locationId);
        var manifestPath = Path.Combine(folder, $"bg_{set.Hash}.json");
        var manifest = new SetManifest
        {
            Hash = set.Hash,
            Status = "encoding",
            UpdatedUtc = DateTime.UtcNow,
            Inputs = set.Tiles.Select(t => t.WebPath).Distinct().ToList(),
            OutputCount = set.OutputCount,
            Geometry = $"{set.StripW}x{set.StripH} outh strips={set.StripsPerOutput}"
        };
        WriteManifest(manifestPath, manifest);

        _logger.LogInformation(
            "Deriving wrapped background set {Hash} for location {LocationId}: {Outputs} output(s), row {RowWidth}px",
            set.Hash, locationId, set.OutputCount, set.RowWidth);

        try
        {
            // Remove stray temp files from an interrupted previous run of this set.
            foreach (var stray in Directory.GetFiles(folder, $"bg_{set.Hash}_out*.webm.tmp"))
                try { File.Delete(stray); } catch { }

            for (int n = 0; n < set.OutputCount; n++)
            {
                ct.ThrowIfCancellationRequested();
                var outPath = Path.Combine(folder, OutputFileName(set.Hash, n));
                if (File.Exists(outPath)) continue;
                await EncodeOutputAsync(set, n, outPath, ct);
            }
            manifest.Status = "done";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            manifest.Status = "failed";
            manifest.Error = ex.Message;
            _logger.LogError(ex, "Rendition encode failed for set {Hash}, location {LocationId}", set.Hash, locationId);
        }
        finally
        {
            manifest.UpdatedUtc = DateTime.UtcNow;
            WriteManifest(manifestPath, manifest);
        }
    }

    private async Task EncodeOutputAsync(SetDescriptor set, int outputIndex, string outPath, CancellationToken ct)
    {
        var inputs = set.Tiles.Select(t => t.FullPath).Distinct().ToList();
        var inputIndex = inputs.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);

        double span = (double)set.StripsPerOutput * set.StripW;
        double outputBase = outputIndex * span;

        var filters = new StringBuilder();
        var stripLabels = new List<string>();

        for (int s = 0; s < set.StripsPerOutput; s++)
        {
            double a = outputBase + s * (double)set.StripW;
            double b = Math.Min(a + set.StripW, set.RowWidth);
            if (b <= a) break;

            // Segments: tile intersections with [a, b), left to right. Loop tiles are
            // extra TileRef entries on the same source file, so "fill from the start
            // of the video and continue" falls out of the same crop math.
            var segLabels = new List<string>();
            int k = 0;
            double stripFill = 0;
            foreach (var tile in set.Tiles)
            {
                double ta = Math.Max(a, tile.CanvasX);
                double tb = Math.Min(b, tile.CanvasX + tile.Width);
                if (tb <= ta) continue;
                int srcX = (int)Math.Round(ta - tile.CanvasX);
                int segW = (int)Math.Round(tb - ta);
                if (segW <= 0) continue;
                var label = $"o{outputIndex}s{s}p{k}";
                filters.Append($"[{inputIndex[tile.FullPath]}:v]crop={segW}:{set.StripH}:{srcX}:0[{label}];");
                segLabels.Add(label);
                stripFill += segW;
                k++;
            }
            if (segLabels.Count == 0) break;

            string stripLabel = $"o{outputIndex}s{s}";
            if (segLabels.Count == 1)
            {
                stripLabel = segLabels[0];
            }
            else
            {
                filters.Append(string.Concat(segLabels.Select(l => $"[{l}]")));
                filters.Append($"hstack=inputs={segLabels.Count}:shortest=1[{stripLabel}];");
            }

            // Uniform strip width is required for vstack; pad the trailing strip.
            // Alpha sets pad transparent so uncovered canvas shows the layers beneath.
            if (stripFill < set.StripW)
            {
                var padded = $"o{outputIndex}s{s}f";
                var padColor = set.Alpha ? "black@0.0" : "black";
                var padPre = set.Alpha ? $"[{stripLabel}]format=yuva420p[{stripLabel}a];[{stripLabel}a]" : $"[{stripLabel}]";
                filters.Append($"{padPre}pad={set.StripW}:{set.StripH}:0:0:{padColor}[{padded}];");
                stripLabel = padded;
            }
            stripLabels.Add(stripLabel);
        }

        if (stripLabels.Count == 0) return;

        string final;
        if (stripLabels.Count == 1)
        {
            final = stripLabels[0];
        }
        else
        {
            filters.Append(string.Concat(stripLabels.Select(l => $"[{l}]")));
            final = $"o{outputIndex}v";
            filters.Append($"vstack=inputs={stripLabels.Count}:shortest=1[{final}];");
        }

        var filterGraph = filters.ToString().TrimEnd(';');
        var ffmpegExe = _ffmpeg.ResolveBinary("ffmpeg.exe", _env.ContentRootPath);

        // Encode to a temp name and move into place on success. Writing the final
        // name directly would leave a truncated-but-playable file if Director
        // restarts mid-encode — every skip/lookup path treats an existing final
        // file as complete, so the wall would loop a video that freezes at the
        // truncation point. The move makes "final file exists" mean "complete".
        var tmpPath = outPath + ".tmp";
        var args = new StringBuilder("-y -hide_banner -loglevel error ");
        foreach (var input in inputs) args.Append($"-i \"{input}\" ");
        // Realtime VP9: ~10x faster than the default deadline at this frame size;
        // signage backgrounds tolerate the modest quality trade. VP9 because the
        // target players' Chromium builds ship without proprietary codecs (no H.264).
        // -f webm is explicit because the .tmp extension defeats format inference.
        args.Append($"-filter_complex \"{filterGraph}\" -map \"[{final}]\" ");
        args.Append("-c:v libvpx-vp9 -deadline realtime -cpu-used 8 -row-mt 1 -crf 32 -b:v 8M -an ");
        if (set.Alpha)
            // alpha_mode=1 is required for Chromium to decode the VP9 alpha plane.
            args.Append("-pix_fmt yuva420p -auto-alt-ref 0 -metadata:s:v:0 alpha_mode=\"1\" ");
        args.Append($"-f webm \"{tmpPath}\"");

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegExe,
            Arguments = args.ToString(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        _logger.LogInformation("Encoding rendition output {Index} of set {Hash}", outputIndex, set.Hash);
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"ffmpeg failed to start ({ffmpegExe})");
        try
        {
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0 || !File.Exists(tmpPath))
            {
                var tail = stderr.Length > 600 ? stderr[^600..] : stderr;
                throw new InvalidOperationException($"ffmpeg exit {process.ExitCode}: {tail}");
            }
            File.Move(tmpPath, outPath, overwrite: true);
        }
        catch
        {
            // Shutdown or failure: stop ffmpeg so it doesn't keep writing, and drop
            // the partial temp file. The next sweep re-encodes this output.
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            throw;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string DerivedFolder(int locationId) =>
        // Pixera pipeline reads the global web root: the Pixera-owning tenant must keep
        // FilePaths_MediaRoot='wwwroot' when tenant media isolation is enabled.
        Path.Combine(_env.WebRootPath, "MediaAssets", locationId.ToString(), "Derived");

    private static string OutputFileName(string hash, int outputIndex) =>
        $"bg_{hash}_out{outputIndex}.webm";

    private static void WriteManifest(string path, SetManifest manifest)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(manifest, _jsonOpts)); }
        catch { }
    }
}
