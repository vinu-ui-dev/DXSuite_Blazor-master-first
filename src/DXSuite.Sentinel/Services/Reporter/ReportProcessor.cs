using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DXSuite.Sentinel.Entities;
using DXSuite.Shared.Services;

namespace DXSuite.Sentinel.Services.Reporter;

/// <summary>
/// Processes pending remediation events and decides what to communicate:
/// 1. EVALUATE - Transient? Mass outage? Recurring? New incident?
/// 2. AGGREGATE - Group related events within time window
/// 3. GENERATE - Create tickets, advisories, or daily digests
/// 4. TRACK - Monitor ticket lifecycle, auto-resolve on recovery
/// </summary>
public class ReportProcessor : IReportProcessor
{
    private readonly IItsmAdapterFactory _itsmFactory;
    private readonly ILlmGateway _llmGateway;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReportProcessor> _logger;

    // Mass outage detection thresholds
    private static readonly TimeSpan AggregationWindow = TimeSpan.FromMinutes(5);
    private static readonly int MassOutageDeviceThreshold = 3;

    // Watermark: tracks the last processed RemediationLog ID to avoid re-processing
    private long _lastProcessedLogId = -1;

    public ReportProcessor(
        IItsmAdapterFactory itsmFactory,
        ILlmGateway llmGateway,
        IServiceScopeFactory scopeFactory,
        ILogger<ReportProcessor> logger)
    {
        _itsmFactory = itsmFactory;
        _llmGateway = llmGateway;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ProcessPendingEventsAsync(CancellationToken ct = default)
    {
        // 1. Load unprocessed remediation logs
        var pendingEvents = await GetPendingEventsAsync(ct);
        if (pendingEvents.Count == 0) return;

        _logger.LogDebug("Processing {Count} pending remediation events", pendingEvents.Count);

        // 2. Check for mass outage patterns
        var outageGroups = DetectMassOutages(pendingEvents);
        foreach (var group in outageGroups)
        {
            await HandleMassOutageAsync(group, ct);
        }

        // 3. Process individual events not part of a mass outage
        var individualEvents = pendingEvents
            .Where(e => !outageGroups.Any(g => g.EventIds.Contains(e.LogId)))
            .ToList();

        foreach (var evt in individualEvents)
        {
            if (ct.IsCancellationRequested) break;
            await HandleIndividualEventAsync(evt, ct);
        }

        // Update watermark to the max processed LogId
        var maxLogId = pendingEvents.Max(e => e.LogId);
        if (maxLogId > _lastProcessedLogId)
            _lastProcessedLogId = maxLogId;

        // 4. Check for auto-resolve (devices that have recovered)
        await CheckAutoResolveAsync(ct);
    }

    private async Task<List<PendingEvent>> GetPendingEventsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

        // On first call, initialize watermark to current max to skip historical backlog
        if (_lastProcessedLogId < 0)
        {
            _lastProcessedLogId = await sentinelDb.RemediationLogs
                .AsNoTracking()
                .OrderByDescending(r => r.LogId)
                .Select(r => r.LogId)
                .FirstOrDefaultAsync(ct);
            return new List<PendingEvent>();
        }

        var logs = await sentinelDb.RemediationLogs
            .AsNoTracking()
            .Include(r => r.DeviceInstance)
            .Where(r => r.LogId > _lastProcessedLogId
                && (r.Result == "Failed" || r.Result == "Escalated"))
            .OrderBy(r => r.LogId)
            .ToListAsync(ct);

        return logs.Select(r => new PendingEvent
        {
            LogId = r.LogId,
            DeviceInstanceId = r.DeviceInstanceId,
            DeviceName = r.DeviceInstance?.DisplayName ?? $"Device {r.DeviceInstanceId}",
            CustomerId = r.DeviceInstance?.CustomerId ?? string.Empty,
            LocationId = r.DeviceInstance?.LocationId,
            TriggerCondition = r.TriggerCondition,
            Outcome = r.Result,
            Details = r.ResultDetails ?? string.Empty,
            CreateDate = r.CreateDate
        }).ToList();
    }

