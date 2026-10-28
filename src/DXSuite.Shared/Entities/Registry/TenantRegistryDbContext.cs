using Microsoft.EntityFrameworkCore;

namespace DXSuite.Shared.Entities.Registry;

public class TenantRegistryDbContext : DbContext, ITenantRegistryDbContext
{
    public TenantRegistryDbContext(DbContextOptions<TenantRegistryDbContext> options) : base(options) { }

    public DbSet<TblTenant> TblTenants { get; set; } = null!;
    public DbSet<TblApiKeyMapping> TblApiKeyMappings { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TblTenant>(e =>
        {
            e.HasIndex(t => t.Subdomain).IsUnique();
            e.Property(t => t.CreatedDate).HasDefaultValueSql("GETUTCDATE()");
            e.Property(t => t.LastEditDate).HasDefaultValueSql("GETUTCDATE()");
        });

        modelBuilder.Entity<TblApiKeyMapping>(e =>
        {
            // Unique index on ApiKeyHash enables O(1) tenant resolution from an API key.
            e.HasIndex(k => k.ApiKeyHash).IsUnique();
            e.HasIndex(k => k.TenantId);
            e.Property(k => k.CreatedDate).HasDefaultValueSql("GETUTCDATE()");
        });
    }
}
