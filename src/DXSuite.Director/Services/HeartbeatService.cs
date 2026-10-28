using DXSuite.Director.Models.BrightSign;
using DXSuite.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Services
{
    /// <summary>
    /// High-performance heartbeat service that batches database writes.
    /// Designed to handle 800+ devices sending heartbeats every 30 seconds.
    ///
    /// Instead of writing to the database on every request (~27/second at scale),
    /// heartbeats are queued in memory and flushed to the database periodically.
    /// Each tenant's heartbeats are flushed using a DI scope bound to that tenant's
    /// connection string, so cross-tenant DB writes are structurally impossible.
    /// </summary>
    public class HeartbeatService : IHeartbeatService, IHostedService, IDisposable
    {
        private readonly IWorkerScopeFactory _workerScopeFactory;
        private readonly ILogger<HeartbeatService> _logger;

        // Outer key: tenantSubdomain. Inner key: deviceId.
        // Overwritten on each heartbeat so only the latest payload is flushed.
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, PlayerHeartbeatModel>>
            _pendingHeartbeats = new();

        // Key: "tenantSubdomain:deviceId" — used to skip DB lookups on repeated heartbeats
        private readonly ConcurrentDictionary<string, DateTime> _knownDevices = new();

        private Timer? _flushTimer;
        private readonly TimeSpan _flushInterval = TimeSpan.FromSeconds(10);
        private readonly TimeSpan _deviceCacheExpiry = TimeSpan.FromMinutes(5);

        private bool _isRunning;
        private readonly SemaphoreSlim _flushLock = new(1, 1);

        public HeartbeatService(IWorkerScopeFactory workerScopeFactory, ILogger<HeartbeatService> logger)
        {
            _workerScopeFactory = workerScopeFactory;
            _logger = logger;
        }

        public void QueueHeartbeat(string tenantSubdomain, PlayerHeartbeatModel payload)
        {
            if (string.IsNullOrEmpty(payload.DeviceId))
                return;

            var tenantQueue = _pendingHeartbeats.GetOrAdd(tenantSubdomain, _ => new ConcurrentDictionary<string, PlayerHeartbeatModel>());
            tenantQueue[payload.DeviceId] = payload;

            _knownDevices[$"{tenantSubdomain}:{payload.DeviceId}"] = DateTime.UtcNow;
        }

        public async Task<bool> DeviceExistsAsync(string tenantSubdomain, string deviceId)
        {
            var cacheKey = $"{tenantSubdomain}:{deviceId}";
            if (_knownDevices.TryGetValue(cacheKey, out var cachedTime)
                && DateTime.UtcNow - cachedTime < _deviceCacheExpiry)
                return true;

            using var scope = await _workerScopeFactory.CreateTenantScopeAsync(tenantSubdomain);
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var exists = await db.TblDevices.AnyAsync(d => d.DeviceId == deviceId);
            if (exists)
                _knownDevices[cacheKey] = DateTime.UtcNow;

            return exists;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("HeartbeatService starting. Flush interval: {Interval}s", _flushInterval.TotalSeconds);
            _isRunning = true;
            _flushTimer = new Timer(FlushHeartbeatsCallback, null, _flushInterval, _flushInterval);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("HeartbeatService stopping. Flushing remaining heartbeats...");
            _isRunning = false;

            if (_flushTimer != null)
            {
                await _flushTimer.DisposeAsync();
                _flushTimer = null;
            }

            await FlushHeartbeatsAsync();
        }

        private void FlushHeartbeatsCallback(object? state)
        {
            _ = Task.Run(async () =>
            {
                try { await FlushHeartbeatsAsync(); }
                catch (Exception ex) { _logger.LogError(ex, "Error in heartbeat flush callback"); }
            });
        }

        private async Task FlushHeartbeatsAsync()
        {
            if (!await _flushLock.WaitAsync(0))
                return;

            try
            {
                foreach (var tenantSubdomain in _pendingHeartbeats.Keys.ToList())
                {
                    if (!_pendingHeartbeats.TryGetValue(tenantSubdomain, out var tenantQueue))
                        continue;

                    var heartbeatsToProcess = new List<PlayerHeartbeatModel>();
                    foreach (var kvp in tenantQueue)
                    {
                        if (tenantQueue.TryRemove(kvp.Key, out var payload))
                            heartbeatsToProcess.Add(payload);
                    }

                    if (heartbeatsToProcess.Count == 0)
                        continue;

                    _logger.LogDebug("Flushing {Count} heartbeats for tenant {Tenant}", heartbeatsToProcess.Count, tenantSubdomain);

                    try
                    {
                        using var scope = await _workerScopeFactory.CreateTenantScopeAsync(tenantSubdomain);
                        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
                        var tenantCtx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                        await ProcessDirectorHeartbeatsAsync(db, tenantCtx.CustomerCode, heartbeatsToProcess);
                        _logger.LogDebug("Flushed {Count} heartbeats for tenant {Tenant}", heartbeatsToProcess.Count, tenantSubdomain);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error flushing heartbeats for tenant {Tenant}", tenantSubdomain);
                    }
                }
            }
            finally
            {
                _flushLock.Release();
            }
        }

        private async Task ProcessDirectorHeartbeatsAsync(IDirectorDBContext db, string customerCode, List<PlayerHeartbeatModel> heartbeats)
        {
            var deviceIds = heartbeats.Select(h => h.DeviceId).ToList();

            var existingDevices = await db.TblDevices
                .Where(d => deviceIds.Contains(d.DeviceId))
                .ToDictionaryAsync(d => d.DeviceId);

            var newDevices = new List<TblDevice>();
            var now = DateTime.UtcNow;

            // Serials that became native on this batch, which is the moment a takeover finished.
            var converted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var platformLookup = await db.TblAppPlatforms
              .Where(p => p.IsActive)
              .ToDictionaryAsync(p => p.PlatformName, p => p.PlatformId, StringComparer.OrdinalIgnoreCase);

            // Platform rows are seeded by migration and never self-register. A player whose
            // ClientPlatform matches no active row resolves to null, which is the only PlatformId
            // that cannot be targeted by a DEVICE- or LOCATION-scoped deployment.
            foreach (var payload in heartbeats)
            {
                if (existingDevices.TryGetValue(payload.DeviceId, out var existing))
                {
                    // Absent fields mean "unchanged": slim heartbeats carry only identity
                    // and health, so inventory columns are only written when present
                    if (!string.IsNullOrEmpty(payload.SerialNumber)) existing.SerialNumber = payload.SerialNumber;
                    if (!string.IsNullOrEmpty(payload.IpAddress)) existing.Ipaddress = payload.IpAddress;
                    if (!string.IsNullOrEmpty(payload.MacAddress)) existing.Macaddress = payload.MacAddress;
                    if (!string.IsNullOrEmpty(payload.OsVersion)) existing.Osversion = payload.OsVersion;
                    if (!string.IsNullOrEmpty(payload.StorageType)) existing.StorageType = payload.StorageType;
                    if (payload.StorageSize.HasValue) existing.StorageSize = payload.StorageSize;
                    if (payload.StorageHealth.HasValue) existing.StorageHealth = payload.StorageHealth;
                    if (!string.IsNullOrEmpty(payload.ClientAppVersion)) existing.ClientAppVersion = payload.ClientAppVersion;
                    if (!string.IsNullOrEmpty(payload.ClientBootloaderVersion)) existing.ClientBootloaderVersion = payload.ClientBootloaderVersion;
                    existing.Heartbeat = now;

                    var hasInputUpdate = !string.IsNullOrEmpty(payload.InputStatus);
                    var hasDisplayUpdate = payload.HdmiConnected.HasValue;
                    var hasManifestVersion = !string.IsNullOrEmpty(payload.ManifestVersion);

                    if (hasInputUpdate || hasDisplayUpdate || hasManifestVersion)
                    {
                        existing.ExtendedData = ExtendedDataHelper.UpdateProperty<DeviceExtendedData>(
                            existing.ExtendedData,
                            ext =>
                            {
                                if (hasManifestVersion)
                                {
                                    ext.ContentSync ??= new ContentSyncData();
                                    ext.ContentSync.ManifestVersion = payload.ManifestVersion;
                                    ext.ContentSync.ReportedAt = now;
                                }

                                if (hasInputUpdate)
                                {
                                    ext.InputStatus ??= new InputStatusData();
                                    ext.InputStatus.Status = payload.InputStatus;
                                    if (payload.InputStatusChangedAt.HasValue)
                                        ext.InputStatus.ChangedAt = DateTimeOffset.FromUnixTimeMilliseconds(payload.InputStatusChangedAt.Value).UtcDateTime;
                                }

                                if (hasDisplayUpdate)
                                {
                                    ext.ConnectedDisplays ??= new ConnectedDisplaysData();
                                    var displayInfo = new[]
                                    {
                                        new
                                        {
                                            output = "HDMI-1",
                                            connected = payload.HdmiConnected!.Value,
                                            powered = payload.HdmiPowered,
                                            resolution = payload.HdmiResolution,
                                            manufacturer = payload.HdmiEdid?.Manufacturer,
                                            model = payload.HdmiEdid?.ProductName,
                                            serialNumber = payload.HdmiEdid?.SerialNumber
                                        }
                                    };
                                    ext.ConnectedDisplays.Displays = System.Text.Json.JsonSerializer.Serialize(displayInfo);
                                    if (payload.HdmiLastChanged.HasValue)
                                        ext.ConnectedDisplays.LastChanged = DateTimeOffset.FromUnixTimeMilliseconds(payload.HdmiLastChanged.Value).UtcDateTime;
                                }
                            });
                    }

                    if (!string.IsNullOrEmpty(payload.PropertiesJson))
                    {
                        try
                        {
                            using var propsDoc = JsonDocument.Parse(payload.PropertiesJson);
                            if (propsDoc.RootElement.TryGetProperty("browserUserAgent", out var uaProp)
                                && uaProp.ValueKind == JsonValueKind.String)
                            {
                                var ua = uaProp.GetString();
                                if (!string.IsNullOrEmpty(ua))
                                {
                                    var (browserType, browserVersion) = ParseUserAgent(ua);
                                    existing.ExtendedData = ExtendedDataHelper.UpdateProperty<DeviceExtendedData>(
                                        existing.ExtendedData,
                                        ext =>
                                        {
                                            ext.Browser ??= new BrowserData();
                                            ext.Browser.UserAgent = ua;
                                            ext.Browser.Type = browserType;
                                            ext.Browser.Version = browserVersion;
                                        });
                                }
                            }

                            CaptureTimeZoneFingerprint(existing, propsDoc.RootElement);
                            CaptureProvisioningFingerprint(existing, propsDoc.RootElement);
                        }
                        catch { }
                    }

                    // A takeover finishes here and nowhere else: the grant is an ordinary device
                    // token with no redemption step, so the first heartbeat that resolves this row
                    // to the native platform is the only observable "it converted".
                    if (existing.PlatformId == null)
                    {
                        existing.PlatformId = ResolvePlatformId(payload.ClientPlatform, platformLookup);
                        if (existing.PlatformId != null && IsNativeBrightSign(payload.ClientPlatform)
                            && !string.IsNullOrEmpty(existing.SerialNumber))
                            converted[existing.SerialNumber] = existing.DeviceId;
                    }

                    // Recorded, not acted on. Flavour is written from an approved pairing request,
                    // so a heartbeat that contradicts the enrolled one is a fault for a human
                    // rather than a value to adopt. Storing the declaration is what lets the
                    // mismatch be derived on read instead of stored and maintained.
                    if (!string.IsNullOrEmpty(payload.ClientPlatform))
                    {
                        existing.ExtendedData = ExtendedDataHelper.UpdateProperty<DeviceExtendedData>(
                            existing.ExtendedData,
                            ext =>
                            {
                                ext.DeclaredFlavour ??= new DeclaredFlavourData();
                                ext.DeclaredFlavour.PlatformName = payload.ClientPlatform;
                                ext.DeclaredFlavour.ReportedAt = now;
                            });
                    }
                }
                // Auto-create needs the inventory fields; a light beat from an unknown device
                // is skipped here and the PlayerContent response asks it to send full details
                else if (!string.IsNullOrEmpty(payload.SerialNumber) || !string.IsNullOrEmpty(payload.Manufacturer))
                {
                    // A player DXSuite has never seen before, which is the ordinary shape of a
                    // takeover: the serial was armed while it still belonged to another system.
                    if (IsNativeBrightSign(payload.ClientPlatform) && !string.IsNullOrEmpty(payload.SerialNumber)
                        && ResolvePlatformId(payload.ClientPlatform, platformLookup) != null)
                        converted[payload.SerialNumber] = payload.DeviceId;

                    newDevices.Add(DeviceProvisioning.Seed(new TblDevice
                    {
                        DeviceId = payload.DeviceId,
                        Manufacturer = payload.Manufacturer,
                        Model = payload.Model,
                        SerialNumber = payload.SerialNumber,
                        Ipaddress = payload.IpAddress,
                        Macaddress = payload.MacAddress,
                        Osversion = payload.OsVersion,
                        CustomerId = customerCode,
                        StorageType = payload.StorageType,
                        StorageSize = payload.StorageSize,
                        StorageHealth = payload.StorageHealth,
                        PlatformId = ResolvePlatformId(payload.ClientPlatform, platformLookup),
                        ClientAppVersion = payload.ClientAppVersion,
                        CreateDate = now,
                        Heartbeat = now
                    }));
                }
            }

            if (newDevices.Count > 0)
            {
                await db.TblDevices.AddRangeAsync(newDevices);

                var newPairs = newDevices
                    .Where(d => !string.IsNullOrEmpty(d.Manufacturer) && !string.IsNullOrEmpty(d.Model))
                    .Select(d => (d.Manufacturer!, d.Model!))
                    .Distinct()
                    .ToList();

                foreach (var (mfr, mdl) in newPairs)
                {
                    bool exists = await db.TblDeviceModelConfigs
                        .AnyAsync(c => c.Manufacturer == mfr && c.Model == mdl);
                    if (!exists)
                        db.TblDeviceModelConfigs.Add(new TblDeviceModelConfig
                        {
                            Manufacturer = mfr,
                            Model = mdl,
                            CreateDate = now
                        });
                }
            }

            await db.SaveChangesAsync();

            await RecordConversionsAsync(db, converted, now);
        }

        /// <summary>
        /// Closes the authorisations whose serials just converted.
        ///
        /// Without this the row stays Armed for the life of its expiry, and "how many armed serials
        /// have converted", the one question this table exists to answer, reads zero through a
        /// takeover that worked. Runtime behaviour never depended on it: the recovery endpoint
        /// derives conversion from the device row. This is the operator's view of the same fact.
        /// </summary>
        private async Task RecordConversionsAsync(
            IDirectorDBContext db, Dictionary<string, string> converted, DateTime now)
        {
            if (converted.Count == 0)
                return;

            var serials = converted.Keys.ToList();

            var authorizations = await db.TblProvisioningAuthorizations
                .Where(a => serials.Contains(a.SerialNumber)
                         && a.State == ProvisioningAuthorizationStates.Armed)
                .ToListAsync();

            if (authorizations.Count == 0)
                return;

            foreach (var authorization in authorizations)
            {
                authorization.State = ProvisioningAuthorizationStates.Converted;
                authorization.ConvertedDate = now;
                authorization.ConvertedDeviceId = converted[authorization.SerialNumber];

                _logger.LogInformation(
                    "Serial {SerialNumber} converted into device {DeviceId}",
                    authorization.SerialNumber, authorization.ConvertedDeviceId);
            }

            await db.SaveChangesAsync();
        }

        private static bool IsNativeBrightSign(string? clientPlatform)
            => string.Equals(clientPlatform, RecoveryHandlerService.EngageBrightSignPlatformName,
                StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Extracts browser name and version from a User-Agent string.
        /// Handles Chrome, Electron, Firefox, Safari, Edge, and WebOS patterns.
        /// </summary>
        /// <summary>
    /// Records the zone fingerprint a player reports, when it reports one. Only BrightSign players
    /// running a bootloader that applies a pushed zone send this; everything else simply never has
    /// the fields, which reads as "not reported" rather than as a problem.
    /// </summary>
    private static void CaptureTimeZoneFingerprint(TblDevice existing, JsonElement properties)
    {
        if (!properties.TryGetProperty("tzOffsetJanuary", out var januaryProp)
            || !properties.TryGetProperty("tzOffsetJuly", out var julyProp))
            return;

        if (!januaryProp.TryGetDecimal(out var january) || !julyProp.TryGetDecimal(out var july))
            return;

        int? year = properties.TryGetProperty("tzYear", out var yearProp)
            && yearProp.TryGetInt32(out var parsedYear) ? parsedYear : null;

        existing.ExtendedData = ExtendedDataHelper.UpdateProperty<DeviceExtendedData>(
            existing.ExtendedData,
            ext =>
            {
                ext.TimeZoneFingerprint ??= new TimeZoneFingerprintData();
                ext.TimeZoneFingerprint.OffsetJanuaryHours = january;
                ext.TimeZoneFingerprint.OffsetJulyHours = july;
                ext.TimeZoneFingerprint.Year = year;
                ext.TimeZoneFingerprint.PosixString = ReadString(properties, "tzPosix");
                ext.TimeZoneFingerprint.AppliedLiteral = ReadString(properties, "tzApplied");
                ext.TimeZoneFingerprint.ReportedAt = DateTime.UtcNow;
            });
    }

    /// <summary>
    /// Resolves a device's platform from the platform it declares, and from nothing else.
    ///
    /// Matching on Manufacturer instead put every applet player reporting "BrightSign" onto the
    /// legacy native platform row, and TblDevice.PlatformId is write-once, so one wrong heartbeat
    /// was permanent. An unrecognised platform resolves to null deliberately: null is the only
    /// PlatformId that a DEVICE- or LOCATION-scoped deployment structurally cannot target, because
    /// those scopes compare AppVersion.PlatformId == device.PlatformId, which is never true of null.
    /// </summary>
    internal static int? ResolvePlatformId(string? clientPlatform, IReadOnlyDictionary<string, int> platformLookup)
        => !string.IsNullOrEmpty(clientPlatform) && platformLookup.TryGetValue(clientPlatform, out var platformId)
            ? platformId
            : null;

    /// <summary>
    /// Records which provisioning package a player came off, and treats a change as a
    /// re-provision.
    ///
    /// Without the fingerprint, "superseded" and "revoked" are labels with no teeth and nobody can
    /// answer which players in the field came off a leaked card. A change to it also means the
    /// card wrote its bootstrap DWS password back over whatever the device had rotated to, so the
    /// recorded rotation is now a password the player does not have. Clearing it puts the device
    /// back through the visible "enrolled but not rotated" state rather than leaving Sentinel
    /// authenticating with a stale secret and failing quietly.
    /// </summary>
    private static void CaptureProvisioningFingerprint(TblDevice existing, JsonElement properties)
    {
        var reported = ReadString(properties, "provisioningFingerprint");
        if (string.IsNullOrEmpty(reported) || reported == existing.ProvisioningFingerprint)
            return;

        existing.ProvisioningFingerprint = reported;
        existing.DwsPasswordProtected = null;
        existing.DwsPasswordRotatedAt = null;
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static (string? Type, string? Version) ParseUserAgent(string userAgent)
        {
            var patterns = new (string Name, string Pattern)[]
            {
                ("Electron", @"Electron/([\d.]+)"),
                ("Edge", @"Edg(?:e|A|iOS)?/([\d.]+)"),
                ("Chrome", @"Chrome/([\d.]+)"),
                ("Firefox", @"Firefox/([\d.]+)"),
                ("Safari", @"Version/([\d.]+).*Safari"),
            };

            foreach (var (name, pattern) in patterns)
            {
                var match = Regex.Match(userAgent, pattern);
                if (match.Success)
                    return (name, match.Groups[1].Value);
            }

            if (userAgent.Contains("WebOS", StringComparison.OrdinalIgnoreCase)
                || userAgent.Contains("Web0S", StringComparison.OrdinalIgnoreCase))
                return ("WebOS Browser", null);

            return (null, null);
        }

        public void Dispose()
        {
            _flushTimer?.Dispose();
            _flushLock.Dispose();
        }
    }
}