    private List<OutageGroup> DetectMassOutages(List<PendingEvent> events)
    {
        var groups = new List<OutageGroup>();

        // Group events by location within the aggregation window
        var locationGroups = events
            .Where(e => e.LocationId.HasValue)
            .GroupBy(e => e.LocationId!.Value)
            .Where(g => g.Count() >= MassOutageDeviceThreshold);

        foreach (var locationGroup in locationGroups)
        {
            groups.Add(new OutageGroup
            {
                Type = "location-mass-outage",
                Scope = $"Location {locationGroup.Key}",
                EventIds = locationGroup.Select(e => e.LogId).ToList(),
                DeviceIds = locationGroup.Select(e => e.DeviceInstanceId).Distinct().ToList(),
                AffectedDeviceCount = locationGroup.Select(e => e.DeviceInstanceId).Distinct().Count(),
                CustomerId = locationGroup.First().CustomerId
            });
        }

        return groups;
    }

    private async Task HandleMassOutageAsync(OutageGroup group, CancellationToken ct)
    {
        _logger.LogInformation("Mass outage detected: {Type} at {Scope} ({Count} devices)",
            group.Type, group.Scope, group.AffectedDeviceCount);

        var adapter = await _itsmFactory.GetAdapterAsync(group.CustomerId, ct);
        if (adapter == null)
        {
            _logger.LogWarning("No ITSM adapter for customer {CustomerId}, mass outage logged but no ticket created", group.CustomerId);
            return;
        }

        var deviceIds = string.Join(",", group.DeviceIds);
        var ticket = new TicketRequest
        {
            Title = $"Mass Outage: {group.AffectedDeviceCount} devices affected at {group.Scope}",
            Description = $"Sentinel has detected {group.AffectedDeviceCount} devices experiencing issues at {group.Scope}.\n\nThis appears to be a {group.Type}. Aggregated event IDs: {string.Join(", ", group.EventIds)}",
            Priority = "high",
            Category = "Digital Signage",
            LocationName = group.Scope
        };

        var result = await adapter.CreateTicketAsync(ticket, ct);
        if (result.Success)
        {
            _logger.LogInformation("Mass outage ticket created: {TicketId}", result.TicketId);

            using var scope = _scopeFactory.CreateScope();
            var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

            // Create outage correlation record
            var correlation = new OutageCorrelation
            {
                CustomerId = group.CustomerId,
                CorrelationType = group.Type,
                Scope = group.Scope.Length > 200 ? group.Scope[..200] : group.Scope,
                AffectedDeviceCount = group.AffectedDeviceCount,
                DeviceInstanceIds = deviceIds,
                DetectedAt = DateTime.UtcNow,
                Status = "active"
            };
            sentinelDb.OutageCorrelations.Add(correlation);
            await sentinelDb.SaveChangesAsync(ct);

            // Create ticket tracking linked to the correlation
            var tracking = new TicketTracking
            {
                CustomerId = group.CustomerId,
                ExternalTicketId = result.TicketId ?? string.Empty,
                ExternalTicketUrl = result.TicketUrl,
                ItsmPlatform = adapter.PlatformId,
                Status = "open",
                Title = ticket.Title.Length > 200 ? ticket.Title[..200] : ticket.Title,
                DeviceInstanceIds = deviceIds,
                OutageCorrelationId = correlation.CorrelationId,
                CreateDate = DateTime.UtcNow
            };
            sentinelDb.TicketTrackings.Add(tracking);
            await sentinelDb.SaveChangesAsync(ct);
        }
    }

    private async Task HandleIndividualEventAsync(PendingEvent evt, CancellationToken ct)
    {
        // Only create tickets for failed or escalated events
        if (evt.Outcome != "Failed" && evt.Outcome != "Escalated")
            return;

        var adapter = await _itsmFactory.GetAdapterAsync(evt.CustomerId, ct);
        if (adapter == null) return;

        using var scope = _scopeFactory.CreateScope();
        var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

        // Dedup: check if an open ticket already exists for this device
        var deviceIdStr = evt.DeviceInstanceId.ToString();
        var existingTicket = await sentinelDb.TicketTrackings
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Status == "open"
                && t.ResolvedAt == null
                && t.DeviceInstanceIds != null
                && t.DeviceInstanceIds.Contains(deviceIdStr), ct);

        if (existingTicket != null)
        {
            _logger.LogDebug("Open ticket {TicketId} already exists for device {DeviceId}, skipping",
                existingTicket.ExternalTicketId, evt.DeviceInstanceId);
            return;
        }

