using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Shared.Services.Identity;

public class PlatformAuditService : IPlatformAuditService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlatformAuditService> _logger;

    public PlatformAuditService(IServiceScopeFactory scopeFactory, ILogger<PlatformAuditService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task WriteAsync(
        string eventType,
        Guid? platformUserId,
        string? tenantId,
        string? resourcePath = null,
        string? ticketRef    = null,
        string? reason       = null,
        string? detailsJson  = null,
        Guid? elevationId    = null,
        string? operatorDisplayName = null)
    {
        try
        {
            // Create an independent scope so this task is not coupled to the HTTP request
            // scope lifetime. The scoped DbContext in the caller's scope may have already
            // disposed by the time the fire-and-forget task executes under load.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IPlatformDbContext>();

            var now = DateTime.UtcNow;

            db.TblPlatformAuditLogs.Add(new TblPlatformAuditLog
            {
                EventTime      = now,
                PlatformUserId = platformUserId,
                TenantId       = tenantId,
                EventType      = eventType,
                ResourcePath   = resourcePath,
                TicketRef      = ticketRef,
                Reason         = reason,
                DetailsJson    = detailsJson
            });

            // Mirror events with a tenant scope to TblTenantAccessNotice for tenant visibility.
            if (!string.IsNullOrEmpty(tenantId))
            {
                db.TblTenantAccessNotices.Add(new TblTenantAccessNotice
                {
                    TenantId            = tenantId,
                    EventTime           = now,
                    OperatorDisplayName = operatorDisplayName,
                    EventType           = eventType,
                    ElevationId         = elevationId,
                    TicketRef           = ticketRef,
                    Reason              = reason,
                    ResourcePath        = resourcePath
                });
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Audit log failures must not surface to the caller — the underlying request should proceed.
            _logger.LogError(ex, "Failed to write audit event {EventType} for tenant {TenantId}", eventType, tenantId);
        }
    }

    public async Task<List<TblPlatformAuditLog>> GetAuditLogAsync(string? tenantId = null, int take = 100)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IPlatformDbContext>();

        var query = db.TblPlatformAuditLogs.AsNoTracking();
        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(e => e.TenantId == tenantId);
        return await query.OrderByDescending(e => e.EventTime).Take(take).ToListAsync();
    }

    public async Task<List<TblTenantAccessNotice>> GetTenantAccessNoticesAsync(string tenantId, int take = 100)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IPlatformDbContext>();

        return await db.TblTenantAccessNotices
            .AsNoTracking()
            .Where(n => n.TenantId == tenantId)
            .OrderByDescending(n => n.EventTime)
            .Take(take)
            .ToListAsync();
    }
}
