using Microsoft.EntityFrameworkCore;

namespace DXSuite.Shared.Entities.Registry;

public interface ITenantRegistryDbContext
{
    DbSet<TblTenant> TblTenants { get; set; }
    DbSet<TblApiKeyMapping> TblApiKeyMappings { get; set; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
