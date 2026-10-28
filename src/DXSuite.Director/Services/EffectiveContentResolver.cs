using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Director.Models.BrightSign;
using DXSuite.Shared.Multitenancy;
using static DXSuite.Director.Models.Types;

namespace DXSuite.Director.Services;

public class PreviewRotationItem
{
    public string Name { get; set; } = "";
    public string? ThumbnailWebPath { get; set; }
    public string? WebPath { get; set; }
    public bool IsVideo { get; set; }
    public bool IsWeb { get; set; }
    public int DurationSeconds { get; set; }
    public int Weight { get; set; } = 1;
    public int? PlayOrder { get; set; }
}

public class PreviewRotationResult
{
    public List<PreviewRotationItem> Items { get; set; } = new();
    public List<PreviewRotationItem> Loop { get; set; } = new();
    public int LoopSeconds { get; set; }
    public bool ShuffleEnabled { get; set; }
    public string? ActiveEventLabel { get; set; }

    /// <summary>
    /// The location's rules when its "Target content by metadata" switch is on, otherwise null. A playlist
    /// preview with rules explains per player; without, every player receives the same rotation.
    /// </summary>
    public LocationMetadataRules? TargetingRules { get; set; }

    /// <summary>The key whose Nothing rule excludes the previewed player before matching, or null.</summary>
    public string? GateKey { get; set; }

    /// <summary>
    /// Every file scheduled at the previewed instant in play order, included or not, each with the gates that
    /// stopped it. <see cref="Items"/> is the included subset.
    /// </summary>
    public List<PreviewDiagnosticRow> Rows { get; set; } = new();

    /// <summary>Files that reach the player but are outside their scheduled window at the previewed instant.</summary>
    public int NotScheduledCount { get; set; }

    /// <summary>Whether the location has any playable file at all, before any gate.</summary>
    public bool HasContent { get; set; }

    /// <summary>Whether any file passes targeting for the player, ignoring time and approval.</summary>
    public bool AnyReachesPlayer { get; set; }

    /// <summary>
    /// The player values this result was evaluated against. Reasons are worded from these rather than from the
    /// subject on screen, which may already have changed while a newer evaluation runs.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> Player { get; set; } =
        new Dictionary<string, IReadOnlySet<string>>(MetadataEvaluator.KeyComparer);
}

/// <summary>One scheduled file in a playlist preview and the independent gates it passed or failed.</summary>
public class PreviewDiagnosticRow
{
    public PreviewRotationItem Item { get; set; } = new();
    public bool Included => !NotTargeted && HeldApprovalStatus == null;
    public bool NotTargeted { get; set; }

    /// <summary>Pending or Declined when approval holds the file back from this preview, otherwise null.</summary>
    public int? HeldApprovalStatus { get; set; }

    /// <summary>The asset's tags, or null for a file with no asset record.</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>>? Tags { get; set; }

    /// <summary>The evaluator's per-key verdicts. Empty when the player was gated or targeting is off.</summary>
    public IReadOnlyList<MetadataKeyVerdict> Verdicts { get; set; } = [];
}

/// <summary>
/// Evaluates the content a location or display serves at an arbitrary instant, for an
/// arbitrary device identity, through the same rules the live player endpoint uses.
/// This is the engine behind the display-emulating preview; it must never read or write
/// the manifest cache, so simulated instants cannot pollute what players receive. It also
/// applies the windows players evaluate client-side (start/end date and time), because at
/// a fixed instant the server-side and client-side filters collapse into one question.
/// </summary>
public interface IEffectiveContentResolver
{
    /// <param name="playerMetadata">The previewed player's metadata, real or composed; empty previews a player with none set.</param>
    Task<PreviewRotationResult> ResolvePlaylistAsync(int locationId, IReadOnlyDictionary<string, string>? playerMetadata,
        bool treatPendingAsApproved, DateTime asOf);