        var ticket = new TicketRequest
        {
            Title = $"Device Alert: {evt.DeviceName} - {evt.TriggerCondition}",
            Description = $"Sentinel Service Tech evaluation result:\n\n**Device:** {evt.DeviceName}\n**Condition:** {evt.TriggerCondition}\n**Actions attempted:** {evt.Details}\n**Outcome:** {evt.Outcome}",
            Priority = evt.Outcome == "Escalated" ? "high" : "medium",
            Category = "Digital Signage",
            AffectedDevices = new List<string> { deviceIdStr }
        };

        if (!string.IsNullOrEmpty(evt.FloorplanUrl))
        {
            ticket.FloorplanUrl = evt.FloorplanUrl;
            ticket.Description += $"\n\n**Floorplan:** {evt.FloorplanUrl}";
        }

        var result = await adapter.CreateTicketAsync(ticket, ct);
        if (result.Success)
        {
            _logger.LogInformation("Ticket created for device {DeviceId}: {TicketId}", evt.DeviceInstanceId, result.TicketId);

            var tracking = new TicketTracking
            {
                CustomerId = evt.CustomerId,
                ExternalTicketId = result.TicketId ?? string.Empty,
                ExternalTicketUrl = result.TicketUrl,
                ItsmPlatform = adapter.PlatformId,
                Status = "open",
                Title = ticket.Title.Length > 200 ? ticket.Title[..200] : ticket.Title,
                DeviceInstanceIds = deviceIdStr,
                CreateDate = DateTime.UtcNow
            };
            sentinelDb.TicketTrackings.Add(tracking);
            await sentinelDb.SaveChangesAsync(ct);
        }
    }

    private async Task CheckAutoResolveAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

            var openTickets = await sentinelDb.TicketTrackings
                .Where(t => t.Status == "open" && t.ResolvedAt == null)
                .ToListAsync(ct);

            foreach (var ticket in openTickets)
            {
                if (ct.IsCancellationRequested) break;
                if (string.IsNullOrEmpty(ticket.DeviceInstanceIds)) continue;

                var deviceIds = ticket.DeviceInstanceIds
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var id) ? id : 0)
                    .Where(id => id > 0)
                    .ToList();

                if (deviceIds.Count == 0) continue;

                // Check if ALL linked devices are now healthy
                var allHealthy = true;
                foreach (var deviceId in deviceIds)
                {
                    var stateJson = await sentinelDb.DeviceInstances
                        .AsNoTracking()
                        .Where(d => d.DeviceInstanceId == deviceId)
                        .Select(d => d.LastStateJson)
                        .FirstOrDefaultAsync(ct);

                    if (string.IsNullOrEmpty(stateJson))
                    {
                        allHealthy = false;
                        break;
                    }

                    try
                    {
                        var state = JsonSerializer.Deserialize<DeviceState>(stateJson);
                        if (state == null || state.OverallHealth != OverallHealthStatus.Healthy)
                        {
                            allHealthy = false;
                            break;
                        }
                    }
                    catch (JsonException)
                    {
                        allHealthy = false;
                        break;
                    }
                }

                if (!allHealthy) continue;

                // All devices recovered -- resolve the ticket
                var adapter = await _itsmFactory.GetAdapterAsync(ticket.CustomerId, ct);
                if (adapter != null)
                {
                    var resolution = new ItsmResolutionInfo
                    {
                        ResolutionNote = "All affected devices have recovered to healthy status.",
                        ResolvedBy = "Sentinel Auto-Resolve",
                        AutoResolved = true
                    };
                    await adapter.ResolveTicketAsync(ticket.ExternalTicketId, resolution, ct);
                }

                ticket.Status = "resolved";
                ticket.ResolvedAt = DateTime.UtcNow;
                ticket.LastUpdated = DateTime.UtcNow;

                _logger.LogInformation("Auto-resolved ticket {TicketId} -- all devices recovered",
                    ticket.ExternalTicketId);
            }

            await sentinelDb.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during auto-resolve check");
        }
    }
}

// Supporting models
public class PendingEvent
{
    public long LogId { get; set; }
    public int DeviceInstanceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public int? LocationId { get; set; }
    public string TriggerCondition { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? FloorplanUrl { get; set; }
    public DateTime CreateDate { get; set; }
}

public class OutageGroup
{
    public string Type { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public List<long> EventIds { get; set; } = new();
    public List<int> DeviceIds { get; set; } = new();
    public int AffectedDeviceCount { get; set; }
    public string CustomerId { get; set; } = string.Empty;
}
