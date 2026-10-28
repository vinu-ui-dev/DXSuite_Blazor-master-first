using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Shared.Services.Identity;

public class PlatformElevationService : IPlatformElevationService
{
    private readonly IPlatformDbContext _db;
    private readonly IPlatformAuditService _audit;
    private readonly ILogger<PlatformElevationService> _logger;

    public PlatformElevationService(IPlatformDbContext db, IPlatformAuditService audit,
        ILogger<PlatformElevationService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<TblPlatformElevation> RequestElevationAsync(
        Guid platformUserId, string tenantId, string roleName, string reason,
        int durationMinutes, Guid requestedByPlatformUserId, string? ticketRef = null)
    {
        if (roleName is not PlatformRoles.TenantSupport and not PlatformRoles.TenantSupportWrite)
            throw new ArgumentException($"Elevation role must be {PlatformRoles.TenantSupport} or {PlatformRoles.TenantSupportWrite}.", nameof(roleName));

        // Enforce tenant-level elevation constraints before creating the row.
        var settings = await GetTenantSettingsAsync(tenantId);
        if (settings.ElevationMaxDurationMinutes.HasValue && durationMinutes > settings.ElevationMaxDurationMinutes.Value)
            throw new InvalidOperationException(
                $"Tenant {tenantId} limits elevation duration to {settings.ElevationMaxDurationMinutes} minutes. Requested: {durationMinutes} minutes.");

        var now = DateTime.UtcNow;
        var elevation = new TblPlatformElevation
        {
            PlatformUserId          = platformUserId,
            TenantId                = tenantId,
            RoleName                = roleName,
            Reason                  = reason,
            TicketRef               = ticketRef,
            Status                  = ElevationStatus.Pending,
            IssuedAt                = now,
            ExpiresAt               = now.AddMinutes(durationMinutes),
            RequestedByPlatformUserId = requestedByPlatformUserId
        };

        _db.TblPlatformElevations.Add(elevation);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Elevation requested: {ElevationId} by {RequestedBy} for {PlatformUserId} → tenant {TenantId} role {Role}",
            elevation.ElevationId, requestedByPlatformUserId, platformUserId, tenantId, roleName);

        return elevation;
    }

    public async Task<TblPlatformElevation> ApproveElevationAsync(
        Guid elevationId, Guid approvedByPlatformUserId)
    {
        var elevation = await _db.TblPlatformElevations.FindAsync(elevationId)
            ?? throw new InvalidOperationException($"Elevation {elevationId} not found.");

        if (elevation.Status != ElevationStatus.Pending)
            throw new InvalidOperationException($"Elevation {elevationId} is not in Pending status.");

        // Separation of duties: the requester cannot self-approve.
        if (elevation.RequestedByPlatformUserId == approvedByPlatformUserId)
            throw new InvalidOperationException("An operator cannot approve their own elevation request.");

        // For tenants that require explicit tenant-admin approval, the TenantApprovedAt
        // field must be set (via the tenant admin portal) before operator approval can activate.
        var settings = await GetTenantSettingsAsync(elevation.TenantId);
        if (settings.RequireTenantApprovalForElevation && elevation.TenantApprovedAt == null)
            throw new InvalidOperationException(
                $"Tenant {elevation.TenantId} requires explicit tenant-admin approval before this elevation can be activated. " +
                "Contact the tenant admin to record their approval.");

        elevation.Status = ElevationStatus.Active;
        elevation.ApprovedByPlatformUserId = approvedByPlatformUserId;

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Elevation approved: {ElevationId} approved by {ApprovedBy} for tenant {TenantId}",
            elevationId, approvedByPlatformUserId, elevation.TenantId);

