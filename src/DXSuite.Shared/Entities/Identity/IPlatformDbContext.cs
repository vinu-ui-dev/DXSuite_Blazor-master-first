using Microsoft.EntityFrameworkCore;

namespace DXSuite.Shared.Entities.Identity;

public interface IPlatformDbContext
{
    DbSet<TblPlatformUser> TblPlatformUsers { get; set; }
    DbSet<TblPlatformUserTenantMembership> TblPlatformUserTenantMemberships { get; set; }
    DbSet<TblPlatformRole> TblPlatformRoles { get; set; }
    DbSet<TblPlatformUserRole> TblPlatformUserRoles { get; set; }
    DbSet<TblPlatformElevation> TblPlatformElevations { get; set; }
    DbSet<TblPlatformAuditLog> TblPlatformAuditLogs { get; set; }
    DbSet<TblTenantAccessNotice> TblTenantAccessNotices { get; set; }
    DbSet<TblTenantSettings> TblTenantSettings { get; set; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