    Task<PreviewRotationResult?> ResolveDisplayAsync(int displayId, DateTime asOf);
}

public class EffectiveContentResolver : IEffectiveContentResolver
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".avi", ".mkv", ".m4v", ".wmv"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg"
    };

    private readonly IDirectorDBContext _db;
    private readonly IMediaFolderCacheService _mediaFolderCache;
    private readonly IMetadataSubjectResolver _metadataSubjects;
    private readonly ILocationMetadataRuleService _locationMetadataRules;
    private readonly ITenantMediaPathResolver _mediaPaths;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<EffectiveContentResolver> _logger;

    public EffectiveContentResolver(
        IDirectorDBContext db,
        IMediaFolderCacheService mediaFolderCache,
        IMetadataSubjectResolver metadataSubjects,
        ILocationMetadataRuleService locationMetadataRules,
        ITenantMediaPathResolver mediaPaths,
        ITenantContext tenantContext,
        ILogger<EffectiveContentResolver> logger)
    {
        _db = db;
        _mediaFolderCache = mediaFolderCache;
        _metadataSubjects = metadataSubjects;
        _locationMetadataRules = locationMetadataRules;
        _mediaPaths = mediaPaths;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<PreviewRotationResult> ResolvePlaylistAsync(int locationId, IReadOnlyDictionary<string, string>? playerMetadata,
        bool treatPendingAsApproved, DateTime asOf)
    {
        var result = new PreviewRotationResult();

        var location = await _db.TblLocations.AsNoTracking().FirstOrDefaultAsync(l => l.LocationId == locationId);
        if (location == null) return result;
        result.ShuffleEnabled = location.EnableShuffle;

        var files = _mediaFolderCache.GetMediaFiles(_tenantContext.TenantId, locationId);
        var mediaFolder = Path.Combine(_mediaPaths.GetRoot(), "MediaAssets", locationId.ToString());
        var metadataMap = new MetadataParser().ParseAllMetadata(mediaFolder);

        // The same two gates the manifest path applies, approval then targeting, with the preview's pending lens
        // on approval only. Every file is judged on both, so the diagnostic can name each gate that stopped it.
        var assetRecords = await _metadataSubjects.GetLocationAssetRecordsAsync(locationId);
        var published = PlaylistManifestRules
            .SelectPublished(files, locationId, assetRecords, location.EnableContentApproval, treatPendingAsApproved)
            .Select(f => f.File)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var targeting = await _locationMetadataRules.GetTargetingAsync(locationId);
        var player = MetadataSubjectResolver.DeviceSubject(playerMetadata);
        result.Player = player;
        if (targeting.Enabled)
        {
            result.TargetingRules = targeting.Rules;
            result.GateKey = PreviewDiagnostics.GateKey(targeting.Rules, player);
        }

        var fileRecords = files
            .Select(file => (File: file, Record: assetRecords.GetValueOrDefault(
                PlaylistManifestRules.BuildAssetWebPath(locationId, Path.GetFileName(file)))))
            .ToList();
        var assetTags = await _metadataSubjects.GetAssetValuesAsync(
            fileRecords.Where(f => f.Record != null).Select(f => f.Record!.AssetId).Distinct().ToList());

        var candidates = new List<(PlayerContentItem Item, string File, PreviewDiagnosticRow Row)>();

        foreach (var (file, record) in fileRecords)
        {
            var fileName = Path.GetFileName(file);
            var extension = Path.GetExtension(file).ToLowerInvariant();

            var tags = record == null ? null : assetTags.GetValueOrDefault(record.AssetId)
                ?? new Dictionary<string, IReadOnlySet<string>>(MetadataEvaluator.KeyComparer);
            var evaluation = targeting.Enabled
                ? MetadataEvaluator.Evaluate(targeting.Rules, tags, player, BlankSubjectPolicy.ApplyKeyRules)
                : null;
            var row = new PreviewDiagnosticRow
            {
                NotTargeted = evaluation is { Included: false },
                HeldApprovalStatus = published.Contains(file) ? null : record?.ApprovalStatus,
                Tags = tags,
                Verdicts = evaluation?.Verdicts ?? []
            };
            if (extension == ".json")
            {
                try
                {
                    var webContent = JsonSerializer.Deserialize<WebContentDefinition>(await File.ReadAllTextAsync(file));
                    if (webContent == null) continue;
                    candidates.Add((new PlayerContentItem
                    {
                        FileName = fileName,
                        Url = webContent.Url,
                        Duration = webContent.Duration ?? 7,
                        StartDate = webContent.StartDate,
                        EndDate = webContent.EndDate,
                        StartTime = webContent.StartTime,
                        EndTime = webContent.EndTime,
                        IsWeb = true,
                        PlayOrder = metadataMap.TryGetValue(fileName, out var webMeta) ? webMeta.PlayOrder : null
                    }, file, row));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Preview skipped unparsable web content {FileName}", fileName);
                }
                continue;
            }

            candidates.Add((metadataMap.TryGetValue(fileName, out var meta) ? meta : new PlayerContentItem { FileName = fileName }, file, row));
        }

        result.HasContent = candidates.Count > 0;
        result.AnyReachesPlayer = candidates.Any(c => !c.Row.NotTargeted);

        // The player evaluates these windows client-side; at a fixed instant they are
        // just more filters
        var active = candidates
            .Where(c => IsActiveAt(c.Item, asOf))
            .OrderBy(c => c.Item.PlayOrder ?? int.MaxValue)
            .ToList();
        result.NotScheduledCount = candidates.Count(c => !c.Row.NotTargeted) - active.Count(c => !c.Row.NotTargeted);

        var imageDuration = ExtendedDataHelper.Parse<LocationExtendedData>(location.ExtendedData)?.ImageDurationSeconds;

        foreach (var (item, file, row) in active)
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            var isVideo = VideoExtensions.Contains(extension);
            var webPath = $"/MediaAssets/{locationId}/{Path.GetFileName(file)}";
            var sidecarDuration = ReadSidecarDurationSeconds(file);

            var duration = item.IsWeb ? item.Duration ?? 7
                : isVideo ? (sidecarDuration ?? 7)
                : ImageExtensions.Contains(extension) ? (imageDuration ?? 7)
                : (sidecarDuration ?? 7);

            row.Item = new PreviewRotationItem
            {
                Name = item.FileName ?? Path.GetFileName(file),
                WebPath = item.IsWeb ? item.Url : webPath,
                ThumbnailWebPath = item.IsWeb ? null
                    : isVideo ? $"/MediaAssets/{locationId}/Thumbnails/{Path.GetFileNameWithoutExtension(file)}.png"
                    : webPath,
                IsVideo = isVideo,
                IsWeb = item.IsWeb,
                DurationSeconds = Math.Max(1, duration),
                // Weights only apply to shuffled playlists, mirroring the manifest
                Weight = result.ShuffleEnabled ? item.Weight ?? 1 : 1,
                PlayOrder = item.PlayOrder
            };
            result.Rows.Add(row);
            if (row.Included)
                result.Items.Add(row.Item);
        }

        FinishResult(result);
        return result;
    }

    public async Task<PreviewRotationResult?> ResolveDisplayAsync(int displayId, DateTime asOf)
    {
        var display = await _db.TblDisplays.AsNoTracking()
            .Include(d => d.TblScheduleEvents)
            .FirstOrDefaultAsync(d => d.DisplayId == displayId);
        if (display == null) return null;

        var result = new PreviewRotationResult();
        var displayItem = new DisplayItem(display);

        // Preview only, and the host clock is deliberate: an author previewing a rotation is
        // asking what this display shows at a moment they chose, not what some device sees.
        var activeEvent = displayItem.GetActiveEvent(display.TblScheduleEvents, asOf);
        result.ActiveEventLabel = activeEvent == null ? "No content scheduled"
            : activeEvent.DefaultContent ? "Default content"
            : DescribeEvent(activeEvent);

        var content = await displayItem.GetContentAssignmentAsync(display, _db, string.Empty, asOf);
        foreach (var item in content.Content)
        {
            var isVideo = item.Asset?.AssetTypeId == (int)AssetType.Video;
            var isWeb = item.Asset?.AssetTypeId == (int)AssetType.Web
                || item.Asset?.AssetTypeId == (int)AssetType.Stream;
            var assetPath = item.Asset?.AssetPath;

            var duration = item.Duration > 0 ? item.Duration
                : isVideo && item.Asset!.Duration > 0 ? item.Asset.Duration : 7;

            result.Items.Add(new PreviewRotationItem
            {
                Name = item.Asset?.AssetName ?? (assetPath != null ? Path.GetFileName(assetPath) : $"Asset {item.AssetId}"),
                WebPath = assetPath,
                ThumbnailWebPath = isWeb || assetPath == null ? null
                    : isVideo
                        ? $"{Path.GetDirectoryName(assetPath)!.Replace('\\', '/')}/Thumbnails/{Path.GetFileNameWithoutExtension(assetPath)}.png"
                        : assetPath,
                IsVideo = isVideo,
                IsWeb = isWeb,
                DurationSeconds = Math.Max(1, (int)Math.Round(duration)),
                Weight = Math.Clamp(item.Weight, 1, 10)
            });
        }

        FinishResult(result);
        return result;
    }

    // ────────────────────────────────────────────────────────────────────────

    private static void FinishResult(PreviewRotationResult result)
    {
        result.Loop = WeightedLoopExpander.Expand(result.Items, i => i.Weight);
        result.LoopSeconds = result.Loop.Sum(i => i.DurationSeconds);
    }

    private static bool IsActiveAt(PlayerContentItem item, DateTime asOf)
    {
        if (item.StartDate.HasValue && asOf.Date < item.StartDate.Value.Date) return false;
        if (item.EndDate.HasValue && asOf.Date > item.EndDate.Value.Date) return false;

        if (item.StartTime.HasValue || item.EndTime.HasValue)
        {
            var t = asOf.TimeOfDay;
            if (item.StartTime.HasValue && t < item.StartTime.Value) return false;
            if (item.EndTime.HasValue && t > item.EndTime.Value) return false;
        }

        if (!string.IsNullOrWhiteSpace(item.Days))
        {
            var dayAbbrev = asOf.DayOfWeek switch
            {
                DayOfWeek.Monday => "Mo",
                DayOfWeek.Tuesday => "Tu",
                DayOfWeek.Wednesday => "We",
                DayOfWeek.Thursday => "Th",
                DayOfWeek.Friday => "Fr",
                DayOfWeek.Saturday => "Sa",
                DayOfWeek.Sunday => "Su",
                _ => ""
            };
            if (!item.Days.Split(',', StringSplitOptions.TrimEntries)
                    .Contains(dayAbbrev, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static int? ReadSidecarDurationSeconds(string file)
    {
        try
        {
            var sidecar = file + ".metadata";
            if (!File.Exists(sidecar)) return null;
            var doc = new System.Xml.XmlDocument();
            doc.Load(sidecar);
            var raw = doc.SelectSingleNode("//field[@name='Duration']")?.Attributes?["value"]?.Value;
            return int.TryParse(raw, out var seconds) && seconds > 0 ? seconds : null;
        }
        catch
        {
            return null;
        }
    }

    private static string DescribeEvent(TblScheduleEvent evt)
    {
        var window = evt.StartTime.HasValue || evt.EndTime.HasValue
            ? $"{Format(evt.StartTime)} to {Format(evt.EndTime)}"
            : "All day";
        return $"Scheduled event ({window})";

        static string Format(TimeOnly? t) => t?.ToString("h:mm tt") ?? "start of day";
    }

}
