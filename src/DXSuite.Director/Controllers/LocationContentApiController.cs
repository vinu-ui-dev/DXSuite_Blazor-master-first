using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Director.Services;
using DXSuite.Director.Services.LocationBundles;
using DXSuite.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Controllers;

/// <summary>
/// Data API consumed by location bundles through the dx-location.js SDK. Every action
/// re-resolves the caller's location access from the cookie principal — access
/// enforcement here mirrors what the razor location components enforce in the circuit
/// (Playlist.razor visibility semantics for assets, assigned-location gate everywhere).
/// </summary>
[Route("api/location-content/{locationId:int}")]
[ApiController]
[Authorize]
public class LocationContentApiController : ControllerBase
{
    private const int StorageValueMaxBytes = 256 * 1024;
    private const int StorageMaxKeysPerLocation = 200;

    private readonly ILocationAccessResolver _accessResolver;
    private readonly ILocationBundleService _bundleService;
    private readonly IAssetIngestService _assetIngestService;
    private readonly IUploadIdentityService _uploadIdentity;
    private readonly IContentScopeService _contentScope;
    private readonly DXSuite.Shared.Services.Identity.ILocationAccessService _locationAccess;
    private readonly IDirectorDBContext _db;
    private readonly IManifestCacheService _manifestCache;
    private readonly DXSuite.Shared.Multitenancy.ITenantContext _tenantContext;
    private readonly IOptions<FeatureFlags> _featureFlags;
    private readonly ILogger<LocationContentApiController> _logger;