        return elevation;
    }

    public async Task RevokeElevationAsync(Guid elevationId, Guid revokedByPlatformUserId)
    {
        var elevation = await _db.TblPlatformElevations.FindAsync(elevationId)
            ?? throw new InvalidOperationException($"Elevation {elevationId} not found.");

        if (elevation.Status == ElevationStatus.Revoked)
            return;

        elevation.Status                 = ElevationStatus.Revoked;
        elevation.RevokedAt              = DateTime.UtcNow;
        elevation.RevokedByPlatformUserId = revokedByPlatformUserId;

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Elevation revoked: {ElevationId} by {RevokedBy}", elevationId, revokedByPlatformUserId);
    }

    public async Task<TblPlatformElevation?> GetLiveElevationAsync(Guid platformUserId, string tenantId)
    {
        var now = DateTime.UtcNow;
        return await _db.TblPlatformElevations
            .AsNoTracking()
            .Where(e => e.PlatformUserId == platformUserId
                     && e.TenantId       == tenantId
                     && e.Status         == ElevationStatus.Active
                     && e.ExpiresAt      > now)
            .OrderByDescending(e => e.ExpiresAt)
            .FirstOrDefaultAsync();
    }

    public async Task<List<TblPlatformElevation>> GetPendingElevationsAsync()
        => await _db.TblPlatformElevations
            .AsNoTracking()
            .Where(e => e.Status == ElevationStatus.Pending)
            .Include(e => e.PlatformUser)
            .OrderBy(e => e.IssuedAt)
            .ToListAsync();

    public async Task<List<TblPlatformElevation>> GetElevationsForTenantAsync(string tenantId, int take = 50)
        => await _db.TblPlatformElevations
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .Include(e => e.PlatformUser)
            .OrderByDescending(e => e.IssuedAt)
            .Take(take)
            .ToListAsync();

    public async Task<TblTenantSettings> GetTenantSettingsAsync(string tenantId, Guid? callerPlatformUserId = null)
    {
        // When called from a tenant-admin context, callerPlatformUserId enforces membership at the
        // service layer. Operator-internal callers (e.g. RequestElevationAsync) omit the parameter.
        if (callerPlatformUserId.HasValue)
            await RequireTenantAdminAsync(callerPlatformUserId.Value, tenantId);

        var settings = await _db.TblTenantSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId);

        return settings ?? new TblTenantSettings { TenantId = tenantId };
    }

    public async Task<List<TblPlatformElevation>> GetPendingElevationsForTenantAsync(
        string tenantId, Guid callerPlatformUserId)
    {
        await RequireTenantAdminAsync(callerPlatformUserId, tenantId);

        return await _db.TblPlatformElevations
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Status == ElevationStatus.Pending)
            .Include(e => e.PlatformUser)
            .OrderBy(e => e.IssuedAt)
            .ToListAsync();
    }

    public async Task<TblPlatformElevation> ApproveTenantElevationAsync(
        Guid elevationId, Guid tenantAdminPlatformUserId)
    {
        var elevation = await _db.TblPlatformElevations.FindAsync(elevationId)
            ?? throw new InvalidOperationException($"Elevation {elevationId} not found.");

        await RequireTenantAdminAsync(tenantAdminPlatformUserId, elevation.TenantId);

        if (elevation.TenantApprovedAt.HasValue)
            throw new InvalidOperationException($"Elevation {elevationId} has already been approved by a tenant admin.");

        if (elevation.Status == ElevationStatus.Revoked)
            throw new InvalidOperationException($"Elevation {elevationId} has already been revoked.");

        elevation.TenantApprovedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.WriteAsync(
            AuditEventType.TenantElevationApproved,
            tenantAdminPlatformUserId,
            elevation.TenantId,
            elevationId: elevationId);

        _logger.LogInformation(
            "Elevation {ElevationId} tenant-approved by {TenantAdmin} for tenant {TenantId}",
            elevationId, tenantAdminPlatformUserId, elevation.TenantId);

        return elevation;
    }

    public async Task RejectTenantElevationAsync(
        Guid elevationId, Guid tenantAdminPlatformUserId, string reason)
    {
        var elevation = await _db.TblPlatformElevations.FindAsync(elevationId)
            ?? throw new InvalidOperationException($"Elevation {elevationId} not found.");

        await RequireTenantAdminAsync(tenantAdminPlatformUserId, elevation.TenantId);

        if (elevation.Status == ElevationStatus.Revoked)
            throw new InvalidOperationException($"Elevation {elevationId} is already revoked.");

        elevation.Status                  = ElevationStatus.Revoked;
        elevation.RevokedAt               = DateTime.UtcNow;
        elevation.RevokedByPlatformUserId = tenantAdminPlatformUserId;
        await _db.SaveChangesAsync();

        await _audit.WriteAsync(
            AuditEventType.TenantElevationRejected,
            tenantAdminPlatformUserId,
            elevation.TenantId,
            reason: reason,
            elevationId: elevationId);

        _logger.LogInformation(
            "Elevation {ElevationId} rejected by tenant admin {TenantAdmin} for tenant {TenantId}. Reason: {Reason}",
            elevationId, tenantAdminPlatformUserId, elevation.TenantId, reason);
    }

    public async Task UpdateTenantSettingsAsync(TblTenantSettings settings, Guid actingPlatformUserId)
    {
        // RequireTenantAdminAsync is the authoritative security boundary — any caller must hold
        // IsTenantAdmin for the target tenant. Page-level IsTenantAdmin check is UX only.
        await RequireTenantAdminAsync(actingPlatformUserId, settings.TenantId);

        var existing = await _db.TblTenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == settings.TenantId);

        if (existing is null)
        {
            _db.TblTenantSettings.Add(settings);
        }
        else
        {
            existing.RequireTenantApprovalForElevation = settings.RequireTenantApprovalForElevation;
            existing.ElevationMaxDurationMinutes       = settings.ElevationMaxDurationMinutes;
            existing.NotifyOnElevationEmailsJson       = settings.NotifyOnElevationEmailsJson;
            existing.BlockedRegionsJson                = settings.BlockedRegionsJson;
        }

        await _db.SaveChangesAsync();

        await _audit.WriteAsync(
            AuditEventType.TenantSettingsUpdated,
            actingPlatformUserId,
            settings.TenantId);

        _logger.LogInformation(
            "Tenant settings updated for {TenantId} by {ActingUser}", settings.TenantId, actingPlatformUserId);
    }

    private async Task RequireTenantAdminAsync(Guid platformUserId, string tenantId)
    {
        var membership = await _db.TblPlatformUserTenantMemberships
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.PlatformUserId == platformUserId
                                   && m.TenantId       == tenantId
                                   && m.IsActive);

        if (membership is null || !membership.IsTenantAdmin)
            throw new UnauthorizedAccessException(
                $"User {platformUserId} does not hold IsTenantAdmin for tenant {tenantId}.");
    }
}
