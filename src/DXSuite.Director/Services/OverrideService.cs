using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Services
{
    public class OverrideService : IOverrideService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OverrideService> _logger;
        private readonly IActiveOverrideGate _overrideGate;

        public OverrideService(IServiceScopeFactory scopeFactory, ILogger<OverrideService> logger, IActiveOverrideGate overrideGate)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _overrideGate = overrideGate;
        }

        private IDirectorDBContext GetDbContext()
        {
            var scope = _scopeFactory.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
        }

        #region CRUD Operations

        public async Task<List<TblOverride>> GetAllOverridesAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            return await db.TblOverrides
                .Include(o => o.OverrideDevices)
                .Include(o => o.OverrideContents)
                .OrderBy(o => o.Priority)
                .ToListAsync();
        }

        public async Task<TblOverride?> GetOverrideByIdAsync(int overrideId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            return await db.TblOverrides
                .Include(o => o.OverrideDevices)
                .Include(o => o.OverrideContents)
                .FirstOrDefaultAsync(o => o.OverrideId == overrideId);
        }

        public async Task<TblOverride> CreateOverrideAsync(TblOverride overrideEntity, string? createdBy = null)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            overrideEntity.CreatedDate = DateTime.UtcNow;
            overrideEntity.ModifiedDate = DateTime.UtcNow;
            overrideEntity.CreatedBy = createdBy;
            overrideEntity.ModifiedBy = createdBy;

            // Get the next priority (append to end of list)
            var maxPriority = await db.TblOverrides.MaxAsync(o => (int?)o.Priority) ?? -1;
            overrideEntity.Priority = maxPriority + 1;

            db.TblOverrides.Add(overrideEntity);
            await db.SaveChangesAsync();
            _overrideGate.Invalidate();

            _logger.LogInformation("Created override {OverrideId}: {OverrideName}", overrideEntity.OverrideId, overrideEntity.OverrideName);

            return overrideEntity;
        }

        public async Task<TblOverride> UpdateOverrideAsync(TblOverride overrideEntity, string? modifiedBy = null)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var existing = await db.TblOverrides.FindAsync(overrideEntity.OverrideId);
            if (existing == null)
                throw new KeyNotFoundException($"Override {overrideEntity.OverrideId} not found");

            existing.OverrideName = overrideEntity.OverrideName;
            existing.Description = overrideEntity.Description;
            existing.IsScheduled = overrideEntity.IsScheduled;
            existing.StartDate = overrideEntity.StartDate;
            existing.EndDate = overrideEntity.EndDate;
            existing.StartTime = overrideEntity.StartTime;
            existing.EndTime = overrideEntity.EndTime;
            existing.Sunday = overrideEntity.Sunday;
            existing.Monday = overrideEntity.Monday;
            existing.Tuesday = overrideEntity.Tuesday;
            existing.Wednesday = overrideEntity.Wednesday;
            existing.Thursday = overrideEntity.Thursday;
            existing.Friday = overrideEntity.Friday;
            existing.Saturday = overrideEntity.Saturday;
            existing.ModifiedDate = DateTime.UtcNow;
            existing.ModifiedBy = modifiedBy;

            await db.SaveChangesAsync();
            _overrideGate.Invalidate();

            _logger.LogInformation("Updated override {OverrideId}: {OverrideName}", existing.OverrideId, existing.OverrideName);

            return existing;
        }

        public async Task DeleteOverrideAsync(int overrideId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var existing = await db.TblOverrides.FindAsync(overrideId);
            if (existing == null)
                throw new KeyNotFoundException($"Override {overrideId} not found");

            var overrideName = existing.OverrideName;

            db.TblOverrides.Remove(existing);
            await db.SaveChangesAsync();
            _overrideGate.Invalidate();

            _logger.LogInformation("Deleted override {OverrideId}: {OverrideName}", overrideId, overrideName);
        }

        #endregion

        #region Device Management

        public async Task<List<string>> GetDeviceIdsForOverrideAsync(int overrideId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            return await db.TblOverrideDevices
                .Where(od => od.OverrideId == overrideId)
                .Select(od => od.DeviceId)
                .ToListAsync();
        }

        public async Task<List<TblDevice>> GetDevicesForOverrideAsync(int overrideId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            return await db.TblOverrideDevices
                .Where(od => od.OverrideId == overrideId)
                .Include(od => od.Device)
                    .ThenInclude(d => d.Location)
                .Include(od => od.Device)
                    .ThenInclude(d => d.Display)
                .Select(od => od.Device)
                .ToListAsync();
        }

        public async Task SetDevicesForOverrideAsync(int overrideId, List<string> deviceIds)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            // Remove existing device associations
            var existingDevices = await db.TblOverrideDevices
                .Where(od => od.OverrideId == overrideId)
                .ToListAsync();

            db.TblOverrideDevices.RemoveRange(existingDevices);

            // Add new device associations
            var newDevices = deviceIds.Select(deviceId => new TblOverrideDevice
            {
                OverrideId = overrideId,
                DeviceId = deviceId
            }).ToList();

            await db.TblOverrideDevices.AddRangeAsync(newDevices);
            await db.SaveChangesAsync();

            _logger.LogInformation("Set {Count} devices for override {OverrideId}", deviceIds.Count, overrideId);
        }

        public async Task<List<int>> GetDisplayIdsForOverrideAsync(int overrideId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            return await db.TblOverrideDisplays
                .Where(od => od.OverrideId == overrideId)
                .Select(od => od.DisplayId)
                .ToListAsync();
        }

        public async Task SetDisplaysForOverrideAsync(int overrideId, List<int> displayIds)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var existingDisplays = await db.TblOverrideDisplays
                .Where(od => od.OverrideId == overrideId)
                .ToListAsync();

            db.TblOverrideDisplays.RemoveRange(existingDisplays);

            var newDisplays = displayIds.Select(displayId => new TblOverrideDisplay
            {
                OverrideId = overrideId,
                DisplayId = displayId
            }).ToList();

            await db.TblOverrideDisplays.AddRangeAsync(newDisplays);
            await db.SaveChangesAsync();

            _logger.LogInformation("Set {Count} displays for override {OverrideId}", displayIds.Count, overrideId);
        }

        #endregion

        #region Content Management

        public async Task<List<TblOverrideContent>> GetContentForOverrideAsync(int overrideId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            return await db.TblOverrideContents
                .Where(oc => oc.OverrideId == overrideId)
                .Include(oc => oc.Device)
                .Include(oc => oc.Asset)
                .OrderBy(oc => oc.DeviceId == null ? 0 : 1) // Default content first
                .ThenBy(oc => oc.ResolutionWidth)
                .ThenBy(oc => oc.ResolutionHeight)
                .ToListAsync();
        }

        public async Task<TblOverrideContent> SetContentForResolutionAsync(int overrideId, int width, int height, string contentPath, int? assetId, int duration = 7)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            // Check if content already exists for this resolution (default, no device)
            var existing = await db.TblOverrideContents
                .FirstOrDefaultAsync(oc => oc.OverrideId == overrideId
                    && oc.DeviceId == null
                    && oc.ResolutionWidth == width
                    && oc.ResolutionHeight == height);

            if (existing != null)
            {
                existing.ContentPath = contentPath;
                existing.AssetId = assetId;
                existing.Duration = duration;
            }
            else
            {
                existing = new TblOverrideContent
                {
                    OverrideId = overrideId,
                    DeviceId = null,
                    ResolutionWidth = width,
                    ResolutionHeight = height,
                    ContentPath = contentPath,
                    AssetId = assetId,
                    Duration = duration,
                    CreatedDate = DateTime.UtcNow
                };
                db.TblOverrideContents.Add(existing);
            }

            await db.SaveChangesAsync();

            _logger.LogInformation("Set content for override {OverrideId} resolution {Width}x{Height}", overrideId, width, height);

            return existing;
        }

        public async Task<TblOverrideContent> SetContentForDeviceAsync(int overrideId, string deviceId, int width, int height, string contentPath, int? assetId, int duration = 7)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            // Check if content already exists for this device
            var existing = await db.TblOverrideContents
                .FirstOrDefaultAsync(oc => oc.OverrideId == overrideId && oc.DeviceId == deviceId);

            if (existing != null)
            {
                existing.ContentPath = contentPath;
                existing.AssetId = assetId;
                existing.Duration = duration;
                existing.ResolutionWidth = width;
                existing.ResolutionHeight = height;
            }
            else
            {
                existing = new TblOverrideContent
                {
                    OverrideId = overrideId,
                    DeviceId = deviceId,
                    ResolutionWidth = width,
                    ResolutionHeight = height,
                    ContentPath = contentPath,
                    AssetId = assetId,
                    Duration = duration,
                    CreatedDate = DateTime.UtcNow
                };
                db.TblOverrideContents.Add(existing);
            }

            await db.SaveChangesAsync();

            _logger.LogInformation("Set content for override {OverrideId} device {DeviceId}", overrideId, deviceId);

            return existing;
        }

        public async Task DeleteContentAsync(int overrideContentId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var existing = await db.TblOverrideContents.FindAsync(overrideContentId);
            if (existing != null)
            {
                db.TblOverrideContents.Remove(existing);
                await db.SaveChangesAsync();

                _logger.LogInformation("Deleted override content {OverrideContentId}", overrideContentId);
            }
        }

        #endregion

        #region Activation

        public async Task<bool> ActivateOverrideAsync(int overrideId, string triggerSource, string? user, string? ip)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var existing = await db.TblOverrides.FindAsync(overrideId);
            if (existing == null)
                return false;

            existing.IsActive = true;
            existing.ModifiedDate = DateTime.UtcNow;
            existing.ModifiedBy = user;

            await db.SaveChangesAsync();
            _overrideGate.Invalidate();
            await LogOverrideActionAsync(overrideId, existing.OverrideName, "ACTIVATED", triggerSource, user, ip);

            _logger.LogInformation("Activated override {OverrideId}: {OverrideName} via {TriggerSource}", overrideId, existing.OverrideName, triggerSource);

            return true;
        }

        public async Task<bool> DeactivateOverrideAsync(int overrideId, string triggerSource, string? user, string? ip)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var existing = await db.TblOverrides.FindAsync(overrideId);
            if (existing == null)
                return false;

            existing.IsActive = false;
            existing.ModifiedDate = DateTime.UtcNow;
            existing.ModifiedBy = user;

            await db.SaveChangesAsync();
            _overrideGate.Invalidate();
            await LogOverrideActionAsync(overrideId, existing.OverrideName, "DEACTIVATED", triggerSource, user, ip);

            _logger.LogInformation("Deactivated override {OverrideId}: {OverrideName} via {TriggerSource}", overrideId, existing.OverrideName, triggerSource);

            return true;
        }

        #endregion

        #region Status Queries

        public async Task<TblOverride?> GetActiveOverrideForDeviceAsync(string deviceId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            // Runs on every player heartbeat: keep this to a single per-device query with no logging
            var overridesForDevice = await db.TblOverrideDevices
                .Where(od => od.DeviceId == deviceId)
                .Include(od => od.Override)
                    .ThenInclude(o => o.OverrideContents)
                .Select(od => od.Override)
                .OrderBy(o => o.Priority)
                .ToListAsync();

            if (overridesForDevice.Count == 0)
                return null;

            // Only devices that actually carry an override pay for this second query, which keeps
            // the heartbeat cost unchanged for the fleet, where an override is the exception.
            var timeZoneId = await db.TblDevices
                .Where(d => d.DeviceId == deviceId)
                .Select(d => d.TimeZone)
                .FirstOrDefaultAsync();

            // An unresolvable zone leaves scheduled overrides inactive, which is this path failing
            // toward its own default: the display keeps showing its normal content. Guessing with
            // the server's clock would put override content on a screen at a time nobody chose.
            if (!DeviceLocalTime.TryResolve(DateTimeOffset.UtcNow, timeZoneId, out var localNow, _logger))
            {
                return overridesForDevice.FirstOrDefault(o => o.IsActive);
            }

            foreach (var over in overridesForDevice)
            {
                if (IsEffectivelyActive(over, localNow))
                {
                    return over;
                }
            }

            return null;
        }

        public async Task<bool> IsOverrideEffectivelyActiveAsync(int overrideId)
        {
            var over = await GetOverrideByIdAsync(overrideId);
            if (over == null)
                return false;

            // No device in scope: server clock, per the rule for operator-facing surfaces.
            return IsEffectivelyActive(over, DateTime.Now);
        }

        public async Task<List<TblOverride>> GetActiveOverridesAsync()
        {
            var allOverrides = await GetAllOverridesAsync();
            // No device in scope: server clock, per the rule for operator-facing surfaces.
            return allOverrides.Where(o => IsEffectivelyActive(o, DateTime.Now)).ToList();
        }

        public async Task<OverrideStatusInfo> GetOverrideStatusAsync(int overrideId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var over = await db.TblOverrides
                .Include(o => o.OverrideDevices)
                .FirstOrDefaultAsync(o => o.OverrideId == overrideId);

            if (over == null)
                throw new KeyNotFoundException($"Override {overrideId} not found");

            // Get override action events from EventLog (using Details JSON to filter by overrideId)
            var activatedType = await db.TblEventTypes.FirstOrDefaultAsync(t => t.EventTypeName == "OVERRIDE_ACTIVATED");
            var deactivatedType = await db.TblEventTypes.FirstOrDefaultAsync(t => t.EventTypeName == "OVERRIDE_DEACTIVATED");

            DateTime? lastActivated = null;
            DateTime? lastDeactivated = null;

            if (activatedType != null)
            {
                var activatedLog = await db.TblEventLogs
                    .Where(l => l.EventTypeId == activatedType.EventTypeId && l.Details != null && l.Details.Contains($"\"overrideId\":{overrideId}"))
                    .OrderByDescending(l => l.CreateDate)
                    .FirstOrDefaultAsync();
                lastActivated = activatedLog?.CreateDate;
            }

            if (deactivatedType != null)
            {
                var deactivatedLog = await db.TblEventLogs
                    .Where(l => l.EventTypeId == deactivatedType.EventTypeId && l.Details != null && l.Details.Contains($"\"overrideId\":{overrideId}"))
                    .OrderByDescending(l => l.CreateDate)
                    .FirstOrDefaultAsync();
                lastDeactivated = deactivatedLog?.CreateDate;
            }

            return new OverrideStatusInfo
            {
                OverrideId = over.OverrideId,
                OverrideName = over.OverrideName,
                Description = over.Description,
                IsActive = over.IsActive,
                IsScheduleActive = IsScheduleActive(over, DateTime.Now),
                IsEffectivelyActive = IsEffectivelyActive(over, DateTime.Now),
                Priority = over.Priority,
                DeviceCount = over.OverrideDevices.Count,
                LastActivated = lastActivated,
                LastDeactivated = lastDeactivated
            };
        }

        public async Task<List<OverrideStatusInfo>> GetAllOverrideStatusesAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var overrides = await db.TblOverrides
                .Include(o => o.OverrideDevices)
                .OrderBy(o => o.Priority)
                .ToListAsync();

            // Pre-fetch event types for override actions
            var activatedType = await db.TblEventTypes.FirstOrDefaultAsync(t => t.EventTypeName == "OVERRIDE_ACTIVATED");
            var deactivatedType = await db.TblEventTypes.FirstOrDefaultAsync(t => t.EventTypeName == "OVERRIDE_DEACTIVATED");

            var result = new List<OverrideStatusInfo>();

            foreach (var over in overrides)
            {
                DateTime? lastActivated = null;
                DateTime? lastDeactivated = null;

                if (activatedType != null)
                {
                    var activatedLog = await db.TblEventLogs
                        .Where(l => l.EventTypeId == activatedType.EventTypeId && l.Details != null && l.Details.Contains($"\"overrideId\":{over.OverrideId}"))
                        .OrderByDescending(l => l.CreateDate)
                        .FirstOrDefaultAsync();
                    lastActivated = activatedLog?.CreateDate;
                }

                if (deactivatedType != null)
                {
                    var deactivatedLog = await db.TblEventLogs
                        .Where(l => l.EventTypeId == deactivatedType.EventTypeId && l.Details != null && l.Details.Contains($"\"overrideId\":{over.OverrideId}"))
                        .OrderByDescending(l => l.CreateDate)
                        .FirstOrDefaultAsync();
                    lastDeactivated = deactivatedLog?.CreateDate;
                }

                result.Add(new OverrideStatusInfo
                {
                    OverrideId = over.OverrideId,
                    OverrideName = over.OverrideName,
                    Description = over.Description,
                    IsActive = over.IsActive,
                    IsScheduleActive = IsScheduleActive(over, DateTime.Now),
                    IsEffectivelyActive = IsEffectivelyActive(over, DateTime.Now),
                    Priority = over.Priority,
                    DeviceCount = over.OverrideDevices.Count,
                    LastActivated = lastActivated,
                    LastDeactivated = lastDeactivated
                });
            }

            return result;
        }

        /// <summary>
        /// <paramref name="localNow"/> is the local wall clock the schedule is judged against: the
        /// requesting device's zone where there is a device, and the server's clock on the
        /// operator-facing listings, which have no device to ask.
        /// </summary>
        private bool IsEffectivelyActive(TblOverride over, DateTime localNow)
        {
            // Either manual OR schedule can activate
            return over.IsActive || IsScheduleActive(over, localNow);
        }

        private bool IsScheduleActive(TblOverride over, DateTime localNow)
        {
            if (!over.IsScheduled)
                return false;

            var now = localNow;
            var currentTime = TimeOnly.FromDateTime(now);
            var currentDayOfWeek = now.DayOfWeek;

            // Check date range
            if (over.StartDate.HasValue && now.Date < over.StartDate.Value.Date)
                return false;
            if (over.EndDate.HasValue && now.Date > over.EndDate.Value.Date)
                return false;

            // Check day of week
            bool isDayActive = currentDayOfWeek switch
            {
                DayOfWeek.Sunday => over.Sunday,
                DayOfWeek.Monday => over.Monday,
                DayOfWeek.Tuesday => over.Tuesday,
                DayOfWeek.Wednesday => over.Wednesday,
                DayOfWeek.Thursday => over.Thursday,
                DayOfWeek.Friday => over.Friday,
                DayOfWeek.Saturday => over.Saturday,
                _ => false
            };

            if (!isDayActive)
                return false;

            // Check time range
            if (!over.StartTime.HasValue && !over.EndTime.HasValue)
                return true; // All day

            var startTime = over.StartTime ?? TimeOnly.MinValue;
            var endTime = over.EndTime ?? TimeOnly.MaxValue;

            // Handle overnight schedules (e.g., 21:00 - 03:00)
            if (startTime > endTime)
            {
                return currentTime >= startTime || currentTime <= endTime;
            }
            else
            {
                return currentTime >= startTime && currentTime <= endTime;
            }
        }

        #endregion

        #region Priority Management

        public async Task ReorderOverridesAsync(List<int> overrideIdsInOrder)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            for (int i = 0; i < overrideIdsInOrder.Count; i++)
            {
                var overrideId = overrideIdsInOrder[i];
                var over = await db.TblOverrides.FindAsync(overrideId);
                if (over != null)
                {
                    over.Priority = i;
                }
            }

            await db.SaveChangesAsync();

            _logger.LogInformation("Reordered {Count} overrides", overrideIdsInOrder.Count);
        }

        #endregion

        #region Resolution Detection

        public async Task<List<ResolutionInfo>> GetResolutionsForDevicesAsync(List<string> deviceIds)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            // Get devices with their displays (for resolution info)
            var devices = await db.TblDevices
                .Where(d => deviceIds.Contains(d.DeviceId))
                .Include(d => d.Display)
                .ToListAsync();

            // Group by resolution
            var resolutions = devices
                .Select(d => new
                {
                    Width = d.Display != null ? (int)d.Display.DisplayWidth : 1920,
                    Height = d.Display != null ? (int)d.Display.DisplayHeight : 1080
                })
                .GroupBy(r => new { r.Width, r.Height })
                .Select(g => new ResolutionInfo
                {
                    Width = g.Key.Width,
                    Height = g.Key.Height,
                    DeviceCount = g.Count()
                })
                .OrderByDescending(r => r.DeviceCount)
                .ToList();

            return resolutions;
        }

        #endregion

        #region Logging

        public async Task LogOverrideActionAsync(int overrideId, string? overrideName, string action, string triggerSource, string? user, string? ip, string? notes = null)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            // Map action to EventType
            var eventTypeName = action switch
            {
                "ACTIVATED" => "OVERRIDE_ACTIVATED",
                "DEACTIVATED" => "OVERRIDE_DEACTIVATED",
                _ => "OVERRIDE_ACTION"
            };

            var eventType = await db.TblEventTypes.FirstOrDefaultAsync(t => t.EventTypeName == eventTypeName);
            var infoSeverity = await db.TblEventSeverities.FirstOrDefaultAsync(s => s.SeverityName == "INFO");

            var log = new TblEventLog
            {
                EventSource = triggerSource,
                EventTypeId = eventType?.EventTypeId ?? 1,
                EventTitle = $"Override {action}: {overrideName}",
                EventMessage = notes,
                EventSeverityId = infoSeverity?.EventSeverityId ?? 1,
                CreateDate = DateTime.UtcNow,
                Details = System.Text.Json.JsonSerializer.Serialize(new
                {
                    overrideId = overrideId,
                    overrideName = overrideName,
                    action = action,
                    triggerSource = triggerSource,
                    triggerUser = user,
                    triggerIp = ip
                })
            };

            db.TblEventLogs.Add(log);
            await db.SaveChangesAsync();
        }

        #endregion

        #region Override Control Location

        public async Task<int> EnsureOverrideControlLocationExistsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            // Check if "Override Control" location already exists
            var existingLocation = await db.TblLocations
                .FirstOrDefaultAsync(l => l.LocationName == "Override Control");

            if (existingLocation != null)
                return existingLocation.LocationId;

            // Check if "Override Control" location type exists
            var locationType = await db.TblLocationTypes
                .FirstOrDefaultAsync(lt => lt.LocationTypeName == "Override Control");

            if (locationType == null)
            {
                // Create the location type
                locationType = new TblLocationType
                {
                    LocationTypeName = "Override Control",
                    LinkedToDisplay = false,
                    IsCustom = true,
                    CreateDate = DateTime.UtcNow
                };
                db.TblLocationTypes.Add(locationType);
                await db.SaveChangesAsync();
            }

            // Get the first location group
            var locationGroup = await db.TblLocationGroups.FirstOrDefaultAsync();
            if (locationGroup == null)
                throw new InvalidOperationException("No location groups exist in the database");

            // Create the "Override Control" location
            var newLocation = new TblLocation
            {
                LocationName = "Override Control",
                LocationTypeId = locationType.LocationTypeId,
                LocationGroupId = locationGroup.LocationGroupId,
                SortOrder = 0,
                CreateDate = DateTime.UtcNow
            };

            db.TblLocations.Add(newLocation);
            await db.SaveChangesAsync();

            _logger.LogInformation("Created Override Control location with ID {LocationId}", newLocation.LocationId);

            return newLocation.LocationId;
        }

        #endregion
    }
}
