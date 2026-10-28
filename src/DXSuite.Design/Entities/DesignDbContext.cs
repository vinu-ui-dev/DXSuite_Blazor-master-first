using Microsoft.EntityFrameworkCore;

namespace DXSuite.Design.Entities;

public class DesignDbContext : DbContext
{
    public DesignDbContext() { }
    public DesignDbContext(DbContextOptions<DesignDbContext> options) : base(options) { }

    // Phase B
    public virtual DbSet<TblDesignNetsuiteConnection> TblDesignNetsuiteConnections { get; set; }

    // Phase C — Catalog
    public virtual DbSet<TblDesignCategoryGroup> TblDesignCategoryGroups { get; set; }
    public virtual DbSet<TblDesignLaborCategory> TblDesignLaborCategories { get; set; }
    public virtual DbSet<TblDesignCategory> TblDesignCategories { get; set; }
    public virtual DbSet<TblDesignLaborType> TblDesignLaborTypes { get; set; }
    public virtual DbSet<TblDesignAttribute> TblDesignAttributes { get; set; }
    public virtual DbSet<TblDesignAttributeOption> TblDesignAttributeOptions { get; set; }
    public virtual DbSet<TblDesignCategoryLaborAssignment> TblDesignCategoryLaborAssignments { get; set; }
    public virtual DbSet<TblDesignCategoryAttribute> TblDesignCategoryAttributes { get; set; }
    public virtual DbSet<TblDesignCatalogItem> TblDesignCatalogItems { get; set; }
    public virtual DbSet<TblDesignCatalogItemLabor> TblDesignCatalogItemLabors { get; set; }
    public virtual DbSet<TblDesignCatalogItemAttribute> TblDesignCatalogItemAttributes { get; set; }

    // Phase I — Labor Columns
    public virtual DbSet<TblDesignLaborColumn> TblDesignLaborColumns { get; set; }
    public virtual DbSet<TblDesignLaborTypeColumn> TblDesignLaborTypeColumns { get; set; }
    public virtual DbSet<TblDesignCategoryColumnAssignment> TblDesignCategoryColumnAssignments { get; set; }
    public virtual DbSet<TblDesignCatalogItemColumn> TblDesignCatalogItemColumns { get; set; }
    public virtual DbSet<TblDesignWorksheetLaborCalculatorRow> TblDesignWorksheetLaborCalculatorRows { get; set; }

    // Phase J — Misc & Travel
    public virtual DbSet<TblDesignMiscItem> TblDesignMiscItems { get; set; }
    public virtual DbSet<TblDesignWorksheetMiscRow> TblDesignWorksheetMiscRows { get; set; }
    public virtual DbSet<TblDesignWorksheetTravelRow> TblDesignWorksheetTravelRows { get; set; }

    // Phase K — Description rows & PDF checkbox
    public virtual DbSet<TblDesignWorksheetSectionDescription> TblDesignWorksheetSectionDescriptions { get; set; }
    public virtual DbSet<TblDesignWorksheetGroupDescription> TblDesignWorksheetGroupDescriptions { get; set; }

    // Phase M — Materials
    public virtual DbSet<TblDesignMaterialsCategory> TblDesignMaterialsCategories { get; set; }
    public virtual DbSet<TblDesignMaterialsItem> TblDesignMaterialsItems { get; set; }
    public virtual DbSet<TblDesignWorksheetMaterialsRow> TblDesignWorksheetMaterialsRows { get; set; }
    public virtual DbSet<TblDesignWorksheetMaterialsCategoryConfig> TblDesignWorksheetMaterialsCategoryConfigs { get; set; }

    // Phase N-7 — Saved worksheet configurations
    public virtual DbSet<TblDesignWorksheetConfig> TblDesignWorksheetConfigs { get; set; }

    // Phase E — Designs & Worksheets
    public virtual DbSet<TblDesign> TblDesigns { get; set; }
    public virtual DbSet<TblDesignWorksheet> TblDesignWorksheets { get; set; }
    public virtual DbSet<TblDesignWorksheetRoom> TblDesignWorksheetRooms { get; set; }
    public virtual DbSet<TblDesignWorksheetLineItem> TblDesignWorksheetLineItems { get; set; }
    public virtual DbSet<TblDesignWorksheetLineItemAttr> TblDesignWorksheetLineItemAttrs { get; set; }
    public virtual DbSet<TblDesignWorksheetLineItemColumnHours> TblDesignWorksheetLineItemColumnHours { get; set; }
    public virtual DbSet<TblDesignWorksheetLaborRow> TblDesignWorksheetLaborRows { get; set; }
    public virtual DbSet<TblDesignWorksheetNonInventoryRow> TblDesignWorksheetNonInventoryRows { get; set; }
    public virtual DbSet<TblDesignTravelConfig> TblDesignTravelConfigs { get; set; }

