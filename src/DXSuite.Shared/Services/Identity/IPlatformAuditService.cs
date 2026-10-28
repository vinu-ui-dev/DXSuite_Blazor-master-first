using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Shared.Services.Identity;

public interface IPlatformAuditService
{
    /// <summary>
    /// Appends an audit event to TblPlatformAuditLog and, when a tenantId is present,
    /// also writes a corresponding TblTenantAccessNotice for tenant visibility.
    /// Never throws — audit failures must not block the caller.
    /// </summary>
    Task WriteAsync(
        string eventType,
        Guid? platformUserId,
        string? tenantId,
        string? resourcePath = null,
        string? ticketRef    = null,
        string? reason       = null,
        string? detailsJson  = null,
        Guid? elevationId    = null,
        string? operatorDisplayName = null);

    /// <summary>Returns recent audit log entries, optionally scoped to a tenant.</summary>
    Task<List<TblPlatformAuditLog>> GetAuditLogAsync(string? tenantId = null, int take = 100);

    /// <summary>Returns tenant-visible access notices for the Vendor Access Log UI.</summary>
    Task<List<TblTenantAccessNotice>> GetTenantAccessNoticesAsync(string tenantId, int take = 100);
}
