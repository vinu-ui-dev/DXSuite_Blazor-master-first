using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DXSuite.Director.Entities;
using DXSuite.Director.Services;

namespace DXSuite.Director.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OverrideController : ControllerBase
    {
        private readonly IOverrideService _overrideService;
        private readonly ILogger<OverrideController> _logger;

        public OverrideController(IOverrideService overrideService, ILogger<OverrideController> logger)
        {
            _overrideService = overrideService;
            _logger = logger;
        }

        private bool ValidateApiKey()
        {
            var apiKey = Request.Headers["X-API-KEY"].ToString();
            return !string.IsNullOrEmpty(apiKey);
        }

        private string? GetClientIp()
        {
            return HttpContext.Connection.RemoteIpAddress?.ToString();
        }

        #region CRUD Endpoints

        /// <summary>
        /// Get all overrides
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<List<OverrideListItem>>> GetOverrides()
        {
            var overrides = await _overrideService.GetAllOverridesAsync();
            var result = overrides.Select(o => new OverrideListItem
            {
                OverrideId = o.OverrideId,
                OverrideName = o.OverrideName,
                Description = o.Description,
                Priority = o.Priority,
                IsActive = o.IsActive,
                IsScheduled = o.IsScheduled,
                DeviceCount = o.OverrideDevices.Count,
                ContentCount = o.OverrideContents.Count
            }).ToList();

            return Ok(result);
        }

        /// <summary>
        /// Get override details by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<OverrideDetail>> GetOverride(int id)
        {
            var over = await _overrideService.GetOverrideByIdAsync(id);
            if (over == null)
                return NotFound(new { message = $"Override {id} not found" });

            var result = new OverrideDetail
            {
                OverrideId = over.OverrideId,
                OverrideName = over.OverrideName,
                Description = over.Description,
                Priority = over.Priority,
                IsActive = over.IsActive,
                IsScheduled = over.IsScheduled,
                StartDate = over.StartDate,
                EndDate = over.EndDate,
                StartTime = over.StartTime?.ToString("HH:mm:ss"),
                EndTime = over.EndTime?.ToString("HH:mm:ss"),
                Sunday = over.Sunday,
                Monday = over.Monday,
                Tuesday = over.Tuesday,
                Wednesday = over.Wednesday,
                Thursday = over.Thursday,
                Friday = over.Friday,
                Saturday = over.Saturday,
                CreatedDate = over.CreatedDate,
                ModifiedDate = over.ModifiedDate,
                CreatedBy = over.CreatedBy,
                ModifiedBy = over.ModifiedBy,
                DeviceIds = over.OverrideDevices.Select(od => od.DeviceId).ToList(),
                Contents = over.OverrideContents.Select(oc => new OverrideContentItem
                {
                    OverrideContentId = oc.OverrideContentId,
                    DeviceId = oc.DeviceId,
                    ResolutionWidth = oc.ResolutionWidth,
                    ResolutionHeight = oc.ResolutionHeight,
                    ContentPath = oc.ContentPath,
                    AssetId = oc.AssetId,
                    Duration = oc.Duration
                }).ToList()
            };

            return Ok(result);
        }

        /// <summary>
        /// Create a new override
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<TblOverride>> CreateOverride([FromBody] CreateOverrideRequest request)
        {
            if (string.IsNullOrEmpty(request.OverrideName))
                return BadRequest(new { message = "Override name is required" });

            var overrideEntity = new TblOverride
            {
                OverrideName = request.OverrideName,
                Description = request.Description,
                IsScheduled = request.IsScheduled,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                StartTime = !string.IsNullOrEmpty(request.StartTime) ? TimeOnly.Parse(request.StartTime) : null,
                EndTime = !string.IsNullOrEmpty(request.EndTime) ? TimeOnly.Parse(request.EndTime) : null,
                Sunday = request.Sunday,
                Monday = request.Monday,
                Tuesday = request.Tuesday,
                Wednesday = request.Wednesday,
                Thursday = request.Thursday,
                Friday = request.Friday,
                Saturday = request.Saturday
            };

            var result = await _overrideService.CreateOverrideAsync(overrideEntity, User.Identity?.Name);

            // Set devices if provided
            if (request.DeviceIds != null && request.DeviceIds.Count > 0)
            {
                await _overrideService.SetDevicesForOverrideAsync(result.OverrideId, request.DeviceIds);
            }

            return CreatedAtAction(nameof(GetOverride), new { id = result.OverrideId }, result);
        }

        /// <summary>
        /// Update an existing override
        /// </summary>
        [HttpPut("{id}")]
        public async Task<ActionResult<TblOverride>> UpdateOverride(int id, [FromBody] UpdateOverrideRequest request)
        {
            var existing = await _overrideService.GetOverrideByIdAsync(id);
            if (existing == null)
                return NotFound(new { message = $"Override {id} not found" });

            existing.OverrideName = request.OverrideName ?? existing.OverrideName;
            existing.Description = request.Description;
            existing.IsScheduled = request.IsScheduled;
            existing.StartDate = request.StartDate;
            existing.EndDate = request.EndDate;
            existing.StartTime = !string.IsNullOrEmpty(request.StartTime) ? TimeOnly.Parse(request.StartTime) : null;
            existing.EndTime = !string.IsNullOrEmpty(request.EndTime) ? TimeOnly.Parse(request.EndTime) : null;
            existing.Sunday = request.Sunday;
            existing.Monday = request.Monday;
            existing.Tuesday = request.Tuesday;
            existing.Wednesday = request.Wednesday;
            existing.Thursday = request.Thursday;
            existing.Friday = request.Friday;
            existing.Saturday = request.Saturday;

            var result = await _overrideService.UpdateOverrideAsync(existing, User.Identity?.Name);

            // Update devices if provided
            if (request.DeviceIds != null)
            {
                await _overrideService.SetDevicesForOverrideAsync(id, request.DeviceIds);
            }

            return Ok(result);
        }

        /// <summary>
        /// Delete an override
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteOverride(int id)
        {
            try
            {
                await _overrideService.DeleteOverrideAsync(id);
                return Ok(new { message = $"Override {id} deleted successfully" });
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { message = $"Override {id} not found" });
            }
        }

        #endregion

        #region External Trigger Endpoints

        /// <summary>
        /// Activate an override (external trigger)
        /// </summary>
        [HttpPost("{id}/activate")]
        [AllowAnonymous]
        public async Task<ActionResult> ActivateOverride(int id)
        {
            if (!ValidateApiKey())
                return Unauthorized(new { message = "Invalid or missing API key" });

            var success = await _overrideService.ActivateOverrideAsync(id, "API", User.Identity?.Name, GetClientIp());

            if (!success)
                return NotFound(new { message = $"Override {id} not found" });

            _logger.LogInformation("Override {OverrideId} activated via API from {Ip}", id, GetClientIp());

            return Ok(new { message = $"Override {id} activated successfully" });
        }

        /// <summary>
        /// Deactivate an override (external trigger)
        /// </summary>
        [HttpPost("{id}/deactivate")]
        [AllowAnonymous]
        public async Task<ActionResult> DeactivateOverride(int id)
        {
            if (!ValidateApiKey())
                return Unauthorized(new { message = "Invalid or missing API key" });

            var success = await _overrideService.DeactivateOverrideAsync(id, "API", User.Identity?.Name, GetClientIp());

            if (!success)
                return NotFound(new { message = $"Override {id} not found" });

            _logger.LogInformation("Override {OverrideId} deactivated via API from {Ip}", id, GetClientIp());

            return Ok(new { message = $"Override {id} deactivated successfully" });
        }

        #endregion

        #region Status Endpoints

        /// <summary>
        /// Get status of a single override
        /// </summary>
        [HttpGet("{id}/status")]
        [AllowAnonymous]
        public async Task<ActionResult<OverrideStatusInfo>> GetOverrideStatus(int id)
        {
            if (!ValidateApiKey())
                return Unauthorized(new { message = "Invalid or missing API key" });

            try
            {
                var status = await _overrideService.GetOverrideStatusAsync(id);
                return Ok(status);
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { message = $"Override {id} not found" });
            }
        }

        /// <summary>
        /// Get status of all overrides
        /// </summary>
        [HttpGet("status")]
        [AllowAnonymous]
        public async Task<ActionResult<List<OverrideStatusInfo>>> GetOverrideStatuses()
        {
            if (!ValidateApiKey())
                return Unauthorized(new { message = "Invalid or missing API key" });

            var statuses = await _overrideService.GetAllOverrideStatusesAsync();
            return Ok(statuses);
        }

        #endregion

        #region Priority Management

        /// <summary>
        /// Reorder override priorities
        /// </summary>
        [HttpPut("reorder")]
        public async Task<ActionResult> ReorderOverrides([FromBody] List<int> overrideIdsInOrder)
        {
            if (overrideIdsInOrder == null || overrideIdsInOrder.Count == 0)
                return BadRequest(new { message = "Override IDs list is required" });

            await _overrideService.ReorderOverridesAsync(overrideIdsInOrder);

            return Ok(new { message = "Overrides reordered successfully" });
        }

        #endregion

        #region Device Management

        /// <summary>
        /// Get devices for an override
        /// </summary>
        [HttpGet("{id}/devices")]
        public async Task<ActionResult<List<string>>> GetDevicesForOverride(int id)
        {
            var deviceIds = await _overrideService.GetDeviceIdsForOverrideAsync(id);
            return Ok(deviceIds);
        }

        /// <summary>
        /// Set devices for an override
        /// </summary>
        [HttpPut("{id}/devices")]
        public async Task<ActionResult> SetDevicesForOverride(int id, [FromBody] List<string> deviceIds)
        {
            await _overrideService.SetDevicesForOverrideAsync(id, deviceIds);
            return Ok(new { message = $"Set {deviceIds.Count} devices for override {id}" });
        }

        /// <summary>
        /// Get resolutions for a list of devices
        /// </summary>
        [HttpPost("resolutions")]
        public async Task<ActionResult<List<ResolutionInfo>>> GetResolutionsForDevices([FromBody] List<string> deviceIds)
        {
            var resolutions = await _overrideService.GetResolutionsForDevicesAsync(deviceIds);
            return Ok(resolutions);
        }

        #endregion

        #region Content Management

        /// <summary>
        /// Get content for an override
        /// </summary>
        [HttpGet("{id}/content")]
        public async Task<ActionResult<List<OverrideContentItem>>> GetContentForOverride(int id)
        {
            var contents = await _overrideService.GetContentForOverrideAsync(id);
            var result = contents.Select(oc => new OverrideContentItem
            {
                OverrideContentId = oc.OverrideContentId,
                DeviceId = oc.DeviceId,
                ResolutionWidth = oc.ResolutionWidth,
                ResolutionHeight = oc.ResolutionHeight,
                ContentPath = oc.ContentPath,
                AssetId = oc.AssetId,
                Duration = oc.Duration
            }).ToList();

            return Ok(result);
        }

        /// <summary>
        /// Set content for a resolution (default content)
        /// </summary>
        [HttpPost("{id}/content/resolution")]
        public async Task<ActionResult<TblOverrideContent>> SetContentForResolution(int id, [FromBody] SetResolutionContentRequest request)
        {
            var content = await _overrideService.SetContentForResolutionAsync(
                id,
                request.ResolutionWidth,
                request.ResolutionHeight,
                request.ContentPath,
                request.AssetId,
                request.Duration
            );

            return Ok(content);
        }

        /// <summary>
        /// Set content for a specific device
        /// </summary>
        [HttpPost("{id}/content/device")]
        public async Task<ActionResult<TblOverrideContent>> SetContentForDevice(int id, [FromBody] SetDeviceContentRequest request)
        {
            var content = await _overrideService.SetContentForDeviceAsync(
                id,
                request.DeviceId,
                request.ResolutionWidth,
                request.ResolutionHeight,
                request.ContentPath,
                request.AssetId,
                request.Duration
            );

            return Ok(content);
        }

        /// <summary>
        /// Delete override content
        /// </summary>
        [HttpDelete("content/{contentId}")]
        public async Task<ActionResult> DeleteContent(int contentId)
        {
            await _overrideService.DeleteContentAsync(contentId);
            return Ok(new { message = $"Content {contentId} deleted successfully" });
        }

        #endregion
    }

    #region Request/Response Models

    public class OverrideListItem
    {
        public int OverrideId { get; set; }
        public string OverrideName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int Priority { get; set; }
        public bool IsActive { get; set; }
        public bool IsScheduled { get; set; }
        public int DeviceCount { get; set; }
        public int ContentCount { get; set; }
    }

    public class OverrideDetail
    {
        public int OverrideId { get; set; }
        public string OverrideName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int Priority { get; set; }
        public bool IsActive { get; set; }
        public bool IsScheduled { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
        public bool Sunday { get; set; }
        public bool Monday { get; set; }
        public bool Tuesday { get; set; }
        public bool Wednesday { get; set; }
        public bool Thursday { get; set; }
        public bool Friday { get; set; }
        public bool Saturday { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public string? CreatedBy { get; set; }
        public string? ModifiedBy { get; set; }
        public List<string> DeviceIds { get; set; } = new();
        public List<OverrideContentItem> Contents { get; set; } = new();
    }

    public class OverrideContentItem
    {
        public int OverrideContentId { get; set; }
        public string? DeviceId { get; set; }
        public int ResolutionWidth { get; set; }
        public int ResolutionHeight { get; set; }
        public string ContentPath { get; set; } = string.Empty;
        public int? AssetId { get; set; }
        public int Duration { get; set; }
    }

    public class CreateOverrideRequest
    {
        public string OverrideName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsScheduled { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
        public bool Sunday { get; set; }
        public bool Monday { get; set; }
        public bool Tuesday { get; set; }
        public bool Wednesday { get; set; }
        public bool Thursday { get; set; }
        public bool Friday { get; set; }
        public bool Saturday { get; set; }
        public List<string>? DeviceIds { get; set; }
    }

    public class UpdateOverrideRequest
    {
        public string? OverrideName { get; set; }
        public string? Description { get; set; }
        public bool IsScheduled { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
        public bool Sunday { get; set; }
        public bool Monday { get; set; }
        public bool Tuesday { get; set; }
        public bool Wednesday { get; set; }
        public bool Thursday { get; set; }
        public bool Friday { get; set; }
        public bool Saturday { get; set; }
        public List<string>? DeviceIds { get; set; }
    }

    public class SetResolutionContentRequest
    {
        public int ResolutionWidth { get; set; }
        public int ResolutionHeight { get; set; }
        public string ContentPath { get; set; } = string.Empty;
        public int? AssetId { get; set; }
        public int Duration { get; set; } = 7;
    }

    public class SetDeviceContentRequest
    {
        public string DeviceId { get; set; } = string.Empty;
        public int ResolutionWidth { get; set; }
        public int ResolutionHeight { get; set; }
        public string ContentPath { get; set; } = string.Empty;
        public int? AssetId { get; set; }
        public int Duration { get; set; } = 7;
    }

    #endregion
}
