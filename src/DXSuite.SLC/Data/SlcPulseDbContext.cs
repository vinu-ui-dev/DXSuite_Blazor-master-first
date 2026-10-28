using Microsoft.EntityFrameworkCore;

namespace DXSuite.SLC.Data;

/// <summary>
/// Minimal EF Core DbContext for reading Pulse DB views needed by the SLC Screenshots page.
/// Scoped per request via AddDbContextFactory so each circuit resolves the correct tenant
/// connection string at activation time rather than at startup.
/// </summary>
public class SlcPulseDbContext : DbContext
{
    public SlcPulseDbContext()
    {
    }

    public SlcPulseDbContext(DbContextOptions<SlcPulseDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<ViewLastMonitor> ViewLastMonitors { get; set; }

    public virtual DbSet<ViewAllScreenshot> ViewAllScreenshots { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Screenshot records in ViewLastMonitors are identified by MonitorItemId == 10.
        // Filter to MonitorItemId == 10 in the query layer, not here.
        modelBuilder.Entity<ViewLastMonitor>(entity =>
        {
            entity.ToView("View_LastMonitor");
        });

        // ViewAllScreenshots contains all historical screenshot readings.
        // Filter to MonitorItemId == 10 in the query layer, not here.
        modelBuilder.Entity<ViewAllScreenshot>(entity =>
        {
            entity.ToView("View_AllScreenshots");
        });
    }
}