    public LocationContentApiController(
        ILocationAccessResolver accessResolver,
        ILocationBundleService bundleService,
        IAssetIngestService assetIngestService,
        IUploadIdentityService uploadIdentity,
        IContentScopeService contentScope,
        DXSuite.Shared.Services.Identity.ILocationAccessService locationAccess,
        IDirectorDBContext db,
        IManifestCacheService manifestCache,
        DXSuite.Shared.Multitenancy.ITenantContext tenantContext,
        IOptions<FeatureFlags> featureFlags,
        ILogger<LocationContentApiController> logger)
    {
        _accessResolver = accessResolver;
        _bundleService = bundleService;
        _assetIngestService = assetIngestService;
        _uploadIdentity = uploadIdentity;
        _contentScope = contentScope;
        _locationAccess = locationAccess;
        _db = db;
        _manifestCache = manifestCache;
        _tenantContext = tenantContext;
        _featureFlags = featureFlags;
        _logger = logger;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Reads
    // ────────────────────────────────────────────────────────────────────────

    [HttpGet("context")]
    public async Task<IActionResult> GetContext(int locationId)
    {
        var (caller, location, error) = await AuthorizeAsync(locationId);
        if (error != null) return error;

        var uploadCandidates = await _uploadIdentity.GetCandidatesAsync(caller!.User.UserId, locationId, HttpContext.RequestAborted);

        var assignment = ExtendedDataHelper.Parse<LocationExtendedData>(location!.ExtendedData)?.Bundle;
        object? bundleInfo = null;
        if (assignment != null && !string.IsNullOrEmpty(assignment.BundleKey))
        {
            var resolved = await _bundleService.ResolveVersionAsync(assignment.BundleKey, assignment.PinnedVersion, HttpContext.RequestAborted);
            bundleInfo = new
            {
                assignment.BundleKey,
                Version = resolved?.Version,
                assignment.PinnedVersion,
                Capabilities = resolved?.Manifest?.Capabilities ?? new List<string>()
            };
        }

        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            Location = new
            {
                location.LocationId,
                location.LocationName,
                LocationTypeName = location.LocationType?.LocationTypeName,
                LinkedToDisplay = location.LocationType?.LinkedToDisplay ?? false,
                location.EnableContentApproval
            },
            Access = new
            {
                Level = caller!.EffectiveAccessLevel,
                caller.Access?.ViewScope,
                Upload = DescribeUploadCandidates(uploadCandidates)
            },
            User = new
            {
                caller.User.UserId,
                UserPrincipalName = caller.User.UserPrincipalName,
                caller.User.GivenName,
                caller.User.Surname,
                Role = caller.Role.ToString()
            },
            Bundle = bundleInfo,
            Settings = assignment?.Settings
        });
    }

    [HttpGet("displays")]
    public async Task<IActionResult> GetDisplays(int locationId)
    {
        var (_, _, error) = await AuthorizeAsync(locationId);
        if (error != null) return error;

        var displays = await _db.TblDisplays
            .AsNoTracking()
            .Where(d => d.LocationId == locationId)
            .OrderBy(d => d.DisplayName)
            .Select(d => new
            {
                d.DisplayId,
                d.DisplayName,
                d.DisplayWidth,
                d.DisplayHeight,
                d.DisplayTypeId,
                d.ConstraintId,
                d.DisplayMode,
                d.OverrideAssetId
            })
            .ToListAsync(HttpContext.RequestAborted);

        var constraints = await _db.TblConstraints
            .AsNoTracking()
            .Where(c => c.OwningLocationId == locationId || c.IsGlobal)
            .Select(c => new
            {
                c.ConstraintId,
                c.ConstraintName,
                c.ResolutionWidth,
                c.ResolutionHeight,
                c.ResolutionMode,
                c.IsGlobal
            })
            .ToListAsync(HttpContext.RequestAborted);

        return Ok(new { Displays = displays, Constraints = constraints });
    }

    [HttpGet("assets")]
    public async Task<IActionResult> GetAssets(int locationId)
    {
        var (caller, location, error) = await AuthorizeAsync(locationId);
        if (error != null) return error;

        // Mirror SaveSchedule's allowlist: it accepts assets from this location or from the
        // shared asset library a display points at, so listing only the location's own assets
        // let a bundle schedule content it could never see. Read and write agree now.
        var libraryIds = await _db.TblDisplays
            .AsNoTracking()
            .Where(d => d.LocationId == locationId && d.AssetLibraryLocationId != null)
            .Select(d => d.AssetLibraryLocationId!.Value)
            .Distinct()
            .ToListAsync(HttpContext.RequestAborted);

        var sourceLocationIds = libraryIds.Append(locationId).Distinct().ToList();

        var assets = await _db.TblAssets
            .AsNoTracking()
            .Where(a => sourceLocationIds.Contains(a.LocationId) && !a.IsDeleted)
            .OrderBy(a => a.SortOrder)
            .ToListAsync(HttpContext.RequestAborted);

        var assetIds = assets.Select(a => a.AssetId).ToList();

        var metadata = await _db.TblAssetContentMetadata
            .AsNoTracking()
            .Where(m => assetIds.Contains(m.AssetId))
            .ToListAsync(HttpContext.RequestAborted);
        var metadataByAsset = metadata.ToLookup(m => m.AssetId);

        var approvals = await _db.TblAssetApprovals
            .AsNoTracking()
            .Where(ap => assetIds.Contains(ap.AssetId))
            .ToListAsync(HttpContext.RequestAborted);
        var approvalByAsset = approvals
            .GroupBy(ap => ap.AssetId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(ap => ap.SubmittedDate).First());

        var scopes = await GetListingScopesAsync(caller!, locationId, libraryIds);
        var memberGroupIds = await GetMemberGroupIdsAsync(caller!);

        var result = new List<object>();
        foreach (var asset in assets)
        {
            var assetMetadata = metadataByAsset[asset.AssetId].ToList();

            // Approval is not applied here: scope decides what is listed, and a scoped manager must see
            // Pending content in her scope. ApprovalStatus rides along for the bundle to badge.
            if (!scopes.TryGetValue(asset.LocationId, out var scope) || scope == null
                || !scope.IsVisible(MetadataSubjectResolver.GroupValues(assetMetadata.Select(m => (m.MetadataKey, m.MetadataValue)))))
                continue;

            approvalByAsset.TryGetValue(asset.AssetId, out var approval);

            result.Add(new
            {
                asset.AssetId,
                asset.AssetName,
                asset.AssetPath,
                asset.AssetTypeId,
                asset.AssetWidth,
                asset.AssetHeight,
                asset.ConstraintId,
                asset.Duration,
                asset.SortOrder,
                asset.ContentSourceUrl,
                Metadata = assetMetadata.Select(m => new { m.MetadataKey, m.MetadataValue }),
                ApprovalStatus = approval?.ApprovalStatus,
                CanEdit = CanMutateAsset(caller, asset, memberGroupIds),
                CanDelete = CanMutateAsset(caller, asset, memberGroupIds)
            });
        }

        return Ok(result);
    }

    [HttpGet("schedule/{displayId:int}")]
    public async Task<IActionResult> GetSchedule(int locationId, int displayId)
    {
        var (_, _, error) = await AuthorizeAsync(locationId);
        if (error != null) return error;

        if (!await DisplayBelongsToLocationAsync(displayId, locationId))
            return NotFound();

        var events = await _db.TblScheduleEvents
            .AsNoTracking()
            .Where(e => e.DisplayId == displayId)
            .OrderBy(e => e.DefaultContent ? 0 : 1).ThenBy(e => e.StartTime)
            .Select(e => new
            {
                e.ScheduleEventId,
                e.DisplayId,
                e.ContentAssignment,
                e.DefaultContent,
                e.StartTime,
                e.EndTime,
                e.StartDate,
                e.EndDate,
                e.Sunday,
                e.Monday,
                e.Tuesday,
                e.Wednesday,
                e.Thursday,
                e.Friday,
                e.Saturday
            })
            .ToListAsync(HttpContext.RequestAborted);

        return Ok(events);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Writes
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Full-sync save: upserts the supplied events and deletes the display's events
    /// that are absent from the payload.
    /// </summary>
    [HttpPut("schedule/{displayId:int}")]
    public async Task<IActionResult> SaveSchedule(int locationId, int displayId, [FromBody] List<ScheduleEventDto> events)
    {
        var (caller, _, error) = await AuthorizeWriteAsync(locationId);
        if (error != null) return error;

        var display = await _db.TblDisplays
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DisplayId == displayId && d.LocationId == locationId, HttpContext.RequestAborted);
        if (display == null)
            return NotFound();

        events ??= new List<ScheduleEventDto>();

        // Every referenced asset must belong to this location (or the display's shared
        // asset library) — a bundle must not be able to schedule another location's content.
        var allowedAssets = await _db.TblAssets
            .AsNoTracking()
            .Where(a => !a.IsDeleted &&
                        (a.LocationId == locationId ||
                         (display.AssetLibraryLocationId != null && a.LocationId == display.AssetLibraryLocationId)))
            .Select(a => new { a.AssetId, a.LocationId })
            .ToListAsync(HttpContext.RequestAborted);
        var allowedAssetIds = allowedAssets.Select(a => a.AssetId).ToHashSet();

        foreach (var dto in events)
        {
            if (string.IsNullOrWhiteSpace(dto.ContentAssignment))
                return BadRequest(new { error = "Every event requires a contentAssignment." });

            var referencedIds = ReadAssetIds(dto.ContentAssignment);
            if (referencedIds == null)
                return BadRequest(new { error = "contentAssignment is not a valid content assignment." });

            var foreign = referencedIds.Where(id => !allowedAssetIds.Contains(id)).ToList();
            if (foreign.Count > 0)
                return BadRequest(new { error = $"contentAssignment references assets outside this location: {string.Join(", ", foreign)}" });
        }

        if (events.Count(e => e.DefaultContent) > 1)
            return BadRequest(new { error = "Only one default-content event is allowed per display." });

        var existing = await _db.TblScheduleEvents
            .Where(e => e.DisplayId == displayId)
            .ToListAsync(HttpContext.RequestAborted);

        // A caller cannot schedule, replace or remove what she cannot see. Only content the save changes is
        // judged, since a full sync must send back events others scheduled; each asset is judged by the
        // scope of the location that owns it, exactly as GetAssets lists it.
        var touchedIds = new HashSet<int>();
        var keptEventIds = events.Where(e => e.ScheduleEventId != 0).Select(e => e.ScheduleEventId).ToHashSet();
        foreach (var dto in events)
        {
            var row = dto.ScheduleEventId != 0 ? existing.FirstOrDefault(e => e.ScheduleEventId == dto.ScheduleEventId) : null;
            if (row != null && string.Equals(row.ContentAssignment, dto.ContentAssignment, StringComparison.Ordinal))
                continue;
            touchedIds.UnionWith(ReadAssetIds(dto.ContentAssignment)!);
            if (row != null)
                touchedIds.UnionWith(ReadAssetIds(row.ContentAssignment) ?? []);
        }
        foreach (var removed in existing.Where(e => !keptEventIds.Contains(e.ScheduleEventId)))
            touchedIds.UnionWith(ReadAssetIds(removed.ContentAssignment) ?? []);

        var touchedAssets = allowedAssets.Where(a => touchedIds.Contains(a.AssetId)).ToList();
        if (touchedAssets.Count > 0)
        {
            var libraryIds = display.AssetLibraryLocationId is int libraryId ? new[] { libraryId } : Array.Empty<int>();
            var scopes = await GetListingScopesAsync(caller!, locationId, libraryIds);
            var touchedValues = await _contentScope.GetAssetValuesAsync(touchedAssets.Select(a => a.AssetId).ToList(), HttpContext.RequestAborted);
            var outOfScope = touchedAssets
                .Where(a => !scopes.TryGetValue(a.LocationId, out var scope) || scope == null
                            || !scope.IsVisible(touchedValues[a.AssetId]))
                .Select(a => a.AssetId)
                .ToList();
            if (outOfScope.Count > 0)
                return BadRequest(new { error = $"This save changes schedule content outside your content scope: {string.Join(", ", outOfScope)}" });
        }

        var keptIds = new HashSet<int>();
        foreach (var dto in events)
        {
            var row = dto.ScheduleEventId != 0
                ? existing.FirstOrDefault(e => e.ScheduleEventId == dto.ScheduleEventId)
                : null;

            if (dto.ScheduleEventId != 0 && row == null)
                return BadRequest(new { error = $"Schedule event {dto.ScheduleEventId} does not exist on this display." });

            if (row == null)
            {
                row = new TblScheduleEvent
                {
                    DisplayId = displayId,
                    LocationId = locationId,
                    // Nullable on the entity but NOT NULL in the database, so leaving it unset
                    // makes EF write an explicit NULL and the insert fails. 1 is the standard
                    // schedule event, matching what Schedule.razor writes.
                    ScheduleEventTypeId = 1,
                    CreateDate = DateTime.UtcNow
                };
                _db.TblScheduleEvents.Add(row);
            }

            row.ContentAssignment = dto.ContentAssignment;
            row.DefaultContent = dto.DefaultContent;
            row.StartTime = dto.StartTime;
            row.EndTime = dto.EndTime;
            row.StartDate = dto.StartDate;
            row.EndDate = dto.EndDate;
            row.Sunday = dto.Sunday;
            row.Monday = dto.Monday;
            row.Tuesday = dto.Tuesday;
            row.Wednesday = dto.Wednesday;
            row.Thursday = dto.Thursday;
            row.Friday = dto.Friday;
            row.Saturday = dto.Saturday;

            if (dto.ScheduleEventId != 0)
                keptIds.Add(dto.ScheduleEventId);
        }

        foreach (var stale in existing.Where(e => !keptIds.Contains(e.ScheduleEventId)))
            _db.TblScheduleEvents.Remove(stale);

        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        _manifestCache.InvalidateDisplay(_tenantContext.TenantId, displayId);

        _logger.LogInformation("Bundle API schedule save: display {DisplayId} at location {LocationId} by {User} ({Count} events)",
            displayId, locationId, caller!.User.UserPrincipalName, events.Count);

        return NoContent();
    }

    [HttpPost("assets")]
    [RequestSizeLimit(500L * 1024 * 1024)]
    public async Task<IActionResult> UploadAsset(int locationId, [FromForm] AssetUploadForm form)
    {
        var (caller, location, error) = await AuthorizeWriteAsync(locationId);
        if (error != null) return error;

        if (form.File == null || form.File.Length == 0)
            return BadRequest(new { error = "No file supplied." });

        if (form.ConstraintId is int constraintId and not 0)
        {
            var constraintOk = await _db.TblConstraints
                .AsNoTracking()
                .AnyAsync(c => c.ConstraintId == constraintId && (c.OwningLocationId == locationId || c.IsGlobal), HttpContext.RequestAborted);
            if (!constraintOk)
                return BadRequest(new { error = "Constraint does not belong to this location." });
        }

        // A person is behind a bundle upload but the bundle is not ours to put a picker in, so a scoped
        // caller must say who they are uploading as. The identity is never derived from caller.Access,
        // which collapses several partial rules to the first.
        int? uploadingAsGroupId = null;
        if (!string.IsNullOrWhiteSpace(form.UploadingAs))
        {
            if (string.Equals(form.UploadingAs, MyselfToken, StringComparison.OrdinalIgnoreCase))
                uploadingAsGroupId = null;
            else if (int.TryParse(form.UploadingAs, out var groupId))
                uploadingAsGroupId = groupId;
            else
                return BadRequest(new { error = $"uploadingAs must be \"{MyselfToken}\" or a group id." });
        }
        else
        {
            var candidates = await _uploadIdentity.GetCandidatesAsync(caller!.User.UserId, locationId, HttpContext.RequestAborted);
            if (candidates.Mode == UploadIdentityMode.Scoped)
            {
                // Exactly one candidate leaves nothing to choose.
                if (candidates.Candidates.Count != 1)
                    return BadRequest(new
                    {
                        error = "uploadingAs is required: choose which identity to upload as.",
                        upload = DescribeUploadCandidates(candidates)
                    });
                uploadingAsGroupId = candidates.Candidates[0].UserGroupId;
            }
        }

        await using var content = form.File.OpenReadStream();
        var result = await _assetIngestService.IngestAsync(new AssetIngestRequest
        {
            LocationId = locationId,
            ConstraintId = form.ConstraintId ?? 0,
            FileName = form.File.FileName,
            Content = content,
            UploaderUserId = caller!.User.UserId,
            UploadingAsUserGroupId = uploadingAsGroupId,
            EnableContentApproval = location!.EnableContentApproval,
            Source = "location-content-api"
        }, HttpContext.RequestAborted);

        if (!result.Success)
            return result.IdentityCandidates != null
                ? BadRequest(new { error = result.Error, upload = DescribeUploadCandidates(result.IdentityCandidates) })
                : BadRequest(new { error = result.Error });

        return Ok(new { result.AssetId, result.AssetPath, result.ApprovalStatus });
    }

    [HttpDelete("assets/{assetId:int}")]
    public async Task<IActionResult> DeleteAsset(int locationId, int assetId)
    {
        var (caller, _, error) = await AuthorizeWriteAsync(locationId);
        if (error != null) return error;

        var asset = await _db.TblAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AssetId == assetId && a.LocationId == locationId && !a.IsDeleted, HttpContext.RequestAborted);
        if (asset == null)
            return NotFound();

        if (!CanMutateAsset(caller!, asset, await GetMemberGroupIdsAsync(caller!)))
            return Forbid();

        await _assetIngestService.DeleteAssetAsync(assetId, HttpContext.RequestAborted);

        _logger.LogInformation("Bundle API asset delete: asset {AssetId} at location {LocationId} by {User}",
            assetId, locationId, caller.User.UserPrincipalName);

        return NoContent();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Per-location key-value storage
    // ────────────────────────────────────────────────────────────────────────

    [HttpGet("storage")]
    public async Task<IActionResult> ListStorage(int locationId)
    {
        var (_, _, error) = await AuthorizeAsync(locationId);
        if (error != null) return error;

        var keys = await _db.TblLocationBundleStorages
            .AsNoTracking()
            .Where(s => s.LocationId == locationId)
            .Select(s => s.StorageKey)
            .OrderBy(k => k)
            .ToListAsync(HttpContext.RequestAborted);

        return Ok(keys);
    }

    [HttpGet("storage/{key}")]
    public async Task<IActionResult> GetStorage(int locationId, string key)
    {
        var (_, _, error) = await AuthorizeAsync(locationId);
        if (error != null) return error;

        var row = await _db.TblLocationBundleStorages
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.LocationId == locationId && s.StorageKey == key, HttpContext.RequestAborted);

        if (row == null) return NotFound();
        return Content(row.Value, "application/json");
    }

    [HttpPut("storage/{key}")]
    public async Task<IActionResult> SetStorage(int locationId, string key)
    {
        var (caller, _, error) = await AuthorizeWriteAsync(locationId);
        if (error != null) return error;

        if (key.Length > 128)
            return BadRequest(new { error = "Storage key exceeds 128 characters." });

        string value;
        using (var reader = new StreamReader(Request.Body))
            value = await reader.ReadToEndAsync(HttpContext.RequestAborted);

        if (System.Text.Encoding.UTF8.GetByteCount(value) > StorageValueMaxBytes)
            return BadRequest(new { error = $"Storage value exceeds {StorageValueMaxBytes / 1024} KB." });

        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(value);
        }
        catch (System.Text.Json.JsonException)
        {
            return BadRequest(new { error = "Storage value must be valid JSON." });
        }

        var row = await _db.TblLocationBundleStorages
            .FirstOrDefaultAsync(s => s.LocationId == locationId && s.StorageKey == key, HttpContext.RequestAborted);

        if (row == null)
        {
            var keyCount = await _db.TblLocationBundleStorages
                .CountAsync(s => s.LocationId == locationId, HttpContext.RequestAborted);
            if (keyCount >= StorageMaxKeysPerLocation)
                return BadRequest(new { error = $"Storage key limit ({StorageMaxKeysPerLocation}) reached for this location." });

            row = new TblLocationBundleStorage { LocationId = locationId, StorageKey = key };
            _db.TblLocationBundleStorages.Add(row);
        }

        row.Value = value;
        row.LastEditDate = DateTime.UtcNow;
        row.LastEditUser = caller!.User.UserPrincipalName;
        await _db.SaveChangesAsync(HttpContext.RequestAborted);

        return NoContent();
    }

    [HttpDelete("storage/{key}")]
    public async Task<IActionResult> DeleteStorage(int locationId, string key)
    {
        var (_, _, error) = await AuthorizeWriteAsync(locationId);
        if (error != null) return error;

        var row = await _db.TblLocationBundleStorages
            .FirstOrDefaultAsync(s => s.LocationId == locationId && s.StorageKey == key, HttpContext.RequestAborted);
        if (row == null) return NotFound();

        _db.TblLocationBundleStorages.Remove(row);
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return NoContent();
    }

    public class ScheduleEventDto
    {
        public int ScheduleEventId { get; set; }
        public string ContentAssignment { get; set; } = string.Empty;
        public bool DefaultContent { get; set; }
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool Sunday { get; set; }
        public bool Monday { get; set; }
        public bool Tuesday { get; set; }
        public bool Wednesday { get; set; }
        public bool Thursday { get; set; }
        public bool Friday { get; set; }
        public bool Saturday { get; set; }
    }

    public class AssetUploadForm
    {
        public Microsoft.AspNetCore.Http.IFormFile? File { get; set; }
        public int? ConstraintId { get; set; }

        /// <summary>"myself" or a user group id. Required from a caller with partial access through more than one rule.</summary>
        public string? UploadingAs { get; set; }
    }

    private const string MyselfToken = "myself";

    // The upload identities as a bundle sees them: each carries the token to send back as uploadingAs.
    private static object DescribeUploadCandidates(UploadCandidates candidates) => new
    {
        Mode = candidates.Mode switch
        {
            UploadIdentityMode.Scoped => "scoped",
            UploadIdentityMode.NotPermitted => "notPermitted",
            _ => "unrestricted"
        },
        Candidates = candidates.Candidates.Select(c => new
        {
            UploadingAs = c.UserGroupId?.ToString() ?? MyselfToken,
            c.Label
        })
    };

    // ────────────────────────────────────────────────────────────────────────
    // Shared enforcement
    // ────────────────────────────────────────────────────────────────────────

    private async Task<(LocationCaller? Caller, TblLocation? Location, IActionResult? Error)> AuthorizeAsync(int locationId)
    {
        if (!_featureFlags.Value.LocationBundles)
            return (null, null, NotFound());

        var caller = await _accessResolver.ResolveAsync(User, locationId, HttpContext.RequestAborted);
        if (caller == null)
            return (null, null, Unauthorized());
        if (!caller.IsAssigned)
            return (null, null, Forbid());

        var location = await _db.TblLocations
            .AsNoTracking()
            .Include(l => l.LocationType)
            .FirstOrDefaultAsync(l => l.LocationId == locationId, HttpContext.RequestAborted);

        if (location == null)
            return (null, null, NotFound());

        return (caller, location, null);
    }

    /// <summary>
    /// Write authorization: assigned + access level above "view" + the X-DX-Bundle
    /// header (cookies are SameSite=Lax; requiring a custom header blocks any
    /// cross-site request that could still carry them).
    /// </summary>
    private async Task<(LocationCaller? Caller, TblLocation? Location, IActionResult? Error)> AuthorizeWriteAsync(int locationId)
    {
        if (!Request.Headers.ContainsKey(LocationBundleConstants.WriteHeaderName))
            return (null, null, BadRequest(new { error = $"Missing required header {LocationBundleConstants.WriteHeaderName}." }));

        var (caller, location, error) = await AuthorizeAsync(locationId);
        if (error != null)
            return (caller, location, error);

        if (!caller!.CanWrite)
            return (caller, location, Forbid());

        return (caller, location, null);
    }

    // Parsed exactly as the display path parses it, so an id the player would resolve cannot slip past the
    // checks in a form a stricter parser skips. Null when the assignment is not valid.
    private static List<int>? ReadAssetIds(string? contentAssignment)
    {
        try
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<ContentAssignment>(contentAssignment ?? "")?.GetAssetIDList() ?? [];
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    private Task<bool> DisplayBelongsToLocationAsync(int displayId, int locationId) =>
        _db.TblDisplays.AsNoTracking().AnyAsync(d => d.DisplayId == displayId && d.LocationId == locationId, HttpContext.RequestAborted);

    /// <summary>
    /// The scope each listed location's assets are judged by. The host location uses the caller's own access;
    /// each linked Asset Library uses the caller's access at that library, so one location's rule never
    /// decides another's visibility. A library where the caller has no rule maps to null and lists nothing.
    /// </summary>
    private async Task<Dictionary<int, ContentScopeContext?>> GetListingScopesAsync(
        LocationCaller caller, int locationId, IEnumerable<int> libraryIds)
    {
        var ct = HttpContext.RequestAborted;
        var scopes = new Dictionary<int, ContentScopeContext?>
        {
            [locationId] = await _contentScope.GetAsync(caller.User.UserId, locationId, caller.Access, ct)
        };

        foreach (var libraryId in libraryIds.Where(id => id != locationId))
        {
            var libraryAccess = await _locationAccess.ResolveAccessAsync(caller.User.UserId, libraryId);
            scopes[libraryId] = libraryAccess == null
                ? null
                : await _contentScope.GetAsync(caller.User.UserId, libraryId, libraryAccess, ct);
        }

        return scopes;
    }

    // Ownership decides who may change an asset; scope only decides who can see it. Group-owned content
    // is controllable by whoever is in the group now.
    private static bool CanMutateAsset(LocationCaller caller, TblAsset asset, IReadOnlySet<int> memberGroupIds) =>
        caller.CanWrite
        && AssetOwnership.CanControl(caller.Access?.AccessLevel, caller.User.UserId, memberGroupIds,
            asset.UploadedByUserId, asset.UploadedByUserGroupId);

    private async Task<IReadOnlySet<int>> GetMemberGroupIdsAsync(LocationCaller caller) =>
        (await _db.TblUserGroupMemberships
            .AsNoTracking()
            .Where(m => m.UserId == caller.User.UserId)
            .Select(m => m.UserGroupId)
            .ToListAsync(HttpContext.RequestAborted)).ToHashSet();
}
