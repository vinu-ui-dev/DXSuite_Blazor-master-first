using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DXSuite.Aviation.Entities;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Director.Models.BrightSign;
using DXSuite.Director.Models.TouchPanel;
using static DXSuite.Director.Models.Types;
using DXSuite.Director.Models.ZoneContent;
using DXSuite.Director.Services;
using DXSuite.Director.Authorization;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Services.Identity;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace DXSuite.Director.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SignageController : ControllerBase
    {
        private readonly IDirectorDBContext _dbContext;
        private readonly IHeartbeatService _heartbeatService;
        private readonly IOverrideService _overrideService;
        private readonly IActiveOverrideGate _overrideGate;
        private readonly IContentHealthService _contentHealthService;
        private readonly IDeviceCommandService _deviceCommands;
        private readonly IPlayerPollFlagCache _pollFlagCache;
        private readonly IManifestCacheService _manifestCache;
        private readonly IMediaFolderCacheService _mediaFolderCache;
        private readonly IMetadataCacheService _metadataCache;
        private readonly IScreenshotService _screenshotService;
        private readonly IConsoleLoggingService _consoleLoggingService;
        private readonly IDbContextFactory<DirectorDbContext> _dbContextFactory;
        private readonly IDeviceTokenService _deviceTokenService;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantMediaPathResolver _mediaPaths;
        private readonly IMediaAccessTokenService _mediaTokens;
        private readonly NowPlayingStore _nowPlaying;
        private readonly IContentManifestLogService _contentManifestLog;
        private readonly ILocationMetadataRuleService _locationMetadataRules;
        private readonly IMetadataSubjectResolver _metadataSubjects;
        private readonly DeliveryMetrics _deliveryMetrics;
        private readonly ILogger<SignageController> _logger;

        // Devices call in on their tenant's host, so absolute URLs handed back to them must be
        // built from the current request rather than a server-global configured base URL â€”
        // in multi-tenant deployments each tenant subdomain yields its own base.
        private string SiteUrl => $"{Request.Scheme}://{Request.Host}{Request.PathBase}";

        /// <summary>
        /// Base for every asset URL in a manifest, carrying the tenant's media access token.
        ///
        /// One property rather than a token threaded through each call site, because the player
        /// authenticates by fetching whatever URL the manifest hands it: getting the token into
        /// this one string is the whole client-side change, for both flavours, and there is none.
        ///
        /// Resolved once per request and cached on the instance, since a manifest can build
        /// hundreds of URLs.
        /// </summary>
        private string MediaUrl => _mediaUrlCache ??=
            $"{SiteUrl}/MediaAssets/{MediaAccessTokenService.PathMarker}/{_mediaTokens.GetOrCreateAsync().GetAwaiter().GetResult()}";

        private string? _mediaUrlCache;

        private string ResolvePublicationUrl(string url) => PublicationUrls.Resolve(url, SiteUrl, () => MediaUrl);

        public SignageController(
            IDirectorDBContext dbContext,
            IHeartbeatService heartbeatService,
            IOverrideService overrideService,
            IActiveOverrideGate overrideGate,
            IContentHealthService contentHealthService,
            IDeviceCommandService deviceCommands,
            IPlayerPollFlagCache pollFlagCache,
            IManifestCacheService manifestCache,
            IMediaFolderCacheService mediaFolderCache,
            IMetadataCacheService metadataCache,
            IScreenshotService screenshotService,
            IConsoleLoggingService consoleLoggingService,
            IDbContextFactory<DirectorDbContext> dbContextFactory,
            IDeviceTokenService deviceTokenService,
            ITenantContext tenantContext,
            ITenantMediaPathResolver mediaPaths,
            IMediaAccessTokenService mediaTokens,
            NowPlayingStore nowPlaying,
            IContentManifestLogService contentManifestLog,
            ILocationMetadataRuleService locationMetadataRules,
            IMetadataSubjectResolver metadataSubjects,
            DeliveryMetrics deliveryMetrics,
            ILogger<SignageController> logger)
        {
            _dbContext = dbContext;
            _heartbeatService = heartbeatService;
            _overrideService = overrideService;
            _overrideGate = overrideGate;
            _contentHealthService = contentHealthService;
            _deviceCommands = deviceCommands;
            _pollFlagCache = pollFlagCache;
            _manifestCache = manifestCache;
            _mediaFolderCache = mediaFolderCache;
            _metadataCache = metadataCache;
            _screenshotService = screenshotService;
            _consoleLoggingService = consoleLoggingService;
            _dbContextFactory = dbContextFactory;
            _deviceTokenService = deviceTokenService;
            _tenantContext = tenantContext;
            _mediaPaths = mediaPaths;
            _mediaTokens = mediaTokens;
            _nowPlaying = nowPlaying;
            _contentManifestLog = contentManifestLog;
            _locationMetadataRules = locationMetadataRules;
            _metadataSubjects = metadataSubjects;
            _deliveryMetrics = deliveryMetrics;
            _logger = logger;
        }

        [HttpGet]
        [Route("Displays")]
        [Authorize(AuthenticationSchemes = AuthSchemes.DeviceToken + "," + AuthSchemes.AdminApiKey)]
        public async Task<LocationDisplayResponse> GetDisplays([FromQuery] int? locationId = null)
        {
            LocationDisplayResponse displayResponse = new LocationDisplayResponse();
            if (locationId != null)
            {
                DXSuite.Director.Entities.TblLocation? location = await _dbContext.TblLocations.FirstOrDefaultAsync(x => x.LocationId == locationId);
                if (location != null)
                {
                    displayResponse.LocationId = location.LocationId;
                    displayResponse.LocationName = location.LocationName;
                    displayResponse.LocationTypeId = location.LocationTypeId;
                }

                // The clock follows the authentication scheme, and this is the only endpoint that
                // accepts both. A device token means a device is in hand and its schedules resolve
                // against that device's zone; an API key is an integration with no device and no
                // place, so it keeps the server's clock by decision rather than by default.
                var localNow = await ResolveCallerLocalNowAsync();

                List<TblDisplay> displayList = await _dbContext.TblDisplays.Where(x => x.LocationId == locationId).Include(x => x.TblScheduleEvents).Include(x => x.Endpoint).ToListAsync();
                foreach (TblDisplay display in displayList)
                {
                    DisplayItem displayItem = new DisplayItem(display);

                    // A device whose zone will not resolve gets the display's own default event,
                    // the same answer the player path gives. Substituting the server's clock here
                    // would be the silent wrong-but-plausible content this work exists to remove.
                    var scheduleEvent = localNow is { } moment
                        ? displayItem.GetActiveEvent(display.TblScheduleEvents, moment)
                        : display.TblScheduleEvents.FirstOrDefault(e => e.DefaultContent);

                    displayItem.Content = await displayItem.GetContentAssignmentForEventAsync(
                        display, scheduleEvent, _dbContext, SiteUrl);
                    displayResponse.Displays.Add(displayItem);
                }
            }

            return displayResponse;
        }

        [HttpGet]
        [Route("GetAirline")]
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<PinCodeResponse> GetAirline([FromQuery] int PinCode, int DisplayId)
        {
            var aviationDbFactory = HttpContext.RequestServices.GetService<IDbContextFactory<AviationDbContext>>();
            if (aviationDbFactory == null) { Response.StatusCode = StatusCodes.Status404NotFound; return null!; }
            using var coreDb = await aviationDbFactory.CreateDbContextAsync();

            var adminPinCode = await _dbContext.TblParameters
                .Where(x => x.KeyName == "PinCode")
                .Select(x => x.Value)
                .FirstOrDefaultAsync();

            PinCodeResponse response = new PinCodeResponse();
            if (PinCode != 0)
            {
                //Moxie Script
                if (PinCode == 97531)
                {
                    response.AirlineList = (await coreDb.TblAirlines.Where(x => x.Tpenabled1 == true || x.Tpenabled2 == true).ToListAsync()).Select(AirlineInfo.FromEntity).ToList();
                    response.IsPinCodeValid = true;
                    response.ResponseMessage = "For Script Access Only, Do Not Use This Login.";
                    return response;
                }
                //Admin User
                else if (adminPinCode is not null && PinCode.ToString() == adminPinCode)
                {
                    response.IsAdminUser = true;
                    if (DisplayId == 1)
                    {
                        response.AirlineList = (await coreDb.TblAirlines.Where(x => x.Tpenabled1 == true).ToListAsync()).Select(AirlineInfo.FromEntity).ToList();
                    }
                    else if (DisplayId == 2)
                    {
                        response.AirlineList = (await coreDb.TblAirlines.Where(x => x.Tpenabled2 == true).ToListAsync()).Select(AirlineInfo.FromEntity).ToList();
                    }
                    else
                    {
                        response.IsPinCodeValid = false;
                        response.ResponseMessage = "Missing DisplayId, Please Try Again.";
                        return response;
                    }

                    response.IsPinCodeValid = true;
                    response.ResponseMessage = "Welcome Admin User";
                }
                else
                {
                    TblAirline? airline = await coreDb.TblAirlines.FirstOrDefaultAsync(x => x.Pincode == PinCode);
                    if (airline != null)
                    {
                        response.Airline = AirlineInfo.FromEntity(airline);

                        response.IsPinCodeValid = true;
                        response.ResponseMessage = String.Format("Welcome {0} User", airline.CarrierName);
                    }
                    else
                    {
                        response.IsPinCodeValid = false;
                        response.ResponseMessage = "Incorrect Pin, Please Try Again.";
                        return response;
                    }
                }
            }
            else
            {
                response.IsPinCodeValid = false;
                response.ResponseMessage = "Please Use Pinpad to Input PinCode.";
            }

            return response;
        }

        [HttpPost]
        [Route("SetAirlineState")]
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<AirlineStateResponse> SetAirlineState([FromQuery] int PinCode, int AirlineRecordId, int AirlineState, int DisplayId)
        {
            var aviationDbFactory = HttpContext.RequestServices.GetService<IDbContextFactory<AviationDbContext>>();
            if (aviationDbFactory == null) { Response.StatusCode = StatusCodes.Status404NotFound; return null!; }
            using var coreDb = await aviationDbFactory.CreateDbContextAsync();

            var adminPinCode = await _dbContext.TblParameters
                .Where(x => x.KeyName == "PinCode")
                .Select(x => x.Value)
                .FirstOrDefaultAsync();

            AirlineStateResponse response = new AirlineStateResponse();
            if (PinCode != 0)
            {
                //Admin User
                if (adminPinCode is not null && PinCode.ToString() == adminPinCode)
                {
                    response.IsAdminUser = true;
                    response.IsPinCodeValid = true;

                    TblAirline? airline = await coreDb.TblAirlines.FirstOrDefaultAsync(x => x.RecordId == AirlineRecordId);
                    if (airline != null)
                    {
                        if (DisplayId == 1)
                        {
                            airline.CarrierStatus1 = AirlineState;
                        }
                        else if (DisplayId == 2)
                        {
                            airline.CarrierStatus2 = AirlineState;
                        }
                        else
                        {
                            response.ResponseMessage = "DisplayId Not Sent, Please Try Again.";
                            return response;
                        }

                        await coreDb.SaveChangesAsync();

                        response.Airline = AirlineInfo.FromEntity(airline);
                        response.ResponseMessage = "Change Completed Successfully!";
                    }
                    response.ResponseMessage = "Airline Not Found";
                }
                else
                {
                    TblAirline? airline = await coreDb.TblAirlines.FirstOrDefaultAsync(x => x.Pincode == PinCode);
                    if (airline != null)
                    {

                        if (airline.RecordId == AirlineRecordId)
                        {
                            if (DisplayId == 1)
                            {
                                airline.CarrierStatus1 = AirlineState;
                            } else if (DisplayId == 2)
                            {
                                airline.CarrierStatus2 = AirlineState;
                            }
                            else
                            {
                                response.ResponseMessage = "DisplayId Not Sent, Please Try Again.";
                                return response;
                            }
                        }

                        await coreDb.SaveChangesAsync();

                        response.Airline = AirlineInfo.FromEntity(airline);

                        response.IsPinCodeValid = true;
                        response.ResponseMessage = "Change Completed Successfully!";
                    }
                    else
                    {
                        response.IsPinCodeValid = false;
                        response.ResponseMessage = "Incorrect PinCode, Please Try Again.";
                    }
                }
            }
            else
            {
                response.IsPinCodeValid = false;
                response.ResponseMessage = "Please Use Pinpad to Input PinCode.";
            }

            return response;
        }

        [HttpGet]
        [Route("GetAirlineStatus")]
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<AirlineStatusListResponse> GetAirlineStatus()
        {
            var aviationDbFactory = HttpContext.RequestServices.GetService<IDbContextFactory<AviationDbContext>>();
            if (aviationDbFactory == null) { Response.StatusCode = StatusCodes.Status404NotFound; return null!; }
            using var coreDb = await aviationDbFactory.CreateDbContextAsync();
            AirlineStatusListResponse response = new AirlineStatusListResponse();
            List<TblAirline> airlineList = await coreDb.TblAirlines.Where(x => x.Tpenabled1 == true || x.Tpenabled2 == true).ToListAsync();
            foreach (TblAirline airline in airlineList)
            {
                response.Airlines.Add(new AirlineStatus(AirlineInfo.FromEntity(airline)));
            }

            return response;
        }

        [HttpGet]
        [Route("GetMessages")]
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<IActionResult> GetMessages([FromQuery] int? displayId = null)
        {
            if (this.CheckScope(ApiKeyScopes.SignageRead) is { } denied) return denied;

            // Get CBP message schedule events (ScheduleEventTypeId = 3)
            var query = _dbContext.TblScheduleEvents.Where(x => x.ScheduleEventTypeId == 3);

            if (displayId.HasValue)
            {
                query = query.Where(x => x.DisplayId == displayId.Value);
            }

            var scheduleEvents = await query.ToListAsync();

            var response = new
            {
                DisplayId = displayId,
                Messages = scheduleEvents.Select(se => new
                {
                    ScheduleEventId = se.ScheduleEventId,
                    DisplayId = se.DisplayId,
                    ContentAssignment = se.ContentAssignment,
                    StartDate = se.StartDate,
                    EndDate = se.EndDate,
                    StartTime = se.StartTime,
                    EndTime = se.EndTime,
                    DefaultContent = se.DefaultContent,
                    Sunday = se.Sunday,
                    Monday = se.Monday,
                    Tuesday = se.Tuesday,
                    Wednesday = se.Wednesday,
                    Thursday = se.Thursday,
                    Friday = se.Friday,
                    Saturday = se.Saturday,
                    CreateDate = se.CreateDate
                }).ToList()
            };

            return Ok(response);
        }

        [HttpPost]
        [Route("PlayerContent")]
        [Authorize(AuthenticationSchemes = AuthSchemes.DeviceToken)]
        [ServiceFilter(typeof(Filters.PlayerPollIntervalFilter))]
        [ServiceFilter(typeof(Filters.PlayerOsTimeZoneFilter))]
        [ServiceFilter(typeof(Filters.PlayerOrientationFilter))]
        [ServiceFilter(typeof(Filters.PlayerVideoPathFilter))]
        public async Task<IActionResult> GetPlayerContent([FromBody] PlayerHeartbeatModel payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.DeviceId))
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return null!;
            }

            // A token speaks for exactly one device once it is bound. Without this the DeviceId in
            // the body decided whose content came back, and the token was never consulted about it.
            if (!DeviceTokenBinding.Matches(HttpContext, payload.DeviceId))
            {
                _logger.LogWarning("Device token bound to {BoundDeviceId} requested content for {RequestedDeviceId}",
                    HttpContext.Items["DeviceId"], payload.DeviceId);
                Response.StatusCode = StatusCodes.Status403Forbidden;
                return null!;
            }

            _heartbeatService.QueueHeartbeat(_tenantContext.Subdomain, payload);

            // Heartbeat fallback for players without a live wake socket
            if (payload.CurrentAsset != null)
            {
                _nowPlaying.Report(_tenantContext.TenantId, payload.DeviceId, payload.CurrentAsset, "heartbeat");
            }

            // Bind the device token to this device on first heartbeat after pairing.
            // Context.Items["DeviceId"] is the DeviceId stored on the token (null until bound).
            // Fire-and-forget; BindToDeviceAsync skips silently if TblDevice doesn't exist yet.
            var boundDeviceId = HttpContext.Items["DeviceId"] as string;
            if (string.IsNullOrEmpty(boundDeviceId)
                && HttpContext.Items["DeviceTokenId"] is string rawTokenId
                && int.TryParse(rawTokenId, out var tokenId))
            {
                _ = _deviceTokenService.BindToDeviceAsync(tokenId, payload.DeviceId);
            }

            // Process content health data (fire-and-forget, don't block response)
            if (payload.ContentHealthEvent != null)
            {
                _ = _contentHealthService.RecordEventAsync(payload.ContentHealthEvent, payload.DeviceId);
                _logger.LogInformation(
                    "Content health event: Device {DeviceId} - {Status}: {Reason}",
                    payload.DeviceId, payload.ContentHealthEvent.Status, payload.ContentHealthEvent.Reason);
            }

            if (payload.ContentHealth != null)
            {
                _ = _contentHealthService.UpdateHealthStatusAsync(payload.DeviceId, payload.ContentHealth);
            }

            var deviceRecord = await _dbContext.TblDevices
                .Include(d => d.Display)
                .FirstOrDefaultAsync(d => d.DeviceId == payload.DeviceId);

            if (DeviceProvisioning.IsOperatorConfirmed(deviceRecord?.TimeZoneSource))
            {
                var setTimeZoneArgument = TimeZoneCatalog.FindSetTimeZoneArgument(deviceRecord!.TimeZone);
                if (setTimeZoneArgument != null)
                    HttpContext.Items[Filters.PlayerOsTimeZoneFilter.ContextItemKey] = setTimeZoneArgument;
            }
            if (deviceRecord == null)
            {
                return Ok(new PlayerContentResponse
                {
                    LocationId = null,
                    Status = "Device not registered",
                    Assets = new List<PlayerContentItem>(),
                    // A light beat cannot auto-register; ask for the full inventory next poll
                    RequestFullInventory = true,
                });
            }
            HttpContext.Items[Filters.PlayerOrientationFilter.ContextItemKey] = DeviceOrientation.ToWidgetTransform(deviceRecord.Orientation);
            HttpContext.Items[Filters.PlayerVideoPathFilter.ContextItemKey] = DeviceVideoPath.ToPlayerValue(deviceRecord.VideoPath, deviceRecord.Model);

            // Slow-changing per-device data (metadata, HTTP-fallback setting, update check)
            // comes from a short-TTL cache instead of fresh queries every poll
            var pollFlags = await _pollFlagCache.GetAsync(payload.DeviceId, payload.ClientAppVersion, payload.ClientBootloaderVersion);
            var deviceMetadata = pollFlags.DeviceMetadata;
            var hasMetadata = deviceMetadata is { Count: > 0 } ? deviceMetadata : null;
            var updateInfo = pollFlags.Update;
            var bootloaderUpdateInfo = pollFlags.BootloaderUpdate;

            // Drain the durable command queue: expired rows are marked, the rest are
            // marked Delivered and carried on this response
            CecCommandInfo? cecCommand = null;
            ScreenshotCommandInfo? screenshotCommand = null;
            var flushCache = false;
            foreach (var pendingCommand in await _deviceCommands.DequeuePendingAsync(payload.DeviceId))
            {
                switch (pendingCommand.CommandType)
                {
                    case DeviceCommandTypes.Cec:
                        var cecPayload = System.Text.Json.JsonSerializer.Deserialize<CecCommandPayload>(pendingCommand.PayloadJson ?? "{}");
                        if (cecPayload != null)
                        {
                            cecCommand = new CecCommandInfo { Action = cecPayload.Action, Output = cecPayload.Output };
                            _logger.LogInformation("CEC command included for device {DeviceId}: {Action}",
                                payload.DeviceId, cecPayload.Action);
                        }
                        break;
                    case DeviceCommandTypes.Screenshot:
                        screenshotCommand = new ScreenshotCommandInfo
                        {
                            RequestId = pendingCommand.RequestId ?? string.Empty,
                            RequestedAt = new DateTimeOffset(pendingCommand.IssuedAt).ToUnixTimeMilliseconds()
                        };
                        _logger.LogInformation("Screenshot command included for device {DeviceId}, RequestId: {RequestId}",
                            payload.DeviceId, pendingCommand.RequestId);
                        break;
                    case DeviceCommandTypes.FlushCache:
                        flushCache = true;
                        _logger.LogInformation("Flush cache command included for device {DeviceId}", payload.DeviceId);
                        break;
                }
            }

            // Read per-device overrides from ExtendedData. The console-logging flag lives in
            // the same blob, so it comes from the device row already loaded above
            var extData = ExtendedDataHelper.Parse<DeviceExtendedData>(deviceRecord.ExtendedData);
            var consoleLoggingEnabled = extData?.ConsoleLogging?.Enabled ?? false;
            var forceHtml5Video = extData?.VideoMode?.ForceHtml5 == true;

            // Build window mode payload for Avalonia players; null for all others (they ignore it)
            var rawWindowMode = extData?.WindowMode;
            WindowModeResponseData? windowModeResponse = rawWindowMode == null ? null : new WindowModeResponseData
            {
                Mode = rawWindowMode.Mode,
                X = rawWindowMode.X,
                Y = rawWindowMode.Y,
                Width = rawWindowMode.Width,
                Height = rawWindowMode.Height
            };

            var allowHttpFallback = pollFlags.AllowHttpFallback;

            // Store any console logs sent by the device
            if (payload.ConsoleLogs != null && payload.ConsoleLogs.Count > 0)
            {
                _ = _consoleLoggingService.StoreLogsAsync(payload.DeviceId, payload.ConsoleLogs);
            }

            // Check for active override FIRST. The gate skips the per-device query for
            // tenants with no active and no scheduled overrides (the common case)
            TblOverride? activeOverride = null;
            if (await _overrideGate.HasOverridesAsync(_dbContext, _tenantContext.TenantId))
            {
                activeOverride = await _overrideService.GetActiveOverrideForDeviceAsync(payload.DeviceId);
            }
            if (activeOverride != null)
            {
                _logger.LogInformation("Active override found for device {DeviceId}: {OverrideName} (ID: {OverrideId})",
                    payload.DeviceId, activeOverride.OverrideName, activeOverride.OverrideId);

                var overrideContent = await GetOverrideContentForDevice(activeOverride, payload.DeviceId, deviceRecord);
                if (overrideContent != null)
                {
                    _logger.LogInformation("Override content found for device {DeviceId}: {Url}",
                        payload.DeviceId, overrideContent.Url);

                    return Ok(new PlayerContentResponse
                    {
                        LocationId = deviceRecord.LocationId,
                        DisplayId = deviceRecord.DisplayId,
                        Status = $"Override: {activeOverride.OverrideName}",
                        IsOverride = true,
                        OverrideId = activeOverride.OverrideId,
                        OverrideName = activeOverride.OverrideName,
                        Assets = new List<PlayerContentItem> { overrideContent },
                        Update = updateInfo,
                BootloaderUpdate = bootloaderUpdateInfo,
                        CecCommand = cecCommand,
                        ScreenshotCommand = screenshotCommand,
                        DeviceMetadata = hasMetadata,
                        ConsoleLoggingEnabled = consoleLoggingEnabled,
                        ForceHtml5Video = forceHtml5Video,
                        FlushCache = flushCache,
                        AllowHttpFallback = allowHttpFallback,
                        WindowMode = windowModeResponse
                    });
                }
                else
                {
                    _logger.LogWarning("Override {OverrideName} found for device {DeviceId} but no matching content. Device resolution: {Width}x{Height}",
                        activeOverride.OverrideName, payload.DeviceId,
                        deviceRecord.Display?.DisplayWidth ?? 1920, deviceRecord.Display?.DisplayHeight ?? 1080);
                }
                // If no content configured for this device's resolution, fall through to normal content
            }

            var displayId = deviceRecord.DisplayId;
            var locationId = deviceRecord.LocationId;

            if (locationId == null && displayId == null)
            {
                return Ok(new PlayerContentResponse
                {
                    LocationId = null,
                    Status = "No location or display assigned",
                    Assets = new List<PlayerContentItem>(),
                    Update = updateInfo,
                BootloaderUpdate = bootloaderUpdateInfo,
                    CecCommand = cecCommand,
                    ScreenshotCommand = screenshotCommand,
                    DeviceMetadata = hasMetadata,
                    ConsoleLoggingEnabled = consoleLoggingEnabled,
                    ForceHtml5Video = forceHtml5Video,
                    FlushCache = flushCache,
                    AllowHttpFallback = allowHttpFallback,
                    WindowMode = windowModeResponse
                });
            }
            else if (displayId is not null)
            {
                // Check manifest cache first (keyed by displayId)
                if (_manifestCache.TryGetManifest(_tenantContext.TenantId, ManifestBucket.ForDisplay(displayId.Value), out var cachedDisplay))
                {
                    _logger.LogDebug("Manifest cache hit for display {DisplayId}", displayId);
                    var cachedDisplayDelivery = ResolveDisplayDelivery(cachedDisplay!, deviceRecord, payload.DeviceId);
                    return Ok(BuildResponseFromCache(cachedDisplayDelivery, payload.ManifestVersion, updateInfo, bootloaderUpdateInfo, cecCommand, screenshotCommand, hasMetadata, consoleLoggingEnabled, forceHtml5Video, flushCache, allowHttpFallback, windowModeResponse));
                }

                var display = await _dbContext.TblDisplays
                    .Include(d => d.TblScheduleEvents)
                    .FirstOrDefaultAsync(d => d.DisplayId == displayId);

                if (display == null)
                {
                    return Ok(new PlayerContentResponse
                    {
                        DisplayId = displayId,
                        Status = "Display not found",
                        Assets = new List<PlayerContentItem>(),
                        Update = updateInfo,
                BootloaderUpdate = bootloaderUpdateInfo,
                        CecCommand = cecCommand,
                        ScreenshotCommand = screenshotCommand,
                        DeviceMetadata = hasMetadata,
                        ConsoleLoggingEnabled = consoleLoggingEnabled,
                        ForceHtml5Video = forceHtml5Video,
                        FlushCache = flushCache,
                        AllowHttpFallback = allowHttpFallback,
                        WindowMode = windowModeResponse
                    });
                }

                // The publication hydrates every schedule event, not just the one that happens to
                // be active for whoever asked first. Which event is on depends on the requesting
                // device's clock; loading the assets does not, and that is the expensive half.
                var displayManifest = new CachedManifest
                {
                    DisplayId = displayId,
                    Status = "Scheduled playlist",
                    EnableFade = display.EnableFade,
                    DisplayEvents = await BuildDisplayPublicationAsync(display)
                };
                _manifestCache.SetManifest(_tenantContext.TenantId, ManifestBucket.ForDisplay(displayId.Value), displayManifest);
                _manifestCache.SetDisplaySources(_tenantContext.TenantId, displayId.Value,
                    displayManifest.DisplayEvents!
                        .SelectMany(e => e.Assets)
                        .Where(a => a.LocationId.HasValue)
                        .Select(a => a.LocationId!.Value));
                await _contentManifestLog.RecordIfChangedAsync("display", displayId.Value, displayManifest);

                var displayDelivery = ResolveDisplayDelivery(displayManifest, deviceRecord, payload.DeviceId);
                return Ok(BuildResponseFromCache(displayDelivery, payload.ManifestVersion, updateInfo, bootloaderUpdateInfo, cecCommand, screenshotCommand, hasMetadata, consoleLoggingEnabled, forceHtml5Video, flushCache, allowHttpFallback, windowModeResponse));
            }
            else
            {
                var locationRecord = await _dbContext.TblLocations.FirstOrDefaultAsync(d => d.LocationId == locationId);

                // Handle zone-based locations (LocationTypeId 1014)
                // These locations have a layout URL in ExtendedData that devices should load
                if (locationRecord?.LocationTypeId == 1014)
                {
                    string? layoutUrl = null;

                    // Parse ExtendedData JSON to get layoutUrl
                    if (!string.IsNullOrEmpty(locationRecord.ExtendedData))
                    {
                        try
                        {
                            var extendedData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(locationRecord.ExtendedData);
                            if (extendedData != null && extendedData.TryGetValue("layoutUrl", out var urlElement))
                            {
                                layoutUrl = urlElement.GetString();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("Failed to parse ExtendedData for location {LocationId}: {Error}", locationId, ex.Message);
                        }
                    }

                    if (!string.IsNullOrEmpty(layoutUrl))
                    {
                        // Append deviceId to the URL for device identification
                        var urlWithParams = AppendDeviceIdToUrl(layoutUrl, payload.DeviceId);

                        // Also append any device metadata as URL parameters
                        if (hasMetadata != null)
                        {
                            foreach (var kvp in hasMetadata)
                            {
                                urlWithParams += $"&{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}";
                            }
                        }

                        return Ok(new PlayerContentResponse
                        {
                            LocationId = locationId,
                            Status = "Zone layout",
                            Assets = new List<PlayerContentItem>
                            {
                                new PlayerContentItem
                                {
                                    FileName = null,
                                    Url = urlWithParams,
                                    Duration = 0, // Web content runs indefinitely
                                    LocationId = locationId,
                                    IsWeb = true,
                                    WebRenderMode = "iframe"
                                }
                            },
                            Update = updateInfo,
                BootloaderUpdate = bootloaderUpdateInfo,
                            CecCommand = cecCommand,
                            ScreenshotCommand = screenshotCommand,
                            DeviceMetadata = hasMetadata,
                            ConsoleLoggingEnabled = consoleLoggingEnabled,
                            ForceHtml5Video = forceHtml5Video,
                            FlushCache = flushCache,
                            AllowHttpFallback = allowHttpFallback,
                            WindowMode = windowModeResponse
                        });
                    }
                    else
                    {
                        return Ok(new PlayerContentResponse
                        {
                            LocationId = locationId,
                            Status = "Zone layout URL not configured",
                            Assets = new List<PlayerContentItem>(),
                            Update = updateInfo,
                BootloaderUpdate = bootloaderUpdateInfo,
                            CecCommand = cecCommand,
                            ScreenshotCommand = screenshotCommand,
                            DeviceMetadata = hasMetadata,
                            ConsoleLoggingEnabled = consoleLoggingEnabled,
                            ForceHtml5Video = forceHtml5Video,
                            FlushCache = flushCache,
                            AllowHttpFallback = allowHttpFallback,
                            WindowMode = windowModeResponse
                        });
                    }
                }

                // Handle Layout locations (LocationTypeId 1015)
                // These locations compose multiple Playlist/Card locations into positioned zones
                if (locationRecord?.LocationTypeId == 1015)
                {
                    // One publication per layout, shared by every device: each source location's
                    // metadata filter is applied per device at delivery, never baked in here.
                    if (_manifestCache.TryGetManifest(_tenantContext.TenantId, ManifestBucket.ForLocation(locationId!.Value), out var cachedLayout))
                    {
                        _logger.LogDebug("Manifest cache hit for layout {LocationId}", locationId);
                        var cachedLayoutDelivery = await ResolveLayoutDeliveryAsync(cachedLayout!, deviceRecord, payload.DeviceId, deviceMetadata);
                        return Ok(BuildResponseFromCache(cachedLayoutDelivery, payload.ManifestVersion, updateInfo, bootloaderUpdateInfo, cecCommand, screenshotCommand, hasMetadata, consoleLoggingEnabled, forceHtml5Video, flushCache, allowHttpFallback, windowModeResponse));
                    }

                    var zones = await BuildLayoutZonesAsync(locationRecord, locationId);

                    // Calculate layout design dimensions from zone positions
                    // The design dimensions are the bounding box containing all zones
                    int? layoutWidth = null;
                    int? layoutHeight = null;
                    var sourceLocationIds = new List<int>();
                    if (!string.IsNullOrEmpty(locationRecord.ExtendedData))
                    {
                        try
                        {
                            var layoutConfig = JsonSerializer.Deserialize<LayoutConfiguration>(locationRecord.ExtendedData, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });
                            if (layoutConfig?.Zones != null && layoutConfig.Zones.Count > 0)
                            {
                                // Calculate bounding box from zone positions (all values in pixels)
                                decimal maxRight = 0;
                                decimal maxBottom = 0;
                                foreach (var zone in layoutConfig.Zones)
                                {
                                    var right = zone.Left + zone.Width;
                                    var bottom = zone.Top + zone.Height;
                                    if (right > maxRight) maxRight = right;
                                    if (bottom > maxBottom) maxBottom = bottom;
                                }
                                layoutWidth = (int)maxRight;
                                layoutHeight = (int)maxBottom;
                                sourceLocationIds = layoutConfig.Zones
                                    .Where(z => !z.SourceDisplayId.HasValue)
                                    .Select(z => z.SourceLocationId)
                                    .Distinct()
                                    .ToList();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to parse layout config for dimensions");
                        }
                    }

                    // Cache the layout manifest. Card zones are left for delivery to resolve against
                    // the asking device's clock, so the publication itself reads no clock.
                    var layoutManifest = new CachedManifest
                    {
                        LocationId = locationId,
                        Status = "Layout",
                        EnableFade = locationRecord.EnableFade,
                        Assets = new List<PlayerContentItem>(),
                        Zones = zones,
                        LayoutWidth = layoutWidth,
                        LayoutHeight = layoutHeight
                    };
                    _manifestCache.SetManifest(_tenantContext.TenantId, ManifestBucket.ForLocation(locationId!.Value), layoutManifest);
                    _manifestCache.SetLayoutSources(_tenantContext.TenantId, locationId.Value, sourceLocationIds);

                    var layoutDelivery = await ResolveLayoutDeliveryAsync(layoutManifest, deviceRecord, payload.DeviceId, deviceMetadata);
                    return Ok(BuildResponseFromCache(layoutDelivery, payload.ManifestVersion, updateInfo, bootloaderUpdateInfo, cecCommand, screenshotCommand, hasMetadata, consoleLoggingEnabled, forceHtml5Video, flushCache, allowHttpFallback, windowModeResponse));
                }

                var enableContentApproval = locationRecord?.EnableContentApproval ?? false;

                // One publication per location, shared by every device there. Which tagged assets
                // reach this device is decided at delivery, against the location's current rules.
                var targeting = await _locationMetadataRules.GetTargetingAsync(locationId!.Value);
                if (_manifestCache.TryGetManifest(_tenantContext.TenantId, ManifestBucket.ForLocation(locationId.Value), out var cachedPlaylist))
                {
                    _logger.LogDebug("Manifest cache hit for playlist {LocationId}", locationId);
                    var cachedDelivery = ResolvePlaylistDelivery(cachedPlaylist!, targeting, deviceRecord, deviceMetadata, locationRecord);
                    return Ok(BuildResponseFromCache(cachedDelivery, payload.ManifestVersion, updateInfo, bootloaderUpdateInfo, cecCommand, screenshotCommand, hasMetadata, consoleLoggingEnabled, forceHtml5Video, flushCache, allowHttpFallback, windowModeResponse));
                }

                // Use cached file list instead of Directory.GetFiles on every request
                var files = _mediaFolderCache.GetMediaFiles(_tenantContext.TenantId, locationId.Value);

                if (files.Length == 0)
                {
                    return Ok(new PlayerContentResponse
                    {
                        LocationId = locationId,
                        Status = "No media folder",
                        Assets = new List<PlayerContentItem>(),
                        Update = updateInfo,
                BootloaderUpdate = bootloaderUpdateInfo,
                        CecCommand = cecCommand,
                        ScreenshotCommand = screenshotCommand,
                        DeviceMetadata = hasMetadata,
                        ConsoleLoggingEnabled = consoleLoggingEnabled,
                        ForceHtml5Video = forceHtml5Video,
                        FlushCache = flushCache,
                        AllowHttpFallback = allowHttpFallback,
                        WindowMode = windowModeResponse
                    });
                }

                var mediaFolder = Path.Combine(_mediaPaths.GetRoot(), "MediaAssets", locationId.ToString());
                var metadataParser = new MetadataParser();
                var metadataMap = metadataParser.ParseAllMetadata(mediaFolder);

                // Publish files by approval status, carrying every tag each asset holds so delivery can
                // target any key the location's rules name. Two queries for the location, not two per file.
                var assetRecords = await _metadataSubjects.GetLocationAssetRecordsAsync(locationId.Value);
                var filteredFiles = PlaylistManifestRules.SelectPublished(files, locationId.Value, assetRecords, enableContentApproval);

                var assetTags = await _metadataSubjects.GetAssetValuesAsync(
                    filteredFiles.Where(f => f.Record != null).Select(f => f.Record!.AssetId).ToList());
                IReadOnlyDictionary<string, IReadOnlySet<string>>? TagsFor(LocationAssetRecord? record) =>
                    record == null ? null : assetTags[record.AssetId];

                // Build asset list with async file reads for JSON content
                var assetList = new List<PlayerContentItem>();
                foreach (var (file, record) in filteredFiles)
                {
                    var fileName = Path.GetFileName(file);
                    var fileExtension = Path.GetExtension(file).ToLower();

                    if (fileExtension == ".json")
                    {
                        try
                        {
                            var jsonContent = await System.IO.File.ReadAllTextAsync(file);
                            var webContent = JsonSerializer.Deserialize<WebContentDefinition>(jsonContent);

                            // File-based web content does not support health monitoring
                            // deviceId is not appended - use database assets for health monitoring

                            assetList.Add(new PlayerContentItem
                            {
                                FileName = null,
                                Url = webContent?.Url,
                                Duration = webContent?.Duration ?? 7,
                                LocationId = locationId,
                                StartDate = webContent?.StartDate,
                                EndDate = webContent?.EndDate,
                                StartTime = webContent?.StartTime,
                                EndTime = webContent?.EndTime,
                                IsWeb = true,
                                PlayOrder = GetPlayOrderFromMetadata(file),
                                WebRenderMode = "iframe",
                                MetadataValues = TagsFor(record)
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error parsing JSON file {FileName}", fileName);
                        }
                        continue;
                    }

                    PlayerContentItem item;
                    if (metadataMap.TryGetValue(fileName, out var meta))
                    {
                        meta.Url = $"{PublicationUrls.MediaBase}/{locationId}/{Uri.EscapeDataString(fileName)}";
                        meta.LocationId = locationId;
                        meta.PlayOrder = GetPlayOrderFromMetadata(file);
                        meta.MetadataValues = TagsFor(record);
                        item = meta;
                    }
                    else
                    {
                        item = new PlayerContentItem
                        {
                            FileName = fileName,
                            Url = $"{PublicationUrls.MediaBase}/{locationId}/{Uri.EscapeDataString(fileName)}",
                            LocationId = locationId,
                            StartDate = null,
                            EndDate = null,
                            StartTime = null,
                            EndTime = null,
                            PlayOrder = GetPlayOrderFromMetadata(file),
                            MetadataValues = TagsFor(record)
                        };
                    }

                    assetList.Add(item);
                }

                // What gets cached is the publication: the assembled list before anything that reads
                // a clock or a player. The media folder walk, sidecar parsing and the asset reads are
                // the expensive part and none of them vary by time of day or device, so they are
                // shared by every device at this location.
                var playlistManifest = new CachedManifest
                {
                    LocationId = locationId,
                    Status = "Success",
                    Shuffle = locationRecord?.EnableShuffle ?? false,
                    EnableFade = locationRecord?.EnableFade ?? true,
                    Assets = assetList
                };
                _manifestCache.SetManifest(_tenantContext.TenantId, ManifestBucket.ForLocation(locationId!.Value), playlistManifest);

                // The log records the publication. A window or day boundary is not a content change
                // and must not read as one on a table with 400-day retention.
                await _contentManifestLog.RecordIfChangedAsync("location", locationId.Value, playlistManifest);

                var playlistDelivery = ResolvePlaylistDelivery(playlistManifest, targeting, deviceRecord, deviceMetadata, locationRecord);
                return Ok(BuildResponseFromCache(playlistDelivery, payload.ManifestVersion, updateInfo, bootloaderUpdateInfo, cecCommand, screenshotCommand, hasMetadata, consoleLoggingEnabled, forceHtml5Video, flushCache, allowHttpFallback, windowModeResponse));
            }
        }

        /// <summary>
        /// The local wall clock this request evaluates schedules against, or null when the caller
        /// is a device whose stored time zone will not resolve. An API-key caller has no device and
        /// no place, so it gets the server's clock: that is the rule for an outside integration,
        /// not a fallback for a device.
        /// </summary>
        private async Task<DateTime?> ResolveCallerLocalNowAsync()
        {
            if (!string.Equals(User.Identity?.AuthenticationType, AuthSchemes.DeviceToken, StringComparison.Ordinal))
                return DateTime.Now;

            var deviceId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(deviceId))
                return null;

            var timeZoneId = await _dbContext.TblDevices
                .Where(d => d.DeviceId == deviceId)
                .Select(d => d.TimeZone)
                .FirstOrDefaultAsync();

            return DeviceLocalTime.TryResolve(DateTimeOffset.UtcNow, timeZoneId, out var localNow, _logger)
                ? localNow
                : null;
        }

        /// <summary>
        /// Resolves a cached layout publication for one device: playlist zones are targeted by their
        /// source location's metadata rules, and display-sourced zones are filled in against the
        /// device's own clock. Zone geometry and names come from the publication untouched.
        /// </summary>
        // Each delivery method is timed in full, failures included, and its metadata combination is recorded
        // only after the timing ends, so observing the meter never inflates the durations it reports.
        private async Task<CachedManifest> ResolveLayoutDeliveryAsync(
            CachedManifest publication, TblDevice device, string deviceId,
            Dictionary<string, string>? deviceMetadata)
        {
            var sourceTargeting = new List<KeyValuePair<int, LocationTargeting>>();
            CachedManifest delivery;
            var passStarted = _deliveryMetrics.StartPass();
            try
            {
                delivery = await ResolveLayoutDeliveryCoreAsync(publication, device, deviceId, deviceMetadata, sourceTargeting);
            }
            finally
            {
                _deliveryMetrics.EndPass(DeliveryMetrics.LayoutKind, passStarted);
            }
            foreach (var (sourceId, targeting) in sourceTargeting)
                _deliveryMetrics.RecordCombination(_tenantContext.TenantId, sourceId, targeting, deviceMetadata);
            return delivery;
        }

        private async Task<CachedManifest> ResolveLayoutDeliveryCoreAsync(
            CachedManifest publication, TblDevice device, string deviceId,
            Dictionary<string, string>? deviceMetadata, List<KeyValuePair<int, LocationTargeting>> sourceTargeting)
        {
            var publishedZones = publication.Zones ?? new List<ZoneDisplayContent>();

            var targetingBySource = new Dictionary<int, LocationTargeting>();
            foreach (var sourceId in publishedZones
                .Where(z => z.TargetingLocationId.HasValue)
                .Select(z => z.TargetingLocationId!.Value)
                .Distinct())
            {
                targetingBySource[sourceId] = await _locationMetadataRules.GetTargetingAsync(sourceId);
            }
            sourceTargeting.AddRange(targetingBySource);
            var targetedZones = LayoutZoneRules.ResolvePlaylistZones(publishedZones, targetingBySource, deviceMetadata, deviceId);
            // Card zones are returned as published and filled below from live queries; every other zone's
            // content is a fresh copy, so its URLs can be resolved in place.
            foreach (var item in targetedZones.Where(z => z.ZoneType != "card").SelectMany(z => z.Content))
                item.Url = ResolvePublicationUrl(item.Url);

            var resolved = DeviceLocalTime.TryResolve(
                DateTimeOffset.UtcNow, device.TimeZone, out var localNow, _logger);

            var displayIds = publishedZones
                .Where(z => z.ZoneType == "card" && z.DisplayId > 0)
                .Select(z => z.DisplayId)
                .Distinct()
                .ToList();

            var displaysMap = displayIds.Count > 0
                ? await _dbContext.TblDisplays
                    .Include(d => d.TblScheduleEvents)
                    .Where(d => displayIds.Contains(d.DisplayId))
                    .ToDictionaryAsync(d => d.DisplayId)
                : new Dictionary<int, TblDisplay>();

            var zones = new List<ZoneDisplayContent>();
            foreach (var zone in targetedZones)
            {
                if (zone.ZoneType != "card")
                {
                    zones.Add(zone);
                    continue;
                }

                var content = new List<ZoneContentItem>();
                if (displaysMap.TryGetValue(zone.DisplayId, out var display))
                {
                    var displayItem = new DisplayItem(display);

                    // An unresolvable zone takes the display's own default event, the same call the
                    // display path makes: decline to discriminate rather than pick another clock.
                    var scheduleEvent = resolved
                        ? displayItem.GetActiveEvent(display.TblScheduleEvents, localNow)
                        : display.TblScheduleEvents.FirstOrDefault(e => e.DefaultContent);

                    var displayContent = await displayItem.GetContentAssignmentForEventAsync(
                        display, scheduleEvent, _dbContext, SiteUrl);

                    content = displayContent.Content.Select(item =>
                    {
                        var isWebAsset = item.Asset?.AssetTypeId == (int)DXSuite.Director.Models.Types.AssetType.Web;
                        var url = isWebAsset
                            ? item.Asset?.AssetPath ?? ""
                            : item.AssetItem?.AssetWebPath ?? "";

                        // Append device metadata to web content URLs
                        if (isWebAsset && !string.IsNullOrEmpty(url))
                        {
                            url = DeviceMetadataService.AppendMetadataToUrl(url, deviceMetadata, deviceId);
                        }

                        return new ZoneContentItem
                        {
                            Url = url,
                            ContentType = GetContentTypeFromAsset(item.Asset),
                            Duration = (int?)item.Duration ?? 7,
                            AssetId = item.Asset?.AssetId
                        };
                    }).ToList();
                }
                else if (zone.DisplayId > 0)
                {
                    _logger.LogWarning("Display {DisplayId} not found for zone {ZoneNumber}", zone.DisplayId, zone.ZoneNumber);
                }

                zones.Add(new ZoneDisplayContent
                {
                    DisplayId = zone.DisplayId,
                    ZoneNumber = zone.ZoneNumber,
                    ZoneName = zone.ZoneName,
                    ZoneType = zone.ZoneType,
                    Layout = zone.Layout,
                    Content = content
                });
            }

            var delivery = new CachedManifest
            {
                LocationId = publication.LocationId,
                Status = publication.Status,
                EnableFade = publication.EnableFade,
                Assets = publication.Assets,
                Zones = zones,
                LayoutWidth = publication.LayoutWidth,
                LayoutHeight = publication.LayoutHeight,
                CachedAt = publication.CachedAt
            };
            delivery.Version = _manifestCache.ComputeDeliveryVersion(delivery);
            _manifestCache.RecordDeliveredVersion(_tenantContext.TenantId, deviceId, delivery.Version);
            return delivery;
        }

        /// <summary>
        /// Hydrates every schedule event of a display into device-neutral content. One asset query
        /// covers all events, and the only device-specific part of the result, the device id some
        /// web URLs carry, is deferred to delivery rather than baked in here.
        /// </summary>
        private async Task<List<CachedDisplayEvent>> BuildDisplayPublicationAsync(TblDisplay display)
        {
            var events = new List<CachedDisplayEvent>();

            if (display.DisplayMode == (int)DXSuite.Director.Models.Types.DisplayMode.Override)
            {
                var overrideAsset = await _dbContext.TblAssets
                    .FirstOrDefaultAsync(x => x.AssetId == display.OverrideAssetId);
                if (overrideAsset != null)
                {
                    var item = new ContentItem
                    {
                        AssetId = overrideAsset.AssetId,
                        Duration = overrideAsset.Duration,
                        Asset = overrideAsset,
                        AssetItem = new AssetItem(overrideAsset, PublicationUrls.SiteBase)
                    };
                    events.Add(new CachedDisplayEvent
                    {
                        DefaultContent = true,
                        Assets = new List<PlayerContentItem> { ProjectContentItem(item) },
                        Weights = new List<int> { item.Weight }
                    });
                }

                return events;
            }

            if (display.DisplayMode != (int)DXSuite.Director.Models.Types.DisplayMode.Schedule &&
                display.DisplayMode != (int)DXSuite.Director.Models.Types.DisplayMode.StatusSelector)
            {
                return events;
            }

            var assignments = new List<(TblScheduleEvent Event, ContentAssignment? Content)>();
            foreach (var scheduleEvent in display.TblScheduleEvents)
            {
                ContentAssignment? parsed = null;
                try
                {
                    parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<ContentAssignment>(scheduleEvent.ContentAssignment);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Schedule event {ScheduleEventId} on display {DisplayId} has unreadable content",
                        scheduleEvent.ScheduleEventId, display.DisplayId);
                }

                assignments.Add((scheduleEvent, parsed));
            }

            var assetIds = assignments
                .Where(a => a.Content?.Content != null)
                .SelectMany(a => a.Content!.GetAssetIDList())
                .Distinct()
                .ToList();

            var assets = assetIds.Count == 0
                ? new List<TblAsset>()
                : await _dbContext.TblAssets.Where(x => assetIds.Contains(x.AssetId)).ToListAsync();

            foreach (var (scheduleEvent, content) in assignments)
            {
                var cached = new CachedDisplayEvent
                {
                    ScheduleEventId = scheduleEvent.ScheduleEventId,
                    DefaultContent = scheduleEvent.DefaultContent,
                    StartDate = scheduleEvent.StartDate,
                    EndDate = scheduleEvent.EndDate,
                    StartTime = scheduleEvent.StartTime,
                    EndTime = scheduleEvent.EndTime,
                    Sunday = scheduleEvent.Sunday,
                    Monday = scheduleEvent.Monday,
                    Tuesday = scheduleEvent.Tuesday,
                    Wednesday = scheduleEvent.Wednesday,
                    Thursday = scheduleEvent.Thursday,
                    Friday = scheduleEvent.Friday,
                    Saturday = scheduleEvent.Saturday
                };

                foreach (var item in content?.Content ?? new List<ContentItem>())
                {
                    var asset = assets.FirstOrDefault(x => x.AssetId == item.AssetId);
                    if (asset == null) continue;

                    item.Asset = asset;
                    item.AssetItem = new AssetItem(asset, PublicationUrls.SiteBase);
                    cached.Assets.Add(ProjectContentItem(item));
                    cached.Weights.Add(item.Weight);
                }

                events.Add(cached);
            }

            return events;
        }

        private PlayerContentItem ProjectContentItem(ContentItem item)
        {
            bool isWebAsset = item.Asset?.AssetTypeId == (int)DXSuite.Director.Models.Types.AssetType.Web;
            bool isStreamAsset = item.Asset?.AssetTypeId == (int)DXSuite.Director.Models.Types.AssetType.Stream;
            bool healthMonitoringEnabled = item.Asset?.EnableHealthMonitoring ?? false;
            var url = (isWebAsset || isStreamAsset) ? item.Asset?.AssetPath ?? "" : (item.AssetItem?.AssetWebPath ?? "");

            StreamExtendedData? streamData = isStreamAsset
                ? ParseStreamExtendedData(item.Asset?.ExtendedData)
                : null;

            return new PlayerContentItem
            {
                FileName = (isWebAsset || isStreamAsset) ? null : (item.Asset?.AssetPath != null ? Path.GetFileName(item.Asset.AssetPath) : "unknown.png"),
                Url = url,
                Duration = (int?)item.Duration ?? 7,
                LocationId = item.Asset?.LocationId,
                IsWeb = isWebAsset,
                WebRenderMode = isWebAsset ? (item.Asset?.WebRenderMode ?? "iframe") : null,
                IsStream = isStreamAsset,
                StreamProtocol = streamData?.StreamProtocol,
                StreamAutoReconnect = streamData?.AutoReconnect ?? true,
                StreamBufferMs = streamData?.BufferMs ?? 3000,
                AssetName = item.Asset?.AssetName,
                AssetInkPath = item.Asset?.AssetInkPath,

                // Health monitoring identifies which player reported a bad page, so the id has to be
                // on the URL the player actually loads.
                AppendDeviceId = isWebAsset && healthMonitoringEnabled && !string.IsNullOrEmpty(url)
            };
        }

        /// <summary>
        /// Chooses which of a display's hydrated events this device should be showing, against its
        /// own clock. A display is not a place: the devices under it each carry a time zone and each
        /// is entitled to its own answer.
        /// </summary>
        private CachedManifest ResolveDisplayDelivery(CachedManifest publication, TblDevice device, string deviceId)
        {
            var passStarted = _deliveryMetrics.StartPass();
            try
            {
                return ResolveDisplayDeliveryCore(publication, device, deviceId);
            }
            finally
            {
                _deliveryMetrics.EndPass(DeliveryMetrics.DisplayKind, passStarted);
            }
        }

        private CachedManifest ResolveDisplayDeliveryCore(CachedManifest publication, TblDevice device, string deviceId)
        {
            var events = publication.DisplayEvents ?? new List<CachedDisplayEvent>();

            var resolved = DeviceLocalTime.TryResolve(
                DateTimeOffset.UtcNow, device.TimeZone, out var localNow, _logger);

            // An unresolvable zone serves the display's own default event, which is this path
            // declining to discriminate rather than picking some other clock.
            var active = resolved
                ? ScheduleEventSelection.PickActive(events, localNow, e => e.DefaultContent)
                : events.FirstOrDefault(e => e.DefaultContent);

            // A timed event carrying no content means the default event holds what to show: the
            // StatusSelector selection that DefaultAssetActivatorService writes on block activation.
            if (active is { DefaultContent: false, Assets.Count: 0 })
                active = events.FirstOrDefault(e => e.DefaultContent) ?? active;

            var assets = (active?.Assets ?? new List<PlayerContentItem>()).Select(a => a.Copy()).ToList();
            foreach (var asset in assets)
                asset.Url = ResolvePublicationUrl(asset.Url);
            foreach (var asset in assets.Where(a => a.AppendDeviceId))
            {
                asset.Url = AppendDeviceIdToUrl(asset.Url, deviceId);
            }

            assets = WeightedLoopExpander.Expand(assets, active?.Weights ?? new List<int>());

            var delivery = new CachedManifest
            {
                DisplayId = publication.DisplayId,
                Status = publication.Status,
                EnableFade = publication.EnableFade,
                Assets = assets,
                CachedAt = publication.CachedAt
            };
            delivery.Version = _manifestCache.ComputeDeliveryVersion(delivery);
            _manifestCache.RecordDeliveredVersion(_tenantContext.TenantId, deviceId, delivery.Version);
            return delivery;
        }

        /// <summary>
        /// Resolves this device's delivery from a cached playlist publication and records the version
        /// it was sent. The decision itself is <see cref="PlaylistDelivery.Resolve"/>.
        /// </summary>
        private CachedManifest ResolvePlaylistDelivery(CachedManifest publication, LocationTargeting targeting, TblDevice device, Dictionary<string, string>? deviceMetadata, Entities.TblLocation? locationRecord)
        {
            CachedManifest delivery;
            var passStarted = _deliveryMetrics.StartPass();
            try
            {
                delivery = ResolvePlaylistDeliveryCore(publication, targeting, device, deviceMetadata, locationRecord);
            }
            finally
            {
                _deliveryMetrics.EndPass(DeliveryMetrics.PlaylistKind, passStarted);
            }
            if ((locationRecord?.LocationId ?? publication.LocationId) is int locationId)
                _deliveryMetrics.RecordCombination(_tenantContext.TenantId, locationId, targeting, deviceMetadata);
            return delivery;
        }

        private CachedManifest ResolvePlaylistDeliveryCore(CachedManifest publication, LocationTargeting targeting, TblDevice device, Dictionary<string, string>? deviceMetadata, Entities.TblLocation? locationRecord)
        {
            var resolved = DeviceLocalTime.TryResolve(
                DateTimeOffset.UtcNow, device.TimeZone, out var localNow, _logger);
            var imageDurationSeconds = ExtendedDataHelper.Parse<LocationExtendedData>(locationRecord?.ExtendedData)?.ImageDurationSeconds;

            var delivery = PlaylistDelivery.Resolve(publication, targeting, deviceMetadata, resolved ? localNow : null, imageDurationSeconds);
            foreach (var asset in delivery.Assets)
                asset.Url = ResolvePublicationUrl(asset.Url);
            delivery.Version = _manifestCache.ComputeDeliveryVersion(delivery);
            _manifestCache.RecordDeliveredVersion(_tenantContext.TenantId, device.DeviceId, delivery.Version);
            return delivery;
        }

        // Helper method for playercontent - uses cached metadata
        private int? GetPlayOrderFromMetadata(string filePath)
        {
            var metadata = _metadataCache.GetMetadata(_tenantContext.TenantId, filePath);
            return metadata?.PlayOrder;
        }

        private PlayerContentResponse BuildResponseFromCache(
            CachedManifest cached,
            string? echoedVersion,
            UpdateInfo? updateInfo,
            UpdateInfo? bootloaderUpdateInfo,
            CecCommandInfo? cecCommand,
            ScreenshotCommandInfo? screenshotCommand,
            Dictionary<string, string>? deviceMetadata,
            bool consoleLoggingEnabled,
            bool forceHtml5Video = false,
            bool flushCache = false,
            bool allowHttpFallback = true,
            WindowModeResponseData? windowMode = null)
        {
            // A queued cache flush suppresses NotModified so the device that is about to
            // wipe its storage receives the full manifest alongside the flush command.
            if (!flushCache && !string.IsNullOrEmpty(echoedVersion) && echoedVersion == cached.Version)
            {
                return new PlayerContentResponse
                {
                    LocationId = cached.LocationId,
                    DisplayId = cached.DisplayId,
                    Status = cached.Status,
                    NotModified = true,
                    ManifestVersion = cached.Version,
                    Shuffle = cached.Shuffle ?? false,
                    EnableFade = cached.EnableFade ?? true,
                    Assets = new List<PlayerContentItem>(),
                    Update = updateInfo,
                BootloaderUpdate = bootloaderUpdateInfo,
                    CecCommand = cecCommand,
                    ScreenshotCommand = screenshotCommand,
                    DeviceMetadata = deviceMetadata,
                    ConsoleLoggingEnabled = consoleLoggingEnabled,
                    ForceHtml5Video = forceHtml5Video,
                    AllowHttpFallback = allowHttpFallback,
                    WindowMode = windowMode
                };
            }

            return new PlayerContentResponse
            {
                ManifestVersion = cached.Version,
                LocationId = cached.LocationId,
                DisplayId = cached.DisplayId,
                Status = cached.Status,
                Shuffle = cached.Shuffle ?? false,
                EnableFade = cached.EnableFade ?? true,
                IsOverride = cached.IsOverride,
                OverrideId = cached.OverrideId,
                OverrideName = cached.OverrideName,
                Assets = cached.Assets,
                Zones = cached.Zones,
                LayoutWidth = cached.LayoutWidth,
                LayoutHeight = cached.LayoutHeight,
                // Device-specific fields â€” always fresh, never cached
                Update = updateInfo,
                BootloaderUpdate = bootloaderUpdateInfo,
                CecCommand = cecCommand,
                ScreenshotCommand = screenshotCommand,
                DeviceMetadata = deviceMetadata,
                ConsoleLoggingEnabled = consoleLoggingEnabled,
                ForceHtml5Video = forceHtml5Video,
                FlushCache = flushCache,
                AllowHttpFallback = allowHttpFallback,
                WindowMode = windowMode
            };
        }

        // Helper method for override content resolution

        private async Task<PlayerContentItem?> GetOverrideContentForDevice(TblOverride activeOverride, string deviceId, TblDevice deviceRecord)
        {
            // Get device resolution (from display if assigned, otherwise default)
            int width = 1920;
            int height = 1080;

            if (deviceRecord.Display != null)
            {
                width = (int)deviceRecord.Display.DisplayWidth;
                height = (int)deviceRecord.Display.DisplayHeight;
            }

            // Get override content - device-specific first, then resolution default
            var contents = await _overrideService.GetContentForOverrideAsync(activeOverride.OverrideId);

            // Try device-specific content first
            var deviceContent = contents.FirstOrDefault(c => c.DeviceId == deviceId);
            if (deviceContent != null)
            {
                return BuildOverrideContentItem(deviceContent);
            }

            // Fall back to resolution default
            var resolutionContent = contents.FirstOrDefault(c =>
                c.DeviceId == null &&
                c.ResolutionWidth == width &&
                c.ResolutionHeight == height);

            if (resolutionContent != null)
            {
                return BuildOverrideContentItem(resolutionContent);
            }

            // No matching content found
            return null;
        }

        private PlayerContentItem BuildOverrideContentItem(TblOverrideContent content)
        {
            var fileName = Path.GetFileName(content.ContentPath);
            var isWeb = content.ContentPath.StartsWith("http://") || content.ContentPath.StartsWith("https://");

            // ContentPath is stored as a relative URL like "/OverrideContent/guid.ext"
            // For non-web content, prepend the media URL base
            string url;
            if (isWeb)
            {
                url = content.ContentPath;
            }
            else if (content.ContentPath.StartsWith("/"))
            {
                // Relative path - prepend media URL
                url = $"{MediaUrl}{content.ContentPath}";
            }
            else
            {
                // Already an absolute URL or other format
                url = content.ContentPath;
            }

            // Override content does not support health monitoring
            // Use scheduled assets with EnableHealthMonitoring for health monitoring

            return new PlayerContentItem
            {
                FileName = isWeb ? null : fileName,
                Url = url,
                Duration = content.Duration,
                IsWeb = isWeb,
                WebRenderMode = isWeb ? "iframe" : null,
                AssetName = content.Asset?.AssetName,
                AssetInkPath = content.Asset?.AssetInkPath
            };
        }

        /// <summary>
        /// Appends deviceId query parameter to a URL for content health monitoring
        /// </summary>
        private string AppendDeviceIdToUrl(string url, string deviceId)
        {
            var separator = url.Contains("?") ? "&" : "?";
            return $"{url}{separator}deviceId={Uri.EscapeDataString(deviceId)}";
        }

        /// <summary>
        /// Parse stream-specific extended data from JSON
        /// </summary>
        private StreamExtendedData? ParseStreamExtendedData(string? json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<StreamExtendedData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Build zone content array for Layout locations (LocationTypeId 1015).
        /// Batch-loads all zone sources in single queries to avoid N+1 problem.
        /// </summary>
        /// <param name="locationRecord">The layout location record</param>
        /// <param name="locationId">The location ID</param>
        private async Task<List<ZoneDisplayContent>> BuildLayoutZonesAsync(
            DXSuite.Director.Entities.TblLocation locationRecord,
            int? locationId)
        {
            var zones = new List<ZoneDisplayContent>();

            if (string.IsNullOrEmpty(locationRecord.ExtendedData))
            {
                _logger.LogWarning("Layout location {LocationId} has no ExtendedData configuration", locationId);
                return zones;
            }

            try
            {
                var layoutConfig = JsonSerializer.Deserialize<LayoutConfiguration>(locationRecord.ExtendedData, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (layoutConfig?.Zones == null || layoutConfig.Zones.Count == 0)
                {
                    _logger.LogWarning("Layout location {LocationId} has no zones configured", locationId);
                    return zones;
                }

                // Batch-load all source displays and locations in single queries (fixes N+1)
                var displayIds = layoutConfig.Zones
                    .Where(z => z.SourceDisplayId.HasValue)
                    .Select(z => z.SourceDisplayId!.Value)
                    .Distinct()
                    .ToList();

                var locationIds = layoutConfig.Zones
                    .Where(z => !z.SourceDisplayId.HasValue)
                    .Select(z => z.SourceLocationId)
                    .Distinct()
                    .ToList();
                var contentBySource = new Dictionary<int, List<ZoneContentItem>>();

                // Single query for all displays with their schedule events
                var displaysMap = displayIds.Count > 0
                    ? await _dbContext.TblDisplays
                        .Include(d => d.TblScheduleEvents)
                        .Where(d => displayIds.Contains(d.DisplayId))
                        .ToDictionaryAsync(d => d.DisplayId)
                    : new Dictionary<int, TblDisplay>();

                // Single query for all source locations (for approval settings and location type)
                var locationsMap = locationIds.Count > 0
                    ? await _dbContext.TblLocations
                        .Include(l => l.LocationType)
                        .Where(l => locationIds.Contains(l.LocationId))
                        .ToDictionaryAsync(l => l.LocationId)
                    : new Dictionary<int, DXSuite.Director.Entities.TblLocation>();

                foreach (var zone in layoutConfig.Zones)
                {
                    List<ZoneContentItem> content;
                    int? targetingLocationId = null;

                    if (zone.SourceDisplayId.HasValue)
                    {
                        // A display-sourced zone is the one clock-dependent part of a layout, and
                        // which event is on depends on the asking device, so it is left unresolved
                        // here and filled in at delivery. Playlist-sourced zones read no clock at
                        // all today, which is a separate defect and off this map.
                        content = new List<ZoneContentItem>();
                    }
                    else
                    {
                        // Playlist location - get content from location media folder
                        if (!locationsMap.TryGetValue(zone.SourceLocationId, out var sourceLocation))
                        {
                            _logger.LogWarning("Source location {LocationId} not found for zone {ZoneNumber}", zone.SourceLocationId, zone.ZoneNumber);
                            content = new List<ZoneContentItem>();
                        }
                        else
                        {
                            if (!contentBySource.TryGetValue(zone.SourceLocationId, out content!))
                            {
                                content = await GetLocationMediaContentAsync(zone.SourceLocationId, sourceLocation.EnableContentApproval);
                                contentBySource[zone.SourceLocationId] = content;
                            }

                            // Zone config is JSON that bundle import also writes, so a zone can name a
                            // source the layout editor would never offer. Only a Playlist location's rules
                            // target content; any other source plays untargeted.
                            if (LocationTypeConstants.IsPlaylist(sourceLocation.LocationType))
                            {
                                targetingLocationId = zone.SourceLocationId;
                            }
                            else
                            {
                                _logger.LogWarning("Zone {ZoneNumber} of layout {LocationId} sources non-Playlist location {SourceLocationId}; its content is not targeted",
                                    zone.ZoneNumber, locationId, zone.SourceLocationId);
                            }
                        }
                    }

                    zones.Add(new ZoneDisplayContent
                    {
                        // Delivery needs to know which display an unresolved zone draws from.
                        DisplayId = zone.SourceDisplayId ?? 0,
                        ZoneNumber = zone.ZoneNumber,
                        ZoneName = zone.SourceLocationName,
                        ZoneType = zone.SourceDisplayId.HasValue ? "card" : "playlist",
                        Layout = new ZoneLayoutPosition
                        {
                            X = zone.Left,
                            Y = zone.Top,
                            Width = zone.Width,
                            Height = zone.Height
                        },
                        Content = content,
                        TargetingLocationId = targetingLocationId
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse layout configuration for location {LocationId}", locationId);
            }

            return zones;
        }

        /// <summary>
        /// The device-neutral content of a playlist-sourced zone: every file in the source location's
        /// media folder that passes its approval gate, each carrying every tag its asset holds so
        /// delivery can target it per device against the source location's current rules.
        /// </summary>
        private async Task<List<ZoneContentItem>> GetLocationMediaContentAsync(int locationId, bool enableContentApproval)
        {
            var content = new List<ZoneContentItem>();

            var files = _mediaFolderCache.GetMediaFiles(_tenantContext.TenantId, locationId);
            if (files.Length == 0)
            {
                return content;
            }

            var assetRecords = await _metadataSubjects.GetLocationAssetRecordsAsync(locationId);
            var publishedFiles = PlaylistManifestRules.SelectPublished(files, locationId, assetRecords, enableContentApproval);
            var assetTags = await _metadataSubjects.GetAssetValuesAsync(
                publishedFiles.Where(f => f.Record != null).Select(f => f.Record!.AssetId).ToList());

            foreach (var (file, record) in publishedFiles)
            {
                var fileName = Path.GetFileName(file);
                var extension = Path.GetExtension(file).ToLower();
                var tags = record == null ? null : assetTags[record.AssetId];

                if (extension == ".json")
                {
                    try
                    {
                        var jsonContent = await System.IO.File.ReadAllTextAsync(file);
                        var webContent = JsonSerializer.Deserialize<WebContentDefinition>(jsonContent);
                        if (webContent?.Url != null)
                        {
                            content.Add(new ZoneContentItem
                            {
                                Url = webContent.Url,
                                ContentType = "web",
                                Duration = webContent.Duration ?? 30,
                                MetadataValues = tags
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse web content JSON: {File}", file);
                    }
                    continue;
                }

                var isVideo = extension == ".mp4" || extension == ".avi";

                content.Add(new ZoneContentItem
                {
                    Url = $"{PublicationUrls.MediaBase}/{locationId}/{Uri.EscapeDataString(fileName)}",
                    ContentType = isVideo ? "video" : "image",
                    Duration = isVideo ? 0 : 7,  // 0 for video means use actual duration
                    MetadataValues = tags
                });
            }

            return content;
        }

        /// <summary>
        /// Determine content type from asset
        /// </summary>
        private string GetContentTypeFromAsset(TblAsset? asset) =>
            DXSuite.Director.Models.AssetContentKind.FromAsset(asset);

        /// <summary>
        /// Queue a CEC command (power on/off) for a device.
        /// The command will be picked up by the device on its next sync.
        /// </summary>
        [HttpPost]
        [Route("CecCommand")]
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<IActionResult> QueueCecCommand([FromBody] CecCommandRequest request)
        {
            if (string.IsNullOrEmpty(request.DeviceId))
            {
                return BadRequest("DeviceId is required");
            }

            if (string.IsNullOrEmpty(request.Action) ||
                (request.Action != "power_on" && request.Action != "power_off"))
            {
                return BadRequest("Action must be 'power_on' or 'power_off'");
            }

            await _deviceCommands.QueueCecAsync(request.DeviceId, request.Action, request.Output ?? "HDMI-1", User?.Identity?.Name);

            return Ok(new { success = true, message = $"CEC {request.Action} command queued for device {request.DeviceId}" });
        }

        /// <summary>
        /// Queue a screenshot request for a device.
        /// The device will capture and upload a screenshot on its next sync.
        /// </summary>
        [HttpPost]
        [Route("RequestScreenshot")]
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<IActionResult> RequestScreenshot([FromBody] ScreenshotRequestModel request)
        {
            if (string.IsNullOrEmpty(request.DeviceId))
            {
                return BadRequest("DeviceId is required");
            }

            await _deviceCommands.QueueScreenshotAsync(request.DeviceId, User?.Identity?.Name);

            return Ok(new { success = true, message = $"Screenshot request queued for device {request.DeviceId}" });
        }

        /// <summary>
        /// Upload a screenshot from a device.
        /// Called by device clients after capturing a screenshot.
        /// </summary>
        [HttpPost]
        [Route("Screenshot")]
        [Authorize(AuthenticationSchemes = AuthSchemes.DeviceToken)]
        public async Task<IActionResult> UploadScreenshot([FromBody] ScreenshotUploadRequest request)
        {
            if (string.IsNullOrEmpty(request.DeviceId))
            {
                return BadRequest("DeviceId is required");
            }

            if (string.IsNullOrEmpty(request.ImageData))
            {
                return BadRequest("ImageData is required");
            }

            try
            {
                // Decode base64 image data
                // Handle data URL format (data:image/jpeg;base64,...)
                var base64Data = request.ImageData;
                if (base64Data.Contains(","))
                {
                    base64Data = base64Data.Substring(base64Data.IndexOf(",") + 1);
                }

                var imageBytes = Convert.FromBase64String(base64Data);

                // Validate reasonable size (max 1MB)
                if (imageBytes.Length > 1_048_576)
                {
                    return BadRequest("Screenshot exceeds maximum size of 1MB");
                }

                // Save screenshot
                await _screenshotService.SaveScreenshotAsync(request.DeviceId, imageBytes, _dbContextFactory);

                _logger.LogInformation("Screenshot uploaded: Device={DeviceId}, Size={Size} bytes",
                    request.DeviceId, imageBytes.Length);

                return Ok(new { success = true, sizeBytes = imageBytes.Length });
            }
            catch (FormatException ex)
            {
                _logger.LogWarning(ex, "Invalid base64 data in screenshot upload from device {DeviceId}", request.DeviceId);
                return BadRequest("Invalid base64 image data");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving screenshot for device {DeviceId}", request.DeviceId);
                return StatusCode(500, "Error saving screenshot");
            }
        }

        /// <summary>
        /// Upload a screenshot from SignageOS native screenshot API.
        /// The native API uploads binary image data directly.
        /// DeviceId, RequestId, and ApiKey are passed as query parameters
        /// since native API doesn't support custom headers.
        /// </summary>
        [HttpPost]
        [Route("Screenshot/Native")]
        [AllowAnonymous] // Query-param token support â€” native screenshot API cannot send custom headers
        public async Task<IActionResult> UploadNativeScreenshot(
            [FromQuery] string deviceId,
            [FromQuery] string? requestId,
            [FromQuery] string? apiKey)
        {
            // Accept device token from either header or query parameter
            var headerApiKey = Request.Headers["X-API-KEY"].ToString();
            var effectiveApiKey = !string.IsNullOrEmpty(headerApiKey) ? headerApiKey : apiKey;

            if (string.IsNullOrEmpty(effectiveApiKey))
            {
                return Unauthorized();
            }

            var tokenResult = await _deviceTokenService.ValidateDeviceTokenAsync(effectiveApiKey);
            if (!tokenResult.IsValid)
            {
                return Unauthorized();
            }

            if (string.IsNullOrEmpty(deviceId))
            {
                return BadRequest("deviceId query parameter is required");
            }

            // This route validates the token itself rather than going through the auth handler, so
            // it compares against the token's own binding rather than HttpContext.Items.
            if (!string.IsNullOrEmpty(tokenResult.DeviceId)
                && !string.Equals(tokenResult.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Device token bound to {BoundDeviceId} uploaded a screenshot as {RequestedDeviceId}",
                    tokenResult.DeviceId, deviceId);
                return Forbid();
            }

            try
            {
                // Read raw binary data from request body
                using var memoryStream = new MemoryStream();
                await Request.Body.CopyToAsync(memoryStream);
                var imageBytes = memoryStream.ToArray();

                if (imageBytes.Length == 0)
                {
                    return BadRequest("No image data received");
                }

                // Validate reasonable size (max 2MB for native screenshots which may be higher res)
                if (imageBytes.Length > 2_097_152)
                {
                    return BadRequest("Screenshot exceeds maximum size of 2MB");
                }

                // Save screenshot
                await _screenshotService.SaveScreenshotAsync(deviceId, imageBytes, _dbContextFactory);

                _logger.LogInformation("Native screenshot uploaded: Device={DeviceId}, Size={Size} bytes, RequestId={RequestId}",
                    deviceId, imageBytes.Length, requestId ?? "none");

                return Ok(new { success = true, sizeBytes = imageBytes.Length });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving native screenshot for device {DeviceId}", deviceId);
                return StatusCode(500, "Error saving screenshot");
            }
        }

        // ----- SignageOS native screenshot upload contract -----
        // The SDK's takeAndUploadScreenshot(base) appends one of /upload/file (multipart),
        // /upload/raw (binary), or /upload/image-data-uri (base64) to the base URL we hand it,
        // choosing per platform. We provide a clean base of /api/signage/screenshot/sos/{requestId};
        // the {requestId} is the one-time upload credential that resolves to the target device, so
        // these stay anonymous (no device token in the URL).

        [HttpPost]
        [Route("screenshot/sos/{requestId}/upload/file")]
        [AllowAnonymous]
        [RequestSizeLimit(20_971_520)]
        [Filters.NeutralizeFormContentType]
        public async Task<IActionResult> UploadSosScreenshotFile(string requestId, IFormFile? file)
        {
            byte[] bytes = Array.Empty<byte>();
            var upload = file;
            if (upload == null && Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                upload = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            }
            if (upload != null)
            {
                using var ms = new MemoryStream();
                await upload.CopyToAsync(ms);
                bytes = ms.ToArray();
            }
            return await SaveSosScreenshotAsync(requestId, bytes);
        }

        [HttpPost]
        [Route("screenshot/sos/{requestId}/upload/raw")]
        [AllowAnonymous]
        [RequestSizeLimit(20_971_520)]
        [Filters.NeutralizeFormContentType]
        public async Task<IActionResult> UploadSosScreenshotRaw(string requestId)
        {
            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms);
            return await SaveSosScreenshotAsync(requestId, ms.ToArray());
        }

        [HttpPost]
        [Route("screenshot/sos/{requestId}/upload/image-data-uri")]
        [AllowAnonymous]
        [RequestSizeLimit(20_971_520)]
        [Filters.NeutralizeFormContentType]
        public async Task<IActionResult> UploadSosScreenshotDataUri(string requestId)
        {
            using var reader = new StreamReader(Request.Body);
            var body = (await reader.ReadToEndAsync()).Trim();

            // Accept either a bare base64 string or a full data URL (data:image/jpeg;base64,...).
            var commaIndex = body.IndexOf(',');
            if (body.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIndex >= 0)
                body = body[(commaIndex + 1)..];

            byte[] bytes;
            try { bytes = Convert.FromBase64String(body); }
            catch (FormatException) { return BadRequest("Invalid base64 image data"); }

            return await SaveSosScreenshotAsync(requestId, bytes);
        }

        /// <summary>
        /// Shared handler for the SignageOS upload-contract routes: validate the request-id credential,
        /// persist (the service downscales), and return the stored URL under several keys for
        /// cross-version SDK compatibility.
        /// </summary>
        private async Task<IActionResult> SaveSosScreenshotAsync(string requestId, byte[] imageBytes)
        {
            var deviceId = await _deviceCommands.ResolveScreenshotUploadAsync(requestId);
            if (string.IsNullOrEmpty(deviceId))
            {
                return Unauthorized();
            }

            if (imageBytes.Length == 0)
            {
                return BadRequest("No image data received");
            }

            try
            {
                await _screenshotService.SaveScreenshotAsync(deviceId, imageBytes, _dbContextFactory);
                await _deviceCommands.AcknowledgeScreenshotAsync(requestId);

                var info = _screenshotService.GetScreenshotInfo(deviceId);
                var url = string.IsNullOrEmpty(info.WebPath) ? null : $"{SiteUrl}{info.WebPath}";

                _logger.LogInformation("SOS screenshot saved: Device={DeviceId}, Stored={Size} bytes, RequestId={RequestId}",
                    deviceId, info.SizeBytes, requestId);

                return Ok(new { url, uri = url, screenshotUrl = url });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving SOS screenshot for request {RequestId}", requestId);
                return StatusCode(500, "Error saving screenshot");
            }
        }

        /// <summary>
        /// Toggle console logging for a device (enable/disable).
        /// When enabled, the device will capture and send console logs with each heartbeat.
        /// Called from Blazor UI - uses session auth, not API key.
        /// </summary>
        [HttpPost]
        [Route("ConsoleLogging/Toggle")]
        [Authorize]
        public async Task<IActionResult> ToggleConsoleLogging([FromBody] ConsoleLoggingToggleRequest request)
        {
            if (string.IsNullOrEmpty(request.DeviceId))
            {
                return BadRequest("DeviceId is required");
            }

            await _consoleLoggingService.SetLoggingEnabledAsync(request.DeviceId, request.Enabled);

            _logger.LogInformation("Console logging {Status} for device {DeviceId}",
                request.Enabled ? "enabled" : "disabled", request.DeviceId);

            return Ok(new { success = true, enabled = request.Enabled });
        }

        /// <summary>
        /// Get console logging data for a device (settings and log entries).
        /// Called from Blazor UI - uses session auth, not API key.
        /// </summary>
        [HttpGet]
        [Route("ConsoleLogging/{deviceId}")]
        [Authorize]
        public async Task<IActionResult> GetConsoleLogging(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                return BadRequest("DeviceId is required");
            }

            var loggingData = await _consoleLoggingService.GetLoggingDataAsync(deviceId);

            return Ok(new
            {
                enabled = loggingData?.Enabled ?? false,
                toggledAt = loggingData?.ToggledAt,
                entries = loggingData?.Entries?.OrderByDescending(e => e.Timestamp).ToList() ?? new List<ConsoleLogEntry>()
            });
        }

        /// <summary>
        /// Clear console logs for a device (keeps logging enabled if it was).
        /// Called from Blazor UI - uses session auth, not API key.
        /// </summary>
        [HttpPost]
        [Route("ConsoleLogging/Clear")]
        [Authorize]
        public async Task<IActionResult> ClearConsoleLogs([FromBody] ConsoleLoggingClearRequest request)
        {
            if (string.IsNullOrEmpty(request.DeviceId))
            {
                return BadRequest("DeviceId is required");
            }

            await _consoleLoggingService.ClearLogsAsync(request.DeviceId);

            _logger.LogInformation("Console logs cleared for device {DeviceId}", request.DeviceId);

            return Ok(new { success = true });
        }

        /// <summary>
        /// Validates a device token on behalf of an external service (e.g., Web Content server).
        /// The caller authenticates with its own API key and must have the token:validate scope.
        /// Returns whether the device token is valid and the associated device ID.
        /// </summary>
        [HttpPost]
        [Route("ValidateToken")]
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<IActionResult> ValidateToken([FromBody] ValidateTokenRequest request)
        {
            if (this.CheckScope(ApiKeyScopes.TokenValidate) is { } denied) return denied;

            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return BadRequest(new ValidateTokenResponse { IsValid = false, InvalidReason = "Token is required" });
            }

            var result = await _deviceTokenService.ValidateDeviceTokenAsync(request.Token);

            return Ok(new ValidateTokenResponse
            {
                IsValid = result.IsValid,
                DeviceId = result.IsValid ? result.DeviceId : null,
                InvalidReason = result.IsValid ? null : result.InvalidReason
            });
        }
    }

    /// <summary>
    /// Request body for CEC command queueing
    /// </summary>
    public class CecCommandRequest
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string? Output { get; set; }
    }

    /// <summary>
    /// Request body for screenshot request queueing
    /// </summary>
    public class ScreenshotRequestModel
    {
        public string DeviceId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request body for screenshot upload from device
    /// </summary>
    public class ScreenshotUploadRequest
    {
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>
        /// Base64-encoded JPEG image data (may include data URL prefix)
        /// </summary>
        public string ImageData { get; set; } = string.Empty;

        /// <summary>
        /// Request ID from the screenshot command (for correlation)
        /// </summary>
        public string? RequestId { get; set; }
    }

    /// <summary>
    /// Extended data for Stream asset type (stored as JSON in TblAsset.ExtendedData)
    /// </summary>
    public class StreamExtendedData
    {
        public string? StreamProtocol { get; set; }
        public bool AutoReconnect { get; set; } = true;
        public int BufferMs { get; set; } = 3000;
    }

    /// <summary>
    /// Request body for toggling console logging
    /// </summary>
    public class ConsoleLoggingToggleRequest
    {
        public string DeviceId { get; set; } = string.Empty;
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// Request body for clearing console logs
    /// </summary>
    public class ConsoleLoggingClearRequest
    {
        public string DeviceId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request/response models for device token validation by external services.
    /// </summary>
    public class ValidateTokenRequest
    {
        public string Token { get; set; } = string.Empty;
    }

    public class ValidateTokenResponse
    {
        public bool IsValid { get; set; }
        public string? DeviceId { get; set; }
        public string? InvalidReason { get; set; }
    }
}