    // Phase E — Requirements
    public virtual DbSet<TblDesignRequirement> TblDesignRequirements { get; set; }
    public virtual DbSet<TblDesignRequirementComment> TblDesignRequirementComments { get; set; }
    public virtual DbSet<TblDesignRequirementCategory> TblDesignRequirementCategories { get; set; }

    // MCP access tokens
    public virtual DbSet<TblDesignMcpToken> TblDesignMcpTokens { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── Phase B ──────────────────────────────────────────────────────────
        modelBuilder.Entity<TblDesignNetsuiteConnection>(e =>
        {
            e.HasIndex(c => new { c.CustomerId, c.IsActive });
        });

        // ── Phase C — Category groups & labor categories ─────────────────────
        modelBuilder.Entity<TblDesignCategoryGroup>(e =>
        {
            e.HasIndex(g => new { g.CustomerId, g.Name });
        });

        modelBuilder.Entity<TblDesignLaborCategory>(e =>
        {
            e.HasIndex(lc => new { lc.CustomerId, lc.Name });
        });

        modelBuilder.Entity<TblDesignCategory>(e =>
        {
            e.HasOne(c => c.CategoryGroup)
                .WithMany(g => g.Categories)
                .HasForeignKey(c => c.CategoryGroupId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Phase C — Enum string conversions ────────────────────────────────
        modelBuilder.Entity<TblDesignLaborType>(e =>
        {
            e.Property(l => l.SubRole).HasConversion<string>().HasMaxLength(50);
            e.Property(l => l.InternalCostPerHr).HasPrecision(18, 4);
            e.Property(l => l.InternalPricePerHr).HasPrecision(18, 4);
            e.Property(l => l.ColumnPercentage).HasPrecision(5, 2);
            e.Property(l => l.MinimumHours).HasPrecision(10, 2);
            e.HasIndex(l => new { l.CustomerId, l.IsActive });
        });

        modelBuilder.Entity<TblDesignAttribute>(e =>
        {
            e.Property(a => a.AttributeType).HasConversion<string>().HasMaxLength(50);
            e.HasIndex(a => a.CustomerId);
        });

        // ── Phase C — Cascade deletes ─────────────────────────────────────────
        modelBuilder.Entity<TblDesignAttributeOption>(e =>
        {
            e.HasOne(o => o.Attribute)
                .WithMany(a => a.Options)
                .HasForeignKey(o => o.AttributeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TblDesignCategoryLaborAssignment>(e =>
        {
            e.Property(a => a.DefaultHours).HasPrecision(10, 2);

            e.HasOne(a => a.Category)
                .WithMany(c => c.LaborAssignments)
                .HasForeignKey(a => a.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(a => a.LaborType)
                .WithMany()
                .HasForeignKey(a => a.LaborTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(a => new { a.CategoryId, a.LaborTypeId }).IsUnique();
        });

        modelBuilder.Entity<TblDesignCategoryAttribute>(e =>
        {
            e.HasOne(a => a.Category)
                .WithMany(c => c.CategoryAttributes)
                .HasForeignKey(a => a.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(a => a.Attribute)
                .WithMany(attr => attr.CategoryAttributes)
                .HasForeignKey(a => a.AttributeId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(a => new { a.CategoryId, a.AttributeId }).IsUnique();
        });

        modelBuilder.Entity<TblDesignCatalogItem>(e =>
        {
            e.Property(i => i.SnapshotJson).HasColumnType("nvarchar(max)");
            e.HasIndex(i => new { i.CustomerId, i.NetsuiteItemId });
        });

        modelBuilder.Entity<TblDesignCatalogItemLabor>(e =>
        {
            e.Property(l => l.Hours).HasPrecision(10, 2);

            e.HasOne(l => l.Item)
                .WithMany(i => i.LaborOverrides)
                .HasForeignKey(l => l.ItemId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(l => l.LaborType)
                .WithMany()
                .HasForeignKey(l => l.LaborTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(l => new { l.ItemId, l.LaborTypeId }).IsUnique();
        });

        modelBuilder.Entity<TblDesignCatalogItemAttribute>(e =>
        {
            e.HasOne(a => a.Item)
                .WithMany(i => i.AttributeValues)
                .HasForeignKey(a => a.ItemId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(a => a.Attribute)
                .WithMany()
                .HasForeignKey(a => a.AttributeId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(a => new { a.ItemId, a.AttributeId }).IsUnique();
        });

        // ── Phase I — Labor Columns ────────────────────────────────────────────
        modelBuilder.Entity<TblDesignLaborColumn>(e =>
        {
            e.HasIndex(c => new { c.CustomerId, c.SortOrder });
        });

        modelBuilder.Entity<TblDesignLaborTypeColumn>(e =>
        {
            e.HasOne(ltc => ltc.LaborType)
                .WithMany(lt => lt.ColumnAssignments)
                .HasForeignKey(ltc => ltc.LaborTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(ltc => ltc.Column)
                .WithMany(c => c.LaborTypeColumns)
                .HasForeignKey(ltc => ltc.ColumnId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(ltc => new { ltc.LaborTypeId, ltc.ColumnId }).IsUnique();
        });

        modelBuilder.Entity<TblDesignCategoryColumnAssignment>(e =>
        {
            e.Property(a => a.DefaultHours).HasPrecision(10, 2);

            e.HasOne(a => a.Category)
                .WithMany()
                .HasForeignKey(a => a.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(a => a.Column)
                .WithMany()
                .HasForeignKey(a => a.ColumnId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(a => new { a.CategoryId, a.ColumnId }).IsUnique();
        });

        modelBuilder.Entity<TblDesignCatalogItemColumn>(e =>
        {
            e.Property(c => c.Hours).HasPrecision(10, 2);

            e.HasOne(c => c.Item)
                .WithMany(i => i.ColumnOverrides)
                .HasForeignKey(c => c.ItemId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(c => c.Column)
                .WithMany()
                .HasForeignKey(c => c.ColumnId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(c => new { c.ItemId, c.ColumnId }).IsUnique();
        });

        modelBuilder.Entity<TblDesignWorksheetLaborCalculatorRow>(e =>
        {
            e.HasOne(r => r.Worksheet)
                .WithMany()
                .HasForeignKey(r => r.WorksheetId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(r => r.LaborType)
                .WithMany()
                .HasForeignKey(r => r.LaborTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.Column)
                .WithMany()
                .HasForeignKey(r => r.ColumnId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(r => new { r.WorksheetId, r.LaborTypeId, r.ColumnId }).IsUnique();
        });

        // ── Phase J — Misc & Travel ───────────────────────────────────────────
        modelBuilder.Entity<TblDesignMiscItem>(e =>
        {
            e.Property(i => i.DefaultCost).HasPrecision(18, 4);
            e.Property(i => i.DefaultSellPrice).HasPrecision(18, 4);
            e.HasIndex(i => new { i.CustomerId, i.IsTravel });
        });

        modelBuilder.Entity<TblDesignWorksheetMiscRow>(e =>
        {
            e.Property(r => r.Cost).HasPrecision(18, 4);
            e.Property(r => r.SellPrice).HasPrecision(18, 4);

            e.HasOne(r => r.Worksheet)
                .WithMany()
                .HasForeignKey(r => r.WorksheetId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(r => r.MiscItem)
                .WithMany()
                .HasForeignKey(r => r.MiscItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TblDesignWorksheetTravelRow>(e =>
        {
            e.Property(r => r.Cost).HasPrecision(18, 4);
            e.Property(r => r.SellPrice).HasPrecision(18, 4);

            e.HasOne(r => r.Worksheet)
                .WithMany()
                .HasForeignKey(r => r.WorksheetId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(r => r.MiscItem)
                .WithMany()
                .HasForeignKey(r => r.MiscItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Phase E — Designs & Worksheets ───────────────────────────────────
        modelBuilder.Entity<TblDesign>(e =>
        {
            e.Property(d => d.NetsuiteOpportunityData).HasColumnType("nvarchar(max)");
            e.HasIndex(d => new { d.CustomerId, d.NetsuiteOpportunityId });
        });

        modelBuilder.Entity<TblDesignWorksheet>(e =>
        {
            e.HasOne(w => w.Design)
                .WithMany(d => d.Worksheets)
                .HasForeignKey(w => w.DesignId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(w => w.DesignId);
        });

        modelBuilder.Entity<TblDesignWorksheetRoom>(e =>
        {
            e.HasOne(r => r.Worksheet)
                .WithMany(w => w.Rooms)
                .HasForeignKey(r => r.WorksheetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TblDesignWorksheetLineItem>(e =>
        {
            e.Property(l => l.DiscountPct).HasPrecision(10, 4);
            e.Property(l => l.MarginPct).HasPrecision(10, 4);

            e.HasOne(l => l.Worksheet)
                .WithMany(w => w.LineItems)
                .HasForeignKey(l => l.WorksheetId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(l => l.Room)
                .WithMany(r => r.LineItems)
                .HasForeignKey(l => l.RoomId)
                // ClientSetNull = NO ACTION in the DB; EF nulls RoomId in memory when Room is deleted.
                // SetNull would cause a SQL Server multi-cascade-path error (Worksheet→Room→LineItem
                // and Worksheet→LineItem are both CASCADE, which SQL Server forbids).
                .OnDelete(DeleteBehavior.ClientSetNull);

            e.HasOne(l => l.CatalogItem)
                .WithMany()
                .HasForeignKey(l => l.CatalogItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TblDesignWorksheetLineItemAttr>(e =>
        {
            e.HasOne(a => a.LineItem)
                .WithMany(l => l.AttributeValues)
                .HasForeignKey(a => a.LineItemId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(a => a.Attribute)
                .WithMany()
                .HasForeignKey(a => a.AttributeId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(a => new { a.LineItemId, a.AttributeId }).IsUnique();
        });

        modelBuilder.Entity<TblDesignWorksheetLineItemColumnHours>(e =>
        {
            e.Property(h => h.Hours).HasPrecision(10, 2);

            e.HasOne(h => h.Worksheet)
                .WithMany()
                .HasForeignKey(h => h.WorksheetId)
                .OnDelete(DeleteBehavior.Cascade);

            // Use NoAction on LineItem FK to avoid multi-cascade-path conflict
            // (Worksheet→LineItem is already CASCADE; adding another CASCADE from the same root fails in SQL Server).
            e.HasOne(h => h.LineItem)
                .WithMany()
                .HasForeignKey(h => h.LineItemId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasOne(h => h.Column)
                .WithMany()
                .HasForeignKey(h => h.ColumnId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(h => new { h.WorksheetId, h.LineItemId, h.ColumnId }).IsUnique();
        });

        modelBuilder.Entity<TblDesignWorksheetLaborRow>(e =>
        {
            e.Property(l => l.CalculatedHours).HasPrecision(10, 2);
            e.Property(l => l.OverrideHours).HasPrecision(10, 2);
            e.Property(l => l.SubCostPerHr).HasPrecision(18, 4);
            e.Property(l => l.SubPricePerHr).HasPrecision(18, 4);

            e.HasOne(l => l.Worksheet)
                .WithMany(w => w.LaborRows)
                .HasForeignKey(l => l.WorksheetId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(l => l.LaborType)
                .WithMany()
                .HasForeignKey(l => l.LaborTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(l => new { l.WorksheetId, l.LaborTypeId }).IsUnique();
        });

        modelBuilder.Entity<TblDesignWorksheetNonInventoryRow>(e =>
        {
            e.Property(r => r.RowType).HasConversion<string>().HasMaxLength(50);
            e.Property(r => r.Cost).HasPrecision(18, 4);
            e.Property(r => r.SellPrice).HasPrecision(18, 4);

            e.HasOne(r => r.Worksheet)
                .WithMany(w => w.NonInventoryRows)
                .HasForeignKey(r => r.WorksheetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TblDesignTravelConfig>(e =>
        {
            e.Property(t => t.AirfareCostPerPerson).HasPrecision(18, 4);
            e.Property(t => t.RentalCarCostPerDay).HasPrecision(18, 4);
            e.Property(t => t.HotelCostPerNight).HasPrecision(18, 4);
            e.Property(t => t.PerDiemPerPerson).HasPrecision(18, 4);
            e.Property(t => t.FlatTripCharge).HasPrecision(18, 4);

            e.HasOne(t => t.Worksheet)
                .WithOne(w => w.TravelConfig)
                .HasForeignKey<TblDesignTravelConfig>(t => t.WorksheetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Phase E — Requirements ─────────────────────────────────────────
        modelBuilder.Entity<TblDesignRequirementCategory>(e =>
        {
            e.HasOne(c => c.Design)
                .WithMany(d => d.RequirementCategories)
                .HasForeignKey(c => c.DesignId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(c => new { c.DesignId, c.Name }).IsUnique();
            e.HasIndex(c => new { c.DesignId, c.SortOrder });
        });

        modelBuilder.Entity<TblDesignRequirement>(e =>
        {
            e.Property(r => r.Priority).HasConversion<string>().HasMaxLength(50);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(50);

            e.HasOne(r => r.Design)
                .WithMany(d => d.Requirements)
                .HasForeignKey(r => r.DesignId)
                .OnDelete(DeleteBehavior.Cascade);

            // NoAction avoids a multi-cascade-path conflict (Design→Requirement is already CASCADE;
            // Design→Category→Requirement would be a second path, which SQL Server forbids).
            // Category deletion is guarded in the UI: a category with requirements cannot be removed.
            e.HasOne(r => r.Category)
                .WithMany(c => c.Requirements)
                .HasForeignKey(r => r.CategoryId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasIndex(r => new { r.DesignId, r.Status });
            e.HasIndex(r => new { r.CategoryId, r.SortOrder });
        });

        modelBuilder.Entity<TblDesignMcpToken>(e =>
        {
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => new { t.CustomerId, t.UserId });
        });

        modelBuilder.Entity<TblDesignRequirementComment>(e =>
        {
            e.HasOne(c => c.Requirement)
                .WithMany(r => r.Comments)
                .HasForeignKey(c => c.RequirementId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Phase M — Materials ───────────────────────────────────────────────
        modelBuilder.Entity<TblDesignMaterialsCategory>(e =>
        {
            e.HasIndex(c => new { c.CustomerId, c.SortOrder });
        });

        modelBuilder.Entity<TblDesignMaterialsItem>(e =>
        {
            e.Property(i => i.DefaultCost).HasPrecision(18, 4);
            e.Property(i => i.DefaultSellPrice).HasPrecision(18, 4);

            e.HasOne(i => i.Category)
                .WithMany(c => c.Items)
                .HasForeignKey(i => i.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(i => new { i.CustomerId, i.CategoryId, i.SortOrder });
        });

        modelBuilder.Entity<TblDesignWorksheetMaterialsRow>(e =>
        {
            e.Property(r => r.Cost).HasPrecision(18, 4);
            e.Property(r => r.SellPrice).HasPrecision(18, 4);

            e.HasOne(r => r.Worksheet)
                .WithMany()
                .HasForeignKey(r => r.WorksheetId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(r => r.Item)
                .WithMany()
                .HasForeignKey(r => r.ItemId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(r => new { r.WorksheetId, r.ItemId }).IsUnique();
        });

        modelBuilder.Entity<TblDesignWorksheetMaterialsCategoryConfig>(e =>
        {
            e.HasOne(c => c.Worksheet)
                .WithMany()
                .HasForeignKey(c => c.WorksheetId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(c => c.Category)
                .WithMany()
                .HasForeignKey(c => c.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(c => new { c.WorksheetId, c.CategoryId }).IsUnique();
        });

        // ── Phase K — Description rows ────────────────────────────────────────
        modelBuilder.Entity<TblDesignWorksheetSectionDescription>(e =>
        {
            e.HasOne(d => d.Worksheet)
                .WithMany()
                .HasForeignKey(d => d.WorksheetId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(d => new { d.WorksheetId, d.Section }).IsUnique();
        });

        modelBuilder.Entity<TblDesignWorksheetGroupDescription>(e =>
        {
            e.HasOne(d => d.Worksheet)
                .WithMany()
                .HasForeignKey(d => d.WorksheetId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict so deleting a CategoryGroup doesn't silently cascade into worksheet data.
            e.HasOne(d => d.CategoryGroup)
                .WithMany()
                .HasForeignKey(d => d.CategoryGroupId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(d => new { d.WorksheetId, d.CategoryGroupId }).IsUnique();
        });
    }
}
