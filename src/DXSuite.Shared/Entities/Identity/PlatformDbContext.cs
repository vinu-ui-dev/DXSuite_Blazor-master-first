using Microsoft.EntityFrameworkCore;

namespace DXSuite.Shared.Entities.Identity;

/// <summary>
/// DbContext for the central DXSuite_Platform database.
/// Connects to a single shared DB — not per-tenant. Registered conditionally
/// only when the Platform connection string is present in appsettings.
/// </summary>
public class PlatformDbContext : DbContext, IPlatformDbContext
{
    public PlatformDbContext(DbContextOptions<PlatformDbContext> options) : base(options) { }

    public DbSet<TblPlatformUser> TblPlatformUsers { get; set; } = null!;
    public DbSet<TblPlatformUserTenantMembership> TblPlatformUserTenantMemberships { get; set; } = null!;
    public DbSet<TblPlatformRole> TblPlatformRoles { get; set; } = null!;
    public DbSet<TblPlatformUserRole> TblPlatformUserRoles { get; set; } = null!;
    public DbSet<TblPlatformElevation> TblPlatformElevations { get; set; } = null!;
    public DbSet<TblPlatformAuditLog> TblPlatformAuditLogs { get; set; } = null!;
    public DbSet<TblTenantAccessNotice> TblTenantAccessNotices { get; set; } = null!;
    public DbSet<TblTenantSettings> TblTenantSettings { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TblPlatformUser>(entity =>
        {
            entity.HasKey(e => e.PlatformUserId);
            entity.Property(e => e.PlatformUserId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.CreatedDate).HasDefaultValueSql("(getutcdate())");
            entity.HasIndex(e => e.PrimaryEmail)
                .IsUnique()
                .HasDatabaseName("UQ_PlatformUser_PrimaryEmail");
        });

        modelBuilder.Entity<TblPlatformUserTenantMembership>(entity =>
        {
            entity.HasKey(e => e.MembershipId);
            entity.Property(e => e.MembershipId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.GrantedDate).HasDefaultValueSql("(getutcdate())");
            entity.HasIndex(e => new { e.PlatformUserId, e.TenantId })
                .IsUnique()
                .HasDatabaseName("UQ_PlatformMembership_UserTenant");
            entity.HasOne(e => e.PlatformUser)
                .WithMany(u => u.TenantMemberships)
                .HasForeignKey(e => e.PlatformUserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_PlatformMembership_PlatformUser");
        });

        modelBuilder.Entity<TblPlatformRole>(entity =>
        {
            entity.HasKey(e => e.RoleId);
            entity.HasIndex(e => e.RoleName)
                .IsUnique()
                .HasDatabaseName("UQ_PlatformRole_RoleName");

            // Seed the four purpose-bound platform roles. RoleId values are stable — never reuse.
            entity.HasData(
                new TblPlatformRole { RoleId = 1, RoleName = PlatformRoles.PlatformOperator,   Description = "Manage tenants, operators, and billing. Cannot read tenant business data." },
                new TblPlatformRole { RoleId = 2, RoleName = PlatformRoles.TenantSupport,      Description = "Time-bound read access into a specific tenant for support purposes." },
                new TblPlatformRole { RoleId = 3, RoleName = PlatformRoles.TenantSupportWrite, Description = "Time-bound read+write access into a specific tenant. Requires second approver." },
                new TblPlatformRole { RoleId = 4, RoleName = PlatformRoles.Auditor,            Description = "Read-only access to platform audit logs across all tenants. No tenant data access." }
            );
        });

        modelBuilder.Entity<TblPlatformUserRole>(entity =>
        {
            entity.HasKey(e => e.PlatformUserRoleId);
            entity.Property(e => e.PlatformUserRoleId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.GrantedDate).HasDefaultValueSql("(getutcdate())");
            entity.HasIndex(e => new { e.PlatformUserId, e.RoleId })
                .IsUnique()
                .HasDatabaseName("UQ_PlatformUserRole");
            entity.HasOne(e => e.PlatformUser)
                .WithMany()
                .HasForeignKey(e => e.PlatformUserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_PlatformUserRole_User");
            entity.HasOne(e => e.Role)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_PlatformUserRole_Role");
        });

        modelBuilder.Entity<TblPlatformElevation>(entity =>
        {
            entity.HasKey(e => e.ElevationId);
            entity.Property(e => e.ElevationId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.IssuedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Status).HasDefaultValue(ElevationStatus.Pending);
            entity.HasIndex(e => new { e.PlatformUserId, e.TenantId, e.ExpiresAt })
                .HasDatabaseName("IX_PlatformElevation_UserTenantExpiry");
            entity.HasOne(e => e.PlatformUser)
                .WithMany()
                .HasForeignKey(e => e.PlatformUserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_PlatformElevation_User");
        });

        modelBuilder.Entity<TblPlatformAuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditId);
            entity.Property(e => e.AuditId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.EventTime).HasDefaultValueSql("(getutcdate())");
            // No Update/Delete operations are issued by the application. The SQL role for the
            // platform DB application account should enforce INSERT-only on this table.
        });

        modelBuilder.Entity<TblTenantAccessNotice>(entity =>
        {
            entity.HasKey(e => e.NoticeId);
            entity.Property(e => e.NoticeId).HasDefaultValueSql("(newid())");
            entity.Property(e => e.EventTime).HasDefaultValueSql("(getutcdate())");
            entity.HasIndex(e => new { e.TenantId, e.EventTime })
                .HasDatabaseName("IX_TenantAccessNotice_TenantTime");
        });

        modelBuilder.Entity<TblTenantSettings>(entity =>
        {
            entity.HasKey(e => e.TenantSettingsId);
            entity.HasIndex(e => e.TenantId)
                .IsUnique()
                .HasDatabaseName("UQ_TenantSettings_TenantId");
        });
    }
}
