using Microsoft.EntityFrameworkCore;

namespace DXSuite.CoreData.Entities;

public class CoreDataDbContext : DbContext
{
    public CoreDataDbContext(DbContextOptions<CoreDataDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<TblApiKey> TblApiKeys { get; set; }
    public virtual DbSet<TblCountryLanguage> TblCountryLanguages { get; set; }
    public virtual DbSet<TblEventLog> TblEventLogs { get; set; }
    public virtual DbSet<TblEventType> TblEventTypes { get; set; }
    public virtual DbSet<TblLanguage> TblLanguages { get; set; }
    public virtual DbSet<TblSystemHealth> TblSystemHealths { get; set; }
    public virtual DbSet<TblWeather> TblWeathers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TblApiKey>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<TblCountryLanguage>(entity =>
        {
            entity.HasKey(e => e.CountryCode).HasName("PK__tblCount__5D9B0D2DBCCAA3FC");
        });

        modelBuilder.Entity<TblEventLog>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.EventType).WithMany(p => p.TblEventLogs)
                .HasConstraintName("FK_tblEventLog_tblEventType");
        });

        modelBuilder.Entity<TblEventType>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<TblSystemHealth>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<TblWeather>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
        });
    }
}
